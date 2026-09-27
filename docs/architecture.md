# Architecture

ArtSpace shares C# source across native Uno desktop and WebAssembly hosts. There is no embedded third-party editor or parallel JavaScript document engine.

## Package boundaries

Six libraries are independent of Uno: `Core` owns document/geometry/editable contours; `Layout` handles constraints/snapping; `Documents` validates and serializes formats; `Editing` owns transactions/selection; `Skia` renders and converts geometry; `Illustration` exposes editing/clipping commands. `Controls`, `Editor` and `Workbench` target desktop/browser Uno. All nine are packable.

Controls do not own document state. The surface is embeddable without the full workbench. The host supplies `EditorSession`, `IWorkspaceStorage`, fonts and disposal. Provenance is recorded in `THIRD-PARTY-NOTICES.md`; no temporary source-migration workflow is required for consumer builds.

## Rendering and ownership

The canvas derives from Uno `SKCanvasElement` and paints into Uno's shared Skia composition path. It does not maintain an additional application-owned CPU framebuffer for interactive painting. Backend/hardware acceleration depends on the host/browser/driver. The pinned stack is .NET SDK 10.0.401, Uno SDK 6.7.30 and compatible SkiaSharp 3.119.2 managed/native assets. Compile-time compatibility is not native ABI validation.

`SceneRenderer` owns cached paths. `Geometry(node)` is borrowed: copy before transforming and do not dispose it. `CreateTextOutline(node)` returns an owned path. The exact geometry snapshot includes geometry parameters, path dimensions/data, native points/tangents and fill rule; hits do not rebuild SVG text. Pruning removes deleted IDs without discarding unchanged native geometry. Cached text runs own fonts and are invalidated by typography/typeface changes rather than paint/position changes.

Clipping transforms the mask's filled geometry into its container's local coordinates and intersects the canvas clip before drawing content. The mask itself is not painted in Preview. Compound fill rules and the same transformed geometry are used in picking. Outline mode reveals mask and hidden contours. A container's own fill remains independent of its child clipping region.

Culling uses the actual canvas clip and conservatively inflated leaf geometry, not an arbitrary fixed world-space viewport margin. Groups may overflow their nominal bounds; text and filtered/shadow subtrees are kept conservative. The full hierarchy is still visited. CPU geometry operations and scene traversal remain; this is not a raw-WebGPU compute engine or million-object throughput guarantee.

## Contours and text

`EditablePath` is a temporary managed contour/anchor/tangent buffer, not a second persisted document tree. The document retains pen points or SVG path data and fill rules. `PathEditing.Read` preserves lines/cubics, elevates quadratics exactly and adaptively approximates rational conics with bounded subdivision. `Write` normalizes geometry while re-expressing transforms/gradient endpoints. A pre-gesture basis avoids accumulation of normalization drift. Managed anchors are doubles; Skia geometry is float-based.

Painting and outlining share text runs for baseline, wrapping, alignment and tracking. Line breaking uses Unicode scalar offsets and prefix advances. This remains a basic text engine, not complete complex-script shaping, bidi, fallback or variable-font support. See [path editing](path-editing.md).

## Transactions, selection and snapping

Commands use `EditorSession.Edit` or `UpdateSelection`. Gestures begin once, preview and commit once. Cancellation restores the original snapshot. The snapshot is retained until commit serialization succeeds, so serialization errors remain reversible. History uses bounded document snapshots rather than fine-grained edit deltas.

Selection/root arrays are reused for geometry previews and viewport changes. Structural changes, selection changes and document notifications invalidate them. Embedding hosts making direct model changes must use the session notification/transaction contract.

`SnapIndex` captures stationary target rectangles at drag start and indexes their edge/center coordinates by axis. Queries preserve the exhaustive reference's ordering, tie-breaking and guide extents. Index construction is `O(N log N)`; a query is `O(log N + G)` for live guides. Auto-layout documents rebuild conservatively because other target bounds may move during preview. The target data are retained; diagnostic counters and editor sessions are intended for their owning execution context.

## Clipping model and commands

`ClipPathId` references a direct vector child; the `ClippingPath` helper resolves it. `ClippingOperations` implements Make, Release, EditMask and EditContents without an Uno dependency. Make retains geometry, creates/reuses a clipping container, removes mask appearance and preserves node identities/stacking. Release removes the relation while retaining the group and unpainted mask. Active clipping paths cannot be regrouped into a different parent without releasing them first.

Validation rejects dangling/non-child/text/container mask references. Clone/paste and component synchronization remap mask IDs. Deleting a clipping path releases its owner's reference. Mask-anchor edits remain ordinary contour transactions.

## Input and storage

Direct selection maintains active contour/anchor addresses outside persisted data. It supports multiple anchors within one object, marquee, nudging, direction handles, insertion/removal and cancellation. Custom menus maintain an active index independent of popup focus timing. The production browser adapter forwards selected input/modifiers; C# decides ownership. Text inputs and modal dialogs retain their normal behavior.

WebAssembly uses IndexedDB and local file/clipboard bridges behind `IWorkspaceStorage`; desktop uses application-local data. Autosave is recovery, not backup, and document contents are not uploaded.

Native schema 1 remains readable; saves upgrade to schema 2. The version guard stops older readers from silently ignoring clipping. Native validation enforces bounded size/count/depth and geometry/appearance/reference invariants. SVG parsing disables external entities and never executes imported scripts. Supported clipping is a local `userSpaceOnUse` definition with one vector shape/compound path. Missing, external, recursive or unsupported definitions fail closed. General SVG transform/paint-server/opacity-mask limits remain.

## Validation and delivery

The engine runner has 141 cases. The benchmark harness additionally checks schema migration, serialization rollback, mask ownership and miter picking before measuring. Ten browser scenarios drive the real published Uno app under `/ArtSpace/`. Diagnostics are read-only; no test-only document mutation API exists.

Build validates native compilation, engine/benchmark behavior, package generation and browser acceptance. Pages verifies artifact commit provenance and public-site acceptance. Tagged releases run browser tests and create native/browser/source/package archives; NuGet publication needs explicit credentials. See [clipping/performance](clipping-and-performance.md) for reproducible fixtures and interpretation. Software graphics is not physical-GPU validation; compilation is not native interactive certification.

Primary references: [Uno canvas](https://platform.uno/docs/articles/controls/SKCanvasElement.html), [Uno SDK](https://platform.uno/docs/articles/features/using-the-uno-sdk.html), [SkiaSharp](https://github.com/mono/SkiaSharp), [Skia license](https://skia.googlesource.com/skia/+/main/LICENSE), [SkCanvas](https://api.skia.org/classSkCanvas.html), and [GitHub Pages workflows](https://docs.github.com/en/pages/getting-started-with-github-pages/using-custom-workflows-with-github-pages).
