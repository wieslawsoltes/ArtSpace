using ArtSpace.Core;
using ArtSpace.Documents;
using ArtSpace.Editing;
using ArtSpace.Illustration;
using ArtSpace.Skia;
using SkiaSharp;

internal static class StrokeBoundaryTests
{
    private static void Check(bool condition, string message = "Stroke boundary assertion failed")
    { if (!condition) throw new InvalidOperationException(message); }
    public static void Register(Action<string, Action> test)
    {
        test("stroke hit tolerance is world-space under anisotropic scale", () =>
        {
            var node = VariableStrokeTests.Line(); node.X = node.Y = 0; node.AffineTransform = Matrix2D.Scale(10, 1);
            using var renderer = new SceneRenderer();
            Check(renderer.HitTest([node], new(-3, 0), tolerance: 4) == node);
            Check(renderer.HitTest([node], new(-30, 0), tolerance: 4) is null);
            Check(renderer.HitTest([node], new(500, 13), tolerance: 4) == node);
            Check(renderer.HitTest([node], new(500, 15), tolerance: 4) is null);
        });
        test("world-space stroke tolerance cache observes transform and zoom radius", () =>
        {
            var node = VariableStrokeTests.Line(); node.X = node.Y = 0;
            using var renderer = new SceneRenderer();
            Check(renderer.HitTest([node], new(50, 13), tolerance: 4) == node);
            var builds = renderer.StrokePickBuilds;
            Check(renderer.HitTest([node], new(50, 13), tolerance: 2) is null); Check(renderer.StrokePickBuilds >= builds);
            node.AffineTransform = Matrix2D.Scale(1, 2);
            Check(renderer.HitTest([node], new(50, 23), tolerance: 4) == node);
            Check(renderer.StrokePickBuilds > builds);
        });
        test("warm uniform stroke hit testing does not allocate geometry or managed collections", () =>
        {
            var node = VariableStrokeTests.Line(); node.Strokes[0].Dashes = [20, 10]; DesignNode[] roots = [node];
            using var renderer = new SceneRenderer();
            for (var i = 0; i < 1000; i++) Check(renderer.HitTest(roots, new(45, 91), tolerance: 2) == node);
            var before = GC.GetAllocatedBytesForCurrentThread(); var outlines = renderer.StrokeOutlineBuilds; var borders = renderer.StrokePickBuilds;
            for (var i = 0; i < 1000; i++) Check(renderer.HitTest(roots, new(45, 91), tolerance: 2) == node);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(allocated == 0, $"Warm picking allocated {allocated} bytes.");
            Check(renderer.StrokeOutlineBuilds == outlines && renderer.StrokePickBuilds == borders);
        });
        test("zero-length variable strokes respect round square and butt caps", () =>
        {
            var node = VariableStrokeTests.Line("M20 20L20 20"); var stroke = node.Strokes[0]; stroke.WidthProfile = [new()];
            using var renderer = new SceneRenderer();
            Check(renderer.StrokeOutline(node, stroke).IsEmpty);
            stroke.Cap = StrokeCap.Round; Check(renderer.StrokeOutline(node, stroke).Contains(25, 20)); Check(!renderer.StrokeOutline(node, stroke).Contains(28, 28));
            stroke.Cap = StrokeCap.Square; Check(renderer.StrokeOutline(node, stroke).Contains(28, 28));
        });
        test("move-only width contours do not unexpectedly become painted dots", () =>
        {
            var node = VariableStrokeTests.Line("M20 20"); node.Strokes[0].WidthProfile = [new()]; node.Strokes[0].Cap = StrokeCap.Round;
            using var renderer = new SceneRenderer(); Check(renderer.StrokeOutline(node, node.Strokes[0]).IsEmpty);
        });
        test("profile changes preserve gradient shaders and paint opacity on a single-fill overlap", () =>
        {
            var node = VariableStrokeTests.Line("M0 0L100 0L50 0"); var stroke = node.Strokes[0]; stroke.Opacity = .5; stroke.WidthProfile = [new()];
            using var renderer = new SceneRenderer(); using var pixels = SKBitmap.Decode(renderer.ExportPng([node], new(0, 0, 240, 200)));
            Check(pixels.GetPixel(110, 80).Alpha is >= 126 and <= 129, "Overlap must not compound opacity.");
        });
        test("width style overrides survive symbol synchronization and undo", () =>
        {
            var source = new DesignNode { Kind = NodeKind.Component, Fills = [] }; source.Add(VariableStrokeTests.Line());
            var editor = new EditorSession(new() { Pages = [new() { Nodes = [source] }] });
            var instance = ComponentService.InsertInstance(editor, source, new(300, 0)); var id = instance.Id;
            editor.Select(instance.Children[0]); using var renderer = new SceneRenderer();
            StrokeProfileOperations.SetPreset(editor, renderer, 0, StrokeWidthPreset.Lens);
            ComponentService.Synchronize(editor.Document);
            Check(editor.Document.Find(id)!.Children[0].Strokes[0].WidthProfile.Count == 7);
            Check(source.Children[0].Strokes[0].WidthProfile.Count == 0);
            editor.Undo(); Check(editor.Document.Find(id)!.Children[0].Strokes[0].WidthProfile.Count == 0);
        });
        test("expanding an opacity-mask source preserves its identity and owner reference", () =>
        {
            var owner = new DesignNode { Kind = NodeKind.Group, Fills = [], Width = 220, Height = 180 };
            owner.Add(new() { Width = 220, Height = 180, Fill = "#2080FF" });
            var source = owner.Add(VariableStrokeTests.Line()); source.Strokes[0].WidthProfile = StrokeProfiles.Create(StrokeWidthPreset.Lens);
            owner.OpacityMaskId = source.Id;
            var editor = new EditorSession(new() { Pages = [new() { Nodes = [owner] }] }); editor.Select(source);
            using var renderer = new SceneRenderer(); using var before = SKBitmap.Decode(renderer.ExportPng([owner], new(0, 0, 240, 200)));
            IllustrationOperations.OutlineStrokes(editor, renderer); Check(owner.OpacityMaskId == source.Id && owner.OpacityMask?.Kind == NodeKind.Group);
            DocumentJson.Validate(editor.Document);
            using var after = SKBitmap.Decode(renderer.ExportPng([owner], new(0, 0, 240, 200))); VariableStrokeTests.Same(before, after, 2);
            editor.Undo(); Check(editor.Document.Find(owner.Id)!.OpacityMaskId == source.Id);
        });
        test("variable gradient SVG keeps stop alpha and user-space transforms", () =>
        {
            var node = VariableStrokeTests.Line("M0 0L100 40"); node.Strokes[0].WidthProfile = StrokeProfiles.Create(StrokeWidthPreset.Diamond);
            node.Strokes[0].Paint = new()
            {
                Kind = FillKind.LinearGradient, GradientSpace = GradientSpace.UserSpaceOnUse,
                Start = new(0, 0), End = new(100, 0), GradientTransform = Matrix2D.Rotation(5), Opacity = .6,
                Stops = [new() { Offset = 0, Color = "#FF0000", Opacity = .5 }, new() { Offset = 1, Color = "#0000FF", Opacity = .8 }]
            };
            using var renderer = new SceneRenderer(); var svg = renderer.ExportSvg([node], new(0, 0, 240, 200));
            var roundtrip = SvgFormat.Import(svg).Document;
            using var before = SKBitmap.Decode(renderer.ExportPng([node], new(0, 0, 240, 200)));
            using var after = SKBitmap.Decode(renderer.ExportPng(roundtrip.Pages[0].Nodes, new(0, 0, 240, 200))); VariableStrokeTests.Same(before, after, 2);
        });
        test("point-storage accounting stays bounded and resets with disposed stroke caches", () =>
        {
            using var renderer = new SceneRenderer();
            for (var i = 0; i < 300; i++)
            {
                var node = VariableStrokeTests.Line(); node.Strokes[0].WidthProfile = [new()]; renderer.StrokeOutline(node, node.Strokes[0]);
                renderer.StrokeCenterlines(node);
            }
            Check(renderer.CachedStrokeCenterlineSamples <= 250_000 && renderer.CachedStrokeOutlinePoints <= 1_000_000);
            renderer.ClearCache(); Check(renderer.CachedStrokeCenterlineSamples == 0 && renderer.CachedStrokeOutlinePoints == 0);
        });
    }
}
