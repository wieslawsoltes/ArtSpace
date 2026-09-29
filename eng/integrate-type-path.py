from pathlib import Path
import re

def patch(path, old, new):
    file = Path(path)
    text = file.read_text()
    if old not in text:
        raise RuntimeError(f'Missing integration anchor in {path}: {old[:100]}')
    file.write_text(text.replace(old, new))

patch('src/ArtSpace.Core/Document.cs', '    public string Text { get; set; } = "Text";', '    public TypeOnPathOptions? TextPath { get; set; }\n    public string Text { get; set; } = "Text";')
patch('src/ArtSpace.Core/Document.cs', '        if (!node.IsContainer)\n', '        if (!node.IsContainer && node.TextPath is null)\n')
patch('src/ArtSpace.Documents/DocumentJson.cs', 'CurrentFormatVersion = 4', 'CurrentFormatVersion = 5')
patch('src/ArtSpace.Documents/DocumentJson.cs', 'Save using schema 4.', 'Save using schema 5.')
patch('src/ArtSpace.Documents/DocumentJson.cs', '            ValidateLiveAppearance(n);', '''            ValidateLiveAppearance(n);
            if (n.TextPath is { } pathText)
            {
                pathText.Validate();
                if (n.Kind != NodeKind.Text || n.Text is null || n.Text.Length > TypeOnPathOptions.MaxTextLength
                    || string.IsNullOrWhiteSpace(n.PathData) || n.PathData.Length > 2_000_000 || n.Points.Count != 0
                    || n.PathWidth <= 0 || n.PathHeight <= 0 || !double.IsFinite(n.FontSize) || n.FontSize <= 0
                    || !double.IsFinite(n.LetterSpacing) || Math.Abs(n.LetterSpacing) > 10000 || !Enum.IsDefined(n.TextAlign))
                    throw new InvalidDataException("Invalid type-on-path text or baseline data.");
            }''')
patch('src/ArtSpace.Skia/SceneRenderer.cs', '        PruneEffects(retained);', '        PruneEffects(retained);\n        PruneTypeOnPath(nodes.Where(n => n.TextPath is not null).Select(n => n.Id).ToHashSet(StringComparer.Ordinal));')
patch('src/ArtSpace.Skia/SceneRenderer.cs', 'if (node.Kind == NodeKind.Path && node.PathWidth > 0 && node.PathHeight > 0)', 'if ((node.Kind == NodeKind.Path || node.TextPath is not null) && node.PathWidth > 0 && node.PathHeight > 0)')
patch('src/ArtSpace.Skia/SceneRenderer.cs', '            if (node.Kind == NodeKind.Text && inside) return node;', '            if (node.Kind == NodeKind.Text && node.TextPath is null && inside) return node;')
patch('src/ArtSpace.Skia/SceneRenderer.cs', '            var path = Geometry(node);\n            var pickBounds', '            var path = node.TextPath is not null ? TypeOnPathLayout(node).Outline : Geometry(node);\n            var pickBounds')
patch('src/ArtSpace.Skia/SceneRenderer.Text.cs', '    private void ClearTextLayouts()\n    {', '    private void ClearTextLayouts()\n    {\n        ClearTypeOnPath();')
patch('src/ArtSpace.Skia/SceneRenderer.Text.cs', '        var path = new SKPath();\n        try', '        if (node.TextPath is not null) return TypeOnPathLayout(node).CreateOutline();\n        var path = new SKPath();\n        try')
patch('src/ArtSpace.Skia/SceneRenderer.Text.cs', '    public void DrawText(SKCanvas canvas, DesignNode node, SKPaint paint)\n    {', '    public void DrawText(SKCanvas canvas, DesignNode node, SKPaint paint)\n    {\n        if (node.TextPath is not null) { TypeOnPathLayout(node).Draw(canvas, paint); return; }')
patch('src/ArtSpace.Skia/PathEditing.cs', 'node.Kind is not NodeKind.Text and not NodeKind.Slice;', '(node.Kind != NodeKind.Text || node.TextPath is not null) && node.Kind != NodeKind.Slice;')
patch('src/ArtSpace.Skia/PathEditing.cs', '        basis ??= node;', '''        if (node.TextPath is not null)
        {
            using var validate = new MeasuredContour(localGeometry);
        }
        basis ??= node;''')
patch('src/ArtSpace.Skia/PathEditing.cs', 'basis.Kind == NodeKind.Path && basis.PathWidth > 0', '(basis.Kind == NodeKind.Path || basis.TextPath is not null) && basis.PathWidth > 0')
patch('src/ArtSpace.Skia/PathEditing.cs', '        node.Kind = NodeKind.Path; node.PathData', '        node.Kind = node.TextPath is null ? NodeKind.Path : NodeKind.Text; node.PathData')
patch('src/ArtSpace.Skia/PathEditing.cs', '        NodeGeometry.SetLocalMatrix(node, Matrix2D.Translation(bounds.Left, bounds.Top) * oldMatrix);', '''        if (node.TextPath is null) NodeGeometry.SetLocalMatrix(node, Matrix2D.Translation(bounds.Left, bounds.Top) * oldMatrix);
        else NodeGeometry.SetExactMatrix(node, Matrix2D.Translation(bounds.Left, bounds.Top) * oldMatrix);''')
patch('src/ArtSpace.Illustration/PathOperations.cs', '                PathEditing.Write(node, path); node.Name += " outlines";', '                node.TextPath = null; PathEditing.Write(node, path); node.Name += " outlines";')
patch('src/ArtSpace.Illustration/PathOperations.cs', '        var selected = Vectors(editor);', '        var selected = Vectors(editor);\n        if (selected.Any(n => n.TextPath is not null)) throw new InvalidOperationException("Create text outlines before making a compound path.");')
patch('src/ArtSpace.Illustration/PathOperations.cs', '        editor.Edit("Release compound path",', '        if (nodes.Any(n => n.TextPath is not null)) throw new InvalidOperationException("Create text outlines before releasing compound glyph contours.");\n        editor.Edit("Release compound path",')
patch('src/ArtSpace.Workbench/StudioWorkbench.Inspector.cs', '            if (node.Kind == NodeKind.Text) BuildTypography();', '            if (node.Kind == NodeKind.Text) BuildTypography();\n            if (node.TextPath is not null) BuildTypeOnPath();')
patch('src/ArtSpace.Workbench/StudioWorkbench.Illustration.cs', '            case "Type":', '''            case "Type":
                yield return Async("Type on a Path…", CreateTypeOnPathAsync);
                yield return Item("Edit Path Baseline", Surface.EnterPathEditing, enabled: Session.Primary?.TextPath is not null);
                yield return Item("Flip Path Text", () => TypeOnPathOperations.Update(Session, "Flip path text", o => o.Flip = !o.Flip), enabled: Session.Primary?.TextPath is not null);''')
patch('src/ArtSpace.Workbench/StudioWorkbench.Commands.cs', 'SvgFormat.Export(nodes, bounds)', 'ArtSpace.Illustration.IllustrationSvgExport.Export(nodes, bounds, Surface.Renderer)')
patch('src/ArtSpace.Documents/SvgFormat.cs', '        if (!node.Visible || node.Kind == NodeKind.Slice) return null;', '''        if (!node.Visible || node.Kind == NodeKind.Slice) return null;
        if (node.TextPath is not null) throw new InvalidOperationException("Use IllustrationSvgExport or Create Outlines to export type-on-path text as vector geometry.");''')
patch('src/ArtSpace.Documents/SvgFormat.cs', '        if (root.Name.LocalName != "svg") throw new InvalidDataException("The file root must be svg.");', '''        if (root.Name.LocalName != "svg") throw new InvalidDataException("The file root must be svg.");
        if (root.Descendants().Any(e => e.Name.LocalName == "textPath"))
            throw new InvalidDataException("SVG textPath import is not supported yet. Convert path text to outlines in the source application.");''')
patch('src/ArtSpace.Editor/DesignSurface.cs', 'AnchorMarquee, PendingTransform, PendingVertex }', 'AnchorMarquee, PendingTransform, PendingVertex, PendingTypePath, TypePath }')
patch('src/ArtSpace.Editor/DesignSurface.cs', '                ResetPathGesture(); _gesture = Gesture.None;', '                ResetPathGesture(); ResetTypeOnPathGesture(); _gesture = Gesture.None;')
patch('src/ArtSpace.Editor/DesignSurface.cs', '        if (IllustrationPressed(world, screen, e)) return;', '        if (TypeOnPathPressed(world, screen)) return;\n        if (IllustrationPressed(world, screen, e)) return;')
patch('src/ArtSpace.Editor/DesignSurface.cs', '            case Gesture.Pan:', '            case Gesture.PendingTypePath:\n            case Gesture.TypePath: MoveTypeOnPath(world, screen, e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control)); break;\n            case Gesture.Pan:')
patch('src/ArtSpace.Editor/DesignSurface.cs', 'or Gesture.PendingVertex or Gesture.Vertex) Moved(sender, e);', 'or Gesture.PendingVertex or Gesture.Vertex or Gesture.PendingTypePath or Gesture.TypePath) Moved(sender, e);')
patch('src/ArtSpace.Editor/DesignSurface.cs', 'and not Gesture.PendingTransform and not Gesture.PendingVertex) editor.CommitInteraction();', 'and not Gesture.PendingTransform and not Gesture.PendingVertex and not Gesture.PendingTypePath) editor.CommitInteraction();\n        ResetTypeOnPathGesture();')
f = Path('src/ArtSpace.Editor/DesignSurface.cs'); s = f.read_text(); s, count = re.subn(r'(void CancelGesture\(\)\s*\{)', r'\1\n        ResetTypeOnPathGesture();', s); assert count == 1; f.write_text(s)
patch('src/ArtSpace.Editor/DesignSurface.Rendering.cs', '            DrawGradientHandles(canvas);', '            DrawGradientHandles(canvas);\n            DrawTypeOnPathHandles(canvas);')
patch('src/ArtSpace.App/Platforms/WebAssembly/BrowserWorkspaceStorage.cs', '                json.WriteNumber("graphicStyles", session.Document.GraphicStyles.Count);', '''                json.WriteNumber("graphicStyles", session.Document.GraphicStyles.Count);
                json.WriteNumber("pathTextLayoutBuilds", workbench.Surface.Renderer.PathTextLayoutBuilds);
                json.WriteNumber("textBaselineBuilds", workbench.Surface.Renderer.TextBaselineBuilds);
                if (primary?.TextPath is { } pathText)
                {
                    var status = workbench.Surface.Renderer.GetTypeOnPathStatus(primary);
                    json.WriteStartObject("pathText"); json.WriteNumber("start", pathText.Start); json.WriteNumber("end", pathText.End);
                    json.WriteBoolean("flip", pathText.Flip); json.WriteNumber("baselineShift", pathText.BaselineShift);
                    json.WriteNumber("length", status.PathLength); json.WriteBoolean("overflow", status.Overflow);
                    json.WriteNumber("visibleGlyphs", status.VisibleGlyphs); json.WriteEndObject();
                }
                json.WriteStartArray("typePathHandles");
                foreach (var handle in workbench.Surface.GetTypeOnPathHandles())
                {
                    json.WriteStartObject(); json.WriteNumber("kind", handle.Kind);
                    json.WriteNumber("x", handle.Position.X + origin.X); json.WriteNumber("y", handle.Position.Y + origin.Y);
                    json.WriteNumber("baseX", handle.BaselinePosition.X + origin.X); json.WriteNumber("baseY", handle.BaselinePosition.Y + origin.Y);
                    json.WriteEndObject();
                }
                json.WriteEndArray();''')
patch('tests/ArtSpace.Tests/Program.cs', 'var tests = new List<(string Name, Action Test)>();', 'if (args.Contains("--type-on-path-benchmark")) return TypeOnPathBenchmarks.Run();\n\nvar tests = new List<(string Name, Action Test)>();\nTypeOnPathTests.Register(Test);')
# Existing tests follow current schema when checking the version produced by Save, not old literal versions.
for file in Path('tests').rglob('*'):
    if not file.is_file() or file.suffix not in ['.cs', '.mjs']: continue
    text = file.read_text().replace('FormatVersion == 4', 'FormatVersion == DocumentJson.CurrentFormatVersion')
    text = text.replace('formatVersion).toBe(4)', 'formatVersion).toBe(5)')
    text = text.replace('formatVersion\\\":4', 'formatVersion\\\":5')
    file.write_text(text)
patch('Directory.Build.props', '<Version>0.5.0-alpha.1</Version>', '<Version>0.6.0-alpha.1</Version>')
patch('src/ArtSpace.App/ArtSpace.App.csproj', '<ApplicationDisplayVersion>0.5.0</ApplicationDisplayVersion>', '<ApplicationDisplayVersion>0.6.0</ApplicationDisplayVersion>')
patch('src/ArtSpace.App/ArtSpace.App.csproj', '<ApplicationVersion>5</ApplicationVersion>', '<ApplicationVersion>6</ApplicationVersion>')
print('Integrated type-on-path model, rendering, UI and regression coverage.')
