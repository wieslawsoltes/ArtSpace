using ArtSpace.Core;
using ArtSpace.Documents;
using ArtSpace.Editing;
using ArtSpace.Skia;
using SkiaSharp;

namespace ArtSpace.Illustration;

/// <summary>Contour-aware illustration commands; every edit is a single transaction.</summary>
public static class PathOperations
{
    private static DesignNode[] Vectors(EditorSession editor)
    {
        var nodes = editor.SelectionRoots.Where(PathEditing.CanEdit).ToArray();
        if (nodes.Length == 0) throw new InvalidOperationException("Select an unlocked vector object.");
        return nodes;
    }

    public static void Edit(EditorSession editor, SceneRenderer renderer, string label, Action<EditablePath> edit)
    {
        var nodes = Vectors(editor);
        editor.Edit(label, () =>
        {
            foreach (var node in nodes)
            {
                var geometry = PathEditing.Read(node, renderer);
                edit(geometry); PathEditing.Write(node, geometry);
            }
        });
    }

    public static void AddAnchors(EditorSession editor, SceneRenderer renderer) => Edit(editor, renderer, "Add anchor points", path =>
    {
        var count = path.Contours.Sum(c => c.SegmentCount);
        if (path.AnchorCount + count > EditablePath.MaxAnchors) throw new InvalidOperationException("Anchor limit reached.");
        for (var ci = 0; ci < path.Contours.Count; ci++)
            for (var i = path.Contours[ci].SegmentCount - 1; i >= 0; i--) path.Split(new(ci, i), .5);
    });

    public static void Reverse(EditorSession editor, SceneRenderer renderer) => Edit(editor, renderer, "Reverse path direction", path => path.Reverse());
    public static void Smooth(EditorSession editor, SceneRenderer renderer, bool smooth) => Edit(editor, renderer, smooth ? "Smooth anchors" : "Corner anchors", path => path.Smooth(path.Addresses.ToArray(), smooth));
    public static void Close(EditorSession editor, SceneRenderer renderer) => Edit(editor, renderer, "Close contours", path => { foreach (var contour in path.Contours.Where(c => c.Points.Count > 1)) contour.Closed = true; });

    public static void CreateOutlines(EditorSession editor, SceneRenderer renderer)
    {
        var nodes = editor.SelectionRoots.SelectMany(n => n.DescendantsAndSelf()).Where(n => n.Kind == NodeKind.Text && !n.IsEffectivelyLocked).Distinct().ToArray();
        if (nodes.Length == 0) throw new InvalidOperationException("Select text or a group containing unlocked text.");
        editor.Edit("Create text outlines", () =>
        {
            foreach (var node in nodes)
            {
                using var path = renderer.CreateTextOutline(node);
                if (path.IsEmpty) continue; // Whitespace has no outlines; do not delete the original object.
                PathEditing.Write(node, path); node.Name += " outlines"; node.Text = "";
            }
        });
    }

    public static void MakeCompound(EditorSession editor, SceneRenderer renderer)
    {
        var selected = Vectors(editor);
        if (selected.Length < 2) throw new InvalidOperationException("Select two or more vectors with the same parent.");
        var parent = selected[0].Parent;
        if (selected.Any(n => n.Parent != parent)) throw new InvalidOperationException("Compound path objects must have the same parent.");
        var siblings = parent?.Children ?? editor.Page.Nodes;
        var ordered = selected.OrderBy(n => siblings.IndexOf(n)).ToArray();
        var top = ordered[^1];
        var result = DocumentJson.CloneNode(top, true); result.Name = "Compound path";
        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        foreach (var node in ordered)
        {
            using var part = new SKPath(renderer.Geometry(node));
            part.Transform(SceneRenderer.Matrix(node.LocalMatrix * top.LocalMatrix.Inverse)); path.AddPath(part);
        }
        PathEditing.Write(result, path);
        editor.Edit("Make compound path", () =>
        {
            var insert = siblings.IndexOf(top) - ordered.Length + 1;
            foreach (var node in ordered) editor.RemoveNode(node);
            editor.AddNode(result, parent); siblings.Remove(result); siblings.Insert(Math.Clamp(insert, 0, siblings.Count), result); editor.Select(result);
        });
    }

    public static void ReleaseCompound(EditorSession editor, SceneRenderer renderer)
    {
        var nodes = Vectors(editor);
        editor.Edit("Release compound path", () =>
        {
            var selection = new List<string>();
            foreach (var node in nodes)
            {
                var path = PathEditing.Read(node, renderer);
                if (path.Contours.Count < 2) { selection.Add(node.Id); continue; }
                var siblings = node.Parent?.Children ?? editor.Page.Nodes; var index = siblings.IndexOf(node);
                foreach (var contour in path.Contours)
                {
                    var part = new EditablePath { FillRule = path.FillRule }; part.Contours.Add(contour);
                    var output = DocumentJson.CloneNode(node, true); output.Name = node.Name + " contour";
                    PathEditing.Write(output, part); editor.AddNode(output, node.Parent);
                    siblings.Remove(output); siblings.Insert(index++, output); selection.Add(output.Id);
                }
                editor.RemoveNode(node);
            }
            editor.Select(selection);
        });
    }
}
