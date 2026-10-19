# PhotoCull Project Guidelines

These guidelines apply to all changes in this repository.

## Open Source and Licensing

- PhotoCull is open source and licensed under the MIT License. Read `LICENSE` before changing license or copyright notices; do not remove or shorten them.
- Use only code, libraries, models, and media whose licenses allow the intended use and redistribution. Verify the specific license before adding dependencies or external content.
- Preserve required copyright, license, and attribution notices. Update `THIRD-PARTY-NOTICES.md` and relevant documentation when needed.
- Do not copy code or content from sources with unknown, unclear, or incompatible licenses. Ask for clarification if licensing cannot be established; do not guess.
- Keep changes transparent and maintainable for the community. Avoid introducing unnecessary proprietary tools, services, or formats.

## Language

- Write source-code comments, XML documentation, technical documentation, and agent instructions in English.
- Keep user-facing UI text consistent with the application's existing language and localization patterns; do not change UI language as a side effect of this rule.

## Product Principles

- PhotoCull processes photos locally. Do not add cloud processing, telemetry, accounts, or network access to the app. Downloading the local model through `scripts/download-models.ps1` is the documented exception.
- The app must never delete photos automatically. Deletion must be explicitly confirmed by the user; preserve the preview and confirmation steps.
- Preserve existing user behavior and local privacy guarantees when refactoring or adding features.

## Architecture

- `src/PhotoCull.App`: WinUI interface, settings, and view models.
- `src/PhotoCull.Core`: domain models, abstractions, and grouping and ranking logic.
- `src/PhotoCull.Infrastructure`: file, image, metadata, storage, and pipeline implementations.
- `src/PhotoCull.Ai`: ONNX inference and embeddings.
- `tests/PhotoCull.Tests`: automated tests.
- Core is the domain foundation and must not depend on App, Infrastructure, or AI. Infrastructure and AI implement or consume Core abstractions; the App composes concrete services at the composition root.
- Prefer dependencies on Core abstractions. Keep domain rules independent of concrete file, database, UI, or ONNX technologies. Address existing direct coupling, such as the ViewModel's dependency on the pipeline, when relevant; do not perform unnecessary rewrites.
- Separate responsibilities: UI and view models handle interaction and UI state, the pipeline coordinates scans, Core contains domain decisions, and Infrastructure encapsulates technical details. Put new logic in the layer responsible for it.
- Use dependency injection for replaceable services and tests. Do not add a second service locator or global state mechanism.
- Prefer local, traceable data flows and explicit side effects. Photo analysis and similarity grouping must work offline; network access does not belong in the regular app workflow.
- Compute expensive image features only when needed for comparison. Preserve the grouping-criteria order and its persisted explanation data. Changes to thresholds or ordering can affect set membership and require appropriate tests and documentation.
- Keep persistence behind repository abstractions. Treat database and cache data as local implementation details; make stored-format changes carefully and account for existing user data.
- Keep failures and partial failures visible. Where possible, isolate corrupt or unsupported individual images so a scan can continue. Do not silently swallow errors or report success for failed operations.
- Keep domain rules independent of WinUI, the file system, and ONNX where practical so they can be verified with focused unit tests. Test integration boundaries using existing test patterns.

## Changes and Validation

- Keep this `AGENTS.md` up to date automatically: review it for every code, architecture, product, or build change, and update it in the same work when principles, workflows, or conventions change. Do not wait for a separate request. Do not add one-off implementation details; durable rules belong here, while detailed explanations belong in the appropriate project documentation.
- Read existing code and follow established patterns. Keep changes focused and update directly related tests and documentation.
- Do not broadly disable nullable warnings. Preserve the project's nullable and async patterns, and pass cancellation tokens through cancellable I/O and analysis operations.
- Run tests with `dotnet test PhotoCull.slnx`. For application or cross-project configuration changes, build on Windows with `dotnet build PhotoCull.slnx`. Explicitly report missing SDKs, workloads, or platform requirements.
- Add or update tests for behavior changes, especially grouping thresholds and ordering, recommendations, deletion flows, error cases, and persistence. Use isolated test data rather than real user photos or production data.
- Treat corrupt or unsupported individual photos as isolated failures where possible so scanning can continue; log and report failures clearly. Correctly abort on cancellation or overall operation failures, and do not report success when an operation failed.
- Before changing database schemas, settings, or cache formats, check compatibility with existing installations and local user data. Apply required migrations explicitly and safely; never silently delete or overwrite user data.
- Preserve keyboard accessibility, clear status and error messages, and confirmations for destructive actions in the UI. Give new controls accessible names and meaningful labels.
- For new packages, models, and media, verify origin, license, redistribution terms, and required notices. Update `THIRD-PARTY-NOTICES.md` and documentation when needed. Models must be usable locally; do not add network access during normal app use.
- Report when tests or builds could not be run rather than claiming successful validation.
- Do not alter unrelated or pre-existing user changes.
