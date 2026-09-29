using ArtSpace.Core;

namespace ArtSpace.Documents;

/// <summary>Locally positioned vector coverage with its gradient expressed in local user-space coordinates.</summary>
public sealed record SvgStrokeOutline(string PathData, FillStyle Fill);

/// <summary>Optional host geometry services; Documents remains independent of Skia and Uno.</summary>
public sealed class SvgExportOptions
{
    /// <summary>Expands nonuniform strokes for standard SVG output. The native centerline and profile are not mutated.</summary>
    public Func<DesignNode, StrokeStyle, SvgStrokeOutline>? ExpandStroke { get; init; }
}
