using System.Diagnostics;
using System.Text.Json;
using ArtSpace.Core;
using ArtSpace.Skia;
using SkiaSharp;

internal static class GlyphAppendBenchmarks
{
    private readonly record struct Sample(double Milliseconds, long ManagedBytes);
    public static int Run() { Console.WriteLine(Report()); return 0; }

    public static string Report()
    {
        const int glyphs = 2048, iterations = 8, samples = 7;
        using var font = new SKFont(SKTypeface.Default, 20);
        using var glyph = font.GetTextPath("B", SKPoint.Empty);
        var matrices = Enumerable.Range(0, glyphs).Select(i => SceneRenderer.Matrix(Matrix2D.Rotation(i * 13 % 360) * Matrix2D.Translation(i % 64 * 24, i / 64 * 32))).ToArray();
        SKPath Build(bool direct)
        {
            var path = new SKPath();
            foreach (var matrix in matrices)
            {
                if (direct) path.AddPath(glyph, matrix);
                else { using var copy = new SKPath(glyph); copy.Transform(matrix); path.AddPath(copy); }
            }
            return path;
        }
        using var a = Build(false); using var b = Build(true);
        if (a.ToSvgPathData() != b.ToSvgPathData()) throw new InvalidOperationException("Transformed glyph append differs from clone-transform geometry.");
        Sample Measure(bool direct)
        {
            var bytes = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < iterations; i++) { using var path = Build(direct); }
            return new(Stopwatch.GetElapsedTime(start).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - bytes);
        }
        for (var i = 0; i < 4; i++) { Measure(false); Measure(true); }
        var reference = new Sample[samples]; var optimized = new Sample[samples];
        for (var i = 0; i < samples; i++)
        {
            if (i % 2 == 0) { reference[i] = Measure(false); optimized[i] = Measure(true); }
            else { optimized[i] = Measure(true); reference[i] = Measure(false); }
        }
        static object Summary(Sample[] data) => new
        {
            medianMs = data.Select(s => s.Milliseconds).Order().ElementAt(data.Length / 2),
            medianManagedBytes = data.Select(s => s.ManagedBytes).Order().ElementAt(data.Length / 2), samples = data
        };
        return JsonSerializer.Serialize(new
        {
            schema = 1, runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            glyphsPerLayout = glyphs, layoutsPerSample = iterations, samples,
            note = "Alternating-order CPU geometry-construction experiment. One existing glyph outline is instanced repeatedly; font lookup, arc-length measurement, rasterization and GPU work are excluded. Not application FPS or old-release timing.",
            cloneTransform = Summary(reference), transformedAppend = Summary(optimized),
            referenceTemporaryPathsPerSample = glyphs * iterations, optimizedTemporaryPathsPerSample = 0,
            exactGeometryEqual = true, outlinePoints = a.PointCount
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
