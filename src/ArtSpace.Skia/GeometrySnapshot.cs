using ArtSpace.Core;

namespace ArtSpace.Skia;

/// <summary>Exact geometry-only identity. No path serialization or hash collisions on cache hits.</summary>
internal sealed class GeometrySnapshot
{
    private readonly NodeKind _kind;
    private readonly PathFillRule _fillRule;
    private readonly double _width, _height, _pathWidth, _pathHeight, _radius, _ratio;
    private readonly int _sides;
    private readonly bool _closed;
    private readonly string? _data;
    private readonly Matrix2D _baselineTransform;
    private readonly Point[] _points;
    private readonly record struct Point(Vec2 Position, Vec2? In, Vec2? Out);

    public GeometrySnapshot(DesignNode node)
    {
        _baselineTransform = node.TextPath?.SvgPosition?.PathTransform ?? Matrix2D.Identity;
        _kind = node.Kind; _fillRule = node.FillRule; _width = node.Width; _height = node.Height;
        _pathWidth = node.PathWidth; _pathHeight = node.PathHeight; _radius = node.CornerRadius;
        _ratio = node.StarRatio; _sides = node.Sides; _closed = node.Closed; _data = node.PathData;
        _points = !string.IsNullOrWhiteSpace(_data) || node.Kind != NodeKind.Path ? []
            : node.Points.Select(p => new Point(p.Position, p.ControlIn, p.ControlOut)).ToArray();
    }

    public bool Matches(DesignNode n)
    {
        if (_baselineTransform != (n.TextPath?.SvgPosition?.PathTransform ?? Matrix2D.Identity) || _kind != n.Kind || _fillRule != n.FillRule || _width != n.Width || _height != n.Height ||
            _pathWidth != n.PathWidth || _pathHeight != n.PathHeight || _radius != n.CornerRadius ||
            _ratio != n.StarRatio || _sides != n.Sides || _closed != n.Closed || _data != n.PathData) return false;
        if (!string.IsNullOrWhiteSpace(_data) || n.Kind != NodeKind.Path) return true;
        if (_points.Length != n.Points.Count) return false;
        for (var i = 0; i < _points.Length; i++)
        {
            var p = n.Points[i]; var old = _points[i];
            if (old.Position != p.Position || old.In != p.ControlIn || old.Out != p.ControlOut) return false;
        }
        return true;
    }
}
