using System.Xml.Linq;
using ArtSpace.Core;
using ArtSpace.Documents;
using ArtSpace.Editing;
using ArtSpace.Illustration;
using ArtSpace.Skia;
using SkiaSharp;

internal static class AppearanceRegressionTests
{
    private static void Check(bool condition, string message = "Appearance regression failed.") { if (!condition) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException) { return; }
        throw new Exception("Expected invalid input rejection.");
    }
    private static DesignNode Gradient(GradientSpace space = GradientSpace.Legacy) => new()
    {
        Width = 100, Height = 100,
        Fills = [new() { Kind = FillKind.LinearGradient, GradientSpace = space, Start = new(0, 0), End = new(1, 0), Stops = [new() { Offset = 0, Color = "#FF0000" }, new() { Offset = 1, Color = "#0000FF" }] }]
    };
    public static void Register(Action<string, Action> test)
    {
        test("retained identity shaders render distinct endpoint colors and correct opacity", () =>
        {
            var n = Gradient(); n.Fills[0].Opacity = .5;
            foreach (var stop in n.Fills[0].Stops) stop.Opacity = .5;
            using var r = new SceneRenderer();
            for (var i = 0; i < 3; i++)
            {
                using var bitmap = SKBitmap.Decode(r.ExportPng([n], new(0, 0, 100, 100)));
                var left = bitmap.GetPixel(5, 50); var right = bitmap.GetPixel(94, 50);
                Check(left.Red > 220 && left.Blue < 40 && right.Blue > 220 && right.Red < 40);
                Check(Math.Abs(left.Alpha - 64) <= 1 && Math.Abs(right.Alpha - 64) <= 1);
            }
            Check(r.GradientBuilds == 1);
        });
        test("gradient coordinate mapping follows actual imported path bounds", () =>
        {
            var n = Gradient(GradientSpace.ObjectBoundingBox); n.Kind = NodeKind.Path; n.PathData = "M20 30H80V70H20Z";
            using var r = new SceneRenderer(); var map = r.GradientCoordinateMatrix(n, n.Fills[0]);
            Check(map.Map(Vec2.Zero).DistanceTo(new(20, 30)) < 1e-6);
            Check(map.Map(new Vec2(1, 1)).DistanceTo(new(80, 70)) < 1e-6);
        });
        test("user-space gradient normalization preserves pixels under affine skew", () => Normalize(GradientSpace.UserSpaceOnUse));
        test("object-box gradient normalization preserves pixels under affine skew", () => Normalize(GradientSpace.ObjectBoundingBox));
        test("affine bounds equal four-corner bounds over randomized transforms", () =>
        {
            var random = new Random(9197);
            for (var i = 0; i < 1000; i++)
            {
                double R() => random.NextDouble() * 40 - 20;
                var m = new Matrix2D(R(), R(), R(), R(), R(), R()); var b = new RectD(R(), R(), R(), R());
                var expected = RectD.Bounds(new[] { m.Map(new Vec2(b.X, b.Y)), m.Map(new Vec2(b.Right, b.Y)), m.Map(new Vec2(b.Right, b.Bottom)), m.Map(new Vec2(b.X, b.Bottom)) });
                var actual = m.Map(b);
                Check(Math.Abs(actual.X - expected.X) < 1e-8 && Math.Abs(actual.Y - expected.Y) < 1e-8 && Math.Abs(actual.Width - expected.Width) < 1e-8 && Math.Abs(actual.Height - expected.Height) < 1e-8);
            }
        });
        test("affine rectangle mapping does not allocate managed arrays", () =>
        {
            var m = new Matrix2D(1, .2, .3, 1.2, 6, 7); var b = new RectD(10, 20, 30, 40); double sum = 0;
            for (var i = 0; i < 100; i++) sum += m.Map(b).Width;
            var start = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10000; i++) sum += m.Map(b).Width;
            var bytes = GC.GetAllocatedBytesForCurrentThread() - start;
            Check(bytes == 0 && sum > 0);
        });
        test("native affine serialization excludes recursively computed properties", () =>
        {
            var n = Gradient(); NodeGeometry.SetExactMatrix(n, new(1, .2, .4, 1.5, 6, 7));
            var text = DocumentJson.Save(new() { Pages = [new() { Nodes = [n] }] });
            Check(!text.Contains("\"inverse\"", StringComparison.Ordinal) && text.Length < 20000);
            var read = DocumentJson.Load(text).Pages[0].Nodes[0];
            Check(read.LocalMatrix.Map(new Vec2(5, 8)).DistanceTo(n.LocalMatrix.Map(new Vec2(5, 8))) < 1e-8);
        });
        test("SVG mask default export region is bounded by export viewport", () =>
        {
            var a = Gradient(); var b = new DesignNode { Width = 100, Height = 100, Fill = "#FFFFFF" };
            var e = new EditorSession(new() { Pages = [new() { Nodes = [a, b] }] }); e.SelectAll(); OpacityMaskOperations.Make(e);
            var xml = XDocument.Parse(SvgFormat.Export(e.Page.Nodes, new(0, 0, 160, 120)));
            var mask = xml.Descendants().Single(x => x.Name.LocalName == "mask");
            Check(double.Parse(mask.Attribute("width")!.Value, System.Globalization.CultureInfo.InvariantCulture) <= 161);
            Check(double.Parse(mask.Attribute("height")!.Value, System.Globalization.CultureInfo.InvariantCulture) <= 121);
        });
        test("SVG transform parsing rejects unknown commands and malformed arity", () =>
        {
            Reject(() => SvgFormat.ParseTransform("translate(10,20) garbage"));
            Reject(() => SvgFormat.ParseTransform("perspective(30)"));
            Reject(() => SvgFormat.ParseTransform("scale(2,3,4)"));
            Reject(() => SvgFormat.ParseTransform("matrix(1,0,0,1,0)"));
            Reject(() => SvgFormat.ParseTransform("translate(1px,2)"));
            Reject(() => SvgFormat.ParseTransform("scale(0)"));
        });
        test("SVG skew and transform order agree with explicit affine matrices", () =>
        {
            var actual = SvgFormat.ParseTransform("translate(12,8) skewX(30) scale(2,3)");
            var expected = Matrix2D.Scale(2, 3) * new Matrix2D(1, 0, Math.Tan(Math.PI / 6), 1, 0, 0) * Matrix2D.Translation(12, 8);
            Check(actual.Map(new Vec2(10, 20)).DistanceTo(expected.Map(new Vec2(10, 20))) < 1e-8);
        });
        test("schemas one and two upgrade to schema four on save", () =>
        {
            foreach (var version in new[] { 1, 2 })
            {
                var json = DocumentJson.Save(new DesignDocument()).Replace("\"formatVersion\":6", "\"formatVersion\":" + version, StringComparison.Ordinal);
                var document = DocumentJson.Load(json); Check(document.FormatVersion == version);
                Check(DocumentJson.Save(document).Contains("\"formatVersion\":6", StringComparison.Ordinal));
            }
        });
    }
    private static void Normalize(GradientSpace space)
    {
        var n = Gradient(space); n.Kind = NodeKind.Path; n.PathData = "M20 30H80V70H20Z";
        if (space == GradientSpace.UserSpaceOnUse) { n.Fills[0].Start = new(20, 30); n.Fills[0].End = new(80, 30); }
        NodeGeometry.SetExactMatrix(n, new(1.2, .15, .3, 1, 5, 7));
        using var r = new SceneRenderer(); var original = r.ExportPng([n], new(0, 0, 160, 140));
        using var geometry = new SKPath(r.Geometry(n)); PathEditing.Write(n, geometry);
        using var before = SKBitmap.Decode(original); using var after = SKBitmap.Decode(r.ExportPng([n], new(0, 0, 160, 140)));
        for (var y = 0; y < before.Height; y++) for (var x = 0; x < before.Width; x++)
        {
            var a = before.GetPixel(x, y); var b = after.GetPixel(x, y);
            Check(Math.Abs(a.Alpha - b.Alpha) <= 2, $"Normalized alpha changed at {x},{y}.");
            if (a.Alpha > 200 && b.Alpha > 200) Check(Math.Abs(a.Red - b.Red) <= 3 && Math.Abs(a.Blue - b.Blue) <= 3, $"Normalized gradient changed at {x},{y}.");
        }
    }
}
