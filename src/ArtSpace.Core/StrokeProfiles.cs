namespace ArtSpace.Core;

/// <summary>A width knot at normalized contour arc length. Side widths multiply the base stroke weight.</summary>
public sealed class StrokeWidthPoint
{
    public double Position { get; set; }
    public double Left { get; set; } = .5;
    public double Right { get; set; } = .5;
    public StrokeWidthPoint Clone() => new() { Position = Position, Left = Left, Right = Right };
}

public enum StrokeWidthPreset { Uniform, TaperBoth, TaperStart, TaperEnd, Lens, Diamond }

/// <summary>UI-independent, piecewise-linear width profiles; an empty profile is the exact native uniform stroke.</summary>
public static class StrokeProfiles
{
    public const int MaxPoints = 64;
    public const double MaxSide = 16;

    public static void Validate(IReadOnlyList<StrokeWidthPoint>? points)
    {
        if (points is null || points.Count > MaxPoints)
            throw new InvalidDataException("A stroke width profile supports at most 64 knots.");
        var previous = -1d;
        foreach (var point in points)
        {
            if (point is null || !double.IsFinite(point.Position) || point.Position is < 0 or > 1
                || point.Position <= previous || !double.IsFinite(point.Left) || !double.IsFinite(point.Right)
                || point.Left is < 0 or > MaxSide || point.Right is < 0 or > MaxSide)
                throw new InvalidDataException("Width positions must be strictly increasing in 0–1; side factors must be finite in 0–16.");
            previous = point.Position;
        }
    }

    public static (double Left, double Right) Evaluate(IReadOnlyList<StrokeWidthPoint> points, double position)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (!double.IsFinite(position)) throw new ArgumentOutOfRangeException(nameof(position));
        if (points.Count == 0) return (.5, .5);
        if (position <= points[0].Position) return (points[0].Left, points[0].Right);
        if (position >= points[^1].Position) return (points[^1].Left, points[^1].Right);
        var lo = 0; var hi = points.Count - 1;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (points[mid].Position <= position) lo = mid; else hi = mid;
        }
        var a = points[lo]; var b = points[hi];
        var t = (position - a.Position) / (b.Position - a.Position);
        return (a.Left + (b.Left - a.Left) * t, a.Right + (b.Right - a.Right) * t);
    }

    public static double MaximumSide(IReadOnlyList<StrokeWidthPoint> points)
    {
        var value = points.Count == 0 ? .5 : 0;
        for (var i = 0; i < points.Count; i++) value = Math.Max(value, Math.Max(points[i].Left, points[i].Right));
        return value;
    }

    public static List<StrokeWidthPoint> Create(StrokeWidthPreset preset) => preset switch
    {
        StrokeWidthPreset.Uniform => [],
        StrokeWidthPreset.TaperBoth => [Point(0, 0), Point(.5, .5), Point(1, 0)],
        StrokeWidthPreset.TaperStart => [Point(0, 0), Point(1, .5)],
        StrokeWidthPreset.TaperEnd => [Point(0, .5), Point(1, 0)],
        StrokeWidthPreset.Lens => [Point(0, 0), Point(.125, .33), Point(.25, .433), Point(.5, .5), Point(.75, .433), Point(.875, .33), Point(1, 0)],
        StrokeWidthPreset.Diamond => [Point(0, .1), Point(.5, .75), Point(1, .1)],
        _ => throw new ArgumentOutOfRangeException(nameof(preset))
    };

    public static string Name(StrokeWidthPreset preset) => preset switch
    {
        StrokeWidthPreset.Uniform => "Uniform", StrokeWidthPreset.TaperBoth => "Taper Both Ends",
        StrokeWidthPreset.TaperStart => "Taper Start", StrokeWidthPreset.TaperEnd => "Taper End",
        StrokeWidthPreset.Lens => "Lens", StrokeWidthPreset.Diamond => "Diamond",
        _ => throw new ArgumentOutOfRangeException(nameof(preset))
    };

    /// <summary>Reverse contour-relative placement and exchange sides, preserving the original physical width.</summary>
    public static List<StrokeWidthPoint> Reverse(IReadOnlyList<StrokeWidthPoint> points)
    {
        Validate(points);
        var result = new List<StrokeWidthPoint>(points.Count);
        for (var i = points.Count - 1; i >= 0; i--)
            result.Add(new() { Position = 1 - points[i].Position, Left = points[i].Right, Right = points[i].Left });
        return result;
    }

    public static List<StrokeWidthPoint> Copy(IReadOnlyList<StrokeWidthPoint> points) => points.Select(p => p.Clone()).ToList();
    /// <summary>Insert an interpolated knot, retaining strict ordering and avoiding duplicate locations.</summary>
    public static int Insert(List<StrokeWidthPoint> points, double position)
    {
        Validate(points);
        if (!double.IsFinite(position) || position is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(position));
        for (var i = 0; i < points.Count; i++) if (Math.Abs(points[i].Position - position) < 1e-6) return i;
        if (points.Count >= MaxPoints) throw new InvalidOperationException("The 64-point stroke profile limit has been reached.");
        var width = Evaluate(points, position);
        var index = points.FindIndex(p => p.Position > position); if (index < 0) index = points.Count;
        points.Insert(index, new() { Position = position, Left = width.Left, Right = width.Right }); return index;
    }
    private static StrokeWidthPoint Point(double position, double side) => new() { Position = position, Left = side, Right = side };
}
