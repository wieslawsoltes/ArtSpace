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

ArtSpace runs the same document model, transaction engine, geometry operations, custom controls and workbench in a native desktop host and real Uno WebAssembly. JavaScript is limited to browser capabilities: file transfer, IndexedDB persistence, a small navigation-key adapter and opt-in read-only diagnostics. The UI and editing engine are C#, not an HTML shell around a different browser-only editor.

The workspace follows familiar illustration conventions: an application menu, appearance control bar, two-column tool palette, document tab, rulers, gray pasteboard, white artboards, properties/layers/artboards/history panels and status bar. The original **Alpine Echoes** artwork is fully editable across three artboards.

**Status: `0.1.0-alpha.1`.** This is a functional illustration alpha, not a complete or pixel-identical Adobe Illustrator replacement. See the [feature matrix](docs/feature-matrix.md) for explicit implementation and interoperability boundaries.

## What works

| Area | Implemented workflows |
| --- | --- |
| Drawing | Rectangles, ellipses, polygons, stars, line segments, arrows, pen paths, pencil/freehand strokes, fixed-width paintbrush, text, artboards and slices |
| Editing | Selection, deep selection, marquee, eight-handle resize, rotation, constrained transforms, duplication, grouping, ordering, alignment, distribution, nudging, clipboard and transactional undo/redo |
| Paths | Pen tangents, direct manipulation of native anchors/direction handles, smooth/corner conversion, exact cubic subdivision, direction reversal, closure, shape expansion, stroke outlines and positive/negative offsets |
| Appearance | Multiple fills/strokes, linear/radial gradients, on-canvas gradient direction, stroke caps/joins/dashes, opacity, blend modes, shadows and artboard clipping |
| Pathfinder | Vector union, subtract, intersect and exclude through Skia path operations |
| Repetition | Bounded editable object blends with compatible geometry, radial repeats, local symbols/components and linked instances |
| Workspace | Resizable right dock, properties, hierarchical layers, filtering, visibility/locking, artboards, history, swatches, color spectrum, snapping, guides, rulers, outline preview, zoom and pan |
| Files | Validated native `.artspace` JSON, editable SVG subset import, SVG/PNG export, clipboard SVG import, IndexedDB autosave and native local recovery |

## Run locally

The toolchain is pinned in `global.json`: **.NET SDK 10.0.401** and **Uno SDK 6.7.30**. Uno's stable NuGet index was checked on September 27, 2026. Managed and native **SkiaSharp 3.119.2** are kept ABI-compatible with the Uno runtime.

```bash
git clone https://github.com/wieslawsoltes/ArtSpace.git
cd ArtSpace
python3 scripts/fetch-assets.py

# Geometry, document and transaction regressions
dotnet run --project tests/ArtSpace.Tests -c Release

# Shared native desktop host
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

Open `http://127.0.0.1:4173/ArtSpace/`. Serve over HTTP(S), not `file://`. The asset script retrieves a pinned Inter font and its SIL OFL notice; font binaries are not committed.

## Reuse the libraries

Nine projects are independently packable. The application host supplies startup, storage, fonts and an instance of the workbench.

| Package | Responsibility |
| --- | --- |
| `ArtSpace.Core` | Document, appearance, paths, affine geometry and viewport |
| `ArtSpace.Layout` | Layout, constraints and snapping calculations |
| `ArtSpace.Documents` | Validated persistence, clipboard formats, SVG subset and sample artwork |
| `ArtSpace.Editing` | Selection, transactions, history, commands and linked components |
| `ArtSpace.Skia` | Path cache, drawing, hit testing, Boolean geometry and raster export |
| `ArtSpace.Illustration` | Stroke/shape expansion, offsets, blends, repeats and anchor operations |
| `ArtSpace.Controls` | Custom menus, panel docks, resize grips, vector icons, numeric/color fields and layer rows |
| `ArtSpace.Editor` | Embeddable Uno drawing surface and gesture engine |
| `ArtSpace.Workbench` | Illustration workspace, command routing, properties and file workflows |

A headless vector operation:

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
session.Undo(); // Restores the original star in one transaction.
```

The executing platform needs matching Skia native assets. CI produces package artifacts; this is not a claim that packages have been published to nuget.org.

## Rendering and validation

The editor paints through Uno's **`SKCanvasElement`** into its shared Skia composition path, avoiding an extra application-owned CPU bitmap/upload layer. The graphics backend and hardware acceleration depend on the host, browser and driver. Path construction and Boolean operations run through Skia's CPU geometry APIs; this is not a GPU-compute-only engine.

The engine suite contains **74 cases**. Four browser acceptance scenarios drive the actual published Uno application with pointer/keyboard input, covering drawing, transforms, undo/redo, file download/recovery, pen/gradient/zoom/artboards, panel visibility/compact layout and custom-menu shape expansion. Workflow results and artifacts are authoritative for each commit.

```bash
npm ci
npx playwright install chromium
# Run after starting the local static server above.
npx playwright test tests/browser/illustration.spec.mjs
```

`?test=1` enables read-only diagnostics, not a test-only editing API. The navigation-key adapter runs identically in production and tests; C# controls decide whether a key is handled. CI uses Chromium software graphics for portability, so it is **not** a physical-GPU benchmark. Native compilation is separately checked on Windows, Linux and macOS; compilation does not certify every native interaction.

## Build, deployment and releases

**Build** runs regressions, compiles native hosts, publishes WebAssembly, packages all libraries and retains browser screenshots/traces. **Pages** deploys only verified, successful main-branch browser artifacts, checks their commit identifier and runs the browser scenarios against the public URL. **Release** builds tagged browser/desktop archives, packages and checksums. Public NuGet publication requires an explicitly configured `NUGET_API_KEY` in the `nuget` environment.

## Compatibility boundaries

Native `.ai`, `.eps` and PDF interchange are not implemented. SVG support is a documented subset, not lossless Illustrator roundtripping. Imported scripts and external active content are not executed. Advanced typography and text outlines, CMYK/ICC/spot-color production, overprint/separations, gradient meshes, perspective tools, image tracing, full brush families, arbitrary masks, multiple open documents, Adobe plugins and cloud collaboration are outside this alpha.

There is one active document session with multiple artboards. The controls use Uno layout/input/text primitives; not every platform primitive is replaced. Browser recovery is local storage, not a backup service: download a native document copy for safekeeping.

## Documentation and license

[Getting started](docs/getting-started.md) · [Architecture](docs/architecture.md) · [Feature matrix](docs/feature-matrix.md) · [Contributing](CONTRIBUTING.md) · [Changelog](CHANGELOG.md)

ArtSpace is [MIT-licensed](LICENSE). Its shared engine is adapted from the author's MIT [VectorSpace](https://github.com/wieslawsoltes/VectorSpace) snapshot `587f780f2803f6b3aa59dfb5411da2c2fe601dfa`. Attribution is retained in [third-party notices](THIRD-PARTY-NOTICES.md).

Adobe Illustrator is a design and interaction reference. ArtSpace contains no Adobe source code, proprietary artwork, logos or product icons and is not affiliated with or endorsed by Adobe.
