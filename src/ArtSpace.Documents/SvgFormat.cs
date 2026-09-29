using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ArtSpace.Core;

namespace ArtSpace.Documents;

public sealed record SvgImportResult(DesignDocument Document, IReadOnlyList<string> Warnings);

/// <summary>Safe, editable SVG interchange. Scripts, external resources and DTDs are never executed.</summary>
public static partial class SvgFormat
{
    private static readonly XNamespace Ns = "http://www.w3.org/2000/svg";
    public static string Export(IEnumerable<DesignNode> roots, RectD bounds)
    {
        if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Bottom) || bounds.IsEmpty) throw new ArgumentException("SVG export needs finite nonempty bounds.", nameof(bounds));
        var defs = new XElement(Ns + "defs");
        defs.AddAnnotation(new ExportViewport(bounds));
        var svg = new XElement(Ns + "svg", new XAttribute("width", F(bounds.Width)), new XAttribute("height", F(bounds.Height)), new XAttribute("viewBox", $"{F(bounds.X)} {F(bounds.Y)} {F(bounds.Width)} {F(bounds.Height)}"), defs);
        foreach (var node in roots) svg.Add(ExportNode(node, defs, true));
        return new XDocument(new XDeclaration("1.0", "utf-8", null), svg).ToString();
    }
    private static XElement? ExportNode(DesignNode node, XElement defs, bool world = false)
    {
        if (!node.Visible || node.Kind == NodeKind.Slice) return null;
        if (node.TextPath is not null) throw new InvalidOperationException("Use IllustrationSvgExport or Create Outlines to export type-on-path text as vector geometry.");
        if (node.Effects.Any(effect => effect.Enabled)) throw new InvalidOperationException("Live effects require native or PNG export; SVG filter interchange is not yet supported.");
        var group = new XElement(Ns + "g", new XAttribute("id", "layer-" + node.Id), new XAttribute("data-name", node.Name), new XAttribute("transform", Transform(world ? node.WorldMatrix : node.LocalMatrix)), new XAttribute("opacity", F(node.Opacity)));
        if (node.Blend != BlendKind.Normal) group.SetAttributeValue("style", "mix-blend-mode:" + SvgBlendName(node.Blend));
        var shadow = node.Shadows.FirstOrDefault(s => s.Visible);
        if (shadow is not null)
        {
            var filterId = "shadow-" + node.Id;
            defs.Add(new XElement(Ns + "filter", new XAttribute("id", filterId), new XAttribute("x", "-100%"), new XAttribute("y", "-100%"), new XAttribute("width", "300%"), new XAttribute("height", "300%"), new XElement(Ns + "feDropShadow", new XAttribute("dx", F(shadow.X)), new XAttribute("dy", F(shadow.Y)), new XAttribute("stdDeviation", F(shadow.Blur / 2)), new XAttribute("flood-color", shadow.Color), new XAttribute("flood-opacity", F(shadow.Opacity)))));
            group.SetAttributeValue("filter", "url(#" + filterId + ")");
        }
        for (var i = 0; i < node.Fills.Count; i++)
        {
            var fill = node.Fills[i]; if (!fill.Visible) continue; var color = fill.Color;
            if (fill.Kind != FillKind.Solid)
            {
                var id = $"paint-{node.Id}-{i}";
                var gradient = ExportGradient(fill, node, id);
                defs.Add(gradient); color = "url(#" + id + ")";
            }
            var shape = Shape(node); shape.SetAttributeValue("fill", color); shape.SetAttributeValue("fill-opacity", F(fill.Opacity)); shape.SetAttributeValue("stroke", "none"); group.Add(shape);
        }
        for (var strokeIndex = 0; strokeIndex < node.Strokes.Count; strokeIndex++)
        {
            var stroke = node.Strokes[strokeIndex];
            if (!stroke.Visible || stroke.Paint?.Visible == false) continue;
            var strokeColor = stroke.Paint?.Color ?? stroke.Color;
            if (stroke.Paint is { Kind: not FillKind.Solid } paint)
            {
                var id = $"stroke-paint-{node.Id}-{strokeIndex}";
                defs.Add(ExportGradient(paint, node, id)); strokeColor = "url(#" + id + ")";
            }
            var shape = Shape(node); shape.SetAttributeValue("fill", "none"); shape.SetAttributeValue("stroke", strokeColor); shape.SetAttributeValue("stroke-width", F(stroke.Width)); shape.SetAttributeValue("stroke-opacity", F(stroke.Opacity * (stroke.Paint?.Opacity ?? 1))); shape.SetAttributeValue("stroke-linejoin", stroke.Join.ToString().ToLowerInvariant()); shape.SetAttributeValue("stroke-linecap", stroke.Cap.ToString().ToLowerInvariant()); shape.SetAttributeValue("stroke-miterlimit", F(stroke.MiterLimit));
            if (stroke.Dashes.Count > 0) { shape.SetAttributeValue("stroke-dasharray", string.Join(" ", stroke.Dashes.Select(F))); shape.SetAttributeValue("stroke-dashoffset", F(stroke.DashOffset)); } group.Add(shape);
        }
        var children = new XElement(Ns + "g");
        if (node.ClipContent)
        {
            var id = "clip-" + node.Id; defs.Add(new XElement(Ns + "clipPath", new XAttribute("id", id), new XElement(Ns + "rect", new XAttribute("width", F(node.Width)), new XAttribute("height", F(node.Height)), new XAttribute("rx", F(node.CornerRadius))))); children.SetAttributeValue("clip-path", "url(#" + id + ")");
        }
        var contentTarget = children;
        if (node.ClippingPath is { } mask)
        {
            var id = "vector-clip-" + node.Id;
            var shape = Shape(mask);
            var scale = mask.Kind == NodeKind.Path && mask.PathWidth > 0 && mask.PathHeight > 0 ? Matrix2D.Scale(mask.Width / mask.PathWidth, mask.Height / mask.PathHeight) : Matrix2D.Identity;
            shape.SetAttributeValue("transform", Transform(scale * mask.LocalMatrix));
            shape.SetAttributeValue("clip-rule", mask.FillRule == PathFillRule.EvenOdd ? "evenodd" : "nonzero");
            defs.Add(new XElement(Ns + "clipPath", new XAttribute("id", id), new XAttribute("clipPathUnits", "userSpaceOnUse"), shape));
            contentTarget = new XElement(Ns + "g", new XAttribute("clip-path", "url(#" + id + ")")); children.Add(contentTarget);
        }
        foreach (var child in node.Children) if (child.Id != node.ClipPathId && child.Id != node.OpacityMaskId) contentTarget.Add(ExportNode(child, defs));
        if (children.HasElements) group.Add(children);
        if (node.OpacityMaskId is not null && node.OpacityMaskEnabled)
        {
            var definition = ExportOpacityMask(node, defs); defs.Add(definition);
            var masked = new XElement(Ns + "g", new XAttribute("mask", "url(#" + definition.Attribute("id")!.Value + ")"));
            var artwork = group.Elements().ToArray(); foreach (var element in artwork) { element.Remove(); masked.Add(element); }
            group.Add(masked);
        }
        return group;
    }
    private static XElement Shape(DesignNode node)
    {
        if (node.Kind == NodeKind.Text)
        {
            var anchor = node.TextAlign == TextAlignment.Center ? "middle" : node.TextAlign == TextAlignment.Right ? "end" : "start";
            var x = node.TextAlign == TextAlignment.Center ? node.Width / 2 : node.TextAlign == TextAlignment.Right ? node.Width : 0;
            var element = new XElement(Ns + "text", new XAttribute("font-family", node.FontFamily), new XAttribute("font-size", F(node.FontSize)), new XAttribute("font-weight", node.FontWeight), new XAttribute("letter-spacing", F(node.LetterSpacing)), new XAttribute("text-anchor", anchor));
            var lines = node.Text.Replace("\r", "").Split('\n');
            for (var i = 0; i < lines.Length; i++) element.Add(new XElement(Ns + "tspan", new XAttribute("x", F(x)), new XAttribute("y", F(node.FontSize + i * node.FontSize * node.LineHeight)), lines[i]));
            return element;
        }
        var shape = new XElement(Ns + "path", new XAttribute("d", VectorPath.Build(node)), new XAttribute("fill-rule", node.FillRule == PathFillRule.EvenOdd ? "evenodd" : "nonzero"));
        if (node.Kind == NodeKind.Path && node.PathWidth > 0 && node.PathHeight > 0) shape.SetAttributeValue("transform", $"scale({F(node.Width / node.PathWidth)} {F(node.Height / node.PathHeight)})");
        return shape;
    }
    public static SvgImportResult Import(string source, string name = "Imported SVG")
    {
        if (source.Length > DocumentJson.MaxDocumentCharacters) throw new InvalidDataException("SVG exceeds the import limit.");
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = DocumentJson.MaxDocumentCharacters };
        using var text = new StringReader(source); using var reader = XmlReader.Create(text, settings);
        var xml = XDocument.Load(reader); var root = xml.Root ?? throw new InvalidDataException("SVG is empty.");
        if (root.Name.LocalName != "svg") throw new InvalidDataException("The file root must be svg.");
        if (root.Descendants().Any(e => e.Name.LocalName == "textPath"))
            throw new InvalidDataException("SVG textPath import is not supported yet. Convert path text to outlines in the source application.");
        var warnings = new HashSet<string>(); var viewBox = Values(root.Attribute("viewBox")?.Value);
        var width = Number(root, "width", viewBox.Length == 4 ? viewBox[2] : 800); var height = Number(root, "height", viewBox.Length == 4 ? viewBox[3] : 600);
        var frame = new DesignNode { Kind = NodeKind.Frame, Name = name, Width = Math.Max(1, width), Height = Math.Max(1, height), Fills = [], ClipContent = true };
        var clips = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var definition in root.Descendants().Where(e => e.Name.LocalName == "clipPath"))
        {
            var id = definition.Attribute("id")?.Value;
            if (id is not null && !clips.TryAdd(id, definition)) throw new InvalidDataException("Duplicate SVG clipping identifier.");
        }
        var gradients = Definitions(root, "linearGradient", "radialGradient");
        var opacityMasks = Definitions(root, "mask");
        var activeMasks = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        foreach (var child in root.Elements())
        {
            var node = Read(child, 0); if (node is not null) frame.Add(node);
        }
        if (viewBox.Length == 4 && viewBox[2] > 0 && viewBox[3] > 0)
        {
            var transform = Matrix2D.Translation(-viewBox[0], -viewBox[1]) * Matrix2D.Scale(width / viewBox[2], height / viewBox[3]);
            foreach (var n in frame.Children) NodeGeometry.SetExactMatrix(n, n.LocalMatrix * transform);
        }
        var document = new DesignDocument { Name = name, Pages = [new() { Nodes = [frame] }] }; document.RebuildParents(); DocumentJson.Validate(document); return new(document, warnings.ToArray());
        DesignNode? Read(XElement element, int depth)
        {
            if (++count > DocumentJson.MaxNodes || depth > 60) throw new InvalidDataException("SVG node or nesting limit exceeded.");
            var kind = element.Name.LocalName;
            if (kind is "defs" or "title" or "desc" or "metadata") return null;
            if (kind is "script" or "foreignObject" or "image" or "use" or "style" or "filter" or "clipPath" or "mask") { warnings.Add($"{kind} elements were not imported."); return null; }
            var node = new DesignNode { Name = element.Attribute("data-name")?.Value ?? element.Attribute("id")?.Value ?? kind, X = Number(element, "x"), Y = Number(element, "y"), Width = Number(element, "width", width), Height = Number(element, "height", height), Fills = [] };
            switch (kind)
            {
                case "g": case "svg": node.Kind = NodeKind.Group; node.X = node.Y = 0; break;
                case "rect": node.Kind = NodeKind.Rectangle; node.CornerRadius = Number(element, "rx"); break;
                case "circle": var radius = Number(element, "r"); node.Kind = NodeKind.Ellipse; node.Width = node.Height = radius * 2; node.X = Number(element, "cx") - radius; node.Y = Number(element, "cy") - radius; break;
                case "ellipse": node.Kind = NodeKind.Ellipse; node.Width = Number(element, "rx") * 2; node.Height = Number(element, "ry") * 2; node.X = Number(element, "cx") - node.Width / 2; node.Y = Number(element, "cy") - node.Height / 2; break;
                case "line": node.Kind = NodeKind.Path; node.Points = [new() { Position = new(Number(element, "x1"), Number(element, "y1")) }, new() { Position = new(Number(element, "x2"), Number(element, "y2")) }]; node.PathWidth = node.Width; node.PathHeight = node.Height; break;
                case "polygon": case "polyline": var numbers = Values(element.Attribute("points")?.Value); node.Kind = NodeKind.Path; node.Closed = kind == "polygon"; node.PathWidth = node.Width; node.PathHeight = node.Height; for (var i = 0; i + 1 < numbers.Length; i += 2) node.Points.Add(new() { Position = new(numbers[i], numbers[i + 1]) }); break;
                case "path": node.Kind = NodeKind.Path; node.PathData = element.Attribute("d")?.Value ?? ""; node.PathWidth = node.Width; node.PathHeight = node.Height; break;
                case "text": node.Kind = NodeKind.Text; node.Text = element.Value; node.FontSize = Number(element, "font-size", 16); node.FontFamily = element.Attribute("font-family")?.Value ?? "Inter"; node.FontWeight = (int)Number(element, "font-weight", 400); node.Y -= node.FontSize; node.Width = Math.Max(1, node.Text.Length * node.FontSize * .6); node.Height = node.FontSize * 1.3; break;
                default: warnings.Add($"{kind} elements were not imported."); return null;
            }
            string? Attribute(string key) => Inherited(element, key);
            node.FillRule = Attribute("fill-rule") == "evenodd" ? PathFillRule.EvenOdd : PathFillRule.NonZero;
            var fill = Attribute("fill") ?? "#000000";
            if (kind is not "g" and not "svg" && fill != "none")
            {
                var appearance = fill.StartsWith("url", StringComparison.OrdinalIgnoreCase) ? ReadGradient(fill, node, gradients, viewBox.Length == 4 ? viewBox[2] : width, viewBox.Length == 4 ? viewBox[3] : height) : new FillStyle { Color = fill == "currentColor" ? Attribute("color") ?? "#000000" : fill };
                appearance.Opacity = Math.Clamp(Scalar(Attribute("fill-opacity"), 1), 0, 1);
                node.Fills.Add(appearance);
            }
            var stroke = Attribute("stroke"); if (stroke is not null && stroke != "none") node.Strokes.Add(new() { Color = stroke, Width = Numbers.Parse(Attribute("stroke-width") ?? "1", 1), Opacity = Numbers.Parse(Attribute("stroke-opacity") ?? "1", 1), Cap = Enum.TryParse<StrokeCap>(Attribute("stroke-linecap"), true, out var cap) ? cap : StrokeCap.Butt, Join = Enum.TryParse<StrokeJoin>(Attribute("stroke-linejoin"), true, out var join) ? join : StrokeJoin.Miter, MiterLimit = Numbers.Parse(Attribute("stroke-miterlimit") ?? "4", 4), Dashes = (Attribute("stroke-dasharray") ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).Where(x => x != "none").Select(x => Numbers.Parse(x, 0)).Where(x => x > 0).ToList() });
            if (node.Strokes.Count > 0)
            {
                var appearance = node.Strokes[0];
                appearance.DashOffset = Scalar(Attribute("stroke-dashoffset"), 0);
                if (stroke!.StartsWith("url", StringComparison.OrdinalIgnoreCase))
                {
                    appearance.Paint = ReadGradient(stroke, node, gradients, viewBox.Length == 4 ? viewBox[2] : width, viewBox.Length == 4 ? viewBox[3] : height);
                    appearance.Color = appearance.Paint.Color;
                }
            }
            if (Own(element, "mix-blend-mode") is { } blend)
            {
                if (Enum.TryParse<BlendKind>(blend.Replace("-", ""), true, out var parsed) && Enum.IsDefined(parsed)) node.Blend = parsed;
                else warnings.Add("Unsupported SVG blend mode: " + blend);
            }
            node.Opacity = Math.Clamp(Scalar(Own(element, "opacity"), 1), 0, 1); node.Visible = Attribute("display") != "none" && Attribute("visibility") != "hidden";
            if (kind is "g" or "svg") foreach (var child in element.Elements()) { var c = Read(child, depth + 1); if (c is not null) node.Add(c); }
            var clipReference = element.Attribute("clip-path")?.Value ?? Style(element, "clip-path");
            if (!string.IsNullOrWhiteSpace(clipReference) && clipReference != "none")
            {
                var reference = Regex.Match(clipReference, "^url\\(\\s*['\"]?#([^'\"\\s)]+)['\"]?\\s*\\)$", RegexOptions.CultureInvariant);
                if (!reference.Success || !clips.TryGetValue(reference.Groups[1].Value, out var definition)) throw new InvalidDataException("Missing or external SVG clipping paths are not supported.");
                if ((definition.Attribute("clipPathUnits")?.Value ?? "userSpaceOnUse") != "userSpaceOnUse") throw new InvalidDataException("Only userSpaceOnUse SVG clipping paths are supported.");
                var shapes = definition.Elements().Where(e => e.Name.LocalName is not "title" and not "desc").ToArray();
                if (shapes.Length != 1 || shapes[0].Name.LocalName is not ("path" or "rect" or "circle" or "ellipse" or "polygon" or "polyline" or "line") || shapes[0].Attribute("clip-path") is not null || Style(shapes[0], "clip-path") is not null) throw new InvalidDataException("An SVG clipping definition must contain one vector shape or compound path.");
                var mask = Read(shapes[0], depth + 1) ?? throw new InvalidDataException("Invalid SVG clipping shape.");
                mask.FillRule = (shapes[0].Attribute("clip-rule")?.Value ?? Style(shapes[0], "clip-rule") ?? definition.Attribute("clip-rule")?.Value ?? "nonzero") == "evenodd" ? PathFillRule.EvenOdd : PathFillRule.NonZero;
                mask.Fills.Clear(); mask.Strokes.Clear(); mask.Shadows.Clear();
                if (definition.Attribute("transform") is { } clipTransform) NodeGeometry.SetExactMatrix(mask, mask.LocalMatrix * ParseTransform(clipTransform.Value));
                var wrapper = new DesignNode { Kind = NodeKind.Group, Name = node.Name + " / Clip Group", Width = node.Width, Height = node.Height, Fills = [], ClipPathId = mask.Id };
                wrapper.Opacity = node.Opacity; node.Opacity = 1; wrapper.Add(node); wrapper.Add(mask); node = wrapper;
            }
            var opacityReference = Own(element, "mask");
            if (!string.IsNullOrWhiteSpace(opacityReference) && opacityReference != "none")
            {
                var maskId = LocalReference(opacityReference);
                if (!opacityMasks.TryGetValue(maskId, out var definition) || !activeMasks.Add(maskId)) throw new InvalidDataException("Missing or cyclic SVG opacity mask.");
                try
                {
                    if ((Own(definition, "maskUnits") ?? "objectBoundingBox") != "userSpaceOnUse" || (Own(definition, "maskContentUnits") ?? "userSpaceOnUse") != "userSpaceOnUse") throw new InvalidDataException("Opacity-mask import currently requires userSpaceOnUse units.");
                    if (Inherited(definition, "color-interpolation") is { } interpolation && interpolation != "sRGB") throw new InvalidDataException("Only sRGB opacity masks are supported.");
                    if (definition.Descendants().Any(e => e.Name.LocalName is "script" or "foreignObject" or "image" or "use" or "style" || Own(e, "filter") is not null)) throw new InvalidDataException("Unsupported content in SVG opacity mask.");
                    var mask = new DesignNode { Kind = NodeKind.Group, Name = maskId + " / Mask", Width = width, Height = height, Fills = [] };
                    foreach (var child in definition.Elements()) { var source = Read(child, depth + 1); if (source is not null) mask.Add(source); }
                    var mode = Own(element, "mask-mode");
                    if (mode is null or "match-source") mode = Own(definition, "mask-type") ?? "luminance";
                    var region = new RectD(Scalar(Own(definition, "x"), -.1 * width, width), Scalar(Own(definition, "y"), -.1 * height, height), Scalar(Own(definition, "width"), 1.2 * width, width), Scalar(Own(definition, "height"), 1.2 * height, height));
                    if (region.Width <= 0 || region.Height <= 0) throw new InvalidDataException("Empty SVG mask region.");
                    var wrapper = new DesignNode { Kind = NodeKind.Group, Name = node.Name + " / Opacity Mask", Width = node.Width, Height = node.Height, Fills = [], OpacityMaskId = mask.Id, OpacityMaskRegion = region, OpacityMaskMode = mode switch { "alpha" => OpacityMaskMode.Alpha, "luminance" => OpacityMaskMode.Luminance, _ => throw new InvalidDataException("Unsupported SVG mask mode.") }, Opacity = node.Opacity };
                    node.Opacity = 1; wrapper.Add(node); wrapper.Add(mask); node = wrapper;
                }
                finally { activeMasks.Remove(maskId); }
            }
            if (element.Attribute("transform") is { } attribute) NodeGeometry.SetExactMatrix(node, node.LocalMatrix * ParseTransform(attribute.Value));
            return node;
        }
    }
    private static string? Style(XElement element, string key) => element.Attribute("style")?.Value.Split(';').Select(x => x.Split(':', 2)).FirstOrDefault(x => x.Length == 2 && x[0].Trim() == key)?[1].Trim();
    private static double Number(XElement element, string name, double fallback = 0) => Values(element.Attribute(name)?.Value).FirstOrDefault(fallback);
    private static double[] Values(string? text) => text is null ? [] : NumberRegex().Matches(text).Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
    public static Matrix2D ParseTransform(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = Matrix2D.Identity; var end = 0;
        foreach (Match match in TransformRegex().Matches(text))
        {
            if (text[end..match.Index].Any(c => !char.IsWhiteSpace(c) && c != ',')) throw new InvalidDataException("Unsupported SVG transform syntax.");
            var arguments = match.Groups[2].Value;
            if (NumberRegex().Replace(arguments, "").Any(c => !char.IsWhiteSpace(c) && c != ',')) throw new InvalidDataException("Invalid SVG transform arguments.");
            var v = Values(arguments);
            var matrix = match.Groups[1].Value switch
            {
                "matrix" when v.Length == 6 => new Matrix2D(v[0], v[1], v[2], v[3], v[4], v[5]),
                "translate" when v.Length is 1 or 2 => Matrix2D.Translation(v[0], v.Length == 2 ? v[1] : 0),
                "scale" when v.Length is 1 or 2 => Matrix2D.Scale(v[0], v.Length == 2 ? v[1] : v[0]),
                "rotate" when v.Length == 1 => Matrix2D.Rotation(v[0]),
                "rotate" when v.Length == 3 => Matrix2D.Translation(-v[1], -v[2]) * Matrix2D.Rotation(v[0]) * Matrix2D.Translation(v[1], v[2]),
                "skewX" when v.Length == 1 => new Matrix2D(1, 0, Math.Tan(v[0] * Math.PI / 180), 1, 0, 0),
                "skewY" when v.Length == 1 => new Matrix2D(1, Math.Tan(v[0] * Math.PI / 180), 0, 1, 0, 0),
                _ => throw new InvalidDataException("Unsupported SVG transform or invalid argument count.")
            };
            result = matrix * result; end = match.Index + match.Length;
            if (!AffineGeometry.IsInvertible(result)) throw new InvalidDataException("Singular or nonfinite SVG transforms are not supported.");
        }
        if (text[end..].Any(c => !char.IsWhiteSpace(c) && c != ',')) throw new InvalidDataException("Unsupported SVG transform syntax.");
        return result;
    }
    private static string SvgBlendName(BlendKind blend) => blend switch
    {
        BlendKind.ColorDodge => "color-dodge", BlendKind.ColorBurn => "color-burn",
        BlendKind.HardLight => "hard-light", BlendKind.SoftLight => "soft-light",
        _ => blend.ToString().ToLowerInvariant()
    };
    private static string Transform(Matrix2D m) => $"matrix({F(m.M11)} {F(m.M12)} {F(m.M21)} {F(m.M22)} {F(m.DX)} {F(m.DY)})";
    private static string F(double number) => number.ToString("G17", CultureInfo.InvariantCulture);
    [GeneratedRegex(@"[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?")] private static partial Regex NumberRegex();
    [GeneratedRegex(@"(matrix|translate|scale|rotate|skewX|skewY)\s*\(([^)]*)\)")] private static partial Regex TransformRegex();
}
