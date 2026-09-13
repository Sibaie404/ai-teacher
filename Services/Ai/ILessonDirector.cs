namespace AiTeacher.Services.Ai;

public interface ILessonDirector
{
    /// <summary>
    /// Plans the structure of a lesson before it is written.
    /// Returns null when planning is disabled, unavailable, or fails,
    /// in which case generation proceeds without a plan.
    /// </summary>
    Task<LessonPlan?> PlanLessonAsync(string topic, LessonLength length, CancellationToken ct);
}
