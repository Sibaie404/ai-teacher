using System.Text.Json;
using AiTeacher.Services.Board;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AiTeacher.Pages.BoardDebug;

public class DirectModel : PageModel
{
    private readonly IBoardDirectorService _director;

    public DirectModel(IBoardDirectorService director)
    {
        _director = director;
    }

    [BindProperty]
    public string Topic { get; set; } = "";

    // Optional fixed narration, one spoken line per row.
    [BindProperty]
    public string SpokenLinesText { get; set; } = "";

    // Alternative mode: raw model output pasted in, run through parse+validate only.
    [BindProperty]
    public string PastedOutput { get; set; } = "";

    public BoardDirectorResult? Result { get; private set; }
    public string? ScriptJson { get; private set; }
    public bool Ran { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostRunAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Topic))
        {
            ModelState.AddModelError(nameof(Topic), "Enter a topic.");
            return Page();
        }
        var spokenLines = SplitLines(SpokenLinesText);
        Result = await _director.DirectAsync(Topic.Trim(), spokenLines.Count > 0 ? spokenLines : null, ct);
        Finish();
        return Page();
    }

    public IActionResult OnPostParse()
    {
        if (string.IsNullOrWhiteSpace(PastedOutput))
        {
            ModelState.AddModelError(nameof(PastedOutput), "Paste a model response first.");
            return Page();
        }
        var spokenLines = SplitLines(SpokenLinesText);
        Result = _director.ParseAndValidate(PastedOutput, spokenLines.Count > 0 ? spokenLines : null);
        Finish();
        return Page();
    }

    private void Finish()
    {
        Ran = true;
        if (Result?.Script is not null)
            ScriptJson = JsonSerializer.Serialize(Result.Script, BoardActionJson.Options);
    }

    private static List<string> SplitLines(string text) =>
        (text ?? "")
            .Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
}
