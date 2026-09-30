using ArtSpace.Core;
using ArtSpace.Documents;
using ArtSpace.Editing;
using ArtSpace.Illustration;
using ArtSpace.Skia;

internal static class SvgTextSpacingTests
{
    private static DesignNode Import(string anchor = "start", double spacing = 0, string path = "M20 100H420") =>
        SvgFormat.Import($"<svg width='500' height='400'><defs><path id='b' d='{path}'/></defs><text font-family='Inter' font-size='20' text-anchor='{anchor}' letter-spacing='{spacing.ToString(System.Globalization.CultureInfo.InvariantCulture)}'><textPath href='#b' startOffset='50%'>ABCD</textPath></text></svg>")
            .Document.AllNodes().Single(n => n.TextPath is not null);
    private static void Check(bool value) { if (!value) throw new Exception("SVG character-cell spacing assertion failed."); }
    private static void Near(double a, double b) { if (Math.Abs(a - b) > .003) throw new Exception($"{a} differs from {b}."); }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; } catch (InvalidDataException) { return; }
        throw new Exception("Expected explicit rejection.");
    }
    public static void Register(Action<string, Action> test)
    {
        foreach (var anchor in new[] { "start", "middle", "end" })
            test("SVG trailing letter spacing participates in " + anchor + " anchoring", () =>
            {
                using var renderer = new SceneRenderer(); var unspaced = Import(anchor);
                var plain = renderer.GetTypeOnPathStatus(unspaced);
                var width = renderer.GetTypeOnPathGlyphs(unspaced)[0].Advance;
                foreach (var spacing in new[] { -2d, 2d })
                {
                    var node = Import(anchor, spacing); var status = renderer.GetTypeOnPathStatus(node);
                    var glyph = renderer.GetTypeOnPathGlyphs(node)[0];
                    Near(status.TextAdvance, plain.TextAdvance + 4 * spacing);
                    Near(glyph.Advance, width + spacing);
                    var fraction = anchor == "middle" ? .5 : anchor == "end" ? 1d : 0;
                    Near(glyph.Distance, 200 - status.TextAdvance * fraction + glyph.Advance / 2);
                    Near(glyph.Transform.DX, 220 - status.TextAdvance * fraction);
                }
            });
        test("SVG curve tangents are sampled at the spaced character-cell midpoint", () =>
        {
            using var renderer = new SceneRenderer(); var node = Import("start", 4, "M20 300Q220 -100 420 300");
            var status = renderer.GetTypeOnPathStatus(node); var glyph = renderer.GetTypeOnPathGlyphs(node)[0];
            Near(glyph.Distance, status.PathLength / 2 + glyph.Advance / 2);
            var sample = renderer.GetTypeOnPathSample(node, glyph.Distance / status.PathLength);
            Near(glyph.Transform.M11, sample.Tangent.X); Near(glyph.Transform.M12, sample.Tangent.Y);
            Near(glyph.Transform.DX, sample.Position.X - sample.Tangent.X * glyph.Advance / 2);
            Near(glyph.Transform.DY, sample.Position.Y - sample.Tangent.Y * glyph.Advance / 2);
        });
        test("native brackets keep inter-glyph tracking and glyph-width tangents", () =>
        {
            using var renderer = new SceneRenderer(); var node = Import(); node.TextPath!.SvgPosition = null;
            var plain = renderer.GetTypeOnPathStatus(node); var width = renderer.GetTypeOnPathGlyphs(node)[0].Advance;
            node.LetterSpacing = 4; var status = renderer.GetTypeOnPathStatus(node); var glyphs = renderer.GetTypeOnPathGlyphs(node);
            Near(status.TextAdvance, plain.TextAdvance + 12); Near(glyphs[0].Advance, width); Near(glyphs[0].Distance, width / 2);
            Near(glyphs[1].Transform.DX - glyphs[0].Transform.DX, width + 4);
        });
        test("editable SVG retains tracked curve geometry and text through reimport", () =>
        {
            using var renderer = new SceneRenderer(); var node = Import("middle", 3, "M20 300Q220 -100 420 300");
            var before = DocumentJson.SaveNodes([node]); var bounds = new RectD(0, 0, 500, 400);
            var svg = IllustrationSvgExport.Export([node], bounds, renderer, SvgPathTextExportMode.Editable);
            var copy = SvgFormat.Import(svg).Document.AllNodes().Single(n => n.TextPath is not null);
            Check(copy.Text == node.Text && copy.LetterSpacing == 3 && DocumentJson.SaveNodes([node]) == before);
            Check(renderer.ExportPng([node], bounds).SequenceEqual(renderer.ExportPng([copy], bounds)));
        });
        test("native tracked editable export fails explicitly without mutating source", () =>
        {
            var node = Import("middle", 3); node.TextPath!.SvgPosition = null;
            var editor = new EditorSession(new() { Pages = [new() { Nodes = [node] }] });
            var before = DocumentJson.Save(editor.Document); using var renderer = new SceneRenderer();
            Reject(() => IllustrationSvgExport.Export([node], new(0, 0, 500, 400), renderer, SvgPathTextExportMode.Editable));
            Check(!IllustrationSvgExport.Export([node], new(0, 0, 500, 400), renderer).Contains("<textPath"));
            Check(DocumentJson.Save(editor.Document) == before && editor.History.Count == 0);
        });
        test("reversed SVG character advances produce a cached diagnostic rather than reversed ink", () =>
        {
            using var renderer = new SceneRenderer(); var node = Import(spacing: -1000);
            Check(renderer.GetTypeOnPathStatus(node).Error is not null); var builds = renderer.PathTextLayoutBuilds;
            Check(renderer.GetTypeOnPathStatus(node).Error is not null && renderer.PathTextLayoutBuilds == builds);
            Reject(() => renderer.ValidateTypeOnPath(node));
        });
        test("SVG fractional or excessive weights are rejected rather than rounded", () =>
        {
            foreach (var weight in new[] { "500.5", "1e20", "0" })
                Reject(() => SvgFormat.Import($"<svg><defs><path id='b' d='M0 50H200'/></defs><text font-weight='{weight}'><textPath href='#b'>A</textPath></text></svg>"));
        });
    }
}
