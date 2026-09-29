using ArtSpace.Core;
using ArtSpace.Documents;
using ArtSpace.Editing;

namespace ArtSpace.Illustration;

/// <summary>Appearance commands shared by native and browser workbenches; all edits are atomic and undoable.</summary>
public static class AppearanceOperations
{
    public static void AddEffect(EditorSession session, LiveEffectKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        session.UpdateSelection("Add " + LiveEffect.Name(kind), node =>
        {
            if (node.Effects.Count >= DocumentJson.MaxEffectsPerNode)
                throw new InvalidOperationException("This object's effect stack is full.");
            node.Effects.Add(new()
            {
                Kind = kind, Color = kind == LiveEffectKind.OuterGlow ? "#FFB84D" : "#000000",
                Amount = kind == LiveEffectKind.Saturation ? 0 : 1
            });
        });
    }

    public static void UpdateEffect(EditorSession session, int index, string label, Action<LiveEffect> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        session.UpdateSelection(label, node => { if (index < node.Effects.Count) update(node.Effects[index]); });
    }

    public static void RemoveEffect(EditorSession session, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        session.UpdateSelection("Remove live effect", node => { if (index < node.Effects.Count) node.Effects.RemoveAt(index); });
    }

    public static void DuplicateEffect(EditorSession session, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        session.UpdateSelection("Duplicate live effect", node =>
        {
            if (index >= node.Effects.Count) return;
            if (node.Effects.Count >= DocumentJson.MaxEffectsPerNode)
                throw new InvalidOperationException("This object's effect stack is full.");
            node.Effects.Insert(index + 1, node.Effects[index].Clone());
        });
    }

    public static void MoveEffect(EditorSession session, int index, int direction)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        session.UpdateSelection("Reorder live effect", node =>
        {
            var target = index + direction;
            if (index >= node.Effects.Count || target < 0 || target >= node.Effects.Count) return;
            (node.Effects[index], node.Effects[target]) = (node.Effects[target], node.Effects[index]);
        });
    }

    public static void Clear(EditorSession session, bool basic = false) => session.UpdateSelection(
        basic ? "Reduce to basic appearance" : "Clear appearance", node =>
        {
            var fill = node.Fill;
            node.Fills = basic ? [new() { Color = fill }] : [];
            node.Strokes = []; node.Shadows = []; node.Effects = [];
            node.Opacity = 1; node.Blend = BlendKind.Normal;
        });

    public static string CaptureStyle(EditorSession session, string? name = null)
    {
        var node = session.Primary ?? throw new InvalidOperationException("Select artwork to create a graphic style.");
        if (session.Document.GraphicStyles.Count >= DocumentJson.MaxGraphicStyles)
            throw new InvalidOperationException("This document's graphic-style library is full.");
        name = string.IsNullOrWhiteSpace(name) ? "Graphic Style " + (session.Document.GraphicStyles.Count + 1) : name.Trim();
        if (name.Length > 256) throw new ArgumentException("Style names are limited to 256 characters.", nameof(name));
        var style = GraphicStyle.Capture(node, name);
        session.Edit("New graphic style", () => session.Document.GraphicStyles.Add(style));
        return style.Id;
    }

    public static void ApplyStyle(EditorSession session, string id)
    {
        var style = FindStyle(session, id);
        session.UpdateSelection("Apply " + style.Name, style.ApplyTo);
    }

    public static void RenameStyle(EditorSession session, string id, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        name = name.Trim();
        if (name.Length > 256) throw new ArgumentException("Style names are limited to 256 characters.", nameof(name));
        var style = FindStyle(session, id);
        session.Edit("Rename graphic style", () => style.Name = name);
    }

    public static void DeleteStyle(EditorSession session, string id)
    {
        var style = FindStyle(session, id);
        session.Edit("Delete graphic style", () => session.Document.GraphicStyles.Remove(style));
    }

    private static GraphicStyle FindStyle(EditorSession session, string id) =>
        session.Document.GraphicStyles.Find(style => style.Id == id)
        ?? throw new InvalidOperationException("The graphic style no longer exists.");
}
