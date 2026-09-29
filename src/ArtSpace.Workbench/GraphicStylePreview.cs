using ArtSpace.Core;
using ArtSpace.Skia;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;

namespace ArtSpace.Workbench;

/// <summary>A live vector thumbnail. No bitmap encoding, readback, WebView, or private framebuffer.</summary>
public sealed class GraphicStylePreview : SKCanvasElement
{
    private readonly SceneRenderer _renderer = new();
    private readonly DesignNode _sample = new() { X = 24, Y = 14, Width = 112, Height = 36, CornerRadius = 7 };
    private GraphicStyle? _style;
    public new GraphicStyle? Style
    {
        get => _style;
        set
        {
            if (ReferenceEquals(_style, value)) return;
            _style = value; _renderer.ClearCache();
            value?.ApplyTo(_sample); Invalidate();
        }
    }
    public GraphicStylePreview()
    {
        IsHitTestVisible = false;
        Unloaded += (_, _) => _renderer.ClearCache();
    }
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        if (_style is null || area.Width <= 0 || area.Height <= 0) return;
        var saved = canvas.SaveCount;
        canvas.Save();
        try
        {
            canvas.ClipRect(new(0, 0, (float)area.Width, (float)area.Height));
            canvas.DrawColor(new SKColor(46, 46, 46));
            var scale = (float)Math.Min(area.Width / 160, area.Height / 64);
            canvas.Translate((float)(area.Width - 160 * scale) / 2, (float)(area.Height - 64 * scale) / 2);
            canvas.Scale(scale); _renderer.DrawWorldNode(canvas, _sample);
        }
        finally { canvas.RestoreToCount(saved); }
    }
}
