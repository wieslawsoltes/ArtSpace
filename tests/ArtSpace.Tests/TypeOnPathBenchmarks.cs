using System.Diagnostics;
using System.Text.Json;
using ArtSpace.Core;
using ArtSpace.Editing;
using ArtSpace.Illustration;
using ArtSpace.Skia;
using SkiaSharp;

internal static class TypeOnPathBenchmarks
{
    private readonly record struct Sample(double Milliseconds, long ManagedBytes);
    private static Sample Measure(Action action)
    {
        var bytes = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp(); action();
        return new(Stopwatch.GetElapsedTime(start).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - bytes);
    }
    public static int Run()
    {
        var node = new DesignNode { Kind = NodeKind.Path, Width = 480, Height = 200, PathWidth = 480, PathHeight = 200,
            PathData = "M20 180C80 0 370 0 460 180", Fill = "#1D4864", FontSize = 22 };
        var editor = new EditorSession(new() { Pages = [new() { Nodes = [node] }] }); editor.Select(node);
        using var cold = new SceneRenderer(); using var warm = new SceneRenderer();
        TypeOnPathOperations.Create(editor, warm, "Retained path typography");
        var nodes = new[] { node };
        using var a = new SKBitmap(512, 240); using var b = new SKBitmap(512, 240);
        using var ca = new SKCanvas(a); using var cb = new SKCanvas(b);
        void Draw(SceneRenderer renderer, SKCanvas canvas) { canvas.Clear(SKColors.Transparent); renderer.Draw(canvas, nodes); }
        Draw(cold, ca); Draw(warm, cb);
        if (!a.Bytes.SequenceEqual(b.Bytes)) throw new InvalidOperationException("Path text reference pixels differ.");
        const int frames = 40; const int samples = 7;
        void ColdBatch() { for (var i = 0; i < frames; i++) { cold.ClearCache(); Draw(cold, ca); } }
        void WarmBatch() { for (var i = 0; i < frames; i++) Draw(warm, cb); }
        ColdBatch(); WarmBatch();
        var baseline = warm.TextBaselineBuilds; var layouts = warm.PathTextLayoutBuilds;
        var reference = new List<Sample>(); var retained = new List<Sample>();
        for (var i = 0; i < samples; i++)
        {
            if (i % 2 == 0) { reference.Add(Measure(ColdBatch)); retained.Add(Measure(WarmBatch)); }
            else { retained.Add(Measure(WarmBatch)); reference.Add(Measure(ColdBatch)); }
            if (!a.Bytes.SequenceEqual(b.Bytes)) throw new InvalidOperationException("Path text pixels changed during measurement.");
        }
        if (warm.TextBaselineBuilds != baseline || warm.PathTextLayoutBuilds != layouts)
            throw new InvalidOperationException("Warm path text rebuilt its native layout.");
        static object Report(List<Sample> s) => new
        {
            medianMs = s.Select(x => x.Milliseconds).Order().ElementAt(s.Count / 2),
            medianManagedBytes = s.Select(x => x.ManagedBytes).Order().ElementAt(s.Count / 2), samples = s
        };
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = 1, runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, framesPerSample = frames, samples,
            note = "Alternating-order CPU experiment on software Skia. Reference deliberately clears all renderer caches each draw; not an old-release or physical-GPU FPS comparison. Warm path excludes baseline measurement and glyph construction.",
            reference = Report(reference), retained = Report(retained), pixelsEqual = true,
            warmBaselineRebuilds = warm.TextBaselineBuilds - baseline, warmLayoutRebuilds = warm.PathTextLayoutBuilds - layouts,
            approximateRetainedLayoutBytes = warm.ApproximatePathTextBytes
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
