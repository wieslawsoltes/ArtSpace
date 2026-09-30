using ArtSpace.Core;
using ArtSpace.Documents;
using ArtSpace.Skia;

namespace ArtSpace.Illustration;

public static class IllustrationSvgExport
{
    /// <summary>
    /// Export a detached snapshot. Type-on-path objects become vector glyph outlines, preserving their
    /// appearance without claiming native SVG textPath/shaping equivalence. The source document and
    /// undo history are untouched. Existing unsupported SVG live effects still fail explicitly.
    /// </summary>
    public static string Export(IEnumerable<DesignNode> roots, RectD bounds, SceneRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(roots); ArgumentNullException.ThrowIfNull(renderer);
        var copies = new List<DesignNode>();
        foreach (var original in roots)
        {
            var copy = DocumentJson.CloneNode(original);
            NodeGeometry.SetExactMatrix(copy, original.WorldMatrix);
            foreach (var node in copy.DescendantsAndSelf())
            {
                if (node.TextPath is null) continue;
                using var glyphs = renderer.CreateTextOutline(node);
                node.TextPath = null;
                if (glyphs.IsEmpty)
                {
                    node.Kind = NodeKind.Path; node.PathData = "M0 0"; node.Points.Clear(); node.Text = "";
                }
                else { PathEditing.Write(node, glyphs); node.Text = ""; }
            }
            copies.Add(copy);
        }
        return SvgFormat.Export(copies, bounds);
    }
}
