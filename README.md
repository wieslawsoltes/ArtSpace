<div align="center">

# ArtSpace

### Vector illustration. One C# engine. Desktop and browser.

A local-first illustration editor built with **Uno Platform** and **SkiaSharp**, with a compact Illustrator-style workspace and nine reusable .NET libraries.

[Open ArtSpace](https://wieslawsoltes.github.io/ArtSpace/) · [Getting started](docs/getting-started.md) · [Path editing](docs/path-editing.md) · [Architecture](docs/architecture.md)

[![Build](https://github.com/wieslawsoltes/ArtSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/ArtSpace/actions/workflows/build.yml)
[![Pages](https://github.com/wieslawsoltes/ArtSpace/actions/workflows/pages.yml/badge.svg)](https://github.com/wieslawsoltes/ArtSpace/actions/workflows/pages.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

</div>

---

## A real illustration application

ArtSpace runs the same C# document model, transaction engine, vector operations, custom controls and workbench in native desktop hosts and Uno WebAssembly. It is not an HTML mock-up around a separate browser-only drawing engine. JavaScript is limited to browser capabilities: file transfer, IndexedDB recovery, navigation-key adaptation and opt-in read-only diagnostics.

The dark workspace combines an application menu, appearance control bar, two-column toolbox, document tab, rulers, gray pasteboard, artboards, properties/layers/artboards/history panels and status bar. The original **Alpine Echoes** artwork is fully editable across three artboards.

**Current package version: `0.2.0-alpha.1`.** ArtSpace is an independent functional alpha, not a complete or pixel-identical Adobe Illustrator replacement. [Feature boundaries](docs/feature-matrix.md) distinguish implemented workflows from remaining gaps.

## Illustration workflows

| Area | Capabilities |
| --- | --- |
| Draw | Rectangles, ellipses, polygons, stars, lines, arrows, pen paths, freehand strokes, fixed-width paintbrush, text, artboards and slices |
| Transform | Select, marquee, move, eight-handle resize, rotate, constrained transforms, duplicate, group, order, align, distribute and nudge |
| Edit paths | Direct-edit native, imported SVG, expanded and compound contours; select multiple anchors, marquee, edit tangents, insert, cut/remove, smooth/corner, reverse and close |
| Create geometry | Exact cubic subdivision, shape expansion, stroke outlines, positive/negative offsets, Boolean union/subtract/intersect/exclude, compound make/release |
| Typography | Basic text editing, wrapping, tracking/alignment and **Create Outlines** using the same glyph layout as rendering |
| Appearance | Multiple fills/strokes, linear/radial gradients, on-canvas gradient direction, fill rules, stroke caps/joins/dashes, opacity, blend modes, shadows and frame clipping |
| Reuse | Bounded editable blends, radial repeats, local linked symbols/components and instances |
| Navigate | Hierarchical layers, filtering, visibility/locking, artboard navigation, history, swatches, snapping, guides, rulers, outline preview, pan and zoom |
| Save | Validated `.artspace` JSON, editable SVG subset import, SVG/PNG export, clipboard transfer and local recovery |

Path changes use cancellable transactions. A drag creates one undo item; Escape restores the pre-drag document. Nonzero/even-odd rules survive native persistence and SVG interchange, so compound holes retain their intended fill behavior. See [path editing and outlines](docs/path-editing.md) for precision limits and keyboard behavior.

## Build and run

The pinned toolchain is **.NET SDK 10.0.401**, **Uno SDK 6.7.30**, and **SkiaSharp 3.119.2** matched to Uno's native runtime. The stable Uno index was checked on September 27, 2026. Keep managed and native Skia ABI versions aligned.

```bash
git clone https://github.com/wieslawsoltes/ArtSpace.git
cd ArtSpace
python3 scripts/fetch-assets.py

# Headless geometry, persistence and transaction regressions
dotnet run --project tests/ArtSpace.Tests -c Release

# Shared native desktop host
dotnet run --project src/ArtSpace.App -f net10.0-desktop \
  -p:ArtSpaceDesktopOnly=true
```

Publish and serve the actual browser application:

```bash
dotnet workload install wasm-tools --skip-manifest-update
dotnet publish src/ArtSpace.App -f net10.0-browserwasm -c Release \
  -o artifacts/publish -p:WasmShellWebAppBasePath=/ArtSpace/
python3 scripts/collect-site.py artifacts/publish artifacts/site
python3 scripts/serve-site.py --directory artifacts/site
```

Open `http://127.0.0.1:4173/ArtSpace/`. Use HTTP(S), not `file://`. The asset script retrieves pinned Inter font assets with their SIL OFL notice; font binaries are not committed.

## Reusable packages

| Package | Responsibility |
| --- | --- |
| `ArtSpace.Core` | Document, appearance, affine geometry, managed editable contours and viewport |
| `ArtSpace.Layout` | Layout, constraints and snapping |
| `ArtSpace.Documents` | Validated persistence, clipboard formats, SVG subset and sample artwork |
| `ArtSpace.Editing` | Selection, transactions, history and linked components |
| `ArtSpace.Skia` | Path cache, drawing, hit testing, glyph outlines, contour conversion and raster export |
| `ArtSpace.Illustration` | Shape/stroke expansion, offsets, blends, repeats, compound paths and editing commands |
| `ArtSpace.Controls` | Custom menus, panel docks, resize grips, icons, numeric/color fields and layer rows |
| `ArtSpace.Editor` | Embeddable Uno surface, direct manipulation and gesture state |
| `ArtSpace.Workbench` | Illustration workspace, command routing, property and file workflows |

The first six libraries have no Uno dependency. The application host supplies platform startup, fonts and storage; there is no static document singleton.

```csharp
using ArtSpace.Documents;
using ArtSpace.Editing;
using ArtSpace.Workbench;

var session = new EditorSession(IllustrationSample.Create());
// storage implements ArtSpace.Documents.IWorkspaceStorage.
window.Content = new StudioWorkbench(session, storage);
```

For headless edits, consume `PathEditing` and `PathOperations` without a window. Matching Skia native assets are required on the executing platform. CI package artifacts are not a claim of publication to nuget.org.

## Rendering and verification

Painting uses Uno **SKCanvasElement** and its shared Skia composition path instead of an extra application-owned CPU bitmap/upload layer. Actual hardware acceleration and graphics backend depend on the host/browser/driver. Path construction, conversion and Boolean operations remain CPU geometry work; this is not a raw-WebGPU or GPU-compute-only engine.

The engine suite contains **107 regression cases**. Seven browser scenarios use the published Uno app and real pointer/keyboard/file-picker input: drawing, transforms, undo/redo, save/recovery, tools/artboards, menus, compact layout, imported contour editing, text outlines and cancelled anchor gestures.

```bash
npm ci
npx playwright install chromium
# Start the static server above first.
npx playwright test tests/browser/illustration.spec.mjs
```

The optional `?test=1` state is read-only. Tests do not mutate documents through a hidden API. CI Chromium uses software graphics and is not a physical-GPU benchmark. Native Windows/Linux/macOS compilation is separate from interactive native validation. Workflow conclusions and retained screenshots/traces are the source of truth for each commit.

## Delivery

**Build** runs regressions, compiles native hosts, publishes WebAssembly, packs all libraries and tests browser interactions. **Pages** deploys only successful main-branch Build artifacts, checks `build-info.json` provenance and runs acceptance against the public URL. **Release** produces tagged browser/native/source archives, packages and checksums; NuGet publication requires `NUGET_API_KEY` in the `nuget` environment.

## Compatibility boundaries

Native AI/EPS/PDF interchange, CMYK/ICC/spot-color production, overprint/separations, advanced complex-script/OpenType layout, meshes, perspective tools, tracing, pressure/pattern brushes, arbitrary masks, multiple open documents and Adobe plugins remain unimplemented. SVG interchange is a safe editable subset, not lossless Illustrator roundtripping. Imported scripts and external active content are not executed.

Direct selection edits one object's contours at a time. Rational conics become adaptively approximated cubics on editing; text outlines reproduce ArtSpace's current basic text layout. Local recovery is not a backup: download a native copy for safekeeping.

## Documentation and license

[Getting started](docs/getting-started.md) · [Path editing](docs/path-editing.md) · [Architecture](docs/architecture.md) · [Feature matrix](docs/feature-matrix.md) · [Contributing](CONTRIBUTING.md) · [Changelog](CHANGELOG.md)

ArtSpace is [MIT-licensed](LICENSE). Its shared engine derives from the author's MIT [VectorSpace](https://github.com/wieslawsoltes/VectorSpace) snapshot `587f780f2803f6b3aa59dfb5411da2c2fe601dfa`; attribution is retained in [third-party notices](THIRD-PARTY-NOTICES.md).

Adobe Illustrator is the requested design/interaction reference. ArtSpace contains no Adobe source code, proprietary artwork, logos or product icons and is not affiliated with or endorsed by Adobe.
