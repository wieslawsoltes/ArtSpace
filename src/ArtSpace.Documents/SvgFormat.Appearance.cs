using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ArtSpace.Core;

namespace ArtSpace.Documents;

public static partial class SvgFormat
{
    private static string? Own(XElement element, string key) => Style(element, key) ?? element.Attribute(key)?.Value;
    private static string? Inherited(XElement element, string key) => element.AncestorsAndSelf().Select(e => Own(e, key)).FirstOrDefault(v => v is not null);
    private static string LocalReference(string value)
    {
        var match = Regex.Match(value.Trim(), "^url\\(\\s*['\"]?#([^'\"\\s)]+)['\"]?\\s*\\)$", RegexOptions.CultureInvariant);
        if (!match.Success) throw new InvalidDataException("Only local SVG fragment references are supported.");
        return match.Groups[1].Value;
    }
    private static Dictionary<string, XElement> Definitions(XElement root, params string[] kinds)
    {
        var result = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var element in root.Descendants().Where(e => kinds.Contains(e.Name.LocalName)))
            if (element.Attribute("id") is { } id && !result.TryAdd(id.Value, element))
                throw new InvalidDataException("Duplicate SVG definition identifier.");
        return result;
    }
    private static double Scalar(string? value, double fallback, double percentageScale = 1)
    {
        if (value is null) return fallback;
        var trimmed = value.Trim(); var percent = trimmed.EndsWith('%');
        if (percent) trimmed = trimmed[..^1];
        else if (trimmed.EndsWith("px", StringComparison.Ordinal)) trimmed = trimmed[..^2];
        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
            throw new InvalidDataException("Unsupported or nonfinite SVG numeric value: " + value);
        return percent ? number * percentageScale / 100 : number;
    }

    private static FillStyle ReadGradient(string paint, DesignNode node, Dictionary<string, XElement> registry, double viewportWidth, double viewportHeight)
    {
        var id = LocalReference(paint);
        if (!registry.TryGetValue(id, out var gradient)) throw new InvalidDataException("Missing SVG gradient definition: " + id);
        var chain = new List<XElement>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var current = gradient; ;)
        {
            if (chain.Count >= 32 || !seen.Add(current.Attribute("id")!.Value)) throw new InvalidDataException("Cyclic or excessive SVG gradient inheritance.");
            chain.Add(current);
            var href = current.Attribute("href")?.Value ?? current.Attribute(XNamespace.Get("http://www.w3.org/1999/xlink") + "href")?.Value;
            if (href is null) break;
            if (!href.StartsWith('#') || !registry.TryGetValue(href[1..], out current!)) throw new InvalidDataException("Missing or external SVG gradient inheritance.");
        }
        string? Value(string name) => chain.Select(e => Own(e, name)).FirstOrDefault(v => v is not null);
        if (Value("color-interpolation") is { } interpolation && interpolation != "sRGB") throw new InvalidDataException("Only sRGB gradient interpolation is currently supported.");
        var space = Value("gradientUnits") switch { null or "objectBoundingBox" => GradientSpace.ObjectBoundingBox, "userSpaceOnUse" => GradientSpace.UserSpaceOnUse, _ => throw new InvalidDataException("Invalid SVG gradient units.") };
        var xScale = space == GradientSpace.ObjectBoundingBox ? 1 : viewportWidth;
        var yScale = space == GradientSpace.ObjectBoundingBox ? 1 : viewportHeight;
        var radial = gradient.Name.LocalName == "radialGradient";
        var start = radial ? new Vec2(Scalar(Value("cx"), xScale / 2, xScale), Scalar(Value("cy"), yScale / 2, yScale))
            : new Vec2(Scalar(Value("x1"), 0, xScale), Scalar(Value("y1"), 0, yScale));
        var end = new Vec2(Scalar(Value("x2"), xScale, xScale), Scalar(Value("y2"), 0, yScale));
        var transform = Value("gradientTransform") is { } t ? ParseTransform(t) : Matrix2D.Identity;
        if (space == GradientSpace.UserSpaceOnUse) transform *= node.LocalMatrix.Inverse;
        if (!AffineGeometry.IsInvertible(transform)) throw new InvalidDataException("Singular SVG gradient transforms are not supported.");
        var radiusScale = space == GradientSpace.ObjectBoundingBox ? 1 : Math.Sqrt((viewportWidth * viewportWidth + viewportHeight * viewportHeight) / 2);
        if (Scalar(Value("fr"), 0, radiusScale) != 0) throw new InvalidDataException("SVG radial start-radius gradients are not yet supported.");
        var fill = new FillStyle
        {
            Kind = radial ? FillKind.RadialGradient : FillKind.LinearGradient,
            GradientSpace = space, Start = start, End = end,
            GradientRadius = Scalar(Value("r"), radiusScale / 2, radiusScale),
            GradientFocus = radial ? new Vec2(Scalar(Value("fx"), start.X, xScale), Scalar(Value("fy"), start.Y, yScale)) : null,
            GradientTransform = transform,
            GradientSpread = Value("spreadMethod") switch { null or "pad" => GradientSpread.Pad, "repeat" => GradientSpread.Repeat, "reflect" => GradientSpread.Reflect, _ => throw new InvalidDataException("Invalid gradient spread method.") },
            Stops = []
        };
        if (fill.GradientRadius < 0) throw new InvalidDataException("Gradient radius cannot be negative.");
        var stopSource = chain.FirstOrDefault(e => e.Elements(Ns + "stop").Any() || e.Elements().Any(s => s.Name.LocalName == "stop"));
        if (stopSource is not null)
        {
            var previous = 0d;
            foreach (var stop in stopSource.Elements().Where(s => s.Name.LocalName == "stop"))
            {
                if (fill.Stops.Count >= 4096) throw new InvalidDataException("SVG gradient stop limit exceeded.");
                var offset = Math.Max(previous, Math.Clamp(Scalar(Own(stop, "offset"), 0), 0, 1)); previous = offset;
                var color = Inherited(stop, "stop-color") ?? "#000000";
                if (color == "currentColor") color = Inherited(stop, "color") ?? "#000000";
                fill.Stops.Add(new() { Offset = offset, Color = color, Opacity = Math.Clamp(Scalar(Inherited(stop, "stop-opacity"), 1), 0, 1) });
            }
        }
        return fill;
    }

    private static XElement ExportGradient(FillStyle fill, DesignNode node, string id)
    {
        var radial = fill.Kind == FillKind.RadialGradient;
        var start = fill.Start; var end = fill.End; var focus = fill.GradientFocus ?? start;
        var radius = fill.GradientRadius; var transform = fill.GradientTransform;
        var box = fill.GradientSpace == GradientSpace.ObjectBoundingBox;
        if (fill.GradientSpace == GradientSpace.Legacy)
        {
            start = new(start.X * node.Width, start.Y * node.Height); end = new(end.X * node.Width, end.Y * node.Height);
            focus = start; radius = Math.Max(1, start.DistanceTo(end));
        }
        // Shape() emits a path-space scale for paths whose stored coordinates differ from their size.
        // User-space paint coordinates must be re-expressed before that scale, not scaled a second time.
        if (!box && node.Kind == NodeKind.Path && node.PathWidth > 0 && node.PathHeight > 0 && node.Width > 0 && node.Height > 0)
            transform *= Matrix2D.Scale(node.PathWidth / node.Width, node.PathHeight / node.Height);
        var result = new XElement(Ns + (radial ? "radialGradient" : "linearGradient"), new XAttribute("id", id),
            new XAttribute("gradientUnits", box ? "objectBoundingBox" : "userSpaceOnUse"),
            new XAttribute("gradientTransform", Transform(transform)),
            new XAttribute("spreadMethod", fill.GradientSpread.ToString().ToLowerInvariant()), new XAttribute("color-interpolation", "sRGB"));
        if (radial)
        {
            result.SetAttributeValue("cx", F(start.X)); result.SetAttributeValue("cy", F(start.Y)); result.SetAttributeValue("r", F(radius));
            result.SetAttributeValue("fx", F(focus.X)); result.SetAttributeValue("fy", F(focus.Y));
        }
        else
        {
            result.SetAttributeValue("x1", F(start.X)); result.SetAttributeValue("y1", F(start.Y));
            result.SetAttributeValue("x2", F(end.X)); result.SetAttributeValue("y2", F(end.Y));
        }
        foreach (var stop in fill.Stops.OrderBy(s => s.Offset))
            result.Add(new XElement(Ns + "stop", new XAttribute("offset", F(Math.Clamp(stop.Offset, 0, 1))), new XAttribute("stop-color", stop.Color), new XAttribute("stop-opacity", F(stop.Opacity))));
        return result;
    }

    private sealed record ExportViewport(RectD Bounds);

    private static XElement ExportOpacityMask(DesignNode owner, XElement defs)
    {
        var id = "opacity-mask-" + owner.Id;
        var viewport = defs.Annotation<ExportViewport>()?.Bounds ?? owner.WorldBounds;
        var region = owner.OpacityMaskRegion ?? owner.WorldMatrix.Inverse.Map(viewport);
        if (region.IsEmpty || !double.IsFinite(region.Right) || !double.IsFinite(region.Bottom)) throw new InvalidOperationException("Invalid SVG mask export bounds.");
        var mask = new XElement(Ns + "mask", new XAttribute("id", id), new XAttribute("maskUnits", "userSpaceOnUse"), new XAttribute("maskContentUnits", "userSpaceOnUse"),
            new XAttribute("x", F(region.X)), new XAttribute("y", F(region.Y)), new XAttribute("width", F(region.Width)), new XAttribute("height", F(region.Height)),
            new XAttribute("style", "mask-type:" + (owner.OpacityMaskMode == OpacityMaskMode.Alpha ? "alpha" : "luminance")), new XAttribute("color-interpolation", "sRGB"));
        if (owner.OpacityMaskInverted)
            throw new InvalidOperationException("SVG export of inverted opacity masks is not supported yet. Disable inversion or use PNG/native export.");
        mask.Add(ExportNode(owner.OpacityMask!, defs)); return mask;
    }
}
