namespace ArtSpace.Core;

public enum PathTextAlignment { Baseline, Ascender, Descender, Center }

/// <summary>
/// Text follows the node's retained PathData baseline. Start/End are fractions of its arc length,
/// measured in the original contour direction. Flip reverses both travel and glyph orientation.
/// Positive BaselineShift moves toward the glyph's ascender, independently of Flip.
/// </summary>
public sealed class TypeOnPathOptions
{
    public const int MaxTextLength = 8192;
    public double Start { get; set; }
    public double End { get; set; } = 1;
    public SvgTextPathPosition? SvgPosition { get; set; }
    public bool Flip { get; set; }
    public double BaselineShift { get; set; }
    public PathTextAlignment Alignment { get; set; }

    public TypeOnPathOptions Clone() => new()
    {
        Start = Start, End = End, Flip = Flip, BaselineShift = BaselineShift, Alignment = Alignment, SvgPosition = SvgPosition?.Clone()
    };

    public void Validate()
    {
        SvgPosition?.Validate();
        if (!double.IsFinite(Start) || !double.IsFinite(End) || Start < 0 || End > 1 || Start > End)
            throw new InvalidDataException("Type-on-path brackets must satisfy 0 ≤ Start ≤ End ≤ 1.");
        if (!double.IsFinite(BaselineShift) || Math.Abs(BaselineShift) > 10000 || !Enum.IsDefined(Alignment))
            throw new InvalidDataException("Invalid type-on-path alignment or baseline shift.");
    }
}
