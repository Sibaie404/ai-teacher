namespace AiTeacher.Services.Ai;

public sealed record LessonBeat(
    string Title,
    string Goal,
    string? BoardHint,
    string? DrawHint);

public sealed record LessonPlan(
    string Topic,
    string TopicKind,
    string VisualKind,
    List<LessonBeat> Beats,
    string? StyleNotes)
{
    public bool IsVerbalTopic =>
        string.Equals(TopicKind, "verbal", StringComparison.OrdinalIgnoreCase);

    public bool WantsVisual =>
        !IsVerbalTopic && !string.Equals(VisualKind, "none", StringComparison.OrdinalIgnoreCase);

    public string DescribeVisual() =>
        string.Equals(VisualKind, "other", StringComparison.OrdinalIgnoreCase)
            ? "simple, topic-matching"
            : VisualKind.Replace('-', ' ');
}
