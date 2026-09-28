using System.Diagnostics;
using ArtSpace.Core;
using ArtSpace.Skia;
using SkiaSharp;

internal static class AppearanceBenchmarks
{
    private sealed record Measurement(double MedianMs, long MedianAllocatedBytes, double[] SamplesMs);
    private static Measurement Measure(Action action)
    {
        action(); // Warm the exact code path before recording five samples.
        var ms = new double[5]; var bytes = new long[5];
        for (var i = 0; i < 5; i++)
        {
            var allocation = GC.GetAllocatedBytesForCurrentThread(); var started = Stopwatch.GetTimestamp();
            action(); ms[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            bytes[i] = GC.GetAllocatedBytesForCurrentThread() - allocation;
        }
        var ordered = (double[])ms.Clone(); Array.Sort(ordered); Array.Sort(bytes);
        return new(ordered[2], bytes[2], ms);
    }
    public static object Run()
    {
        var m = new Matrix2D(1.2, .15, .35, .8, 6, 7); var b = new RectD(12, 23, 45, 67);
        const int maps = 100000; double checksum = 0;
        RectD Reference() => RectD.Bounds(new[] { m.Map(new Vec2(b.X, b.Y)), m.Map(new Vec2(b.Right, b.Y)), m.Map(new Vec2(b.Right, b.Bottom)), m.Map(new Vec2(b.X, b.Bottom)) });
        var exact = m.Map(b); var reference = Reference();
        if (Math.Abs(exact.X - reference.X) > 1e-8 || Math.Abs(exact.Width - reference.Width) > 1e-8) throw new InvalidOperationException("Affine bounds differ.");
        var corners = Measure(() => { for (var i = 0; i < maps; i++) checksum += Reference().Width; });
        var direct = Measure(() => { for (var i = 0; i < maps; i++) checksum += m.Map(b).Width; });
        var n = new DesignNode { Width = 64, Height = 64, Fills = [new() { Kind = FillKind.LinearGradient, Start = new(0,0), End = new(1,1), Stops = [new() { Offset = 0, Color = "#EFAB66", Opacity = .7 }, new() { Offset = 1, Color = "#234A66" }] }] };
        using var renderer = new SceneRenderer(); using var surface = SKSurface.Create(new SKImageInfo(64, 64));
        const int frames = 500;
        void Draw() { surface.Canvas.Clear(SKColors.Transparent); renderer.Draw(surface.Canvas, [n]); }
        Draw();
        var buildsBefore = renderer.GradientBuilds;
        var cached = Measure(() => { for (var i = 0; i < frames; i++) Draw(); });
        var cachedBuilds = renderer.GradientBuilds - buildsBefore;
        using var cachedImage = surface.Snapshot(); using var cachedData = cachedImage.Encode(SKEncodedImageFormat.Png, 100);
        var cold = Measure(() => { for (var i = 0; i < frames; i++) { renderer.ClearCache(); Draw(); } });
        using var coldImage = surface.Snapshot(); using var coldData = coldImage.Encode(SKEncodedImageFormat.Png, 100);
        if (!cachedData.ToArray().SequenceEqual(coldData.ToArray()) || cachedBuilds != 0) throw new InvalidOperationException("Appearance cache changed pixels or rebuilt warm gradients.");
        return new
        {
            note = "Five-sample CPU medians on software Skia. Cleared-cache baseline deliberately rebuilds both geometry and gradient per draw; not an old-release FPS benchmark.",
            affineBounds = new { maps, fourCornerReference = corners, allocationFree = direct, checksum },
            gradientDrawing = new { framesPerSample = frames, cached, clearedEachDraw = cold, cachedGradientRebuilds = cachedBuilds, pixelsEqual = true }
        };
    }
}
