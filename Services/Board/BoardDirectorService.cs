using System.Text;
using System.Text.Json;
using AiTeacher.Models.Board;
using AiTeacher.Services.Ai;

namespace AiTeacher.Services.Board;

// The Director agent: turns a topic (and optionally a fixed narration) into a
// structured BoardScript of BoardAction tool calls. Prompted with hand-authored
// golden lessons as few-shot examples, validated against the schema, and given
// one repair round-trip when the first response fails validation.
public sealed class BoardDirectorService : IBoardDirectorService
{
    private const int MaxAttempts = 2;

    private static readonly string[] FewShotSlugs =
    {
        "01-pythagorean-theorem",
        "02-slope-intercept"
    };

    private readonly IAiChatClient _chat;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<BoardDirectorService> _logger;

    public BoardDirectorService(IAiChatClient chat, IWebHostEnvironment env, ILogger<BoardDirectorService> logger)
    {
        _chat = chat;
        _env = env;
        _logger = logger;
    }

    public async Task<BoardDirectorResult> DirectAsync(string topic, IReadOnlyList<string>? spokenLines, CancellationToken ct)
    {
        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(topic, spokenLines);

        var raw = "";
        List<string> lastErrors = new();

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var prompt = attempt == 1
                ? userPrompt
                : BuildRepairPrompt(userPrompt, raw, lastErrors);

            raw = await _chat.CompleteAsync(systemPrompt, prompt, ct);

            var result = ParseAndValidate(raw, spokenLines);
            if (result.Success)
                return new BoardDirectorResult { Script = result.Script, RawResponse = raw, Attempts = attempt };

            lastErrors = result.Errors;
            _logger.LogWarning(
                "Board director attempt {Attempt} failed validation: {Errors}",
                attempt, string.Join(" | ", lastErrors));
        }

        return new BoardDirectorResult { RawResponse = raw, Errors = lastErrors, Attempts = MaxAttempts };
    }

    public BoardDirectorResult ParseAndValidate(string rawModelOutput, IReadOnlyList<string>? expectedSpokenLines)
    {
        var errors = new List<string>();
        var json = ExtractJson(rawModelOutput);
        if (json is null)
        {
            errors.Add("No JSON object found in the response.");
            return new BoardDirectorResult { RawResponse = rawModelOutput, Errors = errors };
        }

        BoardScript? script;
        try
        {
            script = JsonSerializer.Deserialize<BoardScript>(json, BoardActionJson.Options);
        }
        catch (JsonException ex)
        {
            errors.Add($"JSON did not match the BoardScript schema: {ex.Message}");
            return new BoardDirectorResult { RawResponse = rawModelOutput, Errors = errors };
        }

        if (script is null)
        {
            errors.Add("JSON deserialized to null.");
            return new BoardDirectorResult { RawResponse = rawModelOutput, Errors = errors };
        }

        Validate(script, expectedSpokenLines, errors);

        return new BoardDirectorResult
        {
            Script = errors.Count == 0 ? script : null,
            RawResponse = rawModelOutput,
            Errors = errors
        };
    }

    private static void Validate(BoardScript script, IReadOnlyList<string>? expectedSpokenLines, List<string> errors)
    {
        if (script.Actions.Count == 0)
            errors.Add("The script has no actions.");

        if (script.Actions.Count != script.SpokenLines.Count)
            errors.Add($"actions ({script.Actions.Count}) and spokenLines ({script.SpokenLines.Count}) must have the same length.");

        if (script.Actions.Count != script.Timings.Count)
            errors.Add($"actions ({script.Actions.Count}) and timings ({script.Timings.Count}) must have the same length.");

        if (expectedSpokenLines is not null && script.SpokenLines.Count != expectedSpokenLines.Count)
            errors.Add($"spokenLines must match the provided narration exactly: expected {expectedSpokenLines.Count} lines, got {script.SpokenLines.Count}.");

        for (var i = 1; i < script.Timings.Count; i++)
        {
            if (script.Timings[i] < script.Timings[i - 1])
            {
                errors.Add($"timings must be non-decreasing (timings[{i}] = {script.Timings[i]} < timings[{i - 1}] = {script.Timings[i - 1]}).");
                break;
            }
        }

        // Every emphasis/erase/focus target must reference an action id defined earlier.
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < script.Actions.Count; i++)
        {
            var action = script.Actions[i];
            var target = action switch
            {
                HighlightAction a => a.TargetId,
                CircleTermAction a => a.TargetId,
                UnderlineAction a => a.TargetId,
                StrikeAction a => a.TargetId,
                FocusAction a => a.TargetId,
                EraseAction a => a.TargetId,
                _ => null
            };
            if (target is not null && !seenIds.Contains(target))
                errors.Add($"actions[{i}] ({ActionType(action)}) targets id \"{target}\" which no earlier action defines.");

            if (action is DrawLineAction line && line.AxesId is not null && !seenIds.Contains(line.AxesId))
                errors.Add($"actions[{i}] (draw_line) references axesId \"{line.AxesId}\" which no earlier action defines.");
            if (action is DrawPointAction point && point.AxesId is not null && !seenIds.Contains(point.AxesId))
                errors.Add($"actions[{i}] (draw_point) references axesId \"{point.AxesId}\" which no earlier action defines.");
            if (action is DrawCircleAction circle && circle.AxesId is not null && !seenIds.Contains(circle.AxesId))
                errors.Add($"actions[{i}] (draw_circle) references axesId \"{circle.AxesId}\" which no earlier action defines.");

            if (!string.IsNullOrWhiteSpace(action.Id))
                seenIds.Add(action.Id!);
        }
    }

    private static string ActionType(BoardAction action)
    {
        var name = action.GetType().Name;
        if (name.EndsWith("Action", StringComparison.Ordinal))
            name = name[..^6];
        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])) sb.Append('_');
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.ToString();
    }

    // Pull the outermost JSON object out of a model response that may wrap it
    // in prose or a ```json fence.
    private static string? ExtractJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim();

        var fenceStart = text.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            var afterFence = text.IndexOf('\n', fenceStart);
            var fenceEnd = afterFence < 0 ? -1 : text.IndexOf("```", afterFence, StringComparison.Ordinal);
            if (afterFence >= 0 && fenceEnd > afterFence)
                text = text.Substring(afterFence + 1, fenceEnd - afterFence - 1).Trim();
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        return text.Substring(start, end - start + 1);
    }

    private string BuildSystemPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are the Board Director for an AI tutor that teaches students in grades 9-12 (SAT/ACT level).");
        sb.AppendLine("Your job: choreograph a whiteboard the way a great teacher uses one — like popular whiteboard-style tutoring videos.");
        sb.AppendLine("You output ONLY a JSON object (no prose, no markdown fence) describing the board script.");
        sb.AppendLine();
        sb.AppendLine("## Output shape");
        sb.AppendLine("""
{
  "schemaVersion": 1,
  "spokenLines": ["one narration beat per action"],
  "timings": [0.0, 3.5],
  "actions": [ { "type": "...", ... } ]
}
""");
        sb.AppendLine("Hard invariant: actions, spokenLines, and timings MUST have the same length. Item i of each is one teaching beat: the narrator says spokenLines[i] while actions[i] appears on the board at timings[i] seconds.");
        sb.AppendLine();
        sb.AppendLine("## Action types");
        sb.AppendLine("Every action may carry: \"id\" (string, define it so later actions can reference it), \"region\", \"style\" { \"color\", \"size\", \"emphasis\" }.");
        sb.AppendLine("- write_text: { \"text\" } — short board notes, labels, key terms. Never full sentences.");
        sb.AppendLine("- write_math: { \"latex\" } — equations and worked steps, plain unicode math (x², √, ±, ½, θ).");
        sb.AppendLine("- draw_axes: { \"range\": { \"xMin\", \"xMax\", \"yMin\", \"yMax\" }, \"xLabel\"?, \"yLabel\"? }");
        sb.AppendLine("- draw_line: { \"equation\": \"y=2x+1\" | \"x=3\", \"axesId\"?, \"label\"? }");
        sb.AppendLine("- draw_point: { \"at\": { \"x\", \"y\" }, \"label\"?, \"axesId\"? }");
        sb.AppendLine("- draw_circle: { \"center\": { \"x\", \"y\" }, \"radius\", \"label\"?, \"axesId\"? }");
        sb.AppendLine("- draw_square: { \"center\": { \"x\", \"y\" }, \"size\", \"angleDegrees\"?, \"label\"? }");
        sb.AppendLine("- draw_triangle: { \"baseLength\"?, \"heightLength\"?, \"acuteAngleDegrees\"?, \"labels\": { \"baseLabel\", \"heightLabel\", \"hypotenuseLabel\", \"angleLabel\" }? } (right triangle) or { \"v1\", \"v2\", \"v3\" } (general)");
        sb.AppendLine("- draw_arrow: { \"from\": { \"x\", \"y\" }, \"to\": { \"x\", \"y\" }, \"label\"?, \"shape\"?: \"straight\"|\"curve\" } — x/y in 0..1 are fractions of the region.");
        sb.AppendLine("- draw_bracket: { \"from\", \"to\", \"label\"?, \"bracketStyle\"?: \"curly\"|\"square\" }");
        sb.AppendLine("- draw_bar_chart: { \"bars\": [ { \"label\", \"value\" } ], \"title\"? }");
        sb.AppendLine("- highlight / circle_term / underline / strike: { \"targetId\" } — emphasize an earlier action by its id. Use these the way a teacher circles or underlines on a real board.");
        sb.AppendLine("- focus: { \"targetId\" } — draw attention to an earlier item.");
        sb.AppendLine("- erase: { \"targetId\" }, clear: {} (wipe page), new_page: { \"pageTitle\"? } (fresh board, like wiping and continuing).");
        sb.AppendLine();
        sb.AppendLine("## Regions");
        sb.AppendLine("Title, TopLeft, TopCenter, TopRight, Left, Center, Right, BottomLeft, BottomCenter, BottomRight, WorkArea, Sidebar, AnswerBox.");
        sb.AppendLine("Layout habits: lesson title in Title. Worked math steps flow down Left (or Center). Diagrams go in WorkArea. The final answer goes in AnswerBox with color \"success\" and emphasis. Side notes go Right.");
        sb.AppendLine();
        sb.AppendLine("## Style values");
        sb.AppendLine("color: ink (default), accent, danger, success, muted, highlight. size: xs, sm, md (default), lg, xl, title.");
        sb.AppendLine();
        sb.AppendLine("## Teaching craft rules");
        sb.AppendLine("1. Board lines are notes, not prose: \"Divide by 3\", \"x = 4\", \"m = slope\" — never \"We divide both sides by 3\".");
        sb.AppendLine("2. Every material step of the work appears on the board — setup, substitution, simplification, check.");
        sb.AppendLine("3. Draw the diagram early when the topic is geometric; reference it while working.");
        sb.AppendLine("4. Circle or highlight the final answer. Emphasize key formulas with color accent.");
        sb.AppendLine("5. Timings: first beat at 0.0, then realistic gaps (2.5-6s per beat, longer for complex formulas).");
        sb.AppendLine("6. 6-12 beats for a focused mini-lesson.");
        sb.AppendLine("7. Spoken lines are warm, plain teacher speech. Short sentences. Numbers spelled the way a person says them.");
        sb.AppendLine();
        sb.AppendLine("## Examples of ideal output");
        foreach (var slug in FewShotSlugs)
        {
            var example = LoadGoldenLesson(slug);
            if (example is null) continue;
            sb.AppendLine();
            sb.AppendLine($"### Example: {slug}");
            sb.AppendLine(example);
        }
        return sb.ToString();
    }

    private static string BuildUserPrompt(string topic, IReadOnlyList<string>? spokenLines)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Topic: {topic}");
        if (spokenLines is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("The narration is already written. Use these spoken lines EXACTLY as given, in order, one action per line — do not add, remove, reword, or reorder them:");
            for (var i = 0; i < spokenLines.Count; i++)
                sb.AppendLine($"{i + 1}. {spokenLines[i]}");
            sb.AppendLine();
            sb.AppendLine("Produce the JSON board script whose spokenLines array is exactly this narration and whose actions choreograph the board for each line.");
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("Write the mini-lesson yourself: narration, timings, and board actions. Produce only the JSON board script.");
        }
        return sb.ToString();
    }

    private static string BuildRepairPrompt(string originalPrompt, string previousResponse, List<string> errors)
    {
        var sb = new StringBuilder();
        sb.AppendLine(originalPrompt);
        sb.AppendLine();
        sb.AppendLine("Your previous response failed validation with these errors:");
        foreach (var error in errors)
            sb.AppendLine($"- {error}");
        sb.AppendLine();
        sb.AppendLine("Previous response:");
        sb.AppendLine(previousResponse);
        sb.AppendLine();
        sb.AppendLine("Return a corrected JSON board script that fixes every error. Output only the JSON object.");
        return sb.ToString();
    }

    private string? LoadGoldenLesson(string slug)
    {
        try
        {
            var path = Path.Combine(_env.WebRootPath, "golden-lessons", slug + ".json");
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            // Reduce the fixture to exactly the output shape we ask the model for,
            // so the examples match the contract (fixtures carry extra metadata).
            var example = new Dictionary<string, object?>
            {
                ["schemaVersion"] = 1,
                ["spokenLines"] = root.GetProperty("spokenLines"),
                ["timings"] = root.GetProperty("timings"),
                ["actions"] = root.GetProperty("actions")
            };
            return JsonSerializer.Serialize(example, BoardActionJson.Options);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load golden lesson {Slug} for few-shot prompt", slug);
            return null;
        }
    }
}
