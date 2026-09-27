using ArtSpace.Core;

namespace ArtSpace.Layout;

/// <summary>Immutable drag-target index. Queries preserve SnapEngine's target/anchor tie order without scanning every target.</summary>
public sealed class SnapIndex
{
    private readonly record struct Entry(double Position, int Target, int Anchor, RectD Bounds);
    private readonly record struct Candidate(Entry Entry, int MovingAnchor, double Delta);
    private readonly Entry[] _x, _y;
    public int TargetCount { get; }
    public long LastComparisons { get; private set; }
    public SnapIndex(IEnumerable<RectD> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var bounds = targets.ToArray(); TargetCount = bounds.Length;
        _x = new Entry[checked(bounds.Length * 3)]; _y = new Entry[_x.Length];
        for (var i = 0; i < bounds.Length; i++)
        {
            var b = bounds[i];
            if (!double.IsFinite(b.X) || !double.IsFinite(b.Y) || !double.IsFinite(b.Right) || !double.IsFinite(b.Bottom)) throw new ArgumentException("Snap bounds must be finite.", nameof(targets));
            _x[i * 3] = new(b.X, i, 0, b); _x[i * 3 + 1] = new(b.Center.X, i, 1, b); _x[i * 3 + 2] = new(b.Right, i, 2, b);
            _y[i * 3] = new(b.Y, i, 0, b); _y[i * 3 + 1] = new(b.Center.Y, i, 1, b); _y[i * 3 + 2] = new(b.Bottom, i, 2, b);
        }
        static int Compare(Entry a, Entry b) { var c = a.Position.CompareTo(b.Position); if (c != 0) return c; c = a.Target.CompareTo(b.Target); return c != 0 ? c : a.Anchor.CompareTo(b.Anchor); }
        Array.Sort(_x, Compare); Array.Sort(_y, Compare); _x = Deduplicate(_x); _y = Deduplicate(_y);
    }
    private static Entry[] Deduplicate(Entry[] sorted)
    {
        if (sorted.Length == 0) return sorted;
        var count = 1;
        for (var i = 1; i < sorted.Length; i++) if (sorted[i].Position != sorted[count - 1].Position) sorted[count++] = sorted[i];
        Array.Resize(ref sorted, count); return sorted;
    }
    public SnapResult Snap(RectD moving, double tolerance, IEnumerable<Guide>? guides = null)
    {
        if (!double.IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        if (!double.IsFinite(moving.X) || !double.IsFinite(moving.Y) || !double.IsFinite(moving.Right) || !double.IsFinite(moving.Bottom)) throw new ArgumentException("Moving bounds must be finite.", nameof(moving));
        LastComparisons = 0;
        Span<double> x = stackalloc double[] { moving.X, moving.Center.X, moving.Right };
        Span<double> y = stackalloc double[] { moving.Y, moving.Center.Y, moving.Bottom };
        var cx = Find(_x, x, tolerance); var cy = Find(_y, y, tolerance);
        var dx = cx?.Delta ?? 0; var dy = cy?.Delta ?? 0;
        var bx = cx.HasValue ? Math.Abs(dx) : double.PositiveInfinity; var by = cy.HasValue ? Math.Abs(dy) : double.PositiveInfinity;
        SnapLine? lx = cx is { } a ? new(false, a.Entry.Position, Math.Min(moving.Y, a.Entry.Bounds.Y), Math.Max(moving.Bottom, a.Entry.Bounds.Bottom)) : null;
        SnapLine? ly = cy is { } b ? new(true, b.Entry.Position, Math.Min(moving.X, b.Entry.Bounds.X), Math.Max(moving.Right, b.Entry.Bounds.Right)) : null;
        foreach (var guide in guides ?? [])
            foreach (var value in guide.Horizontal ? y : x)
            {
                var d = guide.Position - value; var distance = Math.Abs(d); if (distance > tolerance) continue;
                if (guide.Horizontal && distance < by) { by = distance; dy = d; ly = new(true, guide.Position, moving.X - 100, moving.Right + 100); }
                if (!guide.Horizontal && distance < bx) { bx = distance; dx = d; lx = new(false, guide.Position, moving.Y - 100, moving.Bottom + 100); }
            }
        IReadOnlyList<SnapLine> lines = lx.HasValue && ly.HasValue ? new[] { lx.Value, ly.Value } : lx.HasValue ? new[] { lx.Value } : ly.HasValue ? new[] { ly.Value } : Array.Empty<SnapLine>();
        return new(new(dx, dy), lines);
    }
    private Candidate? Find(Entry[] entries, ReadOnlySpan<double> moving, double tolerance)
    {
        Candidate? best = null;
        for (var i = 0; i < moving.Length; i++)
        {
            var lo = 0; var hi = entries.Length;
            while (lo < hi) { var mid = lo + (hi - lo) / 2; LastComparisons++; if (entries[mid].Position < moving[i]) lo = mid + 1; else hi = mid; }
            for (var j = Math.Max(0, lo - 1); j <= Math.Min(entries.Length - 1, lo); j++)
            {
                var candidate = new Candidate(entries[j], i, entries[j].Position - moving[i]); LastComparisons++;
                if (Math.Abs(candidate.Delta) <= tolerance && (!best.HasValue || Better(candidate, best.Value))) best = candidate;
            }
        }
        return best;
    }
    private static bool Better(Candidate a, Candidate b)
    {
        var comparison = Math.Abs(a.Delta).CompareTo(Math.Abs(b.Delta)); if (comparison != 0) return comparison < 0;
        comparison = a.Entry.Target.CompareTo(b.Entry.Target); if (comparison != 0) return comparison < 0;
        comparison = a.MovingAnchor.CompareTo(b.MovingAnchor); return comparison != 0 ? comparison < 0 : a.Entry.Anchor < b.Entry.Anchor;
    }
}
