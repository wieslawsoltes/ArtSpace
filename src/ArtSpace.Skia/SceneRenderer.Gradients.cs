using ArtSpace.Core;
using SkiaSharp;

namespace ArtSpace.Skia;

public sealed partial class SceneRenderer
{
    private readonly Dictionary<FillStyle, CachedGradient> _gradients = new(ReferenceEqualityComparer.Instance);
    private readonly record struct GradientKey(FillKind Kind, GradientSpace Space, GradientSpread Spread, Vec2 Start, Vec2 End, double Radius, Vec2? Focus, Matrix2D Transform, RectD Box, double Width, double Height);
    private readonly record struct StopKey(double Offset, string Color, double Opacity);
    private sealed record CachedGradient(GradientKey Key, StopKey[] Stops, SKShader Shader);
    public long GradientBuilds { get; private set; }
    public long GradientCacheHits { get; private set; }
    public int CachedGradientCount => _gradients.Count;

    private RectD GradientBounds(DesignNode node)
    {
        if (node.Kind == NodeKind.Text) return node.LocalBounds;
        var b = Geometry(node).TightBounds;
        return new(b.Left, b.Top, b.Width, b.Height);
    }

    /// <summary>Maps persisted gradient coordinates to node-local coordinates for interactive editing.</summary>
    public Matrix2D GradientCoordinateMatrix(DesignNode node, FillStyle fill)
    {
        ArgumentNullException.ThrowIfNull(node); ArgumentNullException.ThrowIfNull(fill);
        if (fill.GradientSpace == GradientSpace.Legacy)
            return Matrix2D.Scale(Math.Max(.000001, node.Width), Math.Max(.000001, node.Height)) * fill.GradientTransform;
        if (fill.GradientSpace == GradientSpace.UserSpaceOnUse) return fill.GradientTransform;
        var b = GradientBounds(node);
        return fill.GradientTransform * Matrix2D.Scale(Math.Max(.000001, b.Width), Math.Max(.000001, b.Height)) * Matrix2D.Translation(b.X, b.Y);
    }

    private SKShader? Shader(FillStyle fill, DesignNode node)
    {
        if (fill.Kind == FillKind.Solid) return null;
        var box = fill.GradientSpace == GradientSpace.ObjectBoundingBox ? GradientBounds(node) : node.LocalBounds;
        var key = new GradientKey(fill.Kind, fill.GradientSpace, fill.GradientSpread, fill.Start, fill.End, fill.GradientRadius, fill.GradientFocus, fill.GradientTransform, box, node.Width, node.Height);
        if (_gradients.TryGetValue(fill, out var cached) && cached.Key == key && cached.Stops.Length == fill.Stops.Count)
        {
            var equal = true;
            for (var i = 0; i < cached.Stops.Length; i++)
            {
                var a = cached.Stops[i]; var b = fill.Stops[i];
                if (a.Offset != b.Offset || a.Color != b.Color || a.Opacity != b.Opacity) { equal = false; break; }
            }
            if (equal) { GradientCacheHits++; return cached.Shader; }
        }
        var snapshot = fill.Stops.Select(s => new StopKey(s.Offset, s.Color, s.Opacity)).ToArray();
        var stops = snapshot.OrderBy(s => s.Offset).ToArray();
        SKShader shader;
        if (stops.Length == 0) shader = SKShader.CreateColor(SKColors.Transparent);
        else if (stops.Length == 1) shader = SKShader.CreateColor(Color(stops[0].Color, stops[0].Opacity));
        else
        {
            var colors = stops.Select(s => Color(s.Color, s.Opacity)).ToArray();
            var positions = stops.Select(s => (float)Math.Clamp(s.Offset, 0, 1)).ToArray();
            var start = fill.Start; var end = fill.End; var radius = fill.GradientRadius;
            var transform = fill.GradientTransform;
            var focus = fill.GradientFocus ?? start;
            if (fill.GradientSpace == GradientSpace.Legacy)
            {
                start = new(start.X * node.Width, start.Y * node.Height);
                end = new(end.X * node.Width, end.Y * node.Height);
                focus = start; radius = Math.Max(1, start.DistanceTo(end));
            }
            else if (fill.GradientSpace == GradientSpace.ObjectBoundingBox)
                transform *= Matrix2D.Scale(Math.Max(.000001, box.Width), Math.Max(.000001, box.Height)) * Matrix2D.Translation(box.X, box.Y);
            var tile = fill.GradientSpread switch { GradientSpread.Repeat => SKShaderTileMode.Repeat, GradientSpread.Reflect => SKShaderTileMode.Mirror, _ => SKShaderTileMode.Clamp };
            SKShader? basic;
            if (fill.Kind == FillKind.LinearGradient)
                basic = start.DistanceTo(end) < 1e-9 ? SKShader.CreateColor(colors[^1]) : SKShader.CreateLinearGradient(ToPoint(start), ToPoint(end), colors, positions, tile);
            else if (radius <= 1e-9) basic = SKShader.CreateColor(colors[^1]);
            else
            {
                var offset = focus - start; var length = offset.DistanceTo(Vec2.Zero);
                if (length >= radius) focus = start + offset * (radius * .999999 / Math.Max(length, 1e-12));
                basic = focus == start ? SKShader.CreateRadialGradient(ToPoint(start), (float)radius, colors, positions, tile)
                    : SKShader.CreateTwoPointConicalGradient(ToPoint(focus), 0, ToPoint(start), (float)radius, colors, positions, tile);
            }
            if (basic is null) throw new InvalidOperationException("Could not construct gradient shader.");
            // Skia may return the same native/managed shader for an identity matrix. Do not dispose
            // that object via a temporary 'using': the retained cache owns it from this point onward.
            if (transform == Matrix2D.Identity) shader = basic;
            else
            {
                try { shader = basic.WithLocalMatrix(Matrix(transform)) ?? throw new InvalidOperationException("Could not transform gradient shader."); }
                catch { basic.Dispose(); throw; }
                if (!ReferenceEquals(shader, basic)) basic.Dispose();
            }
        }
        if (_gradients.Count >= 8192) ClearGradients();
        else cached?.Shader.Dispose();
        _gradients[fill] = new(key, snapshot, shader); GradientBuilds++; return shader;
    }
    private static SKPoint ToPoint(Vec2 point) => new((float)point.X, (float)point.Y);
    private void PruneGradients(IEnumerable<DesignNode> nodes)
    {
        var retained = nodes.SelectMany(n => n.Fills.Concat(n.Strokes.Select(s => s.Paint).OfType<FillStyle>())).ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var fill in _gradients.Keys.Where(f => !retained.Contains(f)).ToArray())
        { _gradients[fill].Shader.Dispose(); _gradients.Remove(fill); }
    }
    private void ClearGradients()
    {
        foreach (var entry in _gradients.Values) entry.Shader.Dispose(); _gradients.Clear();
    }
}
