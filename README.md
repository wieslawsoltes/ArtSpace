<div align="center">

# ArtSpace

### Vector illustration. One C# engine. Desktop and browser.

A local-first illustration editor built with **Uno Platform** and **SkiaSharp**, with an Illustrator-style dark workspace and nine reusable .NET libraries.

[Open ArtSpace](https://wieslawsoltes.github.io/ArtSpace/) · [Getting started](docs/getting-started.md) · [Appearance and rendering](docs/appearance-rendering.md) · [Feature matrix](docs/feature-matrix.md)

[![Build](https://github.com/wieslawsoltes/ArtSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/ArtSpace/actions/workflows/build.yml)
[![Pages](https://github.com/wieslawsoltes/ArtSpace/actions/workflows/pages.yml/badge.svg)](https://github.com/wieslawsoltes/ArtSpace/actions/workflows/pages.yml)
[![MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

[![NuGet](https://img.shields.io/nuget/vpre/ArtSpace.Core.svg?label=NuGet)](https://www.nuget.org/packages/ArtSpace.Core)
[![Downloads](https://img.shields.io/nuget/dt/ArtSpace.Core.svg)](https://www.nuget.org/packages/ArtSpace.Core)

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

## NuGet packages

All nine libraries are MIT-licensed, versioned together with the app and published to [NuGet.org](https://www.nuget.org/packages?q=ArtSpace) on tagged releases, with symbol packages (`.snupkg`) and SourceLink. The first six packages target `net10.0` and do not depend on Uno (only `ArtSpace.Skia` and `ArtSpace.Illustration` need SkiaSharp); `ArtSpace.Controls`, `ArtSpace.Editor` and `ArtSpace.Workbench` are Uno Platform libraries targeting `net10.0-desktop` and `net10.0-browserwasm`. Hosts own lifetime, fonts and an `IWorkspaceStorage` implementation. Skia consumers need compatible native assets for the executing platform.

```bash
dotnet add package ArtSpace.Core --prerelease
```

| Package | Version | Downloads | Description |
|---|---|---|---|
| [ArtSpace.Core](https://www.nuget.org/packages/ArtSpace.Core) | [![NuGet](https://img.shields.io/nuget/vpre/ArtSpace.Core.svg)](https://www.nuget.org/packages/ArtSpace.Core) | [![Downloads](https://img.shields.io/nuget/dt/ArtSpace.Core.svg)](https://www.nuget.org/packages/ArtSpace.Core) | Document, appearance, affine geometry and managed editable contours. |
| [ArtSpace.Layout](https://www.nuget.org/packages/ArtSpace.Layout) | [![NuGet](https://img.shields.io/nuget/vpre/ArtSpace.Layout.svg)](https://www.nuget.org/packages/ArtSpace.Layout) | [![Downloads](https://img.shields.io/nuget/dt/ArtSpace.Layout.svg)](https://www.nuget.org/packages/ArtSpace.Layout) | Constraints, auto-layout and reference/indexed snapping. |
| [ArtSpace.Documents](https://www.nuget.org/packages/ArtSpace.Documents) | [![NuGet](https://img.shields.io/nuget/vpre/ArtSpace.Documents.svg)](https://www.nuget.org/packages/ArtSpace.Documents) | [![Downloads](https://img.shields.io/nuget/dt/ArtSpace.Documents.svg)](https://www.nuget.org/packages/ArtSpace.Documents) | Validated native JSON schemas, node clipboard, SVG subset and storage contract. |
| [ArtSpace.Editing](https://www.nuget.org/packages/ArtSpace.Editing) | [![NuGet](https://img.shields.io/nuget/vpre/ArtSpace.Editing.svg)](https://www.nuget.org/packages/ArtSpace.Editing) | [![Downloads](https://img.shields.io/nuget/dt/ArtSpace.Editing.svg)](https://www.nuget.org/packages/ArtSpace.Editing) | Selection, transactions, undo/redo history, linked components and viewport. |
| [ArtSpace.Skia](https://www.nuget.org/packages/ArtSpace.Skia) | [![NuGet](https://img.shields.io/nuget/vpre/ArtSpace.Skia.svg)](https://www.nuget.org/packages/ArtSpace.Skia) | [![Downloads](https://img.shields.io/nuget/dt/ArtSpace.Skia.svg)](https://www.nuget.org/packages/ArtSpace.Skia) | Cached paths/text/shaders, compositing, masks, culling, hit testing, Boolean paths and raster export. |
| [ArtSpace.Illustration](https://www.nuget.org/packages/ArtSpace.Illustration) | [![NuGet](https://img.shields.io/nuget/vpre/ArtSpace.Illustration.svg)](https://www.nuget.org/packages/ArtSpace.Illustration) | [![Downloads](https://img.shields.io/nuget/dt/ArtSpace.Illustration.svg)](https://www.nuget.org/packages/ArtSpace.Illustration) | Expansion, offsets, compounds, clipping/opacity-mask commands, blends, repeats and anchor editing. |
| [ArtSpace.Controls](https://www.nuget.org/packages/ArtSpace.Controls) | [![NuGet](https://img.shields.io/nuget/vpre/ArtSpace.Controls.svg)](https://www.nuget.org/packages/ArtSpace.Controls) | [![Downloads](https://img.shields.io/nuget/dt/ArtSpace.Controls.svg)](https://www.nuget.org/packages/ArtSpace.Controls) | Custom Uno menus, panel dock, fields, resize grips, vector icons and layer rows. |
| [ArtSpace.Editor](https://www.nuget.org/packages/ArtSpace.Editor) | [![NuGet](https://img.shields.io/nuget/vpre/ArtSpace.Editor.svg)](https://www.nuget.org/packages/ArtSpace.Editor) | [![Downloads](https://img.shields.io/nuget/dt/ArtSpace.Editor.svg)](https://www.nuget.org/packages/ArtSpace.Editor) | Embeddable Uno/Skia drawing surface with direct-manipulation, pen and anchor tools. |
| [ArtSpace.Workbench](https://www.nuget.org/packages/ArtSpace.Workbench) | [![NuGet](https://img.shields.io/nuget/vpre/ArtSpace.Workbench.svg)](https://www.nuget.org/packages/ArtSpace.Workbench) | [![Downloads](https://img.shields.io/nuget/dt/ArtSpace.Workbench.svg)](https://www.nuget.org/packages/ArtSpace.Workbench) | Complete illustration workspace: menus, properties, layers, command routing and storage workflows. |

Dependencies follow the project references: `Layout` and `Documents` → `Core`; `Editing` → `Layout` + `Documents`; `Skia` → `Editing` + SkiaSharp; `Illustration` → `Skia`; `Controls` is standalone Uno; `Editor` → `Controls` + `Skia`; `Workbench` → `Editor` + `Illustration`. `OpacityMaskOperations`, `PathOperations` and `PathEditing` are reusable independently of the shell.

### ArtSpace.Core

The serializable illustration model: pages, artboards and nodes with multiple fills/strokes, gradients, blends, shadows, clipping paths and alpha/luminance opacity masks, plus double-precision affine geometry and `EditablePath`, a managed multi-contour cubic path used for anchor editing. No dependencies and no UI.

```bash
dotnet add package ArtSpace.Core --prerelease
```

**Key types** (namespace `ArtSpace.Core`)

- `DesignDocument` / `DesignPage` / `DesignNode` – node tree with `Fills`, `Strokes`, `ClipPathId`, `OpacityMaskId`, `WorldBounds`.
- `FillStyle`, `StrokeStyle`, `GradientStop` – solid and linear/radial gradient paints with spread and transforms.
- `EditablePath` – contours of `PathPoint`s with `Split`, `Remove`, `Cut`, `Reverse`, `Smooth`, `HitSegment`, `ToSvgPathData`.
- `Vec2`, `RectD`, `Matrix2D`, `AffineGeometry` – immutable geometry and affine validation.

**Usage**

```csharp
using ArtSpace.Core;

var document = new DesignDocument { Name = "Poster" };
var artboard = new DesignNode { Kind = NodeKind.Frame, Name = "A4", Width = 595, Height = 842, Fill = "#FFFFFF" };
document.Pages[0].Nodes.Add(artboard);
var sun = artboard.Add(new DesignNode { Kind = NodeKind.Ellipse, Name = "Sun", X = 200, Y = 120, Width = 180, Height = 180 });
sun.Fills[0] = new FillStyle { Kind = FillKind.RadialGradient };
sun.Strokes.Add(new StrokeStyle { Color = "#203F49", Width = 3 });

var path = new EditablePath();
var contour = new EditablePath.Contour { Closed = true };
contour.Points.Add(new PathPoint { Position = new Vec2(0, 0) });
contour.Points.Add(new PathPoint { Position = new Vec2(100, 0), ControlIn = new Vec2(60, -40) });
contour.Points.Add(new PathPoint { Position = new Vec2(50, 80) });
path.Contours.Add(contour);
path.Split(new EditablePath.Address(0, 0), 0.5);           // insert an anchor on the first segment
Console.WriteLine($"{path.AnchorCount} anchors: {path.ToSvgPathData()}");
```

### ArtSpace.Layout

Auto-layout, constraints and snapping for artboards and groups, including a prebuilt `SnapIndex` so repeated drags do not rescan stationary targets. Depends on `ArtSpace.Core`; no UI.

```bash
dotnet add package ArtSpace.Layout --prerelease
```

**Key types**

- `LayoutEngine.Arrange(node)` / `Arrange(roots)` – lays out auto-layout frames.
- `LayoutEngine.Resize(node, width, height)` – resizes and applies child constraints.
- `SnapIndex` – indexed stationary targets with `Snap(moving, tolerance, guides)`.
- `SnapEngine.Snap(...)` – one-shot snapping; `SnapResult` carries the `Correction` and `SnapLine`s.

**Usage**

```csharp
using ArtSpace.Core;
using ArtSpace.Layout;

var row = new DesignNode { Kind = NodeKind.Frame, Name = "Swatches" };
row.Layout = new AutoLayout { Direction = LayoutDirection.Horizontal, Gap = 8, HugWidth = true, HugHeight = true };
for (var i = 0; i < 3; i++) row.Add(new DesignNode { Name = $"Swatch {i}", Width = 40, Height = 40 });
LayoutEngine.Arrange(row);

var index = new SnapIndex(row.Children.Select(c => c.WorldBounds));
SnapResult snap = index.Snap(new RectD(62, 18, 40, 40), tolerance: 4);
Console.WriteLine($"{row.Width} x {row.Height}, correction {snap.Correction}");
```

### ArtSpace.Documents

Native JSON (schema 4, reading 1–4) with validation, node clipboard serialization, the supported SVG subset (paths, gradients, masks and affine transforms, returning warnings for anything skipped), the original sample artwork and the `IWorkspaceStorage` host contract. Depends on `ArtSpace.Core`; no UI.

```bash
dotnet add package ArtSpace.Documents --prerelease
```

**Key types**

- `DocumentJson` – `Load`, `Save`, `Validate`, `CloneNode`, `SaveNodes`/`LoadNodes`.
- `SvgFormat` – `Export(roots, bounds)` and `Import(svg)` returning `SvgImportResult` (document + warnings).
- `IllustrationSample` / `SampleDocument` – the editable Alpine Echoes artwork and a basic sample.
- `IWorkspaceStorage` – autosave, open and save operations implemented by each host.

**Usage**

```csharp
using ArtSpace.Core;
using ArtSpace.Documents;

DesignDocument document = IllustrationSample.Create();
File.WriteAllText("alpine.artspace", DocumentJson.Save(document));
var reloaded = DocumentJson.Load(File.ReadAllText("alpine.artspace"));   // validated on load

var artboard = reloaded.Pages[0].Nodes[0];
File.WriteAllText("artboard.svg", SvgFormat.Export([artboard], artboard.WorldBounds));

SvgImportResult imported = SvgFormat.Import(File.ReadAllText("logo.svg"));
foreach (var warning in imported.Warnings) Console.WriteLine(warning);
```

### ArtSpace.Editing

The headless editor: selection, transactional edits with bounded undo/redo, move/duplicate/group/align/distribute, clipboard and linked components, plus a viewport. Depends on `Core`, `Layout` and `Documents`; no UI.

```bash
dotnet add package ArtSpace.Editing --prerelease
```

**Key types**

- `EditorSession` – `Document`, `Page`, `Selection`, `Tool`, `Edit(label, action)`, `Undo()`/`Redo()`, `Changed`.
- `EditorSession.MoveSelection`, `DuplicateSelection`, `GroupSelection`, `Align`, `Distribute`, `CopySelection`/`Paste`.
- `ComponentService` – `MakeComponent`, `InsertInstance`, `SetOverride`, `Synchronize(document)`.
- `Viewport` – zoom/pan with `WorldToScreen`, `ScreenToWorld`, `ZoomAt`, `Fit`.

**Usage**

```csharp
using ArtSpace.Core;
using ArtSpace.Documents;
using ArtSpace.Editing;

var session = new EditorSession(IllustrationSample.Create());
session.Changed += (_, e) => Console.WriteLine($"{e.Kind}: {e.Label}");

var badge = new DesignNode { Kind = NodeKind.Star, Name = "Badge", Width = 120, Height = 120, Fill = "#E6AA67" };
session.Edit("Add badge", () => session.AddNode(badge));   // one undoable transaction
session.Select(badge);
session.DuplicateSelection(offset: 24);
session.Align("left");

Console.WriteLine($"Undo: {session.UndoLabel}");
session.Undo();
```

### ArtSpace.Skia

The SkiaSharp renderer shared by the editor and headless tools: retained path/text/gradient caches, blending, clipping and opacity-mask compositing, culling, hit testing, glyph outlines, Boolean operations, `SKPath` ↔ `EditablePath` conversion and PNG export. Depends on `ArtSpace.Editing` and SkiaSharp; no Uno dependency.

```bash
dotnet add package ArtSpace.Skia --prerelease
```

**Key types**

- `SceneRenderer` – `Draw(canvas, nodes)`, `HitTest(roots, point)`, `Geometry(node)`, `CreateTextOutline`, `ExportPng`.
- `PathEditing` – `Read(node, renderer)` to an `EditablePath` and `Write(node, path)` back to the node.
- `BooleanOperations.Apply(editor, renderer, BooleanOperation.Union)` – union/subtract/intersect/exclude.

**Usage**

```csharp
using ArtSpace.Core;
using ArtSpace.Documents;
using ArtSpace.Editing;
using ArtSpace.Skia;

var session = new EditorSession(IllustrationSample.Create());
using var renderer = new SceneRenderer();
var artboard = session.Page.Nodes[0];
File.WriteAllBytes("artboard.png", renderer.ExportPng([artboard], artboard.WorldBounds, 2));

if (renderer.HitTest(session.Page.Nodes, new Vec2(200, 150), deep: true) is { } node && PathEditing.CanEdit(node))
{
    EditablePath path = PathEditing.Read(node, renderer);
    path.Reverse();
    session.Edit("Reverse path", () => PathEditing.Write(node, path));
}
```

### ArtSpace.Illustration

Illustrator-style commands that operate on an `EditorSession` selection as single undoable transactions: expand shapes, outline strokes, offset paths, compound paths, clipping and opacity masks, blends, radial repeats, text outlines and anchor editing. Depends on `ArtSpace.Skia`; no Uno dependency.

```bash
dotnet add package ArtSpace.Illustration --prerelease
```

**Key types**

- `IllustrationOperations` – `ExpandShapes`, `OutlineStrokes`, `OffsetPaths`, `Blend`, `RadialRepeat`.
- `PathOperations` – `MakeCompound`/`ReleaseCompound`, `CreateOutlines`, `AddAnchors`, `Reverse`, `Close`.
- `ClippingOperations` – `Make`/`Release` clipping groups, edit mask or contents.
- `OpacityMaskOperations` – `Make(editor, mode)`, `Release`, `Invert`, `ToggleEnabled`, `SetMode`.

**Usage**

```csharp
using ArtSpace.Core;
using ArtSpace.Documents;
using ArtSpace.Editing;
using ArtSpace.Illustration;
using ArtSpace.Skia;

var document = new DesignDocument();
var back = new DesignNode { Kind = NodeKind.Ellipse, Name = "Back", Width = 120, Height = 120, Fill = "#E6AA67" };
var front = new DesignNode { Kind = NodeKind.Ellipse, Name = "Front", X = 300, Width = 60, Height = 60, Fill = "#203F49" };
document.Pages[0].Nodes.AddRange([back, front]);
var session = new EditorSession(document);
using var renderer = new SceneRenderer();

session.Select([back.Id, front.Id]);
IllustrationOperations.Blend(session, steps: 5);            // each call is one undoable edit
session.Undo();

session.Select([back.Id, front.Id]);
PathOperations.MakeCompound(session, renderer);
IllustrationOperations.OffsetPaths(session, renderer, 6);
Console.WriteLine(string.Join(", ", session.History));
```

### ArtSpace.Controls

Compact dark-theme Uno Platform controls with shared design tokens: a keyboard-navigable command menu bar, tabbed panel dock with resize grips, scrubbable numeric fields, color fields with a spectrum picker, segmented controls, inspector sections, retained inspector bindings, layer rows and original vector icons. It has no document or editor dependency; requires Uno Platform (Skia renderer).

```bash
dotnet add package ArtSpace.Controls --prerelease
```

**Key types**

- `Studio` – tokens and helpers such as `Font`, `Brush`, `Text`, `Input`, `Columns`, `Surface`.
- `CommandMenuBar` / `MenuCommand` – application menus with shortcuts and enablement.
- `PanelDock` / `DockResizeGrip` – tabbed side panels and drag-resizable docks.
- `NumericField`, `ColorField`, `SegmentedControl`, `InspectorSection`, `IconButton`, `LayerRow`.

**Usage**

```csharp
using ArtSpace.Controls;
using Microsoft.UI.Xaml.Controls;

var menu = new CommandMenuBar();
menu.Add("Object", () => [new MenuCommand("Group", () => Console.WriteLine("group"), "Ctrl+G")]);

var stroke = new InspectorSection("Stroke");
stroke.Body.Children.Add(new ColorField("#203F49", hex => Console.WriteLine(hex)));
stroke.Body.Children.Add(new NumericField("Weight", 1, w => Console.WriteLine(w)) { Minimum = 0 });

var dock = new PanelDock();
dock.Add("Properties", stroke);
window.Content = new StackPanel { Children = { menu, dock } };
```

### ArtSpace.Editor

The embeddable drawing surface: an Uno `SKCanvasElement`-based canvas with selection and direct-selection, pen/pencil/brush/shape/text tools, anchor and handle editing, rulers, snapping and artboard presentation. Bind it to an `EditorSession` and add your own chrome. Depends on `ArtSpace.Controls` and `ArtSpace.Skia`; requires Uno Platform.

```bash
dotnet add package ArtSpace.Editor --prerelease
```

**Key types**

- `DesignSurface` – `Session`, `Renderer`, `FillColor`/`StrokeColor`/`StrokeWidth`, `Fit()`, `ZoomTo`, `Invalidate()`, `StatusChanged`.
- `DesignSurface.EnterPathEditing`, `GetPathAnchors`, `EditSelectedAnchors`, `RemoveSelectedAnchors` – contour editing.
- `AnchorInfo` – anchor/handle state exposed for panels and diagnostics.
- `Keyboard` – current modifier-key state (`Control`, `Shift`, `Alt`).

**Usage**

```csharp
using ArtSpace.Documents;
using ArtSpace.Editing;
using ArtSpace.Editor;

var session = new EditorSession(IllustrationSample.Create());
var surface = new DesignSurface { Session = session, FillColor = "#E6AA67", StrokeWidth = 2 };
surface.StatusChanged += message => Console.WriteLine(message);
session.Tool = EditorTool.Pen;
window.Content = surface;
```

### ArtSpace.Workbench

The complete ArtSpace workspace as one control: menus, appearance bar, toolbox, rulers, artboards, layers/properties/history panels, command routing, clipboard and storage workflows. The host supplies an `EditorSession` and an `IWorkspaceStorage` (browser, desktop or your own). Depends on `ArtSpace.Editor` and `ArtSpace.Illustration`; requires Uno Platform.

```bash
dotnet add package ArtSpace.Workbench --prerelease
```

**Key types**

- `StudioWorkbench(EditorSession session, IWorkspaceStorage storage)` – the workbench `UserControl`.
- `StudioWorkbench.Surface` – the hosted `DesignSurface` (renderer, typeface, focus).
- `StudioWorkbench.ShowStatus(message, error)` and `HandleHostNavigation(key)`.
- `IWorkspaceStorage` – implement for autosave, open and save.

**Usage**

```csharp
using ArtSpace.Documents;
using ArtSpace.Editing;
using ArtSpace.Workbench;

var session = new EditorSession(IllustrationSample.Create());
// Supply an IWorkspaceStorage implementation for autosave, open and save.
var workbench = new StudioWorkbench(session, storage);
window.Content = workbench;
window.Closed += (_, _) => workbench.Dispose();
window.Activate();
```

### Live appearance from another host

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

The repository registers **247 engine cases**, **five additional benchmark boundary checks** and **21 browser scenarios**. Browser tests exercise real pointer, keyboard and file-picker input against the published Uno app. Diagnostics enabled by `?test=1` are read-only, not an editing API. A completed workflow result identifies which commit passed.

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
