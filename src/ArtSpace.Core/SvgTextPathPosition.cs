namespace ArtSpace.Core;

/// <summary>
/// SVG anchor positioning is different from a bounded Illustrator-style bracket interval.
/// Offset retains its original percentage/user-unit representation; PathLength calibrates
/// non-percentage start offsets. The referenced path's own transform affects the baseline,
/// not glyph size. Definition ancestors deliberately do not participate (SVG 2, Text 11.8).
/// </summary>
public sealed class SvgTextPathPosition
{
    public double Offset { get; set; }
    public bool Percentage { get; set; }
    public double? PathLength { get; set; }
    public Matrix2D PathTransform { get; set; } = Matrix2D.Identity;

    public SvgTextPathPosition Clone() => new()
    {
        Offset = Offset, Percentage = Percentage, PathLength = PathLength, PathTransform = PathTransform
    };

    public double Resolve(double measuredLength)
    {
        Validate();
        if (!double.IsFinite(measuredLength) || measuredLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(measuredLength));
        var offset = Percentage ? Offset * .01 * measuredLength
            : PathLength is { } authored ? Offset / authored * measuredLength : Offset;
        if (!double.IsFinite(offset)) throw new InvalidDataException("SVG path-text offset exceeds the numeric range.");
        return offset;
    }

    public double FromDistance(double distance, double measuredLength)
    {
        if (!double.IsFinite(distance) || !double.IsFinite(measuredLength) || measuredLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(distance));
        return Percentage ? distance / measuredLength * 100
            : PathLength is { } authored ? distance / measuredLength * authored : distance;
    }

    public void Validate()
    {
        if (!double.IsFinite(Offset) || Math.Abs(Offset) > 1e9)
            throw new InvalidDataException("SVG startOffset must be finite and bounded.");
        if (PathLength is { } length && (!double.IsFinite(length) || length <= 0 || length > 1e9))
            throw new InvalidDataException("SVG pathLength must be finite and positive in this importer.");
        if (!AffineGeometry.IsInvertible(PathTransform))
            throw new InvalidDataException("SVG text-path transform must be finite and invertible.");
    }
}
