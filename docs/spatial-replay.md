# Native spatial scene replay

ArtSpace records settled document content as native Skia vector commands and replays it into Uno's composition canvas. Interactive overlays remain separate, and active document gestures use the direct renderer. The picture is not a screenshot or an application-owned pixel framebuffer.

## Clip-aware native playback

The retained picture now uses the R-tree bounding-box hierarchy exposed by the pinned SkiaSharp 3.119.2 `SKPictureRecorder.BeginRecording(SKRect, bool)` API. Skia indexes the drawing commands during recording and uses the destination clip to select commands during replay. This reduces command replay work for a zoomed viewport within a larger recorded scene.

This index is distinct from the document's snapping index. It neither changes node ownership nor makes document traversal, parsing, editing, hit testing or snapshots GPU-only. Skia can execute the retained vector commands on the graphics backend provided by Uno; physical GPU acceleration depends on that host and its driver/browser configuration.

Managed `QuickReject` culling is disabled while recording, because a scale-dependent rejection must not be frozen into commands later replayed at another zoom. The native hierarchy evaluates destination clipping. Filtered offscreen sources and clipping/opacity-mask composition must still match the direct renderer, which remains the reference path.

## Ownership and configuration

```csharp
using ArtSpace.Skia;

using var renderer = new SceneRenderer
{
    EnableRetainedScene = true,
    EnableSceneSpatialIndex = true,
    RetainedSceneBudgetBytes = 32 * 1024 * 1024
};

// canvas is the host-owned Skia canvas with its current viewport transform.
renderer.DrawRetained(canvas, page, visibleWorldBounds);

// After changing document content:
renderer.InvalidateRetainedScene();
```

`EnableSceneSpatialIndex` defaults to true. Turning it off records an unindexed native picture for controlled comparison. Changing it invalidates the existing picture. Assigning the same configuration values does not invalidate or re-record a settled scene.

Disabling retention releases the recorded commands immediately. Changing the command budget invalidates the old picture and clears a previous over-budget rejection. This fixes the case where raising a budget could leave a scene permanently using direct drawing until another document edit occurred.

Values at or below zero disable recording. An oversized picture is disposed after recording, then drawing falls back to the direct path. With unchanged configuration/content, that rejected scene is not recorded repeatedly. The budget is based on Skia's **approximate native command-storage accounting**, not a hard bound on transient/native/GPU allocations, images, fonts or filter intermediates.

## Reproduce the comparison

```bash
dotnet run --project tests/ArtSpace.Tests -c Release
dotnet run --project tests/ArtSpace.Tests -c Release --no-build -- --spatial-benchmark
```

The permanent Build workflow retains `spatial-replay.json` in `ArtSpace-performance`. The fixture uses 6,400 leaves plus one group, first fits the document, then replays 256×256 zoomed viewports. Seven samples of thirty frames alternate indexed and unindexed execution order.

Both paths use the same current renderer and retained paint resources. Twenty-four comparisons require exact pixel equality against direct drawing before timings are accepted. Warm replay must not rebuild pictures or paints. The report includes all samples, median batch times, managed allocation, recording/replay counts and approximate command bytes.

Cold fit measurements are reported separately and include resource construction, recording and first playback. They run in fixed indexed-then-unindexed order; that order, runtime warmup and host load affect them. Warm measurements exclude index construction and recording. This is a software-Skia CPU comparison, **not a physical-GPU measurement, complete application FPS benchmark or previous-release speed comparison**.

## Regression coverage

Nine additional engine cases cover exact indexed/direct/unindexed pixels for overflowing groups, transformed/dashed artwork, visible shadows from offscreen sources, combined vector/luminance masking, mask inversion, and zoom/clip changes. They also cover cache rejection recovery after increasing the budget, immediate release on disabling retention, equal-value configuration reuse, zero-budget re-enablement and preservation of caller canvas state.

The existing browser acceptance suite exercises the indexed default through the real Uno WebAssembly host, alongside live effect/style editing and selection-cache reuse. A successful compile alone does not prove that the required native recording entry point executes correctly; runtime browser tests remain a release gate.

## Primary implementation references

- [Pinned SkiaSharp recorder source](https://github.com/mono/SkiaSharp/blob/v3.119.2/binding/SkiaSharp/SKPictureRecorder.cs)
- [Skia picture recorder](https://api.skia.org/classSkPictureRecorder.html)
- [Uno SKCanvasElement](https://platform.uno/docs/articles/controls/SKCanvasElement.html)
- [Appearance and rendering](appearance-rendering.md)

Native schema remains 4; this optimization does not add another document-format migration.
