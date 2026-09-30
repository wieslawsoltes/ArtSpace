# Type on a Path — 0.6.0-alpha.1

ArtSpace supports editable, tangent-oriented text along one open or closed vector contour. Text and the baseline remain editable until Create Outlines is explicitly invoked. This is the basic path-text workflow, not all Illustrator typography or warp variants.

## Create and edit

Select an unlocked vector object and choose **Type → Type on a Path…**. Enter the text in the prompt. The object keeps its identity and transform; its vector geometry becomes the text baseline. Its fill/stroke appearance is replaced with a solid text fill, while existing object effects remain. Undo restores the original vector object.

To retain an existing text object's typography and appearance, select that text and an unlocked sibling vector contour, then invoke the same command. The text keeps its identity and adopts the baseline's position in the parent and stacking order. The separate baseline object is consumed, reversibly. Active clipping/opacity-mask sources cannot be consumed as baselines.

The **Type on a Path** section in Properties exposes Start, End, alignment, baseline shift, Flip, overflow status, baseline editing, outlines and conversion to ordinary area text. Typography controls continue to edit text, size, weight, tracking and left/center/right alignment. Retained field bindings update their values without reconstructing the section for ordinary option edits.

## Canvas brackets

With the Selection tool (**V**) and one path-text object selected, three brackets are displayed. Start and End set the permitted arc-length interval; the center bracket translates the interval. Moving the center bracket across the baseline flips travel and glyph orientation. Hold Control while dragging the center to suppress that flip. The inspector's Flip checkbox performs the same orientation change explicitly.

A click does not start an undo transaction. Movement beyond three screen pixels starts one interaction; release commits one undo entry, while Escape cancels it. Bracket positions are projected onto the actual measured contour, not a bounding rectangle. Start and End are normalized fractions with `0 ≤ Start ≤ End ≤ 1`. Closed contours include their closing segment, but the interval does not wrap across the seam.

The center bracket moves the selected interval, so a full `[0,1]` interval cannot translate further until a bracket is moved inward. On tight/self-intersecting contours, numerical closest-point projection can switch between nearby segments. The projection table is retained and its local refinement is bounded; it is not an exact intersection solver.

## Alignment, overflow and baseline editing

Baseline/Ascender/Descender/Center align against the supplied font metrics. Positive baseline shift moves toward the glyph ascender. Flip reverses travel and rotates glyphs rather than mirroring their shapes. Left/center/right text alignment is measured inside the Start/End interval.

Characters whose full advance does not fit inside the interval are omitted from visible geometry and reported as overflow. The original text remains unchanged. There is no automatic threading to another contour. Spaces consume advance; tabs and line-break scalars display as spaces on the single-line baseline. This is not paragraph layout.

Choose **Edit baseline anchors** or **Type → Edit Path Baseline** to use the existing Direct Selection controls. Anchor/tangent edits update text layout and preserve text properties. Baseline edits must retain one nondegenerate contour; operations that would split it into multiple contours fail transactionally. Create text outlines before performing compound-glyph operations.

**Convert to area text** discards the baseline but preserves text, typography, appearance and placement. **Create Outlines** instead converts visible glyphs into ordinary editable paths. Both are reversible. Whitespace-only outlines preserve the original editable text, consistent with the existing outline command.

## Reusable APIs

`ArtSpace.Core.TypeOnPathOptions` is a serializable model with no Uno/Skia dependency. `ArtSpace.Skia.MeasuredContour` owns native arc-length measurement and a lazily created projection table. `PathTextLayout` owns placed glyph geometry and exposes scalar/UTF-16 addresses, glyph transforms, advances and overflow status. `TypeOnPathOperations` provides atomic creation, attachment, option updates and conversion.

```csharp
using ArtSpace.Core;
using ArtSpace.Editing;
using ArtSpace.Illustration;
using ArtSpace.Skia;

var baseline = new DesignNode
{
    Kind = NodeKind.Path,
    Width = 480,
    Height = 200,
    PathWidth = 480,
    PathHeight = 200,
    PathData = "M20 180C80 0 370 0 460 180",
    FontSize = 24
};
var session = new EditorSession(new DesignDocument
{
    Pages = [new() { Nodes = [baseline] }]
});
using var renderer = new SceneRenderer();
session.Select(baseline);
TypeOnPathOperations.Create(session, renderer, "ALPINE ECHOES");
TypeOnPathOperations.Update(session, "Adjust path text", options =>
{
    options.Start = 0.1;
    options.End = 0.9;
    options.Alignment = PathTextAlignment.Center;
});
PathTextStatus status = renderer.GetTypeOnPathStatus(baseline);
using var glyphs = renderer.CreateTextOutline(baseline);
// glyphs is an owned copy; the native document is still editable text.
```

Matching Skia native assets are required. Renderer, measurements and layouts belong to a single owning execution context. Dispose objects you own; renderer-managed paths/layouts remain borrowed. `CreateTextOutline` returns an owned copy.

## Rendering and performance

One retained glyph path is shared by painting, hit testing and outline conversion. Glyph templates are built once per distinct scalar within each layout. Baseline measurements are cached separately from typography/options, so bracket and font changes do not remeasure unchanged geometry. Repeated paint or placement changes do not rebuild glyph layout. Pointer projection builds its coarse table only when needed and reuses it thereafter.

Unchanged scenes also reuse ArtSpace's existing native R-tree display list. The new code does not create a bitmap-backed editor surface or a second rendering backend. The host's Skia canvas determines physical acceleration. Text parsing, scalar layout, path measurement and edit transactions remain CPU work.

The path-text caches retain up to 128 entries with approximate 32 MiB glyph-layout accounting. This is not a hard limit on total native/GPU/transient memory. Native text is limited to 8,192 UTF-16 characters; baseline point and output-outline budgets guard excessive geometry. Malformed baselines become retained diagnostics during interactive painting rather than exceptions escaping the render callback. Export rejects such objects explicitly, so invalid text cannot silently disappear into output.

Run the reproducible CPU experiment:

```bash
dotnet run --project tests/ArtSpace.Tests -c Release
dotnet run --project tests/ArtSpace.Tests -c Release --no-build -- --type-on-path-benchmark
```

Build retains `type-on-path.json` alongside the existing benchmark reports. The fixture measures seven alternating-order samples of forty draws on a 512 × 240 software-Skia bitmap. The reference deliberately clears every renderer cache each draw; the warm path retains measurements and glyph geometry. All samples, allocations, rebuild counts and exact pixel comparisons are reported. This is not an older-release, application-FPS, startup or physical-GPU benchmark.

The initial integration run measured 10.9732 ms / 336,320 managed bytes per forced-cold batch versus 6.5371 ms / 1,280 bytes per warm batch, with zero warm baseline/layout rebuilds and exact cache/reference pixels. Source: [run 36633267894](https://github.com/wieslawsoltes/ArtSpace/actions/runs/36633267894), artifact `ArtSpace-type-path-measurements`. Host load and tiering affect timing; final build artifacts are authoritative for later runs.

## Persistence and export

Native saves use **schema 6** and retain text, baseline geometry and options. Schemas 1–6 remain readable. Older versions reject schema 6 instead of silently dropping path-text semantics. Keep an original copy for older-version workflows.

The workbench's SVG export uses `IllustrationSvgExport.Export(roots, bounds, renderer)` on a detached copy, converting path text to vector glyph outlines. This preserves appearance without changing native text or history. The low-level `SvgFormat.Export` rejects unconverted native bracket text. Version 0.7 adds a defined editable SVG textPath subset and an optional editable export command; see [SVG path-text interchange](svg-path-text.md). Full SVG text layout is not claimed. Existing SVG filter/effect limitations continue to apply.

PNG and SVG selection bounds include shifted path-text ink. General live-effect/stroke expansion is not a new full visual-bounds implementation. Cached direct/retained rendering uses exact pixel comparisons; persisted outlined paths can differ at antialiased edges after float coordinate normalization. Regression tests measure a bounded coverage error and also check gradient placement under affine transforms.

## Remaining typography boundaries

The layout enumerates Unicode scalars without splitting surrogate pairs, but does not perform general shaping, ligature/kerning resolution, bidirectional ordering, font fallback or variable-font-axis selection. It supports tangent-oriented glyphs, not Skew/Ribbon/Stair/Gravity or envelope warps. There is no threaded path text, cross-seam interval wrapping or automatic text-flow continuation. Other Illustrator parity gaps remain documented in the [feature matrix](feature-matrix.md).

Reference workflows: [Adobe type on a path](https://helpx.adobe.com/illustrator/using/creating-type-path.html). Native measurement: [SkiaSharp path information](https://learn.microsoft.com/en-us/xamarin/xamarin-forms/user-interface/graphics/skiasharp/curves/information).
