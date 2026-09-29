using ArtSpace.Skia;
using SkiaSharp;

namespace ArtSpace.Editor;

public sealed partial class DesignSurface
{
    public sealed record WidthHandleState(int Index, double Position, double X, double Y, double LeftX, double LeftY, double RightX, double RightY, bool Selected);
    private string? _widthNodeId;
    private int _widthStroke, _widthPoint = -1, _widthContour;
    private bool _widthMovePoint, _widthNewPoint, _widthDuplicate;
    private int _widthSide = 1;
    private VariableStrokeGeometry.Location _widthLocation;

    public int ActiveWidthStroke => _widthStroke;
    public void ActivateWidthStroke(int index)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        CancelGesture(); _widthStroke = index; _widthPoint = -1;
        if (Session is { } session) session.Tool = EditorTool.Width;
        FocusCanvas(); _canvas.Invalidate();
    }

    private (DesignNode Node, StrokeStyle Stroke)? WidthTarget()
    {
        if (Session?.Primary is not { } node || !PathEditing.CanEdit(node) || node.Strokes.Count == 0) return null;
        if (_widthNodeId != node.Id) { _widthNodeId = node.Id; _widthPoint = -1; _widthContour = 0; }
        _widthStroke = Math.Clamp(_widthStroke, 0, node.Strokes.Count - 1);
        var stroke = node.Strokes[_widthStroke];
        if (!stroke.Visible || stroke.Width <= 0) return null;
        if (_widthPoint >= stroke.WidthProfile.Count) _widthPoint = -1;
        return (node, stroke);
    }

    public IEnumerable<WidthHandleState> WidthHandles
    {
        get
        {
            if (Session is not { Tool: EditorTool.Width } editor || WidthTarget() is not { } target) yield break;
            var contours = Renderer.StrokeCenterlines(target.Node); if (contours.Count == 0) yield break;
            _widthContour = Math.Clamp(_widthContour, 0, contours.Count - 1);
            for (var i = 0; i < target.Stroke.WidthProfile.Count; i++)
            {
                var knot = target.Stroke.WidthProfile[i]; var at = VariableStrokeGeometry.At(contours, _widthContour, knot.Position);
                Vec2 Screen(Vec2 point) => editor.Viewport.WorldToScreen(target.Node.WorldMatrix.Map(point));
                var center = Screen(at.Point); var left = Screen(at.Point + at.Normal * (target.Stroke.Width * knot.Left));
                var right = Screen(at.Point - at.Normal * (target.Stroke.Width * knot.Right));
                yield return new(i, knot.Position, center.X, center.Y, left.X, left.Y, right.X, right.Y, i == _widthPoint);
            }
        }
    }

    private bool WidthPressed(Vec2 world, Vec2 screen, PointerRoutedEventArgs e)
    {
        if (Session is not { Tool: EditorTool.Width } editor) return false;
        try
        {
            var target = WidthTarget();
            if (target is not null)
            {
                foreach (var handle in WidthHandles.Reverse())
                {
                    var left = screen.DistanceTo(new(handle.LeftX, handle.LeftY)); var right = screen.DistanceTo(new(handle.RightX, handle.RightY));
                    if (screen.DistanceTo(new(handle.X, handle.Y)) <= 6)
                    { Prepare(target.Value, handle.Index, handle.Position, true, false, 1); return true; }
                    if (Math.Min(left, right) <= 8)
                    { Prepare(target.Value, handle.Index, handle.Position, false, false, left <= right ? 1 : -1); return true; }
                }
            }
            var hit = Renderer.HitTest(editor.Page.Nodes, world, true, 5 / editor.Viewport.Zoom);
            if (hit is not null && PathEditing.CanEdit(hit) && hit.Strokes.Count > 0)
            {
                if (!editor.SelectedIds.Contains(hit.Id)) editor.Select(hit);
                target = WidthTarget();
            }
            if (target is null) { StatusChanged?.Invoke("Select a stroked vector, then drag its centerline or a width handle. Shift W selects the Width tool."); return true; }
            var local = target.Value.Node.WorldMatrix.Inverse.Map(world);
            var location = VariableStrokeGeometry.Locate(Renderer.StrokeCenterlines(target.Value.Node), local);
            if (location.ContourIndex < 0) return true;
            var projected = editor.Viewport.WorldToScreen(target.Value.Node.WorldMatrix.Map(location.Point));
            if (hit != target.Value.Node && projected.DistanceTo(screen) > 8) { _widthPoint = -1; _canvas.Invalidate(); return true; }
            _widthContour = location.ContourIndex;
            var side = (local.X - location.Point.X) * location.Normal.X + (local.Y - location.Point.Y) * location.Normal.Y >= 0 ? 1 : -1;
            Prepare(target.Value, -1, location.Position, false, true, side); return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        { CancelGesture(); StatusChanged?.Invoke(ex.Message); return true; }

        void Prepare((DesignNode Node, StrokeStyle Stroke) target, int index, double position, bool movePoint, bool newPoint, int side)
        {
            _widthPoint = index; _widthMovePoint = movePoint; _widthNewPoint = newPoint; _widthSide = side;
            _widthDuplicate = movePoint && e.KeyModifiers.HasFlag(VirtualKeyModifiers.Menu);
            _widthLocation = VariableStrokeGeometry.At(Renderer.StrokeCenterlines(target.Node), _widthContour, position);
            _gesture = Gesture.PendingWidth; _canvas.Invalidate();
        }
    }

    private void MoveWidth(Vec2 world, Vec2 screen, bool asymmetric)
    {
        if (Session is not { } editor || WidthTarget() is not { } target) { CancelGesture(); return; }
        try
        {
            if (_gesture == Gesture.PendingWidth)
            {
                if (screen.DistanceTo(_startScreen) < 3) return;
                editor.BeginInteraction(_widthMovePoint ? "Move width point" : "Change stroke width");
                _gesture = Gesture.Width;
                if (_widthNewPoint)
                {
                    if (target.Stroke.WidthProfile.Count == 0)
                        target.Stroke.WidthProfile = [new() { Position = 0 }, new() { Position = 1 }];
                    _widthPoint = StrokeProfiles.Insert(target.Stroke.WidthProfile, _widthLocation.Position);
                }
            }
            if (_widthPoint < 0 || _widthPoint >= target.Stroke.WidthProfile.Count) { CancelGesture(); return; }
            var local = target.Node.WorldMatrix.Inverse.Map(world);
            var points = target.Stroke.WidthProfile; var knot = points[_widthPoint];
            if (_widthMovePoint)
            {
                var position = VariableStrokeGeometry.Locate([Renderer.StrokeCenterlines(target.Node)[_widthContour]], local).Position;
                if (_widthDuplicate)
                {
                    if (Math.Abs(position - knot.Position) < 1e-5) return;
                    var old = knot.Clone(); var existing = points.Count;
                    var index = StrokeProfiles.Insert(points, position);
                    if (points.Count == existing) return;
                    _widthPoint = index; knot = points[index]; knot.Left = old.Left; knot.Right = old.Right; _widthDuplicate = false;
                }
                else
                {
                    var min = _widthPoint == 0 ? 0 : points[_widthPoint - 1].Position + 1e-6;
                    var max = _widthPoint == points.Count - 1 ? 1 : points[_widthPoint + 1].Position - 1e-6;
                    if (min <= max) knot.Position = Math.Clamp(position, min, max);
                }
            }
            else
            {
                var at = VariableStrokeGeometry.At(Renderer.StrokeCenterlines(target.Node), _widthContour, knot.Position);
                var distance = ((local.X - at.Point.X) * at.Normal.X + (local.Y - at.Point.Y) * at.Normal.Y) * _widthSide;
                var value = Math.Clamp(distance / target.Stroke.Width, 0, StrokeProfiles.MaxSide);
                if (_widthSide > 0) knot.Left = value; else knot.Right = value;
                if (!asymmetric) knot.Left = knot.Right = value;
            }
            StrokeProfiles.Validate(points); Renderer.StrokeOutline(target.Node, target.Stroke);
            ComponentService.SetAppearanceOverride(target.Node); editor.Preview();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        { CancelGesture(); StatusChanged?.Invoke(ex.Message); }
    }

    public bool HandleWidthKey(VirtualKey key, bool control, bool shift, bool alt)
    {
        if (Session is not { Tool: EditorTool.Width } editor || control) return false;
        if (key == VirtualKey.Escape)
        {
            if (editor.IsInteracting || _gesture == Gesture.PendingWidth) { CancelGesture(); return true; }
            if (_widthPoint >= 0) { _widthPoint = -1; _canvas.Invalidate(); return true; }
            return false;
        }
        if (key is not VirtualKey.Delete and not VirtualKey.Back and not VirtualKey.Left and not VirtualKey.Right) return false;
        if (WidthTarget() is not { } target || _widthPoint < 0) return true;
        var index = _widthPoint;
        editor.Edit(key is VirtualKey.Delete or VirtualKey.Back ? "Delete width point" : "Nudge width point", () =>
        {
            if (key is VirtualKey.Delete or VirtualKey.Back) target.Stroke.WidthProfile.RemoveAt(index);
            else
            {
                var points = target.Stroke.WidthProfile;
                var min = index == 0 ? 0 : points[index - 1].Position + 1e-6;
                var max = index == points.Count - 1 ? 1 : points[index + 1].Position - 1e-6;
                if (min <= max) points[index].Position = Math.Clamp(points[index].Position + (key == VirtualKey.Left ? -1 : 1) * (shift ? .1 : .01), min, max);
            }
            Renderer.StrokeOutline(target.Node, target.Stroke); ComponentService.SetAppearanceOverride(target.Node);
        });
        if (key is VirtualKey.Delete or VirtualKey.Back) _widthPoint = -1;
        return true;
    }

    private void ResetWidthGesture() { _widthNewPoint = _widthMovePoint = _widthDuplicate = false; }
    private void DrawWidthHandles(SKCanvas canvas)
    {
        if (Session?.Tool != EditorTool.Width) return;
        using var line = new SKPaint { IsAntialias = true, Color = new SKColor(68, 124, 238), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        using var fill = new SKPaint { IsAntialias = true, Color = SKColors.White };
        foreach (var handle in WidthHandles)
        {
            var center = new SKPoint((float)handle.X, (float)handle.Y); var left = new SKPoint((float)handle.LeftX, (float)handle.LeftY); var right = new SKPoint((float)handle.RightX, (float)handle.RightY);
            canvas.DrawLine(left, right, line);
            fill.Color = handle.Selected ? line.Color : SKColors.White;
            canvas.DrawCircle(left, 4, fill); canvas.DrawCircle(left, 4, line); canvas.DrawCircle(right, 4, fill); canvas.DrawCircle(right, 4, line);
            using var diamond = new SKPath(); diamond.MoveTo(center.X, center.Y - 4); diamond.LineTo(center.X + 4, center.Y); diamond.LineTo(center.X, center.Y + 4); diamond.LineTo(center.X - 4, center.Y); diamond.Close();
            canvas.DrawPath(diamond, fill); canvas.DrawPath(diamond, line);
        }
    }
}
