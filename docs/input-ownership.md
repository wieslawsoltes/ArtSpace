# Input ownership and path-text responsiveness

This guide supplements the Type on a Path and UI-performance guides for the completed 0.6 implementation. The repository now defines **306 engine cases**, **26 real Uno browser scenarios**, and **three isolated browser-keyboard adapter tests**. Workflow reports, not these counts, establish which revision passed. The five benchmark safety checks remain separate from the registered engine cases.

## Presses and releases have one owner

The browser host publishes modifiers and forwards eligible navigation keys to the shared C# workbench. A handled press owns its matching release, even when that press closes a menu or opens a dialog. Otherwise an Enter release can reach a newly focused native control and activate an unrelated command.

The adapter tracks physical key identity (`code`, with `key` fallback) only for consumed presses. Repeats do not grow the set. Blur, hidden-document transitions and adapter replacement clear ownership. Unhandled key releases and ordinary text, modifier shortcuts and input-method composition are not suppressed.

The HTML editing element can lag behind the XAML focus transition. Its editability is therefore supplied to the workbench as a flag instead of being used to veto an open menu's keyboard ownership. With no owning menu, text inputs and modal dialogs retain normal editing. No JavaScript code mutates documents or implements menu commands.

The workbench retains its application-menu reference rather than walking the visual tree for every navigation key. Shared routed preview handlers recheck ownership before default button/focus handling, covering a key delivered after Uno processes a queued pointer click. Modal lifetime is tracked explicitly; background shortcuts must not execute behind an active dialog.

`HandleHostNavigation(VirtualKey)` remains available for existing hosts. The overload accepting a native-text-input flag provides the additional browser context without removing the original binary signature.

## Startup, recovery and cancellation

After loading, the editor acquires keyboard focus only when no control already has it. Save is also routed as an application shortcut when the browser/XAML root has no focused control immediately after recovery. Neither path intentionally takes focus from an existing input or modal.

The first Escape during a pointer gesture cancels that gesture and retains the restored object selection. A later Escape can clear selection. This matters for path-text bracket edits: rollback must not be followed by ordinary deselection in the same event. Pointer capture is released during cancellation. Pending clicks still do not capture document history; an actual drag commits one undo step.

Numeric controls compare their current text with their last displayed value during commit, so rapid one-character edits cannot depend on delayed TextChanged delivery. Repeated Enter is idempotent; Escape restores the displayed value without committing it.

## Retention under cache pressure

Path-text glyph layouts and contour measurements are bounded to 128 entries and evict the least recently used entry rather than flushing the entire cache. Warm painting updates both layout and baseline recency without constructing missing measurements. Replacing an entry with the same identifier does not evict an unrelated entry merely because the cache is full. Conversion to ordinary text prunes obsolete baseline/layout resources.

The CPU benchmark includes a cache-pressure fixture: populate the cache, then interleave 256 new labels with sixteen frequently used labels. It verifies zero rebuilds for those hot labels and bounded final cache counts. Eviction scans at most the configured entry count; warm lookup does not allocate an eviction list. Approximate glyph accounting is not a hard bound on all native, GPU or transient allocations.

## Run and interpret the tests

```bash
node --test tests/keyboard-bridge.test.mjs
dotnet run --project tests/ArtSpace.Tests -c Release
# After publishing and serving the real Uno WebAssembly app:
npx playwright test
```

The browser tests observe actual open-menu, focused-control and dialog state through the opt-in read-only diagnostics. They do not wait a guessed fixed dialog-animation delay or invoke a test-only edit API. The rapid-transition scenario intentionally sends pointer/Home/Enter without an intervening delay, then types and cancels the modal while checking that background selection and history remain unchanged. Recovery-save and cancelled-bracket cases assert the resulting native document and selection.

The separate Node tests execute the production browser adapter with event-target doubles, covering consumed key pairs, native-input flags, unowned/IME input, Ctrl/Command-S, reset and reinstall. They are fast adapter checks, not substitutes for the real Uno browser suite.

Rendering measurements use software Skia, and CI Chromium uses its portable software graphics configuration. Neither UI test success nor native compilation certifies physical-GPU throughput or full Illustrator feature/visual parity.
