# Changelog

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

### Boundaries

One object's contours are edited at a time. Rational geometry is approximated on editing. Text outlines reproduce ArtSpace's current basic text layout; this does not add advanced shaping, bidi, font fallback or variable-font axes. AI/EPS/PDF, color-managed print production, masks, meshes and full live effects remain unimplemented.

## 0.1.0-alpha.1 — 2026-09-27

Initial independent illustration editor derived from the author's MIT VectorSpace engine. Added the shared Uno desktop/WebAssembly app, dark illustration workspace, original Alpine Echoes artwork, nine reusable libraries, vector drawing/manipulation, stroke/shape expansion, offsets, blends, repeats, Pathfinder, gradients, local persistence/recovery, SVG/PNG workflows and build/Pages/release automation.

The initial alpha was not full or pixel-identical Illustrator parity. Hardware acceleration depends on host/driver, and software-rendered CI is not a physical GPU benchmark.
