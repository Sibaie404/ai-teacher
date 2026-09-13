using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AiTeacher.Services.Ai;

public sealed class LessonDirector : ILessonDirector
{
    private const int MinBeatCount = 4;
    private const int MaxBeatCount = 20;

    private static readonly string[] KnownTopicKinds = { "math", "verbal", "general" };
    private static readonly string[] KnownVisualKinds =
    {
        "none", "coordinate-graph", "triangle", "trig-triangle", "circle", "bar-chart", "other"
    };

    private const string SystemPrompt =
        "You are the lesson director for an SAT tutoring app. Before a lesson is written, you design its structure: " +
        "how the topic should be classified, which visual (if any) fits it best, and the ordered teaching beats. " +
        "Return ONLY a single JSON object. No markdown, no code fences, no commentary.";

    private readonly IAiChatClient _ai;
    private readonly AiOptions _options;
    private readonly ILogger<LessonDirector> _logger;

    public LessonDirector(IAiChatClient ai, IOptions<AiOptions> options, ILogger<LessonDirector> logger)
    {
        _ai = ai;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<LessonPlan?> PlanLessonAsync(string topic, LessonLength length, CancellationToken ct)
    {
        topic = (topic ?? "").Trim();
        if (topic.Length == 0 || !_options.UseOpenAi() || !_options.Director.Enabled)
            return null;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var text = await _ai.CompleteAsync(SystemPrompt, BuildUserPrompt(topic, length, retry: attempt > 0), ct);
                var plan = ParsePlan(topic, text);
                if (plan is not null)
                    return plan;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lesson director planning attempt {Attempt} failed for topic \"{Topic}\".", attempt + 1, topic);
            }
        }

        _logger.LogWarning("Lesson director could not produce a plan for topic \"{Topic}\"; generating without one.", topic);
        return null;
    }

    private static string BuildUserPrompt(string topic, LessonLength length, bool retry)
    {
        var isShort = length == LessonLength.Short;
        var durationLabel = isShort ? "~3-5 minute" : "~12-18 minute";
        var beatRange = isShort ? "6 to 9" : "10 to 16";
        var exampleCount = isShort ? "at least 2 worked examples" : "at least 6 worked examples";

        return
            $"Design the lesson plan for a {durationLabel} SAT lesson on this topic:\n\n{topic}\n\n" +
            "Return ONLY a JSON object with this exact shape:\n" +
            "{\n" +
            "  \"topicKind\": \"math\" | \"verbal\" | \"general\",\n" +
            "  \"visualKind\": \"none\" | \"coordinate-graph\" | \"triangle\" | \"trig-triangle\" | \"circle\" | \"bar-chart\" | \"other\",\n" +
            "  \"styleNotes\": \"one or two short sentences of guidance for the lesson writer\",\n" +
            "  \"beats\": [\n" +
            "    {\n" +
            "      \"title\": \"short section name\",\n" +
            "      \"goal\": \"what the student should get from this beat\",\n" +
            "      \"board\": \"what belongs on the whiteboard during this beat\",\n" +
            "      \"draw\": \"a DRAW command hint if this beat teaches with a visual, else null\"\n" +
            "    }\n" +
            "  ]\n" +
            "}\n\n" +
            "Rules:\n" +
            $"- Use {beatRange} beats covering: hook, concept intuition, {exampleCount} (one beat each), practice/quiz, common traps, recap.\n" +
            "- \"topicKind\" is \"verbal\" only for reading/writing/grammar/vocabulary topics with no meaningful diagram.\n" +
            "- \"visualKind\" is the single most helpful visual for this topic. Use \"none\" for verbal topics.\n" +
            "- Place the visual-intuition beat before example-only solving, not after.\n" +
            "- Keep every string short: titles under 8 words, goals and board hints under 20 words.\n" +
            "- Supported DRAW hint commands: axes, line, point, circle, square, bar, triangle, focus, clear " +
            "(e.g. \"DRAW: axes x=-5..5 y=-5..5\", \"DRAW: triangle right; legs a,b; hypotenuse c\").\n" +
            (retry ? "- The prior response was not valid JSON. Return ONLY the JSON object this time.\n" : "");
    }

    private LessonPlan? ParsePlan(string topic, string text)
    {
        var json = ExtractJsonObject(text);
        if (json is null)
            return null;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        var topicKind = NormalizeChoice(ReadString(root, "topicKind"), KnownTopicKinds, "general");
        var visualKind = NormalizeChoice(ReadString(root, "visualKind"), KnownVisualKinds,
            fallback: string.IsNullOrWhiteSpace(ReadString(root, "visualKind")) ? "none" : "other");
        var styleNotes = Clip(ReadString(root, "styleNotes"), 400);

        if (!root.TryGetProperty("beats", out var beatsElement) || beatsElement.ValueKind != JsonValueKind.Array)
            return null;

        var beats = new List<LessonBeat>();
        foreach (var beatElement in beatsElement.EnumerateArray())
        {
            if (beats.Count >= MaxBeatCount)
                break;
            if (beatElement.ValueKind != JsonValueKind.Object)
                continue;

            var title = Clip(ReadString(beatElement, "title"), 80);
            var goal = Clip(ReadString(beatElement, "goal"), 200);
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(goal))
                continue;

            beats.Add(new LessonBeat(
                string.IsNullOrWhiteSpace(title) ? $"Beat {beats.Count + 1}" : title!,
                goal ?? "",
                Clip(ReadString(beatElement, "board"), 200),
                Clip(ReadString(beatElement, "draw"), 220)));
        }

        if (beats.Count < MinBeatCount)
        {
            _logger.LogWarning("Lesson director plan for topic \"{Topic}\" had only {Count} usable beats; discarding.", topic, beats.Count);
            return null;
        }

        return new LessonPlan(topic, topicKind, visualKind, beats, styleNotes);
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }

    private static string NormalizeChoice(string? value, string[] known, string fallback)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        return known.Contains(normalized) ? normalized : fallback;
    }

    private static string? Clip(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? ExtractJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var first = text.IndexOf('{');
        var last = text.LastIndexOf('}');
        if (first < 0 || last <= first)
            return null;

        return text.Substring(first, last - first + 1);
    }
}
