using AiTeacher.Models.Board;

namespace AiTeacher.Services.Ai;

public sealed record AiVideoPack(
    string Narration,
    List<string> BoardLines,
    List<double> BoardTimings,
    List<double>? BoardTimestampSeconds = null,
    List<string>? NarrationSegments = null,
    List<BoardAction>? BoardActions = null);
