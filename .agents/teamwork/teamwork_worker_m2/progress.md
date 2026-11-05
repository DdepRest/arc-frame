# Progress — Milestone 2 (Unit Test TrashDir Isolation)

Last visited: 2026-11-02T15:49:00Z

## Status
- [x] Read DISPATCH.md, ORIGINAL_REQUEST.md, PROJECT.md, and survey findings.
- [x] Initialized BRIEFING.md and progress.md.
- [x] Inspect each of the 5 files in scope:
  - [x] OrdersHistoryViewModelTests.cs
  - [x] OrderStorageServiceTests.cs
  - [x] ManualChecklistTests.cs
  - [x] StabilityAndSafetyFeaturesTests.cs
  - [x] OrderImportExportServiceValidationTests.cs
- [x] Implement changes in `OrdersHistoryViewModelTests.cs` (TrashDir isolation, field init fix, Dispose cleanup).
- [x] Implement changes in `OrderStorageServiceTests.cs` (TrashDir isolation, Dispose cleanup, guard test).
- [x] Implement changes in `ManualChecklistTests.cs` (TrashDir isolation, Dispose cleanup).
- [x] Implement changes in `StabilityAndSafetyFeaturesTests.cs` (`[Collection("FileSystem")]`, statics snapshot & restore in Dispose).
- [x] Implement changes in `OrderImportExportServiceValidationTests.cs` (TrashDir isolation & restore in Dispose).
- [x] Add guard test to verify isolation from `%APPDATA%\MosquitoNetCalculator\orders_trash`.
- [x] Run `dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj`.
- [x] Verify zero files created in production `%APPDATA%\MosquitoNetCalculator\orders_trash`.
- [x] Update BRIEFING.md and write handoff.md.
- [ ] Send completion message to parent.
