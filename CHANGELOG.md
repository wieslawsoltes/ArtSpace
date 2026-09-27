# Changelog

## 0.3.0-alpha.1 — 2026-09-27

### Added

- Editable vector clipping sets with Make/Release, mask/content selection, nested rendering, compound holes, clipping-aware picking and stable child references.
- Native/cloned/clipboard/component clipping reference preservation and safe SVG single-shape/compound-path `userSpaceOnUse` clipping interchange.
- Schema 2 native saves with legacy schema 1 reading. Older readers reject the new schema rather than silently dropping clipping behavior.
- Exact retained geometry-cache snapshots, cached text/font layouts and selection/root lists, and incremental deleted-resource pruning.
- Sorted-axis stationary-target snapping indexes with exhaustive-reference-equivalent tie-breaking and guide extents.
- Conservative nested-leaf culling against the actual canvas clip, retaining overflowing groups and offscreen shadow sources.
- 34 additional engine regressions (141 total), five untimed benchmark boundary checks and three browser scenarios (ten total).
- CPU timing/allocation reports with snapping/pixel-equivalence checks retained by Build. Build, Pages and Release run the complete browser suite.
- Clipping/performance documentation, schema compatibility guidance and refreshed feature boundaries.

### Correctness

- Retain rollback snapshots until commit serialization succeeds.
- Reject regrouping an active clipping path into a different parent until its existing mask is released.
- Include miter reach in picking bounds and preserve mask identities during symbol synchronization.
- Keep unchanged geometry cached during object movement and appearance changes.

### Boundaries

Vector clipping is not opacity/luminance masking. SVG clipping supports one vector shape or compound path per local `userSpaceOnUse` definition; unsupported definitions fail closed. Existing SVG transform/paint-server limits remain. Performance reports measure CPU microbenchmarks on software Skia, exclude cold index construction from query timings and make no physical-GPU or whole-app frame-rate claim.

## 0.2.0-alpha.1 — 2026-09-27

### Added

- Managed, renderer-independent editable contours with double-coordinate anchors and tangent handles.
- Direct anchor editing for primitives, imported SVG, compound paths, Boolean/expanded geometry and outlined glyphs.
- Multi-anchor selection/marquee, drag/nudge, exact cubic insertion, reconnecting removal, incident-segment cutting, smooth/corner conversion and cancellation.
- Quadratic elevation and bounded rational-conic-to-cubic conversion.
- Nonzero/even-odd fill rules across native JSON, SVG inheritance/export, renderer caching, hit testing and geometry results.
- Make/Release Compound Path using the topmost selected appearance and preserved sibling stacking.
- Create Outlines with shared rendering/layout runs, stable identifiers, preserved appearance/transforms and undo.
- 33 additional engine regressions (107 total) and three additional browser scenarios (seven total).
- A path-editing/outline guide and refreshed architecture, README and feature boundaries.

### Correctness

- Pointer updates use a pre-gesture basis instead of accumulating normalized-coordinate drift.
- Moving a direction handle aligns its opposite while preserving the opposite length; Alt allows independent edits.
- Closing cubic contours retain the first anchor's incoming handle and do not introduce links between contours.
- Text fill, stroke and outlines share layout; tracking uses Unicode scalar offsets and a linear prefix-advance line breaker.
- Whitespace-only and locked text are not destructively converted.

### Boundaries at this version

One object's contours are edited at a time. Rational geometry is approximated on editing. Text outlines reproduce the basic text layout, without advanced shaping, bidi, font fallback or variable-font axes. AI/EPS/PDF, color-managed print production, masks, meshes and full live effects were not implemented in 0.2.

## 0.1.0-alpha.1 — 2026-09-27

Initial independent illustration editor derived from the author's MIT VectorSpace engine. Added the shared Uno desktop/WebAssembly app, dark workspace, original Alpine Echoes artwork, nine reusable libraries, vector drawing/manipulation, stroke/shape expansion, offsets, blends, repeats, Pathfinder, gradients, local recovery, SVG/PNG workflows and build/Pages/release automation.

The initial alpha was not full or pixel-identical Illustrator parity. Hardware acceleration depends on host/driver; software-rendered CI is not a physical-GPU benchmark.
