<div align="center">

# ArtSpace

### Vector illustration. One C# engine. Desktop and browser.

A local-first illustration editor built with **Uno Platform** and **SkiaSharp**, with an Illustrator-style dark workspace and nine reusable .NET libraries.

[Open ArtSpace](https://wieslawsoltes.github.io/ArtSpace/) · [Getting started](docs/getting-started.md) · [Path editing](docs/path-editing.md) · [Opacity masks and gradients](docs/opacity-masks-and-gradients.md)

[![Build](https://github.com/wieslawsoltes/ArtSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/ArtSpace/actions/workflows/build.yml)
[![Pages](https://github.com/wieslawsoltes/ArtSpace/actions/workflows/pages.yml/badge.svg)](https://github.com/wieslawsoltes/ArtSpace/actions/workflows/pages.yml)
[![MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

</div>

---

## An editable illustration workspace

ArtSpace shares its C# document model, transaction engine, geometry, custom controls and workbench between native desktop hosts and real Uno WebAssembly. JavaScript is confined to file transfer, local recovery, input adaptation and opt-in read-only diagnostics. There is no separate JavaScript editor hidden beneath an HTML mock-up.

The compact dark shell combines menus, appearance controls, toolbox, rulers, pasteboard, artboards, layers/properties/history and status bar. The original **Alpine Echoes** sample is editable vector artwork across three artboards.

**Version: `0.4.1-alpha.1`.** This is an independent functional alpha, not complete or pixel-identical Adobe Illustrator parity. The [feature matrix](docs/feature-matrix.md) distinguishes implemented behavior and remaining compatibility boundaries.

## New in 0.4.1

**Responsive selection and panels:** retained inspector sections and values, visible-panel-only refreshes, incremental layer rows, indexed selection and lazy drag transactions. Plain clicks no longer serialize the document or construct a snapping index. Focused edits and section expansion survive ordinary updates. [Implementation and reproducible measurements](docs/ui-performance.md).

## New in 0.4

**Editable alpha and luminance masks:** create, release, invert, disable, select source/content independently and retain nested mask relationships through undo, cloning, clipboard and symbols. The Transparency section exposes mode and source editing.

**SVG paints and affine fidelity:** import/export linear/radial gradient fills with stop opacity, coordinates, focus, radius, spread and local inheritance. Preserve nested group scaling/skew instead of silently changing only nominal bounds. Supported SVG opacity masks retain source artwork and finite regions.

**Lower repeated rendering cost:** retain gradient shaders with exact invalidation and correct native ownership; compute affine rectangle bounds without managed arrays. Five-sample CPU benchmark results and pixel-equivalence checks are retained by CI.

[Read the workflow, API and compatibility guide](docs/opacity-masks-and-gradients.md).

## Capabilities

| Area | Implemented workflows |
| --- | --- |
| Drawing | Primitives, pen/cubic paths, pencil, fixed-width brush, text, artboards and slices |
| Editing | Selection, marquee, transforms, duplication, grouping, order, alignment, distribution, clipboard and transactional undo/redo |
| Contours | Native/imported/expanded/compound/glyph paths, multiple anchors, tangent editing, exact cubic insertion, reconnecting removal, segment cutting and smooth/corner conversion |
| Geometry | Shape/stroke expansion, offsets, Boolean union/subtract/intersect/exclude and compound paths with nonzero/even-odd rules |
| Appearance | Multiple fills/strokes, linear/radial gradients, opacity, blends, shadows, vector clipping and editable alpha/luminance masks |
| Text | Basic wrapping, tracking/alignment and Create Outlines using the same glyph runs as painting |
| Reuse | Bounded editable blends/repeats and linked local symbols/components |
| Files | Native JSON, supported SVG interchange, SVG/PNG export and IndexedDB/native recovery |
| Performance | Retained geometry/text/gradient caches, selection snapshots, indexed stationary-target snapping and conservative nested-leaf culling |

## Build and run

The pinned stack is **.NET SDK 10.0.401**, **Uno SDK 6.7.30** and **SkiaSharp 3.119.2** matched to the Uno native runtime. Keep managed/native Skia ABI families aligned; a standalone major dependency bump is not a validated runtime migration.

```bash
git clone https://github.com/wieslawsoltes/ArtSpace.git
cd ArtSpace
python3 scripts/fetch-assets.py

dotnet run --project tests/ArtSpace.Tests -c Release
dotnet run --project src/ArtSpace.App -f net10.0-desktop \
  -p:ArtSpaceDesktopOnly=true
```

Publish the browser application:

```bash
dotnet workload install wasm-tools --skip-manifest-update
dotnet publish src/ArtSpace.App -f net10.0-browserwasm -c Release \
  -o artifacts/publish -p:WasmShellWebAppBasePath=/ArtSpace/
python3 scripts/collect-site.py artifacts/publish artifacts/site
python3 scripts/serve-site.py --directory artifacts/site
```

Open `http://127.0.0.1:4173/ArtSpace/`. HTTP(S) is required; `file://` is not supported. The asset script retrieves pinned Inter files and their SIL OFL notice. Font binaries are not committed.

## Download

Every [release](https://github.com/wieslawsoltes/ArtSpace/releases/latest) ships a self-contained, single-file desktop app — no .NET install needed:

| OS | x64 | Arm64 |
| --- | --- | --- |
| Windows | `ArtSpace-<version>-win-x64.zip` | `ArtSpace-<version>-win-arm64.zip` |
| macOS | `ArtSpace-<version>-osx-x64.tar.gz` | `ArtSpace-<version>-osx-arm64.tar.gz` |
| Linux | `ArtSpace-<version>-linux-x64.tar.gz` | `ArtSpace-<version>-linux-arm64.tar.gz` |

Extract and run `ArtSpace` (`ArtSpace.exe` on Windows). Builds are not code-signed yet: on macOS clear the quarantine flag with `xattr -d com.apple.quarantine ArtSpace`; on Windows choose **More info → Run anyway** in SmartScreen. Verify downloads against `SHA256SUMS.txt`.

The libraries below are published to [NuGet.org](https://www.nuget.org/packages?q=ArtSpace), e.g. `dotnet add package ArtSpace.Workbench`.

## Reusable libraries

| Package | Responsibility |
| --- | --- |
| `ArtSpace.Core` | Document, appearance, affine geometry and managed editable contours |
| `ArtSpace.Layout` | Constraints, layout and reference/indexed snapping |
| `ArtSpace.Documents` | Validated native schemas, clipboard and SVG subset |
| `ArtSpace.Editing` | Selection, transactions, history and linked components |
| `ArtSpace.Skia` | Cached paths/text/shaders, compositing, culling, hit testing, glyph paths and raster export |
| `ArtSpace.Illustration` | Expansion, offsets, compounds, clipping/opacity-mask commands, blends and repeats |
| `ArtSpace.Controls` | Custom menus, panels, fields, resize grips, vector icons and layer rows |
| `ArtSpace.Editor` | Embeddable Uno drawing surface and direct-manipulation gestures |
| `ArtSpace.Workbench` | Application workspace, properties, command routing and storage workflows |

The first six packages do not depend on Uno. Hosts own lifetime, fonts and an `IWorkspaceStorage` implementation. `OpacityMaskOperations`, `PathOperations` and `PathEditing` are reusable independently of the shell.

```csharp
using ArtSpace.Documents;
using ArtSpace.Editing;
using ArtSpace.Workbench;

var session = new EditorSession(IllustrationSample.Create());
window.Content = new StudioWorkbench(session, storage);
```

Skia consumers need compatible native assets for the executing platform. Tagged releases publish all nine packages, with symbols, to NuGet.org.

## Verification and performance

The repository contains **211 registered engine cases**, **five additional benchmark boundary checks** and **18 browser scenarios**. Tests exercise actual pointer, keyboard and file-picker input against the published Uno app. Diagnostics enabled by `?test=1` are read-only, not an editing API.

```bash
npm ci
npx playwright install chromium
npx playwright test # after starting the local static server

dotnet run --project tests/ArtSpace.Tests -c Release --no-build -- --benchmark
```

Build validates the engine/benchmarks, Windows/Linux/macOS compilation, browser publication/acceptance and all nine packages. Pages deploys successful main-branch artifacts, verifies `build-info.json` and tests the public application. Release runs for `v*` tags or a supplied manual version: it repeats the engine and browser gates, publishes self-contained single-file desktop executables for Windows, macOS and Linux (x64 and arm64) plus browser/source/package archives and `SHA256SUMS.txt`. Tags attach them to a GitHub Release and publish the packages to NuGet.org with [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) (OIDC, no stored API key) from the protected `nuget` environment; manual runs are dry runs that only upload workflow artifacts. Workflow results establish which commit passed.

Rendering uses Uno **SKCanvasElement** and its shared Skia composition path, not an extra application-owned CPU framebuffer. Hardware acceleration depends on the host/browser/driver; path operations remain CPU-side. CI Chromium and benchmark surfaces use software graphics. CPU allocations/timings are not whole-app frame rates or physical-GPU certification. See [clipping/performance](docs/clipping-and-performance.md) and the [0.4 guide](docs/opacity-masks-and-gradients.md#cache-ownership-and-measured-work).

## Compatibility

**Native saves use schema 3.** ArtSpace 0.4 reads schemas 1–3 and upgrades older documents when saving. Previous releases reject schema 3 rather than silently dropping new mask/paint/affine semantics. Preserve an original copy for older-version workflows. Autosave is local recovery, not durable backup.

Supported SVG gradients and masks are not full SVG/Illustrator roundtripping. Opacity-mask import currently requires user-space units; inverted masks retain native/PNG output but SVG export is explicitly rejected. General filters, image/use content, gradient strokes, stylesheet cascade and linear-light paint interpolation remain outside the supported subset.

Advanced shaping, variable-font workflows, gradient meshes, full live effects, image tracing, pressure/pattern brushes, AI/EPS/PDF interchange, CMYK/ICC production, multiple documents and arbitrary floating docking remain unfinished. Direct contour editing works on one object's contours at a time; rational curves are approximated on editing. See the complete [feature matrix](docs/feature-matrix.md).

## Documentation and license

[Getting started](docs/getting-started.md) · [Architecture](docs/architecture.md) · [Contours/outlines](docs/path-editing.md) · [Clipping/performance](docs/clipping-and-performance.md) · [Opacity/gradients](docs/opacity-masks-and-gradients.md) · [Contributing](CONTRIBUTING.md)

ArtSpace is [MIT-licensed](LICENSE), derived from the author's MIT [VectorSpace](https://github.com/wieslawsoltes/VectorSpace) snapshot `587f780f2803f6b3aa59dfb5411da2c2fe601dfa`. Attribution is retained in [third-party notices](THIRD-PARTY-NOTICES.md).

Adobe Illustrator is a design and interaction reference. ArtSpace contains no Adobe source code, proprietary artwork, logos or product icons and is not affiliated with or endorsed by Adobe.

## Responsive selection and panels

Selection and property-editing performance is described in [UI performance](docs/ui-performance.md), including retained controls, change routing, lazy drag transactions and reproducible browser measurements.
