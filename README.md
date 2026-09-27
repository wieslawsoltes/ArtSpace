<div align="center">

# ArtSpace

### Vector illustration. One C# engine. Desktop and browser.

A local-first illustration editor built with **Uno Platform** and **SkiaSharp**, with a compact Illustrator-style workspace and nine reusable .NET libraries.

[Open ArtSpace](https://wieslawsoltes.github.io/ArtSpace/) · [Getting started](docs/getting-started.md) · [Path editing](docs/path-editing.md) · [Clipping and performance](docs/clipping-and-performance.md)

[![Build](https://github.com/wieslawsoltes/ArtSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/ArtSpace/actions/workflows/build.yml)
[![Pages](https://github.com/wieslawsoltes/ArtSpace/actions/workflows/pages.yml/badge.svg)](https://github.com/wieslawsoltes/ArtSpace/actions/workflows/pages.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

</div>

---

## A real illustration application

ArtSpace runs the same C# document model, transaction engine, vector operations, custom controls and workbench in native desktop hosts and Uno WebAssembly. It is not an HTML mock-up around a separate browser-only drawing engine. JavaScript is limited to file transfer, IndexedDB recovery, input adaptation and opt-in read-only diagnostics.

The dark workspace combines an application menu, appearance bar, toolbox, document tab, rulers, pasteboard, artboards, properties/layers/artboards/history panels and status bar. The original **Alpine Echoes** artwork is fully editable across three artboards.

**Current package version: `0.3.0-alpha.1`.** This is an independent functional alpha, not a complete or pixel-identical Adobe Illustrator replacement. The [feature matrix](docs/feature-matrix.md) records implemented workflows and remaining boundaries.

## Illustration workflows

| Area | Capabilities |
| --- | --- |
| Drawing | Primitives, pen paths, freehand strokes, fixed-width brush, text, artboards and slices |
| Transformation | Selection, marquee, move, eight-handle resize, rotation, constrained transforms, duplication, grouping, ordering, alignment, distribution and nudging |
| Path editing | Native/imported/expanded/compound contours, multi-anchor selection, tangent editing, exact cubic insertion, reconnecting removal, segment cutting, smooth/corner conversion and undo |
| Geometry | Shape/stroke expansion, offsets, union/subtract/intersect/exclude, compound make/release and nonzero/even-odd fills |
| Typography | Basic editing, wrapping, tracking/alignment and Create Outlines using the same glyph layout as painting |
| Clipping | Editable vector masks, mask/content selection, nested sets, compound holes, release, native persistence and supported SVG clipping interchange |
| Appearance | Multiple fills/strokes, linear/radial gradients, on-canvas direction, caps/joins/dashes, opacity, blends, shadows and frame clipping |
| Reuse | Bounded editable blends, radial repeats, linked local symbols/components and instances |
| Navigation | Hierarchical layers, visibility/locking, swatches, indexed snapping, guides, rulers, outline preview, pan and zoom |
| Files | Validated native JSON, editable SVG subset, SVG/PNG export, clipboard transfer and local recovery |

**New in 0.3:** Ctrl 7 creates a vector clipping set; Ctrl Alt 7 releases it. Edit the clipping path or artwork independently from the Object menu and Properties panel. Geometry, text and selection caches now survive unrelated changes, snapping uses an indexed stationary-target path, and conservative leaf culling avoids offscreen paint work. See [contracts, measurements and limits](docs/clipping-and-performance.md).

## Build and run

The pinned stack is **.NET SDK 10.0.401**, **Uno SDK 6.7.30**, and **SkiaSharp 3.119.2** matched to Uno's native runtime. Uno's stable index was checked on September 27, 2026. Keep managed/native Skia ABI versions aligned.

```bash
git clone https://github.com/wieslawsoltes/ArtSpace.git
cd ArtSpace
python3 scripts/fetch-assets.py

# Geometry, persistence and transaction regressions
dotnet run --project tests/ArtSpace.Tests -c Release

# Native desktop host
dotnet run --project src/ArtSpace.App -f net10.0-desktop \
  -p:ArtSpaceDesktopOnly=true
```

Publish and serve the browser app:

```bash
dotnet workload install wasm-tools --skip-manifest-update
dotnet publish src/ArtSpace.App -f net10.0-browserwasm -c Release \
  -o artifacts/publish -p:WasmShellWebAppBasePath=/ArtSpace/
python3 scripts/collect-site.py artifacts/publish artifacts/site
python3 scripts/serve-site.py --directory artifacts/site
```

Open `http://127.0.0.1:4173/ArtSpace/`. Use HTTP(S), not `file://`. The asset script fetches pinned Inter assets with their SIL OFL notice; font binaries are not committed.

## Reusable packages

| Package | Responsibility |
| --- | --- |
| `ArtSpace.Core` | Document, appearance, affine geometry and managed editable contours |
| `ArtSpace.Layout` | Layout, constraints, exhaustive/reference snapping and indexed snapping |
| `ArtSpace.Documents` | Native validation/migration, clipboard, SVG subset and sample artwork |
| `ArtSpace.Editing` | Selection snapshots, transactions, history and linked components |
| `ArtSpace.Skia` | Retained geometry/text caches, clipping, culling, picking, glyph outlines and raster export |
| `ArtSpace.Illustration` | Expansion, offsets, blends, repeats, compound paths and clipping commands |
| `ArtSpace.Controls` | Custom menus, docks, resize grips, icons, numeric/color fields and layer rows |
| `ArtSpace.Editor` | Embeddable Uno surface, direct manipulation and gestures |
| `ArtSpace.Workbench` | Illustration workspace, command routing, properties and file workflows |

The first six libraries have no Uno dependency. The host supplies fonts, storage and lifetime; no static document singleton is required.

```csharp
using ArtSpace.Documents;
using ArtSpace.Editing;
using ArtSpace.Workbench;

var session = new EditorSession(IllustrationSample.Create());
// storage implements IWorkspaceStorage.
window.Content = new StudioWorkbench(session, storage);
```

Use `PathEditing`, `PathOperations` and `ClippingOperations` without a window. Matching Skia native assets are needed on the executing platform. CI package artifacts are not a claim of NuGet.org publication.

## Rendering and performance

Painting uses Uno **SKCanvasElement** and its shared Skia composition path, avoiding an extra application-owned CPU framebuffer/upload layer. Backend and hardware acceleration depend on the host/browser/driver. Path geometry and Boolean operations remain CPU work; this is not a raw-WebGPU engine.

Warm geometry lookup uses exact snapshots instead of reconstructing SVG keys. Stationary-target drags use sorted-axis snapping indexes with reference-equivalent ties and guides. Text runs/fonts and selection lists are retained across unrelated changes. Conservative culling checks leaves against the actual canvas clip while preserving overflowing groups and offscreen shadow sources.

```bash
# CPU allocation/timing report, with equivalence and safety preflight checks
dotnet run --project tests/ArtSpace.Tests -c Release --no-build -- --benchmark
```

Build retains the JSON as **ArtSpace-performance**. The [performance guide](docs/clipping-and-performance.md#reproduce-measurements) documents fixtures, sample results and limitations. Index construction is excluded from query timings. These are software-Skia CPU microbenchmarks, not whole-app frame-rate or physical-GPU claims.

## Verification and delivery

The suite contains **141 engine regressions**, five untimed benchmark boundary checks and **ten browser scenarios**. The latter drive actual pointer, keyboard and file-picker input, including clipping creation/release, mask-anchor editing, holes, recovery and cache retention.

```bash
npm ci
npx playwright install chromium
# Start the static server first.
npx playwright test
```

`?test=1` exposes read-only diagnostics, never an editing API. **Build** runs regression/benchmark checks, compiles Windows/Linux/macOS, publishes WebAssembly, packages all libraries and runs browser acceptance. **Pages** deploys successful main-branch artifacts, verifies `build-info.json` and runs public-site acceptance. **Release** produces tagged archives, packages and checksums; public NuGet publication requires an explicitly configured secret. Workflow conclusions and retained reports establish results for each commit.

## File compatibility and remaining boundaries

**Native saves now use schema 2.** ArtSpace 0.3 reads schemas 1 and 2 and upgrades schema 1 when saving. Older ArtSpace versions reject schema 2 rather than silently removing clipping behavior. Retain an original copy for older-version workflows.

Vector clipping is implemented; opacity/luminance masks are not. SVG clipping accepts one primitive/compound path per `userSpaceOnUse` definition and rejects unsupported clipping definitions. Existing transform, paint-server and SVG subset limits remain. This is not lossless Illustrator interchange.

AI/EPS/PDF, CMYK/ICC/spot-color production, overprint, advanced text shaping, meshes, perspective tools, tracing, pressure/pattern brushes, multiple documents, arbitrary floating docking and Adobe plugins remain outside this alpha. Rational conics are approximated during editing; direct selection edits one object's contours at a time. Autosave is local recovery, not a backup service.

## Documentation and license

[Getting started](docs/getting-started.md) · [Path editing](docs/path-editing.md) · [Clipping/performance](docs/clipping-and-performance.md) · [Architecture](docs/architecture.md) · [Feature matrix](docs/feature-matrix.md) · [Contributing](CONTRIBUTING.md) · [Changelog](CHANGELOG.md)

ArtSpace is [MIT-licensed](LICENSE). Its shared engine derives from the author's MIT [VectorSpace](https://github.com/wieslawsoltes/VectorSpace) snapshot `587f780f2803f6b3aa59dfb5411da2c2fe601dfa`; attribution is retained in [third-party notices](THIRD-PARTY-NOTICES.md).

Adobe Illustrator is the design/interaction reference. ArtSpace contains no Adobe source code, proprietary artwork, logos or product icons and is not affiliated with or endorsed by Adobe.
