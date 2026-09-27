# Feature matrix — 0.3.0-alpha.1

ArtSpace is an independent illustration editor. Familiar workspace conventions do not imply complete Adobe Illustrator behavior, file compatibility or pixel parity.

| Capability | Status | Boundary |
| --- | --- | --- |
| Shared Uno desktop/browser app | Implemented | One C# engine/workbench; native compilation is distinct from interactive testing |
| Dark illustration workspace | Implemented | Custom menus, control bar, toolbox, artboards and panels; not pixel-identical Illustrator |
| Reusable custom controls | Implemented | Built on Uno input/layout/text primitives |
| GPU-integrated painting | Implemented | SKCanvasElement/Skia, host-dependent acceleration; geometry remains CPU-side |
| Primitives and pen paths | Implemented | Rectangles, ellipses, polygons, stars, lines, arrows and cubic paths |
| Imported/compound anchor editing | Implemented with limits | Multiple contours in one object, including primitives and glyph outlines; rational curves are approximated on editing |
| Multi-anchor editing | Implemented with limits | Shift selection, marquee, dragging, nudging and cancellation; no cross-object anchor selection/lasso |
| Anchor insertion/removal | Implemented | Exact cubic split, reconnecting removal, incident-segment cutting, smooth/corner and reversal |
| Compound paths | Implemented with limits | Make/release, topmost appearance, nonzero/even-odd fills; not live Boolean objects |
| Pencil/paintbrush | Limited | Fixed-width sampled strokes, not pressure/art/pattern/scatter parity |
| Expansion and offsets | Implemented | Shape/stroke expansion and positive/negative offsets |
| Pathfinder | Limited | Union/subtract/intersect/exclude, not a complete divide/trim/crop/live suite |
| Shape Builder, scissors, eraser, knife | Not implemented | Anchor cutting is not a substitute for these tools |
| Transformation | Implemented | Move/resize/rotate/flip, constraints, grouping, order, alignment and distribution |
| Envelope/perspective/mesh distortion | Not implemented | No full distortion toolset |
| Blend and repeat | Limited | Bounded ordinary-object copies and compatible geometry; not live operators |
| Appearance stack | Limited | Multiple fills/strokes, basic gradients/blends/shadows; not all nested Appearance/live-effect semantics |
| Stroke appearance | Implemented | Width, dashes, caps, joins and miter; no variable-width profiles |
| Vector clipping masks | Implemented with limits | Editable mask/content, nested sets, compound holes, picking, Make/Release and persistence; masks must be direct vector children |
| Opacity/luminance masks | Not implemented | Vector clipping does not implement transparency-mask semantics |
| SVG clipping | Limited | Single vector/compound-path `userSpaceOnUse` definitions; missing/external/recursive/unsupported definitions rejected; existing transform/paint-server limitations remain |
| Artboards | Implemented | Multiple frame-based artboards, editing/navigation/export |
| Multiple documents | Not implemented | One active session; artboards are not separate documents |
| Layers | Implemented | Hierarchy, search, visibility/locking, selection, rename and order |
| Docking | Limited | Resizable right tab group; no arbitrary floating/cross-window layout |
| Symbols | Limited | Linked local components/instances with remapped clipping references; not full dynamic symbol semantics |
| Basic text | Limited | Size/weight, wrapping, tracking/alignment; not advanced shaping |
| Create Outlines | Implemented with limits | Same glyph runs as painting, retained identity/appearance/transforms and undo; whitespace preserved |
| Advanced typography | Not implemented | No general complex-script/bidi/fallback, type-on-path, glyph panel or variable-font axes |
| Image placement/tracing | Not implemented | No links manager, Image Trace or raster editor |
| RGB and PNG | Implemented | Screen-oriented, not press-ready output |
| CMYK/ICC/spot/overprint/separations | Not implemented | No color-proofing/print-production claims |
| Native `.artspace` | Implemented | Read schemas 1/2; saves upgrade to schema 2, which older versions reject |
| SVG interchange | Limited | Safe editable subset, not lossless Illustrator roundtrip |
| AI/EPS/PDF interchange | Not implemented | Do not rename native files to these extensions |
| Undo/redo | Implemented | Bounded snapshots, one transaction per completed gesture, rollback retained until serialization succeeds |
| Local recovery | Implemented | IndexedDB/native storage, not cloud backup |
| Retained caches | Implemented | Exact geometry snapshots, cached text/fonts and selection arrays, deleted-resource pruning |
| Indexed snapping | Implemented with limits | Reference-equivalent stationary-target queries; cold construction costs and conservative auto-layout rebuilds remain |
| Offscreen culling | Implemented with limits | Conservative leaf rejection; groups/shadows/text handled conservatively; hierarchy traversal still occurs |
| Million-object GPU compute | Not implemented | No large-document throughput guarantee or physical-GPU benchmark |
| Collaboration/Adobe plugins/cloud | Not implemented | No Adobe service integration |

## Validation interpretation

There are 141 registered engine cases, five untimed benchmark boundary checks and ten browser scenarios. Browser tests use real Uno pointer/keyboard/file-picker input for ordinary editing, imported contours, text outlines, clipping, recovery and warm-cache retention. Workflow results and retained reports identify the tested commit.

The benchmark verifies reference snapping corrections/guide extents and culling pixel equality. Single-run software-Skia CPU timings and managed allocations do not certify whole-app speed, startup time, physical-GPU throughput or color accuracy. Native compilation is not interactive native certification.

See [path editing](path-editing.md) and [clipping/performance](clipping-and-performance.md). Significant remaining work includes opacity masks, richer typography, endpoint joining/simplification, complete live-shape/effect tools, print interchange, multiple documents, configurable docking and pressure-sensitive brushes.
