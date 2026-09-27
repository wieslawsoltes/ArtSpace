using ArtSpace.Core;
using SkiaSharp;

namespace ArtSpace.Skia;

public sealed partial class SceneRenderer
{
    private readonly record struct TextRun(string Text, float X, float Baseline);

    private SKFont CreateTextFont(DesignNode node) => new(Typeface(node), (float)node.FontSize)
    {
        Edging = SKFontEdging.SubpixelAntialias, Subpixel = true,
        Embolden = _customTypeface is not null && node.FontFamily == "Inter" && node.FontWeight >= 600
    };

    /// <summary>Returns owned glyph outline geometry using exactly the same runs as interactive text drawing.</summary>
    public SKPath CreateTextOutline(DesignNode node)
    {
        if (node.Kind != NodeKind.Text) throw new ArgumentException("The node must contain text.", nameof(node));
        if (node.Text.Length > 100_000) throw new InvalidOperationException("Outline conversion is limited to 100,000 characters per text object.");
        var path = new SKPath();
        try
        {
            using var font = CreateTextFont(node);
            foreach (var run in TextRuns(node, font))
            {
                using var glyphs = font.GetTextPath(run.Text, new SKPoint(run.X, run.Baseline));
                path.AddPath(glyphs);
            }
            return path;
        }
        catch { path.Dispose(); throw; }
    }

    public void DrawText(SKCanvas canvas, DesignNode node, SKPaint paint)
    {
        using var font = CreateTextFont(node);
        foreach (var run in TextRuns(node, font)) canvas.DrawText(run.Text, run.X, run.Baseline, font, paint);
    }

    // Basic, deterministic Latin-oriented layout. Advanced shaping/bidi and font fallback are not claimed.
    // Width measurement is independent of stroke appearance; fill, stroke, and outlines share one layout.
    private static IEnumerable<TextRun> TextRuns(DesignNode node, SKFont font)
    {
        var y = (float)node.FontSize;
        foreach (var line in TextLines(node.Text, font, (float)Math.Max(1, node.Width), (float)node.LetterSpacing))
        {
            var width = Measure(line, font, (float)node.LetterSpacing);
            var x = node.TextAlign == TextAlignment.Center ? ((float)node.Width - width) / 2 : node.TextAlign == TextAlignment.Right ? (float)node.Width - width : 0;
            if (Math.Abs(node.LetterSpacing) < .001) yield return new(line, x, y);
            else foreach (var rune in line.EnumerateRunes())
            {
                var text = rune.ToString();
                yield return new(text, x, y);
                x += font.MeasureText(text) + (float)node.LetterSpacing;
            }
            y += (float)(node.FontSize * node.LineHeight);
        }
    }

    private static float Measure(string text, SKFont font, float tracking)
    {
        if (Math.Abs(tracking) < .001) return font.MeasureText(text);
        var width = 0f; var count = 0;
        foreach (var rune in text.EnumerateRunes()) { width += font.MeasureText(rune.ToString()); count++; }
        return width + Math.Max(0, count - 1) * tracking;
    }

    private static IEnumerable<string> TextLines(string text, SKFont font, float width, float tracking)
    {
        var advances = new Dictionary<int, float>();
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var runes = paragraph.EnumerateRunes().ToArray();
            var prefix = new double[runes.Length + 1];
            var offsets = new int[runes.Length + 1];
            for (var i = 0; i < runes.Length; i++)
            {
                if (!advances.TryGetValue(runes[i].Value, out var advance))
                    advances[runes[i].Value] = advance = font.MeasureText(runes[i].ToString());
                prefix[i + 1] = prefix[i] + advance;
                offsets[i + 1] = offsets[i] + runes[i].Utf16SequenceLength;
            }
            var start = 0; var lastBreak = -1;
            for (var i = 0; i < runes.Length; i++)
            {
                if (runes[i].Value == ' ') lastBreak = i;
                var length = prefix[i + 1] - prefix[start] + Math.Max(0, i - start) * tracking;
                if (lastBreak < start || length <= width) continue;
                if (lastBreak > start) yield return paragraph[offsets[start]..offsets[lastBreak]];
                start = lastBreak + 1; lastBreak = -1;
            }
            yield return paragraph[offsets[start]..];
        }
    }
}
