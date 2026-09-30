using System.Diagnostics;
using System.Text.Json;
using ArtSpace.Core;

internal static class PolylineProjectionBenchmarks
{
    private readonly record struct Sample(double Milliseconds, long ManagedBytes);
    private static Sample Measure(Action action)
    {
        var before = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
        action(); return new(Stopwatch.GetElapsedTime(start).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before);
    }
    public static int Run() { Console.WriteLine(Report()); return 0; }
    public static string Report()
    {
        const int count = 4096, samples = 7, queriesPerSample = 512;
        var points = Enumerable.Range(0, count + 1).Select(i => new Vec2(i, Math.Sin(i * .013) * 120)).ToArray();
        var random = new Random(330792);
        var queries = Enumerable.Range(0, queriesPerSample).Select(_ => new Vec2(random.NextDouble() * count, random.NextDouble() * 400 - 200)).ToArray();
        // Prime the code path on a smaller contour. Retained arrays/index creation are not timed as queries.
        _ = new PolylineProjectionIndex(points.AsSpan(0, 33)).Project(queries[0]);
        PolylineProjectionIndex index = null!;
        var construction = Measure(() => index = new(points));
        var maxExamined = 0; long examined = 0; var checksum = 0d;
        foreach (var query in queries)
        {
            var expected = PolylineProjectionTests.Linear(points, query);
            var actual = index.Project(query, out var statistics);
            if (actual != expected) throw new InvalidOperationException("Projection index changed the exhaustive result.");
            maxExamined = Math.Max(maxExamined, statistics.ExaminedSegments); examined += statistics.ExaminedSegments;
        }
        Action linear = () => { foreach (var query in queries) checksum += PolylineProjectionTests.Linear(points, query).Parameter; };
        Action indexed = () => { foreach (var query in queries) checksum += index.Project(query).Parameter; };
        linear(); indexed();
        var reference = new Sample[samples]; var retained = new Sample[samples];
        for (var i = 0; i < samples; i++)
        {
            if ((i & 1) == 0) { reference[i] = Measure(linear); retained[i] = Measure(indexed); }
            else { retained[i] = Measure(indexed); reference[i] = Measure(linear); }
        }
        return JsonSerializer.Serialize(new
        {
            schema = 1, runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            segments = count, queriesPerSample, samples,
            note = "CPU nearest-point coarse-search experiment. Seven alternating warm samples; index construction is reported separately. This excludes Skia arc-length refinement, rasterization, UI latency and GPU work, and is not an old-release FPS comparison.",
            construction, nativeAllocations = "none: the index contains managed arrays only",
            linear = new { medianMs = reference.Select(x => x.Milliseconds).Order().ElementAt(samples / 2), medianManagedBytes = reference.Select(x => x.ManagedBytes).Order().ElementAt(samples / 2), samples = reference },
            indexed = new { medianMs = retained.Select(x => x.Milliseconds).Order().ElementAt(samples / 2), medianManagedBytes = retained.Select(x => x.ManagedBytes).Order().ElementAt(samples / 2), samples = retained },
            exactComparisons = queries.Length, exactResults = true, averageExaminedSegments = examined / (double)queries.Length, maxExaminedSegments = maxExamined, checksum
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
