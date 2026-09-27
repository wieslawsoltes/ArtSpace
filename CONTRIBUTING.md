# Contributing

Use the SDK pinned in `global.json`. Run `python3 scripts/fetch-assets.py`, then `dotnet run --project tests/ArtSpace.Tests -c Release`. Changes to pointer behavior, layout, typography or controls should include a real browser regression and a screenshot or trace where practical. Follow the local browser commands in the README.

Keep package dependencies directed. Controls remain independent of document/editor state. Do not add application globals to reusable packages. Core, document and illustration operations must remain usable without a window or Uno host.

Document mutations belong in `EditorSession.Edit` or begin/commit/cancel transactions. A continuous gesture produces one undo entry, not one per pointer move. Preserve parent-local coordinates, stable identifiers and component references when cloning. Test cancellation and failed-operation rollback as well as successful edits.

Dispose owned Skia objects, never borrowed cached paths. Validate untrusted files before changing the active document. Do not execute imported scripts, resolve external SVG resources or add telemetry/network services without an explicit review. Keep managed/native Skia versions compatible with Uno; a dependency update must pass browser runtime tests, not only compilation.

A parity feature is complete only when model, command, UI, persistence, undo behavior and tests agree. Update [the feature matrix](docs/feature-matrix.md) honestly. A stub, static screenshot, menu label or unverified implementation is not a completed feature. Preserve the distinction between native compilation, software-rendered CI and physical-GPU validation.

Use original artwork and icons or clearly documented permissive assets. Do not commit proprietary Adobe files, product artwork, logos or copied implementation code. Retain upstream MIT attribution and update third-party notices for new dependencies.

Release tags have the form `v0.1.0-alpha.1`. The release workflow builds browser and desktop archives, nine library packages and checksums. Public NuGet publishing is opt-in through the `nuget` environment's `NUGET_API_KEY`; unconfigured publication is skipped rather than pretending packages were published.
