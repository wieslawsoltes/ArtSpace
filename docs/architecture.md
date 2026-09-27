# Architecture

ArtSpace is a shared C# illustration engine with native desktop and WebAssembly hosts. There is no embedded third-party editor, HTML imitation of the workbench or browser-only document engine.

## Package boundaries

`Core` contains document and geometry types; `Layout` provides layout/snapping; `Documents` handles validation and formats; `Editing` owns selection and transactions; `Skia` implements drawing, hit testing and Boolean geometry; `Illustration` adds vector operations. These six packages do not depend on Uno.

`Controls`, `Editor` and `Workbench` have desktop and browser Uno targets. Controls remain independent of document state. The editor is independently embeddable. The workbench combines the editor, controls and storage abstraction. All nine libraries are packable; `ArtSpace.App` supplies platform startup, fonts and persistence.

The shared source derives from the MIT VectorSpace snapshot recorded in `THIRD-PARTY-NOTICES.md`. ArtSpace's illustration operations, controls, artwork and workspace are maintained here. The one-time migration scripts were removed; ordinary builds compile committed source directly.

## Rendering

The drawing canvas derives from Uno's `SKCanvasElement`. `RenderOverride(SKCanvas, Size)` paints directly into the canvas supplied by Uno's Skia composition path. This avoids an extra application-owned CPU bitmap and upload. Vector icons and color-spectrum controls use the same public integration.

SkiaSharp is the portable vector API for antialiased paths, text rasterization, gradients, clipping and compositing. ArtSpace uses Uno's supported Skia renderer, not an independent raw-WebGPU compositor. Backend selection and physical acceleration depend on the host, browser, driver and fallback policy. Software rendering is valid and used in portable CI.

The toolchain pins `.NET SDK 10.0.401`, `Uno.Sdk 6.7.30` and compatible `SkiaSharp 3.119.2` managed/native assets. The stable Uno SDK index was checked on September 27, 2026. Do not independently upgrade managed SkiaSharp to a new major version without updating and testing Uno's native runtime. Compilation alone does not prove ABI compatibility.

`SceneRenderer` owns cached `SKPath` objects. Callers borrow `Geometry(node)` results and must not dispose them. Operations that transform or retain paths create and dispose their own copies. Exports allocate a bounded raster target separately from interactive rendering.

Geometry, hit testing and drawing share parent/local transforms. The renderer applies appearance and clipping recursively. Path construction, Boolean operations, offsets and outline generation use Skia CPU geometry APIs even when final painting is accelerated. This is not a GPU-compute-only engine.

## Document and transactions

A document owns pages, styles and component metadata. Pages own root nodes, guides and pasteboard appearance. Artboards are clipping frame nodes, not independent document sessions. Nodes contain transform, geometry, text and appearance. Native point paths retain anchors and tangents; imported/expanded paths can instead contain SVG path strings.

`EditorSession` owns selection, active page, viewport and history. Commands execute through `Edit` or `UpdateSelection`. Pointer gestures begin an interaction, preview changes and commit one undo item. Cancellation and failed operations restore a snapshot. History is bounded and deterministic but is not optimized for arbitrarily huge documents.

The renderer caches geometry but does not implement a million-object spatially indexed scene engine. Large-document throughput requires profiling representative artwork and hardware; no such throughput claim is made.

## Illustration operations

Stroke expansion uses Skia stroke-to-fill geometry with caps, joins, miters and dashes, retaining visible filled interiors in a group. Offsets union/subtract a stroked boundary with the original path and normalize their resulting geometry/bounds. Blends generate bounded editable intermediates and require compatible primitive kinds or native point topology. Radial repeats preserve parent transforms around a derived pivot. Anchor subdivision uses exact de Casteljau construction.

All operations are real transactional edits, but they are not claimed to reproduce every Illustrator algorithm or live-object behavior. Limits on counts and distances prevent accidental unbounded allocations.

## Controls and input

Custom buttons, icons, numeric scrubbing, color fields, layer rows, menus, panel tabs and resize grips build on Uno's input/layout/text/accessibility primitives. The editor translates pointer and keyboard input into C# transactions and paints selection, anchors, rulers, guides and gradient handles.

Menus maintain their own active-item index and support keyboard execution independently of popup focus timing. They focus their initial item after loading and expose `HandleNavigationKey` for host adapters.

On WebAssembly, a small production `BrowserKeyboard` adapter forwards only navigation keys that browser/Uno focus routing can consume before routed handlers. JavaScript performs no document editing: the workbench's `HandleHostNavigation` decides whether the current menu or canvas owns the key. Text inputs, modifier shortcuts and modal dialogs retain native behavior. Handled keys prevent the browser's default action; ordinary keys continue through Uno. The adapter is active in production and is not a test-only hook.

The workbench is instantiated with an `EditorSession` and `IWorkspaceStorage`; no static document singleton is required. The host owns its lifetime and disposes it on close.

## Storage and trust

`IWorkspaceStorage` separates browser/native persistence. WebAssembly uses a small JavaScript bridge for IndexedDB, local file selection/download and clipboard. Native storage uses application-local data.

Native JSON is validated before activation, including count/depth/size limits, identifiers, finite geometry and bounded appearance values. SVG parsing disables external entities and does not execute scripts. Unsupported elements are reported, not silently advertised as supported. Continue adding adversarial regressions for malformed or excessive geometry.

Autosave is local recovery, not durable cloud synchronization or backup. The app does not upload document contents. Explicit exports write the requested document or image to the user's local workflow.

## Validation and delivery

The engine suite contains **74 regression cases**, including 21 illustration-focused cases. Four browser scenarios drive the real published WebAssembly application under `/ArtSpace/`. They use actual pointer/keyboard events, read-only `?test=1` diagnostics and retained screenshots/traces. No diagnostic mutation API exists.

Browser output includes `build-info.json` for commit identification. Pages deploys successful main-branch Build artifacts and validates the public application. The native matrix compiles Windows, Linux and macOS. Release tags produce tested browser files, native archives, reusable packages and checksums; public NuGet publication needs an explicit secret.

CI Chromium uses software graphics, so it does not certify physical GPU performance, driver behavior or color accuracy. Native compilation is distinct from interactive native validation. See the [feature matrix](feature-matrix.md) for current product boundaries.

## Primary references

- Uno SDK: https://platform.uno/docs/articles/features/using-the-uno-sdk.html
- Uno canvas: https://platform.uno/docs/articles/controls/SKCanvasElement.html
- Browser input: https://platform.uno/docs/articles/features/browser-input-helper.html
- SkiaSharp: https://github.com/mono/SkiaSharp
- Skia license: https://skia.googlesource.com/skia/+/main/LICENSE
- Pages Actions: https://docs.github.com/en/pages/getting-started-with-github-pages/using-custom-workflows-with-github-pages
