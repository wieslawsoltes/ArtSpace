from pathlib import Path
root=Path('.')
def change(path,old,new):
 p=root/path; s=p.read_text(); assert old in s,(path,old[:80]); p.write_text(s.replace(old,new))
change('src/ArtSpace.Core/Document.cs','    public bool ClipContent { get; set; }','''    public bool ClipContent { get; set; }
    /// <summary>Identifier of the direct child whose filled geometry clips this container's contents.</summary>
    public string? ClipPathId { get; set; }
    [JsonIgnore] public DesignNode? ClippingPath => ClipPathId is null ? null : Children.Find(n => n.Id == ClipPathId);''')
change('src/ArtSpace.Documents/DocumentJson.cs','            node.Id = ids[node.Id];','''            node.Id = ids[node.Id];
            if (node.ClipPathId is { } clip && ids.TryGetValue(clip, out var clipReplacement)) node.ClipPathId = clipReplacement;''')
change('src/ArtSpace.Documents/DocumentJson.cs','            if (!Enum.IsDefined(n.FillRule))','''            if (n.ClipPathId is { } clip)
            {
                var mask = n.Children.Find(child => child?.Id == clip);
                if (!n.IsContainer || mask is null || mask.IsContainer || mask.Children is null || mask.Children.Count != 0 || mask.Kind is NodeKind.Text or NodeKind.Slice)
                    throw new InvalidDataException("A clipping path must reference a direct vector child of its container.");
            }
            if (!Enum.IsDefined(n.FillRule))''')
change('src/ArtSpace.Editing/ComponentService.cs','            instance.Children = copy.Children;','''            var remapped = copy.DescendantsAndSelf().ToDictionary(n => n.SourceId!, n => n.Id);
            foreach (var n in copy.DescendantsAndSelf())
                if (n.ClipPathId is { } mask && remapped.TryGetValue(mask, out var id)) n.ClipPathId = id;
            instance.ClipPathId = copy.ClipPathId;
            instance.Children = copy.Children;''')
change('src/ArtSpace.Editing/EditorSession.cs','    public IReadOnlyList<DesignNode> Selection => Page.AllNodes().Where(n => _selected.Contains(n.Id)).ToArray();\n    public IReadOnlyList<DesignNode> SelectionRoots => Selection.Where(n => !Ancestors(n).Any(a => _selected.Contains(a.Id))).ToArray();','''    private IReadOnlyList<DesignNode>? _selectionCache, _rootsCache;
    public long SelectionMaterializations { get; private set; }
    private void InvalidateSelection() { _selectionCache = null; _rootsCache = null; }
    public IReadOnlyList<DesignNode> Selection
    {
        get
        {
            if (_selectionCache is null)
            {
                SelectionMaterializations++;
                _selectionCache = Array.AsReadOnly(Page.AllNodes().Where(n => _selected.Contains(n.Id)).ToArray());
            }
            return _selectionCache;
        }
    }
    public IReadOnlyList<DesignNode> SelectionRoots => _rootsCache ??= Array.AsReadOnly(Selection.Where(n => !Ancestors(n).Any(a => _selected.Contains(a.Id))).ToArray());''')
change('src/ArtSpace.Editing/EditorSession.cs','    public void Notify(EditorChangeKind kind, string label = "") => Changed?.Invoke(this, new(kind, label));','''    public void Notify(EditorChangeKind kind, string label = "")
    {
        if (kind is EditorChangeKind.Document or EditorChangeKind.Selection) InvalidateSelection();
        Changed?.Invoke(this, new(kind, label));
    }''')
change('src/ArtSpace.Editing/EditorSession.cs','        _before = Capture(); _interactionLabel = label;','        InvalidateSelection(); _before = Capture(); _interactionLabel = label;')
change('src/ArtSpace.Editing/EditorSession.cs','        node.Parent = parent; (parent?.Children ?? Page.Nodes).Add(node);','        node.Parent = parent; (parent?.Children ?? Page.Nodes).Add(node); InvalidateSelection();')
change('src/ArtSpace.Editing/EditorSession.cs','    public void RemoveNode(DesignNode node) => (node.Parent?.Children ?? Page.Nodes).Remove(node);','''    public void RemoveNode(DesignNode node)
    {
        var parent = node.Parent; (parent?.Children ?? Page.Nodes).Remove(node);
        if (parent?.ClipPathId == node.Id) parent.ClipPathId = null;
        InvalidateSelection();
    }''')
change('src/ArtSpace.Editing/EditorSession.cs','        _selected.Clear(); _selected.UnionWith(newIds);','        _selected.Clear(); _selected.UnionWith(newIds); InvalidateSelection();')
p=root/'src/ArtSpace.Skia/SceneRenderer.cs'; s=p.read_text()
s=s.replace('private sealed record CachedPath(string Signature, SKPath Path);','private sealed record CachedPath(GeometrySnapshot Snapshot, SKPath Path);')
s=s.replace('    public long RenderedNodes { get; private set; }','''    public long RenderedNodes { get; private set; }
    public long VisitedNodes { get; private set; }
    public long CulledNodes { get; private set; }
    public long GeometryBuilds { get; private set; }
    public long GeometryCacheHits { get; private set; }
    public bool EnableCulling { get; set; } = true;
    public int CachedGeometryCount => _paths.Count;
    public void PruneCache(IEnumerable<DesignNode> roots)
    {
        var retained = roots.SelectMany(n => n.DescendantsAndSelf()).Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _paths.Keys.Where(id => !retained.Contains(id)).ToArray())
        { _paths[id].Path.Dispose(); _paths.Remove(id); }
    }''')
a=s.index('        var signature = VectorPath.Build(node)'); b=s.index('        if (node.Kind == NodeKind.Path && node.PathWidth',s.index('        var path = SKPath',a))
s=s[:a]+'''        if (_paths.TryGetValue(node.Id, out var cache) && cache.Snapshot.Matches(node))
        { GeometryCacheHits++; return cache.Path; }
        var path = SKPath.ParseSvgPathData(VectorPath.Build(node)) ?? new SKPath();
        GeometryBuilds++;
'''+s[b:]
s=s.replace('_paths[node.Id] = new(signature, path)','_paths[node.Id] = new(new GeometrySnapshot(node), path)')
a=s.index('        RenderedNodes = 0;'); b=s.index('    public void DrawWorldNode',a)
s=s[:a]+'''        RenderedNodes = VisitedNodes = CulledNodes = 0;
        // Actual canvas clip is authoritative; fixed world inflation is unsafe for shadows.
        foreach (var node in nodes) DrawNode(canvas, node);
    }
'''+s[b:]
s=s.replace('private void DrawNode(SKCanvas canvas, DesignNode node)','private void DrawNode(SKCanvas canvas, DesignNode node, bool filteredAncestor = false)')
s=s.replace('''        RenderedNodes++;
        canvas.Save(); canvas.Concat(Matrix(node.LocalMatrix));''','''        VisitedNodes++;
        canvas.Save(); canvas.Concat(Matrix(node.LocalMatrix));
        var filtered = filteredAncestor || node.Shadows.Any(s => s.Visible);
        // Groups may overflow their nominal box; filtered sources can cast visible offscreen shadows.
        if (EnableCulling && !filtered && node.Children.Count == 0 && node.Kind != NodeKind.Text)
        {
            var bounds = Geometry(node).TightBounds;
            var outset = 1d;
            foreach (var stroke in node.Strokes)
                if (stroke.Visible) outset = Math.Max(outset, stroke.Width * .5 * Math.Max(2, stroke.Join == StrokeJoin.Miter ? stroke.MiterLimit : 2));
            var matrix = canvas.TotalMatrix;
            var scale = Math.Max(.000001, Math.Min(Math.Sqrt(matrix.ScaleX * matrix.ScaleX + matrix.SkewY * matrix.SkewY), Math.Sqrt(matrix.ScaleY * matrix.ScaleY + matrix.SkewX * matrix.SkewX)));
            outset += 2 / scale; bounds.Inflate((float)outset, (float)outset);
            if (canvas.QuickReject(bounds)) { CulledNodes++; canvas.Restore(); return; }
        }
        RenderedNodes++;''')
s=s.replace('        if (node.ClipContent)\n','        if (node.ClipContent && !Outlines)\n')
s=s.replace('        foreach (var child in node.Children) DrawNode(canvas, child);','''        if (node.ClippingPath is { } mask && !Outlines)
        {
            using var clip = new SKPath(Geometry(mask)); clip.Transform(Matrix(mask.LocalMatrix));
            canvas.ClipPath(clip, SKClipOperation.Intersect, true);
        }
        foreach (var child in node.Children)
            if (Outlines || child.Id != node.ClipPathId) DrawNode(canvas, child, filtered);''')
s=s.replace('''            var local = node.WorldMatrix.Inverse.Map(point); var inside = node.LocalBounds.Contains(local);
            if (!node.ClipContent || inside)''','''            var local = node.WorldMatrix.Inverse.Map(point); var inside = node.LocalBounds.Contains(local);
            if (node.ClippingPath is { } mask)
            {
                var maskPoint = mask.LocalMatrix.Inverse.Map(local);
                if (!Geometry(mask).Contains((float)maskPoint.X, (float)maskPoint.Y)) continue;
            }
            if (!node.ClipContent || Geometry(node).Contains((float)local.X, (float)local.Y))''')
s=s.replace('                var child = HitTest(node.Children, point, deep, tolerance);','                var child = HitTest(node.Children.Where(c => c.Id != node.ClipPathId), point, deep, tolerance);')
s=s.replace('            var path = Geometry(node);\n            if (node.Fills','''            var path = Geometry(node);
            var pickBounds = path.TightBounds;
            var pickOutset = Math.Max(tolerance, node.Strokes.Count == 0 ? 0 : node.Strokes.Max(s => s.Width) * .5);
            pickBounds.Inflate((float)pickOutset + 1, (float)pickOutset + 1);
            if (!pickBounds.Contains((float)local.X, (float)local.Y)) continue;
            if (node.Fills''')
p.write_text(s)
change('src/ArtSpace.Editor/DesignSurface.cs','    private IReadOnlyList<SnapLine> _snapLines = [];','''    private IReadOnlyList<SnapLine> _snapLines = [];
    private SnapIndex? _snapIndex;
    private bool _dynamicSnapTargets;
    private SnapIndex BuildSnapIndex()
    {
        var editor = Session!; var roots = editor.SelectionRoots;
        return new SnapIndex(editor.Page.AllNodes().Where(n => n.IsEffectivelyVisible && n.Parent?.ClipPathId != n.Id && !editor.SelectedIds.Contains(n.Id) && !roots.Any(n.IsDescendantOf)).Select(n => n.WorldBounds));
    }''')
change('src/ArtSpace.Editor/DesignSurface.cs','            Renderer.ClearCache(); _hover = null;','            if (Session is not null) Renderer.PruneCache(Session.Document.Pages.SelectMany(p => p.Nodes)); _snapIndex = null; _hover = null;')
change('src/ArtSpace.Editor/DesignSurface.cs','        _startBounds = Session.SelectionBounds();','''        _startBounds = Session.SelectionBounds();
        _dynamicSnapTargets = Session.Page.AllNodes().Any(n => n.Layout.Direction != LayoutDirection.None);
        _snapIndex = BuildSnapIndex();''')
change('src/ArtSpace.Editor/DesignSurface.cs','''                    var roots = editor.SelectionRoots;
                    var targets = editor.Page.AllNodes().Where(n => n.IsEffectivelyVisible && !editor.SelectedIds.Contains(n.Id) && !roots.Any(n.IsDescendantOf)).Select(n => n.WorldBounds);
                    var moving = _startBounds with { X = _startBounds.X + delta.X, Y = _startBounds.Y + delta.Y };
                    var snap = SnapEngine.Snap(moving, targets, 5 / editor.Viewport.Zoom, editor.Page.Guides);''','''                    if (_dynamicSnapTargets || _snapIndex is null) _snapIndex = BuildSnapIndex();
                    var moving = _startBounds with { X = _startBounds.X + delta.X, Y = _startBounds.Y + delta.Y };
                    var snap = _snapIndex.Snap(moving, 5 / editor.Viewport.Zoom, editor.Page.Guides);''')
change('src/ArtSpace.Workbench/StudioWorkbench.Illustration.cs','                yield return Item("Close Path",','''                yield return Item("Make Clipping Mask", () => ClippingOperations.Make(Session), "Ctrl 7", selected);
                yield return Item("Release Clipping Mask", () => ClippingOperations.Release(Session), "Ctrl Alt 7", selected);
                yield return Item("Edit Clipping Path", () => { ClippingOperations.EditMask(Session); Surface.EnterPathEditing(); }, enabled: selected);
                yield return Item("Edit Clipped Contents", () => ClippingOperations.EditContents(Session), enabled: selected);
                yield return Item("Close Path",''')
change('src/ArtSpace.Workbench/StudioWorkbench.Illustration.cs','''    private void AddIllustrationSections()
    {''','''    private void AddIllustrationSections()
    {
        if (ClippingOperations.FindGroup(Session.Primary) is not null)
        {
            var clip = AddSection("Clipping Mask");
            clip.Body.Children.Add(new StudioButton("Edit Clipping Path", () => Run(() => { ClippingOperations.EditMask(Session); Surface.EnterPathEditing(); })));
            clip.Body.Children.Add(new StudioButton("Edit Contents", () => Run(() => ClippingOperations.EditContents(Session))));
            clip.Body.Children.Add(new StudioButton("Release Mask", () => Run(() => ClippingOperations.Release(Session))));
        }''')
change('src/ArtSpace.Workbench/StudioWorkbench.Commands.cs','                VirtualKey.Y =>','''                VirtualKey.Number7 => () => { if (Keyboard.Alt) ArtSpace.Illustration.ClippingOperations.Release(Session); else ArtSpace.Illustration.ClippingOperations.Make(Session); },
                VirtualKey.Y =>''')
change('src/ArtSpace.App/Platforms/WebAssembly/BrowserWorkspaceStorage.cs','                json.WriteString("fillRule", primary?.FillRule.ToString());','''                json.WriteString("fillRule", primary?.FillRule.ToString());
                json.WriteString("clipPathId", primary?.ClipPathId);
                json.WriteNumber("clipGroups", session.Page.AllNodes().Count(n => n.ClipPathId is not null));
                json.WriteNumber("geometryBuilds", workbench.Surface.Renderer.GeometryBuilds);
                json.WriteNumber("culledNodes", workbench.Surface.Renderer.CulledNodes);''')
change('Directory.Build.props','0.2.0-alpha.1','0.3.0-alpha.1')
p=root/'src/ArtSpace.Skia/SceneRenderer.Text.cs'; s=p.read_text()
s=s.replace('    private readonly record struct TextRun(string Text, float X, float Baseline);','''    private readonly record struct TextRun(string Text, float X, float Baseline);
    private readonly record struct TextKey(string Text, string Family, int Weight, double Size, double Width, double LineHeight, double Tracking, TextAlignment Alignment);
    private sealed record CachedText(TextKey Key, SKFont Font, TextRun[] Runs);
    private readonly Dictionary<string, CachedText> _textLayouts = [];
    public long TextLayoutBuilds { get; private set; }
    public int CachedTextCount => _textLayouts.Count;
    private void ClearTextLayouts()
    {
        foreach (var entry in _textLayouts.Values) entry.Font.Dispose(); _textLayouts.Clear();
    }
    private CachedText TextLayout(DesignNode node)
    {
        var key = new TextKey(node.Text, node.FontFamily, node.FontWeight, node.FontSize, node.Width, node.LineHeight, node.LetterSpacing, node.TextAlign);
        if (_textLayouts.TryGetValue(node.Id, out var entry) && entry.Key == key) return entry;
        var font = CreateTextFont(node);
        try
        {
            var runs = TextRuns(node, font).ToArray();
            entry?.Font.Dispose();
            if (_textLayouts.Count >= 4096) ClearTextLayouts();
            var result = new CachedText(key, font, runs); _textLayouts[node.Id] = result; TextLayoutBuilds++; return result;
        }
        catch { font.Dispose(); throw; }
    }''')
s=s.replace('''            using var font = CreateTextFont(node);
            foreach (var run in TextRuns(node, font))''','''            var layout = TextLayout(node);
            foreach (var run in layout.Runs)''').replace('font.GetTextPath(run.Text','layout.Font.GetTextPath(run.Text')
s=s.replace('''        using var font = CreateTextFont(node);
        foreach (var run in TextRuns(node, font)) canvas.DrawText(run.Text, run.X, run.Baseline, font, paint);''','''        var layout = TextLayout(node);
        foreach (var run in layout.Runs) canvas.DrawText(run.Text, run.X, run.Baseline, layout.Font, paint);''')
p.write_text(s)
change('src/ArtSpace.Skia/SceneRenderer.cs','public void SetTypeface(SKTypeface typeface) { _customTypeface?.Dispose(); _customTypeface = typeface; }','public void SetTypeface(SKTypeface typeface) { ClearTextLayouts(); _customTypeface?.Dispose(); _customTypeface = typeface; }')
change('src/ArtSpace.Skia/SceneRenderer.cs','        foreach (var p in _paths.Values) p.Path.Dispose(); _paths.Clear();','        foreach (var p in _paths.Values) p.Path.Dispose(); _paths.Clear(); ClearTextLayouts();')
change('src/ArtSpace.Skia/SceneRenderer.cs','''        { _paths[id].Path.Dispose(); _paths.Remove(id); }
    }''','''        { _paths[id].Path.Dispose(); _paths.Remove(id); }
        foreach (var id in _textLayouts.Keys.Where(id => !retained.Contains(id)).ToArray())
        { _textLayouts[id].Font.Dispose(); _textLayouts.Remove(id); }
    }''')
change('src/ArtSpace.Documents/SvgFormat.cs','''        foreach (var child in node.Children) children.Add(ExportNode(child, defs));
        if (children.HasElements) group.Add(children); return group;''','''        var contentTarget = children;
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
        foreach (var child in node.Children) if (child.Id != node.ClipPathId) contentTarget.Add(ExportNode(child, defs));
        if (children.HasElements) group.Add(children); return group;''')
change('src/ArtSpace.Documents/SvgFormat.cs','        var count = 0;','''        var clips = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var definition in root.Descendants().Where(e => e.Name.LocalName == "clipPath"))
        {
            var id = definition.Attribute("id")?.Value;
            if (id is not null && !clips.TryAdd(id, definition)) throw new InvalidDataException("Duplicate SVG clipping identifier.");
        }
        var count = 0;''')
change('src/ArtSpace.Documents/SvgFormat.cs','''            if (element.Attribute("transform") is { } attribute) NodeGeometry.SetLocalMatrix(node, node.LocalMatrix * ParseTransform(attribute.Value));
            if (kind is "g" or "svg") foreach (var child in element.Elements()) { var c = Read(child, depth + 1); if (c is not null) node.Add(c); }
            return node;''','''            if (kind is "g" or "svg") foreach (var child in element.Elements()) { var c = Read(child, depth + 1); if (c is not null) node.Add(c); }
            var clipReference = element.Attribute("clip-path")?.Value ?? Style(element, "clip-path");
            if (!string.IsNullOrWhiteSpace(clipReference) && clipReference != "none")
            {
                var reference = Regex.Match(clipReference, "^url\\\\(\\\\s*['\\\"]?#([^'\\\"\\\\s)]+)['\\\"]?\\\\s*\\\\)$", RegexOptions.CultureInvariant);
                if (!reference.Success || !clips.TryGetValue(reference.Groups[1].Value, out var definition)) throw new InvalidDataException("Missing or external SVG clipping paths are not supported.");
                if ((definition.Attribute("clipPathUnits")?.Value ?? "userSpaceOnUse") != "userSpaceOnUse") throw new InvalidDataException("Only userSpaceOnUse SVG clipping paths are supported.");
                var shapes = definition.Elements().Where(e => e.Name.LocalName is not "title" and not "desc").ToArray();
                if (shapes.Length != 1 || shapes[0].Name.LocalName is not ("path" or "rect" or "circle" or "ellipse" or "polygon" or "polyline" or "line") || shapes[0].Attribute("clip-path") is not null || Style(shapes[0], "clip-path") is not null) throw new InvalidDataException("An SVG clipping definition must contain one vector shape or compound path.");
                var mask = Read(shapes[0], depth + 1) ?? throw new InvalidDataException("Invalid SVG clipping shape.");
                mask.FillRule = (shapes[0].Attribute("clip-rule")?.Value ?? Style(shapes[0], "clip-rule") ?? definition.Attribute("clip-rule")?.Value ?? "nonzero") == "evenodd" ? PathFillRule.EvenOdd : PathFillRule.NonZero;
                mask.Fills.Clear(); mask.Strokes.Clear(); mask.Shadows.Clear();
                if (definition.Attribute("transform") is { } clipTransform) NodeGeometry.SetLocalMatrix(mask, mask.LocalMatrix * ParseTransform(clipTransform.Value));
                var wrapper = new DesignNode { Kind = NodeKind.Group, Name = node.Name + " / Clip Group", Width = node.Width, Height = node.Height, Fills = [], ClipPathId = mask.Id };
                wrapper.Opacity = node.Opacity; node.Opacity = 1; wrapper.Add(node); wrapper.Add(mask); node = wrapper;
            }
            if (element.Attribute("transform") is { } attribute) NodeGeometry.SetLocalMatrix(node, node.LocalMatrix * ParseTransform(attribute.Value));
            return node;''')
p=root/'tests/ArtSpace.Tests/Program.cs'; s=p.read_text(); idx=s.index('var tests ='); s=s[:idx]+'if (args.Contains("--benchmark")) return PerformanceBenchmarks.Run();\n\n'+s[idx:]; s=s.replace('PathEditingTests.Register(Test);','PathEditingTests.Register(Test);\nClippingPerformanceTests.Register(Test);'); p.write_text(s)
