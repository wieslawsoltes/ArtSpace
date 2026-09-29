using ArtSpace.Core;
using ArtSpace.Documents;
using ArtSpace.Editing;
using ArtSpace.Illustration;
using ArtSpace.Skia;
using SkiaSharp;

internal static class TypeOnPathTests
{
    private static DesignNode Baseline(string data = "M10 100L390 100") => new()
    { Kind = NodeKind.Path, Width = 400, Height = 200, PathWidth = 400, PathHeight = 200, PathData = data, Fill = "#111111" };
    private static EditorSession Editor(params DesignNode[] nodes) => new(new() { Pages = [new() { Nodes = [.. nodes] }] });
    private static DesignNode Text(SceneRenderer renderer, string text = "ABCD", string data = "M10 100L390 100")
    {
        var n = Baseline(data); var e = Editor(n); e.Select(n); TypeOnPathOperations.Create(e, renderer, text); return n;
    }
    private static void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
    private static void Near(double a, double b, double tolerance = .002) { if (Math.Abs(a - b) > tolerance) throw new Exception($"{a} != {b}"); }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; } catch (InvalidDataException) { return; } catch (ArgumentException) { return; }
        throw new Exception("Expected validation failure.");
    }
    private static byte[] Pixels(SceneRenderer r, DesignNode n) => r.ExportPng([n], new(0, 0, 440, 260));
    public static void Register(Action<string, Action> test)
    {
        test("path text range validates finite ordered normalized brackets", () =>
        {
            Reject(() => new TypeOnPathOptions { Start = double.NaN }.Validate());
            Reject(() => new TypeOnPathOptions { Start = .8, End = .2 }.Validate());
            Reject(() => new TypeOnPathOptions { End = 1.01 }.Validate());
            Reject(() => new TypeOnPathOptions { BaselineShift = double.PositiveInfinity }.Validate());
            Reject(() => new TypeOnPathOptions { Alignment = (PathTextAlignment)100 }.Validate());
            new TypeOnPathOptions { Start = .5, End = .5 }.Validate();
        });
        test("measured contour preserves open length and tangent", () =>
        {
            using var path = SKPath.ParseSvgPathData("M10 20L110 20"); using var m = new MeasuredContour(path);
            Near(m.Length, 100); Check(!m.IsClosed); var p = m.At(25); Near(p.Position.X, 35); Near(p.Position.Y, 20); Near(p.Tangent.X, 1);
        });
        test("measured closed contour includes closing segment", () =>
        {
            using var path = SKPath.ParseSvgPathData("M0 0L100 0L100 100L0 100Z"); using var m = new MeasuredContour(path);
            Near(m.Length, 400); Check(m.IsClosed); Near(m.At(350).Position.Y, 50);
        });
        test("measured text baseline rejects degenerate and multiple contours", () =>
        {
            using var empty = new SKPath(); Reject(() => { using var m = new MeasuredContour(empty); });
            using var compound = SKPath.ParseSvgPathData("M0 0L10 0 M20 0L30 0"); Reject(() => { using var m = new MeasuredContour(compound); });
        });
        test("path projection reuses its table and resolves line endpoints", () =>
        {
            using var path = SKPath.ParseSvgPathData("M0 0L400 0"); using var m = new MeasuredContour(path);
            Near(m.Project(new(100, 40)), .25, .00001); Near(m.Project(new(-100, 0)), 0); Near(m.Project(new(600, 0)), 1);
            for (var i = 0; i < 100; i++) Near(m.Project(new(200, i)), .5, .00001);
            Check(m.ProjectionTableBuilds == 1);
        });
        test("path projection follows cubic geometry rather than bounding box", () =>
        {
            using var path = SKPath.ParseSvgPathData("M0 150C0 0 300 0 300 150"); using var m = new MeasuredContour(path);
            var sample = m.At(m.Length * .37); Near(m.Project(sample.Position), .37, .0003);
        });
        test("type on a path conversion preserves baseline identity placement and one undo", () =>
        {
            using var r = new SceneRenderer(); var n = Baseline(); n.X = 15; n.Rotation = 12; var original = n.WorldMatrix;
            var e = Editor(n); e.Select(n); TypeOnPathOperations.Create(e, r, "Path text");
            Check(n.Kind == NodeKind.Text && n.TextPath is not null && n.WorldMatrix == original && n.Strokes.Count == 0);
            Check(e.History.Count == 1); e.Undo(); Check(e.Primary!.Kind == NodeKind.Path); e.Redo(); Check(e.Primary!.Text == "Path text");
        });
        test("path text attachment preserves text identity and path stacking", () =>
        {
            using var r = new SceneRenderer(); var t = new DesignNode { Kind = NodeKind.Text, Text = "Attached", Fill = "#AA3344", FontSize = 31 };
            var untouched = new DesignNode(); var p = Baseline(); p.X = 14; p.Rotation = 17; var mapping = p.LocalMatrix;
            var e = Editor(t, untouched, p); e.Select([t.Id, p.Id]); TypeOnPathOperations.Attach(e, r);
            Check(e.Primary == t && e.Page.Nodes.Count == 2 && e.Page.Nodes[0] == untouched && e.Page.Nodes[1] == t);
            Check(t.TextPath is not null && t.Text == "Attached" && t.Fill == "#AA3344"); Near(t.FontSize, 31);
            var q = t.LocalMatrix.Map(new Vec2(23, 67)); var expected = mapping.Map(new Vec2(23, 67)); Near(q.X, expected.X); Near(q.Y, expected.Y);
            e.Undo(); Check(e.Page.Nodes.Count == 3);
        });
        test("path text creation rejects mask sources locked paths and excess text without editing", () =>
        {
            using var r = new SceneRenderer(); var p = Baseline(); p.Locked = true; var e = Editor(p); e.Select(p);
            Reject(() => TypeOnPathOperations.Create(e, r, "Text")); Check(!e.CanUndo);
            p.Locked = false; Reject(() => TypeOnPathOperations.Create(e, r, new string('a', TypeOnPathOptions.MaxTextLength + 1))); Check(!e.CanUndo);
            var g = new DesignNode { Kind = NodeKind.Group, ClipPathId = p.Id }; g.Add(p); g.Add(new()); e = Editor(g); e.Select(p);
            Reject(() => TypeOnPathOperations.Create(e, r, "Text"));
        });
        test("type on a path retains independently cloned options through native persistence", () =>
        {
            using var r = new SceneRenderer(); var n = Text(r); n.TextPath!.Start = .2; n.TextPath.Alignment = PathTextAlignment.Center; n.TextPath.Flip = true;
            var document = Editor(n).Document; var loaded = DocumentJson.Load(DocumentJson.Save(document)); var copy = loaded.Pages[0].Nodes[0];
            Check(loaded.FormatVersion == DocumentJson.CurrentFormatVersion && copy.TextPath!.Flip && copy.PathData == n.PathData);
            var clone = DocumentJson.CloneNode(n, true); clone.TextPath!.Start = .4; Near(n.TextPath.Start, .2);
        });
        test("path text options remain atomic across multiple selections", () =>
        {
            using var r = new SceneRenderer(); var a = Text(r); var b = Text(r); var e = Editor(a, b); e.SelectAll();
            TypeOnPathOperations.Update(e, "Shift baselines", o => o.BaselineShift = 12); Check(e.History.Count == 1);
            Near(a.TextPath!.BaselineShift, 12); Near(b.TextPath!.BaselineShift, 12); e.Undo(); Near(e.Page.Nodes[1].TextPath!.BaselineShift, 0);
        });
        test("invalid path text option edits roll back text and selection", () =>
        {
            using var r = new SceneRenderer(); var n = Text(r); var e = Editor(n); e.Select(n); var before = DocumentJson.Save(e.Document);
            Reject(() => TypeOnPathOperations.Update(e, "Invalid", o => o.Start = 2)); Check(!e.IsInteracting && !e.CanUndo);
            Check(DocumentJson.Save(e.Document) == before && e.Primary!.Id == n.Id);
        });
        test("path text layout follows horizontal glyph advances and baseline", () =>
        {
            using var r = new SceneRenderer(); var n = Text(r); var glyphs = r.GetTypeOnPathGlyphs(n);
            Check(glyphs.Count == 4); Near(glyphs[0].Transform.DY, 100); Near(glyphs[0].Transform.DX, 10);
            Near(glyphs[1].Transform.DX, 10 + glyphs[0].Advance); Check(!r.GetTypeOnPathStatus(n).Overflow);
        });
        test("path text center and right alignment use bracket span", () =>
        {
            using var r = new SceneRenderer(); var n = Text(r); n.TextPath!.Start = .2; n.TextPath.End = .8;
            var s = r.GetTypeOnPathStatus(n); n.TextAlign = TextAlignment.Center;
            var center = r.GetTypeOnPathGlyphs(n)[0]; Near(center.Transform.DX, 10 + 380 * .2 + (s.RangeLength - s.TextAdvance) / 2);
            n.TextAlign = TextAlignment.Right; Near(r.GetTypeOnPathGlyphs(n)[0].Transform.DX, 10 + 380 * .8 - s.TextAdvance);
        });
        test("flipped path text reverses travel and rotates glyphs without mirroring", () =>
        {
            using var r = new SceneRenderer(); var n = Text(r); n.TextPath!.Flip = true; var g = r.GetTypeOnPathGlyphs(n);
            Check(g[1].Distance < g[0].Distance); Near(g[0].Transform.M11, -1); Near(g[0].Transform.M22, -1);
            Near(g[0].Transform.M11 * g[0].Transform.M22 - g[0].Transform.M12 * g[0].Transform.M21, 1);
        });
        test("baseline shift is independent of flipped travel", () =>
        {
            using var r = new SceneRenderer(); var n = Text(r); n.TextPath!.BaselineShift = 15;
            Near(r.GetTypeOnPathGlyphs(n)[0].Transform.DY, 85);
            n.TextPath.Flip = true; Near(r.GetTypeOnPathGlyphs(n)[0].Transform.DY, 115);
        });
        test("ascender descender and center alignments use the same font metrics", () =>
        {
            using var r = new SceneRenderer(); var n = Text(r); n.FontFamily = "sans-serif";
            using var face = SKTypeface.FromFamilyName(n.FontFamily); using var font = new SKFont(face, (float)n.FontSize);
            n.TextPath!.Alignment = PathTextAlignment.Ascender; Near(r.GetTypeOnPathGlyphs(n)[0].Transform.DY, 100 - font.Metrics.Ascent);
            n.TextPath.Alignment = PathTextAlignment.Descender; Near(r.GetTypeOnPathGlyphs(n)[0].Transform.DY, 100 - font.Metrics.Descent);
            n.TextPath.Alignment = PathTextAlignment.Center; Near(r.GetTypeOnPathGlyphs(n)[0].Transform.DY, 100 - (font.Metrics.Ascent + font.Metrics.Descent) / 2);
        });
        test("path text overflow retains source characters without wrapping contours", () =>
        {
            using var r = new SceneRenderer(); var n = Text(r, new string('W', 80)); var s = r.GetTypeOnPathStatus(n);
            Check(s.Overflow && s.TotalGlyphs == 80 && s.VisibleGlyphs < s.TotalGlyphs && n.Text.Length == 80);
            n.TextPath!.Start = n.TextPath.End; s = r.GetTypeOnPathStatus(n); Check(s.Overflow && s.VisibleGlyphs == 0);
        });
        test("empty path text has empty ink and no overflow", () =>
        {
            using var r = new SceneRenderer(); var n = Text(r, ""); var s = r.GetTypeOnPathStatus(n);
            Check(s.TotalGlyphs == 0 && s.VisibleGlyphs == 0 && !s.Overflow); using var p = r.CreateTextOutline(n); Check(p.IsEmpty);
        });
        test("path text enumerates Unicode scalars without splitting surrogate pairs", () =>
        {
            using var r = new SceneRenderer(); var n = Text(r, "A😀B"); var g = r.GetTypeOnPathGlyphs(n);
            Check(g.Count == 3 && g[0].Utf16Index == 0 && g[1].Utf16Index == 1 && g[2].Utf16Index == 3);
        });
        test("path text tracking changes positions without remeasuring baseline", () =>
        {
            using var r = new SceneRenderer(); var n = Text(r); var x = r.GetTypeOnPathGlyphs(n)[1].Transform.DX; var builds = r.TextBaselineBuilds;
            n.LetterSpacing = 5; Near(r.GetTypeOnPathGlyphs(n)[1].Transform.DX, x + 5); Check(r.TextBaselineBuilds == builds);
        });
        test("type on cubic path rotates each glyph to its local tangent", () =>
        {
            using var r = new SceneRenderer(); var n = Text(r, "Curved baseline", "M10 180C30 10 300 10 390 180");
            foreach (var g in r.GetTypeOnPathGlyphs(n))
            {
                var s = r.GetTypeOnPathSample(n, g.Distance / r.GetTypeOnPathStatus(n).PathLength);
                Near(g.Transform.M11, s.Tangent.X); Near(g.Transform.M12, s.Tangent.Y);
            }
        });
        test("warm path text painting reuses glyph geometry and native baseline", () =>
        {
            using var r = new SceneRenderer(); var n = Text(r); using var surface = SKSurface.Create(new SKImageInfo(440, 260)); r.Draw(surface.Canvas, [n]);
            var layout = r.PathTextLayoutBuilds; var baseline = r.TextBaselineBuilds;
            for (var i = 0; i < 20; i++) { n.X = i; n.Fill = i % 2 == 0 ? "#223344" : "#556677"; r.Draw(surface.Canvas, [n]); }
            Check(r.PathTextLayoutBuilds == layout && r.TextBaselineBuilds == baseline);
        });
        test("path text exact cache observes direct options and same-id replacement", () =>
        {
            using var r = new SceneRenderer(); var n = Text(r); r.GetTypeOnPathStatus(n); var count = r.PathTextLayoutBuilds;
            n.TextPath!.Start = .1; r.GetTypeOnPathStatus(n); n.FontSize++; r.GetTypeOnPathStatus(n);
            var copy = DocumentJson.CloneNode(n); copy.TextPath!.Flip = true; r.GetTypeOnPathStatus(copy); Check(r.PathTextLayoutBuilds == count + 3);
        });
        test("path text geometry edits invalidate baseline and layout only when changed", () =>
        {
            using var r = new SceneRenderer(); var n = Text(r); r.GetTypeOnPathStatus(n);
            n.PathData = "M10 120L390 120"; Near(r.GetTypeOnPathGlyphs(n)[0].Transform.DY, 120);
            Check(r.TextBaselineBuilds == 2 && r.PathTextLayoutBuilds == 2);
        });
        test("path text cache resources are released on clear and prune", () =>
        {
            using var r = new SceneRenderer(); var n = Text(r); r.GetTypeOnPathStatus(n);
            Check(r.CachedPathTextCount == 1 && r.ApproximatePathTextBytes > 0); r.PruneCache([]);
            Check(r.CachedPathTextCount == 0 && r.ApproximatePathTextBytes == 0); r.GetTypeOnPathStatus(n); r.ClearCache(); Check(r.CachedPathTextCount == 0);
        });
        test("baseline direct editing keeps path text and its font properties", () =>
        {
            using var r = new SceneRenderer(); var n = Text(r); n.Rotation = 13; var before = n.WorldMatrix.Map(r.GetTypeOnPathGlyphs(n)[0].Transform.Map(Vec2.Zero));
            var basis = n.WorldMatrix; using var geometry = new SKPath(r.Geometry(n)); geometry.Transform(SKMatrix.CreateTranslation(20, 30));
            PathEditing.Write(n, geometry); Check(n.Kind == NodeKind.Text && n.TextPath is not null && n.Text == "ABCD");
            var after = n.WorldMatrix.Map(r.GetTypeOnPathGlyphs(n)[0].Transform.Map(Vec2.Zero)); var delta = basis.Map(new Vec2(20, 30)) - basis.Map(Vec2.Zero);
            Near(after.X, before.X + delta.X); Near(after.Y, before.Y + delta.Y);
        });
        test("path text baseline rejects splitting into multiple contours transactionally", () =>
        {
            using var r = new SceneRenderer(); var n = Text(r); var e = Editor(n); e.Select(n); var before = DocumentJson.Save(e.Document);
            using var split = SKPath.ParseSvgPathData("M0 0L30 0 M50 0L80 0"); Reject(() => e.Edit("Invalid baseline", () => PathEditing.Write(n, split)));
            Check(DocumentJson.Save(e.Document) == before);
        });
        test("path text Create Outlines matches rendered pixels and remains one undo", () =>
        {
            using var r = new SceneRenderer(); var n = Text(r, "Outline me", "M10 150C100 20 250 20 390 150");
            var before = Pixels(r, n); var id = n.Id; var e = Editor(n); e.Select(n); PathOperations.CreateOutlines(e, r);
            Check(n.Kind == NodeKind.Path && n.TextPath is null && n.Id == id); Check(before.SequenceEqual(Pixels(r, n)));
            e.Undo(); Check(e.Primary!.TextPath is not null && e.Primary.Text == "Outline me");
        });
        test("path text hit testing follows glyph ink instead of the baseline rectangle", () =>
        {
            using var r = new SceneRenderer(); var n = Text(r, "MMMM"); using var outline = r.CreateTextOutline(n);
            var found = false;
            for (var x = 10; x < 100 && !found; x++) for (var y = 75; y < 100 && !found; y++)
                if (outline.Contains(x, y)) { Check(r.HitTest([n], new(x, y), true) == n); found = true; }
            Check(found); Check(r.HitTest([n], new(350, 180), true) is null);
        });
        test("path text retained indexed scene matches direct drawing with effects", () =>
        {
            using var r = new SceneRenderer(); var n = Text(r, "Retained curve", "M10 180C70 10 300 20 390 180");
            var page = new DesignPage { Nodes = [n] }; using var bitmap = new SKBitmap(440, 260); using var c = new SKCanvas(bitmap);
            c.Clear(SKColors.Transparent); r.Draw(c, page.Nodes); var direct = bitmap.Bytes;
            c.Clear(SKColors.Transparent); r.DrawRetained(c, page, new(0, 0, 440, 260)); Check(direct.SequenceEqual(bitmap.Bytes));
            var builds = r.PathTextLayoutBuilds; r.DrawRetained(c, page, new(0, 0, 440, 260)); Check(r.SceneRecordings == 1 && r.PathTextLayoutBuilds == builds);
        });
        test("convert path text to area text preserves content and undo", () =>
        {
            using var r = new SceneRenderer(); var n = Text(r); var e = Editor(n); e.Select(n); TypeOnPathOperations.ConvertToAreaText(e);
            Check(n.TextPath is null && n.Kind == NodeKind.Text && n.Text == "ABCD" && n.PathData is null); e.Undo(); Check(e.Primary!.TextPath is not null);
        });
        test("native validation rejects misplaced and oversized path-text payloads", () =>
        {
            var n = new DesignNode { TextPath = new() }; Reject(() => Editor(n));
            n.Kind = NodeKind.Text; n.PathData = "M0 0L100 0"; n.PathWidth = 100; n.PathHeight = 1;
            n.Text = new string('a', TypeOnPathOptions.MaxTextLength + 1); Reject(() => Editor(n));
        });
    }
}
