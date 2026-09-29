using ArtSpace.Core;
using ArtSpace.Documents;
using ArtSpace.Editing;
using ArtSpace.Skia;
using SkiaSharp;

namespace ArtSpace.Illustration;

/// <summary>Illustration operations independent of Uno and the workbench. Every public edit is one undo step.</summary>
public static class IllustrationOperations
{
    private static DesignNode[] Vectors(EditorSession editor)
    {
        var nodes = editor.SelectionRoots.Where(n => !n.IsContainer && !n.IsEffectivelyLocked && n.Kind != NodeKind.Text).ToArray();
        if (nodes.Length == 0) throw new InvalidOperationException("Select an unlocked vector object.");
        return nodes;
    }

    public static void ExpandShapes(EditorSession editor, SceneRenderer renderer)
    {
        var nodes = Vectors(editor);
        editor.Edit("Expand shapes", () =>
        {
            foreach (var node in nodes)
            {
                using var path = new SKPath(renderer.Geometry(node));
                node.Kind = NodeKind.Path;
                node.PathData = path.ToSvgPathData();
                node.PathWidth = node.Width;
                node.PathHeight = node.Height;
                node.Points.Clear();
                node.Name = "Expanded " + node.Name;
            }
        });
    }

    /// <summary>Expands visible stroke appearances, including dash pattern, into actual filled vector geometry.</summary>
    public static void OutlineStrokes(EditorSession editor, SceneRenderer renderer)
    {
        var nodes = Vectors(editor);
        if (!nodes.Any(n => n.Strokes.Any(s => s.Visible && s.Width > 0 && s.Paint?.Visible != false)))
            throw new InvalidOperationException("The selection has no visible strokes.");
        if (nodes.Any(n => n.Parent?.ClipPathId == n.Id)) throw new InvalidOperationException("Release a clipping path before expanding its stroke into a group.");
        editor.Edit("Outline stroke", () =>
        {
            var selected = new List<string>();
            foreach (var node in nodes)
            {
                var strokes = node.Strokes.Where(s => s.Visible && s.Width > 0 && s.Paint?.Visible != false).ToArray();
                if (strokes.Length == 0) { selected.Add(node.Id); continue; }
                var parent = node.Parent;
                var siblings = parent?.Children ?? editor.Page.Nodes;
                var index = siblings.IndexOf(node);
                var group = new DesignNode
                {
                    Id = node.Id, SourceId = node.SourceId, Kind = NodeKind.Group, Name = node.Name + " expanded", X = node.X, Y = node.Y,
                    Width = node.Width, Height = node.Height, Rotation = node.Rotation,
                    FlipX = node.FlipX, FlipY = node.FlipY, Opacity = node.Opacity, Blend = node.Blend,
                    Fills = [], Shadows = node.Shadows, Effects = node.Effects, AffineTransform = node.AffineTransform
                };
                if (node.Fills.Any(f => f.Visible))
                {
                    var fill = DocumentJson.CloneNode(node, true);
                    fill.X = fill.Y = fill.Rotation = 0; fill.FlipX = fill.FlipY = false;
                    fill.Opacity = 1; fill.Blend = BlendKind.Normal; fill.Shadows = []; fill.Effects = []; fill.AffineTransform = null; fill.Strokes = [];
                    group.Add(fill);
                }
                foreach (var stroke in strokes)
                {
                    using var path = new SKPath(renderer.StrokeOutline(node, stroke));
                    var outlined = PathNode(path, stroke.Color, "Stroke outline");
                    outlined.Opacity = stroke.Opacity;
                    if (stroke.Paint is { } sourcePaint)
                    {
                        var fill = GraphicStyle.CloneFill(sourcePaint);
                        if (fill.Kind != FillKind.Solid)
                        {
                            if (fill.GradientSpace == GradientSpace.Legacy)
                            {
                                fill.Start = new(fill.Start.X * node.Width, fill.Start.Y * node.Height);
                                fill.End = new(fill.End.X * node.Width, fill.End.Y * node.Height);
                                fill.GradientFocus = fill.Start; fill.GradientRadius = Math.Max(1, fill.Start.DistanceTo(fill.End));
                            }
                            else fill.GradientTransform = renderer.GradientCoordinateMatrix(node, sourcePaint);
                            fill.GradientSpace = GradientSpace.UserSpaceOnUse;
                            fill.GradientTransform *= Matrix2D.Translation(-outlined.X, -outlined.Y);
                        }
                        outlined.Fills = [fill];
                    }
                    group.Add(outlined);
                }
                var maskOwner = parent?.OpacityMaskId == node.Id ? parent : null;
                editor.RemoveNode(node); editor.AddNode(group, parent);
                if (maskOwner is not null) maskOwner.OpacityMaskId = group.Id;
                siblings.Remove(group); siblings.Insert(Math.Max(0, index), group);
                selected.Add(group.Id);
            }
            editor.Select(selected);
        });
    }

    public static SKPaint StrokePaint(StrokeStyle stroke)
    {
        var paint = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = (float)stroke.Width,
            StrokeCap = (SKStrokeCap)stroke.Cap, StrokeJoin = (SKStrokeJoin)stroke.Join,
            StrokeMiter = (float)stroke.MiterLimit
        };
        using var dash = SceneRenderer.CreateStrokeDash(stroke);
        paint.PathEffect = dash;
        return paint;
    }

    /// <summary>Creates a vector dilation or erosion. Positive distance grows; negative distance insets.</summary>
    public static void OffsetPaths(EditorSession editor, SceneRenderer renderer, double distance)
    {
        if (!double.IsFinite(distance) || Math.Abs(distance) > 10000 || Math.Abs(distance) < .001)
            throw new ArgumentOutOfRangeException(nameof(distance), "Offset must be between -10000 and 10000, excluding zero.");
        var nodes = Vectors(editor);
        editor.Edit("Offset path", () =>
        {
            var selected = new List<string>();
            foreach (var node in nodes)
            {
                using var path = new SKPath(renderer.Geometry(node));
                using var paint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = (float)Math.Abs(distance * 2), StrokeJoin = SKStrokeJoin.Miter, StrokeMiter = 4 };
                using var border = paint.GetFillPath(path) ?? throw new InvalidOperationException("Cannot offset this path.");
                using var result = path.Op(border, distance > 0 ? SKPathOp.Union : SKPathOp.Difference)
                    ?? throw new InvalidOperationException("Cannot offset this path.");
                if (result.IsEmpty) continue;
                var output = DocumentJson.CloneNode(node, true);
                output.Kind = NodeKind.Path; output.Name = node.Name + " offset";
                var bounds = result.TightBounds;
                using var normalized = new SKPath(result);
                normalized.Transform(SKMatrix.CreateTranslation(-bounds.Left, -bounds.Top));
                output.Width = output.PathWidth = Math.Max(.001, bounds.Width);
                output.Height = output.PathHeight = Math.Max(.001, bounds.Height);
                output.PathData = normalized.ToSvgPathData();
                output.FillRule = normalized.FillType == SKPathFillType.EvenOdd ? PathFillRule.EvenOdd : PathFillRule.NonZero;
                NodeGeometry.SetLocalMatrix(output, Matrix2D.Translation(bounds.Left, bounds.Top) * node.LocalMatrix);
                output.Points.Clear();
                editor.AddNode(output, node.Parent); selected.Add(output.Id);
            }
            editor.Select(selected);
        });
    }

    /// <summary>Creates bounded, editable intermediate objects. Geometry morphing is limited to matching point topology.</summary>
    public static void Blend(EditorSession editor, int steps)
    {
        if (steps is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(steps));
        var nodes = Vectors(editor);
        if (nodes.Length != 2 || nodes[0].Parent != nodes[1].Parent || nodes[0].Kind != nodes[1].Kind)
            throw new InvalidOperationException("Select two same-kind vector objects in the same layer.");
        var a = nodes[0]; var b = nodes[1];
        if (a.Kind == NodeKind.Path && (a.Points.Count == 0 || a.Points.Count != b.Points.Count))
            throw new InvalidOperationException("Path blends need matching editable anchor counts.");
        editor.Edit("Make blend", () =>
        {
            var ids = new List<string> { a.Id, b.Id };
            for (var i = 1; i <= steps; i++)
            {
                var t = i / (steps + 1d); var n = DocumentJson.CloneNode(a, true);
                n.Name = $"Blend {i}"; n.X = Lerp(a.X, b.X, t); n.Y = Lerp(a.Y, b.Y, t);
                n.Width = Lerp(a.Width, b.Width, t); n.Height = Lerp(a.Height, b.Height, t);
                n.Rotation = Lerp(a.Rotation, b.Rotation, t); n.Opacity = Lerp(a.Opacity, b.Opacity, t);
                n.Fill = MixColor(a.Fill, b.Fill, t);
                if (a.Kind == NodeKind.Path)
                {
                    // Normalize both source coordinate systems before interpolation.
                    n.PathWidth = n.Width; n.PathHeight = n.Height;
                    for (var j = 0; j < n.Points.Count; j++)
                    {
                        var ap = a.Points[j]; var bp = b.Points[j];
                        Vec2 Scale(DesignNode node, Vec2 p) => new(p.X * node.Width / Math.Max(.001, node.PathWidth == 0 ? node.Width : node.PathWidth), p.Y * node.Height / Math.Max(.001, node.PathHeight == 0 ? node.Height : node.PathHeight));
                        n.Points[j].Position = Mix(Scale(a, ap.Position), Scale(b, bp.Position), t);
                        n.Points[j].ControlIn = ap.ControlIn.HasValue && bp.ControlIn.HasValue ? Mix(Scale(a, ap.ControlIn.Value), Scale(b, bp.ControlIn.Value), t) : null;
                        n.Points[j].ControlOut = ap.ControlOut.HasValue && bp.ControlOut.HasValue ? Mix(Scale(a, ap.ControlOut.Value), Scale(b, bp.ControlOut.Value), t) : null;
                    }
                }
                editor.AddNode(n, a.Parent); ids.Add(n.Id);
            }
            editor.Select(ids);
        });
    }

    public static void RadialRepeat(EditorSession editor, int copies)
    {
        if (copies is < 2 or > 128) throw new ArgumentOutOfRangeException(nameof(copies));
        var roots = editor.SelectionRoots.Where(n => !n.IsEffectivelyLocked).ToArray();
        if (roots.Length == 0) throw new InvalidOperationException("Select an object to repeat.");
        var bounds = editor.SelectionBounds(); var center = new Vec2(bounds.Center.X, bounds.Bottom + bounds.Height * .3);
        editor.Edit("Radial repeat", () =>
        {
            var ids = roots.Select(n => n.Id).ToList();
            for (var i = 1; i < copies; i++)
            {
                var angle = i * Math.PI * 2 / copies;
                foreach (var node in roots)
                {
                    var clone = DocumentJson.CloneNode(node, true); var parentInverse = node.Parent?.WorldMatrix.Inverse ?? Matrix2D.Identity;
                    var rotation = Matrix2D.Translation(-center.X, -center.Y) * Matrix2D.Rotation(angle * 180 / Math.PI) * Matrix2D.Translation(center.X, center.Y);
                    NodeGeometry.SetLocalMatrix(clone, node.WorldMatrix * rotation * parentInverse);
                    clone.Name = node.Name + " repeat " + i; editor.AddNode(clone, node.Parent); ids.Add(clone.Id);
                }
            }
            editor.Select(ids);
        });
    }

    public static void ReversePaths(EditorSession editor)
    {
        var nodes = Vectors(editor).Where(n => n.Points.Count > 1).ToArray();
        if (nodes.Length == 0) throw new InvalidOperationException("Select an editable pen or pencil path.");
        editor.Edit("Reverse path direction", () =>
        {
            foreach (var node in nodes)
            {
                node.Points.Reverse();
                foreach (var p in node.Points) (p.ControlIn, p.ControlOut) = (p.ControlOut, p.ControlIn);
            }
        });
    }

    /// <summary>Splits every cubic segment using de Casteljau; the curve does not change.</summary>
    public static void AddAnchors(EditorSession editor)
    {
        var nodes = Vectors(editor).Where(n => n.Points.Count > 1).ToArray();
        if (nodes.Length == 0) throw new InvalidOperationException("Select an editable pen or pencil path.");
        if (nodes.Any(n => n.Points.Count > 8192)) throw new InvalidOperationException("Anchor limit reached.");
        editor.Edit("Add anchor points", () =>
        {
            foreach (var node in nodes)
            {
                var source = node.Points.ToArray(); var result = new List<PathPoint>();
                for (var i = 0; i < source.Length; i++)
                {
                    var a = source[i]; result.Add(a);
                    if (!node.Closed && i == source.Length - 1) break;
                    var b = source[(i + 1) % source.Length];
                    var p = a.Position; var q = a.ControlOut ?? p; var r = b.ControlIn ?? b.Position; var s = b.Position;
                    var pq = Mix(p, q, .5); var qr = Mix(q, r, .5); var rs = Mix(r, s, .5);
                    var left = Mix(pq, qr, .5); var right = Mix(qr, rs, .5);
                    a.ControlOut = pq; b.ControlIn = rs;
                    result.Add(new() { Position = Mix(left, right, .5), ControlIn = left, ControlOut = right });
                }
                node.Points = result;
            }
        });
    }

    public static void SmoothAnchors(EditorSession editor, bool smooth)
    {
        var nodes = Vectors(editor).Where(n => n.Points.Count > 1).ToArray();
        if (nodes.Length == 0) throw new InvalidOperationException("Select an editable pen or pencil path.");
        editor.Edit(smooth ? "Smooth anchors" : "Corner anchors", () =>
        {
            foreach (var node in nodes)
                for (var i = 0; i < node.Points.Count; i++)
                {
                    var point = node.Points[i];
                    if (!smooth) { point.ControlIn = point.ControlOut = null; continue; }
                    var previous = node.Points[Math.Max(0, i - 1)].Position;
                    var next = node.Points[Math.Min(node.Points.Count - 1, i + 1)].Position;
                    if (node.Closed) { previous = node.Points[(i + node.Points.Count - 1) % node.Points.Count].Position; next = node.Points[(i + 1) % node.Points.Count].Position; }
                    var tangent = (next - previous) / 6;
                    point.ControlIn = point.Position - tangent; point.ControlOut = point.Position + tangent;
                }
        });
    }

    private static DesignNode PathNode(SKPath path, string fill, string name)
    {
        var bounds = path.TightBounds;
        using var normalized = new SKPath(path); normalized.Transform(SKMatrix.CreateTranslation(-bounds.Left, -bounds.Top));
        return new() { Kind = NodeKind.Path, Name = name, X = bounds.Left, Y = bounds.Top, Width = Math.Max(.001, bounds.Width), Height = Math.Max(.001, bounds.Height), PathWidth = Math.Max(.001, bounds.Width), PathHeight = Math.Max(.001, bounds.Height), PathData = normalized.ToSvgPathData(), FillRule = normalized.FillType == SKPathFillType.EvenOdd ? PathFillRule.EvenOdd : PathFillRule.NonZero, Fill = fill };
    }
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    private static Vec2 Mix(Vec2 a, Vec2 b, double t) => a + (b - a) * t;
    private static string MixColor(string a, string b, double t)
    {
        var x = SceneRenderer.Color(a); var y = SceneRenderer.Color(b);
        return $"#{(byte)Lerp(x.Red,y.Red,t):X2}{(byte)Lerp(x.Green,y.Green,t):X2}{(byte)Lerp(x.Blue,y.Blue,t):X2}";
    }
}
