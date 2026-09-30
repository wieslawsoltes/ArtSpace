using SkiaSharp;
using ArtSpace.Core;

namespace ArtSpace.Skia;

/// <summary>Retained geometry cache with explicit native-resource ownership. No Uno dependency.</summary>
public sealed partial class SceneRenderer : IDisposable
{
    private sealed record CachedPath(GeometrySnapshot Snapshot, SKPath Path);
    private readonly Dictionary<string, CachedPath> _paths = [];
    private readonly Dictionary<string, SKTypeface> _typefaces = [];
    private SKTypeface? _customTypeface;
    public bool Outlines { get; set; }
    public long RenderedNodes { get; private set; }
    public long VisitedNodes { get; private set; }
    public long CulledNodes { get; private set; }
    public long GeometryBuilds { get; private set; }
    public long GeometryCacheHits { get; private set; }
    public bool EnableCulling { get; set; } = true;
    public int CachedGeometryCount => _paths.Count;
    public void PruneCache(IEnumerable<DesignNode> roots)
    {
        var nodes = roots.SelectMany(n => n.DescendantsAndSelf()).ToArray();
        InvalidateRetainedScene(); PrunePaints(nodes); PruneGradients(nodes);
        var retained = nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        PruneEffects(retained);
        PruneTypeOnPath(nodes.Where(n => n.TextPath is not null).Select(n => n.Id).ToHashSet(StringComparer.Ordinal));
        foreach (var id in _paths.Keys.Where(id => !retained.Contains(id)).ToArray())
        { _paths[id].Path.Dispose(); _paths.Remove(id); }
        foreach (var id in _textLayouts.Keys.Where(id => !retained.Contains(id)).ToArray())
        { _textLayouts[id].Font.Dispose(); _textLayouts.Remove(id); }
    }
    public void SetTypeface(SKTypeface typeface)
    {
        ArgumentNullException.ThrowIfNull(typeface);
        if (ReferenceEquals(typeface, _customTypeface)) return;
        InvalidateRetainedScene(); ClearTextLayouts(); _customTypeface?.Dispose(); _customTypeface = typeface;
    }
    public void ClearCache()
    {
        InvalidateRetainedScene(); ClearPaints(); ClearEffects();
        foreach (var p in _paths.Values) p.Path.Dispose(); _paths.Clear(); ClearTextLayouts(); ClearGradients();
    }
    public static SKColor Color(string? hex, double opacity = 1)
    {
        if (!SKColor.TryParse(hex, out var color)) color = hex?.ToLowerInvariant() switch { "white" => SKColors.White, "black" => SKColors.Black, "red" => SKColors.Red, "blue" => SKColors.Blue, "green" => SKColors.Green, "transparent" => SKColors.Transparent, _ => new SKColor(217, 217, 217) };
        return color.WithAlpha((byte)Math.Clamp(Math.Round(color.Alpha * opacity), 0, 255));
    }
    public static SKMatrix Matrix(Matrix2D m) => new((float)m.M11, (float)m.M21, (float)m.DX, (float)m.M12, (float)m.M22, (float)m.DY, 0, 0, 1);
    public static SKRect Rect(RectD r) => new((float)r.X, (float)r.Y, (float)r.Right, (float)r.Bottom);
    public SKPath Geometry(DesignNode node)
    {
        if (_paths.TryGetValue(node.Id, out var cache) && cache.Snapshot.Matches(node))
        { GeometryCacheHits++; return cache.Path; }
        var path = SKPath.ParseSvgPathData(VectorPath.Build(node)) ?? new SKPath();
        GeometryBuilds++;
        if ((node.Kind == NodeKind.Path || node.TextPath is not null) && node.PathWidth > 0 && node.PathHeight > 0) path.Transform(SKMatrix.CreateScale((float)(node.Width / node.PathWidth), (float)(node.Height / node.PathHeight)));
        path.FillType = node.FillRule == PathFillRule.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
        if (cache is not null) cache.Path.Dispose();
        if (_paths.Count > 100_000) ClearCache();
        _paths[node.Id] = new(new GeometrySnapshot(node), path); return path;
    }
    public void Draw(SKCanvas canvas, IEnumerable<DesignNode> nodes, RectD? worldViewport = null)
    {
        RenderedNodes = VisitedNodes = CulledNodes = 0;
        // Actual canvas clip is authoritative; fixed world inflation is unsafe for shadows.
        foreach (var node in nodes) DrawNode(canvas, node);
    }
    public void DrawWorldNode(SKCanvas canvas, DesignNode node)
    {
        canvas.Save(); if (node.Parent is not null) canvas.Concat(Matrix(node.Parent.WorldMatrix)); DrawNode(canvas, node); canvas.Restore();
    }
    private void DrawNode(SKCanvas canvas, DesignNode node, bool filteredAncestor = false)
    {
        if (!node.Visible || node.Opacity <= 0 || node.Kind == NodeKind.Slice) return;
        VisitedNodes++;
        canvas.Save(); canvas.Concat(Matrix(node.LocalMatrix));
        var filtered = filteredAncestor || node.Shadows.Any(s => s.Visible) || HasVisibleEffects(node);
        // Groups may overflow their nominal box; filtered sources can cast visible offscreen shadows.
        if (EnableCulling && !filtered && node.Children.Count == 0 && node.Kind != NodeKind.Text)
        {
            var bounds = Geometry(node).TightBounds;
            var outset = 1d;
            foreach (var stroke in node.Strokes)
                if (stroke.Visible) outset = Math.Max(outset, stroke.Width * .5 * Math.Max(2, stroke.Join == StrokeJoin.Miter ? stroke.MiterLimit : 2));
            var matrix = canvas.TotalMatrix;
            var scale = Math.Max(.000001, Math.Min(Math.Sqrt(matrix.ScaleX * matrix.ScaleX + matrix.SkewY * matrix.SkewY), Math.Sqrt(matrix.ScaleY * matrix.ScaleY + matrix.SkewX * matrix.SkewX)));
            outset += 2 / scale; bounds.Inflate((float)outset, (float)outset);
            if (canvas.QuickReject(bounds)) { CulledNodes++; canvas.Restore(); return; }
        }
        RenderedNodes++;
        var masked = !Outlines && node.OpacityMaskEnabled && node.OpacityMask is not null;
        if (masked && node.OpacityMaskRegion is { } region) canvas.ClipRect(Rect(region));
        var filter = Outlines ? null : EffectFilter(node);
        var layer = masked || node.Opacity < .999 || node.Blend != BlendKind.Normal || filter is not null;
        if (layer)
        {
            using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Clamp(node.Opacity * 255, 0, 255)), BlendMode = Blend(node.Blend), ImageFilter = filter };
            canvas.SaveLayer(paint);
        }
        if (Outlines && node.Kind != NodeKind.Text)
        {
            using var outline = new SKPaint { IsAntialias = true, Color = new SKColor(80, 80, 80), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
            canvas.DrawPath(Geometry(node), outline);
        }
        else
        {
            foreach (var fill in node.Fills)
            {
                if (!fill.Visible) continue;
                var paint = FillPaint(fill, node);
                if (node.Kind == NodeKind.Text) DrawText(canvas, node, paint); else canvas.DrawPath(Geometry(node), paint);
            }
            foreach (var stroke in node.Strokes)
            {
                if (!stroke.Visible || stroke.Width <= 0 || stroke.Paint?.Visible == false) continue;
                var paint = StrokePaint(stroke, node);
                if (node.Kind == NodeKind.Text) DrawText(canvas, node, paint); else canvas.DrawPath(Geometry(node), paint);
            }
        }
        canvas.Save();
        if (node.ClipContent && !Outlines)
        {
            using var clip = new SKPath(); clip.AddRoundRect(new SKRect(0, 0, (float)node.Width, (float)node.Height), (float)node.CornerRadius, (float)node.CornerRadius); canvas.ClipPath(clip, SKClipOperation.Intersect, true);
        }
        if (node.ClippingPath is { } mask && !Outlines)
        {
            using var clip = new SKPath(Geometry(mask)); clip.Transform(Matrix(mask.LocalMatrix));
            canvas.ClipPath(clip, SKClipOperation.Intersect, true);
        }
        foreach (var child in node.Children)
            if (Outlines || (child.Id != node.ClipPathId && child.Id != node.OpacityMaskId)) DrawNode(canvas, child, filtered);
        canvas.Restore();
        if (masked) ApplyOpacityMask(canvas, node);
        if (layer) canvas.Restore(); canvas.Restore();
    }
    private SKTypeface Typeface(DesignNode node)
    {
        if (_customTypeface is not null && node.FontFamily == "Inter") return _customTypeface;
        var key = node.FontFamily + "|" + node.FontWeight;
        if (!_typefaces.TryGetValue(key, out var typeface)) _typefaces[key] = typeface = SKTypeface.FromFamilyName(node.FontFamily, new SKFontStyle(node.FontWeight, 5, SKFontStyleSlant.Upright)) ?? SKTypeface.Default;
        return typeface;
    }
    public DesignNode? HitTest(IEnumerable<DesignNode> roots, Vec2 point, bool deep = false, double tolerance = 4)
    {
        foreach (var node in roots.Reverse())
        {
            if (!node.IsEffectivelyVisible || node.IsEffectivelyLocked || node.Kind == NodeKind.Slice) continue;
            var local = node.WorldMatrix.Inverse.Map(point); var inside = node.LocalBounds.Contains(local);
            if (!Outlines && node.OpacityMaskEnabled && node.OpacityMask is not null && MaskCoverageAt(node, local) <= 1d / 255) continue;
            if (node.ClippingPath is { } mask)
            {
                var maskPoint = mask.LocalMatrix.Inverse.Map(local);
                if (!Geometry(mask).Contains((float)maskPoint.X, (float)maskPoint.Y)) continue;
            }
            if (!node.ClipContent || Geometry(node).Contains((float)local.X, (float)local.Y))
            {
                var child = HitTest(node.Children.Where(c => c.Id != node.ClipPathId && c.Id != node.OpacityMaskId), point, deep, tolerance);
                if (child is not null) return deep || node.Kind == NodeKind.Frame || node.Kind == NodeKind.Section ? child : node;
            }
            if (node.Kind == NodeKind.Text && node.TextPath is null && inside) return node;
            if (node.Kind == NodeKind.Group && node.Children.Count > 0) continue;
            if (node.Kind == NodeKind.Frame && inside && node.Fills.Count > 0) return node;
            var path = node.TextPath is not null ? TypeOnPathLayout(node).Outline : Geometry(node);
            var pickBounds = path.TightBounds;
            // Include the miter reach of both the visible stroke and the tolerance-expanded picking stroke.
            var pickOutset = Math.Max(tolerance * 4, node.Strokes.Count == 0 ? 0 : node.Strokes.Max(s => s.Width * .5 * Math.Max(4, s.MiterLimit)));
            pickBounds.Inflate((float)pickOutset + 1, (float)pickOutset + 1);
            if (!pickBounds.Contains((float)local.X, (float)local.Y)) continue;
            if (node.Fills.Any(f => f.Visible) && path.Contains((float)local.X, (float)local.Y)) return node;
            if (node.Strokes.Count > 0)
            {
                using var stroke = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = (float)Math.Max(tolerance * 2, node.Strokes.Max(s => s.Width)), StrokeCap = SKStrokeCap.Round };
                using var outline = new SKPath(); stroke.GetFillPath(path, outline);
                if (outline.Contains((float)local.X, (float)local.Y)) return node;
            }
        }
        return null;
    }
    public byte[] ExportPng(IEnumerable<DesignNode> nodes, RectD bounds, double scale = 1)
    {
        nodes = nodes.ToArray();
        foreach (var node in nodes.SelectMany(n => n.DescendantsAndSelf()))
            if (node.TextPath is not null && node.IsEffectivelyVisible) ValidateTypeOnPath(node);
        var width = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale)); var height = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));
        if (!double.IsFinite(scale) || scale <= 0 || width > 16384 || height > 16384 || (long)width * height > 64_000_000) throw new InvalidOperationException("Export is limited to 16,384 pixels per edge and 64 megapixels.");
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul)) ?? throw new InvalidOperationException("Could not allocate export surface.");
        surface.Canvas.Clear(SKColors.Transparent); surface.Canvas.Scale((float)scale); surface.Canvas.Translate((float)-bounds.X, (float)-bounds.Y);
        var outlines = Outlines; Outlines = false;
        try { foreach (var n in nodes) DrawWorldNode(surface.Canvas, n); }
        finally { Outlines = outlines; }
        using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png, 100); return data.ToArray();
    }
    private static SKBlendMode Blend(BlendKind kind) => kind switch { BlendKind.Multiply => SKBlendMode.Multiply, BlendKind.Screen => SKBlendMode.Screen, BlendKind.Overlay => SKBlendMode.Overlay, BlendKind.Darken => SKBlendMode.Darken, BlendKind.Lighten => SKBlendMode.Lighten, BlendKind.Difference => SKBlendMode.Difference, BlendKind.ColorDodge => SKBlendMode.ColorDodge, BlendKind.ColorBurn => SKBlendMode.ColorBurn, BlendKind.HardLight => SKBlendMode.HardLight, BlendKind.SoftLight => SKBlendMode.SoftLight, BlendKind.Exclusion => SKBlendMode.Exclusion, BlendKind.Hue => SKBlendMode.Hue, BlendKind.Saturation => SKBlendMode.Saturation, BlendKind.Color => SKBlendMode.Color, BlendKind.Luminosity => SKBlendMode.Luminosity, _ => SKBlendMode.SrcOver };
    public void Dispose()
    {
        ClearCache(); DisposeMasks(); foreach (var face in _typefaces.Values.Distinct()) if (face != SKTypeface.Default) face.Dispose(); _typefaces.Clear(); _customTypeface?.Dispose(); _customTypeface = null;
    }
}
