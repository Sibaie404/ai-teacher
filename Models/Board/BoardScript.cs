namespace AiTeacher.Models.Board;

// A BoardScript is the structured replacement for the legacy
// (BoardLines[], BoardTimings[], BoardTimestampSeconds[]) triple.
//
// Invariant: Actions.Count == SpokenLines.Count == Timings.Count.
// Item i in each list is the same teaching beat.
public sealed class BoardScript
{
    public int SchemaVersion { get; set; } = 1;

    public List<BoardAction> Actions { get; set; } = new();

    // The words the narrator speaks while `Actions[i]` is executed.
    public List<string> SpokenLines { get; set; } = new();

    // Planned start time for action i in seconds from the start of narration.
    // May be refined later by word-level audio alignment.
    public List<double> Timings { get; set; } = new();

    // Real start time from word-timing alignment. Empty until resolved.
    public List<double> TimestampSeconds { get; set; } = new();

    public bool HasConsistentShape()
    {
        if (Actions.Count != SpokenLines.Count) return false;
        if (Actions.Count != Timings.Count) return false;
        if (TimestampSeconds.Count > 0 && TimestampSeconds.Count != Actions.Count) return false;
        return true;
    }
}
