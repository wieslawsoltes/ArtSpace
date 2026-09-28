# Changelog

## 0.4.1-alpha.1 — selection and panel responsiveness

- Retain inspector sections and update values rather than recreating controls on every selection and edit.
- Coalesce UI refreshes and defer hidden Layers, Artboards and History panels; preserve section expansion and input state.
- Reconcile layer rows incrementally and batch large collection changes; reuse row visuals and icon geometry.
- Start transform and anchor transactions only after a three-pixel drag threshold; avoid whole-document snapshots and snap-index builds for selection clicks.
- Resolve selections through a revision-invalidated ID/order index, preserve scene ordering, and suppress no-op selection notifications.
- Add UI input-ownership guards, arbitrary opacity synchronization, browser control-value regressions and selection-latency reports.


## 0.4.0-alpha.1 — 2026-09-28

### Added

- Editable alpha/luminance opacity masks with retained vector/text/group source artwork, nesting, inversion, enable/disable, release and independent source/content selection.
- Transparency controls and Object-menu actions. Explicit source selection permits canvas movement even where mask coverage is zero.
- Persistent mask references through JSON validation, undo, cloning, clipboard and component synchronization.
- Supported SVG opacity-mask import/export with user-space units, regions, local reference checks and finite exported mask bounds.
- SVG linear/radial gradient fills with local inheritance, stop opacity, object-box/user-space coordinates, spread, transforms and radial center/focus/radius.
- On-canvas imported-gradient editing and preserved gradient placement during contour normalization.
- Residual affine transforms preserving nested group scaling, skew and reflection, strict SVG transform parsing and round-trip affine numeric serialization.
- Retained gradient shader caches with exact invalidation; allocation-free affine rectangle bounds and single-pass point bounds.
- 54 additional registered engine regressions (195 total), including mask pixels, shader ownership, affine properties, serialization and gradient normalization.
- Three additional real browser scenarios (13 total) for mask commands/source movement/recovery, SVG alpha/radial/affine data and imported gradient editing/cache retention.
- Five-sample appearance CPU benchmark medians with allocation and pixel-equivalence checks.

### Correctness and compatibility

- Fixed retained identity-matrix shaders being disposed as temporary objects, which could remove gradient output and produce incorrect combined opacity.
- Gradient stop alpha and overall fill alpha are applied once each. Luminance is computed after source compositing.
- Computed matrix inverse/bounds properties are excluded from native JSON.
- New saves use schema 3; schemas 1 and 2 remain readable and upgrade when saved. Earlier releases reject the new schema rather than silently dropping semantics.
- Removed temporary integration/toolchain-export workflows after source materialization.

### Boundaries

Inverted masks are supported in native/PNG workflows; SVG export of inversion is explicitly rejected. SVG opacity-mask import requires user-space units. General filters, object-box mask units, image/use sources, gradient strokes, full CSS cascade and linear-light interpolation remain unsupported. This increment does not complete advanced typography, meshes, AI/EPS/PDF, CMYK/ICC production, multiple documents or arbitrary floating docking. CPU software-Skia measurements are not physical-GPU or whole-application frame-rate claims.

## 0.3.0-alpha.1 — 2026-09-27

Added editable vector clipping sets, mask/content selection, nested clips and compound holes; native/clipboard/component mask reference preservation; supported user-space SVG clipping; schema 2 compatibility protection; exact retained geometry snapshots, text/font and selection caches; indexed stationary-target snapping; conservative nested-leaf culling; rollback-through-serialization and live-mask ownership guards.

Validation expanded to 141 registered engine cases, five benchmark safety checks and ten browser scenarios. Build retained CPU reports and all deployment/release workflows ran the complete browser suite. Performance measurements used software Skia and excluded cold snap-index construction from query timings.

## 0.2.0-alpha.1 — 2026-09-27

Added managed editable contours and direct anchor editing for primitives, imported SVG, compounds, Boolean/expanded paths and outlined glyphs; multi-anchor selection/marquee, drag/nudge, exact cubic insertion, reconnecting removal and incident-segment cutting; bounded rational-conic conversion; persistent fill rules; Make/Release Compound Path; and Create Outlines using shared glyph runs.

Fixed absolute pointer normalization drift, opposite-tangent length preservation, closing-contour handles and basic text-layout consistency. Validation expanded to 107 engine cases and seven browser scenarios. Complex shaping and full Illustrator path-tool parity were not claimed.

## 0.1.0-alpha.1 — 2026-09-27

Initial independent illustration editor based on the author's MIT VectorSpace engine. Added the shared Uno desktop/WebAssembly app, original Alpine Echoes artwork, dark workspace, nine reusable libraries, vector drawing/manipulation, expansion/offsets/blends/repeats, Pathfinder, gradients, local recovery, SVG/PNG workflows and build/Pages/release automation.

The initial alpha was not complete or pixel-identical Illustrator parity. Acceleration depended on host/driver, and software-rendered CI was not a physical-GPU benchmark.
