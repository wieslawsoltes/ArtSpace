# Changelog

## 0.7.0-alpha.1 — 2026-09-30

- Add editable SVG single-run textPath import with local/legacy references, inline paths, supported basic-shape baselines, signed and percentage offsets, positive authored path lengths, text anchors, inherited text/paint settings and own-reference transforms.
- Preserve distinct SVG midpoint/open-path and single-circuit/closed-path layout instead of forcing SVG anchors into native bracket intervals.
- Add retained SVG offset controls, an undoable on-canvas offset handle, explicit conversion to native brackets, and editable SVG export alongside the existing default outlined export.
- Eliminate per-glyph native path clones via transformed append, reuse per-import reference definitions and use resolution-independent linear artwork font metrics.
- Save schema 6 while reading schemas 1–6. Reject unsupported text-run layouts and native bracket-overflow editable export rather than changing their appearance silently.
- Add 44 engine cases and three browser workflows. Preserve the existing package, release and Trusted Publishing configuration. CPU construction reports are not physical-GPU benchmarks.


## 0.6.0-alpha.1 — 2026-09-29

- Editable tangent-oriented Type on a Path: create/attach, Start/End/center brackets, flip, metric alignment, baseline shift, explicit overflow and direct baseline anchors.
- Retained glyph geometry shared by drawing, picking and outlines; independent native arc-length/projection caches, exact invalidation and bounded resource accounting.
- Reversible text/outline/area-text conversion, linked-symbol compatibility, native schema 5 and safe diagnostic behavior for malformed baselines.
- SVG outline export on detached snapshots, shifted-ink export bounds and preservation of text gradient coordinate spaces. Native SVG textPath import remains explicitly unsupported.
- 45 additional engine regressions and three real Uno browser scenarios, plus alternating-order CPU cache measurements with exact reference pixels.
- Existing scene replay, release packaging and NuGet Trusted Publishing remain unchanged. This is not complete Illustrator typography/UI parity or a physical-GPU benchmark.


## 0.5.0-alpha.1 — live appearance and retained rendering

- Add ordered non-destructive Gaussian Blur, Drop Shadow, Outer Glow and Saturation effects, with parameter editing, enablement, reorder, duplicate and delete.
- Add document-local Graphic Styles with independent paint/effect ownership, vector thumbnails, bounded paged UI, apply, rename and delete.
- Add retained Appearance and Graphic Styles panels, Effect/Window menu integration, fill/stroke ordering and duplication, and appearance clear/reduce actions.
- Support gradient strokes, signed dash offsets, odd-length dash patterns and nine additional compositing modes (sixteen total).
- Retain native fill/stroke paints, dash effects and image-filter graphs; replay unchanged scenes as native vector display lists into Uno's shared Skia canvas, with separate editing overlays and a bounded direct-render fallback.
- Preserve explicit symbol appearance overrides, restore root opacity/blend on reset, respect locked instances and retain stable override identity for no-op edits.
- Extend SVG gradient-stroke and dash-phase interchange. Native/PNG retain live effects; SVG export explicitly rejects enabled live effects instead of silently discarding them.
- Save native schema 4, read schemas 1–4, and validate new appearance before synchronization and transaction completion.
- Add 36 registered appearance/retention regressions (247 total), three real browser scenarios (21 total), actual-control diagnostics and alternating-order reference-pixel CPU benchmarks.

GPU execution depends on the Uno/Skia host, driver and browser. This increment is not a WebGPU compute rewrite or complete/pixel-identical Illustrator parity. Software benchmark numbers do not establish physical-GPU throughput. See [appearance and rendering](docs/appearance-rendering.md) for semantics, resource limits, workflows and remaining boundaries.

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
