using ArtSpace.Core;
using ArtSpace.Documents;
using ArtSpace.Editing;
using ArtSpace.Illustration;
using ArtSpace.Skia;
using SkiaSharp;

internal static class TypeOnPathBoundaryTests
{
    private static DesignNode Node() => new()
    {
        Kind = NodeKind.Text, Text = "Editable type", TextPath = new(), PathData = "M10 120C100 10 270 10 390 120",
        Width = 400, Height = 180, PathWidth = 400, PathHeight = 180, FontSize = 23, Fill = "#10304A"
    };
    private static EditorSession Editor(DesignNode n) => new(new() { Pages = [new() { Nodes = [n] }] });
    private static void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; } catch (InvalidDataException) { return; }
        throw new Exception("Expected failure.");
    }
    private static double PixelError(byte[] a, byte[] b)
    {
        using var x = SKBitmap.Decode(a); using var y = SKBitmap.Decode(b);
        Check(x.Width == y.Width && x.Height == y.Height); long error = 0, coverage = 0;
        for (var row = 0; row < x.Height; row++) for (var col = 0; col < x.Width; col++)
        {
            var p = x.GetPixel(col, row); var q = y.GetPixel(col, row);
            error += Math.Abs(p.Alpha - q.Alpha); coverage += p.Alpha;
            // Compare premultiplied color, avoiding arbitrary RGB values in nearly-transparent pixels.
            error += Math.Abs(p.Red * p.Alpha - q.Red * q.Alpha) / 255;
            error += Math.Abs(p.Green * p.Alpha - q.Green * q.Alpha) / 255;
            error += Math.Abs(p.Blue * p.Alpha - q.Blue * q.Alpha) / 255;
        }
        return error / (double)Math.Max(1, coverage * 4);
    }
    public static void Register(Action<string, Action> test)
    {
        test("invalid native path baseline is cached as a diagnostic rather than throwing from Draw", () =>
        {
            var n = Node(); n.PathData = "M0 0"; using var r = new SceneRenderer();
            using var surface = SKSurface.Create(new SKImageInfo(440, 240));
            for (var i = 0; i < 20; i++) r.Draw(surface.Canvas, [n]);
            Check(r.GetTypeOnPathStatus(n).Error is not null && r.PathTextLayoutBuilds == 1);
            Reject(() => r.ExportPng([n], new(0, 0, 440, 240))); Reject(() => r.CreateTextOutline(n));
            n.PathData = "M0 120L400 120"; Check(r.GetTypeOnPathStatus(n).Error is null && r.PathTextLayoutBuilds == 2);
        });
        test("multi-contour native baseline does not render misleading text or export missing glyphs", () =>
        {
            var n = Node(); n.PathData = "M0 80L100 80 M150 80L250 80"; using var r = new SceneRenderer();
            Check(r.GetTypeOnPathStatus(n).Error is not null);
            Reject(() => IllustrationSvgExport.Export([n], new(0, 0, 440, 240), r));
        });
        test("path text typeface replacement invalidates measurements and glyph layouts safely", () =>
        {
            var n = Node(); using var r = new SceneRenderer(); r.GetTypeOnPathStatus(n); var old = r.PathTextLayoutBuilds;
            r.SetTypeface(SKTypeface.FromFamilyName("sans-serif")); r.GetTypeOnPathStatus(n);
            Check(r.PathTextLayoutBuilds == old + 1);
        });
        test("path text graphic style application changes appearance but preserves its baseline", () =>
        {
            var n = Node(); var baseline = n.PathData; var settings = n.TextPath!.Clone();
            var style = GraphicStyle.Capture(new DesignNode { Fill = "#AA7722", Effects = [new() { Kind = LiveEffectKind.GaussianBlur }] }, "Soft type");
            using var r = new SceneRenderer(); var before = r.GetTypeOnPathGlyphs(n);
            style.ApplyTo(n);
            Check(n.TextPath is not null && n.PathData == baseline && n.TextPath.Start == settings.Start);
            Check(r.GetTypeOnPathGlyphs(n) == before);
        });
        test("path text inside linked symbols retains independently editable text overrides", () =>
        {
            var n = Node(); var e = Editor(n); e.Select(n); ComponentService.MakeComponent(e); var definition = e.Primary!;
            var instance = ComponentService.InsertInstance(e, definition, new(500, 0)); var child = instance.Children[0];
            Check(child.TextPath is not null && child.Id != n.Id);
            e.Edit("Text override", () => { child.Text = "Override"; ComponentService.SetOverride(child, text: child.Text); });
            e.Edit("Change source", () => { n.TextPath!.Start = .1; n.Fill = "#AA3344"; });
            Check(instance.Children[0].Text == "Override" && instance.Children[0].TextPath!.Start == .1);
            DocumentJson.Validate(e.Document);
        });
        test("path-text SVG export creates vector outlines without mutating native data", () =>
        {
            var n = Node(); n.X = 12; n.Rotation = 8; var e = Editor(n); using var r = new SceneRenderer();
            var before = DocumentJson.Save(e.Document); var region = new RectD(0, 0, 500, 300);
            var svg = IllustrationSvgExport.Export([n], region, r); Check(!svg.Contains("<text") && svg.Contains("<path"));
            Check(before == DocumentJson.Save(e.Document) && !e.CanUndo && n.TextPath is not null);
            var imported = SvgFormat.Import(svg).Document;
            Check(PixelError(r.ExportPng([n], region), r.ExportPng(imported.Pages[0].Nodes, region)) < .003);
        });
        foreach (var coordinateSpace in Enum.GetValues<GradientSpace>())
        {
            test("path text gradient outlines preserve " + coordinateSpace + " painted placement", () =>
            {
                var n = Node(); n.AffineTransform = new(1, .08, .2, 1, 12, 5);
                n.Fills = [new() { Kind = FillKind.LinearGradient, GradientSpace = coordinateSpace,
                    Start = new(0, 0), End = coordinateSpace == GradientSpace.UserSpaceOnUse ? new(400, 0) : new(1, 0),
                    Stops = [new() { Offset = 0, Color = "#FF0000" }, new() { Offset = 1, Color = "#0000FF" }] }];
                using var r = new SceneRenderer(); var region = new RectD(0, 0, 520, 300);
                var reference = r.ExportPng([n], region);
                var svg = IllustrationSvgExport.Export([n], region, r); var imported = SvgFormat.Import(svg).Document;
                var error = PixelError(reference, r.ExportPng(imported.Pages[0].Nodes, region));
                Check(error < .005, $"Gradient path-text export pixel error {error:P5}");
                var editor = Editor(n); editor.Select(n); PathOperations.CreateOutlines(editor, r);
                Check(PixelError(reference, r.ExportPng([n], region)) < .005, "Create Outlines changed the gradient coordinate system.");
            });
        }
        test("unsupported native SVG textPath never silently imports as straight text", () =>
        {
            Reject(() => SvgFormat.Import("<svg><defs><path id='p' d='M0 0L100 0'/></defs><text><textPath href='#p'>Curved</textPath></text></svg>"));
            Reject(() => SvgFormat.Export([Node()], new(0, 0, 440, 240)));
        });
        test("path text export bounds include shifted ink without expanding clipped artboards", () =>
        {
            var n = Node(); n.TextPath!.BaselineShift = 220; using var r = new SceneRenderer();
            var bounds = r.GetArtworkBounds(n); Check(bounds.Y < -100 && bounds.Bottom >= n.WorldBounds.Bottom);
            var frame = new DesignNode { Kind = NodeKind.Frame, Width = 400, Height = 180, ClipContent = true };
            frame.Add(n);
            Check(r.GetArtworkBounds(frame) == frame.WorldBounds, "Clipped artboard dimensions must not grow with hidden text ink.");
            Check(r.GetArtworkBounds(n).Y < -100, "Independent text export must not inherit an external artboard clip.");
            var root = new DesignNode { Kind = NodeKind.Group, Width = 400, Height = 180, Fills = [] };
            root.Add(frame);
            Check(r.GetArtworkBounds(root) == root.WorldBounds, "Nested frame clips must constrain exported descendant ink.");
        });
        test("path text retained replay preserves live effect pixels", () =>
        {
            var n = Node(); n.Effects = [new() { Kind = LiveEffectKind.GaussianBlur, Radius = 2 }];
            using var r = new SceneRenderer(); using var a = new SKBitmap(440, 240); using var b = new SKBitmap(440, 240);
            using var ca = new SKCanvas(a); using var cb = new SKCanvas(b); ca.Clear(SKColors.Transparent); cb.Clear(SKColors.Transparent);
            var page = new DesignPage { Nodes = [n] }; r.Draw(ca, page.Nodes); r.DrawRetained(cb, page, new(0, 0, 440, 240));
            Check(a.Bytes.SequenceEqual(b.Bytes));
        });
    }
}
