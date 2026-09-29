# Live appearance and retained rendering

ArtSpace 0.5.0-alpha.1 adds non-destructive appearance editing and removes repeated managed scene submission from unchanged frames. Native documents save as schema 4 and can load schemas 1–4. Earlier ArtSpace readers must reject schema 4 rather than silently discard effects, gradient strokes, or graphic styles.

## Appearance workflow

Open **Window → Appearance**, or use the Appearance tab in the right dock. Select artwork, choose an effect under **Live Effects**, and select **Add live effect**. Each effect has its own enable switch, parameters, reorder controls, Duplicate, and Delete. Numeric edits commit with Enter or focus loss; Escape cancels uncommitted input. Commands participate in the same undo/redo transaction system as geometry editing.

| Effect | Parameters | Semantics |
| --- | --- | --- |
| Gaussian Blur | Radius | Radius is Gaussian standard deviation in object-local units, not a claim of identical Adobe radius calibration. |
| Drop Shadow | Radius, X, Y, color, opacity | Filtered shadow composited behind the input, retaining the original input. |
| Outer Glow | Radius, color, opacity | A zero-offset filtered shadow behind the input. |
| Saturation | 0–400% | An sRGB-component saturation matrix. Zero is grayscale; 100% is unchanged. Alpha is preserved. |

Enabled effects run in list order on the composed object. Object opacity and blend are applied at the containing compositing layer. Existing legacy shadow documents retain their first-visible-shadow convention; newly added live shadows use the explicit effect stack. Blur and glow do not change editable path geometry or selection bounds.

The panel also provides fills, strokes, legacy shadows, stroke options, appearance clearing, and independent fill/stroke ordering and duplication. Stroke layers paint above fill layers. This is **not** Illustrator's arbitrary interleaved per-paint appearance graph, and effects are currently object-level rather than attached to individual fills or strokes.

**Clear Appearance** removes fills, strokes, shadows and effects, and resets object transparency. **Reduce to Basic** retains a single solid fill. Neither operation destroys geometry, children, clipping paths, or opacity-mask relationships.

## Graphic Styles

Open **Window → Graphic Styles**. Select artwork and choose **New Graphic Style** to capture its fills, strokes, opacity, blend mode, legacy shadows and live effects. Each style has a live vector thumbnail, editable name, Apply, and Delete. Applying a style preserves the target's identity, position, size, path data, children, and mask relationships.

Presets, targets and gradient stops own independent copies. Editing an applied appearance does not mutate the preset or another styled object. Styles are document-local presets, not live linked global appearances. The library has at most 1,024 entries; the panel materializes twelve slots per page rather than creating a control tree for every style.

Appearance commands record explicit symbol-instance appearance overrides. Source synchronization retains those overrides; Reset Overrides restores source appearance. This does not add Illustrator symbol-library interchange or dynamic-symbol parity.

## Strokes and blending

Stroke options support a positive dash/gap sequence, a signed dash offset, and solid, linear-gradient or radial-gradient paint. Odd-length dash lists repeat to create an even-length pattern, as required for SVG-style dash semantics. Gradient stroke opacity is the product of stroke opacity, paint opacity, and stop opacity. Stop position, color, opacity and spread are editable.

The compositing model now includes Normal, Multiply, Screen, Overlay, Darken, Lighten, Difference, Color Dodge, Color Burn, Hard Light, Soft Light, Exclusion, Hue, Saturation, Color, and Luminosity. These map to Skia blend modes. They do not imply CMYK, spot-color, overprint, or Adobe color-management equivalence.

SVG import/export retains the supported gradient-stroke and dash-offset metadata. Native saves retain the complete new appearance model. PNG export renders live effects. **SVG export rejects enabled live effects** with an explicit message; general SVG filter-graph interchange is not implemented. A disabled effect may remain in a native document while SVG export proceeds without that effect. Export bounds remain caller-supplied; expand them when the intended raster output includes an outside glow or shadow.

## Native rendering architecture

The editor renders through Uno `SKCanvasElement`, using the host's existing Skia composition canvas. It does not create a second CPU framebuffer, encode a frame into PNG, upload a screenshot each frame, or host the canvas inside a WebView.

For a settled document, `SceneRenderer.DrawRetained` records the scene into an owned native `SKPicture`. Selection, hover, handles, guides and other editing overlays are drawn separately. Subsequent unchanged frames replay native drawing commands directly into the host canvas instead of re-walking every C# scene node and recreating paint objects. An overscan region allows modest panning and zooming without rerecording. It is a vector display list, not a raster preview, so it is not deliberately downsampled to gain speed.

During actual edit gestures, the editor uses direct rendering, retaining geometry, text, gradients, paints, dashes and effect filters where their values have not changed. Document and preview notifications invalidate the scene display list. Session replacement, page changes, font replacement, cache pruning and disposal release it. The retained mode can be toggled under **View → Retained Scene Rendering**.

A reusable renderer caller must invoke `InvalidateRetainedScene()` after modifying the model before calling `DrawRetained` again. The ordinary `Draw` method continues to observe direct mutations through value-based appearance caches. Both APIs are single-thread-owned; sharing their mutable caches concurrently is unsupported.

### Resource ownership and bounds

Fill and stroke paints are retained by paint-object identity. Shader keys include gradient geometry and exact stop values. Dash effects retain exact interval and phase snapshots. Effect graphs retain exact effect parameter snapshots; native filters retain their input graphs. Changing parameters replaces the affected native graph, not every graph in the document.

The implementation bounds paint entries at 16,384, effect entries at 4,096, effect stacks at 32 entries, and the retained picture at an approximate 32 MiB of command storage by default. A rejected picture falls back to direct rendering without rerecording it every frame. These are application cache limits, **not a hard bound on transient Skia allocations or GPU memory**. Very large filtered layers can still be expensive.

Filtered content is not culled solely by unfiltered geometry bounds: an offscreen source may cast a visible shadow or glow. Native recording avoids freezing scale-dependent managed quick-rejection decisions into a display list intended for later zooming. Actual destination clipping remains authoritative.

## GPU acceleration and measurement boundary

Native playback, gradients, blending and image filters can execute on the GPU when Uno supplies a hardware-backed Skia canvas. Backend selection and fallback belong to the host, graphics driver and browser. This change does not replace Uno's renderer or claim a separate WebGPU compute pipeline.

Geometry editing, parsing, validation, undo snapshots, display-list recording and some layout work remain CPU operations. Existing opacity-mask hit testing uses a reusable one-pixel CPU probe, not a GPU readback. Not every operation in the application is GPU accelerated.

`LiveAppearanceBenchmarks` compares warm direct rendering and warm retained playback on the same software Skia bitmap. It alternates measurement order, records seven samples, checks pixel equality, and fails if unchanged paint/dash resources or the picture are rebuilt. Recording cost is intentionally excluded. The report is a CPU/native-submission experiment, not physical-GPU presentation latency or an application-FPS guarantee.

```bash
dotnet run --project tests/ArtSpace.Tests -c Release
dotnet run --project tests/ArtSpace.Tests -c Release -- --appearance-benchmark
npx playwright test tests/browser/live-appearance.spec.mjs
```

Browser acceptance uses real file pickers, pointer actions and keyboard editing. Opt-in read-only diagnostics under `?test=1` expose actual field values, effect counts, style counts, scene recordings/replays and native resource-build counters. Normal usage does not publish these diagnostics. Browser CI uses SwiftShader; hardware performance must be measured separately.

## Remaining compatibility boundary

ArtSpace remains an independently implemented illustration editor, not a complete or pixel-identical Illustrator replacement. Still outside this increment: native AI/PDF/EPS compatibility, general SVG filters, gradient meshes, editable envelope/warp graphs, variable-width and art/pattern brushes, type-on-path and full text shaping, image tracing, live paint, perspective tools, production CMYK/ICC/spot/overprint workflows, arbitrary per-paint effect graphs, Adobe plugin APIs, and full floating-window/workspace/UI parity. The repository feature matrix separates working features from these boundaries.
