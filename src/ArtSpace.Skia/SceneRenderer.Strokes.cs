using ArtSpace.Core;
using SkiaSharp;

namespace ArtSpace.Skia;

public sealed partial class SceneRenderer
{
    private readonly record struct WidthState(double Position, double Left, double Right);
    private sealed class StrokeGeometryEntry : IDisposable
    {
        public required GeometrySnapshot Geometry;
        public required double Width, Miter, Phase;
        public required StrokeCap Cap;
        public required StrokeJoin Join;
        public required double[] Dashes;
        public required WidthState[] Profile;
        public required SKPath Path;
        public SKPath? PickBorder;
        public double PickTolerance;
        public Matrix2D PickMatrix;
        public LinkedListNode<(string, StrokeStyle)>? Link;
        public void Dispose() { PickBorder?.Dispose(); Path.Dispose(); }
        public bool Matches(DesignNode node, StrokeStyle stroke)
        {
            if (!Geometry.Matches(node) || Width != stroke.Width || Miter != stroke.MiterLimit || Phase != stroke.DashOffset
                || Cap != stroke.Cap || Join != stroke.Join || Dashes.Length != stroke.Dashes.Count || Profile.Length != stroke.WidthProfile.Count) return false;
            for (var i = 0; i < Dashes.Length; i++) if (Dashes[i] != stroke.Dashes[i]) return false;
            for (var i = 0; i < Profile.Length; i++)
            {
                var p = stroke.WidthProfile[i]; if (Profile[i] != new WidthState(p.Position, p.Left, p.Right)) return false;
            }
            return true;
        }
    }
    private sealed record CenterlineEntry(GeometrySnapshot Geometry, VariableStrokeGeometry.Contour[] Contours, int SampleCount);
    private readonly Dictionary<(string, StrokeStyle), StrokeGeometryEntry> _strokeGeometry = [];
    private readonly LinkedList<(string, StrokeStyle)> _strokeGeometryLru = [];
    private readonly Dictionary<string, CenterlineEntry> _strokeCenterlines = [];
    private int _strokeGeometryPoints, _strokePickPoints, _strokeCenterlineSamples;
    public int CachedStrokeCenterlineSamples => _strokeCenterlineSamples;
    public int CachedStrokePickPoints => _strokePickPoints;
    public long StrokeOutlineBuilds { get; private set; }
    public long StrokeOutlineHits { get; private set; }
    public long StrokeCenterlineBuilds { get; private set; }
    public long StrokePickBuilds { get; private set; }
    public int CachedStrokeOutlineCount => _strokeGeometry.Count;
    public int CachedStrokeOutlinePoints => _strokeGeometryPoints;

    /// <summary>Borrowed immutable local coverage. Do not mutate or dispose; invalidated by eviction, pruning or ClearCache.</summary>
    public SKPath StrokeOutline(DesignNode node, StrokeStyle stroke)
    {
        ArgumentNullException.ThrowIfNull(node); ArgumentNullException.ThrowIfNull(stroke);
        var key = (node.Id, stroke);
        if (_strokeGeometry.TryGetValue(key, out var cached) && cached.Matches(node, stroke))
        {
            StrokeOutlineHits++; _strokeGeometryLru.Remove(cached.Link!); _strokeGeometryLru.AddLast(cached.Link!); return cached.Path;
        }
        SKPath path;
        if (stroke.WidthProfile.Count != 0)
        {
            if (node.Kind == NodeKind.Text) throw new InvalidOperationException("Create text outlines before applying a variable-width profile.");
            path = VariableStrokeGeometry.Build(StrokeCenterlines(node), stroke);
        }
        else
        {
            using var paint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = (float)stroke.Width,
                StrokeCap = (SKStrokeCap)stroke.Cap, StrokeJoin = (SKStrokeJoin)stroke.Join, StrokeMiter = (float)stroke.MiterLimit };
            using var dash = CreateStrokeDash(stroke); paint.PathEffect = dash;
            path = paint.GetFillPath(Geometry(node)) ?? throw new InvalidOperationException("Cannot expand the stroke coverage.");
        }
        try
        {
            if (cached is not null) RemoveStrokeGeometry(key, cached);
            while (_strokeGeometryLru.First is { } first && (_strokeGeometry.Count >= 4096 || _strokeGeometryPoints + path.PointCount > 1_000_000))
                RemoveStrokeGeometry(first.Value, _strokeGeometry[first.Value]);
            var entry = new StrokeGeometryEntry
            {
                Geometry = new(node), Width = stroke.Width, Miter = stroke.MiterLimit, Phase = stroke.DashOffset,
                Cap = stroke.Cap, Join = stroke.Join, Dashes = [.. stroke.Dashes],
                Profile = stroke.WidthProfile.Select(p => new WidthState(p.Position, p.Left, p.Right)).ToArray(), Path = path
            };
            entry.Link = _strokeGeometryLru.AddLast(key); _strokeGeometry.Add(key, entry);
            _strokeGeometryPoints += path.PointCount; StrokeOutlineBuilds++; return path;
        }
        catch { path.Dispose(); throw; }
    }

    /// <summary>Read-only cached adaptive samples. Profile or appearance edits do not rebuild the centerline.</summary>
    public IReadOnlyList<VariableStrokeGeometry.Contour> StrokeCenterlines(DesignNode node)
    {
        if (_strokeCenterlines.TryGetValue(node.Id, out var entry) && entry.Geometry.Matches(node)) return entry.Contours;
        var contours = VariableStrokeGeometry.Flatten(Geometry(node));
        RemoveCenterline(node.Id);
        var count = contours.Sum(c => c.Samples.Length);
        while (_strokeCenterlines.Count > 0 && (_strokeCenterlines.Count >= 256 || _strokeCenterlineSamples + count > 250_000))
            RemoveCenterline(_strokeCenterlines.Keys.First());
        _strokeCenterlines[node.Id] = new(new(node), contours, count); _strokeCenterlineSamples += count;
        StrokeCenterlineBuilds++; return contours;
    }

    private bool StrokeContains(DesignNode node, StrokeStyle stroke, Vec2 local, Vec2 world, Matrix2D worldMatrix, double tolerance, double localTolerance)
    {
        var path = StrokeOutline(node, stroke); var bounds = path.TightBounds;
        bounds.Inflate((float)localTolerance, (float)localTolerance);
        if (!bounds.Contains((float)local.X, (float)local.Y)) return false;
        if (path.Contains((float)local.X, (float)local.Y)) return true;
        if (tolerance <= 0) return false;
        var entry = _strokeGeometry[(node.Id, stroke)];
        if (entry.PickBorder is null || entry.PickTolerance != tolerance || entry.PickMatrix != worldMatrix)
        {
            // Offset the transformed coverage, not a locally inflated radius: shear/nonuniform
            // scale must not turn a four-pixel target into a forty-pixel hit zone.
            using var transformed = new SKPath(path); transformed.Transform(Matrix(worldMatrix));
            using var paint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = (float)(tolerance * 2), StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
            var border = paint.GetFillPath(transformed) ?? throw new InvalidOperationException("Cannot create stroke picking coverage.");
            ReleasePickBorder(entry);
            StrokePickBuilds++;
            if (border.PointCount > 1_000_000)
            {
                using (border) return border.Contains((float)world.X, (float)world.Y);
            }
            foreach (var key in _strokeGeometryLru)
            {
                if (_strokePickPoints + border.PointCount <= 1_000_000) break;
                ReleasePickBorder(_strokeGeometry[key]);
            }
            entry.PickBorder = border; entry.PickTolerance = tolerance; entry.PickMatrix = worldMatrix;
            _strokePickPoints += border.PointCount;
        }
        return entry.PickBorder.Contains((float)world.X, (float)world.Y);
    }

    private void ReleasePickBorder(StrokeGeometryEntry entry)
    {
        if (entry.PickBorder is not { } border) return;
        _strokePickPoints -= border.PointCount; border.Dispose(); entry.PickBorder = null;
    }
    private void RemoveCenterline(string id)
    {
        if (_strokeCenterlines.Remove(id, out var entry)) _strokeCenterlineSamples -= entry.SampleCount;
    }

    private static bool Paintable(StrokeStyle stroke) => stroke.Visible && stroke.Width > 0 && stroke.Opacity > 0
        && stroke.Paint is not { Visible: false } && stroke.Paint is not { Opacity: <= 0 };

    /// <summary>Clipping-aware picking of actual painted stroke coverage; tolerance is measured in world units.</summary>
    public DesignNode? HitTest(IEnumerable<DesignNode> roots, Vec2 point, bool deep = false, double tolerance = 4)
    {
        if (!point.IsFinite || !double.IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        var nodes = roots as IList<DesignNode> ?? roots.ToArray();
        for (var i = nodes.Count - 1; i >= 0; i--)
            if (HitNode(nodes[i], point, deep, tolerance) is { } hit) return hit;
        return null;
    }

    private DesignNode? HitNode(DesignNode node, Vec2 point, bool deep, double tolerance)
    {
        if (!node.IsEffectivelyVisible || node.Opacity <= 0 || node.IsEffectivelyLocked || node.Kind == NodeKind.Slice) return null;
        var matrix = node.WorldMatrix;
        if (!matrix.TryInvert(out var inverse)) return null;
        var local = inverse.Map(point); var inside = node.LocalBounds.Contains(local);
        // Largest singular value of the inverse, conservatively converting a world-space hit radius.
        var trace = inverse.M11 * inverse.M11 + inverse.M12 * inverse.M12 + inverse.M21 * inverse.M21 + inverse.M22 * inverse.M22;
        var determinant = inverse.M11 * inverse.M22 - inverse.M12 * inverse.M21;
        var localTolerance = tolerance * Math.Sqrt(Math.Max(0, (trace + Math.Sqrt(Math.Max(0, trace * trace - 4 * determinant * determinant))) / 2));
        if (!Outlines && node.OpacityMaskEnabled && node.OpacityMask is not null && MaskCoverageAt(node, local) <= 1d / 255) return null;
        if (node.ClippingPath is { } mask)
        {
            var p = mask.LocalMatrix.Inverse.Map(local);
            if (!Geometry(mask).Contains((float)p.X, (float)p.Y)) return null;
        }
        if (!node.ClipContent || Geometry(node).Contains((float)local.X, (float)local.Y))
        {
            for (var i = node.Children.Count - 1; i >= 0; i--)
            {
                var child = node.Children[i]; if (child.Id == node.ClipPathId || child.Id == node.OpacityMaskId) continue;
                if (HitNode(child, point, deep, tolerance) is { } hit)
                    return deep || node.Kind is NodeKind.Frame or NodeKind.Section ? hit : node;
            }
        }
        if (node.Kind == NodeKind.Text && inside) return node;
        if (node.Kind == NodeKind.Group && node.Children.Count > 0) return null;
        if (node.Kind == NodeKind.Frame && inside && node.Fills.Count > 0) return node;
        var path = Geometry(node); var bounds = path.TightBounds; var outset = localTolerance;
        foreach (var stroke in node.Strokes)
            if (Paintable(stroke)) outset = Math.Max(outset, stroke.Width * StrokeProfiles.MaximumSide(stroke.WidthProfile) * Math.Max(2, stroke.MiterLimit) + localTolerance);
        bounds.Inflate((float)outset + 1, (float)outset + 1);
        if (!bounds.Contains((float)local.X, (float)local.Y)) return null;
        foreach (var fill in node.Fills)
            if (fill.Visible && path.Contains((float)local.X, (float)local.Y)) return node;
        foreach (var stroke in node.Strokes)
            if (Paintable(stroke) && StrokeContains(node, stroke, local, point, matrix, tolerance, localTolerance)) return node;
        return null;
    }

    private void RemoveStrokeGeometry((string, StrokeStyle) key, StrokeGeometryEntry entry)
    {
        ReleasePickBorder(entry);
        _strokeGeometryPoints -= entry.Path.PointCount; _strokeGeometryLru.Remove(entry.Link!); _strokeGeometry.Remove(key); entry.Dispose();
    }
    private void PruneStrokeGeometry(DesignNode[] nodes)
    {
        var keys = nodes.SelectMany(n => n.Strokes.Select(s => (n.Id, s))).ToHashSet();
        foreach (var pair in _strokeGeometry.ToArray()) if (!keys.Contains(pair.Key)) RemoveStrokeGeometry(pair.Key, pair.Value);
        var ids = nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _strokeCenterlines.Keys.ToArray()) if (!ids.Contains(id)) RemoveCenterline(id);
    }
    private void ClearStrokeGeometry()
    {
        foreach (var value in _strokeGeometry.Values) value.Dispose();
        _strokeGeometry.Clear(); _strokeGeometryLru.Clear(); _strokeCenterlines.Clear(); _strokeGeometryPoints = _strokePickPoints = _strokeCenterlineSamples = 0;
    }
}
