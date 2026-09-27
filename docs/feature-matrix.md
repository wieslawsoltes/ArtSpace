# Feature matrix — 0.1.0-alpha.1

This is an implementation boundary, not a promise of complete Adobe Illustrator compatibility. ArtSpace is an independent editor with original assets and code. Similar menus, tools and visual conventions do not imply identical algorithms, file formats, rendering or workflow parity.

| Capability | Status | Boundary |
| --- | --- | --- |
| Shared Uno desktop/browser application | Implemented | Same C# workbench and engine; native runtime behavior still needs platform-specific interaction testing |
| Dark illustration workspace | Implemented | Menu, appearance bar, fixed tool palette, artboards, panels and status bar; not a pixel-identical Illustrator skin |
| Custom reusable Uno controls | Implemented | Custom menus, tabs, fields, icons and layer rows built on Uno primitives; not every primitive is replaced |
| GPU-integrated painting | Implemented | Uno SKCanvasElement and Skia; backend/hardware depends on host; geometry operations remain CPU-side |
| Basic vector primitives | Implemented | Rectangles, ellipses, polygons, stars, lines and arrows |
| Pen and cubic paths | Implemented | Native point-based paths and SVG path geometry |
| Direct selection | Limited | Editable anchors/tangents on native point paths; no general imported SVG-to-anchor editor |
| Pencil and paintbrush | Limited | Sampled fixed-width strokes; no pressure dynamics, art/pattern/scatter brushes or brush library parity |
| Shape/anchor operations | Implemented | Expansion, stroke outline, offsets, subdivision, smooth/corner conversion and reversal |
| Pathfinder | Limited | Union, subtract, intersect, exclude; no complete divide/trim/merge/crop/live compound-shape suite |
| Shape Builder, scissors, eraser, knife | Not implemented | No claim that Pathfinder substitutes for these tools |
| Transformation | Implemented | Move, resize, rotate, flips, constrained drags, keyboard nudge, grouping and stacking |
| Free distortion, envelope, perspective | Not implemented | No perspective grid, mesh deformation or envelope distortion |
| Repetition and blend | Limited | Bounded ordinary-object copies; same-kind/compatible-point blends, derived radial pivot; not live non-destructive operators |
| Multiple fills/strokes | Implemented | Basic appearance stack; no full nested Illustrator Appearance panel semantics |
| Gradients | Limited | Linear/radial fills and on-canvas direction; no freeform gradients or gradient meshes |
| Stroke appearance | Implemented | Width, dash patterns, caps, joins and miter settings in the model/renderer |
| Variable-width strokes | Not implemented | No width profiles or width-tool control points |
| Opacity and blend modes | Limited | Supported Skia blend subset; no full transparency/knockout/isolated-group parity |
| Shadows | Limited | Basic drop shadows; no general Illustrator live-effect pipeline |
| Clipping | Limited | Artboard/frame clipping; no arbitrary opacity-mask or clipping-path workflow |
| Artboards | Implemented | Multiple frame-based artboards, create/select/resize/navigation/export |
| Multiple open documents | Not implemented | One active session; artboards are not separate documents |
| Layers | Implemented | Hierarchical view, search, selection, visibility, locking, rename and ordering |
| Docking | Limited | Resizable right tab group; no arbitrary panel docking/floating across native/browser windows |
| Symbols | Limited | Local linked components/instances and overrides; no full dynamic symbol semantics |
| Text | Limited | Basic point/box text, font size/weight/alignment and wrapping; no advanced shaping/layout certification |
| Type on a path, glyph panel, variable fonts | Not implemented | No full OpenType or typographic production parity |
| Text outline conversion | Not implemented | Editable text remains text; no advertised Create Outlines workflow |
| Image placement and tracing | Not implemented | No bitmap links manager, Image Trace or raster editing |
| RGB editing and PNG export | Implemented | Screen-oriented rendering; not a color-managed print pipeline |
| CMYK/ICC/spot colors/overprint/separations | Not implemented | No press-ready output or color-proofing claims |
| Native ArtSpace document | Implemented | Validated JSON with version and bounded input |
| SVG interchange | Limited | Safe editable subset with unsupported-element reporting; not lossless Illustrator roundtrip |
| AI/EPS/PDF import and export | Not implemented | Do not rename native files to these extensions |
| Undo/redo | Implemented | Snapshot transactions, bounded history, one transaction per completed gesture |
| Local recovery | Implemented | IndexedDB/native local storage; not a cloud backup or durable synchronization service |
| Collaboration, Adobe cloud, plugins | Not implemented | No Adobe integration or remote document service |
| Large-document GPU-compute processing | Not implemented | No million-object throughput guarantee or hardware benchmark |

## Validation interpretation

The engine regression suite currently contains 71 cases, including geometry/serialization/transactions and 18 illustration-focused cases. Browser acceptance covers startup and actual Uno rendering, primitive creation, nudging, undo/redo, saving/recovery, tool conventions, pen/gradient interaction, zoom, artboard creation and compact layout. The workflow result and retained artifacts are authoritative for whether a particular commit passed.

A screenshot proves that the captured frame rendered, not that every command or product feature is correct. A native build proves compilation, not every native interaction. Chromium software graphics tests do not certify physical GPU performance. Bugs and omissions should be filed with a minimal `.artspace` or SVG reproduction and the deployed commit from `build-info.json`.

## Next substantial parity work

Highest-value gaps are full imported-path anchor editing, richer typography and text outlines, arbitrary clipping/masks, complete Boolean/live shape workflows, accurate color-managed interchange, multiple documents, configurable docking, brush dynamics and broader adversarial/performance testing. Those need actual implementation and verification rather than menu placeholders.
