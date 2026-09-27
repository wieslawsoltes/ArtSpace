# Getting started

ArtSpace opens **Alpine Echoes**, an original three-artboard vector document. The illustration, badge, color study and Bézier ribbon are editable objects. Work stays on your device; there is no account, upload service or collaboration server.

## Workspace

The menu bar contains document and illustration commands. The control bar sets drawing fill, stroke and width and provides opacity/alignment actions. The left toolbar chooses a drawing or navigation tool. The right dock contains Properties, Layers, Artboards and History; drag its left edge to resize it. On compact screens the right dock is hidden to preserve canvas space. Tab toggles editor panels.

Use **Artboards** to select and fit an artboard. **Layers** exposes individual objects, visibility, locking and filtering. Select objects in the canvas or layers to edit their transform and appearance. Changes appear immediately and most document operations are undoable in one step.

## First drawing

Choose **File → New**, preserving the current document with **Save a Copy** first when necessary. A new document receives a white artboard. Press **M** and drag to draw a rectangle, or **L** for an ellipse. Hold Shift for constrained geometry. Press **V** to move or resize an object; drag a bounding-box handle to resize and the rotation handle to rotate. Arrow keys nudge by one pixel; Shift-arrow nudges by ten.

The fill, stroke and width in the control bar also set defaults for newly drawn vector objects. The Properties panel is the authoritative view of the selected object's current values. Color swatches change the selected fill. Gradient commands create a linear or radial gradient; **G** then lets you drag its start and end directly in the canvas.

## Pen and anchor editing

Press **P** and click to create anchors. Drag as you place an anchor to create its tangent. Finish an open path with Enter, or close a path by clicking its first anchor. **N** draws a sampled freehand path. **B** uses a fixed-width freehand brush; this release does not implement pressure-sensitive or patterned brushes.

Use **A** to select a point-based path and drag individual anchors or tangent handles. Moving an anchor moves its attached tangents. Dragging a tangent mirrors the opposite tangent unless Alt is held. Direct anchor editing currently applies to the native point-based paths created by the pen, pencil or sample, not arbitrary imported SVG path strings.

The Object menu provides **Add Anchor Points** (exact cubic subdivision), **Smooth Anchors**, **Corner Anchors**, **Reverse Path Direction** and **Close Path**. These commands operate on selected editable paths.

## Pathfinder, outlines and repetition

Select two or more shapes and use the Properties panel's Pathfinder actions to unite, subtract, intersect or exclude their filled geometry. These are destructive vector edits with undo, not a live Shape Builder tool.

**Object → Outline Stroke** creates actual filled outline geometry while retaining a visible filled interior. Caps, joins and dash patterns participate in expansion. **Expand Shape** converts a primitive's current geometry to a path. **Offset Path** creates a separate expanded or inset vector path; positive distances grow and negative distances shrink the geometry.

**Blend** creates editable intermediate objects between two same-kind siblings. Native point-based paths must have matching anchor counts. This is not a live Illustrator blend object: color, position, size and compatible point topology are interpolated, but arbitrary path correspondence is not inferred. **Radial Repeat** creates bounded copies around a derived pivot below the selection; the copies remain ordinary editable objects.

## Files and recovery

**Save a Copy** downloads an editable `.artspace` JSON document in the browser. On desktop, the host uses its local storage directory. **Open** accepts native documents and a supported SVG subset. Unsupported SVG elements are reported; scripts and external active content are not executed. The importer is not a lossless Illustrator converter.

SVG export is editable vector interchange. PNG export renders the selection or document region to pixels and is not editable. Native `.ai`, `.eps` and print-production color workflows are not supported in this alpha.

Autosave writes recovery data to IndexedDB in the browser and application-local storage on desktop. A reload restores the latest completed recovery save. Browser storage can be cleared or evicted and is not a backup. Save a local copy before switching devices, clearing browser data or replacing a document. There is one active document session, with multiple artboards.

## Keyboard reference

| Action | Shortcut |
| --- | --- |
| Selection / direct selection | V / A |
| Rectangle / ellipse | M / L |
| Pen / pencil / paintbrush | P / N / B |
| Type / line segment | T / Backslash |
| Gradient / eyedropper | G / I |
| Scale / artboard | S / Shift O |
| Hand / zoom | H / Z |
| Temporary pan | Space-drag |
| Zoom to cursor | Ctrl-wheel |
| Zoom out with Zoom tool | Alt-click |
| Select all / deselect | Ctrl A / Escape |
| Group / ungroup | Ctrl G / Ctrl Shift G |
| Duplicate | Ctrl D or Alt-drag |
| Undo / redo | Ctrl Z / Ctrl Shift Z |
| Outline / preview | Ctrl Y |
| Fit first artboard / actual size | Ctrl 0 / Ctrl 1 |
| Fit all / fit selection | Shift 1 / Shift 2 |
| Rulers | Ctrl R |
| New / open / save copy | Ctrl N / Ctrl O / Ctrl S |
| Quick actions / panel visibility | Ctrl K / Tab |

Text controls retain their native editing behavior while focused. Browser- and OS-reserved shortcuts can take precedence. Modifier handling is based on the shared Uno keyboard model; not every host automatically substitutes Command for Control.

## Validation and known boundaries

See [feature matrix](feature-matrix.md) for important missing workflows and [architecture](architecture.md) for embedding and rendering. A successful browser acceptance run verifies only its documented interactions and captured viewport, not complete product parity or physical GPU performance.
