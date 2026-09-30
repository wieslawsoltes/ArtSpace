# Retained contour projection and menu observation

## Closest-point projection during text-path manipulation

Text-on-path brackets and SVG offset handles project a pointer onto a measured baseline. `MeasuredContour` retains a bounded table of 128–4,096 segments, then refines a local arc-length interval with Skia. The old coarse search tested every segment on every pointer update.

`ArtSpace.Core.PolylineProjectionIndex` now owns a snapshot of those segments and a balanced bounding-volume tree. Queries search nearer bounds first and skip bounds whose conservative lower distance exceeds the current best. Exact ties select the earliest original segment, including repeated vertices and self-intersections. A spatially sorted segment's position in the array is never treated as its original path order.

`MeasuredContour.Project(point)` builds this index lazily and reuses it until the measured contour is disposed. `Project(point, useSpatialIndex: false)` remains the executable exhaustive reference. Both routes use the same sampled geometry, closest-segment arithmetic and unchanged 18-iteration native arc-length refinement. This is an optimization of the existing numerical approximation, not a new exact curve-intersection solver. It changes neither persisted text nor glyph placement.

The managed index is immutable after construction and supports concurrent queries. It does not own a font, window, Skia object or GPU resource. `MeasuredContour` itself still belongs to one owning render/UI context because it owns native measurement objects. Per-query statistics are returned by value; no shared query state or managed allocation is introduced.

```csharp
var index = new PolylineProjectionIndex(points); // ReadOnlySpan<Vec2>; the input is copied.
var hit = index.Project(pointer, out var statistics);
// hit.SegmentIndex is in the original point order.
// hit.Parameter is the interpolation parameter within that segment.
```

Construction recursively sorts median partitions: `O(N log² N)` time and `O(N)` retained storage. Queries commonly examine a small subset, but overlapping bounds can still require `O(N)` work. The first projection pays index construction and storage costs. The public index accepts at most 65,536 segments and finite coordinates within ±1e150 to keep squared-distance arithmetic finite; editor baseline samples have the stricter bounds above.

The twelve regressions cover ownership, equal-distance ties, duplicate vertices, randomized exact reference equivalence, conservative rounding at large coordinates, invalid input, concurrent reads, zero-allocation warm queries, and native refined projection on open/closed contours. Engine failures are not replaced with timing tolerances.

## Reproducible performance report

```bash
dotnet run --project tests/ArtSpace.Tests -c Release -- --projection-benchmark
```

The normal CI path-text benchmark also writes `polyline-projection.json`. Seven alternating warm batches compare 512 queries over 4,096 sampled segments. The report includes every sample, construction cost, managed allocations, exact-equivalence checks and examined-segment counts. This isolates the CPU coarse search; it excludes Skia refinement, rendering, screen presentation and GPU work. It is not an old-release or whole-application FPS comparison. Cold construction and worst-case overlap costs must not be hidden behind warm-query results.

## Menu state without layout-driven diagnostics

Changing a menu's active command changes focus and paint, but need not trigger a layout pass. Tests previously navigated with stale layout-driven observations and overshot the editable SVG export command even though that command was present.

`CommandMenuBar.NavigationChanged` and `StudioWorkbench.MenuNavigationChanged` publish actual open/active-command changes directly. Active selection updates touch the previous/current menu buttons rather than rewriting every row. Focus is still acquired after popup loading, even when the initial command has not changed.

Only an explicitly enabled `?test=1` browser observer subscribes to publish the two menu scalars into the frozen read-only observation. It does not serialize documents, refresh inspectors, walk the scene or claim readiness before the first full observation. Identical scalar updates reuse the existing observation. Frame reports preserve menu state, and menu reports preserve frame counters. Disposing the workbench disconnects the event forwarding.

The browser export test waits for each real active-command transition and checks it does not loop around the menu. Three standalone JavaScript tests execute the production storage adapter for opt-in gating, frozen state, identity reuse and interleaved menu/frame observations. These observations do not expose an editor mutation API.
