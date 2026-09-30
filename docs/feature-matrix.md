# Feature matrix — 0.6.0-alpha.1

ArtSpace is an independent illustration editor. Familiar workspace conventions do not imply complete Illustrator behavior, native format compatibility or pixel parity.

| Capability | Status | Boundary |
| --- | --- | --- |
| Shared Uno desktop/browser application | Implemented | One C# engine/workbench; native compilation differs from interactive certification |
| Dark custom workspace | Implemented | Custom menus, toolbox, artboards and right panel group; not a pixel-identical skin |
| Reusable controls/libraries | Implemented | Nine packages using Uno input/layout/text primitives |
| GPU-integrated painting | Implemented | SKCanvasElement/Skia; physical acceleration depends on host/driver; geometry remains CPU-side |
| Primitive and pen drawing | Implemented | Shapes, lines, arrows and cubic paths |
| Direct contour editing | Implemented with limits | Native/imported/compound/expanded/glyph geometry and text baselines; one object at a time; rational conic approximation |
| Multi-anchor tools | Implemented with limits | Marquee, drag/nudge, handles, insertion, removal/cutting and smoothing; no cross-object anchor lasso |
| Pathfinder/compounds | Limited | Union/subtract/intersect/exclude and compounds; no complete live Shape Builder/divide/trim suite |
| Expansion/offset | Implemented | Shape/stroke outlines and positive/negative offsets |
| Pencil/paintbrush | Limited | Fixed-width sampled strokes; no pressure/art/pattern/scatter parity |
| Transforms | Implemented with limits | Move/resize/rotate/flip, retained affine skew/group scale; no full perspective/envelope workflow |
| Linear/radial gradients | Implemented with limits | Opacity, stops, spread, coordinates, affine transforms, center/focus/radius; no freeform/mesh gradients |
| On-canvas gradients | Implemented with limits | Imported spaces respected; radial drag resets focus; no independent focal handle |
| Appearance and effects | Implemented with limits | Ordered object-level blur/shadow/glow/saturation, sixteen blends and paint ordering; no arbitrary per-paint effect graph |
| Graphic Styles | Implemented with limits | Local independent presets, vector previews, apply/rename/delete and paged controls; no Adobe library interchange |
| Gradient/dashed strokes | Implemented with limits | Gradient paint, stop opacity, spread, dash phase and odd-pattern normalization; no variable width/art/pattern brushes |
| Vector clipping | Implemented with limits | Editable direct-child mask, holes, nesting, picking and supported SVG subset |
| Alpha/luminance masks | Implemented with limits | Editable source, nesting, inversion, enable/release; no complete Illustrator isolation/thumbnail UI |
| SVG masks/paints | Limited | Supported user-space source masks and gradient definitions; no object-box mask units, inverted-mask export, arbitrary filters, linear-light interpolation or complete CSS cascade |
| Affine interchange | Implemented with limits | Group scale/skew/reflection, strict syntax; singular matrices rejected; Skia geometry is float-based |
| Artboards | Implemented | Multiple frame-based artboards with editing/navigation/export |
| Multiple documents | Not implemented | One active document session |
| Docking | Limited | Resizable right tabs; no arbitrary floating workspaces |
| Layers | Implemented | Hierarchy, filtering, visibility/locking, selection and ordering |
| Symbols | Limited | Local linked components, mask remapping, text and appearance overrides; no full dynamic symbol semantics |
| Basic typography | Limited | Configured fonts, wrapping, tracking and alignment; no general shaping/bidi/fallback certification |
| Type on a Path | Implemented with limits | One open/closed contour, create/attach, editable baseline, Start/End/center brackets, flip, metric alignment, baseline shift and overflow |
| Path-text threading/warps | Not implemented | No cross-seam wrapping, multiple-contour flow or Skew/Ribbon/Stair/Gravity variants |
| Create Outlines | Implemented with limits | Shared glyph geometry/layout, retained identity/appearance/placement and undo; no advanced shaping upgrade |
| Advanced typography | Not implemented | Variable-font axes, glyph panel, general ligature/kerning resolution, complex-script/bidi and fallback |
| Raster placement/tracing | Not implemented | No links manager, Image Trace or raster editor |
| RGB/PNG | Implemented | Screen-oriented, not press production |
| CMYK/ICC/spot/overprint | Not implemented | No proofing or separations guarantee |
| Native ArtSpace persistence | Implemented | Read schemas 1–5; saves schema 5; older readers reject new semantics |
| SVG path-text interchange | Limited | Workbench exports detached glyph outlines without mutating text; native SVG textPath import/export remains unsupported |
| Other SVG interchange | Limited | Safe editable subset, not lossless Illustrator roundtrip; unsupported live effects rejected |
| AI/EPS/PDF interchange | Not implemented | No native format parity claim |
| Undo/local recovery | Implemented | Bounded transactions and IndexedDB/native recovery, not cloud backup |
| Retained rendering caches | Implemented | Exact geometry/text/gradient/paint/filter/selection caches with owned-resource pruning |
| Retained path typography | Implemented | Cached glyph layout, separate native arc-length measurement and lazy projection table; approximate bounded accounting |
| Native scene replay | Implemented with limits | R-tree SKPicture playback, separate overlays and direct editing; recording remains CPU work |
| Retained panels | Implemented | Visible-panel routing, topology-keyed sections, current-target bindings and incremental layer rows |
| Indexed snapping | Implemented with limits | Stationary-target equivalence; cold construction and dynamic-layout rebuilds remain |
| Conservative culling | Implemented with limits | Leaf painting skipped; hierarchy still visited, text/shadows conservative |
| Affine bounds | Implemented | No per-call corner arrays; tested against corner reference |
| Large-document GPU compute | Not implemented | No million-object or physical-GPU throughput guarantee |
| Collaboration/Adobe plugins/services | Not implemented | Local-first, no Adobe service integration |

## Validation

The repository registers 301 engine cases, five additional benchmark safety checks and 24 real Uno browser scenarios. Workflow conclusions and artifacts identify the tested commit; the existence of tests alone is not a claim that a particular build passed.

Type-on-path tests cover measurement/projection, alignment, flip, Unicode scalar indices, overflow, transactions, caches, baseline editing, invalid input, symbols, gradients, export bounds and outlines. Browser scenarios exercise real menu/prompt input, retained field values, bracket manipulation, cancellation, baseline editing, outlines and recovery.

Cache/replay reference comparisons require exact output pixels. Serialized outlines normalize float coordinates and use bounded coverage/color-error tests; this is distinct from exact cached replay. The CPU reports retain all samples and rebuild counts. Forced-cold comparisons deliberately rebuild caches and are not older-release or physical-GPU FPS comparisons. Screenshots show captured frames, not complete UI parity; native compilation is not interactive certification.

Guides: [Type on a Path](type-on-path.md), [Appearance/rendering](appearance-rendering.md), [UI responsiveness](ui-performance.md), [path editing](path-editing.md), [clipping/performance](clipping-and-performance.md), [opacity/gradients](opacity-masks-and-gradients.md).
