using System.Diagnostics;
using System.Text.Json;
using ArtSpace.Core;
using ArtSpace.Skia;
using SkiaSharp;

internal static class LiveAppearanceBenchmarks
{
    private sealed record Samples(double MedianMs, long MedianAllocatedBytes, double[] Milliseconds, long[] AllocatedBytes);
    public static int Run()
    {
        var page = new DesignPage();
        for (var i = 0; i < 800; i++)
            page.Nodes.Add(new()
            {
                X = (i % 40) * 7, Y = (i / 40) * 9, Width = 5, Height = 6,
                Fills = [new() { Kind = FillKind.LinearGradient, Start = new(0, 0), End = new(1, 0) }],
                Strokes = [new() { Width = .5, Dashes = [2, 1], DashOffset = i % 3 }]
            });
        using var renderer = new SceneRenderer();
        using (var direct = LiveAppearanceTests.Pixels(renderer, page))
        using (var retained = LiveAppearanceTests.Pixels(renderer, page, true))
            if (!direct.Pixels.SequenceEqual(retained.Pixels)) throw new InvalidOperationException("Display-list/reference pixels differ.");
        using var bitmap = new SKBitmap(new SKImageInfo(240, 200, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        var viewport = new RectD(0, 0, 240, 200);
        void Draw(bool retained)
        {
            canvas.Clear(SKColors.Transparent);
            if (retained) renderer.DrawRetained(canvas, page, viewport); else renderer.Draw(canvas, page.Nodes, viewport);
            canvas.Flush();
        }
        for (var i = 0; i < 12; i++) { Draw(false); Draw(true); }
        var paintBuilds = renderer.PaintBuilds; var dashBuilds = renderer.DashBuilds;
        var directTimes = new List<(double Ms, long Bytes)>(); var retainedTimes = new List<(double Ms, long Bytes)>();
        const int frames = 20;
        (double, long) Measure(bool retained)
        {
            var bytes = GC.GetAllocatedBytesForCurrentThread(); var started = Stopwatch.GetTimestamp();
            for (var i = 0; i < frames; i++) Draw(retained);
            return (Stopwatch.GetElapsedTime(started).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - bytes);
        }
        for (var i = 0; i < 7; i++)
        {
            if (i % 2 == 0) { directTimes.Add(Measure(false)); retainedTimes.Add(Measure(true)); }
            else { retainedTimes.Add(Measure(true)); directTimes.Add(Measure(false)); }
        }
        static Samples Report(List<(double Ms, long Bytes)> values) => new(values.Select(v => v.Ms).Order().ElementAt(values.Count / 2),
            values.Select(v => v.Bytes).Order().ElementAt(values.Count / 2), values.Select(v => v.Ms).ToArray(), values.Select(v => v.Bytes).ToArray());
        if (renderer.PaintBuilds != paintBuilds || renderer.DashBuilds != dashBuilds || renderer.SceneRecordings != 1)
            throw new InvalidOperationException("Warm rendering rebuilt retained appearance resources.");
        var directReport = Report(directTimes); var retainedReport = Report(retainedTimes);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = 1, nodes = page.Nodes.Count, framesPerSample = frames, samples = 7, pixelsEqual = true,
            note = "Alternating-order warm CPU timings on a software Skia bitmap. Native display-list playback is supported on host GPU canvases, but this experiment does not measure a physical GPU, screen presentation, application FPS, or the previous release. Recording cost is excluded; rendering quality is unchanged.",
            direct = directReport, retained = retainedReport,
            medianRatio = directReport.MedianMs / retainedReport.MedianMs,
            renderer.SceneRecordings, renderer.SceneReplays, renderer.RetainedSceneBytes,
            renderer.PaintBuilds, renderer.DashBuilds, renderer.GradientBuilds
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
