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
        ArgumentNullException.ThrowIfNull(node);
        var bounds = node.WorldBounds;
        // An artboard export keeps its chosen dimensions even when text extends past its clip.
        if (node.IsContainer && node.ClipContent) return bounds;
        foreach (var child in node.DescendantsAndSelf())
        {
            if (child.TextPath is null || !child.IsEffectivelyVisible) continue;
            var status = GetTypeOnPathStatus(child);
            if (status.Error is not null || status.InkBounds.IsEmpty) continue;
            var ink = child.WorldMatrix.Map(status.InkBounds);
            // Respect frame clips inside this exported subtree. Ancestors outside a selected root
            // are deliberately excluded, matching DrawWorldNode's independent-selection export.
            for (var parent = ReferenceEquals(child, node) ? null : child.Parent; parent is not null; parent = parent.Parent)
            {
                if (parent.ClipContent)
                {
                    var clip = parent.WorldBounds;
                    var left = Math.Max(ink.X, clip.X); var top = Math.Max(ink.Y, clip.Y);
                    ink = new(left, top, Math.Max(0, Math.Min(ink.Right, clip.Right) - left), Math.Max(0, Math.Min(ink.Bottom, clip.Bottom) - top));
                }
                if (ReferenceEquals(parent, node)) break;
            }
            if (!ink.IsEmpty) bounds = RectD.Union(bounds, ink);
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
