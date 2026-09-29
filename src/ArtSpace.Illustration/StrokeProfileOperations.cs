using ArtSpace.Core;
using ArtSpace.Editing;
using ArtSpace.Skia;

namespace ArtSpace.Illustration;

/// <summary>Reusable width editing. Each operation is one undo item and preserves the editable centerline.</summary>
public static class StrokeProfileOperations
{
    public static void SetPreset(EditorSession editor, SceneRenderer renderer, int strokeIndex, StrokeWidthPreset preset)
    {
        var profile = StrokeProfiles.Create(preset);
        Edit(editor, renderer, strokeIndex, "Apply width profile", stroke => stroke.WidthProfile = StrokeProfiles.Copy(profile));
    }

    public static void AddPoint(EditorSession editor, SceneRenderer renderer, int strokeIndex, double position)
    {
        if (!double.IsFinite(position) || position is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(position));
        Edit(editor, renderer, strokeIndex, "Add width point", stroke => Insert(stroke, position));
    }

    public static int Insert(StrokeStyle stroke, double position)
    {
        StrokeProfiles.Validate(stroke.WidthProfile);
        if (!double.IsFinite(position) || position is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(position));
        var list = stroke.WidthProfile;
        for (var i = 0; i < list.Count; i++) if (Math.Abs(list[i].Position - position) < 1e-6) return i;
        if (list.Count >= StrokeProfiles.MaxPoints) throw new InvalidOperationException("The 64-point stroke profile limit has been reached.");
        var width = StrokeProfiles.Evaluate(list, position);
        var index = list.FindIndex(p => p.Position > position); if (index < 0) index = list.Count;
        list.Insert(index, new() { Position = position, Left = width.Left, Right = width.Right }); return index;
    }

    public static void UpdatePoint(EditorSession editor, SceneRenderer renderer, int strokeIndex, int pointIndex, Action<StrokeWidthPoint> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        Edit(editor, renderer, strokeIndex, "Edit width point", stroke =>
        {
            if ((uint)pointIndex >= (uint)stroke.WidthProfile.Count) throw new InvalidOperationException("The width point no longer exists.");
            update(stroke.WidthProfile[pointIndex]);
        });
    }

    public static void RemovePoint(EditorSession editor, SceneRenderer renderer, int strokeIndex, int pointIndex)
        => Edit(editor, renderer, strokeIndex, "Delete width point", stroke =>
        {
            if ((uint)pointIndex >= (uint)stroke.WidthProfile.Count) throw new InvalidOperationException("The width point no longer exists.");
            stroke.WidthProfile.RemoveAt(pointIndex);
        });

    public static void Reverse(EditorSession editor, SceneRenderer renderer, int strokeIndex)
        => Edit(editor, renderer, strokeIndex, "Reverse width profile", stroke => stroke.WidthProfile = StrokeProfiles.Reverse(stroke.WidthProfile));

    public static void SwapSides(EditorSession editor, SceneRenderer renderer, int strokeIndex)
        => Edit(editor, renderer, strokeIndex, "Swap width sides", stroke =>
        {
            foreach (var point in stroke.WidthProfile) (point.Left, point.Right) = (point.Right, point.Left);
        });

    private static void Edit(EditorSession editor, SceneRenderer renderer, int strokeIndex, string label, Action<StrokeStyle> update)
    {
        ArgumentNullException.ThrowIfNull(editor); ArgumentNullException.ThrowIfNull(renderer);
        if (strokeIndex < 0) throw new ArgumentOutOfRangeException(nameof(strokeIndex));
        var nodes = editor.SelectionRoots.Where(n => !n.IsEffectivelyLocked).ToArray();
        if (nodes.Length == 0) throw new InvalidOperationException("Select unlocked vector artwork with a stroke.");
        if (nodes.Any(n => !PathEditing.CanEdit(n) || strokeIndex >= n.Strokes.Count))
            throw new InvalidOperationException("Each selected vector needs the requested stroke. Create outlines before editing text width.");
        editor.Edit(label, () =>
        {
            foreach (var node in nodes)
            {
                var stroke = node.Strokes[strokeIndex]; update(stroke); StrokeProfiles.Validate(stroke.WidthProfile);
                // Preflight before commit: bounded tessellation failures restore the complete transaction.
                renderer.StrokeOutline(node, stroke);
                ComponentService.SetAppearanceOverride(node);
            }
        });
    }
}
