using ArtSpace.Core;
using ArtSpace.Documents;

namespace ArtSpace.Skia;

public sealed partial class SceneRenderer
{
    /// <summary>Exports supported SVG, expanding only variable-width strokes without mutating the native document.</summary>
    public string ExportSvg(IEnumerable<DesignNode> roots, RectD bounds)
        => SvgFormat.Export(roots, bounds, new SvgExportOptions { ExpandStroke = ExpandSvgStroke });

    private SvgStrokeOutline ExpandSvgStroke(DesignNode node, StrokeStyle stroke)
    {
        var fill = stroke.Paint is { } source ? GraphicStyle.CloneFill(source) : new FillStyle { Color = stroke.Color };
        if (fill.Kind != FillKind.Solid)
        {
            if (fill.GradientSpace == GradientSpace.Legacy)
            {
                fill.Start = new(fill.Start.X * node.Width, fill.Start.Y * node.Height);
                fill.End = new(fill.End.X * node.Width, fill.End.Y * node.Height);
                fill.GradientFocus = fill.Start;
                fill.GradientRadius = Math.Max(1, fill.Start.DistanceTo(fill.End));
            }
            else fill.GradientTransform = GradientCoordinateMatrix(node, fill);
            fill.GradientSpace = GradientSpace.UserSpaceOnUse;
        }
        return new(StrokeOutline(node, stroke).ToSvgPathData(), fill);
    }
}
