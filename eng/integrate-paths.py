from pathlib import Path
r=Path('.')
def patch(path,old,new):
 p=r/path;s=p.read_text();assert old in s,(path,old[:100]);p.write_text(s.replace(old,new))
patch('src/ArtSpace.Core/Document.cs','    public string? PathData { get; set; }','    public PathFillRule FillRule { get; set; }\n    public string? PathData { get; set; }')
patch('src/ArtSpace.Documents/DocumentJson.cs','            n.Opacity = Numbers.Clamp', '            if (!Enum.IsDefined(n.FillRule)) throw new InvalidDataException("Invalid path fill rule.");\n            n.Opacity = Numbers.Clamp')
patch('src/ArtSpace.Documents/SvgFormat.cs','new XAttribute("d", VectorPath.Build(node)))','new XAttribute("d", VectorPath.Build(node)), new XAttribute("fill-rule", node.FillRule == PathFillRule.EvenOdd ? "evenodd" : "nonzero"))')
patch('src/ArtSpace.Documents/SvgFormat.cs','            var fill = Attribute("fill")', '            node.FillRule = Attribute("fill-rule") == "evenodd" ? PathFillRule.EvenOdd : PathFillRule.NonZero;\n            var fill = Attribute("fill")')
patch('src/ArtSpace.Skia/SceneRenderer.cs','public sealed class SceneRenderer','public sealed partial class SceneRenderer')
patch('src/ArtSpace.Skia/SceneRenderer.cs','var signature = VectorPath.Build(node);','var signature = VectorPath.Build(node) + "|" + node.FillRule;')
patch('src/ArtSpace.Skia/SceneRenderer.cs','        if (cache is not null) cache.Path.Dispose();','        path.FillType = node.FillRule == PathFillRule.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;\n        if (cache is not null) cache.Path.Dispose();')
p=r/'src/ArtSpace.Skia/SceneRenderer.cs';s=p.read_text();a=s.index('    public void DrawText(');b=s.index('    public DesignNode? HitTest(',a);p.write_text(s[:a]+s[b:])
patch('src/ArtSpace.Skia/BooleanOperations.cs','result.Reset(); result.AddPath(combined);','result.Reset(); result.AddPath(combined); result.FillType = combined.FillType;')
patch('src/ArtSpace.Skia/BooleanOperations.cs','node.Rotation = 0;', 'node.FillRule = result.FillType == SKPathFillType.EvenOdd ? PathFillRule.EvenOdd : PathFillRule.NonZero;\n        node.Rotation = 0;')
patch('src/ArtSpace.Editing/EditorSession.cs','Gradient, Eyedropper, Zoom }','Gradient, Eyedropper, Zoom, AddAnchor, DeleteAnchor, AnchorPoint }')
patch('src/ArtSpace.Editor/DesignSurface.cs','Vertex, Gradient }','Vertex, Gradient, AnchorMarquee }')
patch('src/ArtSpace.Editor/DesignSurface.cs','else if (hit?.Kind == NodeKind.Path && hit.Points.Count > 0) { Session.Select(hit); _vectorNode = hit; _canvas.Invalidate(); e.Handled = true; }','else if (hit is not null && PathEditing.CanEdit(hit)) { Session.Select(hit); EnterPathEditing(); e.Handled = true; }')
patch('src/ArtSpace.Editor/DesignSurface.cs','                _gesture = Gesture.None; _created = null; _penNode = null;','                ResetPathGesture(); _gesture = Gesture.None; _created = null; _penNode = null;')
p=r/'src/ArtSpace.Editor/DesignSurface.cs';s=p.read_text();a=s.index('        if (_vectorNode is not null && editor.Tool == EditorTool.Move)');b=s.index('        if (editor.Tool is EditorTool.Pen',a);s=s[:a]+s[b:];p.write_text(s)
patch('src/ArtSpace.Editor/DesignSurface.cs','            case Gesture.Marquee:', '            case Gesture.AnchorMarquee: UpdateAnchorMarquee(world); break;\n            case Gesture.Marquee:')
patch('src/ArtSpace.Editor/DesignSurface.cs','MoveIllustrationAnchor(world, e.KeyModifiers.HasFlag(VirtualKeyModifiers.Menu))','MovePathAnchor(world, e.KeyModifiers.HasFlag(VirtualKeyModifiers.Menu))')
patch('src/ArtSpace.Editor/DesignSurface.cs','and not Gesture.Marquee and not Gesture.PenControl','and not Gesture.Marquee and not Gesture.AnchorMarquee and not Gesture.PenControl')
patch('src/ArtSpace.Editor/DesignSurface.cs','        Session?.CancelInteraction(); _canvas.Invalidate();','        ResetPathGesture(); Session?.CancelInteraction(); _canvas.Invalidate();')
p=r/'src/ArtSpace.Editor/DesignSurface.Illustration.cs';s=p.read_text();s=s.replace('    private int _anchorHandle;\n','');a=s.index('        if (editor.Tool != EditorTool.DirectSelect)');b=s.index('    private void MoveGradient',a);s=s[:a]+'''        return PathPressed(world, screen, e);
    }

'''+s[b:];s=s.replace('    private void DrawGradientHandles(SKCanvas canvas)\n    {','    private void DrawGradientHandles(SKCanvas canvas)\n    {\n        DrawEditableHandles(canvas);');p.write_text(s)
patch('src/ArtSpace.Editor/DesignSurface.Rendering.cs','editor.SelectionRoots.Count == 0 || _textEditor is not null)', 'editor.SelectionRoots.Count == 0 || _textEditor is not null || IsPathTool)')
patch('src/ArtSpace.Workbench/StudioWorkbench.Commands.cs','        Action? action = null;', '''        try
        {
            if (Surface.HandlePathKey(e.Key, control, shift, alt)) { e.Handled = true; return; }
        }
        catch (Exception ex) { ShowStatus(ex.Message, true); e.Handled = true; return; }
        Action? action = null;''')
patch('src/ArtSpace.Workbench/StudioWorkbench.Commands.cs','VirtualKey.O when shift => () => { Session.OutlinesVisible = !Session.OutlinesVisible; Surface.Invalidate(); },','VirtualKey.O when shift => () => ArtSpace.Illustration.PathOperations.CreateOutlines(Session, Surface.Renderer),')
patch('src/ArtSpace.Workbench/StudioWorkbench.Commands.cs','                VirtualKey.T => () => Session.Tool = EditorTool.Text,','                VirtualKey.C when shift => () => Session.Tool = EditorTool.AnchorPoint,\n                VirtualKey.T => () => Session.Tool = EditorTool.Text,')
patch('src/ArtSpace.Workbench/StudioWorkbench.Commands.cs','            if ((int)e.Key == 220) action', '            if ((int)e.Key == 187) action = () => Session.Tool = EditorTool.AddAnchor;\n            if ((int)e.Key == 189) action = () => Session.Tool = EditorTool.DeleteAnchor;\n            if ((int)e.Key == 220) action')
p=r/'src/ArtSpace.Workbench/StudioWorkbench.Commands.cs';s=p.read_text();s=s.replace('            if ((int)e.Key is 187 or 107) action = () => Surface.ZoomTo(Session.Viewport.Zoom * 1.25);\n','').replace('            if ((int)e.Key is 189 or 109) action = () => Surface.ZoomTo(Session.Viewport.Zoom / 1.25);\n','');s=s.replace('        if (action is not null)', '        if (control && ((int)e.Key is 187 or 107)) action = () => Surface.ZoomTo(Session.Viewport.Zoom * 1.25);\n        if (control && ((int)e.Key is 189 or 109)) action = () => Surface.ZoomTo(Session.Viewport.Zoom / 1.25);\n        if (action is not null)');p.write_text(s)
p=r/'src/ArtSpace.Workbench/StudioWorkbench.Illustration.cs';s=p.read_text()
s=s.replace('(EditorTool.Pen,"pen","Pen (P)"), (EditorTool.Pencil,"pencil","Pencil (N)"),','(EditorTool.Pen,"pen","Pen (P)"), (EditorTool.Pencil,"pencil","Pencil (N)"),\n            (EditorTool.AddAnchor,"plus","Add Anchor Point (+)"), (EditorTool.DeleteAnchor,"minus","Remove Anchor Point (-)"),')
s=s.replace('IllustrationOperations.AddAnchors(Session)', 'PathOperations.AddAnchors(Session, Surface.Renderer)')
s=s.replace('IllustrationOperations.SmoothAnchors(Session, true)', 'SmoothPathAnchors(true)').replace('IllustrationOperations.SmoothAnchors(Session, false)', 'SmoothPathAnchors(false)')
s=s.replace('IllustrationOperations.ReversePaths(Session)','PathOperations.Reverse(Session, Surface.Renderer)').replace('Session.UpdateSelection("Close path", n => n.Closed = true)', 'PathOperations.Close(Session, Surface.Renderer)')
s=s.replace('            case "Type":\n', '''            case "Type":
                yield return Item("Create Outlines", () => PathOperations.CreateOutlines(Session, Surface.Renderer), "Ctrl Shift O", Session.SelectionRoots.Any(n => n.DescendantsAndSelf().Any(c => c.Kind == NodeKind.Text && !c.IsEffectivelyLocked)));
''')
s=s.replace('                yield return Item("Add Anchor Points",', '''                yield return Item("Edit Anchors", Surface.EnterPathEditing, "A", PathEditing.CanEdit(Session.Primary));
                yield return Item("Anchor Point Tool", () => Session.Tool = EditorTool.AnchorPoint, "Shift C");
                yield return Item("Make Compound Path", () => PathOperations.MakeCompound(Session, Surface.Renderer), enabled: Session.SelectionRoots.Count > 1);
                yield return Item("Release Compound Path", () => PathOperations.ReleaseCompound(Session, Surface.Renderer), enabled: selected);
                yield return separator;
                yield return Item("Add Anchor Points",''')
s=s.replace('        var section = AddSection("Pathfinder");', '''        if (PathEditing.CanEdit(Session.Primary))
        {
            var pathSection = AddSection("Path");
            pathSection.Body.Children.Add(new StudioButton("Edit anchors", () => Run(Surface.EnterPathEditing)) { HorizontalAlignment = HorizontalAlignment.Stretch });
            pathSection.Body.Children.Add(Studio.Choice(new[] { "Nonzero", "Even-odd" }, Session.Primary!.FillRule == PathFillRule.EvenOdd ? "Even-odd" : "Nonzero", value => Change("Fill rule", n => n.FillRule = value == "Even-odd" ? PathFillRule.EvenOdd : PathFillRule.NonZero), "Path fill rule"));
            if (Surface.SelectedAnchorCount > 0)
            {
                pathSection.Body.Children.Add(Studio.Text(Surface.SelectedAnchorCount + " anchors selected", 10, Studio.Muted));
                pathSection.Body.Children.Add(Studio.Columns((new StudioButton("Smooth", () => Run(() => SmoothPathAnchors(true))), -1), (new StudioButton("Corner", () => Run(() => SmoothPathAnchors(false))), -1)));
                pathSection.Body.Children.Add(new StudioButton("Remove selected anchors", () => Run(() => Surface.RemoveSelectedAnchors(false))));
            }
        }
        if (Session.Primary?.Kind == NodeKind.Text)
        {
            var typeSection = AddSection("Type");
            typeSection.Body.Children.Add(new StudioButton("Create outlines", () => Run(() => PathOperations.CreateOutlines(Session, Surface.Renderer))) { HorizontalAlignment = HorizontalAlignment.Stretch });
        }
        var section = AddSection("Pathfinder");''')
s=s.replace('    private void SetGradient(bool radial)', '''    private void SmoothPathAnchors(bool smooth)
    {
        if (!Surface.EditSelectedAnchors(smooth ? "Smooth selected anchors" : "Corner selected anchors", (path, anchors) => path.Smooth(anchors, smooth)))
            PathOperations.Smooth(Session, Surface.Renderer, smooth);
    }

    private void SetGradient(bool radial)''')
p.write_text(s)
patch('src/ArtSpace.App/Platforms/WebAssembly/BrowserWorkspaceStorage.cs','                json.WriteBoolean("presenting", workbench.Surface.IsPresenting); json.WriteEndObject();', '''                var origin = workbench.Surface.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
                json.WriteNumber("canvasX", origin.X); json.WriteNumber("canvasY", origin.Y);
                json.WriteString("fillRule", primary?.FillRule.ToString());
                json.WriteStartArray("anchors");
                foreach (var anchor in workbench.Surface.GetPathAnchors())
                {
                    json.WriteStartObject(); json.WriteNumber("contour", anchor.Contour); json.WriteNumber("index", anchor.Index);
                    json.WriteNumber("x", anchor.Position.X + origin.X); json.WriteNumber("y", anchor.Position.Y + origin.Y);
                    json.WriteBoolean("selected", anchor.Selected);
                    if (anchor.ControlIn.HasValue) { json.WriteNumber("inX", anchor.ControlIn.Value.X + origin.X); json.WriteNumber("inY", anchor.ControlIn.Value.Y + origin.Y); }
                    if (anchor.ControlOut.HasValue) { json.WriteNumber("outX", anchor.ControlOut.Value.X + origin.X); json.WriteNumber("outY", anchor.ControlOut.Value.Y + origin.Y); }
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteBoolean("presenting", workbench.Surface.IsPresenting); json.WriteEndObject();''')
patch('tests/ArtSpace.Tests/Program.cs','var failed = 0;','PathEditingTests.Register(Test);\n\nvar failed = 0;')
patch('Directory.Build.props','<Version>0.1.0-alpha.1</Version>','<Version>0.2.0-alpha.1</Version>')
patch('src/ArtSpace.Illustration/IllustrationOperations.cs','output.PathData = normalized.ToSvgPathData();','output.PathData = normalized.ToSvgPathData();\n                output.FillRule = normalized.FillType == SKPathFillType.EvenOdd ? PathFillRule.EvenOdd : PathFillRule.NonZero;')
patch('src/ArtSpace.Illustration/IllustrationOperations.cs','PathData = normalized.ToSvgPathData(), Fill = fill','PathData = normalized.ToSvgPathData(), FillRule = normalized.FillType == SKPathFillType.EvenOdd ? PathFillRule.EvenOdd : PathFillRule.NonZero, Fill = fill')
print('Applied path editing integration.')
