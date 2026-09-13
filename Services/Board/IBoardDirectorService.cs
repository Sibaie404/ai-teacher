using AiTeacher.Models.Board;

namespace AiTeacher.Services.Board;

public interface IBoardDirectorService
{
    // Generate a full board script for a topic. When spokenLines is provided the
    // director must produce exactly one action per spoken line (the narration is
    // fixed, e.g. it came from the lesson-generation agent); when null the
    // director writes the spoken lines too.
    Task<BoardDirectorResult> DirectAsync(string topic, IReadOnlyList<string>? spokenLines, CancellationToken ct);

    // Run only the parse + validate + repair stage on raw model output.
    // Used by the debug page to test outputs pasted from any model.
    BoardDirectorResult ParseAndValidate(string rawModelOutput, IReadOnlyList<string>? expectedSpokenLines);
}

public sealed class BoardDirectorResult
{
    public BoardScript? Script { get; init; }
    public string RawResponse { get; init; } = "";
    public List<string> Errors { get; init; } = new();
    public int Attempts { get; init; } = 1;
    public bool Success => Script is not null && Errors.Count == 0;
}
