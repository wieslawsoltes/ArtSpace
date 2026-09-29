using ArtSpace.Core;
using SkiaSharp;

namespace ArtSpace.Skia;

public sealed partial class SceneRenderer
{
    private readonly record struct EffectState(LiveEffectKind Kind, bool Enabled, double Radius,
        double X, double Y, string Color, double Opacity, double Amount)
    {
        public static EffectState Read(LiveEffect effect) => new(effect.Kind, effect.Enabled,
            effect.Radius, effect.OffsetX, effect.OffsetY, effect.Color, effect.Opacity, effect.Amount);
    }
    private readonly record struct ShadowState(double X, double Y, double Blur, string Color, double Opacity);
    private sealed record EffectEntry(ShadowState? Shadow, EffectState[] States, SKImageFilter? Filter);
    private readonly Dictionary<string, EffectEntry> _effectFilters = new(StringComparer.Ordinal);
    public long EffectFilterBuilds { get; private set; }
    public long EffectFilterHits { get; private set; }
    public int CachedEffectFilterCount => _effectFilters.Count;

    private static bool HasVisibleEffects(DesignNode node)
    {
        foreach (var effect in node.Effects) if (effect.Enabled) return true;
        return false;
    }

    private SKImageFilter? EffectFilter(DesignNode node)
    {
        // Preserve the legacy first-visible-shadow convention for schema 1–3 documents.
        ShadowState? shadow = null;
        foreach (var value in node.Shadows)
        {
            if (!value.Visible) continue;
            shadow = new(value.X, value.Y, value.Blur, value.Color, value.Opacity);
            break;
        }
        if (shadow is null && node.Effects.Count == 0)
        {
            if (_effectFilters.Remove(node.Id, out var removed)) removed.Filter?.Dispose();
            return null;
        }
        _effectFilters.TryGetValue(node.Id, out var cached);
        if (cached is not null && cached.Shadow == shadow && cached.States.Length == node.Effects.Count)
        {
            var equal = true;
            for (var i = 0; i < cached.States.Length; i++)
                if (cached.States[i] != EffectState.Read(node.Effects[i])) { equal = false; break; }
            if (equal) { EffectFilterHits++; return cached.Filter; }
        }

        SKImageFilter? filter = null;
        try
        {
            if (shadow is { } legacy)
                filter = SKImageFilter.CreateDropShadow((float)legacy.X, (float)legacy.Y,
                    (float)Math.Clamp(legacy.Blur / 2, 0, 256), (float)Math.Clamp(legacy.Blur / 2, 0, 256),
                    Color(legacy.Color, legacy.Opacity)) ?? throw new InvalidOperationException("Cannot create shadow filter.");
            foreach (var effect in node.Effects)
            {
                if (!effect.Enabled || (effect.Kind == LiveEffectKind.GaussianBlur && effect.Radius == 0)
                    || (effect.Kind == LiveEffectKind.Saturation && effect.Amount == 1)) continue;
                var next = CreateEffect(effect, filter)
                    ?? throw new InvalidOperationException("Cannot create " + LiveEffect.Name(effect.Kind) + " filter.");
                // Native filters retain their inputs. Identity factories may return the same wrapper.
                if (!ReferenceEquals(filter, next)) filter?.Dispose();
                filter = next;
            }
            var states = node.Effects.Select(EffectState.Read).ToArray();
            if (_effectFilters.Count >= 4096) ClearEffects(); else cached?.Filter?.Dispose();
            _effectFilters[node.Id] = new(shadow, states, filter);
            EffectFilterBuilds++;
            return filter;
        }
        catch { filter?.Dispose(); throw; }
    }

    private static SKImageFilter? CreateEffect(LiveEffect effect, SKImageFilter? input)
    {
        switch (effect.Kind)
        {
            case LiveEffectKind.GaussianBlur:
                return SKImageFilter.CreateBlur((float)effect.Radius, (float)effect.Radius, input);
            case LiveEffectKind.DropShadow:
                return SKImageFilter.CreateDropShadow((float)effect.OffsetX, (float)effect.OffsetY,
                    (float)effect.Radius, (float)effect.Radius, Color(effect.Color, effect.Opacity), input);
            case LiveEffectKind.OuterGlow:
                return SKImageFilter.CreateDropShadow(0, 0, (float)effect.Radius,
                    (float)effect.Radius, Color(effect.Color, effect.Opacity), input);
            case LiveEffectKind.Saturation:
                var saturation = (float)effect.Amount;
                var r = .2126f * (1 - saturation); var g = .7152f * (1 - saturation); var b = .0722f * (1 - saturation);
                using (var color = SKColorFilter.CreateColorMatrix(new float[]
                {
                    r + saturation, g, b, 0, 0,
                    r, g + saturation, b, 0, 0,
                    r, g, b + saturation, 0, 0,
                    0, 0, 0, 1, 0
                })) return SKImageFilter.CreateColorFilter(color, input);
            default:
                throw new InvalidOperationException("Unsupported live effect.");
        }
    }

    private void PruneEffects(HashSet<string> retained)
    {
        foreach (var id in _effectFilters.Keys.Where(id => !retained.Contains(id)).ToArray())
        { _effectFilters[id].Filter?.Dispose(); _effectFilters.Remove(id); }
    }
    private void ClearEffects()
    {
        foreach (var value in _effectFilters.Values) value.Filter?.Dispose();
        _effectFilters.Clear();
    }
}
