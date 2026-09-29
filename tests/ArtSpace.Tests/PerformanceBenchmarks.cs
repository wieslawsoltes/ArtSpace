using System.Diagnostics;
using System.Text.Json;
using ArtSpace.Core;
using ArtSpace.Documents;
using ArtSpace.Editing;
using ArtSpace.Illustration;
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
    private static void VerifyBoundaryRegressions()
    {
        static void Require(bool success, string message) { if (!success) throw new InvalidOperationException(message); }
        var legacy = DocumentJson.Save(new DesignDocument()).Replace("\"formatVersion\":4", "\"formatVersion\":1", StringComparison.Ordinal);
        var migrated = DocumentJson.Load(legacy);
        Require(migrated.FormatVersion == 1, "Legacy schema must still load.");
        var saved = DocumentJson.Save(migrated);
        Require(migrated.FormatVersion == DocumentJson.CurrentFormatVersion && saved.Contains("\"formatVersion\":4", StringComparison.Ordinal), "Native saves must advertise clipping-aware schema 4.");
        var node = new DesignNode();
        var editor = new EditorSession(new() { Pages = [new() { Nodes = [node] }] }); editor.Select(node);
        var before = DocumentJson.Save(editor.Document); var failed = false;
        try { editor.Edit("invalid serialization", () => node.X = double.NaN); }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException) { failed = true; }
        Require(failed && !editor.IsInteracting && !editor.CanUndo && DocumentJson.Save(editor.Document) == before && double.IsFinite(editor.Primary!.X), "Serialization failure must restore the transaction snapshot and selection.");
        var a = new DesignNode(); var mask = new DesignNode();
        editor = new(new() { Pages = [new() { Nodes = [a, mask] }] }); editor.SelectAll(); ClippingOperations.Make(editor);
        var group = editor.Primary!; editor.Select(mask); before = DocumentJson.Save(editor.Document); failed = false;
        try { editor.GroupSelection(); } catch (InvalidOperationException) { failed = true; }
        Require(failed && DocumentJson.Save(editor.Document) == before, "Regrouping a live clipping path must not leave a dangling reference.");
        editor.Select([a.Id, mask.Id]); failed = false;
        try { ClippingOperations.Make(editor); } catch (InvalidOperationException) { failed = true; }
        Require(failed && group.ClippingPath == mask, "Making a nested set must not steal its parent's clipping path.");
        using var renderer = new SceneRenderer();
        var path = new DesignNode { Kind = NodeKind.Path, PathData = "M30 80L50 30L70 80", Fills = [], Strokes = [new() { Width = 20, Join = StrokeJoin.Miter }] };
        using var stroke = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 20, StrokeCap = SKStrokeCap.Round };
        using var outline = stroke.GetFillPath(renderer.Geometry(path));
        var tested = 0;
        for (var y = 0; y < 30; y++)
            for (var x = 30; x < 70; x++)
                if (outline.Contains(x, y)) { tested++; Require(renderer.HitTest([path], new(x, y), true, 0) == path, "Picking broad phase lost a miter."); }
        Require(tested > 0, "Miter fixture must exercise stroke ink outside its centerline bounds.");
    }
    public static int Run()
    {
        // These fail-fast correctness checks are intentionally excluded from all timing/allocation intervals.
        VerifyBoundaryRegressions();
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
        var appearance = AppearanceBenchmarks.Run();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            appearance,
            schema = 1, framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            note = "CPU microbenchmarks; software Skia surface; not a physical GPU or end-to-end frame-rate measurement. Index build excluded from query timings. Single-run timings vary with tiering and host load.",
            boundaryChecks = new { passed = 5, cases = new[] { "schema migration", "serialization rollback", "live mask grouping", "nested mask ownership", "miter picking" } },
            snapping = new { targets = targets.Length, queries = queries.Length, referenceMs = referenceSnap.Ms, indexedMs = indexedSnap.Ms, referenceAllocatedBytes = referenceSnap.Bytes, indexedAllocatedBytes = indexedSnap.Bytes },
            geometryCache = new { hits, oldSignatureMs = oldKey.Ms, snapshotHitMs = cacheHits.Ms, oldSignatureBytes = oldKey.Bytes, snapshotHitBytes = cacheHits.Bytes, signatureCharacters },
            rendering = new { nodes = 5001, frames = 10, renderedNodes = rendered, culledNodes = culled, cullingMs = optimized.Ms, noCullingMs = uncullled.Ms, cullingAllocatedBytes = optimized.Bytes, noCullingAllocatedBytes = uncullled.Bytes, pixelsEqual = true }
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
