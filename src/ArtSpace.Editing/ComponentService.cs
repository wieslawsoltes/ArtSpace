using ArtSpace.Core;
using ArtSpace.Documents;

namespace ArtSpace.Editing;

/// <summary>Reusable local components with linked instances and explicit text, fill and full appearance overrides.</summary>
public static class ComponentService
{
    public static void MakeComponent(EditorSession editor)
    {
        if (editor.SelectionRoots.Count != 1 || editor.Primary is not { } node || node.IsEffectivelyLocked) return;
        editor.Edit("Create component", () =>
        {
            if (node.IsContainer && node.Kind != NodeKind.Instance)
            {
                node.Kind = NodeKind.Component; node.ComponentId = null; return;
            }
            var siblings = node.Parent?.Children ?? editor.Page.Nodes;
            var index = siblings.IndexOf(node);
            var bounds = node.LocalMatrix.Map(node.LocalBounds);
            var component = new DesignNode { Kind = NodeKind.Component, Name = node.Name + " / Component", X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height, Fills = [], Parent = node.Parent };
            siblings.RemoveAt(index); node.X -= bounds.X; node.Y -= bounds.Y; component.Add(node); siblings.Insert(index, component); editor.Select(component);
        });
    }

    public static DesignNode InsertInstance(EditorSession editor, DesignNode component, Vec2 position)
    {
        if (component.Kind != NodeKind.Component) throw new ArgumentException("The source must be a component.", nameof(component));
        var clone = DocumentJson.CloneNode(component);
        SetSources(clone); DocumentJson.RegenerateIds([clone]); clone.Kind = NodeKind.Instance; clone.ComponentId = component.Id; clone.X = position.X; clone.Y = position.Y;
        editor.Edit("Insert component instance", () => { editor.AddNode(clone); editor.Select(clone); }); return clone;
    }
    public static void Detach(EditorSession editor) => editor.UpdateSelection("Detach instance", n =>
    {
        if (n.Kind != NodeKind.Instance) return; n.Kind = NodeKind.Frame; n.ComponentId = null; n.Overrides.Clear(); foreach (var c in n.DescendantsAndSelf()) c.SourceId = null;
    });
    public static void ResetOverrides(EditorSession editor)
    {
        editor.Edit("Reset instance overrides", () =>
        {
            foreach (var node in editor.Selection.Where(n => n.Kind == NodeKind.Instance && !n.IsEffectivelyLocked))
            {
                // Root placement/transparency historically belong to the instance. Reset only
                // transparency explicitly captured by a full appearance override.
                if (node.SourceId is { } source && node.Overrides.TryGetValue(source, out var entry)
          && entry.Appearance is not null && node.ComponentId is { } componentId
          && editor.Document.Find(componentId) is { Kind: NodeKind.Component } definition)
                { node.Opacity = definition.Opacity; node.Blend = definition.Blend; }
                node.Overrides.Clear();
            }
        });
    }
    public static void SetOverride(DesignNode node, string? text = null, string? fill = null)
    {
        var instance = node; while (instance is not null && instance.Kind != NodeKind.Instance) instance = instance.Parent;
        if (instance is null || node.SourceId is null) return;
        if (!instance.Overrides.TryGetValue(node.SourceId, out var value)) instance.Overrides[node.SourceId] = value = new();
        if (text is not null) value.Text = text; if (fill is not null) value.Fill = fill;
    }
    public static void SetAppearanceOverride(DesignNode node)
    {
        var instance = node;
        while (instance is not null && instance.Kind != NodeKind.Instance) instance = instance.Parent;
        if (instance is null || node.SourceId is null) return;
        if (!instance.Overrides.TryGetValue(node.SourceId, out var value)) instance.Overrides[node.SourceId] = value = new();
        var appearance = GraphicStyle.Capture(node, "Appearance override");
        // A semantically unchanged override must not introduce a new GUID and undo entry.
        appearance.Id = value.Appearance?.Id ?? "appearance-" + node.SourceId;
        value.Appearance = appearance;
        value.Fill = null;
    }
    public static void Synchronize(DesignDocument document)
    {
        var components = document.AllNodes().Where(n => n.Kind == NodeKind.Component).ToDictionary(n => n.Id);
        foreach (var instance in document.AllNodes().Where(n => n.Kind == NodeKind.Instance).ToArray())
        {
            if (instance.ComponentId is null || !components.TryGetValue(instance.ComponentId, out var definition)) continue;
            var existing = instance.DescendantsAndSelf().Where(n => n.SourceId is not null).GroupBy(n => n.SourceId!).ToDictionary(g => g.Key, g => g.First().Id);
            var copy = DocumentJson.CloneNode(definition); SetSources(copy);
            foreach (var n in copy.DescendantsAndSelf())
            {
                var source = n.SourceId!; n.Id = existing.GetValueOrDefault(source) ?? Guid.NewGuid().ToString("N");
                if (instance.Overrides.TryGetValue(source, out var o)) { o.Appearance?.ApplyTo(n); if (o.Text is not null) n.Text = o.Text; if (o.Fill is not null) n.Fill = o.Fill; if (o.Visible.HasValue) n.Visible = o.Visible.Value; }
            }
            var remapped = copy.DescendantsAndSelf().ToDictionary(n => n.SourceId!, n => n.Id);
            foreach (var n in copy.DescendantsAndSelf())
                if (n.ClipPathId is { } mask && remapped.TryGetValue(mask, out var id)) n.ClipPathId = id;
            foreach (var n in copy.DescendantsAndSelf())
                if (n.OpacityMaskId is { } maskId && remapped.TryGetValue(maskId, out var newMaskId)) n.OpacityMaskId = newMaskId;
            instance.OpacityMaskId = copy.OpacityMaskId;
            instance.OpacityMaskMode = copy.OpacityMaskMode;
            instance.OpacityMaskEnabled = copy.OpacityMaskEnabled;
            instance.OpacityMaskInverted = copy.OpacityMaskInverted;
            instance.OpacityMaskRegion = copy.OpacityMaskRegion;
            instance.ClipPathId = copy.ClipPathId;
            instance.Children = copy.Children; foreach (var child in instance.Children) child.Parent = instance;
            instance.Fills = copy.Fills; instance.Strokes = copy.Strokes; instance.Shadows = copy.Shadows; instance.Effects = copy.Effects; instance.CornerRadius = copy.CornerRadius;
            instance.Layout = copy.Layout;
            if (instance.SourceId is { } rootSource && instance.Overrides.TryGetValue(rootSource, out var rootOverride)
                && rootOverride.Appearance is not null)
            { instance.Opacity = copy.Opacity; instance.Blend = copy.Blend; }
        }
    }
    private static void SetSources(DesignNode node) { foreach (var n in node.DescendantsAndSelf()) n.SourceId = n.Id; }
}
