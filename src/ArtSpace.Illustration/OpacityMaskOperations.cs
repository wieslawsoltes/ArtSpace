using ArtSpace.Core;
using ArtSpace.Editing;

namespace ArtSpace.Illustration;

/// <summary>Transactional, editable alpha/luminance masks. Mask artwork is retained, not flattened.</summary>
public static class OpacityMaskOperations
{
    public static DesignNode? FindOwner(DesignNode? node)
    {
        for (; node is not null; node = node.Parent)
            if (node.OpacityMaskId is not null) return node;
        return null;
    }

    public static void Make(EditorSession editor, OpacityMaskMode mode = OpacityMaskMode.Luminance)
    {
        ArgumentNullException.ThrowIfNull(editor);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var selected = editor.SelectionRoots.ToArray();
        var container = selected.Length == 1 && selected[0].IsContainer && selected[0].OpacityMaskId is null ? selected[0] : null;
        var nodes = container?.Children.Where(n => n.Id != container.ClipPathId).ToArray() ?? selected;
        if (nodes.Length < 2) throw new InvalidOperationException("Select artwork with a mask object above it, or a group containing both.");
        var parent = nodes[0].Parent;
        if (nodes.Any(n => n.Parent != parent || n.IsEffectivelyLocked || n.Kind == NodeKind.Slice))
            throw new InvalidOperationException("Opacity masks require unlocked, renderable siblings.");
        if (nodes.Any(n => parent?.ClipPathId == n.Id || parent?.OpacityMaskId == n.Id))
            throw new InvalidOperationException("Release the existing mask before moving its source into another set.");
        var siblings = parent?.Children ?? editor.Page.Nodes;
        nodes = nodes.OrderBy(siblings.IndexOf).ToArray();
        var mask = nodes[^1];
        editor.Edit("Make opacity mask", () =>
        {
            if (container is null)
            {
                var bounds = nodes.Select(n => n.LocalMatrix.Map(n.LocalBounds)).Aggregate(RectD.Union);
                container = new() { Kind = NodeKind.Group, Name = "Opacity Mask Group", X = bounds.X, Y = bounds.Y, Width = Math.Max(.001, bounds.Width), Height = Math.Max(.001, bounds.Height), Fills = [], Parent = parent };
                var insertion = siblings.IndexOf(mask);
                foreach (var node in nodes) { if (siblings.IndexOf(node) < insertion) insertion--; siblings.Remove(node); }
                foreach (var node in nodes) { node.X -= bounds.X; node.Y -= bounds.Y; container.Add(node); }
                siblings.Insert(insertion, container);
            }
            container.OpacityMaskId = mask.Id;
            container.OpacityMaskMode = mode;
            container.OpacityMaskEnabled = true;
            container.OpacityMaskInverted = false;
            container.OpacityMaskRegion = null;
            editor.Select(container);
        });
    }

    public static void Release(EditorSession editor) => Change(editor, "Release opacity mask", owner => owner.OpacityMaskId = null);
    public static void Invert(EditorSession editor) => Change(editor, "Invert opacity mask", owner => owner.OpacityMaskInverted = !owner.OpacityMaskInverted);
    public static void ToggleEnabled(EditorSession editor) => Change(editor, "Enable or disable opacity mask", owner => owner.OpacityMaskEnabled = !owner.OpacityMaskEnabled);
    public static void SetMode(EditorSession editor, OpacityMaskMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        Change(editor, "Opacity mask mode", owner => owner.OpacityMaskMode = mode);
    }
    public static void EditMask(EditorSession editor)
    {
        var owner = RequireOwner(editor);
        editor.Select(owner.OpacityMask); editor.Tool = EditorTool.Move;
    }
    public static void EditContents(EditorSession editor)
    {
        var owner = RequireOwner(editor);
        editor.Select(owner.Children.Where(n => n.Id != owner.OpacityMaskId && n.Id != owner.ClipPathId && !n.IsEffectivelyLocked).Select(n => n.Id));
        editor.Tool = EditorTool.Move;
    }
    private static DesignNode RequireOwner(EditorSession editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        var owner = FindOwner(editor.Primary) ?? throw new InvalidOperationException("Select an opacity mask group or an object inside one.");
        if (owner.IsEffectivelyLocked) throw new InvalidOperationException("Unlock the opacity mask group first.");
        return owner;
    }
    private static void Change(EditorSession editor, string label, Action<DesignNode> action)
    {
        var owner = RequireOwner(editor);
        editor.Edit(label, () => { action(owner); editor.Select(owner); });
    }
}
