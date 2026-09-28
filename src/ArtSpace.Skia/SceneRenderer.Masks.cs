using ArtSpace.Core;
using SkiaSharp;

namespace ArtSpace.Skia;

public sealed partial class SceneRenderer
{
    private SKColorFilter? _luminanceFilter;
    private SKSurface? _maskProbe;
    public long MaskComposites { get; private set; }
    public long MaskProbes { get; private set; }
    private SKColorFilter LuminanceFilter => _luminanceFilter ??= SKColorFilter.CreateLumaColor();

    private void ApplyOpacityMask(SKCanvas canvas, DesignNode owner)
    {
        if (owner.OpacityMask is not { } mask || !owner.OpacityMaskEnabled || Outlines) return;
        // The entire isolated source layer participates in DstIn/DstOut, including transparent pixels.
        // Luma filtering happens after composing all source artwork, not independently per shape.
        using var paint = new SKPaint
        {
            BlendMode = owner.OpacityMaskInverted ? SKBlendMode.DstOut : SKBlendMode.DstIn,
            ColorFilter = owner.OpacityMaskMode == OpacityMaskMode.Luminance ? LuminanceFilter : null
        };
        var count = canvas.SaveCount;
        canvas.SaveLayer(paint);
        try { DrawNode(canvas, mask, true); MaskComposites++; }
        finally { canvas.RestoreToCount(count); }
    }

    /// <summary>Coverage at a container-local point. Uses a reusable one-pixel CPU surface, never a GPU readback.</summary>
    public double MaskCoverageAt(DesignNode owner, Vec2 localPoint)
    {
        if (!localPoint.IsFinite) throw new ArgumentOutOfRangeException(nameof(localPoint));
        if (!owner.OpacityMaskEnabled || owner.OpacityMask is not { } source) return 1;
        if (owner.OpacityMaskRegion is { } region && !region.Contains(localPoint)) return 0;
        _maskProbe ??= SKSurface.Create(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Could not allocate opacity-mask hit-test surface.");
        var canvas = _maskProbe.Canvas;
        var oldVisited = VisitedNodes; var oldRendered = RenderedNodes; var oldCulled = CulledNodes;
        var oldOutlines = Outlines; var count = canvas.SaveCount;
        try
        {
            Outlines = false;
            canvas.Save(); canvas.ResetMatrix(); canvas.Clear(SKColors.Transparent);
            canvas.Translate((float)(.5 - localPoint.X), (float)(.5 - localPoint.Y));
            DrawNode(canvas, source, true);
            using var pixels = _maskProbe.PeekPixels();
            var color = pixels.GetPixelColor(0, 0);
            var coverage = color.Alpha / 255d;
            if (owner.OpacityMaskMode == OpacityMaskMode.Luminance)
                coverage *= (.2126 * color.Red + .7152 * color.Green + .0722 * color.Blue) / 255d;
            MaskProbes++;
            return owner.OpacityMaskInverted ? 1 - coverage : coverage;
        }
        finally
        {
            canvas.RestoreToCount(count); Outlines = oldOutlines;
            VisitedNodes = oldVisited; RenderedNodes = oldRendered; CulledNodes = oldCulled;
        }
    }
    private void DisposeMasks()
    {
        _maskProbe?.Dispose(); _maskProbe = null;
        _luminanceFilter?.Dispose(); _luminanceFilter = null;
    }
}
