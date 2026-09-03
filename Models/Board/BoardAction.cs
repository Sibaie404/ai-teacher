using System.Text.Json.Serialization;

namespace AiTeacher.Models.Board;

// Structured tool-call representation of a single board action.
// One BoardAction = one teaching beat = one SPOKEN_LINE.
// This replaces the free-text DRAW: strings with a validated, versioned schema.
//
// Schema version lives on BoardScript; individual actions are discriminated by `type`.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(WriteTextAction), "write_text")]
[JsonDerivedType(typeof(WriteMathAction), "write_math")]
[JsonDerivedType(typeof(DrawAxesAction), "draw_axes")]
[JsonDerivedType(typeof(DrawLineAction), "draw_line")]
[JsonDerivedType(typeof(DrawPointAction), "draw_point")]
[JsonDerivedType(typeof(DrawCircleAction), "draw_circle")]
[JsonDerivedType(typeof(DrawSquareAction), "draw_square")]
[JsonDerivedType(typeof(DrawTriangleAction), "draw_triangle")]
[JsonDerivedType(typeof(DrawArrowAction), "draw_arrow")]
[JsonDerivedType(typeof(DrawBracketAction), "draw_bracket")]
[JsonDerivedType(typeof(DrawBarChartAction), "draw_bar_chart")]
[JsonDerivedType(typeof(HighlightAction), "highlight")]
[JsonDerivedType(typeof(CircleTermAction), "circle_term")]
[JsonDerivedType(typeof(UnderlineAction), "underline")]
[JsonDerivedType(typeof(StrikeAction), "strike")]
[JsonDerivedType(typeof(FocusAction), "focus")]
[JsonDerivedType(typeof(EraseAction), "erase")]
[JsonDerivedType(typeof(ClearAction), "clear")]
[JsonDerivedType(typeof(NewPageAction), "new_page")]
[JsonDerivedType(typeof(PanToAction), "pan_to")]
public abstract record BoardAction
{
    // Stable identifier so later actions can reference this one (highlight, erase, etc).
    public string? Id { get; init; }

    // Optional named region: e.g. "top-left", "work-area", "sidebar", "answer-box".
    // Renderer maps region -> grid cell. If null, renderer picks based on flow.
    public Region? Region { get; init; }

    // Optional style overrides. null = use defaults for this action type.
    public BoardStyle? Style { get; init; }
}

public enum Region
{
    TopLeft,
    TopCenter,
    TopRight,
    Left,
    Center,
    Right,
    BottomLeft,
    BottomCenter,
    BottomRight,
    WorkArea,
    Sidebar,
    AnswerBox,
    Title
}

public sealed record BoardStyle
{
    // Named color: "ink" (default), "chalk", "highlight", "accent", "danger", "success".
    // Concrete hex is resolved in the renderer so it can switch palettes.
    public string? Color { get; init; }

    // Size label: "xs", "sm", "md" (default), "lg", "xl", "title".
    public string? Size { get; init; }

    // Emphasis flag: if true the renderer may bold, grow, or pulse the content.
    public bool? Emphasis { get; init; }
}

// ---------- Text / math ----------

public sealed record WriteTextAction : BoardAction
{
    public required string Text { get; init; }
}

public sealed record WriteMathAction : BoardAction
{
    // KaTeX-compatible source, without surrounding $...$ delimiters.
    public required string Latex { get; init; }
}

// ---------- Geometry primitives ----------

public sealed record AxesRange(double XMin, double XMax, double YMin, double YMax);

public sealed record DrawAxesAction : BoardAction
{
    public required AxesRange Range { get; init; }
    public string? XLabel { get; init; }
    public string? YLabel { get; init; }
}

public sealed record DrawLineAction : BoardAction
{
    // Line equation expressed as the string form we already parse today
    // (e.g. "y=2x+1", "x=3", "2x+y=11"). Renderer parses it.
    public required string Equation { get; init; }

    // Id of a DrawAxesAction on the same page. If null the renderer uses the most recent axes.
    public string? AxesId { get; init; }

    public string? Label { get; init; }
}

public sealed record Point2D(double X, double Y);

public sealed record DrawPointAction : BoardAction
{
    public required Point2D At { get; init; }
    public string? Label { get; init; }
    public string? AxesId { get; init; }
}

public sealed record DrawCircleAction : BoardAction
{
    public required Point2D Center { get; init; }
    public required double Radius { get; init; }
    public string? Label { get; init; }
    public string? AxesId { get; init; }
}

public sealed record DrawSquareAction : BoardAction
{
    public required Point2D Center { get; init; }
    public required double Size { get; init; }
    public double AngleDegrees { get; init; } = 0;
    public string? Label { get; init; }
}

// Triangle is flexible: either specify leg lengths (right triangle)
// or provide three explicit vertices for a general triangle.
public sealed record DrawTriangleAction : BoardAction
{
    // Right-triangle shorthand.
    public double? BaseLength { get; init; }
    public double? HeightLength { get; init; }
    public double? AcuteAngleDegrees { get; init; }

    // General triangle override.
    public Point2D? V1 { get; init; }
    public Point2D? V2 { get; init; }
    public Point2D? V3 { get; init; }

    public TriangleLabels? Labels { get; init; }
}

public sealed record TriangleLabels(
    string? BaseLabel,
    string? HeightLabel,
    string? HypotenuseLabel,
    string? AngleLabel);

public sealed record DrawArrowAction : BoardAction
{
    public required Point2D From { get; init; }
    public required Point2D To { get; init; }
    public string? Label { get; init; }

    // "straight" (default) or "curve" for curved reaction-mechanism arrows.
    public string? Shape { get; init; }
}

public sealed record DrawBracketAction : BoardAction
{
    public required Point2D From { get; init; }
    public required Point2D To { get; init; }
    public string? Label { get; init; }

    // "curly" (default), "square", "round".
    public string? BracketStyle { get; init; }
}

public sealed record BarValue(string Label, double Value);

public sealed record DrawBarChartAction : BoardAction
{
    public required List<BarValue> Bars { get; init; }
    public string? Title { get; init; }
}

// ---------- Emphasis ----------

public sealed record HighlightAction : BoardAction
{
    // Id of an earlier action to highlight, OR arbitrary text to find on the board.
    public string? TargetId { get; init; }
    public string? TargetText { get; init; }
}

public sealed record CircleTermAction : BoardAction
{
    public string? TargetId { get; init; }
    public string? TargetText { get; init; }
}

public sealed record UnderlineAction : BoardAction
{
    public string? TargetId { get; init; }
    public string? TargetText { get; init; }
}

public sealed record StrikeAction : BoardAction
{
    public string? TargetId { get; init; }
    public string? TargetText { get; init; }
}

// ---------- Camera / flow ----------

public sealed record FocusAction : BoardAction
{
    public Point2D? At { get; init; }
    public string? TargetId { get; init; }
}

public sealed record EraseAction : BoardAction
{
    // Specific action to erase by id.
    public string? TargetId { get; init; }
}

// Wipe the current page entirely.
public sealed record ClearAction : BoardAction
{
}

// Start a fresh board page (preserves history for scrollback).
public sealed record NewPageAction : BoardAction
{
    public string? PageTitle { get; init; }
}

public sealed record PanToAction : BoardAction
{
    public Region? ToRegion { get; init; }
    public string? ToId { get; init; }
}
