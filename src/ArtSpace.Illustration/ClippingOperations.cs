using ArtSpace.Core;
using ArtSpace.Editing;

namespace ArtSpace.Illustration;

/// <summary>Non-destructive vector clipping sets. Geometry, children and stable identities remain editable.</summary>
public static class ClippingOperations
{
    public static void Make(EditorSession editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        var selected = editor.SelectionRoots.ToArray();
        var existingGroup = selected.Length == 1 && selected[0].IsContainer && selected[0].ClipPathId is null ? selected[0] : null;
        var nodes = existingGroup?.Children.ToArray() ?? selected;
        if (nodes.Length < 2) throw new InvalidOperationException("Select artwork and a topmost vector clipping path.");
        var parent = nodes[0].Parent;
        if (nodes.Any(n => n.Parent != parent || n.IsEffectivelyLocked) || existingGroup?.IsEffectivelyLocked == true)
            throw new InvalidOperationException("Clipping requires unlocked objects in the same parent.");
        if (parent?.ClipPathId is { } activeMask && nodes.Any(n => n.Id == activeMask))
            throw new InvalidOperationException("Release the existing clipping mask before regrouping its clipping path.");
        if (nodes.Any(n => parent?.OpacityMaskId == n.Id)) throw new InvalidOperationException("Release the opacity mask before regrouping its source.");
        var siblings = parent?.Children ?? editor.Page.Nodes;
        nodes = nodes.OrderBy(siblings.IndexOf).ToArray(); var mask = nodes[^1];
        if (!IsVectorMask(mask)) throw new InvalidOperationException("The topmost object must be a vector shape or compound path. Outline text first.");
        editor.Edit("Make clipping mask", () =>
        {
            var group = existingGroup;
            if (group is null)
            {
                var bounds = mask.LocalMatrix.Map(mask.LocalBounds);
                group = new DesignNode { Kind = NodeKind.Group, Name = "Clip Group", X = bounds.X, Y = bounds.Y,
                    Width = bounds.Width, Height = bounds.Height, Fills = [], Parent = parent };
                var insertion = siblings.IndexOf(mask);
                foreach (var node in nodes) { if (siblings.IndexOf(node) < insertion) insertion--; siblings.Remove(node); }
                foreach (var node in nodes) { node.X -= bounds.X; node.Y -= bounds.Y; group.Add(node); }
                siblings.Insert(insertion, group);
            }
            group.ClipPathId = mask.Id;
            mask.Fills.Clear(); mask.Strokes.Clear(); mask.Shadows.Clear(); mask.Opacity = 1; mask.Blend = BlendKind.Normal;
            editor.Select(group);
        });
    }
    public static void Release(EditorSession editor)
    {
        var groups = editor.SelectionRoots.Select(FindGroup).Where(n => n is not null).Distinct().Cast<DesignNode>().ToArray();
        if (groups.Length == 0) throw new InvalidOperationException("Select a clipping set or an object inside one.");
        if (groups.Any(g => g.IsEffectivelyLocked)) throw new InvalidOperationException("Unlock the clipping set first.");
        // Preserve grouping and appearance. Only remove the clipping relation.
        editor.Edit("Release clipping mask", () => { foreach (var group in groups) group.ClipPathId = null; editor.Select(groups.Select(g => g.Id)); });
    }
    public static void EditMask(EditorSession editor)
    {
        var group = FindGroup(editor.Primary);
        if (group?.ClippingPath is not { } mask) throw new InvalidOperationException("Select a clipping set.");
        editor.Select(mask); editor.Tool = EditorTool.DirectSelect;
    }
    public static void EditContents(EditorSession editor)
    {
        var group = FindGroup(editor.Primary) ?? throw new InvalidOperationException("Select a clipping set.");
        editor.Select(group.Children.Where(n => n.Id != group.ClipPathId && !n.IsEffectivelyLocked).Select(n => n.Id));
        editor.Tool = EditorTool.Move;
    }
    public static DesignNode? FindGroup(DesignNode? node)
    {
        for (; node is not null; node = node.Parent) if (node.ClipPathId is not null) return node;
        return null;
    }
    public static bool IsVectorMask(DesignNode node) => !node.IsContainer && node.Children.Count == 0 && node.Kind is not NodeKind.Text and not NodeKind.Slice;
}
