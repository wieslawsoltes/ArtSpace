using ArtSpace.Core;
using SkiaSharp;

namespace ArtSpace.Skia;

public readonly record struct PathTextGlyph(int ScalarIndex, int Utf16Index, double Advance, double Distance, Matrix2D Transform);
public readonly record struct PathTextStatus(double PathLength, double RangeLength, double TextAdvance, int TotalGlyphs, int VisibleGlyphs, bool Overflow, RectD InkBounds);

/// <summary>
/// Immutable layout and owned vector glyph geometry. Basic Unicode-scalar layout uses the supplied
/// font; shaping, bidi, fallback and automatic threading are not performed. Drawing and Create Outlines
/// use the same geometry. No font or mutable options are borrowed after Build returns.
/// </summary>
public sealed class PathTextLayout : IDisposable
{
    private readonly SKPath _outline;
    private bool _disposed;
    public IReadOnlyList<PathTextGlyph> Glyphs { get; }
    public PathTextStatus Status { get; }
    internal SKPath Outline => !_disposed ? _outline : throw new ObjectDisposedException(nameof(PathTextLayout));

    private PathTextLayout(SKPath outline, List<PathTextGlyph> glyphs, PathTextStatus status)
    {
        _outline = outline; Glyphs = glyphs.AsReadOnly(); Status = status;
    }

    public static PathTextLayout Build(MeasuredContour contour, string text, SKFont font, TypeOnPathOptions options,
        double tracking = 0, TextAlignment alignment = TextAlignment.Left)
    {
        ArgumentNullException.ThrowIfNull(contour); ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font); ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (text.Length > TypeOnPathOptions.MaxTextLength)
            throw new InvalidOperationException($"Type on a path is limited to {TypeOnPathOptions.MaxTextLength} UTF-16 characters.");
        if (!double.IsFinite(tracking) || Math.Abs(tracking) > 10000 || !Enum.IsDefined(alignment))
            throw new ArgumentOutOfRangeException(nameof(tracking));
        var runes = text.EnumerateRunes().ToArray();
        var advances = new double[runes.Length];
        var unique = new Dictionary<int, (string Text, double Advance, SKPath Outline)>();
        var output = new SKPath();
        try
        {
            var total = 0d;
            for (var i = 0; i < runes.Length; i++)
            {
                // A path is a single-line baseline. Preserve source text but display line breaks/tabs as spaces.
                var scalar = runes[i].Value is 9 or 10 or 13 ? 32 : runes[i].Value;
                if (!unique.TryGetValue(scalar, out var glyph))
                {
                    var value = char.ConvertFromUtf32(scalar);
                    var width = (double)font.MeasureText(value);
                    if (!double.IsFinite(width) || width < 0) throw new InvalidOperationException("Invalid glyph advance.");
                    glyph = (value, width, font.GetTextPath(value, SKPoint.Empty)); unique.Add(scalar, glyph);
                }
                advances[i] = glyph.Advance; total += glyph.Advance;
            }
            total += Math.Max(0, runes.Length - 1) * tracking;
            total = Math.Max(0, total);
            var range = (options.End - options.Start) * contour.Length;
            var cursor = alignment == TextAlignment.Center ? (range - total) / 2 : alignment == TextAlignment.Right ? range - total : 0;
            var metrics = font.Metrics;
            var offset = (options.Alignment switch
            {
                PathTextAlignment.Ascender => -metrics.Ascent,
                PathTextAlignment.Descender => -metrics.Descent,
                PathTextAlignment.Center => -(metrics.Ascent + metrics.Descent) / 2,
                _ => 0
            }) - options.BaselineShift;
            var placements = new List<PathTextGlyph>(runes.Length);
            var overflow = total > range + .0001; var utf16 = 0;
            for (var i = 0; i < runes.Length; i++)
            {
                var advance = advances[i]; var midpoint = cursor + advance / 2;
                if (cursor < -.0001 || cursor + advance > range + .0001 || range <= .0001)
                    overflow = true;
                else
                {
                    var distance = options.Flip ? options.End * contour.Length - midpoint : options.Start * contour.Length + midpoint;
                    var sample = contour.At(distance);
                    var t = options.Flip ? sample.Tangent * -1 : sample.Tangent;
                    var normal = new Vec2(-t.Y, t.X);
                    var origin = sample.Position - t * (advance / 2) + normal * offset;
                    var matrix = new Matrix2D(t.X, t.Y, -t.Y, t.X, origin.X, origin.Y);
                    var scalar = runes[i].Value is 9 or 10 or 13 ? 32 : runes[i].Value;
                    using var glyph = new SKPath(unique[scalar].Outline);
                    glyph.Transform(SceneRenderer.Matrix(matrix)); output.AddPath(glyph);
                    if (output.PointCount > 1_000_000) throw new InvalidOperationException("Text outline point budget exceeded.");
                    placements.Add(new(i, utf16, advance, distance, matrix));
                }
                cursor += advance + tracking; utf16 += runes[i].Utf16SequenceLength;
            }
            var bounds = output.TightBounds;
            var status = new PathTextStatus(contour.Length, range, total, runes.Length, placements.Count, overflow,
                new(bounds.Left, bounds.Top, bounds.Width, bounds.Height));
            return new(output, placements, status);
        }
        catch { output.Dispose(); throw; }
        finally { foreach (var glyph in unique.Values) glyph.Outline.Dispose(); }
    }

    public void Draw(SKCanvas canvas, SKPaint paint)
    {
        ArgumentNullException.ThrowIfNull(canvas); ArgumentNullException.ThrowIfNull(paint);
        canvas.DrawPath(Outline, paint);
    }
    public SKPath CreateOutline() => new(Outline);
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _outline.Dispose();
    }
}
