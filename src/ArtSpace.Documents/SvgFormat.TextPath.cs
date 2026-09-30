using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ArtSpace.Core;

namespace ArtSpace.Documents;

public static partial class SvgFormat
{
    private static readonly (string Unit, double Scale)[] TextUnits = [("px", 1d), ("pt", 96d / 72), ("pc", 16d), ("in", 96d), ("cm", 96d / 2.54), ("mm", 96d / 25.4)];
    private sealed record SvgBaseline(string Data, Matrix2D Transform, double? PathLength);

    // Build once per import; references are resolved only within the supplied document.
    private static Dictionary<string, XElement> TextPathDefinitions(XElement root)
    {
        var result = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var count = 0;
        foreach (var element in root.DescendantsAndSelf())
        {
            if (++count > DocumentJson.MaxNodes) throw new InvalidDataException("SVG element budget exceeded.");
            if (element.Attribute("id")?.Value is { Length: > 0 } id && !result.TryAdd(id, element))
                throw new InvalidDataException("Duplicate SVG identifier: " + id);
        }
        return result;
    }

    private static XElement? ReadTextPath(XElement text, DesignNode node, Dictionary<string, XElement> definitions,
        Dictionary<string, SvgBaseline> cache)
    {
        var paths = text.Elements(Ns + "textPath").Concat(text.Elements("textPath")).ToArray();
        if (paths.Length == 0) return null;
        if (paths.Length != 1 || text.Elements().Any(e => e != paths[0]) ||
            text.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)))
            throw new InvalidDataException("Path text requires exactly one textPath without mixed text runs.");
        var pathText = paths[0];
        if (pathText.HasElements) throw new InvalidDataException("Styled/nested textPath runs are not supported; retain a single text run or outline them.");
        foreach (var e in new[] { text, pathText })
        {
            foreach (var attribute in new[] { "textLength", "lengthAdjust", "rotate", "dx" })
                if (Own(e, attribute) is not null) throw new InvalidDataException("Unsupported path-text positioning: " + attribute);
            if (Own(e, "font") is not null) throw new InvalidDataException("Use explicit font properties, not SVG font shorthand.");
        }
        if (Own(pathText, "method") is { } method && method != "align" ||
            Own(pathText, "spacing") is { } spacing && spacing != "exact")
            throw new InvalidDataException("Only align/exact SVG path text is supported.");
        foreach (var attribute in new[] { "clip-path", "mask", "filter" })
            if (Own(pathText, attribute) is { } value && value != "none")
                throw new InvalidDataException("Place clipping, masks and filters on the parent text element.");
        if (Own(pathText, "transform") is not null || Own(pathText, "dy") is not null)
            throw new InvalidDataException("Set transform and dy on the parent text element.");
        if (Inherited(pathText, "direction") is { } direction && direction != "ltr" ||
            Inherited(pathText, "writing-mode") is { } writing && writing is not ("horizontal-tb" or "lr" or "lr-tb") ||
            Inherited(pathText, "unicode-bidi") is { } bidi && bidi != "normal")
            throw new InvalidDataException("Bidirectional or vertical path-text layout is not supported.");
        var dominant = Inherited(pathText, "dominant-baseline");
        if (dominant is not (null or "auto" or "alphabetic"))
            throw new InvalidDataException("Only alphabetic SVG path-text baselines are supported.");
        if (Inherited(pathText, "baseline-shift") is { } shift && shift is not ("0" or "baseline"))
            throw new InvalidDataException("Use a scalar dy for SVG path-text baseline displacement.");

        SvgBaseline baseline;
        if (pathText.Attribute("path") is { } inline)
        {
            if (string.IsNullOrWhiteSpace(inline.Value)) throw new InvalidDataException("Empty inline textPath geometry.");
            baseline = new(inline.Value, Matrix2D.Identity, null);
        }
        else
        {
            var reference = pathText.Attribute("href")?.Value ?? pathText.Attribute(XName.Get("href", "http://www.w3.org/1999/xlink"))?.Value;
            if (reference is null || reference.Length < 2 || reference[0] != '#' || reference[1..].Any(char.IsWhiteSpace))
                throw new InvalidDataException("SVG textPath must reference a local geometry identifier.");
            var id = reference[1..];
            if (!cache.TryGetValue(id, out baseline!))
            {
                if (!definitions.TryGetValue(id, out var geometry)) throw new InvalidDataException("Missing SVG textPath: " + id);
                if (geometry.Elements().Any()) throw new InvalidDataException("Animated or nested SVG textPath geometry is unsupported.");
                var shape = new DesignNode { Fills = [], Width = 1, Height = 1 };
                double N(string name, double fallback = 0) => TextLength(Own(geometry, name), fallback);
                switch (geometry.Name.LocalName)
                {
                    case "path": shape.Kind = NodeKind.Path; shape.PathData = geometry.Attribute("d")?.Value ?? ""; break;
                    case "line": shape.Kind = NodeKind.Path; shape.Points = [new() { Position = new(N("x1"), N("y1")) }, new() { Position = new(N("x2"), N("y2")) }]; break;
                    case "circle": shape.Kind = NodeKind.Ellipse; shape.Width = shape.Height = 2 * N("r"); shape.X = N("cx") - shape.Width / 2; shape.Y = N("cy") - shape.Height / 2; break;
                    case "ellipse": shape.Kind = NodeKind.Ellipse; shape.Width = 2 * N("rx"); shape.Height = 2 * N("ry"); shape.X = N("cx") - shape.Width / 2; shape.Y = N("cy") - shape.Height / 2; break;
                    case "rect":
                        shape.Width = N("width"); shape.Height = N("height"); shape.X = N("x"); shape.Y = N("y");
                        shape.CornerRadius = N("rx", N("ry"));
                        if (Math.Abs(N("ry", shape.CornerRadius) - shape.CornerRadius) > 1e-9)
                            throw new InvalidDataException("Unequal SVG rounded-rectangle radii need a path baseline.");
                        break;
                    case "polygon": case "polyline":
                        var points = geometry.Attribute("points")?.Value ?? "";
                        if (NumberRegex().Replace(points, "").Any(c => !char.IsWhiteSpace(c) && c != ',')) throw new InvalidDataException("Invalid baseline points.");
                        var values = Values(points);
                        if (values.Length < 4 || values.Length % 2 != 0) throw new InvalidDataException("Baseline points require coordinate pairs.");
                        shape.Kind = NodeKind.Path; shape.Closed = geometry.Name.LocalName == "polygon";
                        for (var i = 0; i < values.Length; i += 2) shape.Points.Add(new() { Position = new(values[i], values[i + 1]) });
                        break;
                    default: throw new InvalidDataException("SVG textPath must reference a path or supported basic shape, not " + geometry.Name.LocalName + ".");
                }
                if (!double.IsFinite(shape.Width) || !double.IsFinite(shape.Height) || shape.Width <= 0 || shape.Height <= 0)
                    throw new InvalidDataException("Invalid baseline shape dimensions.");
                var transform = Own(geometry, "transform") is { } value ? ParseTransform(value) : Matrix2D.Identity;
                var authored = geometry.Attribute("pathLength") is { } length ? TextNumber(length.Value) : (double?)null;
                baseline = new(VectorPath.Build(shape), shape.LocalMatrix * transform, authored);
                cache.Add(id, baseline);
            }
        }
        if (baseline.Data.Length is 0 or > 4_000_000) throw new InvalidDataException("Invalid SVG textPath geometry budget.");
        var rawOffset = (Own(pathText, "startOffset") ?? "0").Trim();
        var percentage = rawOffset.EndsWith('%');
        var position = new SvgTextPathPosition
        {
            Offset = percentage ? TextNumber(rawOffset[..^1]) : TextLength(rawOffset), Percentage = percentage,
            PathLength = baseline.PathLength, PathTransform = baseline.Transform
        };
        position.Validate();
        node.Kind = NodeKind.Text; node.X = node.Y = 0; node.PathWidth = node.Width; node.PathHeight = node.Height;
        node.PathData = baseline.Data;
        var whiteSpace = Inherited(pathText, "white-space");
        if (whiteSpace is not (null or "normal" or "nowrap" or "pre" or "pre-wrap" or "break-spaces"))
            throw new InvalidDataException("Unsupported SVG path-text whitespace mode.");
        var xmlSpace = pathText.AncestorsAndSelf().Select(e => e.Attribute(XNamespace.Xml + "space")?.Value).FirstOrDefault(v => v is not null);
        var preserve = whiteSpace is "pre" or "pre-wrap" or "break-spaces" || whiteSpace is null && xmlSpace == "preserve";
        node.Text = preserve ? pathText.Value.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ')
            : SvgWhitespace().Replace(pathText.Value, " ").Trim(' ');
        node.FontSize = TextLength(Inherited(pathText, "font-size"), 16);
        node.FontFamily = (Inherited(pathText, "font-family") ?? "Inter").Split(',')[0].Trim().Trim('\'', '"');
        var weight = Inherited(pathText, "font-weight") ?? "400";
        var numericWeight = weight switch { "normal" => 400d, "bold" => 700d, _ => TextNumber(weight) };
        if (numericWeight < 1 || numericWeight > 1000 || numericWeight != Math.Truncate(numericWeight))
            throw new InvalidDataException("Only integral SVG font weights between 1 and 1000 are supported.");
        node.FontWeight = (int)numericWeight;
        if (node.FontSize is < 1 or > 4096 || node.FontWeight is < 1 or > 1000) throw new InvalidDataException("Invalid SVG path-text font metrics.");
        var fontStyle = Inherited(pathText, "font-style");
        if (fontStyle is not (null or "normal")) throw new InvalidDataException("Italic SVG path text needs an explicitly supported font or outlines.");
        var tracking = Inherited(pathText, "letter-spacing");
        node.LetterSpacing = tracking is null or "normal" ? 0 : TextLength(tracking);
        node.TextAlign = (Inherited(pathText, "text-anchor") ?? "start") switch
        {
            "start" => TextAlignment.Left, "middle" => TextAlignment.Center, "end" => TextAlignment.Right,
            _ => throw new InvalidDataException("Invalid SVG text anchor.")
        };
        var flip = (Own(pathText, "side") ?? "left") switch { "left" => false, "right" => true, _ => throw new InvalidDataException("Invalid SVG textPath side.") };
        node.TextPath = new() { SvgPosition = position, Flip = flip, BaselineShift = -TextLength(Own(text, "dy")) };
        node.TextPath.Validate();
        if (node.Text.Length > TypeOnPathOptions.MaxTextLength) throw new InvalidDataException("SVG path text exceeds the text budget.");
        return pathText;
    }

    private static double TextNumber(string text)
    {
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            throw new InvalidDataException("Expected a finite SVG number: " + text);
        return value;
    }

    // Deliberately reject lists and context-dependent units rather than extracting their first number.
    private static double TextLength(string? text, double fallback = 0)
    {
        if (text is null) return fallback;
        text = text.Trim(); var scale = 1d;
        foreach (var (unit, factor) in TextUnits)
            if (text.EndsWith(unit, StringComparison.Ordinal)) { text = text[..^unit.Length]; scale = factor; break; }
        var result = TextNumber(text) * scale;
        if (!double.IsFinite(result)) throw new InvalidDataException("SVG length overflow.");
        return result;
    }

    private static void ExportTextPathDefinition(DesignNode node, XElement defs)
    {
        var position = node.TextPath?.SvgPosition ?? throw new InvalidOperationException("Convert bracket-based path text through IllustrationSvgExport's editable mode first.");
        position.Validate();
        var path = new XElement(Ns + "path", new XAttribute("id", "text-baseline-" + node.Id), new XAttribute("d", VectorPath.Build(node)));
        var scale = node.PathWidth > 0 && node.PathHeight > 0 ? Matrix2D.Scale(node.Width / node.PathWidth, node.Height / node.PathHeight) : Matrix2D.Identity;
        path.SetAttributeValue("transform", Transform(scale * position.PathTransform));
        if (position.PathLength is { } length) path.SetAttributeValue("pathLength", F(length));
        defs.Add(path);
    }

    private static XElement ExportTextPathShape(DesignNode node)
    {
        var options = node.TextPath!; var position = options.SvgPosition!;
        if (options.Alignment != PathTextAlignment.Baseline) throw new InvalidOperationException("Resolve font-metric baseline alignment through IllustrationSvgExport before editable SVG export.");
        var text = new XElement(Ns + "text", new XAttribute("font-family", node.FontFamily), new XAttribute("font-size", F(node.FontSize)),
            new XAttribute("font-weight", node.FontWeight), new XAttribute("letter-spacing", F(node.LetterSpacing)),
            new XAttribute("text-anchor", node.TextAlign == TextAlignment.Center ? "middle" : node.TextAlign == TextAlignment.Right ? "end" : "start"),
            new XAttribute("dy", F(-options.BaselineShift)), new XAttribute(XNamespace.Xml + "space", "preserve"),
            new XAttribute("style", "white-space:pre;font-kerning:none;font-variant-ligatures:none"));
        var path = new XElement(Ns + "textPath", new XAttribute("href", "#text-baseline-" + node.Id),
            new XAttribute("startOffset", F(position.Offset) + (position.Percentage ? "%" : "")),
            new XAttribute("method", "align"), new XAttribute("spacing", "exact"), node.Text);
        if (options.Flip) path.SetAttributeValue("side", "right");
        text.Add(path); return text;
    }

    [GeneratedRegex("[ \\t\\r\\n]+")]
    private static partial Regex SvgWhitespace();
}
