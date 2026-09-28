# Opacity masks, SVG paints and affine fidelity

Available in **ArtSpace 0.4.0-alpha.1**. The document model, commands, compositing and coordinate conversion run in the same C# libraries on native desktop and Uno WebAssembly.

## Editable opacity masks

Place mask artwork above the artwork it should control, select the sibling objects and choose **Object → Make Opacity Mask**. A selected container can also become a mask group using its topmost eligible child. The source can be a vector object, text or a group of renderable objects. Unlike vector clipping, creating an opacity mask retains the source's paints and opacity.

The **Transparency** section in Properties selects **Luminance** or **Alpha**. Luminance uses the composited source's sRGB luma multiplied by its alpha: opaque white reveals, opaque black conceals, and grey or transparency produces partial coverage. Alpha mode ignores source RGB and uses only transparency. The source is composited as a whole before luminance conversion, so overlapping objects do not each independently mask the destination. Nested masks multiply through normal group composition.

**Invert Mask** complements coverage, including transparent areas. **Disable Opacity Mask** reveals the original content but does not paint the source as ordinary artwork. **Release Opacity Mask** removes the relationship and retains the group and source as editable objects. Undo restores each change in one transaction.

**Edit Mask Artwork** selects the retained source; **Edit Masked Artwork** selects ordinary content. Explicitly selected source artwork remains draggable through its bounds even where its output coverage is zero. Properties and the gradient tool can edit the source without flattening it. This is an explicit-selection editing convention, not a complete Illustrator thumbnail/isolation workspace. Normal picking avoids content with effectively zero mask coverage; it does not expose the hidden source.

A mask region, when present, limits both ordinary and inverted output. Native mask groups without an explicit region use the current rendering clip. SVG exports derive a finite mask region from the requested export viewport instead of emitting enormous surfaces.

## Reuse the commands

```csharp
using ArtSpace.Core;
using ArtSpace.Editing;
using ArtSpace.Illustration;

var artwork = new DesignNode { Width = 240, Height = 180, Fill = "#E6AA67" };
var source = new DesignNode
{
    Width = 240,
    Height = 180,
    Fills = [new()
    {
        Kind = FillKind.LinearGradient,
        Start = new(0, 0),
        End = new(1, 0),
        Stops = [new() { Offset = 0, Color = "#FFFFFF" },
                 new() { Offset = 1, Color = "#000000" }]
    }]
};
var session = new EditorSession(new DesignDocument
{
    Pages = [new() { Nodes = [artwork, source] }]
});
session.Select([artwork.Id, source.Id]);
OpacityMaskOperations.Make(session, OpacityMaskMode.Luminance);
OpacityMaskOperations.Invert(session);
session.Undo();
```

`OpacityMaskId` references a direct renderable child. Validation checks ownership and rejects a source also used as the same owner's clipping path. Cloning, clipboard and linked-symbol synchronization regenerate the reference with the source. Deleting a source releases its relationship; regrouping an active source into a different parent is rejected until released.

## Gradient fidelity

Linear and radial fills now retain separate gradient stops and stop opacity, overall fill opacity, coordinate space, spread method and affine transform. Radial fills retain center, radius and focus. SVG gradient inheritance resolves local `href`/`xlink:href` chains with cycle and depth checks. Stop offsets are clamped and made nondecreasing in document order.

`GradientSpace.Legacy` preserves older ArtSpace normalized-endpoint and circular-radial behavior. `ObjectBoundingBox` maps normalized coordinates through actual geometry bounds; `UserSpaceOnUse` uses explicit local coordinates. `GradientSpread` supports Pad, Repeat and Reflect. Rendering, imported paints and on-canvas controls use these coordinate definitions rather than assuming every imported gradient is a default diagonal fill.

Press **G** and drag to place a gradient's start/end or center/radius. Imported coordinate spaces and transforms are respected. A radial drag recenters its focus; separate focus-handle editing is not included. Direct contour normalization retains legacy gradient locations and re-expresses imported paints in user coordinates when necessary. Persistence preserves the resulting editable paint.

A 50% stop alpha under 50% fill opacity produces approximately 25% coverage, not 50% or 12.5%. Native tests inspect actual pixels and distinct endpoint colors, including repeated cached draws. This caught an identity-local-matrix shader ownership bug during development: a retained shader must not be disposed as a temporary when Skia returns the same object.

## SVG interchange boundaries

Implemented paint import/export covers local linear/radial gradient definitions, inherited gradient attributes/stops, object-box/user-space coordinates, stop alpha, spread, gradient transforms and radial center/focus/radius. The original solid fallback for referenced gradient fills has been removed.

Opacity-mask import currently requires **`maskUnits="userSpaceOnUse"` and user-space content coordinates**. It accepts multiple supported source objects, nesting, mask region and alpha/luminance interpretation. Missing/external/cyclic references, unsupported units and active or unsupported source content are rejected. Export writes editable source artwork and finite user-space mask regions.

**Inverted mask SVG export is explicitly rejected in this version.** Native files and PNG preserve inversion. General SVG filter graphs, object-box mask units, image/use sources, stylesheet cascade, linear-light interpolation, nonzero radial start radius, gradient strokes and arbitrary SVG features are not implied by gradient/mask support. SVG is still a subset, not lossless Illustrator interoperability.

## Exact affine placement

`DesignNode.AffineTransform` stores a residual matrix before editable placement. SVG group scaling now transforms descendants rather than changing only a group's nominal width/height. Skew and reflection are preserved without lossy rotation/scale decomposition. Grouping, reparenting and path normalization retain the residual where the editable transform fields cannot represent it directly.

Transform parsing supports matrix, translation, scale, rotation about an optional pivot and X/Y skew. Unknown syntax, malformed argument counts and singular/nonfinite transforms fail rather than silently becoming identity. Double coordinates are serialized with computed inverse/bounds properties excluded; SVG affine values use round-trip numeric formatting. Skia still rasterizes float geometry, so this is not an arbitrary-precision CAD claim.

## Cache ownership and measured work

The renderer caches gradient shaders by fill-object identity plus exact appearance values. Warm draws compare the coordinate key and stop values without sorting/rebuilding native shaders. Stop/color/coordinate/size changes invalidate the relevant entry. Fill opacity and object placement reuse a shader because they are applied outside its paint definition. Pruning and disposal release removed native resources. Identity matrix handling preserves ownership of a returned shader that aliases its source.

Rectangle affine bounds no longer allocate a four-point array or repeatedly enumerate it. The implementation maps the origin and two edge vectors and combines their signed extrema; randomized regression tests compare it to explicit corner bounds, including negative dimensions and skew.

```bash
dotnet run --project tests/ArtSpace.Tests -c Release
dotnet run --project tests/ArtSpace.Tests -c Release --no-build -- --benchmark
```

The benchmark JSON's `appearance` section reports five warmed samples and median CPU time/managed allocations for 100,000 affine bounds calculations and 500 gradient draws per sample. The draw baseline deliberately clears renderer caches each time, rebuilding geometry and shaders; it is not a measured frame rate of an older release. Cached/reference output pixels must match and warm gradient rebuild count must be zero. All rendering measurements use a software Skia surface. No physical-GPU or whole-application speed guarantee is made.

Mask picking uses a reusable one-pixel CPU surface, never a synchronous GPU readback. It still renders mask source content for a probe and is not a constant-time arbitrary-mask query. Dense mask sources, full scene traversal, cold caches, dynamic layout and snapshot history remain profiling targets.

## Native format and verification

Native saves now use **schema 3**. Schemas 1 and 2 remain readable and upgrade when saved. Earlier releases reject schema 3 rather than silently losing mask, gradient-space or affine semantics. Retain an original copy for older-version workflows.

The expanded engine suite contains 195 cases, including pixel compositing, persistence/reference ownership, affine properties, shader lifetime, gradient normalization and malformed input. Five previous benchmark safety checks remain. Thirteen real browser scenarios exercise existing editing plus opacity-mask commands/source movement, native recovery, SVG alpha/radial/affine import, imported gradient manipulation and warm-cache retention. Workflow conclusions and reports are authoritative for the commit tested.

Primary specifications: [CSS Masking Level 1](https://www.w3.org/TR/css-masking-1/) and [SVG paint servers](https://www.w3.org/TR/SVG2/pservers.html). ArtSpace's zero-coverage picking is an editor selection policy, not a claim of implementing browser CSS hit-testing rules.
