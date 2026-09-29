using System.Diagnostics;
using System.Text.Json;
using ArtSpace.Core;
using ArtSpace.Skia;
using SkiaSharp;

internal static class VariableStrokeBenchmarks
{
    private readonly record struct Measurement(double Milliseconds, long AllocatedBytes);
    private static Measurement Measure(Action action)
    {
        var allocated = GC.GetAllocatedBytesForCurrentThread(); var started = Stopwatch.GetTimestamp();
        action(); return new(Stopwatch.GetElapsedTime(started).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocated);
    }
    private static double Median(IEnumerable<double> values) { var a = values.Order().ToArray(); return (a[(a.Length - 1) / 2] + a[a.Length / 2]) / 2; }
    public static int Run()
    {
        var node = VariableStrokeTests.Line("M0 0C20 -45 80 45 100 0L140 30C160 60 180 -25 190 5");
        var stroke = node.Strokes[0]; stroke.Width = 16; stroke.Cap = StrokeCap.Round; stroke.Join = StrokeJoin.Round;
        stroke.WidthProfile = StrokeProfiles.Create(StrokeWidthPreset.Lens);
        using var renderer = new SceneRenderer(); using var coldRenderer = new SceneRenderer();
        var cold = Measure(() => coldRenderer.StrokeOutline(node, stroke));
        var contours = renderer.StrokeCenterlines(node); renderer.StrokeOutline(node, stroke);
        using var native = new SKBitmap(new SKImageInfo(240, 200, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var reference = new SKBitmap(native.Info); using var cachedCanvas = new SKCanvas(native); using var referenceCanvas = new SKCanvas(reference);
        using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(217, 87, 50, 128) };
        void DrawCached()
        {
            cachedCanvas.Clear(SKColors.Transparent); cachedCanvas.Save(); cachedCanvas.Translate(20, 90);
            cachedCanvas.DrawPath(renderer.StrokeOutline(node, stroke), paint); cachedCanvas.Restore();
        }
        void DrawRebuilt()
        {
            referenceCanvas.Clear(SKColors.Transparent); referenceCanvas.Save(); referenceCanvas.Translate(20, 90);
            using var outline = VariableStrokeGeometry.Build(contours, stroke); referenceCanvas.DrawPath(outline, paint); referenceCanvas.Restore();
        }
        for (var i = 0; i < 50; i++) { DrawCached(); DrawRebuilt(); }
        VariableStrokeTests.Same(native, reference);
        var cached = new Measurement[7]; var rebuilt = new Measurement[7];
        const int Frames = 80;
        void Batch(Action action) { for (var i = 0; i < Frames; i++) action(); }
        var outlines = renderer.StrokeOutlineBuilds; var centers = renderer.StrokeCenterlineBuilds;
        for (var sample = 0; sample < 7; sample++)
        {
            if (sample % 2 == 0) { cached[sample] = Measure(() => Batch(DrawCached)); rebuilt[sample] = Measure(() => Batch(DrawRebuilt)); }
            else { rebuilt[sample] = Measure(() => Batch(DrawRebuilt)); cached[sample] = Measure(() => Batch(DrawCached)); }
            VariableStrokeTests.Same(native, reference);
        }
        if (renderer.StrokeOutlineBuilds != outlines || renderer.StrokeCenterlineBuilds != centers) throw new InvalidOperationException("Warm stroke resources were rebuilt.");

        var line = VariableStrokeTests.Line(); var lineStroke = line.Strokes[0]; lineStroke.Dashes = [20, 10];
        DesignNode[] roots = [line]; var query = new Vec2(45, 91); const double Tolerance = 2;
        using var pickRenderer = new SceneRenderer();
        using var nativeStroke = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = (float)lineStroke.Width, StrokeCap = (SKStrokeCap)lineStroke.Cap, StrokeJoin = (SKStrokeJoin)lineStroke.Join, StrokeMiter = (float)lineStroke.MiterLimit };
        using var dash = SceneRenderer.CreateStrokeDash(lineStroke); nativeStroke.PathEffect = dash;
        using var tolerancePaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = (float)(Tolerance * 2), StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        bool ReferencePick()
        {
            using var coverage = nativeStroke.GetFillPath(pickRenderer.Geometry(line));
            coverage.Transform(SceneRenderer.Matrix(line.WorldMatrix));
            if (coverage.Contains((float)query.X, (float)query.Y)) return true;
            using var border = tolerancePaint.GetFillPath(coverage); return border.Contains((float)query.X, (float)query.Y);
        }
        for (var i = 0; i < 500; i++)
            if (ReferencePick() != (pickRenderer.HitTest(roots, query, tolerance: Tolerance) == line)) throw new InvalidOperationException("Picking reference mismatch.");
        var referencePick = new Measurement[7]; var cachedPick = new Measurement[7]; const int Queries = 2000;
        var cachedPickOutlines = pickRenderer.StrokeOutlineBuilds; var pickBorders = pickRenderer.StrokePickBuilds;
        void PickBatch(bool cache) { for (var i = 0; i < Queries; i++) if (!(cache ? pickRenderer.HitTest(roots, query, tolerance: Tolerance) == line : ReferencePick())) throw new InvalidOperationException("Lost stroke coverage."); }
        for (var sample = 0; sample < 7; sample++)
        {
            if (sample % 2 == 0) { cachedPick[sample] = Measure(() => PickBatch(true)); referencePick[sample] = Measure(() => PickBatch(false)); }
            else { referencePick[sample] = Measure(() => PickBatch(false)); cachedPick[sample] = Measure(() => PickBatch(true)); }
        }
        if (pickRenderer.StrokeOutlineBuilds != cachedPickOutlines || pickRenderer.StrokePickBuilds != pickBorders) throw new InvalidOperationException("Warm picking rebuilt coverage.");
        object Summarize(Measurement[] measurements) => new { medianMs = Median(measurements.Select(m => m.Milliseconds)), medianManagedBytes = Median(measurements.Select(m => (double)m.AllocatedBytes)), samples = measurements };
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = 1, runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            note = "Seven alternating-order samples on software Skia. Same current implementation with retained versus deliberately rebuilt coverage; not a previous-release, physical-GPU, screen-latency or whole-app FPS comparison. Rendering batches include rasterization but exclude centerline construction. Cold build is reported separately and has runtime warmup confounders. Picking checks exact same stroke and tolerance coverage.",
            coldOutline = cold, widthDrawing = new { framesPerSample = Frames, cached = Summarize(cached), rebuilt = Summarize(rebuilt), pixelsEqual = true, warmOutlineRebuilds = 0, warmCenterlineRebuilds = 0, outlinePoints = renderer.CachedStrokeOutlinePoints, centerlineSamples = renderer.CachedStrokeCenterlineSamples },
            strokePicking = new { queriesPerSample = Queries, cached = Summarize(cachedPick), rebuilt = Summarize(referencePick), coverageEqual = true, warmOutlineRebuilds = 0, warmToleranceRebuilds = 0 }
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
