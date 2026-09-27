#!/usr/bin/env python3
"""One-time, reviewable adaptation of the pinned MIT engine. The integration workflow commits the resulting sources."""
from pathlib import Path
import re

root = Path('.')
marker = root / 'eng/illustration-integrated.txt'
if marker.exists():
    raise SystemExit(0)

def edit(path, transform):
    p = root / path
    before = p.read_text()
    p.write_text(transform(before))

def replace(path, old, new):
    def patch(text):
        if old not in text:
            raise RuntimeError(f'Integration anchor missing: {path}: {old[:80]}')
        return text.replace(old, new)
    edit(path, patch)

for p in list((root/'src').rglob('*')) + list((root/'tests').rglob('*')) + [root/'playwright.config.mjs']:
    if p.is_file() and p.suffix in ['.cs','.js','.mjs']:
        p.write_text(p.read_text().replace('vectorSpace','artSpace').replace('VECTORSPACE_URL','ARTSPACE_URL'))

# The independent illustration package can be consumed without an Uno host.
p = root/'src/ArtSpace.Illustration/ArtSpace.Illustration.csproj'
p.write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><IsPackable>true</IsPackable><Description>Transactional illustration operations: vector stroke expansion, path offsetting, blends, repeats and anchor editing.</Description></PropertyGroup>
  <ItemGroup><ProjectReference Include="../ArtSpace.Skia/ArtSpace.Skia.csproj" /></ItemGroup>
</Project>
''')
replace('src/ArtSpace.Workbench/ArtSpace.Workbench.csproj', '</ItemGroup>', '<ProjectReference Include="../ArtSpace.Illustration/ArtSpace.Illustration.csproj" /></ItemGroup>')
replace('tests/ArtSpace.Tests/ArtSpace.Tests.csproj', '</ItemGroup>', '<ProjectReference Include="../../src/ArtSpace.Illustration/ArtSpace.Illustration.csproj" /></ItemGroup>')
replace('ArtSpace.slnx', '</Solution>', '  <Project Path="src/ArtSpace.Illustration/ArtSpace.Illustration.csproj" />\n</Solution>')
replace('Directory.Build.props', '<NuGetAudit>false</NuGetAudit>', '<NuGetAudit>true</NuGetAudit>')

# Stroke settings are preserved in native documents and SVG rather than being renderer constants.
replace('src/ArtSpace.Core/Document.cs', 'public enum FillKind', 'public enum StrokeCap { Butt, Round, Square }\npublic enum StrokeJoin { Miter, Round, Bevel }\npublic enum FillKind')
replace('src/ArtSpace.Core/Document.cs', 'public sealed class StrokeStyle\n{', 'public sealed class StrokeStyle\n{\n    public StrokeCap Cap { get; set; } = StrokeCap.Round;\n    public StrokeJoin Join { get; set; } = StrokeJoin.Round;\n    public double MiterLimit { get; set; } = 4;')
replace('src/ArtSpace.Core/Document.cs', 'public string Background { get; set; } = "#E5E5E5";', 'public string Background { get; set; } = "#565656";')
replace('src/ArtSpace.Skia/SceneRenderer.cs', 'StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round', 'StrokeCap = (SKStrokeCap)stroke.Cap, StrokeJoin = (SKStrokeJoin)stroke.Join, StrokeMiter = (float)stroke.MiterLimit')
replace('src/ArtSpace.Documents/SvgFormat.cs', 'shape.SetAttributeValue("stroke-linejoin", "round"); shape.SetAttributeValue("stroke-linecap", "round");', 'shape.SetAttributeValue("stroke-linejoin", stroke.Join.ToString().ToLowerInvariant()); shape.SetAttributeValue("stroke-linecap", stroke.Cap.ToString().ToLowerInvariant()); shape.SetAttributeValue("stroke-miterlimit", F(stroke.MiterLimit));')
replace('src/ArtSpace.Documents/SvgFormat.cs', 'Opacity = Numbers.Parse(Attribute("stroke-opacity") ?? "1", 1)', 'Opacity = Numbers.Parse(Attribute("stroke-opacity") ?? "1", 1), Cap = Enum.TryParse<StrokeCap>(Attribute("stroke-linecap"), true, out var cap) ? cap : StrokeCap.Butt, Join = Enum.TryParse<StrokeJoin>(Attribute("stroke-linejoin"), true, out var join) ? join : StrokeJoin.Miter, MiterLimit = Numbers.Parse(Attribute("stroke-miterlimit") ?? "4", 4), Dashes = (Attribute("stroke-dasharray") ?? "").Split(new[] { \' \', \',\' }, StringSplitOptions.RemoveEmptyEntries).Where(x => x != "none").Select(x => Numbers.Parse(x, 0)).Where(x => x > 0).ToList()')
replace('src/ArtSpace.Documents/DocumentJson.cs', 'foreach (var child in n.Children) Check(child, depth + 1);', '''foreach (var stroke in n.Strokes)
            {
                if (stroke is null || !double.IsFinite(stroke.Width) || stroke.Width < 0 || stroke.Width > 1e6 || !double.IsFinite(stroke.MiterLimit) || stroke.MiterLimit < 1 || stroke.MiterLimit > 1e6 || !Enum.IsDefined(stroke.Cap) || !Enum.IsDefined(stroke.Join) || stroke.Dashes is null || stroke.Dashes.Count > 4096 || stroke.Dashes.Any(d => !double.IsFinite(d) || d <= 0))
                    throw new InvalidDataException("Invalid stroke appearance.");
            }
            if (!double.IsFinite(n.PathWidth) || !double.IsFinite(n.PathHeight) || n.PathWidth < 0 || n.PathHeight < 0) throw new InvalidDataException("Invalid path dimensions.");
            foreach (var child in n.Children) Check(child, depth + 1);''')

# Custom controls use a coherent, compact dark palette. Document artwork colors are not rewritten.
colors = {'#0D99FF':'#477BDA', '#242424':'#D7D7D7', '#777777':'#A7A7A7', '#E7E7E7':'#272727', '#F3F3F3':'#292929', '#EEEEEE':'#494949', '#DADADA':'#252525', '#E5F4FF':'#40536D', '#DCEFFF':'#465F83'}
for p in (root/'src/ArtSpace.Controls').glob('*'):
    if p.suffix in ['.cs','.xaml']:
        s = p.read_text()
        for a,b in colors.items(): s = s.replace(a,b)
        s = s.replace('CornerRadius" Value="6"','CornerRadius" Value="1"').replace('CornerRadius" Value="5"','CornerRadius" Value="1"').replace('CornerRadius="4"','CornerRadius="0"')
        s = s.replace('Foreground" Value="#262626"','Foreground" Value="#D7D7D7"')
        p.write_text(s)
replace('src/ArtSpace.Controls/StudioResources.xaml.cs', 'Background = Brush("#FFFFFF")', 'Background = Brush("#383838")')
replace('src/ArtSpace.Controls/StudioButton.cs', 'i == _selected ? "#FFFFFF"', 'i == _selected ? "#555555"')
replace('src/ArtSpace.Controls/CommandMenuBar.cs', 'anchor.Focus(FocusState.Programmatic)', '(anchor as Control)?.Focus(FocusState.Programmatic)')
replace('src/ArtSpace.App/App.xaml.cs', 'ApplicationTheme.Light', 'ApplicationTheme.Dark')
# Original document and sample references.
for p in (root/'src').rglob('*.cs'):
    if p.name not in ['SampleDocument.cs','IllustrationSample.cs']:
        p.write_text(p.read_text().replace('SampleDocument.Create()', 'IllustrationSample.Create()'))

# Install the Illustrator-like workspace after the inherited engine/storage wiring has completed.
replace('src/ArtSpace.Workbench/StudioWorkbench.cs', '_toastTimer.Tick += (_, _) => { _toastTimer.Stop(); _toastBorder.Visibility = Visibility.Collapsed; };\n        RefreshAll();', '_toastTimer.Tick += (_, _) => { _toastTimer.Stop(); _toastBorder.Visibility = Visibility.Collapsed; };\n        ConfigureIllustrationWorkspace();\n        RefreshAll();')
replace('src/ArtSpace.Workbench/StudioWorkbench.cs', 'RefreshPages(); RefreshLayers(); RefreshInspector(); RefreshTools();', 'RefreshPages(); RefreshLayers(); RefreshInspector(); RefreshTools(); RefreshIllustrationPanels();')
edit('src/ArtSpace.Workbench/StudioWorkbench.cs', lambda s: re.sub(r'var active = tool == Session.Tool.*?;\n            button.IsSelected = active; button.Glyph = .*?;', 'button.IsSelected = tool == Session.Tool;', s))
edit('src/ArtSpace.Workbench/StudioWorkbench.cs', lambda s: re.sub(r'private void TogglePanels\(\)\s*\{.*?\n    \}', 'private void TogglePanels()\n    {\n        _uiVisible = !_uiVisible; ResizeIllustrationWorkspace(); Surface.FocusCanvas();\n    }', s, flags=re.S))
# Replace the initial empty-selection UI and append illustration controls to the properties panel.
p = root/'src/ArtSpace.Workbench/StudioWorkbench.Inspector.cs'
s = p.read_text(); start = s.index('        if (node is null)'); end = s.index('        var summary', start)
s = s[:start] + '''        if (node is null)
        {
            var document = AddSection("Document");
            document.Body.Children.Add(Studio.Text("RGB artwork · pixels", 11));
            document.Body.Children.Add(Wrapped("Use the tools at left to draw. Select artwork to edit appearance and transform. A edits anchors; Alt-drag breaks tangent symmetry."));
            document.Body.Children.Add(new StudioButton("New artboard", () => Run(AddArtboard)) { RestBackground = Studio.Field });
            AddIllustrationSections(); AddExportSection(); return;
        }
        AddIllustrationSections();
''' + s[end:]
s = s.replace('AddSection("Position")','AddSection("Transform")').replace('floating toolbar','toolbar').replace('AddSection("Auto layout"','AddSection("Layout"')
p.write_text(s)

# Tool identities and keyboard conventions.
replace('src/ArtSpace.Editing/EditorSession.cs', 'Hand, Comment, Slice }', 'Hand, Comment, Slice, DirectSelect, Brush, Gradient, Eyedropper, Zoom }')
p = root/'src/ArtSpace.Workbench/StudioWorkbench.Commands.cs'; s = p.read_text()
s = s.replace('VirtualKey.Y => Session.Redo,','VirtualKey.Y => () => { Session.OutlinesVisible = !Session.OutlinesVisible; Surface.Invalidate(); },\n                VirtualKey.R => () => { Session.RulersVisible = !Session.RulersVisible; Surface.Invalidate(); },\n                VirtualKey.N => () => RunAsync(NewDocumentAsync),\n                VirtualKey.Number0 => () => Surface.Fit(firstFrame: true),\n                VirtualKey.Number1 => () => Surface.ZoomTo(1),')
s = s.replace('VirtualKey.R => () => Session.Tool = EditorTool.Rectangle,','VirtualKey.M => () => Session.Tool = EditorTool.Rectangle,\n                VirtualKey.A => () => Session.Tool = EditorTool.DirectSelect,\n                VirtualKey.B => () => Session.Tool = EditorTool.Brush,\n                VirtualKey.N => () => Session.Tool = EditorTool.Pencil,\n                VirtualKey.G => () => Session.Tool = EditorTool.Gradient,\n                VirtualKey.I => () => Session.Tool = EditorTool.Eyedropper,\n                VirtualKey.Z => () => Session.Tool = EditorTool.Zoom,')
s = s.replace('VirtualKey.O => () => Session.Tool = EditorTool.Ellipse,','VirtualKey.O when shift => () => Session.Tool = EditorTool.Frame,')
s = s.replace('VirtualKey.L => () => Session.Tool = shift ? EditorTool.Arrow : EditorTool.Line,','VirtualKey.L => () => Session.Tool = EditorTool.Ellipse,')
s = s.replace('VirtualKey.S => () => Session.Tool = shift ? EditorTool.Section : EditorTool.Slice,','VirtualKey.S => () => Session.Tool = EditorTool.Scale,').replace('                VirtualKey.A when shift => AddAutoLayout,\n','')
s = s.replace('if ((int)e.Key is 187 or 107)', 'if ((int)e.Key == 220) action = () => Session.Tool = EditorTool.Line;\n            if ((int)e.Key is 187 or 107)')
s = s.replace('Session.Load(new()); Surface.Fit();','Session.Load(new()); AddArtboard();')
s = s.replace('editable Aether sample','editable Alpine Echoes sample').replace('not affiliated with Figma','not affiliated with Adobe')
s = s.replace('("Move / Frame / Rectangle / Ellipse", "V / F / R / O")','("Selection / Direct / Rectangle / Ellipse", "V / A / M / L")').replace('("Pen / Pencil / Text / Comment", "P / Shift P / T / C")','("Pen / Pencil / Brush / Type", "P / N / B / T")')
s = s.replace('This alpha does not provide complete Figma compatibility: .fig files, multiplayer, variables/variants, plugin execution and advanced prototyping are not implemented. SVG import reports unsupported elements instead of executing them.','Independent illustration editor, not full Illustrator parity. Native .ai/.eps, CMYK/ICC print production, gradient meshes, perspective tools, image tracing, advanced typography and Adobe plugins are not implemented. Work is saved locally; download a copy for backup. SVG import reports unsupported elements instead of executing them.')
p.write_text(s)

# Direct editing, brush appearance and gesture integration.
p = root/'src/ArtSpace.Editor/DesignSurface.cs'; s = p.read_text()
s = s.replace('Pinch, Vertex }','Pinch, Vertex, Gradient }')
s = s.replace('        if (_vectorNode is not null && editor.Tool == EditorTool.Move)','        if (IllustrationPressed(world, screen, e)) return;\n        if (_vectorNode is not null && editor.Tool == EditorTool.Move)')
s = s.replace('_gesture = Gesture.Vertex; _vertexIndex = i;', '_gesture = Gesture.Vertex; _vertexIndex = i; _anchorHandle = 0;')
s = s.replace('editor.Tool is EditorTool.Pen or EditorTool.Pencil) { StartPath(world, editor.Tool == EditorTool.Pencil)', 'editor.Tool is EditorTool.Pen or EditorTool.Pencil or EditorTool.Brush) { StartPath(world, editor.Tool is EditorTool.Pencil or EditorTool.Brush)')
s = re.sub(r'            case Gesture.Vertex:.*?\n            default:', '''            case Gesture.Vertex:
                MoveIllustrationAnchor(world, e.KeyModifiers.HasFlag(VirtualKeyModifiers.Menu)); break;
            case Gesture.Gradient:
                MoveGradient(world); break;
            default:''', s, flags=re.S)
s = s.replace('private static DesignNode NewNode','private DesignNode NewNode')
s = s.replace('        return node;\n    }\n    private void StartPath', '''        if (kind is not NodeKind.Frame and not NodeKind.Section and not NodeKind.Slice and not NodeKind.Text)
        {
            if (kind is not NodeKind.Line and not NodeKind.Arrow) node.Fill = FillColor;
            node.Strokes = StrokeWidth > 0 ? [new() { Color = StrokeColor, Width = StrokeWidth }] : [];
        }
        return node;
    }
    private void StartPath''')
s = s.replace('Name = pencil ? "Pencil" : "Vector"', 'Name = editor.Tool == EditorTool.Brush ? "Brush" : pencil ? "Pencil" : "Path"')
s = s.replace('Color = "#333333", Width = 2', 'Color = StrokeColor, Width = editor.Tool == EditorTool.Brush ? Math.Max(4, StrokeWidth) : Math.Max(1, StrokeWidth)')
p.write_text(s)
p = root/'src/ArtSpace.Editor/DesignSurface.Rendering.cs'; s = p.read_text().replace('new(13, 153, 255','new(68, 124, 238').replace('new(250, 250, 250)','new(53, 53, 53)').replace('new(110, 110, 110)','new(205, 205, 205)')
s = s.replace('            DrawSelection(canvas);', '            DrawSelection(canvas);\n            DrawGradientHandles(canvas);')
p.write_text(s)
# Original vector glyphs for new tools.
p = root/'src/ArtSpace.Controls/IconView.cs'; s = p.read_text()
idx = s.index('["move"]')
s = s[:idx] + '''["directselect"] = "M5 3L18 13L12 14L9 21Z",
        ["brush"] = "M20 3L10 14L7 11ZM9 14C10 20 4 22 2 20C6 19 3 15 6 13Z",
        ["gradient"] = "M3 4H21V20H3ZM7 5V19M10 5V19M13 5V19M16 5V19M19 5V19",
        ["eyedropper"] = "M16 3L21 8L17 12L12 7ZM13 8L4 17L3 21L7 20L16 11M5 16L8 19",
        ["zoom"] = "M10 3A7 7 0 1 0 10 17A7 7 0 1 0 10 3M15 15L21 21M6 10H14M10 6V14",
        ''' + s[idx:]
p.write_text(s)

# Regressions execute real Skia geometry and transactional history.
p = root/'tests/ArtSpace.Tests/Program.cs'; s = p.read_text()
s = 'using ArtSpace.Illustration;\n' + s
insert = '''
Test("illustration sample has three artboards", () => { var d = IllustrationSample.Create(); DocumentJson.Validate(d); Equal(d.Pages[0].Nodes.Count, 3); Check(d.AllNodes().Count() > 60); });
Test("illustration sample exports real artwork", () => { var d = IllustrationSample.Create(); using var r = new SceneRenderer(); var n = d.Pages[0].Nodes[0]; using var b = SKBitmap.Decode(r.ExportPng([n], n.WorldBounds, .25)); Equal(b.Width, 260); Equal(b.Height, 205); });
Test("stroke properties roundtrip", () => { var n = Node(); n.Strokes = [new() { Cap = StrokeCap.Square, Join = StrokeJoin.Bevel, MiterLimit = 8 }]; var e = Editor(n); var d = DocumentJson.Load(DocumentJson.Save(e.Document)); Check(d.AllNodes().First().Strokes[0].Cap == StrokeCap.Square); });
Test("stroke expansion creates editable geometry", () => { var n = Node(); n.Fills.Clear(); n.Strokes = [new() { Width = 12, Color = "#FF0000" }]; var e = Editor(n); e.Select(n); using var r = new SceneRenderer(); IllustrationOperations.OutlineStrokes(e,r); Check(e.Primary!.Kind == NodeKind.Group); Check(e.Primary.Children[0].PathData?.Length > 5); e.Undo(); Check(e.Primary!.Kind == NodeKind.Rectangle); });
Test("stroke expansion retains filled interior", () => { var n = Node(); n.Strokes = [new() { Width = 12 }]; var e = Editor(n); e.Select(n); using var r = new SceneRenderer(); IllustrationOperations.OutlineStrokes(e,r); Equal(e.Primary!.Children.Count, 2); });
Test("stroke expansion refuses missing strokes", () => { var n = Node(); var e = Editor(n); e.Select(n); using var r = new SceneRenderer(); Throws(() => IllustrationOperations.OutlineStrokes(e,r)); Equal(e.Page.Nodes.Count,1); });
Test("offset creates separate path and undo", () => { var n = Node(); var e = Editor(n); e.Select(n); using var r = new SceneRenderer(); IllustrationOperations.OffsetPaths(e,r,10); Equal(e.Page.Nodes.Count,2); Check(e.Primary!.PathData is not null); e.Undo(); Equal(e.Page.Nodes.Count,1); });
Test("offset rejects nonfinite distances", () => { var n = Node(); var e = Editor(n); e.Select(n); using var r = new SceneRenderer(); Throws(() => IllustrationOperations.OffsetPaths(e,r,double.NaN)); });
Test("blend produces bounded intermediate objects", () => { var a = Node(); var b = Node(400,100); a.Fill="#000000"; b.Fill="#FFFFFF"; var e=Editor(a,b); e.Select([a.Id,b.Id]); IllustrationOperations.Blend(e,3); Equal(e.Page.Nodes.Count,5); var middle=e.Page.Nodes.Single(n=>n.Name=="Blend 2"); Equal(middle.X,200); Equal(middle.Y,50); Check(middle.Fill=="#7F7F7F"); e.Undo(); Equal(e.Page.Nodes.Count,2); });
Test("blend bounds prevents unbounded allocation", () => { var e=Editor(Node(),Node()); e.SelectAll(); Throws(()=>IllustrationOperations.Blend(e,100000)); });
Test("blend rejects incompatible geometry", () => { var a=Node(); var b=Node(); b.Kind=NodeKind.Ellipse; var e=Editor(a,b); e.SelectAll(); Throws(()=>IllustrationOperations.Blend(e,4)); });
Test("radial repeat is a single transaction", () => { var n=Node(); var e=Editor(n); e.Select(n); IllustrationOperations.RadialRepeat(e,8); Equal(e.Page.Nodes.Count,8); e.Undo(); Equal(e.Page.Nodes.Count,1); });
Test("reverse swaps control handles", () => { var n=Node(); n.Kind=NodeKind.Path; n.Points=[new(){Position=new(0,0),ControlOut=new(20,0)},new(){Position=new(100,0),ControlIn=new(80,0)}]; var e=Editor(n); e.Select(n); IllustrationOperations.ReversePaths(e); Equal(n.Points[0].Position.X,100); Equal(n.Points[0].ControlOut!.Value.X,80); });
Test("subdivision preserves cubic path sample", () => { var n=Node(); n.Kind=NodeKind.Path; n.Points=[new(){Position=new(0,0),ControlOut=new(0,100)},new(){Position=new(100,0),ControlIn=new(100,100)}]; var e=Editor(n); e.Select(n); IllustrationOperations.AddAnchors(e); Equal(n.Points.Count,3); Equal(n.Points[1].Position.X,50); Equal(n.Points[1].Position.Y,75); });
Test("smooth and corner conversion", () => { var n=Node(); n.Kind=NodeKind.Path; n.Points=[new(){Position=new(0,0)},new(){Position=new(50,50)},new(){Position=new(100,0)}]; var e=Editor(n); e.Select(n); IllustrationOperations.SmoothAnchors(e,true); Check(n.Points[1].ControlIn.HasValue); IllustrationOperations.SmoothAnchors(e,false); Check(n.Points.All(p=>p.ControlIn is null && p.ControlOut is null)); });
Test("expand shape preserves appearance", () => { var n=Node(); n.Kind=NodeKind.Star; n.Fill="#E6AA67"; var e=Editor(n); e.Select(n); using var r=new SceneRenderer(); IllustrationOperations.ExpandShapes(e,r); Check(n.Kind==NodeKind.Path && n.PathData is not null && n.Fill=="#E6AA67"); });
Test("SVG retains cap and join", () => { var n=Node(); n.Strokes=[new(){Cap=StrokeCap.Square,Join=StrokeJoin.Bevel}]; var svg=SvgFormat.Export([n],n.WorldBounds); Check(svg.Contains("stroke-linecap=\\"square\\"") && svg.Contains("stroke-linejoin=\\"bevel\\"")); });
Test("invalid strokes are rejected", () => { var n=Node(); n.Strokes=[new(){Width=double.NaN}]; Throws(()=>Editor(n)); });
'''
s = s.replace('var failed = 0;', insert + '\nvar failed = 0;'); p.write_text(s)

marker.write_text('Illustration integration applied. Generated edits are committed as ordinary source files.\n')
print('Illustration workspace integration completed.')
