# Editable SVG path text — 0.7.0-alpha.1

## Import and edit

Open an SVG containing a supported `<text><textPath>…</textPath></text>` run. The text, typography and baseline remain editable. The importer accepts document-local `href` and SVG 1.1 `xlink:href` references, plus SVG 2 inline path data. Supported references are paths, lines, circles, ellipses, rectangles and polygon/polyline geometry. The baseline is copied into the text object; subsequent edits do not modify another visible source path.

SVG anchor positioning is not a native bracket interval. The Properties panel therefore shows **SVG offset** and **Percent / Path units** rather than presenting misleading Start/End brackets. The single on-canvas anchor handle moves the offset. A click does not capture undo history; a drag creates one transaction, and Escape restores both data and selection. Baseline anchors remain editable with Direct Selection. **Use native bracket layout** explicitly switches to the complete native interval; this changes positioning policy and can move the text. Undo restores the SVG policy.

Supported positioning includes start/middle/end text anchoring, signed offsets outside the path length, percentage offsets, positive authored `pathLength` calibration, scalar `dy`, and left/right traversal. Open paths clip by glyph midpoint. Single closed contours permit a single circuit around the anchor, including passage through the contour seam. Source characters remain stored when they cannot be rendered.

Only the referenced geometry's own transform affects its baseline. Transforms on ancestors of the definition are not applied, and scaling the baseline does not scale the glyphs. The text object's own and ancestor transforms continue to affect its complete appearance. This distinction is covered by an independent geometry/advance regression.

Font properties and paint inheritance are read from the text run. Default XML whitespace is collapsed; supported preformatted/XML-preserve modes retain spaces. Nonbreaking spaces are not collapsed. Font fallback, shaping and exact metrics in other SVG viewers remain outside this basic text engine's parity guarantee.

## Export modes

The existing **Export SVG** command remains the appearance-oriented choice: it converts path text to vector outlines on a detached copy. It never changes the editable source or history.

**File → Export Editable SVG…** writes actual `<textPath>` content and local path definitions. Imported SVG offsets retain their original units and authored path length. Native bracket text with no overflow and zero tracking is exported against a trimmed, optionally reversed baseline; font-metric vertical alignment is resolved to a scalar `dy`. Original text content remains editable in other tools, but the receiving font engine determines its metrics.

Native bracket tracking uses inter-glyph spacing and glyph-width tangents, whereas SVG uses complete character cells including trailing letter spacing. Editable export rejects nonzero native tracking; default outlined export preserves its appearance. Imported SVG spacing remains editable and includes trailing spacing in text-anchor placement and tangent sampling. Spacing that reverses a character advance and fractional font-weight values are explicitly unsupported.

Native bracket overflow cannot be represented exactly by SVG's different midpoint clipping rule. Editable export rejects that case rather than dropping characters; use the default outlined export. SVG-positioned overflow can be exported with its original offset semantics. Existing restrictions on enabled live-effect filters and unsupported masks still apply.

```csharp
var imported = SvgFormat.Import(svgText);
var textNode = imported.Document.AllNodes().Single(n => n.TextPath is not null);

using var renderer = new SceneRenderer();
var output = IllustrationSvgExport.Export(
    [textNode], renderer.GetArtworkBounds(textNode), renderer,
    SvgPathTextExportMode.Editable);
```

`ArtSpace.Documents` still depends only on Core, not Skia or Uno. The parser indexes local IDs once and caches each referenced baseline's geometry/transform during the import. `SvgTextPathPosition` stores offset, unit mode, authored path length and the supplemental baseline transform. SceneRenderer resolves lengths and glyph geometry with its bounded exact-key caches.

## Scope and rejection behavior

This is an explicit editable subset, not full SVG text layout. Mixed text runs, nested/styled tspans, multiple textPaths per text object, `textLength`/`lengthAdjust`, stretching, automatic spacing, per-glyph positioning lists, vertical/bidirectional layout, context-dependent font units, font shorthand and italic font simulation are rejected. Non-alphabetic dominant baselines should be converted to scalar `dy` or outlines before import. Missing/external/cyclic/wrong-kind references and duplicate IDs are rejected without fetching any resources. A zero authored path length is outside this importer despite being valid with special semantics in SVG. Baseline geometry still uses the existing single-contour validity/complexity limits.

## Rendering work reduced

Glyph layout now appends an existing glyph path with its transform directly into the output path. It no longer creates, transforms and disposes one temporary native `SKPath` per visible character. This changes geometry construction, not the rendering backend. Exact path-data comparison verifies equivalence with the previous clone-transform-append sequence.

Document fonts use unhinted linear metrics. Grid-fitted raster metrics could disagree with vector outlines on different fallback fonts or fractional zoom levels. This is limited to artwork typography; Uno's UI font rendering is unchanged. The existing strict coverage regression now passes locally without relaxing its tolerance.

The `--glyph-append-benchmark` mode compares both construction paths using 2,048 placements, eight layouts per sample, and seven alternating-order samples after warmup. Reports include all samples, managed allocations, temporary path counts and exact-geometry agreement. This excludes font lookup, arc-length measurement, rasterization and physical-GPU work. The ordinary CI path-text benchmark also writes `glyph-append.json` into its existing performance artifact.

## Pointer projection and read-only observations

SVG anchor dragging uses the retained spatial projection index described in [projection and menu observation](projection-index.md). Exact closest-segment ties and the existing native refinement are preserved. The first query constructs the index; warm queries allocate no managed objects.

Application menu observations are published directly when the active command changes rather than waiting for a layout pass. This fixes stale automation feedback without adding document or inspector work to keyboard navigation.

## Persistence and verification

Native saves use **schema 6**, reading schemas 1–6. Older readers reject schema 6 rather than ignoring SVG positioning semantics. Clone, undo, recovery and symbol serialization preserve independently owned options. Cache keys include offset units, authored length and baseline transform; changing an offset rebuilds layout but does not remeasure unchanged geometry.

The engine suite includes 55 new SVG/path-append cases, and three application browser scenarios cover actual import, inspector editing, anchor manipulation, cancellation, editable export/reimport and native recovery. An additional independent Chromium SVG reference checks spacing/anchoring and authored-length calibration using the browser's own font metrics. Workflow reports are authoritative for which commit passed. No physical-GPU speedup or complete Illustrator parity is claimed.

## Primary references

[SVG 2 text-on-path layout and coordinates](https://www.w3.org/TR/SVG2/text.html#TextPathElement), [SVG 2 authored path length](https://www.w3.org/TR/SVG2/paths.html#PathLengthAttribute), and [SkiaSharp transformed path append](https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skpath.addpath).
