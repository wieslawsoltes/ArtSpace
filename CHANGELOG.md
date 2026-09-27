# Changelog

## 0.1.0-alpha.1 — 2026-09-27

Initial ArtSpace illustration editor, adapted from the author's MIT-licensed VectorSpace engine.

### Added

- Shared Uno Platform desktop and WebAssembly application with a dark illustration workspace, custom command menus, two-column toolbar, appearance controls, rulers, resizable panel tabs and status bar.
- Original, fully editable Alpine Echoes artwork across three artboards.
- Independent `ArtSpace.Illustration` library with shape expansion, stroke outlining, positive/negative offsets, bounded object blends, radial repeats and anchor operations.
- Direct manipulation of native path anchors/tangents, on-canvas gradient direction, appearance sampling and a zoom tool.
- Stroke cap, join, miter and dash persistence/rendering, plus SVG appearance interchange.
- Pathfinder, swatches, artboard navigation and history panels.
- Illustration keyboard conventions, including selection/direct selection, pen/pencil/brush, artboards and outline preview.
- Nine packable libraries, native build matrix, browser acceptance tests, Pages deployment verification and versioned release workflows.
- Getting-started guide, architecture documentation and an explicit feature-boundary matrix.

### Correctness work

- Offset geometry is normalized so selection bounds follow expanded/inset paths and transformed centers are preserved.
- Appearance controls synchronize with selection instead of continuing to display stale drawing defaults.
- Tab panel toggling intercepts preview input before normal focus navigation.
- Browser tests cover real document edits, undo/redo, downloads, recovery and layout, rather than only checking that a canvas exists.

### Important limitations

This alpha is not complete or pixel-identical Adobe Illustrator parity. Native AI/EPS/PDF interchange, print color management, advanced typography, text outlines, meshes, image tracing, brush families, arbitrary masks, full live-effect semantics, multi-document editing and Adobe plugins are not implemented. GPU acceleration depends on the runtime host; CI software graphics tests are not hardware benchmarks.
