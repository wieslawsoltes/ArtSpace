# Feature matrix — 0.1.0-alpha.1

This is an implementation boundary, not a claim of complete Adobe Illustrator compatibility. ArtSpace is an independent editor with original assets. Similar menus, tools and visual conventions do not imply identical algorithms, file formats, rendering or workflow parity.

| Capability | Status | Boundary |
| --- | --- | --- |
| Shared Uno desktop/browser application | Implemented | Same C# workbench/engine; interactive native testing remains separate from compilation |
| Dark illustration workspace | Implemented | Menus, control bar, tools, artboards, panels and status bar; not pixel-identical Illustrator |
| Reusable custom Uno controls | Implemented | Built on Uno layout/input/text primitives, not replacements for every primitive |
| GPU-integrated painting | Implemented | Uno SKCanvasElement/Skia; host-dependent backend; geometry operations remain CPU-side |
| Basic vector primitives | Implemented | Rectangles, ellipses, polygons, stars, lines and arrows |
| Pen and cubic paths | Implemented | Native points/tangents and SVG path geometry |
| Direct anchor selection | Limited | Native point paths; no general imported SVG-string-to-anchor editor |
| Pencil and paintbrush | Limited | Sampled fixed-width strokes, not pressure/art/pattern/scatter brush parity |
| Shape/anchor operations | Implemented | Expansion, stroke outlines, offsets, subdivision, smooth/corner conversion and reversal |
| Pathfinder | Limited | Union, subtract, intersect, exclude; no complete divide/trim/merge/crop/live-compound suite |
| Shape Builder, scissors, eraser, knife | Not implemented | Pathfinder is not a substitute for these interaction tools |
| Transformation | Implemented | Move, resize, rotate, flips, constrained drags, nudge, grouping and stacking |
| Free distortion, envelope, perspective | Not implemented | No perspective grid, mesh or envelope deformation |
| Blend and repetition | Limited | Bounded ordinary-object copies, compatible geometry and derived radial pivot; not live operators |
| Multiple fills/strokes | Implemented | Basic appearance stack, not full nested Illustrator Appearance semantics |
| Gradients | Limited | Linear/radial fills and direction editing; no freeform or gradient mesh |
| Stroke appearance | Implemented | Width, dashes, caps, joins and miter settings in model/renderer |
| Variable-width strokes | Not implemented | No width profiles or control points |
| Opacity and blend modes | Limited | Supported Skia subset, not full transparency/knockout parity |
| Shadows | Limited | Basic drop shadows, not a general live-effect pipeline |
| Clipping | Limited | Artboard/frame clipping, not arbitrary opacity masks or clipping-path workflows |
| Artboards | Implemented | Multiple frame-based artboards, create/select/resize/navigation/export |
| Multiple open documents | Not implemented | One active session; artboards are not document tabs |
| Layers | Implemented | Hierarchy, filter, selection, visibility, locking, rename and ordering |
| Docking | Limited | Resizable right tab group; no arbitrary floating or cross-window docking |
| Symbols | Limited | Local linked components/instances/overrides, not full dynamic symbol semantics |
| Text | Limited | Basic point/box text, size, weight, alignment and wrapping; no advanced shaping certification |
| Type on a path, glyph panel, variable fonts | Not implemented | No full OpenType/typographic production parity |
| Text outlines | Not implemented | Editable text remains text; no advertised Create Outlines workflow |
| Image placement/tracing | Not implemented | No bitmap links manager, Image Trace or raster editor |
| RGB editing and PNG export | Implemented | Screen-oriented, not color-managed press production |
| CMYK/ICC/spot colors/overprint/separations | Not implemented | No press-ready output or proofing claims |
| Native ArtSpace document | Implemented | Validated JSON with version and bounded input |
| SVG interchange | Limited | Safe editable subset with unsupported-element reporting, not lossless Illustrator roundtrip |
| AI/EPS/PDF interchange | Not implemented | Do not rename native files to these extensions |
| Undo/redo | Implemented | Bounded snapshot transactions, one undo item per completed gesture |
| Local recovery | Implemented | IndexedDB/native local storage, not a cloud backup service |
| Keyboard/menu navigation | Implemented | C# menu state plus browser navigation adapter; reserved OS/browser shortcuts may still take precedence |
| Collaboration, Adobe cloud/plugins | Not implemented | No Adobe integration or remote document service |
| Large-document GPU-compute processing | Not implemented | No million-object throughput guarantee or hardware benchmark |

## Validation interpretation

The suite has **74 engine cases**, including 21 illustration-focused regressions. Four browser scenarios cover real Uno startup/rendering, primitive editing, nudge, undo/redo, download/recovery, pen/gradient/zoom/artboards, panel/compact layout and keyboard-driven menu expansion. Workflow conclusions and retained artifacts are authoritative for each commit.

A screenshot proves its captured frame, not every command. A native build proves compilation, not every native interaction. Chromium software graphics does not certify physical-GPU throughput. Report bugs with a minimal `.artspace`/SVG reproduction and the deployed commit from `build-info.json`.

## Substantial remaining parity work

Highest-value gaps include full imported-path anchor editing, richer typography/text outlines, arbitrary masks, complete Boolean/live-shape workflows, accurate color-managed interchange, multiple documents, configurable docking, brush dynamics and broader adversarial/performance testing. These need implementation and validation rather than placeholder menu entries.
