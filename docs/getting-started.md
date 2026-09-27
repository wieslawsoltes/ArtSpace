# Getting started

ArtSpace opens the original **Alpine Echoes** illustration: three artboards containing an editable poster, badge and curve/color study. Work stays on your device.

## Workspace

Menus provide document/illustration commands. The horizontal appearance bar edits fill, stroke, width, opacity and alignment. The two-column toolbox chooses drawing/navigation tools. Properties, Layers, Artboards and History share a resizable right dock. On compact screens the right dock hides to preserve canvas area. Tab toggles panels.

Use Artboards to select and fit an artboard, Layers to inspect/search individual objects, and Properties for transforms and appearance. A filled icon/square indicates the selected tool or anchor. Save a copy before replacing or experimenting with important artwork.

## Draw and edit

Choose File → New for a blank document with a white artboard. M draws rectangles, L ellipses, P pen paths, N freehand paths, B fixed-width brush strokes and T text. V selects/moves objects. Bounding-box handles resize and rotate; Shift constrains gestures. Arrow keys nudge by one document pixel and Shift-arrow by ten.

For a pen path, click to place anchors, drag to set tangents, press Enter to finish or click the first anchor to close. A enters direct selection for native, imported SVG, primitive, compound or expanded paths. Select anchors, Shift-select additional ones, drag a marquee on empty canvas, or use the +/− anchor tools. Ctrl Shift O converts text to editable glyph outlines. Full details, removal versus cutting, numerical limits and examples are in [Path editing and text outlines](path-editing.md).

The control bar also sets drawing defaults. Swatches apply fill colors. Gradient commands create linear/radial fills; G edits their start/end in the canvas. Pathfinder performs union/subtract/intersect/exclude. Object commands expose shape/stroke expansion, offsets, compatible-object blends, repeats and compound paths. Those are transactional edits, not the full Illustrator live-object/effect system.

## Files and recovery

Save a Copy downloads an editable `.artspace` file in the browser. Open reads native documents or imports a supported SVG subset into the current workspace. SVG export provides editable vector interchange; PNG export produces pixels. Native AI/EPS/PDF and print-production color workflows are not supported.

Autosave writes IndexedDB recovery in the browser or local application data on desktop. Reload restores the latest completed save. Browser data can be cleared or evicted: it is not a backup. Download a native copy before switching devices or clearing storage. There is one active document session with multiple artboards.

## Keyboard essentials

| Action | Shortcut |
| --- | --- |
| Selection / direct selection | V / A |
| Rectangle / ellipse | M / L |
| Pen / pencil / paintbrush | P / N / B |
| Type / line segment | T / Backslash |
| Add / remove anchor | + / − |
| Anchor Point tool | Shift C |
| Create text outlines | Ctrl Shift O |
| Gradient / eyedropper | G / I |
| Scale / artboard | S / Shift O |
| Hand / zoom | H / Z |
| Temporary pan / cursor zoom | Space-drag / Ctrl-wheel |
| Group / ungroup | Ctrl G / Ctrl Shift G |
| Duplicate | Ctrl D or Alt-drag |
| Undo / redo | Ctrl Z / Ctrl Shift Z |
| Outline preview | Ctrl Y |
| Fit first artboard / actual size | Ctrl 0 / Ctrl 1 |
| Fit all / fit selection | Shift 1 / Shift 2 |
| Rulers | Ctrl R |
| New / open / save copy | Ctrl N / Ctrl O / Ctrl S |
| Quick actions / panel visibility | Ctrl K / Tab |

While editing anchors, Ctrl A selects that path's anchors, arrows nudge selected anchors, Delete cuts them with incident segments, and Alt-Delete reconnects the remaining neighbors. Escape cancels a drag or clears the anchor selection. Native text inputs retain their own editing shortcuts. Reserved OS/browser shortcuts may take precedence; Command substitution is not certified across every host.

See [feature boundaries](feature-matrix.md) before relying on a production interchange or typography workflow.
