from pathlib import Path
import re

def patch(path, old, new):
    p = Path(path); text = p.read_text()
    if old not in text: raise RuntimeError('Missing integration anchor: ' + path + ': ' + old[:100])
    p.write_text(text.replace(old, new))

# Native model and backward-compatible schema guard.
patch('src/ArtSpace.Core/Document.cs', 'public sealed class GradientStop\n{', 'public sealed class GradientStop\n{\n    public double Opacity { get; set; } = 1;')
patch('src/ArtSpace.Core/Document.cs', 'public sealed class FillStyle\n{', '''public sealed class FillStyle
{
    public GradientSpace GradientSpace { get; set; }
    public GradientSpread GradientSpread { get; set; }
    public Matrix2D GradientTransform { get; set; } = Matrix2D.Identity;
    public double GradientRadius { get; set; } = .5;
    public Vec2? GradientFocus { get; set; }''')
patch('src/ArtSpace.Core/Document.cs', '    public bool Expanded { get; set; } = true;', '''    public string? OpacityMaskId { get; set; }
    public OpacityMaskMode OpacityMaskMode { get; set; }
    public bool OpacityMaskEnabled { get; set; } = true;
    public bool OpacityMaskInverted { get; set; }
    public RectD? OpacityMaskRegion { get; set; }
    [JsonIgnore] public DesignNode? OpacityMask => OpacityMaskId is null ? null : Children.Find(n => n.Id == OpacityMaskId);
    /// <summary>Residual affine transform before editable placement; preserves imported skew and group scale.</summary>
    public Matrix2D? AffineTransform { get; set; }
    public bool Expanded { get; set; } = true;''')
patch('src/ArtSpace.Core/Document.cs', '    [JsonIgnore] public Matrix2D LocalMatrix => Matrix2D.Translation', '    [JsonIgnore] public Matrix2D PlacementMatrix => Matrix2D.Translation')
patch('src/ArtSpace.Core/Document.cs', '    [JsonIgnore] public Matrix2D WorldMatrix', '    [JsonIgnore] public Matrix2D LocalMatrix => (AffineTransform ?? Matrix2D.Identity) * PlacementMatrix;\n    [JsonIgnore] public Matrix2D WorldMatrix')
p=Path('src/ArtSpace.Core/Document.cs'); text=p.read_text(); start=text.index('public static class NodeGeometry')
p.write_text(text[:start]+'''public static class NodeGeometry
{
    /// <summary>Preserves geometry coordinates, including skew and nested group scaling, without decomposition loss.</summary>
    public static void SetExactMatrix(DesignNode node, Matrix2D matrix)
    {
        if (!AffineGeometry.IsInvertible(matrix)) throw new InvalidOperationException("A transform must be finite and invertible.");
        node.AffineTransform = matrix * node.PlacementMatrix.Inverse;
    }
    /// <summary>Re-expresses editable placement and retains any affine residual that cannot be represented by rotation/size.</summary>
    public static void SetLocalMatrix(DesignNode node, Matrix2D matrix)
    {
        if (!AffineGeometry.IsInvertible(matrix)) throw new InvalidOperationException("A transform must be finite and invertible.");
        var oldWidth = node.Width; var oldHeight = node.Height;
        var center = matrix.Map(new Vec2(oldWidth / 2, oldHeight / 2));
        var sx = Math.Sqrt(matrix.M11 * matrix.M11 + matrix.M12 * matrix.M12);
        var determinant = matrix.M11 * matrix.M22 - matrix.M12 * matrix.M21;
        var sy = determinant / sx;
        if (!node.IsContainer)
        {
            node.Width *= Math.Max(.0001, sx); node.Height *= Math.Max(.0001, Math.Abs(sy));
            matrix = Matrix2D.Scale(oldWidth > 0 ? oldWidth / Math.Max(1e-12, node.Width) : 1, oldHeight > 0 ? oldHeight / Math.Max(1e-12, node.Height) : 1) * matrix;
        }
        node.FlipX = false; node.FlipY = sy < 0;
        node.Rotation = Math.Atan2(matrix.M12, matrix.M11) * 180 / Math.PI;
        node.X = center.X - node.Width / 2; node.Y = center.Y - node.Height / 2;
        node.AffineTransform = matrix * node.PlacementMatrix.Inverse;
    }
}
''')
patch('src/ArtSpace.Documents/DocumentJson.cs','public const int CurrentFormatVersion = 2;', 'public const int CurrentFormatVersion = 3;')
patch('src/ArtSpace.Documents/DocumentJson.cs','Save using schema 2.', 'Save using schema 3.')
patch('src/ArtSpace.Documents/DocumentJson.cs','            node.Id = ids[node.Id];','''            node.Id = ids[node.Id];
            if (node.OpacityMaskId is { } opacity && ids.TryGetValue(opacity, out var opacityReplacement)) node.OpacityMaskId = opacityReplacement;''')
patch('src/ArtSpace.Documents/DocumentJson.cs','            if (!Enum.IsDefined(n.FillRule))','''            if (n.AffineTransform is { } affine && !AffineGeometry.IsInvertible(affine)) throw new InvalidDataException("Invalid affine transform.");
            if (!Enum.IsDefined(n.OpacityMaskMode)) throw new InvalidDataException("Invalid opacity mask mode.");
            if (n.OpacityMaskId is { } opacityId)
            {
                var source = n.Children.Find(c => c?.Id == opacityId);
                if (!n.IsContainer || source is null || source.Kind == NodeKind.Slice || opacityId == n.ClipPathId)
                    throw new InvalidDataException("An opacity mask must reference a distinct renderable direct child.");
            }
            if (n.OpacityMaskRegion is { } region && (!double.IsFinite(region.X) || !double.IsFinite(region.Y) || !double.IsFinite(region.Right) || !double.IsFinite(region.Bottom) || region.Width <= 0 || region.Height <= 0))
                throw new InvalidDataException("Invalid opacity mask region.");
            foreach (var fill in n.Fills)
            {
                if (fill is null || fill.Stops is null || fill.Stops.Count > 4096 || !double.IsFinite(fill.Opacity) || !fill.Start.IsFinite || !fill.End.IsFinite || !double.IsFinite(fill.GradientRadius) || fill.GradientRadius < 0 || !Enum.IsDefined(fill.GradientSpace) || !Enum.IsDefined(fill.GradientSpread) || !AffineGeometry.IsInvertible(fill.GradientTransform) || (fill.GradientFocus.HasValue && !fill.GradientFocus.Value.IsFinite) || fill.Stops.Any(s => s is null || !double.IsFinite(s.Offset) || !double.IsFinite(s.Opacity)))
                    throw new InvalidDataException("Invalid gradient appearance.");
            }
            if (!Enum.IsDefined(n.FillRule))''')
patch('src/ArtSpace.Editing/ComponentService.cs','            instance.ClipPathId = copy.ClipPathId;','''            foreach (var n in copy.DescendantsAndSelf())
                if (n.OpacityMaskId is { } maskId && remapped.TryGetValue(maskId, out var newMaskId)) n.OpacityMaskId = newMaskId;
            instance.OpacityMaskId = copy.OpacityMaskId;
            instance.OpacityMaskMode = copy.OpacityMaskMode;
            instance.OpacityMaskEnabled = copy.OpacityMaskEnabled;
            instance.OpacityMaskInverted = copy.OpacityMaskInverted;
            instance.OpacityMaskRegion = copy.OpacityMaskRegion;
            instance.ClipPathId = copy.ClipPathId;''')
patch('src/ArtSpace.Editing/EditorSession.cs','        if (parent?.ClipPathId == node.Id) parent.ClipPathId = null;', '        if (parent?.ClipPathId == node.Id) parent.ClipPathId = null;\n        if (parent?.OpacityMaskId == node.Id) parent.OpacityMaskId = null;')
patch('src/ArtSpace.Editing/EditorSession.cs','nodes.Any(n => n.Parent?.ClipPathId == n.Id)', 'nodes.Any(n => n.Parent?.ClipPathId == n.Id || n.Parent?.OpacityMaskId == n.Id)')
patch('src/ArtSpace.Illustration/ClippingOperations.cs', '        var siblings = parent?.Children ?? editor.Page.Nodes;', '''        if (nodes.Any(n => parent?.OpacityMaskId == n.Id)) throw new InvalidOperationException("Release the opacity mask before regrouping its source.");
        var siblings = parent?.Children ?? editor.Page.Nodes;''')
# Preserve cache ownership and compose masks into an isolated group before applying group opacity/effects.
p=Path('src/ArtSpace.Skia/SceneRenderer.cs'); text=p.read_text(); start=text.index('    private static SKShader? Shader('); end=text.index('    private SKTypeface Typeface(',start); text=text[:start]+text[end:]; p.write_text(text)
patch('src/ArtSpace.Skia/SceneRenderer.cs', '        var retained = roots.SelectMany(n => n.DescendantsAndSelf()).Select(n => n.Id).ToHashSet(StringComparer.Ordinal);', '''        var nodes = roots.SelectMany(n => n.DescendantsAndSelf()).ToArray();
        PruneGradients(nodes);
        var retained = nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);''')
patch('src/ArtSpace.Skia/SceneRenderer.cs','_paths.Clear(); ClearTextLayouts();', '_paths.Clear(); ClearTextLayouts(); ClearGradients();')
patch('src/ArtSpace.Skia/SceneRenderer.cs','        var layer = node.Opacity < .999', '''        var masked = !Outlines && node.OpacityMaskEnabled && node.OpacityMask is not null;
        if (masked && node.OpacityMaskRegion is { } region) canvas.ClipRect(Rect(region));
        var layer = masked || node.Opacity < .999''')
patch('src/ArtSpace.Skia/SceneRenderer.cs','                using var shader = Shader(fill, node.Width, node.Height); paint.Shader = shader;', '''                var shader = Shader(fill, node); paint.Shader = shader;
                if (shader is not null) paint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(Math.Round(fill.Opacity * 255), 0, 255));''')
patch('src/ArtSpace.Skia/SceneRenderer.cs','        if (node.ClipContent && !Outlines)', '        canvas.Save();\n        if (node.ClipContent && !Outlines)')
patch('src/ArtSpace.Skia/SceneRenderer.cs','            if (Outlines || child.Id != node.ClipPathId) DrawNode(canvas, child, filtered);', '''            if (Outlines || (child.Id != node.ClipPathId && child.Id != node.OpacityMaskId)) DrawNode(canvas, child, filtered);
        canvas.Restore();
        if (masked) ApplyOpacityMask(canvas, node);''')
patch('src/ArtSpace.Skia/SceneRenderer.cs','            if (node.ClippingPath is { } mask)', '            if (!Outlines && node.OpacityMaskEnabled && node.OpacityMask is not null && MaskCoverageAt(node, local) <= 1d / 255) continue;\n            if (node.ClippingPath is { } mask)')
patch('src/ArtSpace.Skia/SceneRenderer.cs','node.Children.Where(c => c.Id != node.ClipPathId)', 'node.Children.Where(c => c.Id != node.ClipPathId && c.Id != node.OpacityMaskId)')
patch('src/ArtSpace.Skia/SceneRenderer.cs','        ClearCache(); foreach (var face', '        ClearCache(); DisposeMasks(); foreach (var face')
# SVG uses exact matrices rather than scaling only a group's nominal width/height.
p=Path('src/ArtSpace.Documents/SvgFormat.cs'); text=p.read_text(); text=text.replace('NodeGeometry.SetLocalMatrix(', 'NodeGeometry.SetExactMatrix(')
a=text.index('                var gradient = new XElement('); b=text.index('                defs.Add(gradient);',a)
text=text[:a]+'                var gradient = ExportGradient(fill, node, id);\n'+text[b:]
text=text.replace('if (child.Id != node.ClipPathId) contentTarget.Add', 'if (child.Id != node.ClipPathId && child.Id != node.OpacityMaskId) contentTarget.Add')
text=text.replace('        if (children.HasElements) group.Add(children); return group;', '''        if (children.HasElements) group.Add(children);
        if (node.OpacityMaskId is not null && node.OpacityMaskEnabled)
        {
            var definition = ExportOpacityMask(node, defs); defs.Add(definition);
            var masked = new XElement(Ns + "g", new XAttribute("mask", "url(#" + definition.Attribute("id")!.Value + ")"));
            var artwork = group.Elements().ToArray(); foreach (var element in artwork) { element.Remove(); masked.Add(element); }
            group.Add(masked);
        }
        return group;''')
text=text.replace('        var count = 0;', '''        var gradients = Definitions(root, "linearGradient", "radialGradient");
        var opacityMasks = Definitions(root, "mask");
        var activeMasks = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;''')
text=text.replace('string? Attribute(string key) => element.Attribute(key)?.Value ?? Style(element, key) ?? element.Ancestors().Select(a => a.Attribute(key)?.Value ?? Style(a, key)).FirstOrDefault(v => v is not null);', 'string? Attribute(string key) => Inherited(element, key);')
text=text.replace('''                if (fill.StartsWith("url", StringComparison.OrdinalIgnoreCase)) { warnings.Add("Referenced paint servers currently import as a solid fill."); fill = "#A78BFA"; }
                node.Fills.Add(new() { Color = fill, Opacity = Numbers.Parse(Attribute("fill-opacity") ?? "1", 1) });''','''                var appearance = fill.StartsWith("url", StringComparison.OrdinalIgnoreCase) ? ReadGradient(fill, node, gradients, viewBox.Length == 4 ? viewBox[2] : width, viewBox.Length == 4 ? viewBox[3] : height) : new FillStyle { Color = fill == "currentColor" ? Attribute("color") ?? "#000000" : fill };
                appearance.Opacity = Math.Clamp(Scalar(Attribute("fill-opacity"), 1), 0, 1);
                node.Fills.Add(appearance);''')
text=text.replace('node.Opacity = Number(element, "opacity", 1);', 'node.Opacity = Math.Clamp(Scalar(Own(element, "opacity"), 1), 0, 1);')
needle='            if (element.Attribute("transform") is { } attribute) NodeGeometry.SetExactMatrix(node, node.LocalMatrix * ParseTransform(attribute.Value));'
assert needle in text
text=text.replace(needle, '''            var opacityReference = Own(element, "mask");
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
'''+needle)
p.write_text(text)
# UI commands are inserted before clipping commands to preserve established menu ordering.
patch('src/ArtSpace.Workbench/StudioWorkbench.Illustration.cs','                yield return Item("Make Clipping Mask",', '''                yield return Item("Make Opacity Mask", () => OpacityMaskOperations.Make(Session), enabled: selected);
                yield return Item("Release Opacity Mask", () => OpacityMaskOperations.Release(Session), enabled: selected);
                yield return Item("Edit Opacity Mask", () => OpacityMaskOperations.EditMask(Session), enabled: selected);
                yield return Item("Edit Masked Artwork", () => OpacityMaskOperations.EditContents(Session), enabled: selected);
                yield return Item("Invert Opacity Mask", () => OpacityMaskOperations.Invert(Session), enabled: selected);
                yield return Item("Enable / Disable Opacity Mask", () => OpacityMaskOperations.ToggleEnabled(Session), enabled: selected);
                yield return Item("Make Clipping Mask",''')
patch('src/ArtSpace.Workbench/StudioWorkbench.Illustration.cs','    private void AddIllustrationSections()\n    {','''    private void AddIllustrationSections()
    {
        var transparency = AddSection("Transparency");
        if (OpacityMaskOperations.FindOwner(Session.Primary) is { } owner)
        {
            transparency.Body.Children.Add(Studio.Choice(Enum.GetNames<OpacityMaskMode>(), owner.OpacityMaskMode.ToString(), value => Run(() => OpacityMaskOperations.SetMode(Session, Enum.Parse<OpacityMaskMode>(value))), "Opacity mask mode"));
            transparency.Body.Children.Add(new StudioButton("Edit Mask Artwork", () => Run(() => OpacityMaskOperations.EditMask(Session))));
            transparency.Body.Children.Add(new StudioButton("Edit Masked Artwork", () => Run(() => OpacityMaskOperations.EditContents(Session))));
            transparency.Body.Children.Add(new StudioButton(owner.OpacityMaskInverted ? "Invert Mask: On" : "Invert Mask: Off", () => Run(() => OpacityMaskOperations.Invert(Session))));
            transparency.Body.Children.Add(new StudioButton(owner.OpacityMaskEnabled ? "Disable Opacity Mask" : "Enable Opacity Mask", () => Run(() => OpacityMaskOperations.ToggleEnabled(Session))));
            transparency.Body.Children.Add(new StudioButton("Release Opacity Mask", () => Run(() => OpacityMaskOperations.Release(Session))));
        }
        else transparency.Body.Children.Add(new StudioButton("Make Opacity Mask", () => Run(() => OpacityMaskOperations.Make(Session))) { IsEnabled = Session.Selection.Count > 0 });''')
patch('src/ArtSpace.Editor/DesignSurface.cs', 'n.Parent?.ClipPathId != n.Id &&', 'n.Parent?.ClipPathId != n.Id && n.Parent?.OpacityMaskId != n.Id &&')
patch('src/ArtSpace.App/Platforms/WebAssembly/BrowserWorkspaceStorage.cs','                json.WriteString("clipPathId", primary?.ClipPathId);', '''                json.WriteString("clipPathId", primary?.ClipPathId);
                json.WriteString("opacityMaskId", primary?.OpacityMaskId);
                json.WriteString("opacityMaskMode", primary?.OpacityMaskMode.ToString());
                json.WriteBoolean("opacityMaskInverted", primary?.OpacityMaskInverted ?? false);
                json.WriteBoolean("opacityMaskEnabled", primary?.OpacityMaskEnabled ?? false);
                json.WriteNumber("opacityMasks", session.Page.AllNodes().Count(n => n.OpacityMaskId is not null));
                json.WriteNumber("gradientBuilds", workbench.Surface.Renderer.GradientBuilds);''')
# Adapt the version compatibility preflight to the new schema without removing its legacy-read assertion.
p=Path('tests/ArtSpace.Tests/PerformanceBenchmarks.cs'); text=p.read_text().replace('\\"formatVersion\\":2','\\"formatVersion\\":3').replace('migrated.FormatVersion == 2','migrated.FormatVersion == 3').replace('schema 2','schema 3'); p.write_text(text)
patch('tests/ArtSpace.Tests/Program.cs','ClippingPerformanceTests.Register(Test);','ClippingPerformanceTests.Register(Test);\nAppearanceTests.Register(Test);')
patch('Directory.Build.props','0.3.0-alpha.1','0.4.0-alpha.1')
patch('src/ArtSpace.App/ArtSpace.App.csproj','<ApplicationDisplayVersion>0.3.0</ApplicationDisplayVersion>','<ApplicationDisplayVersion>0.4.0</ApplicationDisplayVersion>')
patch('src/ArtSpace.App/ArtSpace.App.csproj','<ApplicationVersion>3</ApplicationVersion>','<ApplicationVersion>4</ApplicationVersion>')
print('Appearance integration complete.')
