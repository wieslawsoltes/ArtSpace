using ArtSpace.Core;
using SkiaSharp;

namespace ArtSpace.Skia;

/// <summary>Adaptive centerline tessellation and single-fill stroke coverage. All returned paths are caller-owned.</summary>
public static class VariableStrokeGeometry
{
    public const double DefaultTolerance = .02;
    public const int MaxSamples = 100_000;
    public const int MaxOutputPoints = 1_000_000;
    public readonly record struct Sample(Vec2 Point, double Distance);
    public sealed record Contour(Sample[] Samples, bool Closed)
    {
        public double Length => Samples.Length == 0 ? 0 : Samples[^1].Distance;
    }
    public readonly record struct Location(int ContourIndex, double Position, Vec2 Point, Vec2 Normal, double Distance);

    public static Contour[] Flatten(SKPath path, double tolerance = DefaultTolerance)
    {
        if (!double.IsFinite(tolerance) || tolerance <= 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        var editable = PathEditing.Read(path, tolerance / 4);
        var contours = new List<Contour>(); var total = 0;
        foreach (var contour in editable.Contours)
        {
            if (contour.Points.Count == 0) continue;
            var samples = new List<Sample>();
            Add(contour.Points[0].Position);
            var count = contour.Closed ? contour.Points.Count : contour.Points.Count - 1;
            for (var i = 0; i < count; i++)
            {
                var a = contour.Points[i]; var b = contour.Points[(i + 1) % contour.Points.Count];
                Subdivide(a.Position, a.ControlOut ?? a.Position, b.ControlIn ?? b.Position, b.Position, 0);
            }
            contours.Add(new(samples.ToArray(), contour.Closed));
            if (contours.Count > 4096) throw new InvalidOperationException("Stroke contour limit exceeded.");
            void Add(Vec2 p)
            {
                if (!p.IsFinite) throw new InvalidDataException("Nonfinite stroke geometry.");
                var distance = samples.Count == 0 ? 0 : samples[^1].Point.DistanceTo(p);
                if (samples.Count != 0 && distance < 1e-10) return;
                if (++total > MaxSamples) throw new InvalidOperationException("Adaptive stroke sample limit exceeded.");
                samples.Add(new(p, (samples.Count == 0 ? 0 : samples[^1].Distance) + distance));
            }
            void Subdivide(Vec2 a, Vec2 b, Vec2 c, Vec2 d, int depth)
            {
                var chord = a.DistanceTo(d);
                var polygon = a.DistanceTo(b) + b.DistanceTo(c) + c.DistanceTo(d);
                var flat = DistanceToSegment(b, a, d) <= tolerance && DistanceToSegment(c, a, d) <= tolerance;
                if (flat && polygon - chord <= tolerance) { Add(d); return; }
                if (depth >= 24) throw new InvalidOperationException("Stroke subdivision tolerance could not be reached.");
                var ab = (a + b) / 2; var bc = (b + c) / 2; var cd = (c + d) / 2;
                var abc = (ab + bc) / 2; var bcd = (bc + cd) / 2; var mid = (abc + bcd) / 2;
                Subdivide(a, ab, abc, mid, depth + 1); Subdivide(mid, bcd, cd, d, depth + 1);
            }
        }
        return contours.ToArray();
    }

    public static Location Locate(IReadOnlyList<Contour> contours, Vec2 point)
    {
        var best = new Location(-1, 0, default, default, double.PositiveInfinity);
        for (var c = 0; c < contours.Count; c++)
        {
            var contour = contours[c];
            for (var i = 1; i < contour.Samples.Length; i++)
            {
                var a = contour.Samples[i - 1]; var b = contour.Samples[i]; var delta = b.Point - a.Point;
                var length2 = Dot(delta, delta); if (length2 <= 1e-20) continue;
                var t = Math.Clamp(Dot(point - a.Point, delta) / length2, 0, 1);
                var p = a.Point + delta * t; var distance = point.DistanceTo(p);
                if (distance >= best.Distance) continue;
                best = new(c, (a.Distance + (b.Distance - a.Distance) * t) / contour.Length,
                    p, Normal(delta / Math.Sqrt(length2)), distance);
            }
        }
        return best;
    }

    public static Location At(IReadOnlyList<Contour> contours, int contourIndex, double position)
    {
        if ((uint)contourIndex >= (uint)contours.Count || !double.IsFinite(position))
            throw new ArgumentOutOfRangeException(nameof(contourIndex));
        var contour = contours[contourIndex]; var samples = contour.Samples;
        if (samples.Length < 2 || contour.Length <= 0) return new(contourIndex, 0, samples.Length == 0 ? default : samples[0].Point, new(0, 1), 0);
        var s = Math.Clamp(position, 0, 1) * contour.Length;
        var lo = 1; var hi = samples.Length - 1;
        while (lo < hi) { var mid = (lo + hi) / 2; if (samples[mid].Distance < s) lo = mid + 1; else hi = mid; }
        var a = samples[lo - 1]; var b = samples[lo]; var length = b.Distance - a.Distance;
        return new(contourIndex, Math.Clamp(position, 0, 1), a.Point + (b.Point - a.Point) * ((s - a.Distance) / length), Normal((b.Point - a.Point) / length), 0);
    }

    public static SKPath Build(IReadOnlyList<Contour> contours, StrokeStyle stroke)
    {
        StrokeProfiles.Validate(stroke.WidthProfile);
        if (!double.IsFinite(stroke.Width) || stroke.Width < 0) throw new InvalidDataException("Invalid stroke weight.");
        var result = new SKPath { FillType = SKPathFillType.Winding }; var outputPoints = 0;
        try
        {
            foreach (var contour in contours)
            {
                if (contour.Length <= 1e-10 || stroke.Width <= 0) continue;
                var runs = Runs(contour, stroke);
                foreach (var run in runs)
                {
                    var points = run; if (points.Count < 2) continue;
                    var closed = contour.Closed && runs.Count == 1 && points[0].Distance == 0 && Math.Abs(points[^1].Distance - contour.Length) < 1e-8;
                    var directions = new Vec2[points.Count - 1];
                    for (var i = 0; i < directions.Length; i++)
                    {
                        var a = points[i]; var b = points[i + 1]; var delta = b.Point - a.Point;
                        var length = Math.Sqrt(Dot(delta, delta)); if (length <= 1e-10) continue;
                        var direction = directions[i] = delta / length; var normal = Normal(direction);
                        var wa = Width(a.Distance); var wb = Width(b.Distance);
                        Polygon([a.Point + normal * wa.Left, a.Point - normal * wa.Right, b.Point - normal * wb.Right, b.Point + normal * wb.Left]);
                    }
                    for (var i = 1; i < points.Count - 1; i++)
                        Join(points[i].Point, directions[i - 1], directions[i], Width(points[i].Distance), Width(points[i].Distance));
                    if (closed) Join(points[0].Point, directions[^1], directions[0], Width(contour.Length), Width(0));
                    else
                    {
                        Cap(points[0].Point, directions[0] * -1, Width(points[0].Distance), true);
                        Cap(points[^1].Point, directions[^1], Width(points[^1].Distance), false);
                    }
                }
                (double Left, double Right) Width(double distance)
                {
                    // Wrapped dash runs retain distances greater than Length after crossing a closed seam.
                    var s = distance > contour.Length ? distance - contour.Length : distance;
                    var width = StrokeProfiles.Evaluate(stroke.WidthProfile, s / contour.Length);
                    return (width.Left * stroke.Width, width.Right * stroke.Width);
                }
            }
            return result;
        }
        catch { result.Dispose(); throw; }

        void Polygon(Vec2[] points)
        {
            outputPoints += points.Length;
            if (outputPoints > MaxOutputPoints) throw new InvalidOperationException("Stroke outline complexity limit exceeded.");
            double area = 0;
            for (var i = 0; i < points.Length; i++)
            {
                if (!points[i].IsFinite || Math.Abs(points[i].X) > 1e12 || Math.Abs(points[i].Y) > 1e12)
                    throw new InvalidDataException("Stroke outline exceeds the representable geometry range.");
                var a = points[i]; var b = points[(i + 1) % points.Length]; area += Cross(a, b);
            }
            if (Math.Abs(area) < 1e-15) return;
            if (area < 0) Array.Reverse(points);
            result.MoveTo(P(points[0])); for (var i = 1; i < points.Length; i++) result.LineTo(P(points[i])); result.Close();
        }
        void Join(Vec2 p, Vec2 u, Vec2 v, (double Left, double Right) before, (double Left, double Right) after)
        {
            var cross = Cross(u, v); var dot = Dot(u, v);
            if (Math.Abs(cross) < 1e-10)
            {
                if (dot < 0) { Cap(p, u, before, false); Cap(p, v * -1, after, true); }
                return;
            }
            var side = cross > 0 ? -1d : 1d;
            var r0 = side < 0 ? before.Right : before.Left; var r1 = side < 0 ? after.Right : after.Left;
            var a = p + Normal(u) * (side * r0); var b = p + Normal(v) * (side * r1);
            if (stroke.Join == StrokeJoin.Round)
            {
                var angle = Math.Atan2(a.Y - p.Y, a.X - p.X);
                var sweep = Math.Atan2(cross, dot); var count = ArcSteps(Math.Max(r0, r1), Math.Abs(sweep));
                var polygon = new Vec2[count + 2]; polygon[0] = p;
                for (var i = 0; i <= count; i++) { var t = i / (double)count; var r = r0 + (r1 - r0) * t; polygon[i + 1] = p + new Vec2(Math.Cos(angle + sweep * t), Math.Sin(angle + sweep * t)) * r; }
                Polygon(polygon);
            }
            else if (stroke.Join == StrokeJoin.Miter)
            {
                var intersection = a + u * (Cross(b - a, v) / cross);
                if (intersection.DistanceTo(p) <= Math.Max(r0, r1) * Math.Max(1, stroke.MiterLimit)) Polygon([p, a, intersection, b]);
                else Polygon([p, a, b]);
            }
            else Polygon([p, a, b]);
        }
        void Cap(Vec2 p, Vec2 outward, (double Left, double Right) width, bool start)
        {
            if (stroke.Cap == StrokeCap.Butt) return;
            var n = Normal(start ? outward * -1 : outward);
            var a = p + n * width.Left; var b = p - n * width.Right;
            var radius = (width.Left + width.Right) / 2;
            if (radius <= 1e-12) return;
            if (stroke.Cap == StrokeCap.Square) { Polygon([a, b, b + outward * radius, a + outward * radius]); return; }
            var center = (a + b) / 2; var normal = Normal(outward);
            var count = ArcSteps(radius, Math.PI); var polygon = new Vec2[count + 2]; polygon[0] = center;
            for (var i = 0; i <= count; i++) { var angle = Math.PI * i / count; polygon[i + 1] = center + normal * (Math.Cos(angle) * radius) + outward * (Math.Sin(angle) * radius); }
            Polygon(polygon);
        }
    }

    private static List<List<Sample>> Runs(Contour contour, StrokeStyle stroke)
    {
        var runs = new List<List<Sample>>(); List<Sample>? run = null;
        var dash = stroke.Dashes; var dashCount = dash.Count % 2 == 0 ? dash.Count : dash.Count * 2;
        double period = 0;
        for (var i = 0; i < dashCount; i++)
        {
            var value = dash[i % dash.Count]; if (!double.IsFinite(value) || value <= 0) throw new InvalidDataException("Dash lengths must be finite and positive."); period += value;
        }
        if (!double.IsFinite(stroke.DashOffset) || !double.IsFinite(period)) throw new InvalidDataException("Invalid dash phase.");
        var dashIndex = 0; var remain = double.PositiveInfinity;
        if (dashCount > 0)
        {
            var phase = ((stroke.DashOffset % period) + period) % period;
            while (phase >= dash[dashIndex % dash.Count]) { phase -= dash[dashIndex % dash.Count]; dashIndex = (dashIndex + 1) % dashCount; }
            remain = dash[dashIndex % dash.Count] - phase;
        }
        var knot = 0; var steps = 0;
        for (var i = 1; i < contour.Samples.Length; i++)
        {
            var a = contour.Samples[i - 1]; var b = contour.Samples[i]; var distance = a.Distance;
            while (distance < b.Distance - 1e-10)
            {
                if (++steps > MaxSamples) throw new InvalidOperationException("Dash/profile intersection limit exceeded.");
                while (knot < stroke.WidthProfile.Count && stroke.WidthProfile[knot].Position * contour.Length <= distance + 1e-10) knot++;
                var nextKnot = knot < stroke.WidthProfile.Count ? stroke.WidthProfile[knot].Position * contour.Length : double.PositiveInfinity;
                var next = Math.Min(b.Distance, Math.Min(distance + remain, nextKnot));
                if (next <= distance) throw new InvalidOperationException("Stroke distances cannot be represented at this scale.");
                Vec2 Position(double s) => a.Point + (b.Point - a.Point) * ((s - a.Distance) / (b.Distance - a.Distance));
                if (dashIndex % 2 == 0)
                {
                    if (run is null) { run = [new(Position(distance), distance)]; runs.Add(run); }
                    run.Add(new(Position(next), next));
                }
                var consumed = next - distance; distance = next;
                if (dashCount > 0)
                {
                    remain -= consumed;
                    if (remain <= 1e-10) { dashIndex = (dashIndex + 1) % dashCount; remain = dash[dashIndex % dash.Count]; run = null; }
                }
            }
        }
        if (contour.Closed && runs.Count > 1 && runs[0][0].Distance == 0 && Math.Abs(runs[^1][^1].Distance - contour.Length) < 1e-8)
        {
            var tail = runs[^1]; var head = runs[0];
            for (var i = 1; i < head.Count; i++) tail.Add(head[i] with { Distance = head[i].Distance + contour.Length });
            runs.RemoveAt(0);
        }
        return runs;
    }

    private static int ArcSteps(double radius, double angle) => Math.Clamp((int)Math.Ceiling(angle / Math.Max(.001, 2 * Math.Acos(Math.Clamp(1 - DefaultTolerance / Math.Max(DefaultTolerance, radius), -1, 1)))), 1, 8192);
    private static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
    private static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;
    private static Vec2 Normal(Vec2 p) => new(-p.Y, p.X);
    private static SKPoint P(Vec2 p) => new((float)p.X, (float)p.Y);
    private static double DistanceToSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        var delta = b - a; var length2 = Dot(delta, delta);
        return p.DistanceTo(a + delta * (length2 <= 1e-24 ? 0 : Math.Clamp(Dot(p - a, delta) / length2, 0, 1)));
    }
}
