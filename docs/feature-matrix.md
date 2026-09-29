# Feature matrix — 0.5.0-alpha.1

ArtSpace is an independent illustration editor. Similar workspace conventions do not imply complete Illustrator behavior, native format compatibility or pixel parity.

| Capability | Status | Boundary |
| --- | --- | --- |
| Shared Uno desktop/browser application | Implemented | One C# engine/workbench; native compilation differs from interactive certification |
| Dark custom workspace | Implemented | Menus, controls, toolbox, artboards and right panel group; not a pixel-identical skin |
| Reusable controls/libraries | Implemented | Nine packages, built on Uno input/layout/text primitives |
| GPU-integrated painting | Implemented | SKCanvasElement/Skia, host-dependent acceleration; geometry remains CPU-side |
| Primitive and pen drawing | Implemented | Shapes, lines, arrows and cubic paths |
| Direct contour editing | Implemented with limits | Native/imported/compound/expanded/glyph geometry; one object's contours at a time; rational conic approximation |
| Multi-anchor tools | Implemented with limits | Marquee, drag/nudge, handles, insertion, removal/cutting, smoothing; no cross-object anchor lasso or full path toolkit |
| Pathfinder/compounds | Limited | Union/subtract/intersect/exclude and make/release compound paths; no complete live Shape Builder/divide/trim suite |
| Expansion/offset | Implemented | Shape/stroke outlines and positive/negative offsets |
| Pencil/paintbrush | Limited | Fixed-width sampled strokes, not pressure/art/pattern/scatter brush parity |
| Transforms | Implemented with limits | Move/resize/rotate/flip and retained affine skew/group scale; no full perspective/envelope workflow |
| Linear/radial gradients | Implemented with limits | Stop opacity, overall opacity, spread, coordinates, affine transform, radial center/focus/radius; no freeform/mesh gradients |
| On-canvas gradients | Implemented with limits | Imported coordinate spaces respected; radial drag resets focus; no independent focal handle |
| Appearance stack/effects | Implemented with limits | Ordered object-level blur, shadow, glow and saturation; sixteen blends; independent fill/stroke ordering; no arbitrary interleaved per-paint effects or complete Illustrator effect catalog |
| Graphic Styles | Implemented with limits | Document-local independent appearance presets, native previews, apply/rename/delete and twelve-slot paging; not globally linked styles or Adobe library interchange |
| Gradient/dashed strokes | Implemented with limits | Linear/radial stroke paint, stop opacity, spread, dash phase and odd-pattern normalization; no variable-width profiles or art/pattern brushes |
| Vector clipping | Implemented with limits | Editable direct-child mask, holes, nesting, picking and supported SVG subset |
| Alpha/luminance masks | Implemented with limits | Retained editable source, nesting, mode, inversion, enable/release; explicit-source selection rather than full Illustrator isolation/thumbnail UI |
| SVG mask interoperability | Limited | Supported multi-object user-space source masks and regions; no object-box mask units, active/image/use content or general filters; inverted SVG export rejected |
| SVG paint interoperability | Limited | Local linear/radial fill definitions, inheritance/stops, spread, focus and affine coordinates; gradient strokes and dash phase supported; no linear-light interpolation or complete CSS cascade |
| Affine interchange | Implemented with limits | Nested group scaling/skew/reflection preserved, strict transform syntax; singular transforms rejected; Skia uses float geometry |
| Artboards | Implemented | Multiple frame-based artboards with editing/navigation/export |
| Multiple documents | Not implemented | One active document session |
| Docking | Limited | Resizable right panel tabs, no arbitrary native/browser floating windows |
| Layers | Implemented | Hierarchy, filtering, visibility/locking, selection and ordering |
| Symbols | Limited | Linked local components, mask remapping and explicit full appearance overrides/reset; not full dynamic symbols |
| Basic typography | Limited | Wrapping, tracking/alignment, configured Skia fonts |
| Create Outlines | Implemented with limits | Shared glyph layout, retained identity/appearance/placement and undo; no advanced shaping upgrade |
| Advanced typography | Not implemented | Complex-script/bidi/fallback certification, variable axes, glyph panel and type-on-path remain absent |
| Raster placement/tracing | Not implemented | No links manager, Image Trace or raster editor |
| RGB/PNG | Implemented | Screen-oriented, not press production |
| CMYK/ICC/spot/overprint | Not implemented | No print proofing or separations guarantee |
| Native ArtSpace persistence | Implemented | Read schemas 1–4; new saves schema 4; previous readers reject new schema |
| SVG interchange | Limited | Editable safe subset, not lossless Illustrator roundtripping |
| AI/EPS/PDF interchange | Not implemented | No native format parity claim |
| Undo/local recovery | Implemented | Bounded snapshot transactions, IndexedDB/native storage; not a cloud backup |
| Retained rendering caches | Implemented | Exact geometry/text/gradient caches plus retained paints, dashes, filters and selection snapshots; owned-resource pruning |
| Retained native scene | Implemented with limits | Vector SKPicture playback into the host canvas; separate overlays, explicit mutation invalidation, 32 MiB approximate command-storage budget; recording/edits remain CPU-side |
| Retained property panels | Implemented | Visible-panel routing, topology-keyed sections, current-target bindings, input ownership and incremental layer rows |
| Indexed snapping | Implemented with limits | Reference-equivalent stationary targets; cold build and auto-layout rebuild costs remain |
| Conservative culling | Implemented with limits | Leaf painting skipped; full hierarchy still visited; text/shadows conservative |
| Affine bounds hot path | Implemented | No per-call managed corner arrays; exact property tests |
| Large-document GPU compute | Not implemented | No million-object throughput or physical-GPU guarantee |
| Collaboration/Adobe services/plugins | Not implemented | Local-first application, no Adobe integration |

## Validation

247 registered engine cases, five additional benchmark safety checks and 21 real Uno browser scenarios are defined. Build and Pages reports determine which commit passed, including new mask source movement, recovery, SVG radial/affine metadata, gradient manipulation and shader retention. Retained screenshots are evidence of captured frames, not complete visual parity.

Benchmarks compare exact reference snapping/bounds and output pixels. The legacy appearance report records five warmed CPU samples. The new retained-scene report alternates measurement order over seven samples, checks exact reference pixels, and verifies unchanged native resources are not rebuilt. Both use software Skia; scene recording cost is excluded. Cleared-cache comparisons intentionally force rebuilds and are not old-release application FPS measurements. Native build success does not certify every native interaction.

Guides: [live appearance/rendering](appearance-rendering.md), [UI responsiveness](ui-performance.md), [path editing](path-editing.md), [clipping/performance](clipping-and-performance.md), [opacity masks/gradients](opacity-masks-and-gradients.md).
