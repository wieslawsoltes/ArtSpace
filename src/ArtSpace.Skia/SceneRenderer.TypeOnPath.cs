using ArtSpace.Core;
using SkiaSharp;

namespace ArtSpace.Skia;

public sealed partial class SceneRenderer
{
    private readonly record struct PathTextKey(string Text, string Family, int Weight, double Size, double Tracking,
        TextAlignment TextAlignment, double Start, double End, bool Flip, double Shift, PathTextAlignment Alignment);
    private sealed record CachedBaseline(GeometrySnapshot Snapshot, MeasuredContour Measure);
    private sealed record CachedPathText(GeometrySnapshot Snapshot, PathTextKey Key, PathTextLayout Layout, long Bytes);
    private readonly Dictionary<string, CachedBaseline> _textBaselines = [];
    private readonly Dictionary<string, CachedPathText> _pathTextLayouts = [];
    private long _pathTextBytes;
    public long PathTextLayoutBuilds { get; private set; }
    public long TextBaselineBuilds { get; private set; }
    public int CachedPathTextCount => _pathTextLayouts.Count;
    public long ApproximatePathTextBytes => _pathTextBytes;

    private MeasuredContour TextBaseline(DesignNode node)
    {
        if (node.Kind != NodeKind.Text || node.TextPath is null)
            throw new ArgumentException("Select a type-on-path object.", nameof(node));
        if (_textBaselines.TryGetValue(node.Id, out var entry) && entry.Snapshot.Matches(node)) return entry.Measure;
        var measure = new MeasuredContour(Geometry(node));
        entry?.Measure.Dispose();
        if (_textBaselines.Count >= 128)
        {
            foreach (var baseline in _textBaselines.Values) baseline.Measure.Dispose();
            _textBaselines.Clear();
        }
        _textBaselines[node.Id] = new(new GeometrySnapshot(node), measure); TextBaselineBuilds++;
        return measure;
    }

    private PathTextLayout TypeOnPathLayout(DesignNode node)
    {
        var options = node.TextPath ?? throw new ArgumentException("The object does not contain path text.", nameof(node));
        var key = new PathTextKey(node.Text, node.FontFamily, node.FontWeight, node.FontSize, node.LetterSpacing,
            node.TextAlign, options.Start, options.End, options.Flip, options.BaselineShift, options.Alignment);
        if (_pathTextLayouts.TryGetValue(node.Id, out var entry) && entry.Key == key && entry.Snapshot.Matches(node))
            return entry.Layout;
        PathTextLayout result;
        try
        {
            using var font = CreateTextFont(node);
            result = PathTextLayout.Build(TextBaseline(node), node.Text, font, options, node.LetterSpacing, node.TextAlign);
        }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or ArgumentException)
        {
            // Native documents remain inspectable when a baseline is malformed. Cache this diagnostic,
            // rather than repeatedly throwing from the Uno rendering callback. Exports reject it explicitly.
            result = PathTextLayout.Invalid(error.Message);
        }
        var bytes = result.Outline.PointCount * 16L + result.Glyphs.Count * 72L;
        if (entry is not null) { entry.Layout.Dispose(); _pathTextBytes -= entry.Bytes; _pathTextLayouts.Remove(node.Id); }
        // Approximate CPU geometry accounting, not a GPU/native allocation ceiling.
        if (_pathTextLayouts.Count >= 128 || _pathTextBytes + bytes > 32 * 1024 * 1024) ClearPathTextLayouts();
        _pathTextLayouts[node.Id] = new(new GeometrySnapshot(node), key, result, bytes);
        _pathTextBytes += bytes; PathTextLayoutBuilds++;
        return result;
    }

    public PathTextStatus GetTypeOnPathStatus(DesignNode node) => TypeOnPathLayout(node).Status;
    public IReadOnlyList<PathTextGlyph> GetTypeOnPathGlyphs(DesignNode node) => TypeOnPathLayout(node).Glyphs;
    public (Vec2 Position, Vec2 Tangent) GetTypeOnPathSample(DesignNode node, double fraction)
    {
        if (!double.IsFinite(fraction) || fraction < 0 || fraction > 1) throw new ArgumentOutOfRangeException(nameof(fraction));
        var baseline = TextBaseline(node); return baseline.At(fraction * baseline.Length);
    }
    public double ProjectTypeOnPath(DesignNode node, Vec2 localPoint) => TextBaseline(node).Project(localPoint);

    public void ValidateTypeOnPath(DesignNode node)
    {
        if (GetTypeOnPathStatus(node).Error is { } error)
            throw new InvalidOperationException("Invalid type-on-path baseline: " + error);
    }

    /// <summary>Geometric export bounds including path-text ink; excludes general live-effect expansion.</summary>
    public RectD GetArtworkBounds(DesignNode node)
    {
        var bounds = node.WorldBounds;
        foreach (var child in node.DescendantsAndSelf())
        {
            if (child.TextPath is null || !child.IsEffectivelyVisible) continue;
            var status = GetTypeOnPathStatus(child);
            if (status.Error is not null) continue;
            if (!status.InkBounds.IsEmpty) bounds = RectD.Union(bounds, child.WorldMatrix.Map(status.InkBounds));
        }
        return bounds;
    }

    private void ClearPathTextLayouts()
    {
        foreach (var entry in _pathTextLayouts.Values) entry.Layout.Dispose();
        _pathTextLayouts.Clear(); _pathTextBytes = 0;
    }
    private void ClearTypeOnPath()
    {
        ClearPathTextLayouts();
        foreach (var entry in _textBaselines.Values) entry.Measure.Dispose();
        _textBaselines.Clear();
    }
    private void PruneTypeOnPath(HashSet<string> retained)
    {
        foreach (var id in _pathTextLayouts.Keys.Where(id => !retained.Contains(id)).ToArray())
        {
            var entry = _pathTextLayouts[id]; entry.Layout.Dispose(); _pathTextBytes -= entry.Bytes; _pathTextLayouts.Remove(id);
        }
        foreach (var id in _textBaselines.Keys.Where(id => !retained.Contains(id)).ToArray())
        { _textBaselines[id].Measure.Dispose(); _textBaselines.Remove(id); }
    }
}
