<div align="center">

# ArtSpace

### Vector illustration. One C# engine. Desktop and browser.

An independent illustration editor built with **Uno Platform** and **SkiaSharp**, with a compact, dark, Illustrator-style workspace and reusable .NET libraries.

[Open the browser app](https://wieslawsoltes.github.io/ArtSpace/) · [Getting started](docs/getting-started.md) · [Architecture](docs/architecture.md) · [Feature boundaries](docs/feature-matrix.md)

[![Build](https://github.com/wieslawsoltes/ArtSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/ArtSpace/actions/workflows/build.yml)
[![Pages](https://github.com/wieslawsoltes/ArtSpace/actions/workflows/pages.yml/badge.svg)](https://github.com/wieslawsoltes/ArtSpace/actions/workflows/pages.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

</div>

---

## An illustration workspace, not a web mock-up

ArtSpace runs the same document model, transaction engine, geometry operations, custom controls and workbench in a native desktop host and real Uno WebAssembly. The browser's JavaScript is limited to file transfer, IndexedDB persistence and opt-in read-only test diagnostics. The application UI is not an HTML recreation over a separate JavaScript drawing engine.

The workspace follows familiar illustration conventions: an application menu, horizontal appearance controls, a two-column tool palette, document tab, rulers, gray pasteboard, white artboards, properties/layers/artboards/history panels and a bottom status bar. The bundled **Alpine Echoes** artwork is original and fully editable.

**Status: `0.1.0-alpha.1`.** ArtSpace is usable for local vector editing, but it is not a complete or pixel-identical Adobe Illustrator replacement. The supported workflows and explicit interoperability limitations are documented below and in the [feature matrix](docs/feature-matrix.md).

## What works

| Area | Implemented workflows |
| --- | --- |
| Drawing | Rectangles, ellipses, polygons, stars, line segments, arrows, pen paths, pencil/freehand strokes, fixed-width paintbrush, text, artboards and slices |
| Editing | Selection, deep selection, marquee, eight-handle resize, rotation, constrained transforms, duplication, grouping, ordering, alignment, distribution, nudging, clipboard and transactional undo/redo |
| Paths | Bézier pen tangents, direct manipulation of anchors and direction handles on point-based paths, smooth/corner conversion, exact cubic subdivision, direction reversal, closure, shape expansion, stroke outlines and positive/negative offsets |
| Compositing | Multiple fills and strokes, linear/radial gradients, on-canvas gradient direction, stroke caps/joins/dashes, opacity, blend modes, shadows and artboard clipping |
| Pathfinder | Vector union, subtract, intersect and exclude through Skia path operations |
| Repetition | Editable intermediate-object blends with matching geometry, radial repeats, local symbols/components and linked instances |
| Workspace | Resizable right dock, properties, hierarchical layers, filtering, visibility/locking, artboard navigation, history, swatches, color spectrum, snapping, guides, rulers, outline preview, zoom and pan |
| Files | Native `.artspace` JSON, editable SVG subset import, SVG/PNG export, clipboard SVG import, browser IndexedDB autosave and native local recovery |

## Run locally

The toolchain is pinned in `global.json`: **.NET SDK 10.0.401** and **Uno SDK 6.7.30**. The Uno SDK was checked against the stable NuGet index on September 27, 2026. Managed and native Skia must remain ABI-compatible; see [rendering architecture](docs/architecture.md#rendering).

```bash
git clone https://github.com/wieslawsoltes/ArtSpace.git
cd ArtSpace
python3 scripts/fetch-assets.py

# Shared geometry/document regression suite
dotnet run --project tests/ArtSpace.Tests -c Release

# Native desktop host
dotnet run --project src/ArtSpace.App -f net10.0-desktop \
  -p:ArtSpaceDesktopOnly=true
```

For the browser:

```bash
dotnet workload install wasm-tools --skip-manifest-update
dotnet publish src/ArtSpace.App -f net10.0-browserwasm -c Release \
  -o artifacts/publish -p:WasmShellWebAppBasePath=/ArtSpace/
python3 scripts/collect-site.py artifacts/publish artifacts/site
python3 scripts/serve-site.py --directory artifacts/site
```

Open `http://127.0.0.1:4173/ArtSpace/`. Serve the app over HTTP(S); opening `index.html` as a local file is not supported. `scripts/fetch-assets.py` retrieves a pinned, licensed Inter font and its OFL notice; font binaries are not committed to this repository.

## Reuse the libraries

The application host is thin. Nine projects are packable independently:

| Package | Responsibility |
| --- | --- |
| `ArtSpace.Core` | Serializable document, appearance, paths, affine geometry and viewport |
| `ArtSpace.Layout` | Layout, constraints and snapping calculations |
| `ArtSpace.Documents` | Validated native persistence, clipboard formats, SVG subset and sample artwork |
| `ArtSpace.Editing` | Selection, transactions, history, commands and linked components |
| `ArtSpace.Skia` | Path cache, drawing, hit testing, Boolean geometry and raster export |
| `ArtSpace.Illustration` | Stroke/shape expansion, offsets, blends, repeats and anchor operations |
| `ArtSpace.Controls` | Custom menus, panel docks, resize grips, vector icons, numeric/color fields and layer rows |
| `ArtSpace.Editor` | Embeddable Uno drawing surface and pointer/keyboard gesture engine |
| `ArtSpace.Workbench` | Illustration workspace, command routing, properties and file workflows |

Example headless vector operation:

```csharp
using ArtSpace.Core;
using ArtSpace.Editing;
using ArtSpace.Illustration;
using ArtSpace.Skia;

var shape = new DesignNode
{
    Kind = NodeKind.Star,
    Width = 240,
    Height = 240,
    Fill = "#E6AA67",
    Strokes = [new() { Width = 12, Color = "#203F49" }]
};
var session = new EditorSession(new DesignDocument
{
    Pages = [new() { Nodes = [shape] }]
});
using var renderer = new SceneRenderer();
session.Select(shape);
IllustrationOperations.OutlineStrokes(session, renderer);
session.Undo(); // Restores the original editable star in one operation.
```

The example needs the matching Skia native asset for the executing platform. Packages are produced by CI/release workflows; a package artifact is not a claim that it has been published to nuget.org.

## Validation and delivery

The workflows build the engine and native hosts, publish real Uno WebAssembly, run Playwright against the published output and retain screenshots/traces. Pages deployment uses the same `/ArtSpace/` path used by local acceptance tests. Release tags produce versioned artifacts and checksums; NuGet publication requires an explicitly configured secret.

```bash
npm ci
npx playwright install chromium
# Run after starting the local static server above.
npx playwright test tests/browser/illustration.spec.mjs
```

Browser tests drive real pointer and keyboard input. `?test=1` enables read-only diagnostics; there is no test-only document mutation API. CI Chromium uses software graphics for portability, so passing CI is **not** a physical-GPU performance benchmark or a certification of every browser/driver combination.

## Compatibility boundaries

ArtSpace does **not** currently read or write native `.ai` or `.eps` files. SVG is a documented subset, not lossless Illustrator interchange. Unsupported input is reported; scripts and external active content are not executed. Advanced text shaping, CMYK/ICC/spot-color production, overprint/separations, gradient meshes, perspective editing, image tracing, full brush families, Adobe plugins and cloud collaboration are outside this alpha.

The core preserves several inherited design/layout/component capabilities, but their presence does not imply full Illustrator feature parity. There is one active document session; artboards are not independent document tabs. See [the complete boundary list](docs/feature-matrix.md).

## Documentation

[Getting started and shortcuts](docs/getting-started.md) explains the editing model and recovery. [Architecture](docs/architecture.md) describes package boundaries, GPU integration, transactions and portability. [Feature matrix](docs/feature-matrix.md) distinguishes implemented, limited and absent capabilities. [Contributing](CONTRIBUTING.md) covers development conventions.

## License and provenance

ArtSpace is licensed under [MIT](LICENSE). Its shared engine is adapted from [VectorSpace](https://github.com/wieslawsoltes/VectorSpace), pinned at `587f780f2803f6b3aa59dfb5411da2c2fe601dfa`, also by Wiesław Šoltés under MIT. ArtSpace adds the illustration workbench, original artwork and illustration-specific operations. Upstream attribution is retained in [third-party notices](THIRD-PARTY-NOTICES.md).

Adobe Illustrator is a design and interaction reference. ArtSpace contains no Adobe source code, proprietary assets, logos or product icons and is not affiliated with or endorsed by Adobe.
