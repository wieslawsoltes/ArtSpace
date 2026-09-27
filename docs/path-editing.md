# Path editing and text outlines

Available in ArtSpace 0.2.0-alpha.1. These commands run in the same C# libraries on desktop and WebAssembly.

## Direct editing

Select artwork and press **A**, double-click a vector object, or choose **Object → Edit Anchors**. Primitive shapes, imported SVG path strings, expanded/Boolean paths, compound paths and outlined glyphs can all expose their anchors. Entering direct selection is read-only: the object becomes a normalized path only when geometry is actually changed.

Hollow squares indicate unselected anchors; filled squares indicate selected anchors. Click an anchor and drag to move it with its attached handles. Shift-click toggles an anchor in the selection. Drag a marquee on empty canvas around anchors in the active object; Shift-drag adds to the current selection. Press Escape to clear the anchor selection. Ctrl A selects all anchors of the active path rather than all document objects.

Dragging a selected anchor moves the selected anchor set. Arrow keys move selected anchors by one document pixel; Shift-arrow moves by ten. Nudging is expressed in world coordinates, so the direction stays consistent for rotated/flipped objects and nested parents. Shift-drag constrains anchor movement to the dominant local axis.

A direction-handle drag aligns the opposite handle while retaining its length. Alt-drag edits the handle independently. **Shift C** chooses the Anchor Point tool: click a handled anchor to make a corner, or drag from a corner to create tangents. Smooth and Corner controls also operate on the selected anchors.

The Add Anchor Point tool (**+**) inserts an anchor on a segment. Alt-click a segment in direct-selection mode is an additional insertion gesture. Insertion subdivides the original cubic rather than fitting a replacement curve, including curved closing segments. The Object menu's Add Anchor Points subdivides every segment of every contour in one transaction.

## Remove versus cut

The Remove Anchor Point tool (**−**) and **Remove selected anchors** reconnect the remaining neighbors. Delete/Backspace removes selected anchors **and their incident segments**, leaving disconnected runs as separate open contours. Alt-Delete performs reconnecting removal. This distinction matters: pressing Delete is not equivalent to removing a redundant point from an otherwise continuous path.

Escape during a drag cancels the entire gesture. A completed drag, a multi-anchor nudge, a compound operation or text conversion produces one undo transaction. Merely selecting anchors does not change geometry or create a history entry.

## Compound paths and holes

Choose **Object → Make Compound Path** for two or more unlocked vector siblings. The result takes the topmost selected object's appearance and preserves its stacking position relative to unselected siblings. Multiple contours share that appearance. The initial compound uses the even-odd fill rule so nested contours can create holes.

Choose **Release Compound Path** to create separate editable objects from its contours. Releasing a compound can change its visual appearance because a formerly subtractive hole becomes its own filled object; Undo restores the original compound.

The Properties panel exposes **Nonzero** and **Even-odd** fill rules. The rule is retained in native JSON and SVG `fill-rule`, used by Skia drawing/hit testing, and included in geometry cache identity. SVG group-level fill rules are inherited. Open contours remain independent; serialization never creates an accidental line between separate contours.

## Create text outlines

Select text, or a group containing text, and choose **Type → Create Outlines** or **Ctrl Shift O**. ArtSpace obtains glyph paths from its configured Skia font and uses the same text runs for painting and outline generation. Tracking, wrapping, alignment and baseline placement therefore follow the editor's current text layout.

The conversion preserves the node's identifier, world placement, fill/stroke appearance, opacity and parent. Gradients are re-expressed after bounds normalization so their original local positions are retained. Whitespace-only text is left unchanged. The result is vector geometry: press A to edit its glyph contours, and export SVG without a font dependency for that converted text. Undo restores the editable text.

Outlining is destructive with respect to text semantics. Keep an editable original or save a copy before distributing outlined artwork. The conversion does not introduce an advanced text-shaping engine: complex scripts, bidirectional layout, font fallback, variable-font axes and full OpenType behavior remain outside the current text implementation.

## Library API

`ArtSpace.Core.EditablePath` contains managed contours, anchors, tangents, subdivision, hit testing and selection operations without Skia or Uno dependencies. `ArtSpace.Skia.PathEditing` translates Skia geometry to/from that buffer. `ArtSpace.Illustration.PathOperations` exposes transaction-level commands.

```csharp
using ArtSpace.Core;
using ArtSpace.Illustration;
using ArtSpace.Skia;

// session and renderer are owned by the embedding host.
var node = session.Primary
    ?? throw new InvalidOperationException("Select a path first.");
var path = PathEditing.Read(node, renderer);
var anchor = path.Split(new EditablePath.Address(0, 0), 0.5);
path.Translate([anchor], new Vec2(8, 0));
session.Edit("Split and move anchor", () => PathEditing.Write(node, path));

// Text objects selected by the host can be converted separately:
// PathOperations.CreateOutlines(session, renderer);
```

`SceneRenderer.Geometry(node)` returns a borrowed cached path: do not dispose it. `CreateTextOutline` returns an owned `SKPath`: dispose it. PathEditing.Write accepts an optional pre-gesture node snapshot so repeated absolute pointer updates do not accumulate normalization drift.

## Precision and limits

Managed anchors use doubles and round-trip numeric formatting. Skia paths and rasterization use float geometry, so persistence through Skia is not a promise of arbitrary-precision CAD coordinates. Quadratic Béziers are elevated exactly to cubics. Rational conics are converted adaptively to cubics using a sampled local error criterion, with a default tolerance of 0.01 local units and bounded subdivision. This is not a formal global Hausdorff-error guarantee.

Editing is limited to 65,536 anchors per path and conic subdivision depth 16. Inverse/unbounded Skia fill types are rejected. Text outlining is limited to 100,000 characters per object. Failures roll back the transaction rather than leaving partly converted artwork.

The direct editor currently edits one object's contours at a time. It does not implement cross-object anchor selection, lasso selection, curve simplification, general endpoint joining, pressure/width profiles or the full Illustrator path tool suite.

## Verification

The engine suite has 107 cases, including deterministic subdivision property checks, rational curves, tangent length, fill rules, transformed normalization, glyph coverage and transaction rollback. Browser scenarios import SVG through the actual file picker and exercise anchors, insertion, native save, text outlines, marquee/nudge, cancellation and undo/redo. Workflow conclusions—not this feature description—identify which commit passed.

Primary API/interaction references: [SkiaSharp](https://github.com/mono/SkiaSharp), [SKFont.GetTextPath](https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skfont.gettextpath), and [Illustrator path editing](https://helpx.adobe.com/illustrator/using/editing-paths.html). ArtSpace is an independent implementation, not an Adobe product.
