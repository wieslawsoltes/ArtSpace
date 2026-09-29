using ArtSpace.Core;
using SkiaSharp;

namespace ArtSpace.Skia;

public sealed partial class SceneRenderer
{
    private sealed class FillPaintEntry
    {
        public readonly SKPaint Paint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
        public SKShader? Shader;
    }
    private sealed class StrokePaintEntry : IDisposable
    {
        public readonly SKPaint Paint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
        public SKShader? Shader;
        public SKPathEffect? Dash;
        public double[] Intervals = [];
        public double Phase = double.NaN;
        public bool Variable;
        public void Dispose() { Paint.Dispose(); Dash?.Dispose(); }
    }
    private readonly Dictionary<FillStyle, FillPaintEntry> _fillPaints = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<StrokeStyle, StrokePaintEntry> _strokePaints = new(ReferenceEqualityComparer.Instance);
    public long PaintBuilds { get; private set; }
    public long DashBuilds { get; private set; }
    public int CachedPaintCount => _fillPaints.Count + _strokePaints.Count;

    private SKPaint FillPaint(FillStyle fill, DesignNode node)
    {
        if (!_fillPaints.TryGetValue(fill, out var entry))
        {
            if (CachedPaintCount >= 16384) ClearPaints();
            _fillPaints[fill] = entry = new(); PaintBuilds++;
        }
        var shader = Shader(fill, node);
        if (!ReferenceEquals(entry.Shader, shader)) { entry.Paint.Shader = shader; entry.Shader = shader; }
        var color = shader is null ? Color(fill.Color, fill.Opacity) : Color("#FFFFFF", fill.Opacity);
        if (entry.Paint.Color != color) entry.Paint.Color = color;
        return entry.Paint;
    }

    private SKPaint StrokePaint(StrokeStyle stroke, DesignNode node)
    {
        if (!_strokePaints.TryGetValue(stroke, out var entry))
        {
            if (CachedPaintCount >= 16384) ClearPaints();
            _strokePaints[stroke] = entry = new(); PaintBuilds++;
        }
        var source = stroke.Paint;
        var shader = source is null ? null : Shader(source, node);
        var opacity = stroke.Opacity * (source?.Opacity ?? 1);
        var color = shader is null ? Color(source?.Color ?? stroke.Color, opacity) : Color("#FFFFFF", opacity);
        var paint = entry.Paint;
        if (paint.Color != color) paint.Color = color;
        if (!ReferenceEquals(entry.Shader, shader)) { paint.Shader = shader; entry.Shader = shader; }
        if (paint.StrokeWidth != (float)stroke.Width) paint.StrokeWidth = (float)stroke.Width;
        if (paint.StrokeCap != (SKStrokeCap)stroke.Cap) paint.StrokeCap = (SKStrokeCap)stroke.Cap;
        if (paint.StrokeJoin != (SKStrokeJoin)stroke.Join) paint.StrokeJoin = (SKStrokeJoin)stroke.Join;
        if (paint.StrokeMiter != (float)stroke.MiterLimit) paint.StrokeMiter = (float)stroke.MiterLimit;
        var variable = stroke.WidthProfile.Count != 0;
        var style = variable ? SKPaintStyle.Fill : SKPaintStyle.Stroke;
        if (paint.Style != style) paint.Style = style;
        var equal = entry.Variable == variable && entry.Phase == stroke.DashOffset && entry.Intervals.Length == stroke.Dashes.Count;
        if (equal)
            for (var i = 0; i < entry.Intervals.Length; i++)
                if (entry.Intervals[i] != stroke.Dashes[i]) { equal = false; break; }
        if (!equal)
        {
            var dash = variable ? null : CreateStrokeDash(stroke);
            paint.PathEffect = dash;
            if (!ReferenceEquals(entry.Dash, dash)) entry.Dash?.Dispose();
            entry.Dash = dash; entry.Variable = variable; entry.Phase = stroke.DashOffset; entry.Intervals = [.. stroke.Dashes];
            if (dash is not null) DashBuilds++;
        }
        return paint;
    }

    /// <summary>Returns an owned dash effect. SVG odd-length lists repeat to form an even pattern.</summary>
    public static SKPathEffect? CreateStrokeDash(StrokeStyle stroke)
    {
        if (stroke.Dashes.Count == 0) return null;
        var count = stroke.Dashes.Count;
        var intervals = new float[count % 2 == 0 ? count : count * 2];
        double period = 0;
        for (var i = 0; i < intervals.Length; i++)
        {
            intervals[i] = (float)stroke.Dashes[i % count];
            if (!float.IsFinite(intervals[i]) || intervals[i] <= 0)
                throw new InvalidOperationException("Dash intervals must be positive representable lengths.");
            period += intervals[i];
        }
        if (!double.IsFinite(stroke.DashOffset)) throw new InvalidOperationException("Dash offset must be finite.");
        return SKPathEffect.CreateDash(intervals, (float)(stroke.DashOffset % period))
            ?? throw new InvalidOperationException("Cannot create stroke dash pattern.");
    }

    private void PrunePaints(DesignNode[] nodes)
    {
        var fills = nodes.SelectMany(n => n.Fills).ToHashSet(ReferenceEqualityComparer.Instance);
        var strokes = nodes.SelectMany(n => n.Strokes).ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var fill in _fillPaints.Keys.Where(f => !fills.Contains(f)).ToArray())
        { _fillPaints[fill].Paint.Dispose(); _fillPaints.Remove(fill); }
        foreach (var stroke in _strokePaints.Keys.Where(s => !strokes.Contains(s)).ToArray())
        { _strokePaints[stroke].Dispose(); _strokePaints.Remove(stroke); }
    }
    private void ClearPaints()
    {
        foreach (var fill in _fillPaints.Values) fill.Paint.Dispose();
        foreach (var stroke in _strokePaints.Values) stroke.Dispose();
        _fillPaints.Clear(); _strokePaints.Clear();
    }
}
