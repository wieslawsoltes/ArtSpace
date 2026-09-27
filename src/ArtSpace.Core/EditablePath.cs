using System.Globalization;
using System.Text;

namespace ArtSpace.Core;

public enum PathFillRule { NonZero, EvenOdd }

/// <summary>
/// Managed, renderer-independent Bézier editing buffer. Contours remain independent;
/// SVG is the persistence representation, so editing does not introduce a second document format.
/// </summary>
public sealed class EditablePath
{
    public PathFillRule FillRule { get; set; }
    public const int MaxAnchors = 65_536;
    public List<Contour> Contours { get; } = [];
    public int AnchorCount => Contours.Sum(c => c.Points.Count);

    public sealed class Contour
    {
        public List<PathPoint> Points { get; } = [];
        public bool Closed { get; set; }
        public int SegmentCount => Math.Max(0, Points.Count - (Closed ? 0 : 1));
    }

    public readonly record struct Address(int ContourIndex, int PointIndex);
    public readonly record struct SegmentHit(Address Start, double Parameter, double Distance);

    public PathPoint this[Address address] => Contours[address.ContourIndex].Points[address.PointIndex];
    public IEnumerable<Address> Addresses => Contours.SelectMany((c, ci) => c.Points.Select((_, pi) => new Address(ci, pi)));

    public EditablePath Clone()
    {
        var copy = new EditablePath { FillRule = FillRule };
        foreach (var contour in Contours)
        {
            var target = new Contour { Closed = contour.Closed };
            foreach (var point in contour.Points) target.Points.Add(Copy(point));
            copy.Contours.Add(target);
        }
        return copy;
    }

    public static PathPoint Copy(PathPoint point) => new()
    {
        Position = point.Position, ControlIn = point.ControlIn, ControlOut = point.ControlOut
    };

    public void Translate(IEnumerable<Address> addresses, Vec2 delta)
    {
        if (!delta.IsFinite) throw new ArgumentOutOfRangeException(nameof(delta));
        foreach (var address in addresses.Distinct())
        {
            var point = this[address];
            point.Position += delta;
            if (point.ControlIn.HasValue) point.ControlIn += delta;
            if (point.ControlOut.HasValue) point.ControlOut += delta;
        }
    }

    /// <summary>Aligns the opposite tangent without changing its length unless independent is true.</summary>
    public void MoveControl(Address address, bool incoming, Vec2 position, bool independent)
    {
        if (!position.IsFinite) throw new ArgumentOutOfRangeException(nameof(position));
        var point = this[address];
        var opposite = incoming ? point.ControlOut : point.ControlIn;
        var delta = position - point.Position;
        var length = delta.DistanceTo(Vec2.Zero);
        if (incoming) point.ControlIn = position; else point.ControlOut = position;
        if (independent || opposite is null || length < 1e-12) return;
        var other = point.Position - delta * (opposite.Value.DistanceTo(point.Position) / length);
        if (incoming) point.ControlOut = other; else point.ControlIn = other;
    }

    /// <summary>Splits a line or cubic exactly using de Casteljau construction, including the closing segment.</summary>
    public Address Split(Address start, double parameter)
    {
        if (!double.IsFinite(parameter) || parameter <= 0 || parameter >= 1)
            throw new ArgumentOutOfRangeException(nameof(parameter));
        if (AnchorCount >= MaxAnchors) throw new InvalidOperationException("The editable path anchor limit has been reached.");
        var contour = Contours[start.ContourIndex];
        if (start.PointIndex < 0 || start.PointIndex >= contour.SegmentCount)
            throw new ArgumentOutOfRangeException(nameof(start));
        var a = contour.Points[start.PointIndex];
        var b = contour.Points[(start.PointIndex + 1) % contour.Points.Count];
        PathPoint middle;
        if (!a.ControlOut.HasValue && !b.ControlIn.HasValue)
        {
            middle = new() { Position = Lerp(a.Position, b.Position, parameter) };
        }
        else
        {
            var p01 = Lerp(a.Position, a.ControlOut ?? a.Position, parameter);
            var p12 = Lerp(a.ControlOut ?? a.Position, b.ControlIn ?? b.Position, parameter);
            var p23 = Lerp(b.ControlIn ?? b.Position, b.Position, parameter);
            var p012 = Lerp(p01, p12, parameter);
            var p123 = Lerp(p12, p23, parameter);
            middle = new() { Position = Lerp(p012, p123, parameter), ControlIn = p012, ControlOut = p123 };
            a.ControlOut = p01; b.ControlIn = p23;
        }
        contour.Points.Insert(start.PointIndex + 1, middle);
        return new(start.ContourIndex, start.PointIndex + 1);
    }

    public void Remove(IEnumerable<Address> addresses)
    {
        foreach (var group in addresses.Distinct().GroupBy(a => a.ContourIndex).OrderByDescending(g => g.Key))
        {
            var contour = Contours[group.Key];
            foreach (var address in group.OrderByDescending(a => a.PointIndex)) contour.Points.RemoveAt(address.PointIndex);
            if (contour.Points.Count < 2) Contours.RemoveAt(group.Key);
        }
    }

    /// <summary>Deletes anchors and their incident segments, retaining disconnected runs as open contours.</summary>
    public void Cut(IEnumerable<Address> addresses)
    {
        var selected = addresses.ToHashSet();
        var result = new List<Contour>();
        for (var ci = 0; ci < Contours.Count; ci++)
        {
            var contour = Contours[ci];
            var removed = Enumerable.Range(0, contour.Points.Count).Where(i => selected.Contains(new(ci, i))).ToHashSet();
            if (removed.Count == 0) { result.Add(contour); continue; }
            var start = contour.Closed ? (removed.Min() + 1) % contour.Points.Count : 0;
            Contour? run = null;
            for (var n = 0; n < contour.Points.Count; n++)
            {
                var i = (start + n) % contour.Points.Count;
                if (removed.Contains(i)) { Finish(); continue; }
                run ??= new();
                run.Points.Add(Copy(contour.Points[i]));
            }
            Finish();
            void Finish()
            {
                if (run is { Points.Count: > 0 })
                {
                    run.Points[0].ControlIn = null;
                    run.Points[^1].ControlOut = null;
                    result.Add(run);
                }
                run = null;
            }
        }
        Contours.Clear(); Contours.AddRange(result);
    }

    public void Reverse()
    {
        foreach (var contour in Contours)
        {
            contour.Points.Reverse();
            foreach (var point in contour.Points) (point.ControlIn, point.ControlOut) = (point.ControlOut, point.ControlIn);
        }
    }

    public void Smooth(IEnumerable<Address> addresses, bool smooth)
    {
        foreach (var address in addresses.Distinct())
        {
            var contour = Contours[address.ContourIndex]; var point = this[address];
            if (!smooth) { point.ControlIn = point.ControlOut = null; continue; }
            var i = address.PointIndex; var count = contour.Points.Count;
            var before = contour.Points[contour.Closed ? (i + count - 1) % count : Math.Max(0, i - 1)].Position;
            var after = contour.Points[contour.Closed ? (i + 1) % count : Math.Min(count - 1, i + 1)].Position;
            var tangent = (after - before) / 6;
            point.ControlIn = contour.Closed || i > 0 ? point.Position - tangent : null;
            point.ControlOut = contour.Closed || i < count - 1 ? point.Position + tangent : null;
        }
    }

    public SegmentHit? HitSegment(Vec2 position, double tolerance)
    {
        if (!position.IsFinite || !double.IsFinite(tolerance) || tolerance <= 0)
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        SegmentHit? best = null;
        for (var ci = 0; ci < Contours.Count; ci++)
        {
            var contour = Contours[ci];
            for (var pi = 0; pi < contour.SegmentCount; pi++)
            {
                var a = contour.Points[pi]; var b = contour.Points[(pi + 1) % contour.Points.Count];
                var address = new Address(ci, pi);
                if (!a.ControlOut.HasValue && !b.ControlIn.HasValue) TestLine(a.Position, b.Position, 0, 1, address);
                else Visit(a.Position, a.ControlOut ?? a.Position, b.ControlIn ?? b.Position, b.Position, 0, 1, 0, address);
            }
        }
        return best;

        void TestLine(Vec2 a, Vec2 b, double t0, double t1, Address address)
        {
            var ab = b - a; var length = Dot(ab, ab);
            var t = length <= 1e-24 ? 0 : Math.Clamp(Dot(position - a, ab) / length, 0, 1);
            var distance = position.DistanceTo(Lerp(a, b, t));
            if (distance <= tolerance && (!best.HasValue || distance < best.Value.Distance))
                best = new(address, t0 + (t1 - t0) * t, distance);
        }
        void Visit(Vec2 a, Vec2 b, Vec2 c, Vec2 d, double t0, double t1, int depth, Address address)
        {
            var pad = best?.Distance ?? tolerance;
            if (position.X < Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) - pad ||
                position.X > Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) + pad ||
                position.Y < Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) - pad ||
                position.Y > Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)) + pad) return;
            var flatness = Math.Max(DistanceToLine(b, a, d), DistanceToLine(c, a, d));
            if (depth >= 16 || flatness < Math.Max(1e-6, tolerance / 16)) { TestLine(a, d, t0, t1, address); return; }
            var ab = Lerp(a, b, .5); var bc = Lerp(b, c, .5); var cd = Lerp(c, d, .5);
            var abc = Lerp(ab, bc, .5); var bcd = Lerp(bc, cd, .5); var mid = Lerp(abc, bcd, .5);
            var tm = (t0 + t1) / 2;
            Visit(a, ab, abc, mid, t0, tm, depth + 1, address);
            Visit(mid, bcd, cd, d, tm, t1, depth + 1, address);
        }
    }

    public string ToSvgPathData()
    {
        if (AnchorCount > MaxAnchors) throw new InvalidOperationException("The editable path anchor limit has been reached.");
        var result = new StringBuilder();
        foreach (var contour in Contours)
        {
            if (contour.Points.Count == 0) continue;
            result.Append('M'); Point(contour.Points[0].Position);
            for (var i = 1; i < contour.Points.Count; i++) Segment(contour.Points[i - 1], contour.Points[i]);
            if (contour.Closed)
            {
                if (contour.Points[^1].ControlOut.HasValue || contour.Points[0].ControlIn.HasValue)
                    Segment(contour.Points[^1], contour.Points[0]);
                result.Append('Z');
            }
        }
        return result.ToString();
        void Segment(PathPoint a, PathPoint b)
        {
            if (a.ControlOut.HasValue || b.ControlIn.HasValue)
            {
                result.Append('C'); Point(a.ControlOut ?? a.Position); result.Append(' ');
                Point(b.ControlIn ?? b.Position); result.Append(' '); Point(b.Position);
            }
            else { result.Append('L'); Point(b.Position); }
        }
        void Point(Vec2 point)
        {
            if (!point.IsFinite) throw new InvalidOperationException("A path coordinate is not finite.");
            result.Append(point.X.ToString("R", CultureInfo.InvariantCulture)).Append(' ').Append(point.Y.ToString("R", CultureInfo.InvariantCulture));
        }
    }

    public static Vec2 Evaluate(PathPoint a, PathPoint b, double t)
    {
        if (!a.ControlOut.HasValue && !b.ControlIn.HasValue) return Lerp(a.Position, b.Position, t);
        var u = 1 - t;
        return a.Position * (u * u * u) + (a.ControlOut ?? a.Position) * (3 * u * u * t) +
               (b.ControlIn ?? b.Position) * (3 * u * t * t) + b.Position * (t * t * t);
    }
    private static Vec2 Lerp(Vec2 a, Vec2 b, double t) => a + (b - a) * t;
    private static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
    private static double DistanceToLine(Vec2 p, Vec2 a, Vec2 b)
    {
        var delta = b - a; var length = Dot(delta, delta);
        return length <= 1e-24 ? p.DistanceTo(a) : p.DistanceTo(a + delta * Math.Clamp(Dot(p - a, delta) / length, 0, 1));
    }
}
