namespace ArtSpace.Core;

/// <summary>Non-destructive filters applied in list order to the composed, masked object.</summary>
public enum LiveEffectKind { GaussianBlur, DropShadow, OuterGlow, Saturation }

public sealed class LiveEffect
{
    public LiveEffectKind Kind { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>Gaussian standard deviation in the object's local coordinate system.</summary>
    public double Radius { get; set; } = 6;
    public double OffsetX { get; set; } = 6;
    public double OffsetY { get; set; } = 6;
    public string Color { get; set; } = "#000000";
    public double Opacity { get; set; } = .5;
    /// <summary>Saturation multiplier: zero is grayscale; one is unchanged.</summary>
    public double Amount { get; set; } = 1;
    public LiveEffect Clone() => (LiveEffect)MemberwiseClone();
    public static string Name(LiveEffectKind kind) => kind switch
    {
        LiveEffectKind.GaussianBlur => "Gaussian Blur",
        LiveEffectKind.DropShadow => "Drop Shadow",
        LiveEffectKind.OuterGlow => "Outer Glow",
        LiveEffectKind.Saturation => "Saturation",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}

/// <summary>A document-local appearance preset. Applying it never replaces geometry or mask relationships.</summary>
public sealed class GraphicStyle
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Graphic Style";
    public double Opacity { get; set; } = 1;
    public BlendKind Blend { get; set; }
    public List<FillStyle> Fills { get; set; } = [];
    public List<StrokeStyle> Strokes { get; set; } = [];
    public List<ShadowStyle> Shadows { get; set; } = [];
    public List<LiveEffect> Effects { get; set; } = [];

    public static GraphicStyle Capture(DesignNode node, string name) => new()
    {
        Name = name, Opacity = node.Opacity, Blend = node.Blend,
        Fills = node.Fills.Select(CloneFill).ToList(),
        Strokes = node.Strokes.Select(CloneStroke).ToList(),
        Shadows = node.Shadows.Select(CloneShadow).ToList(),
        Effects = node.Effects.Select(e => e.Clone()).ToList()
    };

    public void ApplyTo(DesignNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.Opacity = Opacity; node.Blend = Blend;
        node.Fills = Fills.Select(CloneFill).ToList();
        node.Strokes = Strokes.Select(CloneStroke).ToList();
        node.Shadows = Shadows.Select(CloneShadow).ToList();
        node.Effects = Effects.Select(e => e.Clone()).ToList();
    }

    public static FillStyle CloneFill(FillStyle value) => new()
    {
        Kind = value.Kind, Color = value.Color, Opacity = value.Opacity, Visible = value.Visible,
        Start = value.Start, End = value.End, GradientSpace = value.GradientSpace,
        GradientSpread = value.GradientSpread, GradientTransform = value.GradientTransform,
        GradientRadius = value.GradientRadius, GradientFocus = value.GradientFocus,
        Stops = value.Stops.Select(s => new GradientStop { Offset = s.Offset, Color = s.Color, Opacity = s.Opacity }).ToList()
    };

    public static StrokeStyle CloneStroke(StrokeStyle value) => new()
    {
        Color = value.Color, Width = value.Width, Opacity = value.Opacity, Visible = value.Visible,
        Cap = value.Cap, Join = value.Join, MiterLimit = value.MiterLimit,
        Dashes = [.. value.Dashes], DashOffset = value.DashOffset,
        WidthProfile = StrokeProfiles.Copy(value.WidthProfile),
        Paint = value.Paint is null ? null : CloneFill(value.Paint)
    };

    private static ShadowStyle CloneShadow(ShadowStyle value) => new()
    {
        Visible = value.Visible, Color = value.Color, Opacity = value.Opacity,
        X = value.X, Y = value.Y, Blur = value.Blur
    };
}
