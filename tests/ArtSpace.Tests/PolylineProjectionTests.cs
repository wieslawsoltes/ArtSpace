using ArtSpace.Core;
using ArtSpace.Skia;
using SkiaSharp;

internal static class PolylineProjectionTests
{
    private static void Check(bool value, string message = "Projection assertion failed.")
    { if (!value) throw new InvalidOperationException(message); }
    private static void Throws(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new InvalidOperationException("Expected argument rejection.");
    }

    internal static PolylineProjection Linear(IReadOnlyList<Vec2> points, Vec2 point)
    {
        var best = double.PositiveInfinity; var segment = 0; var parameter = 0d;
        for (var i = 0; i < points.Count - 1; i++)
        {
            var a = points[i]; var d = points[i + 1] - a; var q = point - a;
            var denominator = d.X * d.X + d.Y * d.Y;
            var t = denominator <= 1e-20 ? 0 : Math.Clamp((q.X * d.X + q.Y * d.Y) / denominator, 0, 1);
            var difference = a + d * t - point;
            var squared = difference.X * difference.X + difference.Y * difference.Y;
            if (squared >= best) continue;
            best = squared; segment = i; parameter = t;
        }
        return new(segment, parameter, best);
    }

    public static void Register(Action<string, Action> test)
    {
        test("projection index preserves straight-segment interior and endpoint results", () =>
        {
            Vec2[] points = [new(10, 20), new(110, 20)];
            var index = new PolylineProjectionIndex(points);
            foreach (var point in new[] { new Vec2(50, 40), new(-5, 0), new(150, 30), new(10, 20), new(110, 20) })
                Check(index.Project(point) == Linear(points, point));
        });
        test("projection index resolves equal distances by original segment order", () =>
        {
            Vec2[] points = [new(10, 0), new(10, 20), new(-10, 20), new(-10, 0), new(10, 0)];
            var index = new PolylineProjectionIndex(points);
            foreach (var point in new[] { new Vec2(0, 10), new(0, 0), new(0, 20), new(10, 0) })
                Check(index.Project(point) == Linear(points, point));
        });
        test("projection index retains duplicate zero-length and tiny segment behavior", () =>
        {
            Vec2[] points = [new(0, 0), new(0, 0), new(1e-12, 0), new(10, 10), new(10, 10)];
            var index = new PolylineProjectionIndex(points);
            foreach (var point in new[] { new Vec2(0, 0), new(1e-13, 0), new(10, 10), new(5, 4) })
                Check(index.Project(point) == Linear(points, point));
        });
        test("projection index owns an immutable copy of input coordinates", () =>
        {
            Vec2[] points = [new(0, 0), new(10, 0), new(20, 20)];
            var index = new PolylineProjectionIndex(points); var expected = index.Project(new(5, 3));
            Array.Fill(points, new Vec2(1e8, 1e8));
            Check(index.Project(new(5, 3)) == expected);
        });
        test("projection index agrees with randomized exhaustive scan", () =>
        {
            var random = new Random(330792);
            for (var scene = 0; scene < 40; scene++)
            {
                var points = Enumerable.Range(0, 200).Select(_ => new Vec2(random.NextDouble() * 2000 - 1000, random.NextDouble() * 2000 - 1000)).ToArray();
                var index = new PolylineProjectionIndex(points);
                for (var query = 0; query < 100; query++)
                {
                    var point = new Vec2(random.NextDouble() * 4000 - 2000, random.NextDouble() * 4000 - 2000);
                    Check(index.Project(point) == Linear(points, point), $"Exact reference mismatch: scene {scene}, query {query}.");
                }
            }
        });
        test("projection index keeps ties across distant spatial partitions", () =>
        {
            var points = Enumerable.Range(0, 129).Select(i => new Vec2(i % 2 == 0 ? -10 : 10, i % 4 < 2 ? -10 : 10)).ToArray();
            var index = new PolylineProjectionIndex(points);
            foreach (var point in new[] { new Vec2(0, 0), new(10, 10), new(-10, 10), new(-20, 0) })
                Check(index.Project(point) == Linear(points, point));
        });
        test("projection bounds are conservative for large coordinates and rounded edges", () =>
        {
            foreach (var magnitude in new[] { 1e-6, 1d, 1e12, 1e100 })
            {
                var points = Enumerable.Range(0, 129).Select(i => new Vec2(magnitude + i * magnitude * 1e-5, -magnitude + Math.Sin(i) * magnitude * 1e-4)).ToArray();
                var index = new PolylineProjectionIndex(points);
                for (var i = 0; i < 100; i++)
                {
                    var point = points[i] + new Vec2(magnitude * 1e-6, -magnitude * 1e-6);
                    Check(index.Project(point) == Linear(points, point));
                }
            }
        });
        test("projection index enforces finite coordinate and size budgets", () =>
        {
            Throws(() => new PolylineProjectionIndex([]));
            Throws(() => new PolylineProjectionIndex(new Vec2[PolylineProjectionIndex.MaxSegments + 2]));
            Throws(() => new PolylineProjectionIndex([new(double.NaN, 0), new(1, 1)]));
            Throws(() => new PolylineProjectionIndex([new(0, 0), new(1e151, 1)]));
            var index = new PolylineProjectionIndex([new(0, 0), new(1, 1)]);
            Throws(() => index.Project(new(double.PositiveInfinity, 0)));
            Throws(() => index.Project(new(0, double.NaN)));
        });
        test("projection warm queries allocate no managed objects", () =>
        {
            var points = Enumerable.Range(0, 4097).Select(i => new Vec2(i, Math.Sin(i * .013) * 120)).ToArray();
            var index = new PolylineProjectionIndex(points); var query = new Vec2(1700, 80);
            for (var i = 0; i < 50; i++) index.Project(query);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) index.Project(query);
            Check(GC.GetAllocatedBytesForCurrentThread() == before);
            index.Project(query, out var statistics);
            Check(statistics.ExaminedSegments < 128 && statistics.VisitedNodes < 128);
        });
        test("projection immutable index supports concurrent read queries", () =>
        {
            var points = Enumerable.Range(0, 257).Select(i => new Vec2(i * 3, Math.Cos(i * .1) * 50)).ToArray();
            var index = new PolylineProjectionIndex(points);
            Parallel.For(0, 128, i => { var point = new Vec2(i * 5, 20); Check(index.Project(point) == Linear(points, point)); });
        });
        test("measured contour indexed projection preserves complete native refinement", () =>
        {
            using var path = new SKPath(); path.MoveTo(0, 30); path.CubicTo(150, -200, 500, 500, 800, 30); path.LineTo(1000, 100);
            using var contour = new MeasuredContour(path);
            for (var i = 0; i < 100; i++)
            {
                var point = new Vec2(i * 11 - 30, Math.Sin(i * .3) * 170);
                Check(contour.Project(point) == contour.Project(point, false));
            }
            Check(contour.ProjectionTableBuilds == 1 && contour.ProjectionIndexBuilds == 1);
        });
        test("measured contour closed seams and repeated queries share one index", () =>
        {
            using var path = new SKPath(); path.AddCircle(100, 100, 75);
            using var contour = new MeasuredContour(path);
            foreach (var point in new[] { new Vec2(175, 100), new(100, 25), new(100, 100), new(200, 200) })
                Check(contour.Project(point) == contour.Project(point, false));
            Check(contour.ProjectionTableBuilds == 1 && contour.ProjectionIndexBuilds == 1);
        });
    }
}
