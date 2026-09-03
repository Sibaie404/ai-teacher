using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AiTeacher.Pages.BoardDebug;

public class PlayModel : PageModel
{
    private readonly IWebHostEnvironment _env;

    public PlayModel(IWebHostEnvironment env)
    {
        _env = env;
    }

    public string Slug { get; private set; } = "";
    public string RawJson { get; private set; } = "{}";
    public string Title { get; private set; } = "Golden lesson";
    public bool LessonMissing { get; private set; }

    public IActionResult OnGet(string slug)
    {
        Slug = slug ?? "";
        if (string.IsNullOrWhiteSpace(Slug) || Slug.Contains('/') || Slug.Contains('\\') || Slug.Contains(".."))
        {
            LessonMissing = true;
            return Page();
        }
        var path = Path.Combine(_env.WebRootPath, "golden-lessons", Slug + ".json");
        if (!System.IO.File.Exists(path))
        {
            LessonMissing = true;
            return Page();
        }
        RawJson = System.IO.File.ReadAllText(path);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(RawJson);
            if (doc.RootElement.TryGetProperty("title", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.String)
                Title = t.GetString() ?? Title;
        }
        catch { /* leave defaults */ }
        return Page();
    }
}
