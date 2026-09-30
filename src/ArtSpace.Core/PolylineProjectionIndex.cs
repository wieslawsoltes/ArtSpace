namespace ArtSpace.Core;

/// <summary>A closest-point result in the original (not spatially sorted) polyline segment order.</summary>
public readonly record struct PolylineProjection(int SegmentIndex, double Parameter, double DistanceSquared);

public readonly record struct PolylineProjectionStatistics(int VisitedNodes, int ExaminedSegments);

/// <summary>
/// Owned immutable polyline snapshot with a balanced bounding-volume tree. Queries allocate nothing
/// and break equal-distance ties by the original segment index. Concurrent read queries are supported.
/// Construction sorts median partitions; worst-case query work remains linear for overlapping bounds.
/// </summary>
public sealed class PolylineProjectionIndex
{
    public const int MaxSegments = 65_536;
    private const double CoordinateLimit = 1e150;
    private const double RoundingMargin = 7.105427357601002e-15; // 32 * binary64 machine epsilon.
    private const int LeafSize = 8;
    private readonly Segment[] _segments;
    private readonly Node[] _nodes;
    private int _nodeCount;

    private readonly record struct Segment(Vec2 A, Vec2 B, int Index)
    {
        public double CenterX => A.X * .5 + B.X * .5;
        public double CenterY => A.Y * .5 + B.Y * .5;
    }
    private readonly record struct Node(double MinX, double MinY, double MaxX, double MaxY,
        int Start, int Count, int Left, int Right);
    private sealed class SegmentComparer(bool horizontal) : IComparer<Segment>
    {
        public int Compare(Segment a, Segment b)
        {
            var comparison = (horizontal ? a.CenterX : a.CenterY).CompareTo(horizontal ? b.CenterX : b.CenterY);
            return comparison != 0 ? comparison : a.Index.CompareTo(b.Index);
        }
    }
    private static readonly IComparer<Segment> Horizontal = new SegmentComparer(true);
    private static readonly IComparer<Segment> Vertical = new SegmentComparer(false);

    public int SegmentCount => _segments.Length;
    public int NodeCount => _nodeCount;

    public PolylineProjectionIndex(ReadOnlySpan<Vec2> points)
    {
        if (points.Length is < 2 or > MaxSegments + 1)
            throw new ArgumentOutOfRangeException(nameof(points), $"Supply 2 to {MaxSegments + 1} points.");
        foreach (var point in points) Validate(point, nameof(points));
        _segments = new Segment[points.Length - 1];
        for (var i = 0; i < _segments.Length; i++) _segments[i] = new(points[i], points[i + 1], i);
        _nodes = new Node[checked(_segments.Length * 2)];
        Build(0, _segments.Length);
        // Only completed construction mutates these arrays.
        Array.Resize(ref _nodes, _nodeCount);
    }

    public PolylineProjection Project(Vec2 point) => Project(point, out _);

    public PolylineProjection Project(Vec2 point, out PolylineProjectionStatistics statistics)
    {
        Validate(point, nameof(point));
        var state = new Query(point);
        Search(0, ref state);
        statistics = new(state.Visited, state.Examined);
        return new(state.SegmentIndex, state.Parameter, state.BestSquared);
    }

    private struct Query(Vec2 point)
    {
        public readonly Vec2 Point = point;
        public double BestSquared = double.PositiveInfinity;
        public int SegmentIndex = int.MaxValue;
        public double Parameter;
        public int Visited;
        public int Examined;
    }

    private int Build(int start, int count)
    {
        var index = _nodeCount++;
        var first = _segments[start];
        var minX = Math.Min(first.A.X, first.B.X); var maxX = Math.Max(first.A.X, first.B.X);
        var minY = Math.Min(first.A.Y, first.B.Y); var maxY = Math.Max(first.A.Y, first.B.Y);
        for (var i = start + 1; i < start + count; i++)
        {
            var segment = _segments[i];
            minX = Math.Min(minX, Math.Min(segment.A.X, segment.B.X));
            minY = Math.Min(minY, Math.Min(segment.A.Y, segment.B.Y));
            maxX = Math.Max(maxX, Math.Max(segment.A.X, segment.B.X));
            maxY = Math.Max(maxY, Math.Max(segment.A.Y, segment.B.Y));
        }
        // Outward padding keeps rounded interpolation at an edge inside the conservative bounds.
        var marginX = Math.Max(Math.Abs(minX), Math.Abs(maxX)) * RoundingMargin;
        var marginY = Math.Max(Math.Abs(minY), Math.Abs(maxY)) * RoundingMargin;
        if (count <= LeafSize)
            _nodes[index] = new(minX - marginX, minY - marginY, maxX + marginX, maxY + marginY, start, count, -1, -1);
        else
        {
            Array.Sort(_segments, start, count, maxX - minX >= maxY - minY ? Horizontal : Vertical);
            var half = count / 2;
            var left = Build(start, half); var right = Build(start + half, count - half);
            _nodes[index] = new(minX - marginX, minY - marginY, maxX + marginX, maxY + marginY, 0, 0, left, right);
        }
        return index;
    }

    private static double LowerBound(Node node, Vec2 point)
    {
        var dx = Math.Max(0, Math.Max(node.MinX - point.X, point.X - node.MaxX));
        var dy = Math.Max(0, Math.Max(node.MinY - point.Y, point.Y - node.MaxY));
        // Round conservatively when a distant query makes squared values nearly indistinguishable.
        return (dx * dx + dy * dy) * (1 - RoundingMargin);
    }

    private void Search(int index, ref Query state)
    {
        var node = _nodes[index];
        state.Visited++;
        if (LowerBound(node, state.Point) > state.BestSquared) return;
        if (node.Count != 0)
        {
            for (var i = node.Start; i < node.Start + node.Count; i++)
            {
                var segment = _segments[i]; state.Examined++;
                var d = segment.B - segment.A; var q = state.Point - segment.A;
                var denominator = d.X * d.X + d.Y * d.Y;
                var parameter = denominator <= 1e-20 ? 0 : Math.Clamp((q.X * d.X + q.Y * d.Y) / denominator, 0, 1);
                var difference = segment.A + d * parameter - state.Point;
                var squared = difference.X * difference.X + difference.Y * difference.Y;
                if (squared < state.BestSquared || squared == state.BestSquared && segment.Index < state.SegmentIndex)
                {
                    state.BestSquared = squared; state.SegmentIndex = segment.Index; state.Parameter = parameter;
                }
            }
            return;
        }
        var leftFirst = LowerBound(_nodes[node.Left], state.Point) <= LowerBound(_nodes[node.Right], state.Point);
        Search(leftFirst ? node.Left : node.Right, ref state);
        Search(leftFirst ? node.Right : node.Left, ref state);
    }

    private static void Validate(Vec2 point, string parameter)
    {
        // This bound keeps every subtraction, dot product and squared distance finite, well beyond
        // the coordinate range of Skia's float paths. It also rejects NaN before constructing a tree.
        if (!point.IsFinite || Math.Abs(point.X) > CoordinateLimit || Math.Abs(point.Y) > CoordinateLimit)
            throw new ArgumentOutOfRangeException(parameter, "Polyline coordinates must be finite and within ±1e150.");
    }
}
