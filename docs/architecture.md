# Architecture

ArtSpace is a shared C# illustration engine with native desktop and WebAssembly hosts. There is no embedded third-party editor, HTML imitation of the workbench or browser-only document engine.

## Package boundaries

```text
ArtSpace.Core
  ├─ ArtSpace.Layout
  └─ ArtSpace.Documents
       └─ ArtSpace.Editing
            └─ ArtSpace.Skia
                 ├─ ArtSpace.Illustration
                 └─ ArtSpace.Editor ── ArtSpace.Controls
                        └─ ArtSpace.Workbench
                             └─ ArtSpace.App
```

The diagram summarizes responsibilities rather than every direct project reference. `Core`, `Layout`, `Documents`, `Editing`, `Skia` and `Illustration` do not depend on Uno. `Controls`, `Editor` and `Workbench` have browser and desktop Uno targets. All nine libraries are packable; the application supplies platform startup, fonts, storage and the workbench instance.

The source is adapted from the MIT-licensed VectorSpace snapshot identified in `THIRD-PARTY-NOTICES.md`. ArtSpace-specific operations, artwork, controls and workspace are maintained here. The initial migration scripts are not part of ordinary consumer builds.

## Rendering

The editor derives its drawing canvas from Uno's `SKCanvasElement`. `RenderOverride(SKCanvas, Size)` paints directly into the canvas supplied by Uno's Skia composition path. This avoids the additional application-owned CPU bitmap and upload required by a bitmap-backed canvas. Vector icons and color-spectrum controls use the same public Uno drawing integration.

SkiaSharp is the portable vector rendering API. Its permissive ecosystem provides antialiased paths, text rasterization, gradients, clipping, blend modes and Boolean geometry on desktop and browser. The current release uses Uno's supported Skia integration rather than a separate raw WebGPU compositor. The actual graphics backend and hardware acceleration depend on the host, browser, driver and fallback policy. A software graphics context is valid and is used in portable CI.

The toolchain pins `.NET SDK 10.0.401`, `Uno.Sdk 6.7.30` and the compatible `SkiaSharp 3.119.2` managed/native family. The latest stable Uno SDK was queried from NuGet on September 27, 2026. Do not independently upgrade managed SkiaSharp to a new major version without updating and testing Uno's matching native runtime. A successful compilation alone cannot prove ABI compatibility.

`SceneRenderer` owns cached `SKPath` instances. Callers of `Geometry(node)` borrow the returned path and must not dispose it. Operations that transform or retain geometry create their own `SKPath` copy and dispose it deterministically. Exports allocate a bounded raster target separately from interactive rendering.

Geometry, hit testing and drawing share node transforms. The document uses affine matrices and parent references rebuilt after deserialization. The renderer applies local transforms, appearance and clipping recursively; hit testing follows the same hierarchy. Path operations currently run on CPU through Skia's geometry API even when final painting is accelerated. This is not a GPU-compute-only engine.

## Document and editing model

A `DesignDocument` owns pages, styles and component metadata. A `DesignPage` owns root nodes, guides and its pasteboard appearance. Artboards are frame nodes with clipping; they are not separate document sessions. Nodes contain transform, geometry, text and appearance information. Native point-based paths retain anchors and tangent handles; imported or expanded paths can contain SVG path data instead.

`EditorSession` owns selection, active page, viewport and history. Public illustration operations execute through `Edit(label, action)` or `UpdateSelection`. Pointer gestures use `BeginInteraction`, preview mutations and a final commit so a drag generates one undo item rather than one item per pointer event. Cancellation restores the transaction snapshot. Failed edits roll back.

History uses bounded document snapshots. This makes edits deterministic and straightforward to validate but is not optimized for arbitrarily huge documents. The current renderer's path cache reduces repeated path construction; it is not a million-object spatially indexed scene engine. Large-document performance needs profiling on representative artwork and hardware.

## Illustration operations

`ArtSpace.Illustration` extends the shared editor without depending on the workbench:

- Stroke expansion uses Skia's stroke-to-fill geometry with cap, join, miter and dash settings; filled interiors and object appearance are retained in a group.
- Offset paths combine or subtract a stroked boundary with the original fill geometry.
- Blends generate bounded, ordinary editable objects and require compatible primitive kind or native point topology.
- Radial repeats transform copies while preserving their parent coordinate systems.
- Anchor subdivision uses de Casteljau construction; reverse and smooth/corner conversion operate on native points.

These are implemented commands, not promises of Illustrator's complete geometry semantics. They are deliberately bounded to avoid accidental unbounded allocations from UI inputs.

## Controls and workspace

`ArtSpace.Controls` exposes compact custom buttons, vector icons, numeric scrubbing, color fields, layer rows, command menus, panel tabs and resize grips. They build on Uno input, layout, text and accessibility primitives; this release does not replace every Uno primitive or native text-input implementation.

`ArtSpace.Editor` translates pointer and keyboard interactions into document edits and draws selection, anchors, rulers, guides and gradient handles. It can be embedded independently of the full shell.

`ArtSpace.Workbench` combines the menus, appearance controls, toolbar, canvas, property/layer/artboard/history panels and file commands. It receives an `EditorSession` and an `IWorkspaceStorage`; no static document singleton is required.

```csharp
var session = new EditorSession(IllustrationSample.Create());
var workbench = new StudioWorkbench(session, storage);
window.Content = workbench;
```

The host owns lifetime and should dispose the workbench when closing it. The renderer and timers are instance-owned.

## Storage and trust boundary

`IWorkspaceStorage` separates the application from browser/native persistence. WebAssembly uses a small JavaScript bridge for IndexedDB, file selection/download and clipboard transfer. Native storage uses the application's local data directory.

Native documents are validated before use. Validation includes size/count/depth limits, identifiers, finite geometry and supported appearance values. SVG parsing disables external entities and does not execute scripts. Unsupported elements are reported instead of treating the format as fully supported. Imported artwork remains untrusted input; malformed geometry and excessive complexity must continue to receive regression coverage.

Recovery is local and not a backup service. The app does not upload document content. Explicit exports transfer only the current requested document or rendered result to the user's chosen local workflow.

## Build, tests and deployment

Engine regressions run without a window and exercise real Skia paths and pixel output. Browser tests serve the actual published WebAssembly directory under `/ArtSpace/`, send real pointer/keyboard input, validate state and retain screenshots/traces. An opt-in `?test=1` bridge exposes read-only diagnostics; it does not expose a mutation API.

Build artifacts contain the exact tested browser output and `build-info.json` for commit identification. GitHub Pages uses an Actions deployment, not committed generated application assets. Desktop builds are separately validated for Windows, Linux and macOS. Release workflows package tagged source, browser files, desktop output and libraries with checksums; public NuGet publishing needs an explicitly configured secret.

CI uses Chromium's software graphics path and therefore cannot certify physical GPU throughput, color output or every driver/browser combination. Native compilation is distinct from interactive native runtime validation.

## Primary references

- Uno SDK: https://platform.uno/docs/articles/features/using-the-uno-sdk.html
- Uno SKCanvasElement: https://platform.uno/docs/articles/controls/SKCanvasElement.html
- SkiaSharp: https://github.com/mono/SkiaSharp
- Skia license: https://skia.googlesource.com/skia/+/main/LICENSE
- GitHub Pages Actions: https://docs.github.com/en/pages/getting-started-with-github-pages/using-custom-workflows-with-github-pages
- Illustrator workspace reference: https://helpx.adobe.com/illustrator/desktop/get-started/learn-the-basics/workspace-overview.html
