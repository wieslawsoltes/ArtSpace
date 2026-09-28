using SkiaSharp;
using ArtSpace.Documents;

namespace ArtSpace.Editor;

public sealed partial class DesignSurface
{
    public string FillColor { get; set; } = "#E6AA67";
    public string StrokeColor { get; set; } = "#203F49";
    public double StrokeWidth { get; set; } = 1;
    private DesignNode? _gradientNode;

    private bool IllustrationPressed(Vec2 world, Vec2 screen, PointerRoutedEventArgs e)
    {
        if (Session is not { } editor) return false;
        if (editor.Tool == EditorTool.Zoom)
        {
            var factor = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Menu) ? .8 : 1.25;
            editor.Viewport.ZoomAt(editor.Viewport.Zoom * factor, screen); editor.Notify(EditorChangeKind.Viewport); return true;
        }
        if (editor.Tool == EditorTool.Eyedropper)
        {
            var source = Renderer.HitTest(editor.Page.Nodes, world, true, 4 / editor.Viewport.Zoom);
            if (source is null) return true;
            var appearance = DocumentJson.CloneNode(source);
            FillColor = source.Fill; StrokeColor = source.Strokes.FirstOrDefault()?.Color ?? StrokeColor;
            StrokeWidth = source.Strokes.FirstOrDefault()?.Width ?? StrokeWidth;
            editor.UpdateSelection("Sample appearance", node =>
            {
                var copy = DocumentJson.CloneNode(appearance);
                node.Fills = copy.Fills; node.Strokes = copy.Strokes; node.Opacity = copy.Opacity;
            });
            return true;
        }
        if (editor.Tool == EditorTool.Gradient)
        {
            var node = editor.Primary;
            if (node is null || node.IsEffectivelyLocked) return true;
            editor.BeginInteraction("Edit gradient"); _gradientNode = node;
            var local = node.WorldMatrix.Inverse.Map(world);
            var fill = node.Fills.FirstOrDefault();
            if (fill is null) { fill = new() { Color = FillColor }; node.Fills.Add(fill); }
            if (fill.Kind == FillKind.Solid)
            {
                fill.Kind = FillKind.LinearGradient;
                fill.Stops = [new() { Offset = 0, Color = fill.Color }, new() { Offset = 1, Color = "#203F49" }];
            }
            fill.Start = Renderer.GradientCoordinateMatrix(node, fill).Inverse.Map(local);
            fill.End = fill.Start + new Vec2(.01, .01);
            if (fill.Kind == FillKind.RadialGradient && fill.GradientSpace != GradientSpace.Legacy)
            { fill.GradientFocus = fill.Start; fill.GradientRadius = .01; }
            _gesture = Gesture.Gradient; editor.Preview(); return true;
        }
        return PathPressed(world, screen, e);
    }

    private void MoveGradient(Vec2 world)
    {
        if (_gradientNode is not { } node || node.Fills.Count == 0 || Session is null) return;
        var fill = node.Fills[0];
        var local = node.WorldMatrix.Inverse.Map(world);
        fill.End = Renderer.GradientCoordinateMatrix(node, fill).Inverse.Map(local);
        if (fill.Kind == FillKind.RadialGradient && fill.GradientSpace != GradientSpace.Legacy)
            fill.GradientRadius = fill.Start.DistanceTo(fill.End);
        Session.Preview();
    }

    private void DrawGradientHandles(SKCanvas canvas)
    {
        DrawEditableHandles(canvas);
        if (Session is not { Tool: EditorTool.Gradient, Primary: { } node } editor || node.Fills.FirstOrDefault() is not { Kind: not FillKind.Solid } fill) return;
        var coordinates = Renderer.GradientCoordinateMatrix(node, fill);
        Vec2 Screen(Vec2 p) => editor.Viewport.WorldToScreen(node.WorldMatrix.Map(coordinates.Map(p)));
        var start = Screen(fill.Start);
        var end = Screen(fill.Kind == FillKind.RadialGradient && fill.GradientSpace != GradientSpace.Legacy ? fill.Start + new Vec2(fill.GradientRadius, 0) : fill.End);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White, StrokeWidth = 2 };
        canvas.DrawLine(P(start), P(end), paint); canvas.DrawCircle(P(start), 5, paint); canvas.DrawCircle(P(end), 5, paint);
        paint.Color = new(58, 118, 238); paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = 1;
        canvas.DrawCircle(P(start), 5, paint); canvas.DrawCircle(P(end), 5, paint);
    }
}
