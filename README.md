<div align="center">

# ArtSpace

### Vector illustration. One C# engine. Desktop and browser.

A local-first illustration editor built with **Uno Platform** and **SkiaSharp**, with an Illustrator-style dark workspace and nine reusable .NET libraries.

[Open ArtSpace](https://wieslawsoltes.github.io/ArtSpace/) · [Getting started](docs/getting-started.md) · [Appearance and rendering](docs/appearance-rendering.md) · [Feature matrix](docs/feature-matrix.md)

[![Build](https://github.com/wieslawsoltes/ArtSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/ArtSpace/actions/workflows/build.yml)
[![Pages](https://github.com/wieslawsoltes/ArtSpace/actions/workflows/pages.yml/badge.svg)](https://github.com/wieslawsoltes/ArtSpace/actions/workflows/pages.yml)
[![MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

</div>

---

## An editable illustration workspace

ArtSpace shares its C# document model, transaction engine, geometry, custom controls and workbench between native desktop hosts and real Uno WebAssembly. JavaScript is confined to file transfer, local recovery, input adaptation and opt-in read-only diagnostics. There is no separate JavaScript editor hidden beneath an HTML mock-up.

The compact dark shell combines menus, appearance controls, toolbox, rulers, pasteboard, artboards, layers, properties, history, Appearance, Graphic Styles and a status bar. The original **Alpine Echoes** sample is editable vector artwork across three artboards.

**Version: `0.5.0-alpha.1`.** This is an independent functional alpha, not complete or pixel-identical Adobe Illustrator parity. The [feature matrix](docs/feature-matrix.md) distinguishes implemented behavior and remaining compatibility boundaries.

## New in 0.5

**Live Appearance:** non-destructive Gaussian Blur, Drop Shadow, Outer Glow and Saturation, with editable parameters, enablement, ordering, duplication and removal. Effects compose with masks and object transparency without replacing editable geometry.

**Graphic Styles:** document-local presets with native vector previews, independent appearance copies, apply/rename/delete and symbol appearance overrides. The retained panel materializes twelve style slots per page rather than an entire large library.

**Stroke and compositing controls:** solid/linear/radial gradient strokes, dash offsets, odd-length dash sequences, independent fill/stroke ordering and duplication, and nine additional blend modes. Supported SVG interchange retains gradient-stroke paints and phase.

**Retained native rendering:** unchanged scenes replay native vector display lists directly into Uno's existing Skia canvas. Selection and editing overlays remain separate. Active editing uses the direct path with retained paints, shaders, dashes and effect graphs. Resource budgets and invalidation preserve a direct-render fallback; no frame is encoded into an image for display.

[Read the workflows, architecture, API contract and measurement boundaries](docs/appearance-rendering.md).

## Capabilities

| Area | Implemented workflows |
| --- | --- |
| Drawing | Primitives, pen/cubic paths, pencil, fixed-width brush, text, artboards and slices |
| Editing | Selection, marquee, transforms, duplication, grouping, order, alignment, distribution, clipboard and transactional undo/redo |
| Contours | Native/imported/expanded/compound/glyph paths, multiple anchors, tangent editing, exact cubic insertion, reconnecting removal, segment cutting and smooth/corner conversion |
| Geometry | Shape/stroke expansion, offsets, Boolean union/subtract/intersect/exclude and compound paths with nonzero/even-odd rules |
| Appearance | Multiple fills/strokes, gradient fills/strokes, dashed strokes with phase, sixteen blend modes, ordered live effects, legacy shadows and appearance presets |
| Masking | Vector clipping and editable nested alpha/luminance masks, inversion, enablement and independent source/content selection |
| Text | Basic wrapping, tracking/alignment and Create Outlines using the same glyph runs as painting |
| Reuse | Bounded editable blends/repeats, linked local symbols/components and graphic styles |
| Files | Native JSON, supported SVG interchange, SVG/PNG export and IndexedDB/native recovery |
| Performance | Retained inspector/layer controls, indexed selection/snapping, lazy drag transactions, cached native paints/filters and vector scene replay |

The 0.4.1 selection improvements remain: plain selection clicks do not serialize a document or build snapping data, hidden panels defer work, and ordinary value refreshes retain focus and expansion. See [UI responsiveness](docs/ui-performance.md).

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

## Desktop downloads and packages

Use the assets attached to a [tagged release](https://github.com/wieslawsoltes/ArtSpace/releases). The release workflow builds self-contained, single-file desktop applications:

| OS | x64 | Arm64 |
| --- | --- | --- |
| Windows | `ArtSpace-<version>-win-x64.zip` | `ArtSpace-<version>-win-arm64.zip` |
| macOS | `ArtSpace-<version>-osx-x64.tar.gz` | `ArtSpace-<version>-osx-arm64.tar.gz` |
| Linux | `ArtSpace-<version>-linux-x64.tar.gz` | `ArtSpace-<version>-linux-arm64.tar.gz` |

Extract and run `ArtSpace` (`ArtSpace.exe` on Windows). Verify downloaded assets against `SHA256SUMS.txt`; desktop builds are not yet code-signed. Availability is determined by the actual release assets, not the source version alone.

Tagged release publishing also supports all nine libraries on [NuGet.org](https://www.nuget.org/packages?q=ArtSpace). A source commit or PR build does not by itself publish a new package version. CI package artifacts are available independently of tagged releases.

## Reusable libraries

| Package | Responsibility |
| --- | --- |
| `ArtSpace.Core` | Document, live appearance, graphic styles, affine geometry and managed editable contours |
| `ArtSpace.Layout` | Constraints, layout and reference/indexed snapping |
| `ArtSpace.Documents` | Validated native schemas, clipboard and SVG subset |
| `ArtSpace.Editing` | Selection, transactions, history and linked components/overrides |
| `ArtSpace.Skia` | Cached paths/text/paints/shaders/filters, native display lists, compositing, hit testing and raster export |
| `ArtSpace.Illustration` | Appearance/style commands, expansion, offsets, compounds, masks, blends and repeats |
| `ArtSpace.Controls` | Retained inspectors, custom menus, panels, fields, resize grips, vector icons and layer rows |
| `ArtSpace.Editor` | Embeddable Uno drawing surface and direct-manipulation gestures |
| `ArtSpace.Workbench` | Application workspace, Appearance/Graphic Styles, properties, command routing and storage workflows |

The first six packages do not depend on Uno. Hosts own lifetime, fonts and an `IWorkspaceStorage` implementation. `AppearanceOperations`, `OpacityMaskOperations`, `PathOperations` and `PathEditing` are usable independently of the shell.

```csharp
using ArtSpace.Documents;
using ArtSpace.Editing;
using ArtSpace.Workbench;

var session = new EditorSession(IllustrationSample.Create());
window.Content = new StudioWorkbench(session, storage);
```

Apply non-destructive appearance from another host:

```csharp
using ArtSpace.Core;
using ArtSpace.Illustration;

session.Select(artwork);
AppearanceOperations.AddEffect(session, LiveEffectKind.DropShadow);
AppearanceOperations.UpdateEffect(session, 0, "Shadow radius", effect => effect.Radius = 8);
var styleId = AppearanceOperations.CaptureStyle(session, "Soft shadow");

session.Select(otherArtwork);
AppearanceOperations.ApplyStyle(session, styleId);
```

Skia consumers need compatible native assets for the executing platform. Reusable renderer callers must invalidate the retained scene after model mutations; the direct renderer continues to inspect mutable values. See the [ownership contract](docs/appearance-rendering.md#native-rendering-architecture).

## Verification and performance

The repository registers **239 engine cases**, **five additional benchmark boundary checks** and **21 browser scenarios**. Browser tests exercise real pointer, keyboard and file-picker input against the published Uno app. Diagnostics enabled by `?test=1` are read-only, not an editing API. A completed workflow result identifies which commit passed.

```bash
npm ci
npx playwright install chromium
npx playwright test # after starting the local static server

dotnet run --project tests/ArtSpace.Tests -c Release --no-build -- --benchmark
dotnet run --project tests/ArtSpace.Tests -c Release --no-build -- --appearance-benchmark
```

Build validates engine regressions, reference-pixel benchmarks, Windows/Linux/macOS compilation, browser publication/acceptance and all nine packages. Pages deploys successful main-branch artifacts, verifies `build-info.json` and tests the public application. Release runs for `v*` tags or a supplied manual version and repeats engine/browser gates before packaging. Tagged publication uses NuGet Trusted Publishing from the protected `nuget` environment; manual release runs are dry runs that upload workflow artifacts.

The appearance benchmark alternates warm direct and retained drawing on the same software surface, checks identical pixels, and records timing/allocation samples plus native cache counters. It excludes recording cost. Normal rendering uses Uno **SKCanvasElement** and its shared Skia composition path, not an extra application-owned CPU framebuffer. Hardware acceleration depends on the host/browser/driver; parsing, path operations, snapshots and parts of layout remain CPU-side. CI Chromium uses SwiftShader. Software timings are not physical-GPU certification or whole-application frame rates.

## Compatibility

**Native saves use schema 4.** ArtSpace 0.5 reads schemas 1–4 and upgrades older documents when saving. Earlier readers reject schema 4 rather than silently dropping new appearance semantics. Preserve an original copy for older-version workflows. Autosave is local recovery, not durable backup.

Supported SVG gradients and masks are not full SVG/Illustrator roundtripping. Opacity-mask import currently requires user-space units. Inverted masks and enabled live effects retain native/PNG output but SVG export is explicitly rejected. General SVG filters, image/use content, stylesheet cascade and linear-light paint interpolation remain outside the supported subset.

Advanced shaping, variable-font workflows, type-on-path, gradient meshes, the complete Illustrator effect catalog, arbitrary per-paint effect graphs, image tracing, pressure/art/pattern brushes, AI/EPS/PDF interchange, CMYK/ICC/spot/overprint production, multiple documents and arbitrary floating docking remain unfinished. Direct contour editing works on one object's contours at a time; rational curves are approximated on editing. Appearance radius calibration and UI layout are not asserted to be pixel-identical to Illustrator.

## Documentation and license

[Getting started](docs/getting-started.md) · [Architecture](docs/architecture.md) · [Appearance/rendering](docs/appearance-rendering.md) · [Contours/outlines](docs/path-editing.md) · [Clipping/performance](docs/clipping-and-performance.md) · [Opacity/gradients](docs/opacity-masks-and-gradients.md) · [UI performance](docs/ui-performance.md) · [Contributing](CONTRIBUTING.md)

ArtSpace is [MIT-licensed](LICENSE), derived from the author's MIT [VectorSpace](https://github.com/wieslawsoltes/VectorSpace) snapshot `587f780f2803f6b3aa59dfb5411da2c2fe601dfa`. Attribution is retained in [third-party notices](THIRD-PARTY-NOTICES.md).

Adobe Illustrator is a design and interaction reference. ArtSpace contains no Adobe source code, proprietary artwork, logos or product icons and is not affiliated with or endorsed by Adobe.
