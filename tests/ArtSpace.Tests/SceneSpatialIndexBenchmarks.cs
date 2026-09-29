using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ArtSpace.Core;
using ArtSpace.Skia;
using SkiaSharp;

internal static class SceneSpatialIndexBenchmarks
{
    private sealed record Measurement(double Milliseconds, long AllocatedBytes);
    private static Measurement Measure(Action action)
    {
        var bytes = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
        action();
        return new(Stopwatch.GetElapsedTime(start).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - bytes);
    }
    private static double Median(IEnumerable<double> samples)
    {
        var values = samples.Order().ToArray();
        return values.Length % 2 == 0 ? (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2 : values[values.Length / 2];
    }
    public static int Run()
    {
        const int side = 80, pitch = 32, pixels = 256, frames = 30, sampleCount = 7;
        var page = new DesignPage();
        var group = new DesignNode { Kind = NodeKind.Group, Width = side * pitch, Height = side * pitch, Fills = [] };
        page.Nodes.Add(group);
        for (var y = 0; y < side; y++)
            for (var x = 0; x < side; x++)
                group.Add(new()
                {
                    X = x * pitch, Y = y * pitch, Width = 24, Height = 24,
                    Kind = (x + y) % 3 == 0 ? NodeKind.Ellipse : NodeKind.Rectangle,
                    Fill = (x + y) % 2 == 0 ? "#E6A360" : "#3E8391",
                    Strokes = [new() { Width = 1, Color = "#203F49" }]
                });
        using var bitmap = new SKBitmap(new SKImageInfo(pixels, pixels, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        using var indexed = new SceneRenderer { EnableSceneSpatialIndex = true };
        using var linear = new SceneRenderer { EnableSceneSpatialIndex = false };
        using var direct = new SceneRenderer { EnableRetainedScene = false };
        var sceneSize = side * pitch;
        void DrawFit(SceneRenderer renderer)
        {
            canvas.ResetMatrix(); canvas.Clear(SKColors.Transparent); canvas.Scale((float)pixels / sceneSize);
            renderer.DrawRetained(canvas, page, new(0, 0, sceneSize, sceneSize)); canvas.Flush();
        }
        var indexedCold = Measure(() => DrawFit(indexed));
        var linearCold = Measure(() => DrawFit(linear));
        void DrawViewport(SceneRenderer renderer, int position)
        {
            var x = 240 + position % 6 * 277; var y = 160 + position % 5 * 191;
            canvas.ResetMatrix(); canvas.Clear(SKColors.Transparent); canvas.Translate(-x, -y);
            renderer.DrawRetained(canvas, page, new(x, y, pixels, pixels)); canvas.Flush();
        }
        for (var i = 0; i < 12; i++)
        {
            DrawViewport(direct, i); var expected = bitmap.Pixels;
            DrawViewport(linear, i);
            if (!expected.SequenceEqual(bitmap.Pixels)) throw new InvalidOperationException("Linear retained pixels differ from direct rendering.");
            DrawViewport(indexed, i);
            if (!expected.SequenceEqual(bitmap.Pixels)) throw new InvalidOperationException("Spatially indexed pixels differ from direct rendering.");
        }
        void Batch(SceneRenderer renderer)
        { for (var i = 0; i < frames; i++) DrawViewport(renderer, i); }
        Batch(linear); Batch(indexed);
        var indexedSamples = new List<Measurement>(); var linearSamples = new List<Measurement>();
        var indexedPaints = indexed.PaintBuilds; var linearPaints = linear.PaintBuilds;
        for (var sample = 0; sample < sampleCount; sample++)
        {
            // Alternate execution order; compare like-for-like fully warm replay paths.
            if ((sample & 1) == 0)
            { linearSamples.Add(Measure(() => Batch(linear))); indexedSamples.Add(Measure(() => Batch(indexed))); }
            else
            { indexedSamples.Add(Measure(() => Batch(indexed))); linearSamples.Add(Measure(() => Batch(linear))); }
        }
        if (indexed.SceneRecordings != 1 || linear.SceneRecordings != 1 || indexed.PaintBuilds != indexedPaints || linear.PaintBuilds != linearPaints)
            throw new InvalidOperationException("Warm replay unexpectedly rebuilt scene commands or paints.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = 1, runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
            nodes = side * side + 1, viewportPixels = pixels, framesPerSample = frames, sampleCount,
            note = "Two paths in the same renderer on a software Skia bitmap, after fitting the document then zooming. Alternating warm replay samples exclude recording. Cold fit measurements include resource construction, recording and first playback in fixed indexed-then-linear order. Not physical-GPU timings, whole-app FPS or an older-release comparison.",
            indexed = new
            {
                initialFitRecordAndDraw = indexedCold, samples = indexedSamples,
                medianMs = Median(indexedSamples.Select(s => s.Milliseconds)),
                medianManagedBytes = Median(indexedSamples.Select(s => (double)s.AllocatedBytes)),
                approximateNativeCommandBytes = indexed.RetainedSceneBytes,
                recordings = indexed.SceneRecordings, replays = indexed.SceneReplays
            },
            linear = new
            {
                initialFitRecordAndDraw = linearCold, samples = linearSamples,
                medianMs = Median(linearSamples.Select(s => s.Milliseconds)),
                medianManagedBytes = Median(linearSamples.Select(s => (double)s.AllocatedBytes)),
                approximateNativeCommandBytes = linear.RetainedSceneBytes,
                recordings = linear.SceneRecordings, replays = linear.SceneReplays
            },
            exactPixelComparisons = 24, pixelsEqual = true, warmPaintRebuilds = 0
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
