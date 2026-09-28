# Selection and panel responsiveness

ArtSpace 0.4.1-alpha.1 removes document-wide work from ordinary selection and retains inspector controls. The native schema remains version 3; existing documents do not require conversion.

## Selection-to-panel pipeline

`StudioWorkbench` merges dirty flags in one dispatcher callback. There is no fixed debounce interval. Selection requests update the control bar, the visible inspector, and selected layer rows. They do not rebuild page, artboard or history lists. Viewport changes update only the zoom header; panning at constant zoom does not format it again. Preview events update visible numeric values without replacing controls.

Hidden tabs retain dirty flags but are not materialized repeatedly. Switching tabs or restoring the dock applies the latest state, not an earlier captured selection. Flags deferred for hidden tabs do not schedule an idle polling loop.

`ArtSpace.Controls.RetainedInspector` retains sections by stable key and exact control-topology key. Values and selected object identity are not topology keys. Rectangles with the same fill/stroke structure reuse their controls; adding a gradient stop rebuilds the Fill section, not Transform, Layout, Swatches or Export. Section expansion is preserved. Transform and appearance are placed before auxiliary illustration tools.

Readers resolve the current model. Programmatic reads suppress write handlers. Synchronous selection ownership invalidation cancels stale buffered input before asynchronous rebinding; color-picker callbacks carry an edit generation so a dismissed picker cannot edit a subsequently selected object. Updates of a still-focused field preserve uncommitted text on the same target. Undo and document loading can replace every model instance and force retargeting even if IDs are unchanged.

`NumericField` tracks whether text was actually edited. Focus loss without user changes does not round the stored double to its two-decimal display. Values, button states and labels avoid redundant dependency-property writes. Layer rows construct their visual tree once and retarget actions through the current entry. Icon paths and paints are retained per control and released on unload.

## Layers and editor state

`ReconciledCollection<T>` applies reference-identical row projections without events. Small metadata changes replace only changed entries; large inserts or removals publish one Reset rather than one event per row. Unchanged row entries and selection containers are retained. The ListView remains virtualized; no unbounded nested vertical ScrollViewer was added around it. Selection synchronization uses the selected IDs and row dictionary rather than clearing and re-adding every selected row.

`EditorSession` lazily builds a page ID/order index. Repeated selections resolve only selected IDs and sort them by scene traversal order. Invalid IDs are filtered, nested roots are preserved, no-op selections do not notify, and lazy projections of `SelectedIds` are materialized before replacing their source. Document changes invalidate the index. Transactional direct collection edits followed by selection requests rebuild it so newly inserted objects are immediately selectable. External mutations outside session commands must be followed by `Notify(Document)`, as required for UI synchronization generally.

History labels are projected once per document notification. Undo restoration validates selected IDs with one membership set instead of scanning the page once per selected ID.

## Clicks versus drags

Object selection, resize-handle selection and anchor selection do not begin edit transactions. A pending gesture becomes an edit only after three screen pixels of movement. At that point the editor captures the rollback snapshot; snapping builds its index only when snapping is actually queried. Transform gestures retain scalar transform state, not deep clones of all selected subtrees. Anchor drags still retain editable-path snapshots once movement begins because they need exact rollback and absolute handle updates.

A plain click therefore creates no document snapshot, history entry, component synchronization, auto-layout commit, snap index or autosave request. Real drags remain one undo transaction; cancellation restores the original document. Final pointer coordinates are processed on release. This intentionally makes an Alt-click without dragging a selection action rather than a zero-distance duplicate.

Path editing reuses the renderer's exact geometry cache identity rather than generating a full SVG signature string for each anchor query. Imported contours and geometry invalidation still use the renderer's existing value-based cache checks.

## Validation and measurement

Run the registered engine and collection regressions:

```bash
dotnet run --project tests/ArtSpace.Tests -c Release
```

Build the browser application using the repository guide, serve `artifacts/site`, and run:

```bash
npx playwright test tests/browser/ui-performance.spec.mjs
```

The suite uses real keyboard, pointer and file-picker interactions. Read-only diagnostics expose inspector control values and rectangles, dirty-work counters, snapshots, index builds and section builds. No editor mutation API is exposed. Diagnostics exist only under `?test=1`; normal use does not publish them.

Checks cover repeated selection, focused numeric edits, undo/redo model replacement, multi-selection, hidden tabs, layer collection stability, anchor clicks and cancelled drags. A 2,000-extra-node fixture records pointerdown-to-matching-selection and two animation-frame opportunities. New builds additionally wait for matching actual inspector field values. Raw samples, median and p95 are written under `artifacts/ui-performance` and retained with browser validation artifacts. PR builds can compare the successful base-commit browser artifact on the same CI runner when it is available.

Timings include software rendering and opt-in diagnostic work. They are not physical-GPU presentation latency, FPS, or a guarantee for every drawing. Compare the recorded variants and raw samples; do not infer a release speedup from constructor counters alone. Snapshot serialization on real edits, auto-layout, mask probes, complex paths and total scene rendering remain document-dependent costs. No shortcut skips document validation or makes undo data approximate.
