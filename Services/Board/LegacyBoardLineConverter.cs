using System.Globalization;
using System.Text.RegularExpressions;
using AiTeacher.Models.Board;

namespace AiTeacher.Services.Board;

// Converts legacy free-text board lines (mixed "DRAW: ..." commands and plain text)
// into structured BoardAction values. This lets the existing generation pipeline
// keep emitting the old format while the rendering path reads the new one.
//
// The converter is deliberately lenient - unrecognized DRAW commands become
// WriteTextAction (so the board still shows something) rather than throwing.
public static class LegacyBoardLineConverter
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public static BoardAction Convert(string line, int index)
    {
        var raw = (line ?? string.Empty).Trim();
        if (raw.Length == 0)
            return new WriteTextAction { Id = MakeId("text", index), Text = "" };

        // Not a DRAW command -> plain board text.
        if (!raw.StartsWith("DRAW:", StringComparison.OrdinalIgnoreCase))
            return new WriteTextAction { Id = MakeId("text", index), Text = raw };

        var body = raw.Substring(5).Trim();
        var normalized = NormalizeDrawText(body);
        var lower = normalized.ToLowerInvariant();

        if (lower.StartsWith("clear"))
            return new ClearAction { Id = MakeId("clear", index) };

        if (lower.StartsWith("new page") || lower.StartsWith("newpage") || lower.StartsWith("page"))
            return new NewPageAction { Id = MakeId("page", index) };

        if (lower.StartsWith("focus"))
            return new FocusAction { Id = MakeId("focus", index), TargetId = ExtractAfter(normalized, "focus") };

        if (lower.StartsWith("axes"))
            return ParseAxes(normalized, index);

        if (lower.StartsWith("line "))
            return ParseLine(normalized.Substring(5).Trim(), index);

        if (lower.StartsWith("point"))
            return ParsePoint(normalized.Substring(5).Trim(), index);

        if (lower.StartsWith("circle"))
            return ParseCircle(normalized.Substring(6).Trim(), index);

        if (lower.StartsWith("square"))
            return ParseSquare(normalized.Substring(6).Trim(), index);

        if (lower.StartsWith("triangle"))
            return ParseTriangle(normalized.Substring(8).Trim(), index);

        if (lower.StartsWith("arrow"))
            return ParseArrow(normalized.Substring(5).Trim(), index);

        if (lower.StartsWith("bar"))
            return ParseBar(normalized.Substring(3).Trim(), index);

        // Unknown DRAW: fall through to text so nothing is silently lost.
        return new WriteTextAction { Id = MakeId("text", index), Text = raw };
    }

    public static List<BoardAction> ConvertAll(IEnumerable<string> lines)
    {
        var list = new List<BoardAction>();
        var i = 0;
        foreach (var line in lines)
            list.Add(Convert(line, i++));
        return list;
    }

    private static string MakeId(string prefix, int index) =>
        $"{prefix}-{index:D3}";

    private static string NormalizeDrawText(string raw)
    {
        var s = raw.Replace(' ', ' ')
                   .Replace('−', '-')
                   .Replace('–', '-')
                   .Replace('—', '-')
                   .Replace('×', '*');
        return WhitespaceRegex.Replace(s, " ").Trim();
    }

    private static string? ExtractAfter(string text, string keyword)
    {
        var idx = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var rest = text.Substring(idx + keyword.Length).Trim(' ', ':', ';', '-');
        return rest.Length == 0 ? null : rest;
    }

    private static double? ParseNumeric(string raw)
    {
        var s = raw.Trim().TrimStart('=', ':').TrimEnd(',', ';');
        if (string.IsNullOrEmpty(s)) return null;
        if (s.EndsWith("%"))
        {
            var inner = ParseNumeric(s.Substring(0, s.Length - 1));
            return inner is null ? null : inner / 100.0;
        }
        var slash = s.IndexOf('/');
        if (slash > 0 && slash < s.Length - 1)
        {
            if (double.TryParse(s.Substring(0, slash), NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
                && double.TryParse(s.Substring(slash + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var b)
                && b != 0)
                return a / b;
        }
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static (double? Min, double? Max) ParseRange(string raw)
    {
        var s = raw.Trim();
        var dotMatch = Regex.Match(s, @"([^\s]+)\s*\.\.\s*([^\s]+)");
        if (dotMatch.Success)
        {
            var a = ParseNumeric(dotMatch.Groups[1].Value);
            var b = ParseNumeric(dotMatch.Groups[2].Value);
            if (a is null || b is null || a == b) return (null, null);
            return (Math.Min(a.Value, b.Value), Math.Max(a.Value, b.Value));
        }
        return (null, null);
    }

    private static Point2D? ParsePoint(string raw)
    {
        var s = raw;
        var m = Regex.Match(s, @"\(\s*([^,]+)\s*,\s*([^)]+)\s*\)");
        if (m.Success)
        {
            var x = ParseNumeric(m.Groups[1].Value);
            var y = ParseNumeric(m.Groups[2].Value);
            if (x is null || y is null) return null;
            return new Point2D(x.Value, y.Value);
        }
        var mx = Regex.Match(s, @"\bx\s*[:=]\s*([^\s,;]+)", RegexOptions.IgnoreCase);
        var my = Regex.Match(s, @"\by\s*[:=]\s*([^\s,;]+)", RegexOptions.IgnoreCase);
        if (mx.Success && my.Success)
        {
            var x = ParseNumeric(mx.Groups[1].Value);
            var y = ParseNumeric(my.Groups[1].Value);
            if (x is null || y is null) return null;
            return new Point2D(x.Value, y.Value);
        }
        return null;
    }

    private static BoardAction ParseAxes(string normalized, int index)
    {
        var xRange = Regex.Match(normalized, @"x\s*=\s*([^\s]+)", RegexOptions.IgnoreCase);
        var yRange = Regex.Match(normalized, @"y\s*=\s*([^\s]+)", RegexOptions.IgnoreCase);
        var xr = xRange.Success ? ParseRange(xRange.Groups[1].Value) : (null, null);
        var yr = yRange.Success ? ParseRange(yRange.Groups[1].Value) : (null, null);
        return new DrawAxesAction
        {
            Id = MakeId("axes", index),
            Range = new AxesRange(
                xr.Item1 ?? -5, xr.Item2 ?? 5,
                yr.Item1 ?? -5, yr.Item2 ?? 5)
        };
    }

    private static BoardAction ParseLine(string body, int index)
    {
        var eq = body;
        var labelMatch = Regex.Match(body, @"label\s*=\s*(.+)$", RegexOptions.IgnoreCase);
        string? label = null;
        if (labelMatch.Success)
        {
            label = labelMatch.Groups[1].Value.Trim();
            eq = body.Substring(0, labelMatch.Index).Trim();
        }
        return new DrawLineAction
        {
            Id = MakeId("line", index),
            Equation = eq,
            Label = label
        };
    }

    private static BoardAction ParsePoint(string body, int index)
    {
        var pt = ParsePoint(body);
        var labelMatch = Regex.Match(body, @"label\s*=\s*(.+)$", RegexOptions.IgnoreCase);
        return new DrawPointAction
        {
            Id = MakeId("point", index),
            At = pt ?? new Point2D(0, 0),
            Label = labelMatch.Success ? labelMatch.Groups[1].Value.Trim() : null
        };
    }

    private static BoardAction ParseCircle(string body, int index)
    {
        var centerMatch = Regex.Match(body, @"center\s*=\s*(\([^)]*\)|[^\s;,]+(?:\s*,\s*[^\s;,]+)?)", RegexOptions.IgnoreCase);
        var radiusMatch = Regex.Match(body, @"\b(?:r|radius)\s*=\s*([^\s;,]+)", RegexOptions.IgnoreCase);
        var pt = centerMatch.Success ? ParsePoint(centerMatch.Groups[1].Value) : ParsePoint(body);
        var radius = radiusMatch.Success ? ParseNumeric(radiusMatch.Groups[1].Value) : null;
        return new DrawCircleAction
        {
            Id = MakeId("circle", index),
            Center = pt ?? new Point2D(0, 0),
            Radius = radius ?? 1
        };
    }

    private static BoardAction ParseSquare(string body, int index)
    {
        var centerMatch = Regex.Match(body, @"center\s*=\s*(\([^)]*\)|[^\s;,]+(?:\s*,\s*[^\s;,]+)?)", RegexOptions.IgnoreCase);
        var sizeMatch = Regex.Match(body, @"\b(?:s|size|side)\s*=\s*([^\s;,]+)", RegexOptions.IgnoreCase);
        var angleMatch = Regex.Match(body, @"\bangle\s*=\s*([^\s;,]+)", RegexOptions.IgnoreCase);
        var pt = centerMatch.Success ? ParsePoint(centerMatch.Groups[1].Value) : ParsePoint(body);
        var size = sizeMatch.Success ? ParseNumeric(sizeMatch.Groups[1].Value) : null;
        var angle = angleMatch.Success ? ParseNumeric(angleMatch.Groups[1].Value) : null;
        return new DrawSquareAction
        {
            Id = MakeId("square", index),
            Center = pt ?? new Point2D(0, 0),
            Size = size ?? 2,
            AngleDegrees = angle ?? 0
        };
    }

    private static BoardAction ParseTriangle(string body, int index)
    {
        var legsMatch = Regex.Match(body, @"legs?\s*[:=]?\s*([^,\s;]+)\s*,\s*([^\s;]+)", RegexOptions.IgnoreCase);
        var hypMatch = Regex.Match(body, @"hyp(?:otenuse)?\s*[:=]?\s*([^\s;]+)", RegexOptions.IgnoreCase);
        var angleMatch = Regex.Match(body, @"angle\s*[:=]?\s*([^\s;]+)", RegexOptions.IgnoreCase);
        var baseLenMatch = Regex.Match(body, @"\bbase\s*=\s*([^\s;,]+)", RegexOptions.IgnoreCase);
        var heightLenMatch = Regex.Match(body, @"\bheight\s*=\s*([^\s;,]+)", RegexOptions.IgnoreCase);

        return new DrawTriangleAction
        {
            Id = MakeId("triangle", index),
            BaseLength = baseLenMatch.Success ? ParseNumeric(baseLenMatch.Groups[1].Value) : null,
            HeightLength = heightLenMatch.Success ? ParseNumeric(heightLenMatch.Groups[1].Value) : null,
            AcuteAngleDegrees = null,
            Labels = new TriangleLabels(
                BaseLabel: legsMatch.Success ? legsMatch.Groups[1].Value.Trim() : null,
                HeightLabel: legsMatch.Success ? legsMatch.Groups[2].Value.Trim() : null,
                HypotenuseLabel: hypMatch.Success ? hypMatch.Groups[1].Value.Trim() : null,
                AngleLabel: angleMatch.Success ? angleMatch.Groups[1].Value.Trim() : null)
        };
    }

    private static BoardAction ParseArrow(string body, int index)
    {
        var fromMatch = Regex.Match(body, @"from\s*=\s*(\([^)]*\))", RegexOptions.IgnoreCase);
        var toMatch = Regex.Match(body, @"to\s*=\s*(\([^)]*\))", RegexOptions.IgnoreCase);
        var labelMatch = Regex.Match(body, @"label\s*=\s*(.+)$", RegexOptions.IgnoreCase);
        var from = fromMatch.Success ? ParsePoint(fromMatch.Groups[1].Value) : null;
        var to = toMatch.Success ? ParsePoint(toMatch.Groups[1].Value) : null;
        return new DrawArrowAction
        {
            Id = MakeId("arrow", index),
            From = from ?? new Point2D(0, 0),
            To = to ?? new Point2D(1, 1),
            Label = labelMatch.Success ? labelMatch.Groups[1].Value.Trim() : null
        };
    }

    private static BoardAction ParseBar(string body, int index)
    {
        var bars = new List<BarValue>();
        var pairs = Regex.Matches(body, @"([A-Za-z][A-Za-z0-9_\-]*)\s*=\s*(-?[0-9]+(?:\.[0-9]+)?)");
        foreach (Match p in pairs)
        {
            if (double.TryParse(p.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                bars.Add(new BarValue(p.Groups[1].Value, v));
        }
        return new DrawBarChartAction
        {
            Id = MakeId("bar", index),
            Bars = bars
        };
    }
}
