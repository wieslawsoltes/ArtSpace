using ArtSpace.Core;
using ArtSpace.Documents;
using ArtSpace.Skia;
using SkiaSharp;

namespace ArtSpace.Illustration;

public enum SvgPathTextExportMode { Outlines, Editable }

public static class IllustrationSvgExport
{
    /// <summary>
    /// Export a detached snapshot. Type-on-path objects become vector glyph outlines, preserving their
    /// appearance without claiming native SVG textPath/shaping equivalence. The source document and
    /// undo history are untouched. Existing unsupported SVG live effects still fail explicitly.
    /// </summary>
    public static string Export(IEnumerable<DesignNode> roots, RectD bounds, SceneRenderer renderer)
    {
        return Export(roots, bounds, renderer, SvgPathTextExportMode.Outlines);
    }

    /// <summary>Editable mode retains text content and references; glyph metrics depend on the receiving font engine.</summary>
    public static string Export(IEnumerable<DesignNode> roots, RectD bounds, SceneRenderer renderer, SvgPathTextExportMode pathTextMode)
    {
        if (!Enum.IsDefined(pathTextMode)) throw new ArgumentOutOfRangeException(nameof(pathTextMode));
        ArgumentNullException.ThrowIfNull(roots); ArgumentNullException.ThrowIfNull(renderer);
        var copies = new List<DesignNode>();
        foreach (var original in roots)
        {
            var copy = DocumentJson.CloneNode(original);
            NodeGeometry.SetExactMatrix(copy, original.WorldMatrix);
            foreach (var node in copy.DescendantsAndSelf())
            {
                if (node.TextPath is null) continue;
                if (pathTextMode == SvgPathTextExportMode.Editable)
                {
                    PrepareEditablePathText(node, renderer); continue;
                }
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

    private static void PrepareEditablePathText(DesignNode node, SceneRenderer renderer)
    {
        renderer.ValidateTypeOnPath(node);
        var options = node.TextPath!;
        var shift = renderer.GetTypeOnPathBaselineOffset(node);
        if (options.SvgPosition is null)
        {
            if (node.LetterSpacing != 0)
                throw new InvalidOperationException("Native tracked brackets use different character-cell rotation and anchoring than SVG letter spacing. Use outline SVG export to preserve appearance.");
            if (renderer.GetTypeOnPathStatus(node).Overflow)
                throw new InvalidOperationException("Bracket-overflow text needs outline SVG export; editable SVG cannot preserve its whole-glyph clipping policy.");
            using var measure = new SKPathMeasure(renderer.Geometry(node), false, 4);
            using var segment = new SKPath();
            if (!measure.GetSegment((float)(options.Start * measure.Length), (float)(options.End * measure.Length), segment, true))
                throw new InvalidOperationException("The text bracket interval has no SVG baseline.");
            using var reversed = new SKPath();
            if (options.Flip) reversed.AddPathReverse(segment);
            node.PathData = (options.Flip ? reversed : segment).ToSvgPathData();
            node.PathWidth = node.Width; node.PathHeight = node.Height;
            options.Start = 0; options.End = 1; options.Flip = false;
            options.SvgPosition = new() { Percentage = true, Offset = node.TextAlign == TextAlignment.Center ? 50 : node.TextAlign == TextAlignment.Right ? 100 : 0 };
        }
        // Encode metric alignment as a scalar dy, avoiding engine-dependent dominant-baseline rules.
        options.BaselineShift = -shift; options.Alignment = PathTextAlignment.Baseline;
    }
}
