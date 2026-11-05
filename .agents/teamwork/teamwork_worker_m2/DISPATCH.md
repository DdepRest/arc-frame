## 2026-11-02T12:34:53Z
You are teamwork_worker_m2.
Your working directory is: c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\teamwork_worker_m2
Your parent is: 5194245e-d76b-4446-9341-e73f2b5a09dc

MANDATORY: Read the original user request before starting work:
c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\ORIGINAL_REQUEST.md

Read the project scope and explorer findings:
- PROJECT.md: c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\orchestrator\PROJECT.md
- Survey findings: c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\teamwork_explorer_s2\handoff.md

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

EXCLUSIVE WRITE OWNERSHIP (you may only edit these files):
- MosquitoNetCalculator.Tests/ViewModels/OrdersHistoryViewModelTests.cs
- MosquitoNetCalculator.Tests/Services/OrderStorageServiceTests.cs
- MosquitoNetCalculator.Tests/App/ManualChecklistTests.cs
- MosquitoNetCalculator.Tests/Services/StabilityAndSafetyFeaturesTests.cs
- MosquitoNetCalculator.Tests/Services/OrderImportExportServiceValidationTests.cs

Task: Implement Milestone 2 (Requirement R3: Unit Test TrashDir Isolation):
1. In `MosquitoNetCalculator.Tests/ViewModels/OrdersHistoryViewModelTests.cs`:
   - Isolate `OrderStorageService.TrashDir` in addition to `OrdersDir`.
   - Fix field-initializer race: change `private readonly OrdersHistoryViewModel _vm = new();` to initialize in constructor AFTER `OrderStorageService.OrdersDir = _ordersDir;` and `OrderStorageService.TrashDir = _trashDir;` are assigned.
   - In `Dispose()`, restore both `OrderStorageService.OrdersDir = _originalOrdersDir;` and `OrderStorageService.TrashDir = _originalTrashDir;`, and clean up test directories.
2. In `MosquitoNetCalculator.Tests/Services/OrderStorageServiceTests.cs`:
   - Isolate both `OrdersDir` and `TrashDir` in constructor.
   - In `Dispose()`, restore both original paths and delete test temp directories.
3. In `MosquitoNetCalculator.Tests/App/ManualChecklistTests.cs`:
   - In constructor, capture `_originalTrashDir = OrderStorageService.TrashDir;`, create test trash directory in `_tempDir`, set `OrderStorageService.TrashDir = Path.Combine(_tempDir, "orders_trash");`.
   - In `Dispose()`, restore `OrderStorageService.TrashDir = _originalTrashDir;`.
4. In `MosquitoNetCalculator.Tests/Services/StabilityAndSafetyFeaturesTests.cs`:
   - Add `[Collection("FileSystem")]` attribute.
   - In constructor, snapshot `_originalOrdersDir` and `_originalTrashDir` (and other redirected statics).
   - In `Dispose()`, restore all redirected statics before deleting `_tempTestDir`.
5. In `MosquitoNetCalculator.Tests/Services/OrderImportExportServiceValidationTests.cs`:
   - Snapshot `_originalTrashDir`, redirect `OrderStorageService.TrashDir` to a test directory, and restore in `Dispose()`.
6. Add assertion/test verifying that running unit tests does NOT create any files in `%APPDATA%\MosquitoNetCalculator\orders_trash`.
7. Build and run:
   - `dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj`
   - Verify that zero files are added to `%APPDATA%\MosquitoNetCalculator\orders_trash`.

Write your complete handoff report to:
c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\teamwork_worker_m2\handoff.md
Update progress.md in your directory as you work.
When finished, send a message to parent with summary and test results.
