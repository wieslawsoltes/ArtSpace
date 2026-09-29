using ArtSpace.Core;
using SkiaSharp;

namespace ArtSpace.Skia;

/// <summary>
/// Owned native measurement of one contour. Intended for a single owning UI/render context.
/// Arc lengths use Skia's float-resolution measurement; closest-point projection is bounded numerical
/// approximation for pointer interaction, not an exact geometric intersection solver.
/// </summary>
public sealed class MeasuredContour : IDisposable
{
    private readonly SKPath _path;
    private readonly SKPathMeasure _measure;
    private Vec2[]? _projectionPoints;
    private bool _disposed;
    public double Length { get; }
    public bool IsClosed { get; }
    public long ProjectionTableBuilds { get; private set; }

    public MeasuredContour(SKPath source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.PointCount > 262144) throw new InvalidOperationException("The text baseline exceeds the contour point limit.");
        _path = new SKPath(source);
        try
        {
            using (var check = new SKPathMeasure(_path, false, 4))
            {
                Length = check.Length; IsClosed = check.IsClosed;
                if (!double.IsFinite(Length) || Length <= .0001)
                    throw new InvalidOperationException("Type on a path requires a non-empty contour with positive length.");
                if (check.NextContour())
                    throw new InvalidOperationException("Type on a path requires one contour. Release the compound path first.");
            }
            _measure = new SKPathMeasure(_path, false, 4);
        }
        catch { _path.Dispose(); throw; }
    }

    public (Vec2 Position, Vec2 Tangent) At(double distance)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!double.IsFinite(distance)) throw new ArgumentOutOfRangeException(nameof(distance));
        if (!_measure.GetPositionAndTangent((float)Math.Clamp(distance, 0, Length), out var p, out var t))
            throw new InvalidOperationException("The text baseline could not be measured.");
        var magnitude = Math.Sqrt((double)t.X * t.X + (double)t.Y * t.Y);
        if (magnitude <= 1e-12 || !double.IsFinite(magnitude))
            throw new InvalidOperationException("The text baseline has an undefined tangent.");
        return (new(p.X, p.Y), new(t.X / magnitude, t.Y / magnitude));
    }

    /// <summary>Return the closest arc-length fraction using a retained coarse table and local refinement.</summary>
    public double Project(Vec2 point)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!point.IsFinite) throw new ArgumentOutOfRangeException(nameof(point));
        if (_projectionPoints is null)
        {
            var count = Math.Clamp((int)Math.Min(4096, Math.Ceiling(Length / 2)), 128, 4096);
            var positions = new Vec2[count + 1];
            for (var i = 0; i <= count; i++) positions[i] = At(i * Length / count).Position;
            _projectionPoints = positions; ProjectionTableBuilds++;
        }
        var points = _projectionPoints;
        var best = double.PositiveInfinity; var fraction = 0d; var segment = 0;
        for (var i = 0; i < points.Length - 1; i++)
        {
            var a = points[i]; var d = points[i + 1] - a; var q = point - a;
            var denominator = d.X * d.X + d.Y * d.Y;
            var t = denominator <= 1e-20 ? 0 : Math.Clamp((q.X * d.X + q.Y * d.Y) / denominator, 0, 1);
            var candidate = a + d * t; var error = Squared(candidate - point);
            if (error >= best) continue;
            best = error; segment = i; fraction = (i + t) / (points.Length - 1);
        }
        var lo = Math.Max(0, segment - 1d) / (points.Length - 1);
        var hi = Math.Min(points.Length - 1, segment + 2d) / (points.Length - 1);
        // Refinement improves smooth curves; the best coarse candidate remains valid at corners/cusps.
        var bestActual = Squared(At(fraction * Length).Position - point);
        for (var i = 0; i < 18; i++)
        {
            var a = lo + (hi - lo) / 3; var b = hi - (hi - lo) / 3;
            var ea = Squared(At(a * Length).Position - point); var eb = Squared(At(b * Length).Position - point);
            if (ea < bestActual) { bestActual = ea; fraction = a; }
            if (eb < bestActual) { bestActual = eb; fraction = b; }
            if (ea <= eb) hi = b; else lo = a;
        }
        return Math.Clamp(fraction, 0, 1);
    }

    private static double Squared(Vec2 p) => p.X * p.X + p.Y * p.Y;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _projectionPoints = null; _measure.Dispose(); _path.Dispose();
    }
}
