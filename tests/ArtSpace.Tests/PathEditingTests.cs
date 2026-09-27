using ArtSpace.Core;
using ArtSpace.Documents;
using ArtSpace.Editing;
using ArtSpace.Illustration;
using ArtSpace.Skia;
using SkiaSharp;
using Address = ArtSpace.Core.EditablePath.Address;

internal static class PathEditingTests
{
    public static void Register(Action<string, Action> test)
    {
        test("compound contours remain independent when read and serialized", () =>
        {
            var path = Read("M0 0H100V100H0Z M25 25V75H75V25Z");
            Check(path.Contours.Count == 2 && path.AnchorCount == 8);
            Check(path.Contours.All(c => c.Closed));
            var again = Read(path.ToSvgPathData());
            Check(again.Contours.Count == 2 && again.AnchorCount == 8);
        });
        test("closing cubic merges endpoint without losing incoming control", () =>
        {
            var path = Read("M0 0C20 0 30 20 50 20C30 50 0 30 0 0Z");
            Check(path.AnchorCount == 2 && path.Contours[0].Closed);
            Close(path[new(0, 0)].ControlIn!.Value, new(0, 30));
            Close(Read(path.ToSvgPathData())[new(0, 0)].ControlIn!.Value, new(0, 30));
        });
        test("quadratic degree elevation preserves the exact curve", () =>
        {
            var path = Read("M0 0Q50 100 100 0");
            for (var i = 0; i <= 100; i++)
            {
                var t = i / 100d;
                Close(EditablePath.Evaluate(path[new(0, 0)], path[new(0, 1)], t), new(100 * t, 200 * t * (1 - t)), 1e-4);
            }
        });
        test("conic conversion follows a circular arc within editing tolerance", () =>
        {
            using var sk = new SKPath(); sk.MoveTo(100, 0); sk.ConicTo(100, 100, 0, 100, (float)Math.Sqrt(.5));
            var path = PathEditing.Read(sk, .005);
            var points = path.Contours[0].Points;
            for (var i = 1; i < points.Count; i++)
                for (var j = 0; j <= 32; j++) Close(EditablePath.Evaluate(points[i - 1], points[i], j / 32d).DistanceTo(Vec2.Zero), 100, .01);
        });
        test("conic tolerance rejects invalid parameters", () =>
        {
            using var sk = new SKPath(); sk.AddCircle(0, 0, 10);
            Throws(() => PathEditing.Read(sk, double.NaN)); Throws(() => PathEditing.Read(sk, 0));
        });
        test("inverse paths are not silently converted to bounded shapes", () =>
        {
            using var sk = new SKPath { FillType = SKPathFillType.InverseWinding };
            sk.AddRect(new(0, 0, 10, 10)); Throws(() => PathEditing.Read(sk));
        });
        test("cubic subdivision property test", () =>
        {
            var random = new Random(7007);
            for (var n = 0; n < 100; n++)
            {
                Vec2 Point() => new(random.NextDouble() * 200 - 100, random.NextDouble() * 200 - 100);
                var a = new PathPoint { Position = Point(), ControlOut = Point() };
                var b = new PathPoint { Position = Point(), ControlIn = Point() };
                var path = new EditablePath(); var contour = new EditablePath.Contour(); contour.Points.Add(a); contour.Points.Add(b); path.Contours.Add(contour);
                var originalA = EditablePath.Copy(a); var originalB = EditablePath.Copy(b); var split = .1 + random.NextDouble() * .8;
                path.Split(new(0, 0), split);
                for (var i = 0; i <= 20; i++)
                {
                    var t = i / 20d; var expected = EditablePath.Evaluate(originalA, originalB, t);
                    var actual = t <= split ? EditablePath.Evaluate(contour.Points[0], contour.Points[1], t / split) : EditablePath.Evaluate(contour.Points[1], contour.Points[2], (t - split) / (1 - split));
                    Close(actual, expected, 1e-9);
                }
            }
        });
        test("closing line subdivision preserves contour closure", () =>
        {
            var path = Read("M0 0L100 0L100 100Z");
            var address = path.Split(new(0, 2), .5);
            Check(address == new Address(0, 3)); Close(path[address].Position, new(50, 50));
            Check(path.Contours[0].Closed && path.AnchorCount == 4);
        });
        test("split guards invalid segment and endpoint parameters", () =>
        {
            var path = Read("M0 0L100 100");
            Throws(() => path.Split(new(0, 1), .5)); Throws(() => path.Split(new(0, 0), 0)); Throws(() => path.Split(new(0, 0), 1));
        });
        test("segment hit test reports correct contour and parameter", () =>
        {
            var path = Read("M0 0L100 0 M0 40L100 40"); var hit = path.HitSegment(new(25, 42), 3);
            Check(hit?.Start == new Address(1, 0)); Close(hit!.Value.Parameter, .25); Close(hit.Value.Distance, 2);
            Check(path.HitSegment(new(25, 20), 3) is null);
        });
        test("curved segment hit test includes closing curves", () =>
        {
            var path = Read("M0 0C0 100 100 100 100 0C100 -100 0 -100 0 0Z");
            var hit = path.HitSegment(new(50, -75), 1); Check(hit?.Start == new Address(0, 1)); Close(hit!.Value.Parameter, .5, .01);
        });
        test("anchor movement translates both tangents once", () =>
        {
            var path = Read("M0 0C20 0 80 100 100 100"); var a = new Address(0, 0);
            path.Translate([a, a], new(10, 5)); Close(path[a].Position, new(10, 5)); Close(path[a].ControlOut!.Value, new(30, 5));
        });
        test("direction editing preserves opposite tangent length", () =>
        {
            var path = Read("M0 0C0 0 30 50 50 50C70 50 100 0 100 0");
            path.MoveControl(new(0, 1), false, new(50, 90), false);
            Close(path[new(0, 1)].ControlIn!.Value, new(50, 30));
        });
        test("independent tangent editing does not alter opposite handle", () =>
        {
            var path = Read("M0 0C0 0 30 50 50 50C70 50 100 0 100 0");
            path.MoveControl(new(0, 1), false, new(50, 90), true);
            Close(path[new(0, 1)].ControlIn!.Value, new(30, 50));
        });
        test("delete cuts incident segments rather than connecting across removed anchors", () =>
        {
            var path = Read("M0 0L10 0L20 0L30 0L40 0"); path.Cut([new(0, 2)]);
            Check(path.Contours.Count == 2 && path.Contours.All(c => !c.Closed));
            Check(path.Contours.All(c => c.Points.Count == 2));
            Close(path.Contours[1].Points[0].Position, new(30, 0));
        });
        test("deleting an anchor opens a closed contour", () =>
        {
            var path = Read("M0 0H100V100H0Z"); path.Cut([new(0, 1)]);
            Check(path.Contours.Count == 1 && !path.Contours[0].Closed && path.AnchorCount == 3);
            Close(path[new(0, 0)].Position, new(100, 100));
        });
        test("explicit remove reconnects a contour", () =>
        {
            var path = Read("M0 0H100V100H0Z"); path.Remove([new(0, 1)]);
            Check(path.Contours.Count == 1 && path.Contours[0].Closed && path.AnchorCount == 3);
        });
        test("double reversal restores exact managed path data", () =>
        {
            var path = Read("M0 0C0 50 100 50 100 0L20 20Z M200 200L300 300"); var before = path.ToSvgPathData();
            path.Reverse(); path.Reverse(); Check(before == path.ToSvgPathData());
        });
        test("editing buffer preserves double precision and rejects nonfinite data", () =>
        {
            var path = new EditablePath(); var contour = new EditablePath.Contour(); contour.Points.Add(new() { Position = new(1.23456789012345, 2) }); path.Contours.Add(contour);
            Check(path.ToSvgPathData().Contains("1.23456789012345", StringComparison.Ordinal));
            contour.Points[0].Position = new(double.NaN, 0); Throws(() => path.ToSvgPathData());
        });
        test("even-odd fill survives JSON SVG and Skia hit testing", () =>
        {
            var n = Vector("M0 0H100V100H0Z M25 25H75V75H25Z"); n.FillRule = PathFillRule.EvenOdd;
            var session = Editor(n); var restored = DocumentJson.Load(DocumentJson.Save(session.Document)).Pages[0].Nodes[0];
            using var renderer = new SceneRenderer(); Check(!renderer.Geometry(restored).Contains(50, 50));
            var imported = SvgFormat.Import(SvgFormat.Export([n], n.WorldBounds)).Document.AllNodes().First(x => x.Kind == NodeKind.Path);
            Check(imported.FillRule == PathFillRule.EvenOdd && !renderer.Geometry(imported).Contains(50, 50));
            Check(renderer.HitTest([n], new(50, 50)) is null);
        });
        test("fill rule change invalidates cached geometry", () =>
        {
            var n = Vector("M0 0H100V100H0Z M25 25H75V75H25Z"); using var renderer = new SceneRenderer();
            Check(renderer.Geometry(n).Contains(50, 50)); n.FillRule = PathFillRule.EvenOdd; Check(!renderer.Geometry(n).Contains(50, 50));
        });
        test("SVG inherits even-odd from a group", () =>
        {
            var d = SvgFormat.Import("<svg width='100' height='100' xmlns='http://www.w3.org/2000/svg'><g style='fill-rule:evenodd'><path d='M0 0H100V100H0Z M20 20H80V80H20Z'/></g></svg>").Document;
            Check(d.AllNodes().First(n => n.Kind == NodeKind.Path).FillRule == PathFillRule.EvenOdd);
        });
        test("normalization preserves rotated flipped parent placement and gradient endpoints", () =>
        {
            var parent = new DesignNode { Kind = NodeKind.Group, X = 40, Y = 90, Rotation = 28 };
            var node = parent.Add(Vector("M-10 -20H80V70H-10Z")); node.X = 60; node.Y = 25; node.Rotation = 37; node.FlipX = true;
            node.Fills = [new() { Kind = FillKind.LinearGradient, Start = new(.2, .3), End = new(.9, .8) }];
            var matrix = node.WorldMatrix; var origin = matrix.Map(new Vec2(-10, -20)); var gradient = matrix.Map(new Vec2(20, 30));
            using var renderer = new SceneRenderer(); var path = PathEditing.Read(node, renderer); PathEditing.Write(node, path);
            Close(node.WorldMatrix.Map(Vec2.Zero), origin);
            Close(node.WorldMatrix.Map(new Vec2(node.Fills[0].Start.X * node.Width, node.Fills[0].Start.Y * node.Height)), gradient);
        });
        test("absolute gesture updates do not accumulate normalization drift", () =>
        {
            var node = Vector("M0 0H100V100H0Z"); node.Rotation = 41; var basis = DocumentJson.CloneNode(node);
            using var renderer = new SceneRenderer(); var path = PathEditing.Read(node, renderer); var point = path[new(0, 0)].Position;
            for (var i = 1; i <= 50; i++) { path[new(0, 0)].Position = point + new Vec2(-i, -i); PathEditing.Write(node, path, basis); }
            var actual = renderer.Geometry(node); using var moved = new SKPath(actual); moved.Transform(SceneRenderer.Matrix(node.LocalMatrix));
            var expected = basis.LocalMatrix.Map(new Vec2(-50, -50));
            var buffer = PathEditing.Read(moved); Close(buffer[new(0, 0)].Position, expected, .001);
        });
        test("all-contour anchor commands are atomic and reversible", () =>
        {
            var node = Vector("M0 0H100V100H0Z M25 25H75V75H25Z"); node.FillRule = PathFillRule.EvenOdd;
            var editor = Editor(node); editor.Select(node); using var renderer = new SceneRenderer();
            PathOperations.AddAnchors(editor, renderer); Check(PathEditing.Read(editor.Primary!, renderer).AnchorCount == 16); Check(editor.History.Count == 1);
            editor.Undo(); Check(editor.Primary!.PathData == "M0 0H100V100H0Z M25 25H75V75H25Z");
        });
        test("compound path uses topmost appearance and preserves unselected sibling order", () =>
        {
            var a = new DesignNode(); var middle = new DesignNode { Name = "untouched" }; var b = new DesignNode { X = 25, Y = 25, Width = 50, Height = 50, Fill = "#EE2211" };
            var editor = Editor(a, middle, b); editor.Select([a.Id, b.Id]); using var renderer = new SceneRenderer();
            PathOperations.MakeCompound(editor, renderer);
            Check(editor.Page.Nodes.Count == 2 && editor.Page.Nodes[0] == middle); Check(editor.Primary!.Fill == b.Fill);
            Check(editor.Primary.FillRule == PathFillRule.EvenOdd); Check(PathEditing.Read(editor.Primary, renderer).Contours.Count == 2);
            Check(renderer.HitTest([editor.Primary], new(50, 50)) is null);
            editor.Undo(); Check(editor.Page.Nodes.Count == 3);
        });
        test("compound release creates independent editable contours", () =>
        {
            var n = Vector("M0 0H100V100H0Z M25 25H75V75H25Z"); var editor = Editor(n); editor.Select(n); using var renderer = new SceneRenderer();
            PathOperations.ReleaseCompound(editor, renderer); Check(editor.Page.Nodes.Count == 2);
            Check(editor.Page.Nodes.All(p => PathEditing.Read(p, renderer).Contours.Count == 1));
            editor.Undo(); Check(editor.Page.Nodes.Count == 1);
        });
        test("text outlines preserve world geometry identifiers and appearance", () =>
        {
            var n = Text(); n.Rotation = 25; n.FlipX = true; n.Strokes = [new() { Width = 2, Color = "#123456" }];
            var editor = Editor(n); editor.Select(n); using var renderer = new SceneRenderer();
            using var expected = renderer.CreateTextOutline(n); expected.Transform(SceneRenderer.Matrix(n.WorldMatrix));
            var id = n.Id; PathOperations.CreateOutlines(editor, renderer);
            Check(n.Kind == NodeKind.Path && n.Id == id && n.Strokes.Count == 1 && n.Text == "");
            using var actual = new SKPath(renderer.Geometry(n)); actual.Transform(SceneRenderer.Matrix(n.WorldMatrix));
            Close(actual.TightBounds.Left, expected.TightBounds.Left, .001); Close(actual.TightBounds.Top, expected.TightBounds.Top, .001);
            Close(actual.TightBounds.Right, expected.TightBounds.Right, .001); Close(actual.TightBounds.Bottom, expected.TightBounds.Bottom, .001);
            Check(PathEditing.Read(n, renderer).Contours.Count > 1); Check(!SvgFormat.Export([n], n.WorldBounds).Contains("<text"));
            editor.Undo(); Check(editor.Primary!.Kind == NodeKind.Text && editor.Primary.Text == "Outline O8");
            editor.Redo(); Check(editor.Primary!.Kind == NodeKind.Path);
        });
        test("wrapped tracked text outlines match the rendered glyph coverage", () =>
        {
            var n = Text(); n.Text = "Outline words with tracking\nO8 B"; n.Width = 230; n.Height = 180; n.LetterSpacing = 3; n.TextAlign = TextAlignment.Center;
            var editor = Editor(n); editor.Select(n); using var renderer = new SceneRenderer(); var bounds = new RectD(-20, -20, 300, 250);
            using var before = SKBitmap.Decode(renderer.ExportPng([n], bounds));
            PathOperations.CreateOutlines(editor, renderer);
            using var after = SKBitmap.Decode(renderer.ExportPng([n], bounds));
            long alphaError = 0, coverage = 0;
            for (var y = 0; y < before.Height; y++) for (var x = 0; x < before.Width; x++)
            {
                var a = before.GetPixel(x, y).Alpha; var b = after.GetPixel(x, y).Alpha;
                alphaError += Math.Abs(a - b); coverage += a;
            }
            Check(coverage > 100_000); Check(alphaError / (double)coverage < .18, $"Glyph coverage error {alphaError / (double)coverage:P1}");
        });
        test("text outline conversion is one transaction across groups", () =>
        {
            var group = new DesignNode { Kind = NodeKind.Group, Fills = [] }; group.Add(Text()); group.Add(Text());
            var editor = Editor(group); editor.Select(group); using var renderer = new SceneRenderer(); PathOperations.CreateOutlines(editor, renderer);
            Check(group.Children.All(n => n.Kind == NodeKind.Path)); Check(editor.History.Count == 1);
            editor.Undo(); Check(editor.Primary!.Children.All(n => n.Kind == NodeKind.Text));
        });
        test("whitespace outlines do not destroy the original text", () =>
        {
            var n = Text(); n.Text = "  \n "; var editor = Editor(n); editor.Select(n); using var renderer = new SceneRenderer();
            PathOperations.CreateOutlines(editor, renderer); Check(n.Kind == NodeKind.Text && editor.History.Count == 0);
        });
        test("locked text is not outlined", () =>
        {
            var n = Text(); n.Locked = true; var editor = Editor(n); editor.Select(n); using var renderer = new SceneRenderer();
            Throws(() => PathOperations.CreateOutlines(editor, renderer)); Check(n.Kind == NodeKind.Text);
        });
        test("failed contour edit rolls back the document", () =>
        {
            var n = Vector("M0 0H100V100Z"); var editor = Editor(n); editor.Select(n); using var renderer = new SceneRenderer(); var before = DocumentJson.Save(editor.Document);
            Throws(() => PathOperations.Edit(editor, renderer, "Bad edit", p => p.Contours[0].Points[0].Position = new(double.NaN, 0)));
            Check(DocumentJson.Save(editor.Document) == before && editor.History.Count == 0);
        });
    }
    private static EditablePath Read(string data) { using var path = SKPath.ParseSvgPathData(data); return PathEditing.Read(path); }
    private static DesignNode Vector(string data) => new() { Kind = NodeKind.Path, PathData = data, PathWidth = 100, PathHeight = 100, Width = 100, Height = 100 };
    private static DesignNode Text() => new() { Kind = NodeKind.Text, Text = "Outline O8", FontFamily = "sans-serif", FontSize = 32, Width = 400, Height = 80 };
    private static EditorSession Editor(params DesignNode[] nodes) => new(new() { Pages = [new() { Nodes = nodes.ToList() }] });
    private static void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new InvalidOperationException(message); }
    private static void Close(double actual, double expected, double tolerance = .0001) { if (!double.IsFinite(actual) || Math.Abs(actual - expected) > tolerance) throw new InvalidOperationException($"Expected {expected:R}, got {actual:R} (tolerance {tolerance})."); }
    private static void Close(Vec2 actual, Vec2 expected, double tolerance = .0001) { Close(actual.X, expected.X, tolerance); Close(actual.Y, expected.Y, tolerance); }
    private static void Throws(Action action) { try { action(); } catch { return; } throw new InvalidOperationException("Expected an exception."); }
}
