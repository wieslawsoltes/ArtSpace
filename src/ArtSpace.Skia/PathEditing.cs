using ArtSpace.Core;
using SkiaSharp;

namespace ArtSpace.Skia;

/// <summary>Bridges Skia geometry to managed Bézier editing without making Core depend on Skia.</summary>
public static class PathEditing
{
    public static bool CanEdit(DesignNode? node) => node is { IsContainer: false, IsEffectivelyLocked: false } && (node.Kind != NodeKind.Text || node.TextPath is not null) && node.Kind != NodeKind.Slice;

    public static EditablePath Read(SKPath source, double conicTolerance = .01)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!double.IsFinite(conicTolerance) || conicTolerance <= 0) throw new ArgumentOutOfRangeException(nameof(conicTolerance));
        if (source.FillType is SKPathFillType.InverseEvenOdd or SKPathFillType.InverseWinding)
            throw new InvalidOperationException("Inverse paths cannot be represented as bounded editable artwork.");
        var result = new EditablePath { FillRule = source.FillType == SKPathFillType.EvenOdd ? PathFillRule.EvenOdd : PathFillRule.NonZero };
        EditablePath.Contour? current = null;
        using var iterator = source.CreateRawIterator();
        var points = new SKPoint[4]; var anchors = 0;
        while (true)
        {
            switch (iterator.Next(points))
            {
                case SKPathVerb.Done: return result;
                case SKPathVerb.Move:
                    current = new(); result.Contours.Add(current); Add(V(points[0])); break;
                case SKPathVerb.Line: Ensure(V(points[0])); Add(V(points[1])); break;
                case SKPathVerb.Quad:
                    Ensure(V(points[0])); Quadratic(V(points[0]), V(points[1]), V(points[2])); break;
                case SKPathVerb.Conic:
                    Ensure(V(points[0]));
                    var weight = iterator.ConicWeight();
                    if (!float.IsFinite(weight) || weight <= 0) throw new InvalidDataException("Non-positive conic weights cannot be edited.");
                    ApproximateConic(V(points[0]), V(points[1]), V(points[2]), weight, 0, 1, 0); break;
                case SKPathVerb.Cubic:
                    Ensure(V(points[0])); Cubic(V(points[1]), V(points[2]), V(points[3])); break;
                case SKPathVerb.Close:
                    if (current is null || current.Points.Count == 0) break;
                    if (current.Points.Count > 1 && current.Points[0].Position.DistanceTo(current.Points[^1].Position) < 1e-7)
                    {
                        current.Points[0].ControlIn = current.Points[^1].ControlIn;
                        current.Points.RemoveAt(current.Points.Count - 1); anchors--;
                    }
                    current.Closed = true; current = null; break;
                default: throw new InvalidDataException("Unsupported path verb.");
            }
        }
        void Ensure(Vec2 position)
        {
            if (current is not null) return;
            current = new(); result.Contours.Add(current); Add(position);
        }
        void Add(Vec2 position)
        {
            if (!position.IsFinite) throw new InvalidDataException("Invalid path coordinate.");
            if (++anchors > EditablePath.MaxAnchors) throw new InvalidOperationException("The editable path anchor limit has been reached.");
            current!.Points.Add(new() { Position = position });
        }
        void Cubic(Vec2 first, Vec2 second, Vec2 end)
        {
            if (!first.IsFinite || !second.IsFinite) throw new InvalidDataException("Invalid path control point.");
            current!.Points[^1].ControlOut = first; Add(end); current.Points[^1].ControlIn = second;
        }
        void Quadratic(Vec2 a, Vec2 b, Vec2 c) => Cubic(a + (b - a) * (2d / 3), c + (b - c) * (2d / 3), c);
        void ApproximateConic(Vec2 a, Vec2 b, Vec2 c, double weight, double from, double to, int depth)
        {
            (Vec2 Position, Vec2 Derivative) Sample(double t)
            {
                var u = 1 - t;
                var denominator = u * u + 2 * weight * u * t + t * t;
                var numerator = a * (u * u) + b * (2 * weight * u * t) + c * (t * t);
                var dn = a * (-2 * u) + b * (2 * weight * (1 - 2 * t)) + c * (2 * t);
                var dd = -2 * u + 2 * weight * (1 - 2 * t) + 2 * t;
                return (numerator / denominator, (dn * denominator - numerator * dd) / (denominator * denominator));
            }
            var first = Sample(from); var last = Sample(to); var dt = to - from;
            var p = new PathPoint { Position = first.Position, ControlOut = first.Position + first.Derivative * (dt / 3) };
            var q = new PathPoint { Position = last.Position, ControlIn = last.Position - last.Derivative * (dt / 3) };
            var error = 0d;
            for (var i = 1; i < 8; i++)
            {
                var t = i / 8d;
                error = Math.Max(error, EditablePath.Evaluate(p, q, t).DistanceTo(Sample(from + dt * t).Position));
            }
            if (error <= conicTolerance)
            {
                Cubic(p.ControlOut!.Value, q.ControlIn!.Value, q.Position); return;
            }
            if (depth >= 16) throw new InvalidOperationException("Conic editing tolerance could not be reached within the subdivision limit.");
            var middle = (from + to) / 2;
            ApproximateConic(a, b, c, weight, from, middle, depth + 1);
            ApproximateConic(a, b, c, weight, middle, to, depth + 1);
        }
    }

    public static EditablePath Read(DesignNode node, SceneRenderer renderer) => Read(renderer.Geometry(node));

    /// <summary>
    /// Writes local geometry and normalizes bounds while preserving world placement and gradient locations.
    /// basis is the pre-gesture snapshot, permitting absolute pointer updates rather than cumulative drift.
    /// </summary>
    public static void Write(DesignNode node, EditablePath geometry, DesignNode? basis = null)
    {
        using var path = SKPath.ParseSvgPathData(geometry.ToSvgPathData()) ?? throw new InvalidOperationException("Invalid edited geometry.");
        path.FillType = geometry.FillRule == PathFillRule.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
        Write(node, path, basis);
    }

    public static void Write(DesignNode node, SKPath localGeometry, DesignNode? basis = null)
    {
        ArgumentNullException.ThrowIfNull(node); ArgumentNullException.ThrowIfNull(localGeometry);
        if (localGeometry.FillType is SKPathFillType.InverseWinding or SKPathFillType.InverseEvenOdd)
            throw new InvalidOperationException("Inverse paths cannot be persisted as bounded artwork.");
        if (node.TextPath is not null)
        {
            using var validate = new MeasuredContour(localGeometry);
        }
        basis ??= node;
        var oldMatrix = basis.LocalMatrix; var oldWidth = basis.Width; var oldHeight = basis.Height;
        var bounds = localGeometry.TightBounds;
        if (!float.IsFinite(bounds.Left) || !float.IsFinite(bounds.Top) || !float.IsFinite(bounds.Right) || !float.IsFinite(bounds.Bottom))
            throw new InvalidOperationException("Edited path has invalid bounds.");
        var width = Math.Max(.001, bounds.Width); var height = Math.Max(.001, bounds.Height);
        var paints = node.Fills.Concat(node.Strokes.Select(s => s.Paint).OfType<FillStyle>()).ToArray();
        var gradients = basis.Fills.Concat(basis.Strokes.Select(s => s.Paint).OfType<FillStyle>()).Select(f => (f.Start, f.End, f.GradientSpace, f.GradientTransform, f.GradientFocus, f.GradientRadius)).ToArray();
        var oldBounds = basis.LocalBounds;
        // Text paint coordinates use its local layout box, not the baseline's tight bounds.
        // Match SceneRenderer.GradientBounds when outlining text or editing a path-text baseline.
        if (basis.Kind != NodeKind.Text && gradients.Any(f => f.GradientSpace == GradientSpace.ObjectBoundingBox))
        {
            using var oldPath = SKPath.ParseSvgPathData(VectorPath.Build(basis)) ?? new SKPath();
            if (basis.Kind == NodeKind.Path && basis.PathWidth > 0 && basis.PathHeight > 0)
                oldPath.Transform(SKMatrix.CreateScale((float)(oldWidth / basis.PathWidth), (float)(oldHeight / basis.PathHeight)));
            var old = oldPath.TightBounds; oldBounds = new(old.Left, old.Top, old.Width, old.Height);
        }
        using var normalized = new SKPath(localGeometry);
        normalized.Transform(SKMatrix.CreateTranslation(-bounds.Left, -bounds.Top));
        node.FillRule = localGeometry.FillType == SKPathFillType.EvenOdd ? PathFillRule.EvenOdd : PathFillRule.NonZero;
        node.Kind = node.TextPath is null ? NodeKind.Path : NodeKind.Text; node.PathData = normalized.ToSvgPathData(); node.Points = []; node.Closed = false;
        node.Width = node.PathWidth = width; node.Height = node.PathHeight = height;
        if (node.TextPath is null) NodeGeometry.SetLocalMatrix(node, Matrix2D.Translation(bounds.Left, bounds.Top) * oldMatrix);
        else NodeGeometry.SetExactMatrix(node, Matrix2D.Translation(bounds.Left, bounds.Top) * oldMatrix);
        for (var i = 0; i < Math.Min(paints.Length, gradients.Length); i++)
        {
            if (paints[i].Kind == FillKind.Solid) continue;
            var old = gradients[i];
            var fill = paints[i];
            if (old.GradientSpace == GradientSpace.Legacy && old.GradientTransform == Matrix2D.Identity)
            {
                fill.Start = new((old.Start.X * oldWidth - bounds.Left) / width, (old.Start.Y * oldHeight - bounds.Top) / height);
                fill.End = new((old.End.X * oldWidth - bounds.Left) / width, (old.End.Y * oldHeight - bounds.Top) / height);
            }
            else
            {
                fill.Start = old.Start; fill.End = old.End; fill.GradientFocus = old.GradientFocus; fill.GradientRadius = old.GradientRadius;
                var transform = old.GradientTransform;
                if (old.GradientSpace == GradientSpace.ObjectBoundingBox)
                    transform *= Matrix2D.Scale(Math.Max(.000001, oldBounds.Width), Math.Max(.000001, oldBounds.Height)) * Matrix2D.Translation(oldBounds.X, oldBounds.Y);
                else if (old.GradientSpace == GradientSpace.Legacy)
                {
                    fill.Start = new(old.Start.X * oldWidth, old.Start.Y * oldHeight);
                    fill.End = new(old.End.X * oldWidth, old.End.Y * oldHeight);
                    fill.GradientFocus = fill.Start; fill.GradientRadius = Math.Max(1, fill.Start.DistanceTo(fill.End));
                }
                fill.GradientSpace = GradientSpace.UserSpaceOnUse;
                fill.GradientTransform = transform * Matrix2D.Translation(-bounds.Left, -bounds.Top) * Matrix2D.Scale(node.Width / width, node.Height / height);
            }
        }
    }
    private static Vec2 V(SKPoint point) => new(point.X, point.Y);
}
