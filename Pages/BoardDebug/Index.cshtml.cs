using System.Text.Json;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AiTeacher.Pages.BoardDebug;

public class IndexModel : PageModel
{
    private readonly IWebHostEnvironment _env;

    public IndexModel(IWebHostEnvironment env)
    {
        _env = env;
    }

    public List<GoldenLessonSummary> Lessons { get; private set; } = new();

    public void OnGet()
    {
        var dir = Path.Combine(_env.WebRootPath, "golden-lessons");
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f))
        {
            try
            {
                using var stream = System.IO.File.OpenRead(file);
                using var doc = JsonDocument.Parse(stream);
                var root = doc.RootElement;
                Lessons.Add(new GoldenLessonSummary
                {
                    Slug = Path.GetFileNameWithoutExtension(file),
                    Title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "Untitled" : "Untitled",
                    Subject = root.TryGetProperty("subject", out var s) ? s.GetString() ?? "" : "",
                    Grade = root.TryGetProperty("grade", out var g) ? g.GetString() ?? "" : "",
                    ActionCount = root.TryGetProperty("actions", out var a) && a.ValueKind == JsonValueKind.Array ? a.GetArrayLength() : 0
                });
            }
            catch
            {
                // Skip malformed fixture files - the debug page shouldn't crash for one bad lesson.
            }
        }
    }
}

public sealed class GoldenLessonSummary
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Grade { get; set; } = "";
    public int ActionCount { get; set; }
}
