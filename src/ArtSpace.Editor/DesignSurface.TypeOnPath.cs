using ArtSpace.Skia;
using SkiaSharp;

namespace ArtSpace.Editor;

public sealed partial class DesignSurface
{
    public readonly record struct TypeOnPathHandle(int Kind, Vec2 Position, Vec2 BaselinePosition, double Fraction);
    private DesignNode? _typePathDragNode;
    private TypeOnPathOptions? _typePathOriginal;
    private Matrix2D _typePathInverse;
    private int _typePathHandle;
    private double _typePathInitialFraction, _typePathInitialSide;

    /// <summary>Read-only bracket positions in surface screen coordinates. 0=start, 1=end, 2=center.</summary>
    public IReadOnlyList<TypeOnPathHandle> GetTypeOnPathHandles()
    {
        if (Session is not { Tool: EditorTool.Move, Primary: { TextPath: { } options } node } editor
            || editor.SelectionRoots.Count != 1 || node.IsEffectivelyLocked || IsPresenting) return [];
        if (Renderer.GetTypeOnPathStatus(node).Error is not null) return [];
        var svg = options.SvgPosition;
        var svgFraction = svg is null ? 0 : svg.Resolve(Renderer.GetTypeOnPathStatus(node).PathLength) / Renderer.GetTypeOnPathStatus(node).PathLength;
        var result = new TypeOnPathHandle[svg is null ? 3 : 1];
        var fractions = svg is null ? new[] { options.Start, options.End, (options.Start + options.End) / 2 } : new[] { Math.Clamp(options.Flip ? 1 - svgFraction : svgFraction, 0, 1) };
        for (var i = 0; i < result.Length; i++)
        {
            var sample = Renderer.GetTypeOnPathSample(node, fractions[i]);
            var p = editor.Viewport.WorldToScreen(node.WorldMatrix.Map(sample.Position));
            var next = editor.Viewport.WorldToScreen(node.WorldMatrix.Map(sample.Position + sample.Tangent));
            var tangent = next - p; var length = Math.Max(1e-12, tangent.DistanceTo(Vec2.Zero)); tangent /= length;
            var normal = new Vec2(-tangent.Y, tangent.X);
            // Distinct offsets keep coincident start/end handles independently pickable on closed contours.
            var distance = i == 0 ? 16 : i == 1 ? 30 : 24;
            result[i] = new(svg is null ? i : 3, p + normal * distance, p, fractions[i]);
        }
        return result;
    }

    private bool TypeOnPathPressed(Vec2 world, Vec2 screen)
    {
        if (Session?.Primary is not { TextPath: not null } node || Session.Tool != EditorTool.Move) return false;
        foreach (var handle in GetTypeOnPathHandles())
        {
            if (handle.Position.DistanceTo(screen) > 8) continue;
            _typePathDragNode = node; _typePathOriginal = node.TextPath.Clone(); _typePathHandle = handle.Kind;
            _typePathInverse = node.WorldMatrix.Inverse;
            var point = _typePathInverse.Map(world);
            _typePathInitialFraction = Renderer.ProjectTypeOnPath(node, point);
            var sample = Renderer.GetTypeOnPathSample(node, _typePathInitialFraction);
            _typePathInitialSide = SignedSide(sample.Position, sample.Tangent, point);
            _gesture = Gesture.PendingTypePath; return true;
        }
        return false;
    }

    private void MoveTypeOnPath(Vec2 world, Vec2 screen, bool preventFlip)
    {
        if (Session is not { } editor || _typePathDragNode is not { } node || _typePathOriginal is not { } original) return;
        try
        {
            if (_gesture == Gesture.PendingTypePath)
            {
                if (screen.DistanceTo(_startScreen) < 3) return;
                if (node.IsEffectivelyLocked || !ReferenceEquals(editor.Primary, node)) { ResetTypeOnPathGesture(); _gesture = Gesture.None; return; }
                editor.BeginInteraction("Move type-on-path bracket"); _gesture = Gesture.TypePath;
            }
            var point = _typePathInverse.Map(world);
            var fraction = Renderer.ProjectTypeOnPath(node, point);
            var delta = fraction - _typePathInitialFraction;
            var changed = original.Clone();
            if (_typePathHandle == 3 && changed.SvgPosition is { } svg && original.SvgPosition is { } initial)
            {
                var length = Renderer.GetTypeOnPathStatus(node).PathLength;
                var distance = initial.Resolve(length) + delta * length * (original.Flip ? -1 : 1);
                svg.Offset = Math.Clamp(svg.FromDistance(distance, length), -1e9, 1e9);
            }
            else if (_typePathHandle == 0) changed.Start = Math.Clamp(original.Start + delta, 0, original.End);
            else if (_typePathHandle == 1) changed.End = Math.Clamp(original.End + delta, original.Start, 1);
            else
            {
                delta = Math.Clamp(delta, -original.Start, 1 - original.End);
                changed.Start = original.Start + delta; changed.End = original.End + delta;
                if (!preventFlip)
                {
                    var sample = Renderer.GetTypeOnPathSample(node, fraction);
                    var side = SignedSide(sample.Position, sample.Tangent, point);
                    changed.Flip = original.Flip ^ (_typePathInitialSide * side < 0 && Math.Abs(side) > .5);
                }
            }
            changed.Validate(); node.TextPath = changed; editor.Preview();
        }
        catch (Exception ex) { CancelGesture(); ResetTypeOnPathGesture(); StatusChanged?.Invoke(ex.Message); }
    }

    private static double SignedSide(Vec2 origin, Vec2 tangent, Vec2 point)
    {
        var d = point - origin; return -tangent.Y * d.X + tangent.X * d.Y;
    }
    private void ResetTypeOnPathGesture() { _typePathDragNode = null; _typePathOriginal = null; }

    private void DrawTypeOnPathHandles(SKCanvas canvas)
    {
        if (Session?.Primary is not { TextPath: not null } node) return;
        var handles = GetTypeOnPathHandles(); if (handles.Count == 0) return;
        using var paint = new SKPaint { IsAntialias = true, Color = new(68, 124, 238), StrokeWidth = 1, Style = SKPaintStyle.Stroke };
        canvas.Save();
        canvas.Translate((float)Session.Viewport.Pan.X, (float)Session.Viewport.Pan.Y);
        canvas.Scale((float)Session.Viewport.Zoom); canvas.Concat(SceneRenderer.Matrix(node.WorldMatrix));
        paint.StrokeWidth = (float)(1 / Session.Viewport.Zoom);
        canvas.DrawPath(Renderer.Geometry(node), paint); canvas.Restore();
        paint.StrokeWidth = 1;
        foreach (var handle in handles)
        {
            canvas.DrawLine((float)handle.BaselinePosition.X, (float)handle.BaselinePosition.Y, (float)handle.Position.X, (float)handle.Position.Y, paint);
            var box = new SKRect((float)handle.Position.X - 4, (float)handle.Position.Y - 4, (float)handle.Position.X + 4, (float)handle.Position.Y + 4);
            paint.Style = SKPaintStyle.Fill; paint.Color = SKColors.White; canvas.DrawRect(box, paint);
            paint.Style = SKPaintStyle.Stroke; paint.Color = new(68, 124, 238); canvas.DrawRect(box, paint);
        }
        if (Renderer.GetTypeOnPathStatus(node).Overflow)
        {
            var end = handles[^1].Position; paint.Color = new(229, 90, 75);
            canvas.DrawLine((float)end.X - 2, (float)end.Y, (float)end.X + 2, (float)end.Y, paint);
            canvas.DrawLine((float)end.X, (float)end.Y - 2, (float)end.X, (float)end.Y + 2, paint);
        }
    }
}
