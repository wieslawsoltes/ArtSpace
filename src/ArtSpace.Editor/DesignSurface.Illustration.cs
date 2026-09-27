using SkiaSharp;
using ArtSpace.Documents;

namespace ArtSpace.Editor;

public sealed partial class DesignSurface
{
    public string FillColor { get; set; } = "#E6AA67";
    public string StrokeColor { get; set; } = "#203F49";
    public double StrokeWidth { get; set; } = 1;
    private int _anchorHandle;
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
            var start = node.WorldMatrix.Inverse.Map(world);
            var fill = node.Fills.FirstOrDefault();
            if (fill is null) { fill = new() { Color = FillColor }; node.Fills.Add(fill); }
            if (fill.Kind == FillKind.Solid)
            {
                fill.Kind = FillKind.LinearGradient;
                fill.Stops = [new() { Offset = 0, Color = fill.Color }, new() { Offset = 1, Color = "#203F49" }];
            }
            fill.Start = new(start.X / Math.Max(.001, node.Width), start.Y / Math.Max(.001, node.Height));
            fill.End = fill.Start + new Vec2(.01, .01); _gesture = Gesture.Gradient; editor.Preview(); return true;
        }
        if (editor.Tool != EditorTool.DirectSelect) return false;
        if (_vectorNode is not null && editor.SelectedIds.Contains(_vectorNode.Id) && TryAnchor(_vectorNode, screen)) return true;
        var hit = Renderer.HitTest(editor.Page.Nodes, world, true, 5 / editor.Viewport.Zoom);
        if (hit is null) { _vectorNode = null; editor.Select((DesignNode?)null); return true; }
        if (hit.IsEffectivelyLocked) return true;
        editor.Select(hit, e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift));
        _vectorNode = hit.Points.Count > 0 ? hit : null;
        if (_vectorNode is not null) TryAnchor(_vectorNode, screen);
        Invalidate(); return true;
    }

    private bool TryAnchor(DesignNode node, Vec2 screen)
    {
        if (Session is not { } editor || node.IsEffectivelyLocked) return false;
        Vec2 Position(Vec2 p) => editor.Viewport.WorldToScreen(node.WorldMatrix.Map(VectorPointPosition(node, p)));
        for (var i = 0; i < node.Points.Count; i++)
        {
            var point = node.Points[i]; var handle = -1;
            if (point.ControlIn.HasValue && Position(point.ControlIn.Value).DistanceTo(screen) < 7) handle = 1;
            if (point.ControlOut.HasValue && Position(point.ControlOut.Value).DistanceTo(screen) < 7) handle = 2;
            if (Position(point.Position).DistanceTo(screen) < 7) handle = 0;
            if (handle < 0) continue;
            editor.BeginInteraction(handle == 0 ? "Move anchor" : "Move direction handle");
            _vectorNode = node; _vertexIndex = i; _anchorHandle = handle; _gesture = Gesture.Vertex; return true;
        }
        return false;
    }

    private void MoveIllustrationAnchor(Vec2 world, bool independent)
    {
        if (_vectorNode is null || Session is null) return;
        var node = _vectorNode; var point = node.Points[_vertexIndex];
        var position = node.WorldMatrix.Inverse.Map(world);
        if (node.PathWidth > 0 && node.PathHeight > 0)
            position = new(position.X * node.PathWidth / Math.Max(.001, node.Width), position.Y * node.PathHeight / Math.Max(.001, node.Height));
        if (_anchorHandle == 0)
        {
            var delta = position - point.Position; point.Position = position;
            if (point.ControlIn.HasValue) point.ControlIn += delta;
            if (point.ControlOut.HasValue) point.ControlOut += delta;
        }
        else if (_anchorHandle == 1)
        {
            point.ControlIn = position;
            if (!independent && point.ControlOut.HasValue) point.ControlOut = point.Position * 2 - position;
        }
        else
        {
            point.ControlOut = position;
            if (!independent && point.ControlIn.HasValue) point.ControlIn = point.Position * 2 - position;
        }
        Session.Preview();
    }

    private void MoveGradient(Vec2 world)
    {
        if (_gradientNode is not { } node || node.Fills.Count == 0 || Session is null) return;
        var local = node.WorldMatrix.Inverse.Map(world);
        node.Fills[0].End = new(local.X / Math.Max(.001, node.Width), local.Y / Math.Max(.001, node.Height));
        Session.Preview();
    }

    private void DrawGradientHandles(SKCanvas canvas)
    {
        if (Session is not { Tool: EditorTool.Gradient, Primary: { } node } editor || node.Fills.FirstOrDefault() is not { Kind: not FillKind.Solid } fill) return;
        Vec2 Screen(Vec2 p) => editor.Viewport.WorldToScreen(node.WorldMatrix.Map(new(p.X * node.Width, p.Y * node.Height)));
        var start = Screen(fill.Start); var end = Screen(fill.End);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White, StrokeWidth = 2 };
        canvas.DrawLine(P(start), P(end), paint); canvas.DrawCircle(P(start), 5, paint); canvas.DrawCircle(P(end), 5, paint);
        paint.Color = new(58, 118, 238); paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = 1;
        canvas.DrawCircle(P(start), 5, paint); canvas.DrawCircle(P(end), 5, paint);
    }
}
