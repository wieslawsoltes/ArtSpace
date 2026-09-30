using ArtSpace.Core;
using ArtSpace.Documents;
using ArtSpace.Editing;
using ArtSpace.Illustration;
using ArtSpace.Skia;
using SkiaSharp;
using System.Xml.Linq;

internal static class SvgTextPathTests
{
    private static void Check(bool value, string message = "SVG path-text assertion failed.") { if (!value) throw new Exception(message); }
    private static void Near(double a, double b, double tolerance = .002) => Check(Math.Abs(a - b) < tolerance, $"{a} differs from {b}");
    private static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } catch (InvalidOperationException) { return; } throw new Exception("Expected an explicit rejection."); }
    private static string Svg(string attrs = "", string text = "ABC", string textAttrs = "", string geometry = "<path id='baseline' d='M20 100H420'/>") =>
        $"<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' width='500' height='240'><defs>{geometry}</defs><text id='label' font-size='20' {textAttrs}><textPath href='#baseline' {attrs}>{text}</textPath></text></svg>";
    private static DesignNode Import(string source) => SvgFormat.Import(source).Document.AllNodes().Single(n => n.TextPath is not null);
    private static EditorSession Editor(DesignNode node) => new(new() { Pages = [new() { Nodes = [node] }] });
    private static void Similar(byte[] first, byte[] second, double tolerance = .015)
    {
        using var a = SKBitmap.Decode(first); using var b = SKBitmap.Decode(second);
        Check(a.Width == b.Width && a.Height == b.Height);
        long error = 0, coverage = 0;
        for (var y = 0; y < a.Height; y++) for (var x = 0; x < a.Width; x++)
        { var p = a.GetPixel(x, y); var q = b.GetPixel(x, y); error += Math.Abs(p.Alpha - q.Alpha); coverage += p.Alpha; }
        Check(coverage > 1000 && error / (double)coverage < tolerance, $"Alpha coverage error: {error}/{coverage}");
    }
    public static void Register(Action<string, Action> test)
    {
        test("SVG path text remains editable and stores a detached baseline", () =>
        {
            var n = Import(Svg()); Check(n.Kind == NodeKind.Text && n.Text == "ABC");
            Check(n.TextPath!.SvgPosition is { Percentage: false, Offset: 0 });
            Check(n.PathData!.Contains("420")); var e = Editor(n);
            var copy = DocumentJson.CloneNode(n, true); copy.TextPath!.SvgPosition!.Offset = 12;
            Check(n.TextPath.SvgPosition.Offset == 0 && copy.Id != n.Id); DocumentJson.Validate(e.Document);
        });
        test("SVG offset unit changes use each selected contour's own length", () =>
        {
            var a = Import(Svg("startOffset='50%'"));
            var b = Import(Svg("startOffset='25%'", geometry: "<path id='baseline' d='M0 60H100'/>"));
            var editor = new EditorSession(new() { Pages = [new() { Nodes = [a, b] }] });
            editor.SelectAll(); using var renderer = new SceneRenderer();
            TypeOnPathOperations.SetSvgOffsetUnits(editor, renderer, false);
            Near(a.TextPath!.SvgPosition!.Offset, 200); Near(b.TextPath!.SvgPosition!.Offset, 25);
            Check(!a.TextPath.SvgPosition.Percentage && !b.TextPath.SvgPosition.Percentage && editor.History.Count == 1);
            TypeOnPathOperations.SetSvgOffsetUnits(editor, renderer, false); Check(editor.History.Count == 1);
            editor.Undo(); Check(editor.Page.Nodes.All(n => n.TextPath!.SvgPosition!.Percentage));
        });
        test("SVG offset unit changes retain authored pathLength and locked artwork", () =>
        {
            var a = Import(Svg("startOffset='25'", geometry: "<path id='baseline' pathLength='100' d='M0 60H400'/>"));
            var b = DocumentJson.CloneNode(a, true); b.Locked = true;
            var editor = new EditorSession(new() { Pages = [new() { Nodes = [a, b] }] });
            editor.Select([a.Id, b.Id]); using var renderer = new SceneRenderer();
            TypeOnPathOperations.SetSvgOffsetUnits(editor, renderer, true);
            Near(a.TextPath!.SvgPosition!.Offset, 25); Near(a.TextPath.SvgPosition.PathLength!.Value, 100);
            Check(a.TextPath.SvgPosition.Percentage && !b.TextPath!.SvgPosition!.Percentage);
        });
        test("SVG legacy xlink and inline baseline precedence are supported", () =>
        {
            Check(Import(Svg().Replace("href='#baseline'", "xlink:href='#baseline'")).Text == "ABC");
            var n = Import(Svg("path='M0 30H100'")); using var r = new SceneRenderer();
            Near(r.GetTypeOnPathStatus(n).PathLength, 100);
        });
        test("SVG baseline own transform does not scale glyphs or use definition ancestors", () =>
        {
            var source = Svg(geometry: "<g transform='translate(900,800)'><path id='baseline' transform='translate(10,20) scale(2)' d='M0 40H100'/></g>");
            var n = Import(source); using var r = new SceneRenderer(); var status = r.GetTypeOnPathStatus(n);
            Near(status.PathLength, 200); var sample = r.GetTypeOnPathSample(n, 0); Near(sample.Position.X, 10); Near(sample.Position.Y, 100);
            var plain = Import(Svg()); Near(r.GetTypeOnPathGlyphs(n)[0].Advance, r.GetTypeOnPathGlyphs(plain)[0].Advance);
        });
        test("SVG path text inherits paint typography and preserves text-anchor", () =>
        {
            var n = Import("<svg width='500' height='200'><defs><path id='b' d='M0 80H400'/></defs><g fill='#123456' font-size='24pt' font-weight='bold' letter-spacing='2' text-anchor='middle'><text><textPath href='#b' startOffset='50%'>ABC</textPath></text></g></svg>");
            Check(n.Fill == "#123456" && n.FontWeight == 700 && n.TextAlign == TextAlignment.Center); Near(n.FontSize, 32); Near(n.LetterSpacing, 2);
        });
        foreach (var anchor in new[] { "start", "middle", "end" })
        {
            test("SVG text-anchor " + anchor + " positions relative to startOffset", () =>
            {
                var n = Import(Svg("startOffset='50%'", textAttrs: "text-anchor='" + anchor + "'")); using var r = new SceneRenderer();
                var status = r.GetTypeOnPathStatus(n); var glyph = r.GetTypeOnPathGlyphs(n)[0];
                var expected = 200 - status.TextAdvance * (anchor == "middle" ? .5 : anchor == "end" ? 1 : 0) + glyph.Advance / 2;
                Near(glyph.Distance, expected);
            });
        }
        test("SVG authored pathLength calibrates numeric offsets but not percentage offsets", () =>
        {
            var n = Import(Svg("startOffset='25'", geometry: "<path id='baseline' pathLength='100' d='M20 100H420'/>")); using var r = new SceneRenderer();
            var g = r.GetTypeOnPathGlyphs(n)[0]; Near(g.Distance, 100 + g.Advance / 2);
            n.TextPath!.SvgPosition!.Percentage = true;
            var h = r.GetTypeOnPathGlyphs(n)[0]; Near(h.Distance, g.Distance);
            n.TextPath.SvgPosition.Offset = 50; Near(r.GetTypeOnPathGlyphs(n)[0].Distance, 200 + h.Advance / 2);
        });
        test("SVG open paths clip glyph midpoints rather than whole character boxes", () =>
        {
            var n = Import(Svg("startOffset='-3'", "A")); using var r = new SceneRenderer();
            Check(r.GetTypeOnPathStatus(n).VisibleGlyphs == 1);
            n.TextPath!.SvgPosition!.Offset = -100; Check(r.GetTypeOnPathStatus(n).VisibleGlyphs == 0 && n.Text == "A");
            n.TextPath.SvgPosition.Offset = 500; Check(r.GetTypeOnPathStatus(n).Overflow);
        });
        test("SVG right-side layout reverses travel without mirroring glyphs", () =>
        {
            var n = Import(Svg("startOffset='25%' side='right'")); using var r = new SceneRenderer(); var glyph = r.GetTypeOnPathGlyphs(n)[0];
            Near(glyph.Distance, 300 - glyph.Advance / 2); Near(glyph.Transform.M11, -1);
            Near(glyph.Transform.M11 * glyph.Transform.M22 - glyph.Transform.M12 * glyph.Transform.M21, 1);
        });
        test("SVG closed path text crosses the seam once without dropping the source text", () =>
        {
            var n = Import(Svg("startOffset='98%'", "ABCDEFGHIJ", geometry: "<path id='baseline' d='M20 50H120V150H20Z'/>")); using var r = new SceneRenderer();
            var status = r.GetTypeOnPathStatus(n); Check(status.VisibleGlyphs == 10 && !status.Overflow);
            Check(r.GetTypeOnPathGlyphs(n).Any(g => g.Distance < 100));
            n.Text = new string('W', 100); Check(r.GetTypeOnPathStatus(n).Overflow && n.Text.Length == 100);
        });
        foreach (var geometry in new[] { "<circle id='baseline' cx='200' cy='100' r='80'/>", "<ellipse id='baseline' cx='200' cy='100' rx='80' ry='40'/>", "<line id='baseline' x1='0' y1='100' x2='400' y2='100'/>", "<polyline id='baseline' points='0,80 200,80 300,120'/>", "<polygon id='baseline' points='0,80 200,80 300,120'/>", "<rect id='baseline' x='30' y='40' width='300' height='150' rx='10'/>" })
        {
            test("SVG basic-shape baseline " + geometry.Split(' ')[0][1..], () =>
            { using var r = new SceneRenderer(); var n = Import(Svg(geometry: geometry)); Check(r.GetTypeOnPathStatus(n).VisibleGlyphs == 3); });
        }
        test("SVG whitespace collapse preserves nonbreaking spaces and xml preserve", () =>
        {
            Check(Import(Svg(text: "  A \n B&#160;C\t ")).Text == "A B\u00a0C");
            Check(Import(Svg(text: " A  B ", textAttrs: "xml:space='preserve'")).Text == " A  B ");
        });
        test("SVG scalar dy shifts the glyph baseline without changing contour geometry", () =>
        {
            var n = Import(Svg(textAttrs: "dy='-12'")); using var r = new SceneRenderer(); var glyph = r.GetTypeOnPathGlyphs(n)[0];
            Near(glyph.Transform.DY, 88); Near(r.GetTypeOnPathSample(n, 0).Position.Y, 100);
        });
        test("SVG path-text options are deeply cloned and retained by schema six", () =>
        {
            var n = Import(Svg("startOffset='33%'", geometry: "<path id='baseline' pathLength='200' transform='scale(2)' d='M0 60H200'/>"));
            var e = Editor(n); var saved = DocumentJson.Save(e.Document); Check(saved.Contains("\"formatVersion\":6"));
            var loaded = DocumentJson.Load(saved).AllNodes().Single(); Check(loaded.TextPath!.SvgPosition!.Percentage && loaded.TextPath.SvgPosition.PathLength == 200);
            var cloned = n.TextPath!.Clone(); cloned.SvgPosition!.Offset = 90; Near(n.TextPath.SvgPosition!.Offset, 33);
        });
        test("SVG offset changes rebuild glyph layout without remeasuring the baseline", () =>
        {
            var n = Import(Svg("startOffset='10%'")); using var r = new SceneRenderer(); r.GetTypeOnPathStatus(n);
            var baseline = r.TextBaselineBuilds; var layouts = r.PathTextLayoutBuilds;
            n.TextPath!.SvgPosition!.Offset = 20; r.GetTypeOnPathStatus(n);
            Check(r.TextBaselineBuilds == baseline && r.PathTextLayoutBuilds == layouts + 1);
            n.TextPath.SvgPosition.PathTransform = Matrix2D.Scale(2, 2); r.GetTypeOnPathStatus(n);
            Check(r.TextBaselineBuilds == baseline + 1);
        });
        test("SVG same-id option replacement does not return stale glyph geometry", () =>
        {
            var n = Import(Svg("startOffset='10%'")); using var r = new SceneRenderer(); var before = r.GetTypeOnPathGlyphs(n)[0].Distance;
            var copy = DocumentJson.CloneNode(n); copy.TextPath!.SvgPosition!.Offset = 60;
            Check(r.GetTypeOnPathGlyphs(copy)[0].Distance > before + 100);
        });
        test("SVG baseline editing bakes the reference transform only once", () =>
        {
            var n = Import(Svg(geometry: "<path id='baseline' transform='translate(10,20) scale(2)' d='M0 40H200'/>")); using var r = new SceneRenderer();
            var bounds = new RectD(0, 0, 500, 240); var before = r.ExportPng([n], bounds);
            var geometry = PathEditing.Read(n, r); PathEditing.Write(n, geometry);
            Check(n.TextPath!.SvgPosition!.PathTransform == Matrix2D.Identity); Similar(before, r.ExportPng([n], bounds));
        });
        test("SVG offset-unit conversion preserves resolved placement", () =>
        {
            var p = new SvgTextPathPosition { Offset = 25, Percentage = true, PathLength = 60 };
            var distance = p.Resolve(400); p.Percentage = false; p.Offset = p.FromDistance(distance, 400); Near(p.Offset, 15); Near(p.Resolve(400), distance);
        });
        test("SVG direct editable export and reimport keep text and alpha coverage", () =>
        {
            var n = Import(Svg("startOffset='50%'", textAttrs: "text-anchor='middle' dy='-8'")); using var r = new SceneRenderer();
            var bounds = new RectD(0, 0, 500, 240); var svg = IllustrationSvgExport.Export([n], bounds, r, SvgPathTextExportMode.Editable);
            Check(svg.Contains("textPath") && svg.Contains("startOffset=\"50%\"")); var imported = Import(svg);
            Check(imported.Text == "ABC"); Similar(r.ExportPng([n], bounds), r.ExportPng([imported], bounds));
        });
        test("default SVG export still uses outlines without changing the editable document", () =>
        {
            var n = Import(Svg()); var e = Editor(n); using var r = new SceneRenderer(); var before = DocumentJson.Save(e.Document);
            var svg = IllustrationSvgExport.Export([n], new(0, 0, 500, 240), r);
            Check(!svg.Contains("<text") && before == DocumentJson.Save(e.Document) && e.History.Count == 0);
        });
        foreach (var alignment in new[] { TextAlignment.Left, TextAlignment.Center, TextAlignment.Right })
        {
            test("native brackets export as editable SVG " + alignment, () =>
            {
                var n = Import(Svg()); n.TextPath!.SvgPosition = null; n.TextPath.Start = .15; n.TextPath.End = .85; n.TextAlign = alignment;
                using var r = new SceneRenderer(); var bounds = new RectD(0, 0, 500, 240); var before = r.ExportPng([n], bounds);
                var svg = IllustrationSvgExport.Export([n], bounds, r, SvgPathTextExportMode.Editable); var imported = Import(svg);
                Check(imported.Text == "ABC" && n.TextPath.SvgPosition is null); Similar(before, r.ExportPng([imported], bounds));
            });
        }
        test("flipped native bracket export reverses geometry and retains metric alignment", () =>
        {
            var n = Import(Svg()); n.TextPath!.SvgPosition = null; n.TextPath.Start = .1; n.TextPath.End = .9; n.TextPath.Flip = true; n.TextPath.Alignment = PathTextAlignment.Ascender;
            using var r = new SceneRenderer(); var bounds = new RectD(0, 0, 500, 240); var before = r.ExportPng([n], bounds);
            var svg = IllustrationSvgExport.Export([n], bounds, r, SvgPathTextExportMode.Editable); Similar(before, r.ExportPng([Import(svg)], bounds));
        });
        test("native overflow editable export fails explicitly but outlined export remains valid", () =>
        {
            var n = Import(Svg(text: new string('W', 100))); n.TextPath!.SvgPosition = null; using var r = new SceneRenderer();
            Reject(() => IllustrationSvgExport.Export([n], new(0, 0, 500, 240), r, SvgPathTextExportMode.Editable));
            Check(!IllustrationSvgExport.Export([n], new(0, 0, 500, 240), r).Contains("<textPath"));
        });
        test("switching SVG anchor layout to brackets is atomic and preserves baseline identity", () =>
        {
            var n = Import(Svg("startOffset='25%'", geometry: "<path id='baseline' transform='translate(10,20) scale(2)' d='M0 40H200'/>"));
            var e = Editor(n); e.Select(n); using var r = new SceneRenderer(); var start = r.GetTypeOnPathSample(n, 0).Position;
            TypeOnPathOperations.UseBracketLayout(e, r); Check(n.TextPath!.SvgPosition is null); Near(r.GetTypeOnPathSample(n, 0).Position.Y, start.Y);
            Check(e.History.Count == 1); e.Undo(); Check(e.Primary!.TextPath!.SvgPosition!.Offset == 25);
        });
        test("SVG shared reference data is reused and imported texts have independent options", () =>
        {
            var source = "<svg><defs><path id='b' d='M0 100H400'/></defs>" + string.Concat(Enumerable.Range(0, 100).Select(i => $"<text><textPath href='#b' startOffset='{i}%'>ABC</textPath></text>")) + "</svg>";
            var nodes = SvgFormat.Import(source).Document.AllNodes().Where(n => n.TextPath is not null).ToArray();
            Check(nodes.Length == 100 && ReferenceEquals(nodes[0].PathData, nodes[99].PathData));
            nodes[0].TextPath!.SvgPosition!.Offset = -20; Check(nodes[1].TextPath!.SvgPosition!.Offset == 1);
        });
        foreach (var attrs in new[] { "method='stretch'", "spacing='auto'", "side='up'", "textLength='200'", "startOffset='20em'", "startOffset='NaN'" })
            test("SVG path text rejects unsupported attribute " + attrs, () => Reject(() => Import(Svg(attrs))));
        foreach (var text in new[] { "<tspan fill='red'>A</tspan>", "<textPath href='#baseline'>A</textPath>" })
            test("SVG path text rejects nested runs " + text[..6], () => Reject(() => Import(Svg(text: text))));
        test("SVG textPath rejects external missing cyclic and duplicate references", () =>
        {
            Reject(() => Import(Svg().Replace("#baseline", "https://example.org/curve.svg#b")));
            Reject(() => Import(Svg().Replace("href='#baseline'", "href='#missing'")));
            Reject(() => Import(Svg(geometry: "<use id='baseline' href='#baseline'/>")));
            Reject(() => Import(Svg(geometry: "<path id='baseline' d='M0 0H100'/><path id='baseline' d='M0 0H200'/>")));
        });
        test("SVG offset validation rejects nonfinite coordinates and singular transforms", () =>
        {
            Reject(() => new SvgTextPathPosition { Offset = double.NaN }.Validate());
            Reject(() => new SvgTextPathPosition { PathLength = 0 }.Validate());
            Reject(() => new SvgTextPathPosition { PathTransform = Matrix2D.Scale(0, 1) }.Validate());
        });
        test("glyph transformed append equals clone-transform append without modifying source", () =>
        {
            using var glyph = SKPath.ParseSvgPathData("M0 0C10 -20 20 -20 30 0L0 0Z"); var source = glyph.ToSvgPathData();
            using var reference = new SKPath(); using var optimized = new SKPath();
            for (var i = 0; i < 100; i++)
            {
                var matrix = SceneRenderer.Matrix(Matrix2D.Rotation(i * 7) * Matrix2D.Translation(i * 3, i * 2));
                using var copy = new SKPath(glyph); copy.Transform(matrix); reference.AddPath(copy); optimized.AddPath(glyph, matrix);
            }
            Check(reference.ToSvgPathData() == optimized.ToSvgPathData() && glyph.ToSvgPathData() == source);
        });
    }
}
