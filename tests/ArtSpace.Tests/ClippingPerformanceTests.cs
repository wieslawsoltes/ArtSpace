using ArtSpace.Core;
using ArtSpace.Documents;
using ArtSpace.Editing;
using ArtSpace.Illustration;
using ArtSpace.Layout;
using ArtSpace.Skia;
using SkiaSharp;

internal static class ClippingPerformanceTests
{
    private static DesignNode Shape(double x = 0, double y = 0, double w = 100, double h = 100) => new() { X = x, Y = y, Width = w, Height = h, Fill = "#E08040" };
    private static EditorSession Editor(params DesignNode[] nodes) => new(new() { Pages = [new() { Nodes = [.. nodes] }] });
    private static void Check(bool value) { if (!value) throw new Exception("Assertion failed."); }
    private static void Near(double a, double b) { if (Math.Abs(a - b) > 1e-4) throw new Exception($"{a} != {b}"); }
    private static void Throws(Action action) { try { action(); } catch (InvalidOperationException) { return; } catch (InvalidDataException) { return; } catch (ArgumentException) { return; } throw new Exception("Expected rejection."); }
    private static byte[] Pixels(SceneRenderer renderer, IEnumerable<DesignNode> nodes) => renderer.ExportPng(nodes, new(0, 0, 160, 160));
    public static void Register(Action<string, Action> test)
    {
        test("clipping masks preserve identities and remove only mask appearance", () =>
        {
            var content = Shape(); var mask = Shape(20, 20, 60, 60); mask.Kind = NodeKind.Ellipse;
            var e = Editor(content, mask); e.SelectAll(); ClippingOperations.Make(e);
            var g = e.Primary!; Check(g.ClipPathId == mask.Id && g.ClippingPath == mask && g.Children[0] == content);
            Check(mask.Fills.Count == 0 && mask.Strokes.Count == 0); Near(content.WorldBounds.X, 0); Near(mask.WorldBounds.X, 20);
            DocumentJson.Validate(e.Document); e.Undo(); Check(e.Page.Nodes.Count == 2 && e.Page.Nodes[1].Fills.Count == 1);
            e.Redo(); Check(e.Primary!.ClipPathId == mask.Id);
        });
        test("clipping selection stacking respects unselected siblings", () =>
        {
            var a = Shape(); var b = Shape(); var mask = Shape(); var top = Shape();
            var e = Editor(a, b, mask, top); e.Select([a.Id, mask.Id]); ClippingOperations.Make(e);
            Check(e.Page.Nodes[0] == b && e.Page.Nodes[1].ClippingPath == mask && e.Page.Nodes[2] == top);
        });
        test("clipping a selected group does not destroy its transform or appearance", () =>
        {
            var group = new DesignNode { Kind = NodeKind.Group, Rotation = 23, Opacity = .6, Fills = [] };
            var child = group.Add(Shape()); var mask = group.Add(Shape(10, 10, 70, 70)); var before = child.WorldMatrix;
            var e = Editor(group); e.Select(group); ClippingOperations.Make(e);
            Check(e.Primary == group && group.ClipPathId == mask.Id && child.WorldMatrix == before); Near(group.Opacity, .6);
        });
        test("clipping release keeps grouping and mask geometry", () =>
        {
            var a = Shape(); var b = Shape(20, 20, 60, 60); var e = Editor(a, b); e.SelectAll(); ClippingOperations.Make(e);
            var g = e.Primary!; e.Select(a); ClippingOperations.Release(e);
            Check(g.ClipPathId is null && g.Children.Count == 2 && b.Fills.Count == 0);
            e.Undo(); Check(e.Page.Nodes[0].ClipPathId == b.Id);
        });
        test("clipping path and contents can be selected independently", () =>
        {
            var a = Shape(); var b = Shape(); var e = Editor(a, b); e.SelectAll(); ClippingOperations.Make(e);
            ClippingOperations.EditMask(e); Check(e.Primary == b && e.Tool == EditorTool.DirectSelect);
            ClippingOperations.EditContents(e); Check(e.Primary == a && e.Tool == EditorTool.Move);
        });
        test("clipping rejects text masks without partially editing", () =>
        {
            var a = Shape(); var text = Shape(); text.Kind = NodeKind.Text; var e = Editor(a, text); e.SelectAll();
            var before = DocumentJson.Save(e.Document); Throws(() => ClippingOperations.Make(e)); Check(DocumentJson.Save(e.Document) == before && !e.CanUndo);
        });
        test("clipping rejects locked contents", () =>
        {
            var a = Shape(); a.Locked = true; var mask = Shape(); var e = Editor(a, mask); e.Select([a.Id, mask.Id]);
            Throws(() => ClippingOperations.Make(e)); Check(e.Page.Nodes.Count == 2);
        });
        test("clipping rejects different parents", () =>
        {
            var g = new DesignNode { Kind = NodeKind.Group }; var a = g.Add(Shape()); var b = Shape(); var e = Editor(g, b); e.Select([a.Id, b.Id]);
            Throws(() => ClippingOperations.Make(e));
        });
        test("clipping references validate and survive native roundtrip", () =>
        {
            var a = Shape(); var b = Shape(); var e = Editor(a, b); e.SelectAll(); ClippingOperations.Make(e);
            var loaded = DocumentJson.Load(DocumentJson.Save(e.Document)); Check(loaded.Pages[0].Nodes[0].ClippingPath!.Id == b.Id);
            e.Primary!.ClipPathId = "missing"; Throws(() => DocumentJson.Validate(e.Document));
        });
        test("clipping clones and clipboard remap mask identifiers", () =>
        {
            var e = Editor(Shape(), Shape()); e.SelectAll(); ClippingOperations.Make(e); var g = e.Primary!;
            var copy = DocumentJson.CloneNode(g, true); Check(copy.ClipPathId != g.ClipPathId && copy.ClippingPath is not null);
            var pasted = DocumentJson.LoadNodes(DocumentJson.SaveNodes([g]))[0]; Check(pasted.ClippingPath is not null && pasted.ClipPathId != g.ClipPathId);
        });
        test("deleting a clipping path releases the relation and undo restores it", () =>
        {
            var e = Editor(Shape(), Shape()); e.SelectAll(); ClippingOperations.Make(e); var g = e.Primary!; e.Select(g.ClippingPath);
            e.DeleteSelection(); Check(g.ClipPathId is null); DocumentJson.Validate(e.Document);
            e.Undo(); Check(e.Page.Nodes[0].ClippingPath is not null);
        });
        test("component instance synchronization remaps nested clipping references", () =>
        {
            var e = Editor(Shape(), Shape()); e.SelectAll(); ClippingOperations.Make(e); ComponentService.MakeComponent(e);
            var definition = e.Primary!; var instance = ComponentService.InsertInstance(e, definition, new(200, 0));
            Check(instance.ClippingPath is not null && instance.ClipPathId != definition.ClipPathId);
            e.Edit("move source", () => definition.Children[0].X += 2);
            Check(instance.ClippingPath is not null); DocumentJson.Validate(e.Document);
        });
        test("clip rendering and picking exclude hidden content and preserve ellipse", () =>
        {
            var a = Shape(); var b = Shape(20, 20, 60, 60); b.Kind = NodeKind.Ellipse;
            var e = Editor(a, b); e.SelectAll(); ClippingOperations.Make(e); using var r = new SceneRenderer();
            using var bitmap = SKBitmap.Decode(Pixels(r, e.Page.Nodes));
            Check(bitmap.GetPixel(50, 50).Alpha == 255 && bitmap.GetPixel(22, 22).Alpha == 0 && bitmap.GetPixel(5, 5).Alpha == 0);
            Check(r.HitTest(e.Page.Nodes, new(22, 22), true) is null); Check(r.HitTest(e.Page.Nodes, new(50, 50), true) == a);
            Check(r.HitTest(e.Page.Nodes, new(50, 50)) == e.Primary);
        });
        test("clip geometry ignores mask fill and opacity", () =>
        {
            var e = Editor(Shape(), Shape(20, 20, 60, 60)); e.SelectAll(); ClippingOperations.Make(e);
            e.Primary!.ClippingPath!.Opacity = 0; using var r = new SceneRenderer(); using var b = SKBitmap.Decode(Pixels(r, e.Page.Nodes));
            Check(b.GetPixel(50, 50).Alpha == 255);
        });
        test("compound clipping holes remain empty", () =>
        {
            var mask = Shape(); mask.Kind = NodeKind.Path; mask.FillRule = PathFillRule.EvenOdd;
            mask.PathData = "M10 10H90V90H10Z M35 35H65V65H35Z";
            var e = Editor(Shape(), mask); e.SelectAll(); ClippingOperations.Make(e); using var r = new SceneRenderer();
            using var b = SKBitmap.Decode(Pixels(r, e.Page.Nodes)); Check(b.GetPixel(20, 20).Alpha == 255 && b.GetPixel(50, 50).Alpha == 0);
            Check(r.HitTest(e.Page.Nodes, new(50, 50), true) is null);
        });
        test("clipping releases the full hidden artwork", () =>
        {
            var e = Editor(Shape(), Shape(20, 20, 60, 60)); e.SelectAll(); ClippingOperations.Make(e); ClippingOperations.Release(e);
            using var r = new SceneRenderer(); using var b = SKBitmap.Decode(Pixels(r, e.Page.Nodes)); Check(b.GetPixel(5, 5).Alpha == 255);
        });
        test("SVG clip export import preserves pixel coverage and holes", () =>
        {
            var mask = Shape(); mask.Kind = NodeKind.Path; mask.FillRule = PathFillRule.EvenOdd;
            mask.PathData = "M10 10H90V90H10Z M35 35H65V65H35Z";
            var e = Editor(Shape(), mask); e.SelectAll(); ClippingOperations.Make(e); using var r = new SceneRenderer();
            var svg = SvgFormat.Export(e.Page.Nodes, new(0, 0, 160, 160)); Check(svg.Contains("clip-rule=\"evenodd\""));
            var imported = SvgFormat.Import(svg); Check(imported.Warnings.Count == 0);
            Check(Pixels(r, e.Page.Nodes).SequenceEqual(Pixels(r, imported.Document.Pages[0].Nodes)));
        });
        test("SVG missing or external clip definitions fail closed", () =>
        {
            Throws(() => SvgFormat.Import("<svg><rect width='100' height='100' clip-path='url(#missing)'/></svg>"));
            Throws(() => SvgFormat.Import("<svg><rect clip-path='url(https://example.org/a.svg#x)'/></svg>"));
        });
        test("SVG unsupported clipping units fail closed", () => Throws(() => SvgFormat.Import("<svg><defs><clipPath id='a' clipPathUnits='objectBoundingBox'><rect width='1' height='1'/></clipPath></defs><rect clip-path='url(#a)'/></svg>")));
        test("SVG clipping references cannot recurse", () => Throws(() => SvgFormat.Import("<svg><defs><clipPath id='a'><path d='M0 0H20V20Z' clip-path='url(#a)'/></clipPath></defs><rect clip-path='url(#a)'/></svg>")));
        test("SVG clips on translated primitives use user coordinates", () =>
        {
            var d = SvgFormat.Import("<svg width='160' height='160'><defs><clipPath id='a'><rect x='40' y='40' width='20' height='20'/></clipPath></defs><rect x='20' y='20' width='100' height='100' clip-path='url(#a)'/></svg>").Document;
            using var r = new SceneRenderer(); using var b = SKBitmap.Decode(Pixels(r, d.Pages[0].Nodes)); Check(b.GetPixel(50, 50).Alpha == 255 && b.GetPixel(70, 70).Alpha == 0);
        });
        test("geometry cache hits allocate no SVG strings", () =>
        {
            using var r = new SceneRenderer(); var n = Shape(); n.Kind = NodeKind.Star; var first = r.Geometry(n); var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) Check(ReferenceEquals(first, r.Geometry(n)));
            Check(GC.GetAllocatedBytesForCurrentThread() - allocated < 1024 && r.GeometryBuilds == 1);
        });
        test("geometry cache detects every primitive and point mutation without notifying", () =>
        {
            using var r = new SceneRenderer(); var n = Shape(); r.Geometry(n); n.Width++; r.Geometry(n); n.CornerRadius = 2; r.Geometry(n);
            n.Kind = NodeKind.Path; n.Points = [new() { Position = new(0, 0) }, new() { Position = new(10, 10) }]; r.Geometry(n);
            n.Points[0].ControlOut = new(1, 2); r.Geometry(n); n.Points[1].Position = new(12, 12); r.Geometry(n);
            n.Closed = true; r.Geometry(n); n.FillRule = PathFillRule.EvenOdd; r.Geometry(n); Check(r.GeometryBuilds == 8);
        });
        test("geometry cache ignores appearance placement and string-equivalent replacement", () =>
        {
            using var r = new SceneRenderer(); var n = Shape(); n.PathData = "M0 0H20V20Z"; var a = r.Geometry(n);
            n.X++; n.Rotation = 20; n.Fill = "red"; n.PathData = new string(n.PathData.ToCharArray()); Check(ReferenceEquals(a, r.Geometry(n)));
        });
        test("pruning disposes removed entries without invalidating retained geometry", () =>
        {
            using var r = new SceneRenderer(); var a = Shape(); var b = Shape(); var retained = r.Geometry(a); r.Geometry(b);
            r.PruneCache([a]); Check(r.CachedGeometryCount == 1 && ReferenceEquals(retained, r.Geometry(a)));
        });
        test("culling matches reference pixels with huge strokes and overflowing groups", () =>
        {
            var g = new DesignNode { Kind = NodeKind.Group, Width = 1, Height = 1, Fills = [] };
            g.Add(Shape(30, 40)); g.Add(Shape(4000, 4000)); var stroke = g.Add(Shape(160, 30, 20, 20)); stroke.Strokes = [new() { Width = 90, Join = StrokeJoin.Miter }];
            using var r = new SceneRenderer(); var culled = Pixels(r, [g]); r.EnableCulling = false; Check(culled.SequenceEqual(Pixels(r, [g])));
        });
        test("culling never drops offscreen shadow sources or filtered descendants", () =>
        {
            var g = new DesignNode { Kind = NodeKind.Group, X = -250, Fills = [], Shadows = [new() { X = 300, Y = 0, Blur = 8, Opacity = 1 }] };
            g.Add(Shape(0, 0, 60, 60)); using var r = new SceneRenderer(); var optimized = Pixels(r, [g]); r.EnableCulling = false;
            Check(optimized.SequenceEqual(Pixels(r, [g]))); using var image = SKBitmap.Decode(optimized); Check(image.GetPixel(60, 30).Alpha > 0);
        });
        test("culling reaches children rather than only roots", () =>
        {
            var g = new DesignNode { Kind = NodeKind.Group, Fills = [] }; g.Add(Shape(20, 20, 20, 20));
            for (var i = 0; i < 1000; i++) g.Add(Shape(2000 + i * 10, 2000, 5, 5));
            using var r = new SceneRenderer(); using var surface = SKSurface.Create(new SKImageInfo(160, 160)); r.Draw(surface.Canvas, [g]); Check(r.CulledNodes == 1000 && r.RenderedNodes == 2);
        });
        test("text layout cache invalidates typography but not paint or position", () =>
        {
            var n = Shape(); n.Kind = NodeKind.Text; n.Text = "A wrapped paragraph for layout"; using var r = new SceneRenderer();
            Pixels(r, [n]); Check(r.TextLayoutBuilds == 1); n.Fill = "red"; n.X++; Pixels(r, [n]); Check(r.TextLayoutBuilds == 1);
            n.Width++; Pixels(r, [n]); n.LetterSpacing = 1; Pixels(r, [n]); n.TextAlign = TextAlignment.Center; Pixels(r, [n]);
            Check(r.TextLayoutBuilds == 4); r.PruneCache([]); Check(r.CachedTextCount == 0);
        });
        test("selection snapshots reuse references during viewport and geometry previews", () =>
        {
            var n = Shape(); var e = Editor(n); e.Select(n); var selected = e.Selection; var roots = e.SelectionRoots;
            for (var i = 0; i < 100; i++) { e.Notify(EditorChangeKind.Viewport); e.Preview(); Check(ReferenceEquals(selected, e.Selection) && ReferenceEquals(roots, e.SelectionRoots)); }
            e.Edit("move", () => n.X++); Check(e.Primary == n); e.Undo(); Check(e.Primary != n && e.Primary!.Id == n.Id);
        });
        test("selection caches reflect in-transaction additions and removals", () =>
        {
            var n = Shape(); var e = Editor(n); e.Select(n); _ = e.Selection;
            e.Edit("replace", () => { e.RemoveNode(n); Check(e.Selection.Count == 0); var added = Shape(); e.AddNode(added); e.Select(added); Check(e.Primary == added); });
        });
        test("indexed snapping matches exhaustive correction and guide extents", () =>
        {
            var random = new Random(883); var targets = Enumerable.Range(0, 300).Select(_ => new RectD(random.Next(-1000, 1000), random.Next(-1000, 1000), random.Next(0, 100), random.Next(0, 100))).ToArray();
            var index = new SnapIndex(targets); var guides = new[] { new Guide { Position = 40 }, new Guide { Horizontal = true, Position = 65 } };
            for (var i = 0; i < 1000; i++)
            {
                var box = new RectD(random.Next(-1100, 1100), random.Next(-1100, 1100), random.Next(0, 90), random.Next(0, 90)); var tolerance = random.NextDouble() * 15;
                var a = SnapEngine.Snap(box, targets, tolerance, guides); var b = index.Snap(box, tolerance, guides); Check(a.Correction == b.Correction && a.Lines.SequenceEqual(b.Lines));
            }
        });
        test("indexed snapping retains duplicate-coordinate and symmetric-distance ties", () =>
        {
            var targets = new[] { new RectD(10, 20, 0, 0), new RectD(-10, -20, 0, 0), new RectD(10, -400, 0, 0) };
            var a = SnapEngine.Snap(default, targets, 20); var b = new SnapIndex(targets).Snap(default, 20); Check(a.Correction == b.Correction && a.Lines.SequenceEqual(b.Lines));
        });
        test("indexed snapping is logarithmic in target coordinates", () =>
        {
            var index = new SnapIndex(Enumerable.Range(0, 10000).Select(i => new RectD(i * 20, i * 30, 10, 10))); index.Snap(new(9, 19, 40, 50), 5); Check(index.LastComparisons < 120);
            Check(new SnapIndex([]).Snap(default, 5).Lines.Count == 0); Throws(() => index.Snap(default, double.NaN));
        });
    }
}
