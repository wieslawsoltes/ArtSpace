# Feature matrix — 0.2.0-alpha.1

ArtSpace is an independent illustration editor. Similar tools and workspace conventions are not a claim of complete Adobe Illustrator behavior, file compatibility or pixel parity.

| Capability | Status | Boundary |
| --- | --- | --- |
| Shared Uno desktop/browser application | Implemented | Same C# engine/workbench; interactive native testing is separate from compilation |
| Dark illustration workspace | Implemented | Menus, control bar, toolbox, artboards, panels and status; not pixel-identical Illustrator |
| Reusable custom controls | Implemented | Build on Uno input/layout/text primitives, not replacements for every primitive |
| GPU-integrated painting | Implemented | SKCanvasElement/Skia, host-dependent acceleration; geometry work remains CPU-side |
| Vector primitives | Implemented | Rectangles, ellipses, polygons, stars, lines and arrows |
| Pen and cubic paths | Implemented | Native anchors/tangents and SVG path geometry |
| Imported/compound anchor editing | Implemented with limits | Multiple contours within one object, including primitives, imported SVG, expanded/Boolean paths and outlined glyphs; rational curves are approximated on editing |
| Multi-anchor editing | Implemented with limits | Shift selection, rectangular marquee, drag, nudge and cancellation within the active object; no cross-object anchor editing or lasso |
| Anchor insertion/removal | Implemented | Exact cubic split, explicit reconnecting removal, Delete/Backspace segment cutting, smooth/corner and reversal |
| Compound paths | Implemented with limits | Make/release, topmost appearance, nonzero/even-odd rules; not live Boolean compound objects |
| Pencil/paintbrush | Limited | Fixed-width sampled strokes, not pressure/art/pattern/scatter brush parity |
| Expansion and offsets | Implemented | Shape expansion, stroke outlines and positive/negative offsets |
| Pathfinder | Limited | Union, subtract, intersect, exclude; no complete divide/trim/merge/crop/live suite |
| Shape Builder, scissors, eraser, knife | Not implemented | Anchor cutting is not a substitute for these interaction tools |
| Transformation | Implemented | Move, resize, rotate, flips, constraints, nudging, grouping and stacking |
| Envelope/perspective/mesh distortion | Not implemented | No full distortion toolset |
| Blend and repeat | Limited | Bounded ordinary-object copies and compatible geometry; not live non-destructive operators |
| Appearance stack | Limited | Multiple fills/strokes, not all nested Illustrator Appearance semantics |
| Gradients | Limited | Linear/radial and on-canvas direction; no freeform or gradient mesh |
| Stroke appearance | Implemented | Width, dashes, caps, joins and miter; no variable-width profiles |
| Opacity/blend/shadows | Limited | Supported Skia blends and basic drop shadows, not full transparency/live-effect semantics |
| Clipping | Limited | Frame/artboard clipping, not arbitrary opacity masks or clipping-path workflows |
| Artboards | Implemented | Multiple frame-based artboards, editing, navigation and export |
| Multiple documents | Not implemented | One active session; artboards are not document tabs |
| Layers | Implemented | Hierarchy, search, visibility, locking, rename, selection and order |
| Docking | Limited | Resizable right tab group; no arbitrary floating or cross-window layout |
| Symbols | Limited | Local linked components/instances/overrides, not full dynamic symbol semantics |
| Text | Limited | Basic text, size/weight, wrapping, tracking and alignment; not advanced shaping |
| Create Outlines | Implemented with limits | Glyph paths from the same basic text runs as painting, preserved identity/appearance/transforms and undo; whitespace retained |
| Advanced typography | Not implemented | No general bidi/complex-script shaping, font fallback, type-on-path, glyph panel or variable-font axes |
| Image placement/tracing | Not implemented | No links manager, Image Trace or raster editor |
| RGB and PNG | Implemented | Screen-oriented, not press-ready color-managed output |
| CMYK/ICC/spot/overprint/separations | Not implemented | No print-production/proofing claims |
| Native `.artspace` | Implemented | Validated JSON, including path fill rules |
| SVG interchange | Limited | Editable subset with inherited fill rules and unsupported-element reporting; not lossless Illustrator roundtrip |
| AI/EPS/PDF interchange | Not implemented | Do not rename native files to these extensions |
| Undo/redo | Implemented | Bounded snapshots, one transaction per completed gesture |
| Local recovery | Implemented | IndexedDB/native storage, not cloud backup or synchronization |
| Collaboration/Adobe plugins/cloud | Not implemented | No Adobe service or remote document integration |
| Million-object GPU compute | Not implemented | No throughput claim or physical-GPU benchmark |

## Validation interpretation

The engine suite has **107 cases**. Seven browser scenarios exercise real Uno rendering and input, including imported contours, text outlines, anchor marquee/nudge/cancellation, file download/recovery, ordinary editing and menu navigation. Workflow results and artifacts identify the tested commit.

A screenshot proves its captured frame, not every command. Native compilation does not certify every native interaction. Software graphics in CI does not certify GPU throughput or color accuracy. Report issues with a minimal `.artspace`/SVG reproduction and the deployed commit in `build-info.json`.

Detailed numerical, interaction and typography limits are documented in [Path editing and text outlines](path-editing.md). Significant remaining areas include arbitrary masks, richer typography, endpoint joining/simplification, complete live-shape/effect workflows, print interchange, multiple documents, configurable docking and pressure-sensitive brushes.
