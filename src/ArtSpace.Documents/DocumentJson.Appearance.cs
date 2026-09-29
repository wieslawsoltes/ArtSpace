using ArtSpace.Core;

namespace ArtSpace.Documents;

public static partial class DocumentJson
{
    public const int MaxEffectsPerNode = 32;
    public const int MaxGraphicStyles = 1024;

    private static void ValidateLiveAppearance(DesignNode node)
    {
        if (!Enum.IsDefined(node.Blend) || node.Effects is null || node.Effects.Count > MaxEffectsPerNode)
            throw new InvalidDataException("Invalid blend mode or live-effect stack.");
        foreach (var effect in node.Effects)
        {
            if (effect is null || !Enum.IsDefined(effect.Kind) || !double.IsFinite(effect.Radius)
                || effect.Radius is < 0 or > 256 || !double.IsFinite(effect.OffsetX) || Math.Abs(effect.OffsetX) > 4096
                || !double.IsFinite(effect.OffsetY) || Math.Abs(effect.OffsetY) > 4096
                || !double.IsFinite(effect.Opacity) || effect.Opacity is < 0 or > 1
                || !double.IsFinite(effect.Amount) || effect.Amount is < 0 or > 4 || effect.Color is null)
                throw new InvalidDataException("Invalid live effect: radius 0–256, offsets ±4096, opacity 0–1 and saturation 0–4 are supported.");
        }
        foreach (var entry in node.Overrides.Values)
        {
            if (entry is null) throw new InvalidDataException("Invalid symbol override.");
            if (entry.Appearance is { } appearance)
                Validate(new DesignDocument { Pages = [new() { Nodes = [new()
                {
                    Fills = appearance.Fills, Strokes = appearance.Strokes, Shadows = appearance.Shadows,
                    Effects = appearance.Effects, Opacity = appearance.Opacity, Blend = appearance.Blend
                }] }] });
        }
        foreach (var stroke in node.Strokes)
        {
            if (stroke is null || !double.IsFinite(stroke.DashOffset) || Math.Abs(stroke.DashOffset) > 1e9)
                throw new InvalidDataException("Invalid stroke dash offset.");
            if (stroke.Paint is { } paint) ValidatePaint(paint);
        }
        foreach (var shadow in node.Shadows)
        {
            if (shadow is null || !double.IsFinite(shadow.X) || !double.IsFinite(shadow.Y)
                || !double.IsFinite(shadow.Blur) || shadow.Blur < 0 || !double.IsFinite(shadow.Opacity))
                throw new InvalidDataException("Invalid shadow appearance.");
        }
    }

    private static void ValidatePaint(FillStyle fill)
    {
        if (!Enum.IsDefined(fill.Kind) || fill.Color is null || fill.Stops is null || fill.Stops.Count > 4096
            || !double.IsFinite(fill.Opacity) || !fill.Start.IsFinite || !fill.End.IsFinite
            || !double.IsFinite(fill.GradientRadius) || fill.GradientRadius < 0
            || !Enum.IsDefined(fill.GradientSpace) || !Enum.IsDefined(fill.GradientSpread)
            || !AffineGeometry.IsInvertible(fill.GradientTransform)
            || (fill.GradientFocus.HasValue && !fill.GradientFocus.Value.IsFinite)
            || fill.Stops.Any(s => s is null || s.Color is null || !double.IsFinite(s.Offset) || !double.IsFinite(s.Opacity)))
            throw new InvalidDataException("Invalid gradient stroke paint.");
    }

    private static void ValidateGraphicStyles(DesignDocument document)
    {
        if (document.GraphicStyles is null || document.GraphicStyles.Count > MaxGraphicStyles)
            throw new InvalidDataException("A document supports at most 1024 graphic styles.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var style in document.GraphicStyles)
        {
            if (style is null || string.IsNullOrWhiteSpace(style.Id) || !ids.Add(style.Id)
                || string.IsNullOrWhiteSpace(style.Name) || style.Name.Length > 256
                || !double.IsFinite(style.Opacity) || style.Opacity is < 0 or > 1)
                throw new InvalidDataException("Invalid or duplicate graphic style.");
            // Reuse the complete node-appearance validator without copying any style arrays.
            // The temporary document has no styles, so validation cannot recurse through presets.
            var node = new DesignNode
            {
                Fills = style.Fills, Strokes = style.Strokes, Shadows = style.Shadows,
                Effects = style.Effects, Opacity = style.Opacity, Blend = style.Blend
            };
            Validate(new DesignDocument { FormatVersion = CurrentFormatVersion, Pages = [new() { Nodes = [node] }] });
        }
    }
}
