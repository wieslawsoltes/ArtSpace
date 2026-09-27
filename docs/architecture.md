# Architecture

ArtSpace shares C# source across native Uno desktop and WebAssembly hosts. There is no embedded third-party editor or parallel JavaScript document engine.

## Package boundaries

Six libraries are independent of Uno: `Core` owns document/geometry/editable contours; `Layout` handles constraints/snapping; `Documents` validates and serializes formats; `Editing` owns transactions and selection; `Skia` renders and converts geometry; `Illustration` exposes editing commands. `Controls`, `Editor` and `Workbench` target desktop/browser Uno. All nine are packable.

Controls do not own document state. The editor surface can be embedded separately from the full workbench. The host supplies `EditorSession`, `IWorkspaceStorage`, fonts and lifetime/disposal. Source provenance is recorded in `THIRD-PARTY-NOTICES.md`.

## Rendering

The canvas derives from Uno `SKCanvasElement` and paints into Uno's shared Skia composition path. It does not maintain an additional application-owned CPU framebuffer for interactive painting. SkiaSharp supplies paths, clipping, gradients, text, compositing and raster export. Actual backend/hardware acceleration depends on the host/browser/driver; software rendering is valid.

The pinned stack is .NET SDK 10.0.401, Uno SDK 6.7.30 and compatible SkiaSharp 3.119.2 managed/native assets. Do not independently replace the managed Skia version without updating and validating Uno's matching native runtime. Compile-time compatibility is not ABI validation.

`SceneRenderer` owns cached paths. `Geometry(node)` returns a borrowed object; callers that transform or retain it must copy it. `CreateTextOutline(node)` returns an owned path. The geometry cache includes path fill rule and dimensions. Rendering and hit testing use the same transform hierarchy and fill behavior.

Path construction, Boolean operations, conic conversion, offsets and stroke expansion run on CPU even when final painting is accelerated. This is not a raw-WebGPU or GPU-compute-only engine. Path caching is not a million-object spatial index or throughput guarantee.

## Contours and typography

`EditablePath` is a temporary managed editing buffer, not a second persisted document tree. It contains independent open/closed contours with anchors and optional cubic handles. The native document retains legacy pen points or SVG path data plus a nonzero/even-odd fill rule.

`PathEditing.Read` iterates actual Skia verbs. Lines and cubics are preserved, quadratics are elevated exactly, and rational conics are approximated by bounded adaptive cubic subdivision. Closing duplicate endpoints are merged while retaining their incoming handle. Hit testing uses bounded adaptive subdivision and segment projection.

`PathEditing.Write` normalizes the resulting geometry, re-expresses the node transform and gradient endpoints, and preserves world placement. Its optional basis snapshot supports absolute pointer updates without cumulative normalization drift. Managed coordinates are double precision, but Skia geometry is float-based. See [precision and limits](path-editing.md#precision-and-limits).

Painting and text outlining share text runs, including baseline, wrapping, alignment and tracking. The line breaker uses prefix advances and Unicode scalar offsets rather than repeatedly measuring a growing line. This remains a basic text implementation, not a complete shaping/bidi/fallback engine. Outlining preserves the node identity and appearance but removes its editable text semantics; undo restores the original.

## Transactions and input

`EditorSession` owns active page, selection, viewport and bounded snapshot history. Commands use `Edit` or `UpdateSelection`. Pointer gestures begin once, preview mutations and commit once. Cancellation and exceptions restore the snapshot. Entering anchor selection without editing does not convert primitives or create history.

The direct-selection surface maintains contour/anchor addresses outside the persisted document. It supports multi-anchor translation, local-axis constraints, world-coordinate nudging, tangent alignment with opposite-length preservation, Alt-independent handles, insertion, marquee and cancellation. Explicit anchor removal reconnects neighbors; Delete/Backspace cuts incident segments and can produce multiple open contours. One object's contours are edited at a time.

Custom menus keep an active-item index rather than relying on asynchronous popup focus timing. A small production browser adapter forwards navigation keys when browser focus routing consumes them before Uno events. JavaScript performs no document mutation; C# decides ownership. Text inputs and modal dialogs retain native behavior. The adapter is not test-only.

## Storage and trust

WebAssembly uses IndexedDB, local file selection/download and clipboard through an `IWorkspaceStorage` bridge; native storage uses application-local data. Autosave is recovery, not a backup service, and document contents are not uploaded.

Native JSON is validated before activation, with depth/count/size limits, identifiers, finite geometry and supported appearance values. SVG parsing disables external entities and does not execute scripts. Unsupported elements are reported. Editable contours reject inverse/unbounded fill types and enforce count/subdivision limits. Continue expanding malformed-input and excessive-complexity coverage.

## Validation and delivery

107 engine cases include exact-subdivision properties, conic samples, tangent behavior, fill-rule roundtrips, transformed bounds, glyph coverage and rollback. Seven browser scenarios drive the actual published Uno app under `/ArtSpace/` with pointer, keyboard and file-picker input. Opt-in `?test=1` diagnostics expose read-only state and anchor positions, never mutation commands.

Build compiles Windows/Linux/macOS, publishes browser output, packs all libraries and retains tests/screenshots. Pages deploys successful main-branch artifacts, verifies `build-info.json` and tests the public URL. Tagged releases generate browser/native/source archives, packages and checksums; NuGet publication needs an explicit secret. CI software graphics is not physical-GPU validation, and native compilation is not interactive runtime certification.

Primary references: [Uno SKCanvasElement](https://platform.uno/docs/articles/controls/SKCanvasElement.html), [Uno SDK](https://platform.uno/docs/articles/features/using-the-uno-sdk.html), [SkiaSharp](https://github.com/mono/SkiaSharp), [Skia license](https://skia.googlesource.com/skia/+/main/LICENSE), and [GitHub Pages custom workflows](https://docs.github.com/en/pages/getting-started-with-github-pages/using-custom-workflows-with-github-pages).
