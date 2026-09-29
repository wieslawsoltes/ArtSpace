from pathlib import Path
import subprocess


def replace(path, old, new, count=1):
    p = Path(path)
    text = p.read_text()
    assert text.count(old) == count, (path, old[:100], text.count(old), count)
    p.write_text(text.replace(old, new))


for path, sha in {
    'src/ArtSpace.Core/Document.cs': '957b36f5510a14df7f2d10cda6c9d346798da97c',
    'src/ArtSpace.Documents/DocumentJson.cs': 'e8f87037cf974df7cf1887fe746c0474c834563e',
    'src/ArtSpace.Skia/SceneRenderer.cs': '348d22d012cac377700158ee11b50274763069f0',
    'src/ArtSpace.Editing/ComponentService.cs': '8ef194b0b7543ccf5f565d378a0b9d9b0a418f0b',
    'src/ArtSpace.Skia/SceneRenderer.Gradients.cs': '67199f4bccf046d210e95e3012d9732a94204c6c',
}.items():
    assert subprocess.check_output(['git', 'hash-object', path], text=True).strip() == sha, path

p = 'src/ArtSpace.Core/Document.cs'
replace(p, 'public enum BlendKind { Normal, Multiply, Screen, Overlay, Darken, Lighten, Difference }',
    'public enum BlendKind { Normal, Multiply, Screen, Overlay, Darken, Lighten, Difference, ColorDodge, ColorBurn, HardLight, SoftLight, Exclusion, Hue, Saturation, Color, Luminosity }')
replace(p, '    public List<double> Dashes { get; set; } = [];',
    '    public List<double> Dashes { get; set; } = [];\n    public double DashOffset { get; set; }\n    public FillStyle? Paint { get; set; }')
replace(p, '    public List<ShadowStyle> Shadows { get; set; } = [];',
    '    public List<ShadowStyle> Shadows { get; set; } = [];\n    public List<LiveEffect> Effects { get; set; } = [];')
replace(p, '    public bool? Visible { get; set; }',
    '    public bool? Visible { get; set; }\n    public GraphicStyle? Appearance { get; set; }')
replace(p, '    public Dictionary<string, string> ColorStyles { get; set; } = [];',
    '    public Dictionary<string, string> ColorStyles { get; set; } = [];\n    public List<GraphicStyle> GraphicStyles { get; set; } = [];')

p = 'src/ArtSpace.Documents/DocumentJson.cs'
replace(p, 'public static class DocumentJson', 'public static partial class DocumentJson')
replace(p, 'public const int CurrentFormatVersion = 3;', 'public const int CurrentFormatVersion = 4;')
replace(p, 'Save using schema 3. Legacy schema 1 is upgraded so older readers cannot silently discard clipping semantics.',
    'Save using schema 4. Older documents upgrade so earlier readers cannot silently discard live appearance semantics.')
replace(p, '        var ids = new HashSet<string>(StringComparer.Ordinal); var count = 0;',
    '        ValidateGraphicStyles(document);\n        var ids = new HashSet<string>(StringComparer.Ordinal); var count = 0;')
replace(p, '            if (n.ClipPathId is { } clip)',
    '            ValidateLiveAppearance(n);\n            if (n.ClipPathId is { } clip)')
replace(p, '            foreach (var fill in n.Fills)\n            {',
    '            foreach (var fill in n.Fills)\n            {\n                if (fill is not null) ValidatePaint(fill);')
replace(p, 'stroke is null || !double.IsFinite(stroke.Width)',
    'stroke is null || !double.IsFinite(stroke.Opacity) || !double.IsFinite(stroke.Width)')

p = 'src/ArtSpace.Documents/DocumentJson.Appearance.cs'
replace(p, '        foreach (var stroke in node.Strokes)', '''        foreach (var entry in node.Overrides.Values)
        {
            if (entry is null) throw new InvalidDataException("Invalid symbol override.");
            if (entry.Appearance is { } appearance)
                Validate(new DesignDocument { Pages = [new() { Nodes = [new()
                {
                    Fills = appearance.Fills, Strokes = appearance.Strokes, Shadows = appearance.Shadows,
                    Effects = appearance.Effects, Opacity = appearance.Opacity, Blend = appearance.Blend
                }] }] });
        }
        foreach (var stroke in node.Strokes)''')

p = 'src/ArtSpace.Editing/EditorSession.cs'
replace(p, '        ComponentService.Synchronize(Document);',
    '        DocumentJson.Validate(Document);\n        ComponentService.Synchronize(Document);')
replace(p, '        var before = _before; var after = Capture(); _before = null;',
    '        DocumentJson.Validate(Document);\n        var before = _before; var after = Capture(); _before = null;')

p = 'src/ArtSpace.Editing/ComponentService.cs'
replace(p, '    public static void Synchronize(DesignDocument document)', '''    public static void SetAppearanceOverride(DesignNode node)
    {
        var instance = node;
        while (instance is not null && instance.Kind != NodeKind.Instance) instance = instance.Parent;
        if (instance is null || node.SourceId is null) return;
        if (!instance.Overrides.TryGetValue(node.SourceId, out var value)) instance.Overrides[node.SourceId] = value = new();
        value.Appearance = GraphicStyle.Capture(node, "Appearance override");
        value.Fill = null;
    }
    public static void Synchronize(DesignDocument document)''')
replace(p, 'if (instance.Overrides.TryGetValue(source, out var o)) { if (o.Text is not null)',
    'if (instance.Overrides.TryGetValue(source, out var o)) { o.Appearance?.ApplyTo(n); if (o.Text is not null)')
replace(p, 'instance.Shadows = copy.Shadows; instance.CornerRadius',
    'instance.Shadows = copy.Shadows; instance.Effects = copy.Effects; instance.CornerRadius')
replace(p, '            instance.Layout = copy.Layout;', '''            instance.Layout = copy.Layout;
            if (instance.SourceId is { } rootSource && instance.Overrides.TryGetValue(rootSource, out var rootOverride)
                && rootOverride.Appearance is not null)
            { instance.Opacity = copy.Opacity; instance.Blend = copy.Blend; }''')

p = 'src/ArtSpace.Illustration/AppearanceOperations.cs'
s = Path(p).read_text().replace('session.UpdateSelection(', 'UpdateSelection(session, ')
s = s.replace('    private static GraphicStyle FindStyle', '''    private static void UpdateSelection(EditorSession session, string label, Action<DesignNode> update) =>
        session.UpdateSelection(label, node => { update(node); ComponentService.SetAppearanceOverride(node); });

    private static GraphicStyle FindStyle''')
Path(p).write_text(s)

p = 'src/ArtSpace.Skia/SceneRenderer.cs'
replace(p, '        PruneGradients(nodes);', '        InvalidateRetainedScene(); PrunePaints(nodes); PruneGradients(nodes);')
replace(p, '        var retained = nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);',
    '        var retained = nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);\n        PruneEffects(retained);')
replace(p, '        ClearTextLayouts(); _customTypeface?.Dispose();',
    '        InvalidateRetainedScene(); ClearTextLayouts(); _customTypeface?.Dispose();')
replace(p, '        foreach (var p in _paths.Values) p.Path.Dispose();',
    '        InvalidateRetainedScene(); ClearPaints(); ClearEffects();\n        foreach (var p in _paths.Values) p.Path.Dispose();')
replace(p, 'var filtered = filteredAncestor || node.Shadows.Any(s => s.Visible);',
    'var filtered = filteredAncestor || node.Shadows.Any(s => s.Visible) || HasVisibleEffects(node);')
replace(p, '''        var layer = masked || node.Opacity < .999 || node.Blend != BlendKind.Normal || node.Shadows.Any(s => s.Visible);
        if (layer)
        {
            using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Clamp(node.Opacity * 255, 0, 255)), BlendMode = Blend(node.Blend) };
            var shadow = node.Shadows.FirstOrDefault(s => s.Visible);
            using var filter = shadow is null ? null : SKImageFilter.CreateDropShadow((float)shadow.X, (float)shadow.Y, (float)Math.Clamp(shadow.Blur / 2, 0, 256), (float)Math.Clamp(shadow.Blur / 2, 0, 256), Color(shadow.Color, shadow.Opacity));
            paint.ImageFilter = filter; canvas.SaveLayer(paint);
        }''', '''        var filter = Outlines ? null : EffectFilter(node);
        var layer = masked || node.Opacity < .999 || node.Blend != BlendKind.Normal || filter is not null;
        if (layer)
        {
            using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Clamp(node.Opacity * 255, 0, 255)), BlendMode = Blend(node.Blend), ImageFilter = filter };
            canvas.SaveLayer(paint);
        }''')
replace(p, '''            foreach (var fill in node.Fills.Where(f => f.Visible))
            {
                using var paint = new SKPaint { IsAntialias = true, Color = Color(fill.Color, fill.Opacity), Style = SKPaintStyle.Fill };
                var shader = Shader(fill, node); paint.Shader = shader;
                if (shader is not null) paint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(Math.Round(fill.Opacity * 255), 0, 255));''', '''            foreach (var fill in node.Fills)
            {
                if (!fill.Visible) continue;
                var paint = FillPaint(fill, node);''')
replace(p, '''            foreach (var stroke in node.Strokes.Where(s => s.Visible && s.Width > 0))
            {
                using var paint = new SKPaint { IsAntialias = true, Color = Color(stroke.Color, stroke.Opacity), Style = SKPaintStyle.Stroke, StrokeWidth = (float)stroke.Width, StrokeCap = (SKStrokeCap)stroke.Cap, StrokeJoin = (SKStrokeJoin)stroke.Join, StrokeMiter = (float)stroke.MiterLimit };
                using var dash = stroke.Dashes.Count >= 2 && stroke.Dashes.All(d => d > 0) ? SKPathEffect.CreateDash(stroke.Dashes.Select(d => (float)d).ToArray(), 0) : null; paint.PathEffect = dash;''', '''            foreach (var stroke in node.Strokes)
            {
                if (!stroke.Visible || stroke.Width <= 0 || stroke.Paint?.Visible == false) continue;
                var paint = StrokePaint(stroke, node);''')
replace(p, 'BlendKind.Difference => SKBlendMode.Difference, _ => SKBlendMode.SrcOver',
    'BlendKind.Difference => SKBlendMode.Difference, ' + ', '.join('BlendKind.' + name + ' => SKBlendMode.' + name for name in ['ColorDodge', 'ColorBurn', 'HardLight', 'SoftLight', 'Exclusion', 'Hue', 'Saturation', 'Color', 'Luminosity']) + ', _ => SKBlendMode.SrcOver')

p = 'src/ArtSpace.Skia/SceneRenderer.Gradients.cs'
replace(p, 'nodes.SelectMany(n => n.Fills).ToHashSet(ReferenceEqualityComparer.Instance)',
    'nodes.SelectMany(n => n.Fills.Concat(n.Strokes.Select(s => s.Paint).OfType<FillStyle>())).ToHashSet(ReferenceEqualityComparer.Instance)')

p = 'src/ArtSpace.Skia/PathEditing.cs'
replace(p, '        var gradients = basis.Fills.Select(',
    '        var paints = node.Fills.Concat(node.Strokes.Select(s => s.Paint).OfType<FillStyle>()).ToArray();\n        var gradients = basis.Fills.Concat(basis.Strokes.Select(s => s.Paint).OfType<FillStyle>()).Select(')
replace(p, 'Math.Min(node.Fills.Count, gradients.Length)', 'Math.Min(paints.Length, gradients.Length)')
replace(p, '            if (node.Fills[i].Kind == FillKind.Solid)', '            if (paints[i].Kind == FillKind.Solid)')
replace(p, '            var fill = node.Fills[i];', '            var fill = paints[i];')

p = 'src/ArtSpace.Illustration/IllustrationOperations.cs'
replace(p, 'Fills = [], Shadows = node.Shadows', 'Fills = [], Shadows = node.Shadows, Effects = node.Effects, AffineTransform = node.AffineTransform')
replace(p, 'fill.Shadows = []; fill.Strokes = [];', 'fill.Shadows = []; fill.Effects = []; fill.AffineTransform = null; fill.Strokes = [];')
replace(p, 's.Visible && s.Width > 0', 's.Visible && s.Width > 0 && s.Paint?.Visible != false', 2)
replace(p, '''                    outlined.Opacity = stroke.Opacity;
                    group.Add(outlined);''', '''                    outlined.Opacity = stroke.Opacity;
                    if (stroke.Paint is { } sourcePaint)
                    {
                        var fill = GraphicStyle.CloneFill(sourcePaint);
                        if (fill.Kind != FillKind.Solid)
                        {
                            if (fill.GradientSpace == GradientSpace.Legacy)
                            {
                                fill.Start = new(fill.Start.X * node.Width, fill.Start.Y * node.Height);
                                fill.End = new(fill.End.X * node.Width, fill.End.Y * node.Height);
                                fill.GradientFocus = fill.Start; fill.GradientRadius = Math.Max(1, fill.Start.DistanceTo(fill.End));
                            }
                            else fill.GradientTransform = renderer.GradientCoordinateMatrix(node, sourcePaint);
                            fill.GradientSpace = GradientSpace.UserSpaceOnUse;
                            fill.GradientTransform *= Matrix2D.Translation(-outlined.X, -outlined.Y);
                        }
                        outlined.Fills = [fill];
                    }
                    group.Add(outlined);''')
replace(p, '''        if (stroke.Dashes.Count > 0)
        {
            var dashes = stroke.Dashes.Select(x => (float)Math.Max(.01, x)).ToArray();
            if (dashes.Length % 2 != 0) dashes = [.. dashes, .. dashes];
            paint.PathEffect = SKPathEffect.CreateDash(dashes, 0);
        }''', '''        using var dash = SceneRenderer.CreateStrokeDash(stroke);
        paint.PathEffect = dash;''')

p = 'src/ArtSpace.Documents/SvgFormat.cs'
replace(p, '        if (!node.Visible || node.Kind == NodeKind.Slice) return null;',
    '        if (!node.Visible || node.Kind == NodeKind.Slice) return null;\n        if (node.Effects.Any(effect => effect.Enabled)) throw new InvalidOperationException("Live effects require native or PNG export; SVG filter interchange is not yet supported.");')
replace(p, 'node.Blend.ToString().ToLowerInvariant()', 'SvgBlendName(node.Blend)')
replace(p, '''        foreach (var stroke in node.Strokes.Where(s => s.Visible))
        {
            var shape = Shape(node); shape.SetAttributeValue("fill", "none"); shape.SetAttributeValue("stroke", stroke.Color);''', '''        for (var strokeIndex = 0; strokeIndex < node.Strokes.Count; strokeIndex++)
        {
            var stroke = node.Strokes[strokeIndex];
            if (!stroke.Visible || stroke.Paint?.Visible == false) continue;
            var strokeColor = stroke.Paint?.Color ?? stroke.Color;
            if (stroke.Paint is { Kind: not FillKind.Solid } paint)
            {
                var id = $"stroke-paint-{node.Id}-{strokeIndex}";
                defs.Add(ExportGradient(paint, node, id)); strokeColor = "url(#" + id + ")";
            }
            var shape = Shape(node); shape.SetAttributeValue("fill", "none"); shape.SetAttributeValue("stroke", strokeColor);''')
replace(p, 'F(stroke.Opacity)', 'F(stroke.Opacity * (stroke.Paint?.Opacity ?? 1))')
replace(p, 'if (stroke.Dashes.Count > 0) shape.SetAttributeValue("stroke-dasharray", string.Join(" ", stroke.Dashes.Select(F))); group.Add(shape);',
    'if (stroke.Dashes.Count > 0) { shape.SetAttributeValue("stroke-dasharray", string.Join(" ", stroke.Dashes.Select(F))); shape.SetAttributeValue("stroke-dashoffset", F(stroke.DashOffset)); } group.Add(shape);')
replace(p, '            node.Opacity = Math.Clamp(Scalar(Own(element, "opacity"), 1), 0, 1);', '''            if (node.Strokes.Count > 0)
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
            node.Opacity = Math.Clamp(Scalar(Own(element, "opacity"), 1), 0, 1);''')
replace(p, '    private static string Transform(Matrix2D m)', '''    private static string SvgBlendName(BlendKind blend) => blend switch
    {
        BlendKind.ColorDodge => "color-dodge", BlendKind.ColorBurn => "color-burn",
        BlendKind.HardLight => "hard-light", BlendKind.SoftLight => "soft-light",
        _ => blend.ToString().ToLowerInvariant()
    };
    private static string Transform(Matrix2D m)''')

p = 'src/ArtSpace.Editor/DesignSurface.cs'
replace(p, '            _session = value;', '            Renderer.InvalidateRetainedScene();\n            _session = value;')
replace(p, '    private void SessionChanged(object? sender, EditorChangedEventArgs e)\n    {',
    '    private void SessionChanged(object? sender, EditorChangedEventArgs e)\n    {\n        if (e.Kind is EditorChangeKind.Document or EditorChangeKind.Preview) Renderer.InvalidateRetainedScene();')
replace(p, '    public event Action<string>? StatusChanged;', '    public event Action<string>? StatusChanged;\n    public event Action? FrameRendered;')
replace(p, 'Renderer.Dispose();', 'FrameRendered = null; Renderer.Dispose();')
p = 'src/ArtSpace.Editor/DesignSurface.Rendering.cs'
replace(p, '        else Renderer.Draw(canvas, editor.Page.Nodes, worldRect);',
    '        else if (editor.IsInteracting) Renderer.Draw(canvas, editor.Page.Nodes, worldRect);\n        else Renderer.DrawRetained(canvas, editor.Page, worldRect);')
replace(p, '        canvas.Restore();\n    }\n    private void DrawGrid', '        canvas.Restore();\n        FrameRendered?.Invoke();\n    }\n    private void DrawGrid')

p = 'src/ArtSpace.Workbench/StudioWorkbench.Inspector.cs'
replace(p, '=> _inspectorView!.Section(title, shape, build, glyph, action);',
    '=> (_activeInspector ?? _inspectorView)!.Section(title, shape, build, glyph, action);')
# Existing appearance sections now preserve complete symbol overrides; transform/layout stay unchanged.
s = Path(p).read_text()
start = s.index('    private void BuildFills(')
end = s.index('    private void ', s.index('    private void BuildEffects(') + 20)
s = s[:start] + s[start:end].replace('Change(', 'ChangeAppearance(') + s[end:]
Path(p).write_text(s)

p = 'src/ArtSpace.Workbench/StudioWorkbench.Refresh.cs'
replace(p, 'Pages = 256, Assets = 512,', 'Pages = 256, Assets = 512, Appearance = 1024, GraphicStyles = 2048,')
replace(p, '| Pages | Assets\n', '| Pages | Assets | Appearance | GraphicStyles\n')
replace(p, '        _uiDirty |= dirty;',
    '        if ((dirty & UiDirty.Inspector) != 0) dirty |= UiDirty.Appearance | UiDirty.GraphicStyles;\n        _uiDirty |= dirty;')
replace(p, '            // The illustration shell does not display the legacy Pages list.',
    '            RefreshAppearancePanels(dirty);\n            // The illustration shell does not display the legacy Pages list.')

p = 'src/ArtSpace.Workbench/StudioWorkbench.cs'
replace(p, '        if (e.Kind is EditorChangeKind.Document or EditorChangeKind.Selection)\n        {',
    '        if (e.Kind is EditorChangeKind.Document or EditorChangeKind.Selection)\n        {\n            SuspendAppearanceEditing();')
replace(p, '_inspectorView?.Dispose();', '_inspectorView?.Dispose(); _appearanceView?.Dispose(); _graphicStylesView?.Dispose();')

p = 'src/ArtSpace.Workbench/StudioWorkbench.Illustration.cs'
replace(p, '        _illustrationDock.Add("History", Studio.Scroll(_historyPanel));',
    '        _illustrationDock.Add("History", Studio.Scroll(_historyPanel));\n        ConfigureAppearancePanels();')
replace(p, '            if (name != "Properties") _inspectorView?.SuspendEditing();',
    '            if (name != "Properties") _inspectorView?.SuspendEditing();\n            SuspendAppearanceEditing();')
replace(p, '                "History" => UiDirty.History,',
    '                "History" => UiDirty.History,\n                "Appearance" => UiDirty.Appearance,\n                "Graphic Styles" => UiDirty.GraphicStyles,')
replace(p, '        else if (!showRight) _inspectorView?.SuspendEditing();',
    '        else if (!showRight) { _inspectorView?.SuspendEditing(); SuspendAppearanceEditing(); }')
replace(p, '                yield return Item("Drop Shadow", () => Session.UpdateSelection("Add drop shadow", n => n.Shadows.Add(new() { X = 5, Y = 8, Blur = 12, Opacity = .3 })), enabled: selected);',
    '                foreach (var kind in Enum.GetValues<LiveEffectKind>()) yield return Item(LiveEffect.Name(kind), () => AddLiveEffect(kind), enabled: selected);')
replace(p, 'new[] { "Properties", "Layers", "Artboards", "History" }',
    'new[] { "Properties", "Layers", "Artboards", "History", "Appearance", "Graphic Styles" }')
replace(p, '            case "View":', '''            case "View":
                yield return Item("Retained Scene Rendering", () => { Surface.Renderer.EnableRetainedScene = !Surface.Renderer.EnableRetainedScene; Surface.Renderer.InvalidateRetainedScene(); Surface.Invalidate(); });''')

p = 'src/ArtSpace.Controls/PanelDock.cs'
replace(p, 'grid.RowDefinitions.Add(new() { Height = new(31) });', 'grid.RowDefinitions.Add(new() { Height = GridLength.Auto });')
replace(p, '''        _tabs.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        Grid.SetColumn(button, _panels.Count);''', '''        if (_panels.Count < 4) _tabs.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        if (_panels.Count % 4 == 0) _tabs.RowDefinitions.Add(new() { Height = new(31) });
        Grid.SetRow(button, _panels.Count / 4); Grid.SetColumn(button, _panels.Count % 4);''')

p = 'src/ArtSpace.Workbench/StudioWorkbench.Appearance.cs'
s = Path(p).read_text().replace('Change(', 'ChangeAppearance(').replace('Action<GradientStop>', 'Action<ArtSpace.Core.GradientStop>')
s = s.replace('    private LiveEffectKind _newEffectKind;', '''    private LiveEffectKind _newEffectKind;
    private int _stylePage;
    private const int StylesPerPage = 12;
    private void ChangeAppearance(string label, Action<DesignNode> update) => Run(() =>
        Session.UpdateSelection(label, node => { update(node); ComponentService.SetAppearanceOverride(node); }));''')
s = s.replace('        _graphicStylesView.Begin(!ReferenceEquals(_stylesDocument, Session.Document));', '''        if (!ReferenceEquals(_stylesDocument, Session.Document)) _stylePage = 0;
        var pages = Math.Max(1, (Session.Document.GraphicStyles.Count + StylesPerPage - 1) / StylesPerPage);
        _stylePage = Math.Clamp(_stylePage, 0, pages - 1);
        _graphicStylesView.Begin(!ReferenceEquals(_stylesDocument, Session.Document));''')
s = s.replace('                b.Observe(_ => create.IsEnabled = Session.Primary is not null); body.Children.Add(create);', '''                b.Observe(_ => create.IsEnabled = Session.Primary is not null); body.Children.Add(create);
                var previous = b.Button(() => "Previous", () => { _stylePage--; RequestUi(UiDirty.GraphicStyles); });
                var next = b.Button(() => "Next", () => { _stylePage++; RequestUi(UiDirty.GraphicStyles); });
                b.Observe(_ => { previous.IsEnabled = _stylePage > 0; next.IsEnabled = (_stylePage + 1) * StylesPerPage < Session.Document.GraphicStyles.Count; });
                body.Children.Add(Studio.Columns((previous, -1), (next, -1)));
                body.Children.Add(b.Text(() => "Page " + (_stylePage + 1) + " · " + Session.Document.GraphicStyles.Count + " styles"));''')
s = s.replace('            for (var index = 0; index < Session.Document.GraphicStyles.Count; index++)',
    '            var first = _stylePage * StylesPerPage;\n            for (var index = first; index < Math.Min(first + StylesPerPage, Session.Document.GraphicStyles.Count); index++)')
s = s.replace('Inspect("Graphic style " + (i + 1), id,', 'Inspect("Graphic style " + (i - first + 1), id,')
Path(p).write_text(s)
replace('src/ArtSpace.Workbench/GraphicStylePreview.cs', '    public GraphicStyle? Style', '    public new GraphicStyle? Style')

p = 'src/ArtSpace.Controls/RetainedInspector.cs'
replace(p, '''        var field = new StudioButton(read(), () => { if (CanWrite) write(); });
        Observe''', '''        var field = new StudioButton(read(), () => { if (CanWrite) write(); });
        _fields.Add((read(), field, () => field.Content?.ToString() ?? ""));
        Observe''')
replace(p, '''        var field = new IconButton(read(), label, () => { if (CanWrite) write(); });
        Observe''', '''        var field = new IconButton(read(), label, () => { if (CanWrite) write(); });
        _fields.Add((label, field, () => field.Glyph));
        Observe''')

p = 'src/ArtSpace.App/Platforms/WebAssembly/BrowserWorkspaceStorage.cs'
replace(p, '                json.WriteNumber("gradientBuilds", workbench.Surface.Renderer.GradientBuilds);', '''                json.WriteNumber("gradientBuilds", workbench.Surface.Renderer.GradientBuilds);
                json.WriteNumber("effects", primary?.Effects.Count ?? 0);
                json.WriteNumber("effectRadius", primary?.Effects.FirstOrDefault()?.Radius ?? 0);
                json.WriteNumber("graphicStyles", session.Document.GraphicStyles.Count);
                json.WriteNumber("appearanceBuilds", workbench.AppearancePanelBuilds);
                json.WriteNumber("sceneRecordings", workbench.Surface.Renderer.SceneRecordings);
                json.WriteNumber("sceneReplays", workbench.Surface.Renderer.SceneReplays);
                json.WriteNumber("sceneBytes", workbench.Surface.Renderer.RetainedSceneBytes);
                json.WriteNumber("paintBuilds", workbench.Surface.Renderer.PaintBuilds);
                json.WriteNumber("dashBuilds", workbench.Surface.Renderer.DashBuilds);
                json.WriteNumber("effectFilterBuilds", workbench.Surface.Renderer.EffectFilterBuilds);''')
replace(p, 'foreach (var field in workbench.InspectorFields)', 'foreach (var field in workbench.InspectorFields.Concat(workbench.AppearanceFields))')
replace(p, '        workbench.UiRefreshed += QueuePublish;', '        workbench.UiRefreshed += QueuePublish;\n        workbench.Surface.FrameRendered += QueuePublish;')

p = 'tests/ArtSpace.Tests/Program.cs'
replace(p, 'if (args.Contains("--benchmark"))', 'if (args.Contains("--appearance-benchmark")) return LiveAppearanceBenchmarks.Run();\n\nif (args.Contains("--benchmark"))')
replace(p, 'var tests = new List<(string Name, Action Test)>();', 'var tests = new List<(string Name, Action Test)>();\nLiveAppearanceTests.Register(Test);')
for p in ['tests/ArtSpace.Tests/PerformanceBenchmarks.cs', 'tests/ArtSpace.Tests/AppearanceTests.cs', 'tests/ArtSpace.Tests/AppearanceRegressionTests.cs']:
    s = Path(p).read_text().replace(r'\"formatVersion\":3', r'\"formatVersion\":4').replace('FormatVersion == 3', 'FormatVersion == 4').replace('schema 3', 'schema 4').replace('schema three', 'schema four')
    if p.endswith('PerformanceBenchmarks.cs'):
        s = s.replace('catch (ArgumentException) { failed = true; }', 'catch (Exception ex) when (ex is ArgumentException or InvalidDataException) { failed = true; }')
    Path(p).write_text(s)
replace('tests/browser/appearance.spec.mjs', 'expect(document.formatVersion).toBe(3)', 'expect(document.formatVersion).toBe(4)')
replace('Directory.Build.props', '<Version>0.4.1-alpha.1</Version>', '<Version>0.5.0-alpha.1</Version>')
replace('.github/workflows/build.yml', '          cat artifacts/benchmarks/cpu.json', '''          cat artifacts/benchmarks/cpu.json
          dotnet run --project tests/ArtSpace.Tests -c Release --no-build -- --appearance-benchmark > artifacts/benchmarks/appearance-retention.json
          cat artifacts/benchmarks/appearance-retention.json''')
print('Integrated appearance model, native retention, UI and regression registration.')
