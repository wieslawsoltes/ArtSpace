# Architecture

ArtSpace shares one C# document, geometry and workbench implementation across native Uno and WebAssembly hosts. JavaScript adapts local file/storage/input capabilities; it does not implement a parallel editor.

## Packages and ownership

`Core`, `Layout`, `Documents`, `Editing`, `Skia` and `Illustration` are independent of Uno. They own the model/managed contours, constraints/snapping, validated formats, transactions, rendering/conversion and illustration commands respectively. `Controls`, `Editor` and `Workbench` build reusable Uno components on top. The thin application supplies storage, fonts, startup and lifetime. All nine libraries are packable; the host disposes its workbench/renderer.

`SceneRenderer.Geometry` returns a borrowed cached path. Copy before mutating and never dispose the borrowed result. Text-outline creation returns an owned path. Geometry snapshots avoid SVG key allocation; text runs retain owned fonts; exact fill snapshots retain gradient shaders. Pruning removes dead entries and renderer disposal releases native resources. A local-matrix shader may alias its source, so its ownership cannot be modeled as unconditional disposal of a temporary shader.

## Rendering and masks

The editor derives from Uno `SKCanvasElement` and uses the shared Skia composition path. There is no additional application-owned CPU framebuffer for interactive painting. Actual graphics backend/hardware acceleration depends on the host/browser/driver. The pinned .NET/Uno/Skia versions remain ABI-coordinated. CPU path operations and traversal remain; this is not a raw-WebGPU engine.

Vector clipping uses transformed filled mask geometry. Opacity masks isolate content and composite the retained source into a separate layer, applying alpha or post-composite luma through DstIn; inversion uses the complementary DstOut operation. Source artwork is omitted from normal child painting but retained in the model. Nested masks work through the same recursive renderer. Optional mask regions and finite export bounds constrain work.

Opacity hit testing samples source coverage through a reusable one-pixel CPU surface, avoiding GPU readback. This is an editor picking policy and can still traverse a complex mask source. Explicit source selection permits manipulating its bounds even when its mask contribution conceals all content.

Leaf culling uses actual canvas clip bounds and conservative stroke/antialias margins. Groups may overflow their boxes, and text/shadow subtrees remain conservative. All hierarchy nodes are still visited; the optimization avoids expensive painting/resource work, not O(N) traversal itself.

## Coordinates and paints

Nodes expose editable placement and an optional residual affine transform. Exact imported group scale and skew are retained before placement, rather than being decomposed only into width/height/rotation. World matrices compose through parents. Affine rectangle bounds use signed edge extrema with no temporary corner arrays.

Gradient data distinguishes legacy normalized endpoints, object-bounding-box coordinates and user coordinates. Stops retain opacity separately from fill opacity. Radial center/radius/focus, spread and gradient transforms participate in cache identity. On-canvas tools use the renderer's coordinate mapping. Contour normalization preserves legacy endpoints and re-expresses imported paint transforms to maintain visual locations.

`EditablePath` is a managed double-coordinate temporary buffer, not a competing persisted scene. Skia paths remain float-based. Quadratics elevate exactly; rational conics use bounded adaptive approximation. Text painting/outlines share basic glyph runs, not a complete complex-script/bidi/fallback engine.

## Transactions and document references

Commands use `EditorSession.Edit` and gestures begin once, preview, then commit/cancel. Rollback remains available through commit serialization. Snapshot history is bounded but still incurs full-document work. Selection/root snapshots are invalidated on structural/document/selection changes and reused for unrelated previews/viewport changes.

`ClipPathId` and `OpacityMaskId` point to eligible direct children, validated before activation. Clone/clipboard/component synchronization remap both. Deleting a source releases its reference; active sources cannot be silently regrouped into other owners. Mask/gradient/affine properties persist with the ordinary document.

`SnapIndex` stores sorted stationary target coordinates, preserving reference ordering/ties/guide extents. Queries are O(log N + G), with O(N log N) construction. Auto-layout documents rebuild conservatively when previews can move targets. Editor and renderer instances are owned by their UI/execution context, not advertised as concurrently mutable.

## Files, trust and compatibility

Native saves use schema 6; schemas 1–6 remain readable. Earlier releases reject schema 6 rather than losing new semantics. Computed inverse/bounds properties are excluded from JSON. Validation enforces geometry, resource-count, depth, mask-reference and paint invariants. Local recovery is not backup; no document upload service exists.

SVG parsing prohibits DTD/external entities and never executes imported scripts. Local gradient references are bounded and cycle checked. Mask import supports explicitly documented user-space source/region semantics; unsupported definitions fail closed. Export uses finite mask regions and precise affine numbers. Inverted mask SVG export remains an explicit error. General filters, image/use sources, CSS cascade, gradient strokes and native Illustrator formats are outside this subset.

## Input and validation

Custom menus keep navigation state independent of popup focus timing. Production browser input adaptation forwards only owned events/modifiers, leaving normal text/modal editing alone. Real Playwright tests use file-picker, pointer and keyboard events; opt-in diagnostics expose state but no mutation API.

195 engine tests and five benchmark safety checks cover the shared libraries. Thirteen browser scenarios exercise the published app. Build compiles Windows/Linux/macOS, publishes WebAssembly, packages libraries, measures CPU/reference equivalence and retains test artifacts. Pages checks commit provenance and public acceptance. Release builds single-file desktop executables and package outputs; tags publish packages to NuGet.org through Trusted Publishing. No temporary source-integration or toolchain-export workflows are part of ordinary builds.

Read [opacity/gradient design](opacity-masks-and-gradients.md) and [clipping/performance](clipping-and-performance.md) for measurements and limits. CI uses software graphics and native compilation is not native interaction certification. Primary integration references are [Uno SKCanvasElement](https://platform.uno/docs/articles/controls/SKCanvasElement.html), [SkiaSharp](https://github.com/mono/SkiaSharp), [CSS Masking](https://www.w3.org/TR/css-masking-1/) and [SVG paint servers](https://www.w3.org/TR/SVG2/pservers.html).


## Retained path typography

Schema 5 adds `DesignNode.TextPath` options while keeping text as a Text node and persisting its owned baseline in PathData/PathWidth/PathHeight. Schemas 1–5 remain readable. `TypeOnPathOperations` provides reversible commands; `MeasuredContour` owns one native path measure and a lazy projection table; `PathTextLayout` owns the positioned glyph path. Renderer keys compare exact options/typography/geometry. Font changes clear owned resources; ordinary paint and placement changes preserve layout.

Drawing, picking and outlines share glyph geometry. Direct Selection edits the baseline, while Selection exposes lazy-transaction text brackets. The existing scene recorder can retain settled output. Arc length, scalar layout and editing remain CPU work; backend hardware selection is unchanged.

Interactive invalid-baseline failures become cached diagnostics, and exports reject them explicitly. `IllustrationSvgExport` clones requested roots and outlines path text without changing document/history. Normalized float serialization may differ at antialiased edges, so outline interchange uses bounded coverage tests; cached/direct replay is tested with exact pixels. See [ownership, performance and limitations](type-on-path.md).

## SVG anchor layout and direct glyph append

See [editable SVG path text](svg-path-text.md). Core retains `SvgTextPathPosition`; Documents indexes local references without taking a Skia dependency. The renderer distinguishes SVG anchor/midpoint/seam layout from native whole-glyph bracket layout. Its exact cache identity covers positioning data and supplemental baseline transforms. Glyph construction appends transformed paths without temporary per-glyph native wrappers. The optional editable SVG writer preserves text; the default export remains detached outlines.
