using System.Diagnostics;
using System.Text.Json;
using ArtSpace.Core;
using ArtSpace.Layout;
using ArtSpace.Skia;
using SkiaSharp;

internal static class PerformanceBenchmarks
{
    private static (double Ms, long Bytes) Measure(Action action)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var bytes = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
        action(); return (Stopwatch.GetElapsedTime(start).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - bytes);
    }
    public static int Run()
    {
        var targets = Enumerable.Range(0, 10000).Select(i => new RectD((i % 100) * 32, (i / 100) * 32, 20, 20)).ToArray();
        var queries = Enumerable.Range(0, 100).Select(i => new RectD(i * 27 + .4, i * 13 + .6, 80, 60)).ToArray();
        var index = new SnapIndex(targets);
        foreach (var q in queries)
        {
            var reference = SnapEngine.Snap(q, targets, 5); var actual = index.Snap(q, 5);
            if (reference.Correction != actual.Correction || !reference.Lines.SequenceEqual(actual.Lines)) throw new InvalidOperationException("Snapping reference mismatch.");
        }
        var referenceSnap = Measure(() => { foreach (var q in queries) SnapEngine.Snap(q, targets, 5); });
        var indexedSnap = Measure(() => { foreach (var q in queries) index.Snap(q, 5); });
        var star = new DesignNode { Kind = NodeKind.Star, Sides = 32, Width = 400, Height = 400 };
        using var renderer = new SceneRenderer(); renderer.Geometry(star);
        const int hits = 20000; long signatureCharacters = 0;
        var oldKey = Measure(() => { for (var i = 0; i < hits; i++) signatureCharacters += (VectorPath.Build(star) + "|" + star.FillRule).Length; });
        var cacheHits = Measure(() => { for (var i = 0; i < hits; i++) renderer.Geometry(star); });
        var group = new DesignNode { Kind = NodeKind.Group, Fills = [] };
        for (var i = 0; i < 5000; i++) group.Add(new() { X = i % 100 * 50, Y = i / 100 * 50, Width = 30, Height = 30 });
        using var surface = SKSurface.Create(new SKImageInfo(256, 256)); renderer.Draw(surface.Canvas, [group]);
        var culled = renderer.CulledNodes; var rendered = renderer.RenderedNodes;
        var optimized = Measure(() => { for (var i = 0; i < 10; i++) { surface.Canvas.Clear(SKColors.Transparent); renderer.Draw(surface.Canvas, [group]); } });
        using var optimizedImage = surface.Snapshot(); using var optimizedPng = optimizedImage.Encode(SKEncodedImageFormat.Png, 100);
        renderer.EnableCulling = false;
        var uncullled = Measure(() => { for (var i = 0; i < 10; i++) { surface.Canvas.Clear(SKColors.Transparent); renderer.Draw(surface.Canvas, [group]); } });
        using var referenceImage = surface.Snapshot(); using var referencePng = referenceImage.Encode(SKEncodedImageFormat.Png, 100);
        if (!optimizedPng.ToArray().SequenceEqual(referencePng.ToArray())) throw new InvalidOperationException("Culling pixel mismatch.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = 1, framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            note = "CPU microbenchmarks; software Skia surface; not a physical GPU or end-to-end frame-rate measurement. Index build excluded from query timings.",
            snapping = new { targets = targets.Length, queries = queries.Length, referenceMs = referenceSnap.Ms, indexedMs = indexedSnap.Ms, referenceAllocatedBytes = referenceSnap.Bytes, indexedAllocatedBytes = indexedSnap.Bytes },
            geometryCache = new { hits, oldSignatureMs = oldKey.Ms, snapshotHitMs = cacheHits.Ms, oldSignatureBytes = oldKey.Bytes, snapshotHitBytes = cacheHits.Bytes, signatureCharacters },
            rendering = new { nodes = 5001, frames = 10, renderedNodes = rendered, culledNodes = culled, cullingMs = optimized.Ms, noCullingMs = uncullled.Ms, cullingAllocatedBytes = optimized.Bytes, noCullingAllocatedBytes = uncullled.Bytes, pixelsEqual = true }
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
