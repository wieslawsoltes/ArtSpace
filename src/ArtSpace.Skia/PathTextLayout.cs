using ArtSpace.Core;
using SkiaSharp;

namespace ArtSpace.Skia;

public readonly record struct PathTextGlyph(int ScalarIndex, int Utf16Index, double Advance, double Distance, Matrix2D Transform);
public readonly record struct PathTextStatus(double PathLength, double RangeLength, double TextAdvance, int TotalGlyphs, int VisibleGlyphs, bool Overflow, RectD InkBounds, string? Error = null);

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

    internal static PathTextLayout Invalid(string error) => new(new SKPath(), [], new(0, 0, 0, 0, 0, true, default, error));

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
            var svg = options.SvgPosition;
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
                // SVG rotates/anchors the complete character cell, including its trailing spacing.
                // Native brackets retain their existing inter-glyph tracking and whole-glyph fit.
                advances[i] = glyph.Advance + (svg is null ? 0 : tracking);
                if (svg is not null && advances[i] < 0)
                    throw new InvalidOperationException("SVG letter spacing that reverses a character advance is unsupported; use outlined text.");
                total += advances[i];
            }
            if (svg is null) total += Math.Max(0, runes.Length - 1) * tracking;
            total = Math.Max(0, total);
            var svgAnchor = svg?.Resolve(contour.Length) ?? 0;
            var alignmentFraction = alignment == TextAlignment.Center ? .5 : alignment == TextAlignment.Right ? 1d : 0;
            var range = svg is null ? (options.End - options.Start) * contour.Length : contour.Length;
            var cursor = alignment == TextAlignment.Center ? (range - total) / 2 : alignment == TextAlignment.Right ? range - total : 0;
            if (svg is not null) cursor = svgAnchor - total * alignmentFraction;
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
                // SVG clips by glyph midpoint. Closed contours permit one circuit about the anchor;
                // native brackets retain their existing whole-glyph-fit policy.
                var outside = svg is null
                    ? cursor < -.0001 || cursor + advance > range + .0001 || range <= .0001
                    : contour.IsClosed
                        ? midpoint < svgAnchor - range * alignmentFraction || midpoint >= svgAnchor + range * (1 - alignmentFraction)
                        : midpoint < 0 || midpoint > range;
                if (outside) overflow = true;
                else
                {
                    var distance = options.Flip ? options.End * contour.Length - midpoint : options.Start * contour.Length + midpoint;
                    if (svg is not null)
                    {
                        var onPath = contour.IsClosed ? ((midpoint % range) + range) % range : midpoint;
                        distance = options.Flip ? range - onPath : onPath;
                    }
                    var sample = contour.At(distance);
                    var t = options.Flip ? sample.Tangent * -1 : sample.Tangent;
                    var normal = new Vec2(-t.Y, t.X);
                    var origin = sample.Position - t * (advance / 2) + normal * offset;
                    var matrix = new Matrix2D(t.X, t.Y, -t.Y, t.X, origin.X, origin.Y);
                    var scalar = runes[i].Value is 9 or 10 or 13 ? 32 : runes[i].Value;
                    // Append with the transform directly; no per-character native SKPath clone.
                    output.AddPath(unique[scalar].Outline, SceneRenderer.Matrix(matrix));
                    if (output.PointCount > 1_000_000) throw new InvalidOperationException("Text outline point budget exceeded.");
                    placements.Add(new(i, utf16, advance, distance, matrix));
                }
                cursor += advance + (svg is null ? tracking : 0); utf16 += runes[i].Utf16SequenceLength;
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
