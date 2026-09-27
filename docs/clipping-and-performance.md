# Clipping masks and performance — 0.3.0-alpha.1

ArtSpace now supports editable vector clipping sets in the shared desktop and WebAssembly engine. This release also reduces work in geometry caching, text layout, selection enumeration, drag snapping and offscreen painting. It does not replace the rendering backend or claim complete Illustrator parity.

## Clipping workflow

Place a vector shape above the artwork it should clip. Select the sibling objects and choose **Object → Make Clipping Mask** or **Ctrl 7**. The topmost selected object becomes the clipping path. A selected container can also become a clipping set using its topmost child. Text must be outlined before it can become a clipping path.

Clipping is non-destructive to geometry: the mask and artwork remain separate editable nodes. As part of creating a mask, its fill, stroke and shadow appearance are removed. Undo restores the original appearance. Nonzero and even-odd path rules determine the clipped region, including compound holes. Nested clipping sets intersect their clipping regions during rendering.

Use **Object → Edit Clipping Path** or the Properties panel to select and edit mask anchors. **Edit Clipped Contents** selects the ordinary artwork instead. Explicit editing can move content that is currently outside the visible mask; normal picking does not select hidden content through a clipping hole.

**Object → Release Clipping Mask** or **Ctrl Alt 7** removes the clipping relationship, retaining the group, its transform/appearance, and the unpainted former mask. Ungroup separately when independent siblings are needed. Delete the actual clipping path to release its relationship. Regrouping an active mask path is rejected until the existing mask is released, rather than leaving a dangling reference.

Outline preview reveals the mask and hidden vector geometry. Preview/export uses clipping again. Frame/artboard clipping remains supported alongside vector clipping; a frame's own painted background is not one of its clipped children.

## Model and reusable API

A clipping container stores `ClipPathId`, the stable identifier of a direct vector child. `ClippingPath` resolves that child. JSON validation rejects missing, non-child, text or container mask references. Cloning, clipboard import and linked-component synchronization remap identifiers. Selection and transform operations retain ordinary node ownership.

```csharp
using ArtSpace.Core;
using ArtSpace.Editing;
using ArtSpace.Illustration;

var artwork = new DesignNode
{
    Width = 240, Height = 180, Fill = "#E6AA67"
};
var mask = new DesignNode
{
    Kind = NodeKind.Ellipse,
    X = 30, Y = 20, Width = 180, Height = 140
};
var session = new EditorSession(new DesignDocument
{
    Pages = [new() { Nodes = [artwork, mask] }]
});
session.Select([artwork.Id, mask.Id]);
ClippingOperations.Make(session);
ClippingOperations.EditMask(session);
// A host can now expose the mask's ordinary path-editing controls.
ClippingOperations.Release(session);
session.Undo(); // Restores the clipping relationship.
```

Each Make/Release or completed anchor gesture is an undo transaction. Mask creation does not flatten or Boolean-intersect the original artwork.

## Native format compatibility

**Native saves use schema 2.** ArtSpace 0.3 reads schemas 1 and 2; saving a legacy document upgrades its `FormatVersion` to 2. ArtSpace 0.2 and earlier reject schema 2 instead of silently ignoring clipping references and displaying unmasked artwork. Keep an original copy when moving between versions. This version guard applies to native `.artspace` files; SVG remains the documented interchange subset.

The editor retains its rollback snapshot until commit serialization succeeds. Serialization failures therefore restore the document and selection instead of leaving an incomplete transaction.

## SVG clipping boundary

Export writes local `<clipPath>` definitions, transforms and `clip-rule` alongside the retained artwork. Import supports **one vector primitive or compound path per `userSpaceOnUse` definition**, including direct nonzero/even-odd clip rules. A compound path may contain multiple contours and holes.

Missing/external references, recursive clipped definitions, multiple shapes per definition and `objectBoundingBox` units are rejected rather than silently importing unclipped artwork. Opacity/luminance masks, general SVG filters, CSS stylesheets and referenced paint servers are not made fully compatible by this release. Existing affine/group-transform and SVG paint-server limitations still apply; this is not lossless Illustrator/SVG roundtripping.

## Performance changes

### Exact geometry cache

Previously, even a geometry cache hit rebuilt SVG path text for its key. The cache now retains an exact geometry snapshot: primitive parameters, fill rule, path dimensions/string and copied native anchor/tangent values. A hit compares those values without allocating SVG text. There is no probabilistic hash-based identity.

Changing geometry invalidates the corresponding entry; moving an object or changing a fill color does not. Document changes prune deleted entries instead of clearing every retained path. `Geometry(node)` returns borrowed native geometry owned by the renderer; callers must copy a path before mutating it and must not dispose the borrowed result.

Text layouts retain their font and positioned text runs. Typography changes rebuild them; paint and position changes reuse them. Typeface replacement and renderer disposal clear owned text resources. Selection/root lists are similarly reused across viewport and geometry-preview events, with invalidation on document/selection/structural changes. Direct model changes made by embedding hosts must follow the session notification contract.

### Indexed snapping

`ArtSpace.Layout.SnapIndex` snapshots stationary target bounds and stores sorted horizontal/vertical anchor coordinates. Nearest coordinate lookup uses binary search for the moving object's edges/center, preserving the exhaustive implementation's target order, moving-anchor order, ties and guide extents. Duplicate coordinates retain the original winning target.

The editor builds this index at drag start and reuses it while targets are stationary. Auto-layout documents use a conservative rebuild path because preview layout can move other objects. Index construction is `O(N log N)` and allocates target storage; queries are `O(log N + G)` for `G` guides. This is not a constant-time cold drag-start claim. The reference `SnapEngine` remains available for equivalence tests.

### Conservative culling

Leaf geometry is rejected against the actual Skia canvas clip before paint/shader creation. Stroke reach and antialias margins are included. Groups are not rejected by their nominal boxes because their children may overflow them. Text and filtered/shadow subtrees remain conservative so offscreen sources can still cast visible shadows. Picking bounds include miter reach before exact path tests.

The hierarchy is still visited: this release reduces expensive painting/native allocations, not traversal to `O(visible objects)`. `SceneRenderer.EnableCulling = false` provides a reference path. `Draw` resets visited/rendered/culled counters; geometry and text build/hit counters accumulate over the renderer lifetime.

## Reproduce measurements

```bash
dotnet run --project tests/ArtSpace.Tests -c Release
dotnet run --project tests/ArtSpace.Tests -c Release --no-build -- --benchmark
```

Build retains the JSON report as **ArtSpace-performance**. The harness compares 100 snap queries against 10,000 grid targets, 20,000 warm star-geometry cache accesses, and ten 256×256 software-Skia frames of 5,000 leaves inside one group. Snapping corrections and guide lines must match the exhaustive reference; culling output must match the reference pixels. Five untimed boundary checks cover schema migration, serialization rollback, live-mask grouping, nested mask ownership and miter picking.

The first integration run on Ubuntu 24.04.5 / .NET 10.0.12 recorded:

| Scenario | Reference | Optimized | Managed allocations, reference → optimized |
| --- | ---: | ---: | ---: |
| 100 snap queries / 10,000 targets | 77.1633 ms | 0.3072 ms | 288,031,200 → 8,800 bytes |
| 20,000 warm geometry accesses | 621.2648 ms for SVG-key construction | 2.9055 ms for exact cache hits | 276,486,208 → 0 bytes |
| Ten frames / 5,001 nodes | 121.5792 ms without culling | 61.3601 ms with culling | 12,802,000 → 94,208 bytes |

That culling fixture painted 37 nodes and skipped 4,964 leaves, with identical output pixels. Source: [integration run 36346098611](https://github.com/wieslawsoltes/ArtSpace/actions/runs/36346098611), artifact `ArtSpace-performance`.

These are **CPU microbenchmarks on a software Skia surface**, not physical-GPU, browser startup or whole-application frame-rate measurements. Index construction is excluded from query timings. The geometry reference measures the old SVG-key construction rather than a complete old application frame. Single-run timings vary with tiered compilation and host load; compare retained reports using the same fixtures. No timing threshold is used as a correctness test.

## Validation and remaining scope

The engine regression suite has 141 cases, including 34 clipping/performance cases. Ten browser scenarios cover the previous editing features plus mask creation/release, native save/recovery, direct mask-anchor editing, imported clipping holes and cache retention during movement. Build, Pages and Release run the browser scenarios; workflow conclusions and artifacts establish which commit actually passed.

This release does not implement opacity masks, gradient meshes, advanced shaping, full live effects, AI/EPS/PDF interchange, CMYK/ICC production, multiple documents or arbitrary floating docking. See the [feature matrix](feature-matrix.md).
