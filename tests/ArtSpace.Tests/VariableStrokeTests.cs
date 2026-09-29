using ArtSpace.Core;
using ArtSpace.Documents;
using ArtSpace.Editing;
using ArtSpace.Illustration;
using ArtSpace.Skia;
using SkiaSharp;

internal static class VariableStrokeTests
{
    internal static DesignNode Line(string data = "M0 0L100 0") => new()
    {
        Kind = NodeKind.Path, X = 40, Y = 80, Width = 100, Height = 100, PathWidth = 100, PathHeight = 100,
        PathData = data, Fills = [], Strokes = [new() { Width = 20, Color = "#E84020", Cap = StrokeCap.Butt }]
    };
    private static EditorSession Editor(DesignNode n) => new(new() { Pages = [new() { Nodes = [n] }] });
    private static void Check(bool condition, string message = "Stroke assertion failed") { if (!condition) throw new InvalidOperationException(message); }
    private static void Near(double a, double b, double epsilon = 1e-5) => Check(Math.Abs(a - b) <= epsilon, $"{a} != {b}");
    private static void Throws(Action action) { try { action(); } catch (Exception e) when (e is InvalidOperationException or InvalidDataException or ArgumentException) { return; } throw new Exception("Expected rejection."); }
    internal static void Same(SKBitmap a, SKBitmap b, int tolerance = 0)
    {
        var x = a.Pixels; var y = b.Pixels; Check(x.Length == y.Length);
        for (var i = 0; i < x.Length; i++)
            Check(Math.Abs(x[i].Alpha - y[i].Alpha) <= tolerance && Math.Abs(x[i].Red - y[i].Red) <= tolerance
                && Math.Abs(x[i].Green - y[i].Green) <= tolerance && Math.Abs(x[i].Blue - y[i].Blue) <= tolerance, "Stroke pixels differ at " + i);
    }
    private static SKBitmap Pixels(SceneRenderer renderer, params DesignNode[] nodes) => SKBitmap.Decode(renderer.ExportPng(nodes, new(0, 0, 240, 200)));
    public static void Register(Action<string, Action> test)
    {
        test("width presets validate and return independently owned knots", () =>
        {
            foreach (var preset in Enum.GetValues<StrokeWidthPreset>()) { var a = StrokeProfiles.Create(preset); var b = StrokeProfiles.Create(preset); StrokeProfiles.Validate(a); if (a.Count > 0) { a[0].Left = 5; Check(b[0].Left != 5); } }
        });
        test("width interpolation is piecewise linear and extends endpoint widths", () =>
        {
            List<StrokeWidthPoint> points = [new() { Position = .2, Left = 0, Right = 1 }, new() { Position = .8, Left = 1, Right = 0 }];
            Near(StrokeProfiles.Evaluate(points, .5).Left, .5); Near(StrokeProfiles.Evaluate(points, 0).Right, 1); Near(StrokeProfiles.Evaluate(points, 1).Left, 1); Near(StrokeProfiles.Evaluate([], .2).Left, .5);
        });
        test("invalid unordered duplicate null and nonfinite width knots fail closed", () =>
        {
            Throws(() => StrokeProfiles.Validate(null)); Throws(() => StrokeProfiles.Validate([null!]));
            Throws(() => StrokeProfiles.Validate([new() { Position = .5 }, new() { Position = .4 }]));
            Throws(() => StrokeProfiles.Validate([new() { Position = .5 }, new() { Position = .5 }]));
            Throws(() => StrokeProfiles.Validate([new() { Left = double.NaN }])); Throws(() => StrokeProfiles.Validate([new() { Right = 17 }]));
            Throws(() => StrokeProfiles.Validate(Enumerable.Range(0, 65).Select(i => new StrokeWidthPoint { Position = i / 64d }).ToArray()));
        });
        test("reversing width profiles exchanges sides and is involutive", () =>
        {
            List<StrokeWidthPoint> points = [new() { Position = .2, Left = .3, Right = .8 }, new() { Position = .9, Left = 1, Right = .4 }];
            var reversed = StrokeProfiles.Reverse(points); Near(reversed[0].Position, .1); Near(reversed[0].Left, .4);
            var restored = StrokeProfiles.Reverse(reversed); Near(restored[0].Position, .2); Near(restored[0].Right, .8);
        });
        test("adaptive centerlines preserve corners and locate normalized arc length", () =>
        {
            using var path = SKPath.ParseSvgPathData("M0 0L100 0L100 100"); var lines = VariableStrokeGeometry.Flatten(path);
            Near(lines[0].Length, 200); var hit = VariableStrokeGeometry.Locate(lines, new(95, 70)); Near(hit.Position, .85); Near(hit.Point.X, 100);
            var at = VariableStrokeGeometry.At(lines, 0, .25); Near(at.Point.X, 50); Near(at.Normal.Y, 1);
        });
        test("adaptive centerlines retain cubic extrema and independent contours", () =>
        {
            using var path = SKPath.ParseSvgPathData("M0 0C0 100 100 100 100 0 M200 0L300 0"); var lines = VariableStrokeGeometry.Flatten(path);
            Check(lines.Length == 2 && lines[0].Samples.Length > 8); Check(lines[0].Samples.Max(p => p.Point.Y) > 74.9); Near(lines[1].Length, 100);
        });
        test("asymmetric width coverage follows exact straight-line side interpolation", () =>
        {
            var n = Line(); n.Strokes[0].WidthProfile = [new() { Position = 0, Left = .25, Right = .75 }, new() { Position = 1, Left = 1, Right = .5 }];
            using var r = new SceneRenderer(); var path = r.StrokeOutline(n, n.Strokes[0]);
            Check(path.Contains(50, 12) && !path.Contains(50, 14)); Check(path.Contains(50, -12) && !path.Contains(50, -14));
        });
        test("taper endpoints remain pointed without replacing centerline geometry", () =>
        {
            var n = Line(); var source = n.PathData; n.Strokes[0].WidthProfile = StrokeProfiles.Create(StrokeWidthPreset.TaperBoth);
            using var r = new SceneRenderer(); var path = r.StrokeOutline(n, n.Strokes[0]);
            Check(path.Contains(50, 9) && !path.Contains(2, 2)); Check(n.PathData == source && n.Kind == NodeKind.Path);
        });
        test("variable stroke butt square and round caps have distinct coverage", () =>
        {
            var n = Line(); n.Strokes[0].WidthProfile = [new() { Position = 0 }]; using var r = new SceneRenderer();
            Check(!r.StrokeOutline(n, n.Strokes[0]).Contains(-5, 0)); n.Strokes[0].Cap = StrokeCap.Square;
            Check(r.StrokeOutline(n, n.Strokes[0]).Contains(-8, 8)); n.Strokes[0].Cap = StrokeCap.Round;
            Check(r.StrokeOutline(n, n.Strokes[0]).Contains(-8, 0) && !r.StrokeOutline(n, n.Strokes[0]).Contains(-8, 8));
        });
        test("variable stroke miter bevel and round joins retain their outer boundaries", () =>
        {
            var n = Line("M0 0L100 0L100 100"); var stroke = n.Strokes[0]; stroke.WidthProfile = [new()]; using var r = new SceneRenderer();
            stroke.Join = StrokeJoin.Miter; Check(r.StrokeOutline(n, stroke).Contains(109, -9));
            stroke.Join = StrokeJoin.Bevel; Check(!r.StrokeOutline(n, stroke).Contains(109, -9));
            stroke.Join = StrokeJoin.Round; Check(r.StrokeOutline(n, stroke).Contains(105, -5));
        });
        test("closed variable contours preserve holes and join the seam", () =>
        {
            var n = Line("M0 0H100V100H0Z"); n.Strokes[0].WidthProfile = [new()]; using var r = new SceneRenderer();
            var path = r.StrokeOutline(n, n.Strokes[0]); Check(path.Contains(2, 50)); Check(!path.Contains(50, 50));
        });
        test("variable dash phase changes coverage and odd sequences repeat", () =>
        {
            var n = Line(); var s = n.Strokes[0]; s.WidthProfile = [new()]; s.Dashes = [10]; using var r = new SceneRenderer();
            Check(r.StrokeOutline(n, s).Contains(5, 0) && !r.StrokeOutline(n, s).Contains(15, 0));
            s.DashOffset = 10; Check(!r.StrokeOutline(n, s).Contains(5, 0) && r.StrokeOutline(n, s).Contains(15, 0));
        });
        test("small dash density fails within bounded geometry work", () =>
        {
            var n = Line(); n.Strokes[0].WidthProfile = [new()]; n.Strokes[0].Dashes = [.00001, .00001]; using var r = new SceneRenderer();
            Throws(() => r.StrokeOutline(n, n.Strokes[0]));
        });
        test("warm width coverage retains geometry across paint placement and opacity edits", () =>
        {
            var n = Line(); var s = n.Strokes[0]; s.WidthProfile = StrokeProfiles.Create(StrokeWidthPreset.Lens); using var r = new SceneRenderer();
            var p = r.StrokeOutline(n, s); var builds = r.StrokeOutlineBuilds; var centers = r.StrokeCenterlineBuilds;
            n.X++; s.Color = "#0000FF"; s.Opacity = .25; Check(ReferenceEquals(p, r.StrokeOutline(n, s))); Check(r.StrokeOutlineBuilds == builds && r.StrokeCenterlineBuilds == centers);
        });
        test("width parameter edits rebuild only the outline while path changes rebuild centerline", () =>
        {
            var n = Line(); var s = n.Strokes[0]; s.WidthProfile = [new()]; using var r = new SceneRenderer(); r.StrokeOutline(n, s);
            s.WidthProfile[0].Left = 1; r.StrokeOutline(n, s); Check(r.StrokeOutlineBuilds == 2 && r.StrokeCenterlineBuilds == 1);
            n.PathData = "M0 0L100 40"; r.StrokeOutline(n, s); Check(r.StrokeCenterlineBuilds == 2);
        });
        test("equal knot replacement is an exact width cache hit", () =>
        {
            var n = Line(); var s = n.Strokes[0]; s.WidthProfile = [new()]; using var r = new SceneRenderer(); var p = r.StrokeOutline(n, s);
            s.WidthProfile = StrokeProfiles.Copy(s.WidthProfile); Check(ReferenceEquals(p, r.StrokeOutline(n, s)));
        });
        test("stroke caches prune deleted entries and release commands on clear", () =>
        {
            var n = Line(); using var r = new SceneRenderer(); r.StrokeOutline(n, n.Strokes[0]); Check(r.CachedStrokeOutlineCount == 1);
            r.PruneCache([]); Check(r.CachedStrokeOutlineCount == 0 && r.CachedStrokeOutlinePoints == 0);
            r.StrokeOutline(n, n.Strokes[0]); r.ClearCache(); Check(r.CachedStrokeOutlineCount == 0);
        });
        test("dash gaps are not picked and visible dash segments remain selectable", () =>
        {
            var n = Line(); n.Strokes[0].Dashes = [10, 20]; using var r = new SceneRenderer();
            Check(r.HitTest([n], new(45, 80), tolerance: 0) == n); Check(r.HitTest([n], new(60, 80), tolerance: 0) is null);
        });
        test("invisible and zero-opacity wide strokes do not inflate picking coverage", () =>
        {
            var n = Line(); n.Strokes = [new() { Width = 100, Visible = false }, new() { Width = 2, Cap = StrokeCap.Butt }]; using var r = new SceneRenderer();
            Check(r.HitTest([n], new(90, 100), tolerance: 0) is null); n.Strokes[0].Visible = true; n.Strokes[0].Opacity = 0;
            Check(r.HitTest([n], new(90, 100), tolerance: 0) is null); Check(r.HitTest([n], new(90, 80), tolerance: 0) == n);
        });
        test("actual stroke caps determine picking at line endpoints", () =>
        {
            var n = Line(); using var r = new SceneRenderer(); Check(r.HitTest([n], new(35, 80), tolerance: 0) is null);
            n.Strokes[0].Cap = StrokeCap.Round; Check(r.HitTest([n], new(35, 80), tolerance: 0) == n);
        });
        test("wide profile regions outside the base weight remain paintable and selectable", () =>
        {
            var n = Line(); n.Strokes[0].WidthProfile = [new() { Left = 4, Right = 1 }]; using var r = new SceneRenderer();
            Check(r.HitTest([n], new(90, 140), tolerance: 0) == n); using var pixels = Pixels(r, n); Check(pixels.GetPixel(90, 140).Alpha > 250);
        });
        test("width presets edits and deletion are atomic and undoable", () =>
        {
            var n = Line(); var e = Editor(n); e.Select(n); using var r = new SceneRenderer();
            StrokeProfileOperations.SetPreset(e, r, 0, StrokeWidthPreset.TaperBoth); Check(e.History.Count == 1);
            StrokeProfileOperations.UpdatePoint(e, r, 0, 1, p => p.Left = 1); Check(e.Primary!.Strokes[0].WidthProfile[1].Left == 1);
            e.Undo(); Near(e.Primary!.Strokes[0].WidthProfile[1].Left, .5); e.Redo(); Near(e.Primary!.Strokes[0].WidthProfile[1].Left, 1);
            StrokeProfileOperations.RemovePoint(e, r, 0, 1); Check(e.Primary!.Strokes[0].WidthProfile.Count == 2);
        });
        test("invalid width transactions restore geometry profile and history", () =>
        {
            var n = Line(); n.Strokes[0].WidthProfile = StrokeProfiles.Create(StrokeWidthPreset.TaperBoth); var e = Editor(n); e.Select(n); using var r = new SceneRenderer();
            Throws(() => StrokeProfileOperations.UpdatePoint(e, r, 0, 1, p => p.Position = -1));
            Check(e.History.Count == 0 && !e.IsInteracting); Near(e.Primary!.Strokes[0].WidthProfile[1].Position, .5);
        });
        test("width commands reject locked vectors and unsupported live text", () =>
        {
            var n = Line(); var e = Editor(n); e.Select(n); n.Locked = true; using var r = new SceneRenderer();
            Throws(() => StrokeProfileOperations.SetPreset(e, r, 0, StrokeWidthPreset.Lens)); Check(e.History.Count == 0);
            n.Locked = false; n.Kind = NodeKind.Text; Throws(() => StrokeProfileOperations.SetPreset(e, r, 0, StrokeWidthPreset.Lens));
        });
        test("schema five preserves asymmetric knots clipboard and old document upgrades", () =>
        {
            var n = Line(); n.Strokes[0].WidthProfile = [new() { Position = .3, Left = .25, Right = 1 }]; var e = Editor(n);
            var saved = DocumentJson.Save(e.Document); var loaded = DocumentJson.Load(saved); Check(loaded.FormatVersion == 5); Near(loaded.AllNodes().Single().Strokes[0].WidthProfile[0].Right, 1);
            var clone = DocumentJson.CloneNode(n, true); clone.Strokes[0].WidthProfile[0].Left = 9; Near(n.Strokes[0].WidthProfile[0].Left, .25);
            var pasted = DocumentJson.LoadNodes(DocumentJson.SaveNodes([n])); Near(pasted[0].Strokes[0].WidthProfile[0].Position, .3);
            var legacy = new DesignDocument { FormatVersion = 4 }; Check(DocumentJson.Load(DocumentJson.Save(legacy)).FormatVersion == 5);
        });
        test("graphic styles own independent width profiles", () =>
        {
            var n = Line(); n.Strokes[0].WidthProfile = [new() { Left = 1 }]; var style = GraphicStyle.Capture(n, "Ink"); var target = Line(); style.ApplyTo(target);
            target.Strokes[0].WidthProfile[0].Left = 5; Near(style.Strokes[0].WidthProfile[0].Left, 1); Near(n.Strokes[0].WidthProfile[0].Left, 1);
        });
        test("unused styles validate width profile data before loading", () =>
        {
            var n = Line(); var style = GraphicStyle.Capture(n, "Bad"); style.Strokes[0].WidthProfile = [new() { Right = double.PositiveInfinity }];
            Throws(() => DocumentJson.Validate(new() { GraphicStyles = [style] }));
        });
        test("variable widths remain visible through retained native scene replay", () =>
        {
            var n = Line("M0 0C20 -40 80 40 100 0"); n.Strokes[0].WidthProfile = StrokeProfiles.Create(StrokeWidthPreset.Lens);
            var e = Editor(n); using var r = new SceneRenderer(); using var direct = LiveAppearanceTests.Pixels(r, e.Page); using var retained = LiveAppearanceTests.Pixels(r, e.Page, true); Same(direct, retained);
        });
        test("expanded variable strokes retain source gradient placement and appearance", () =>
        {
            var n = Line(); n.Strokes[0].WidthProfile = StrokeProfiles.Create(StrokeWidthPreset.TaperBoth); n.Strokes[0].Paint = new() { Kind = FillKind.LinearGradient, Start = new(0, 0), End = new(1, 0) };
            var e = Editor(n); e.Select(n); using var r = new SceneRenderer(); using var before = Pixels(r, n);
            IllustrationOperations.OutlineStrokes(e, r); Check(e.Primary!.Kind == NodeKind.Group); using var after = Pixels(r, e.Primary); Same(before, after, 2);
        });
        test("SVG variable-width export expands coverage without mutating native knots", () =>
        {
            var n = Line(); n.Strokes[0].WidthProfile = StrokeProfiles.Create(StrokeWidthPreset.Lens); using var r = new SceneRenderer();
            var source = n.PathData; var svg = r.ExportSvg([n], new(0, 0, 240, 200)); Check(svg.Contains("data-artspace-expanded-stroke")); Check(n.PathData == source && n.Strokes[0].WidthProfile.Count == 7);
            var imported = SvgFormat.Import(svg).Document; using var a = Pixels(r, n); using var b = Pixels(r, imported.Pages[0].Nodes.ToArray()); Same(a, b, 2);
        });
        test("renderer-free SVG export explicitly rejects variable widths without a geometry provider", () =>
        {
            var n = Line(); n.Strokes[0].WidthProfile = [new()]; Throws(() => SvgFormat.Export([n], new(0, 0, 240, 200)));
        });
    }
}
