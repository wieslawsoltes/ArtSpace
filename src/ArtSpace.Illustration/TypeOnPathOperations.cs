using ArtSpace.Core;
using ArtSpace.Documents;
using ArtSpace.Editing;
using ArtSpace.Skia;
using SkiaSharp;

namespace ArtSpace.Illustration;

/// <summary>Reusable text-on-contour commands. Geometry, text and appearance remain independently editable.</summary>
public static class TypeOnPathOperations
{
    public static void Create(EditorSession editor, SceneRenderer renderer, string text)
    {
        ArgumentNullException.ThrowIfNull(editor); ArgumentNullException.ThrowIfNull(renderer);
        ValidateText(text);
        var selected = editor.SelectionRoots;
        if (selected.Count != 1 || !CanUseBaseline(selected[0]))
            throw new InvalidOperationException("Select one unlocked vector contour, or select text and a sibling contour to attach them.");
        var node = selected[0];
        using var baseline = new SKPath(renderer.Geometry(node));
        using var measurement = new MeasuredContour(baseline);
        var data = baseline.ToSvgPathData();
        var fill = node.Fills.FirstOrDefault(f => f.Visible)?.Color ?? node.Strokes.FirstOrDefault()?.Color ?? "#203F49";
        editor.Edit("Create type on a path", () =>
        {
            node.PathData = data; node.PathWidth = node.Width; node.PathHeight = node.Height; node.Points.Clear();
            node.Kind = NodeKind.Text; node.TextPath = new(); node.Text = text;
            node.Fills = [new() { Color = fill }]; node.Strokes.Clear();
            node.Name = "Type on a Path"; editor.Tool = EditorTool.Move;
        });
    }

    public static void Attach(EditorSession editor, SceneRenderer renderer)
    {
        var selected = editor.SelectionRoots;
        if (selected.Count != 2) throw new InvalidOperationException("Select one text object and one vector contour.");
        var text = selected.SingleOrDefault(n => n.Kind == NodeKind.Text && n.TextPath is null);
        var path = selected.SingleOrDefault(CanUseBaseline);
        if (text is null || path is null || text.Parent != path.Parent || text.IsEffectivelyLocked || IsMaskSource(text))
            throw new InvalidOperationException("Select unlocked sibling text and vector objects that are not active mask sources.");
        ValidateText(text.Text);
        using var baseline = new SKPath(renderer.Geometry(path));
        using var measurement = new MeasuredContour(baseline);
        var data = baseline.ToSvgPathData(); var transform = path.LocalMatrix;
        editor.Edit("Attach text to path", () =>
        {
            var siblings = path.Parent?.Children ?? editor.Page.Nodes;
            var index = siblings.IndexOf(path); if (siblings.IndexOf(text) < index) index--;
            siblings.Remove(text); editor.RemoveNode(path);
            text.Width = path.Width; text.Height = path.Height;
            text.PathData = data; text.PathWidth = path.Width; text.PathHeight = path.Height;
            text.Points.Clear(); text.TextPath = new();
            NodeGeometry.SetExactMatrix(text, transform);
            siblings.Insert(Math.Clamp(index, 0, siblings.Count), text);
            editor.Select(text); editor.Tool = EditorTool.Move;
        });
    }

    public static void Update(EditorSession editor, string label, Action<TypeOnPathOptions> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var nodes = editor.SelectionRoots.Where(n => n.Kind == NodeKind.Text && n.TextPath is not null && !n.IsEffectivelyLocked).ToArray();
        if (nodes.Length == 0) throw new InvalidOperationException("Select unlocked type-on-path text.");
        editor.Edit(label, () =>
        {
            foreach (var node in nodes)
            {
                var options = node.TextPath!.Clone(); change(options); options.Validate(); node.TextPath = options;
            }
        });
    }

    /// <summary>Keep text and appearance but discard the baseline, producing ordinary editable area text.</summary>
    public static void ConvertToAreaText(EditorSession editor)
    {
        var nodes = editor.SelectionRoots.Where(n => n.Kind == NodeKind.Text && n.TextPath is not null && !n.IsEffectivelyLocked).ToArray();
        if (nodes.Length == 0) throw new InvalidOperationException("Select unlocked type-on-path text.");
        editor.Edit("Convert to area text", () =>
        {
            foreach (var node in nodes)
            {
                node.TextPath = null; node.PathData = null; node.Points.Clear(); node.PathWidth = node.PathHeight = 0;
            }
        });
    }

    /// <summary>Changes offset units while retaining each selected baseline's own measured position.</summary>
    public static void SetSvgOffsetUnits(EditorSession editor, SceneRenderer renderer, bool percentage)
    {
        ArgumentNullException.ThrowIfNull(editor); ArgumentNullException.ThrowIfNull(renderer);
        var nodes = editor.SelectionRoots.Where(n => n.TextPath?.SvgPosition is { } position && position.Percentage != percentage && !n.IsEffectivelyLocked).ToArray();
        if (nodes.Length == 0) return;
        editor.Edit("SVG text offset units", () =>
        {
            foreach (var node in nodes)
            {
                renderer.ValidateTypeOnPath(node);
                var length = renderer.GetTypeOnPathStatus(node).PathLength;
                var options = node.TextPath!.Clone(); var position = options.SvgPosition!;
                var distance = position.Resolve(length);
                position.Percentage = percentage; position.Offset = position.FromDistance(distance, length);
                options.Validate(); node.TextPath = options;
            }
        });
    }

    /// <summary>Explicitly switches SVG anchor layout to a full native bracket interval; text placement may change.</summary>
    public static void UseBracketLayout(EditorSession editor, SceneRenderer renderer)
    {
        var nodes = editor.SelectionRoots.Where(n => n.TextPath?.SvgPosition is not null && !n.IsEffectivelyLocked).ToArray();
        if (nodes.Length == 0) throw new InvalidOperationException("Select SVG-positioned path text.");
        editor.Edit("Use native bracket layout", () =>
        {
            foreach (var node in nodes)
            {
                renderer.ValidateTypeOnPath(node);
                var data = renderer.Geometry(node).ToSvgPathData();
                var options = node.TextPath!.Clone(); options.SvgPosition = null; options.Start = 0; options.End = 1;
                node.PathData = data; node.PathWidth = node.Width; node.PathHeight = node.Height; node.TextPath = options;
            }
        });
    }

    public static bool CanUseBaseline(DesignNode node) => PathEditing.CanEdit(node) && node.Kind != NodeKind.Text && !IsMaskSource(node);
    private static bool IsMaskSource(DesignNode node) => node.Parent is { } parent && (parent.ClipPathId == node.Id || parent.OpacityMaskId == node.Id);
    private static void ValidateText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > TypeOnPathOptions.MaxTextLength) throw new InvalidOperationException("Type-on-path text exceeds the supported length.");
    }
}
