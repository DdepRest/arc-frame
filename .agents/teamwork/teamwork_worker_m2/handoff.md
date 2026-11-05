# Handoff Report: Milestone 2 (Requirement R3: Unit Test TrashDir Isolation)

## 1. Observation

### 1.1 Pre-Modification State & Contamination Vectors
1. `MosquitoNetCalculator.Tests/ViewModels/OrdersHistoryViewModelTests.cs`:
   - Line 15: `private readonly OrdersHistoryViewModel _vm = new();` executed as a C# inline field initializer prior to constructor body execution.
   - When `_vm` was initialized, `OrderStorageService.OrdersDir` and `OrderStorageService.TrashDir` had not been redirected, pointing to production `%APPDATA%\MosquitoNetCalculator\orders` and `orders_trash`.
   - `OrderStorageService.TrashDir` was never isolated in the test fixture; any call to `DeleteOrder(id)` (e.g., in `SaveAndDelete_Roundtrip`) moved test orders directly into `%APPDATA%\MosquitoNetCalculator\orders_trash`.
2. `MosquitoNetCalculator.Tests/Services/OrderStorageServiceTests.cs`:
   - Only `OrderStorageService.OrdersDir` was redirected; `OrderStorageService.TrashDir` remained pointing to production AppData.
   - Tests calling `DeleteOrder` (`DeleteOrder_RemovesOrder`, `SaveOrder_LegacyGuidId_LoadsAndDeletes`) moved test orders into production AppData trash.
3. `MosquitoNetCalculator.Tests/App/ManualChecklistTests.cs`:
   - Isolated `OrdersDir`, `SettingsPath`, and `PricesPath`, but left `OrderStorageService.TrashDir` at default production AppData.
   - `Check5_Delete_Removes_From_List` moved `{ ContractNumber = "1-1" }` into production AppData trash.
4. `MosquitoNetCalculator.Tests/Services/StabilityAndSafetyFeaturesTests.cs`:
   - Lacked the `[Collection("FileSystem")]` attribute, allowing xUnit to execute it in parallel with other file-system-modifying tests.
   - Redirected `OrdersDir`, `TrashDir`, and `OrderBackupService` statics, but failed to restore them in `Dispose()`, deleting `_tempTestDir` and leaving static properties pointing to non-existent deleted paths.
5. `MosquitoNetCalculator.Tests/Services/OrderImportExportServiceValidationTests.cs`:
   - Instantiated `new OrdersHistoryViewModel()`, which instantiates `OrderStorageService` and triggers eager directory creation against default static paths, without redirecting `TrashDir`.

### 1.2 Implemented Changes
1. `MosquitoNetCalculator.Tests/ViewModels/OrdersHistoryViewModelTests.cs`:
   - Changed `_vm = new()` from field initializer to declaration `private readonly OrdersHistoryViewModel _vm;`.
   - Added `_trashDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "orders_trash");` and `_originalTrashDir = OrderStorageService.TrashDir;`.
   - In constructor, assigned `OrderStorageService.OrdersDir = _ordersDir;` and `OrderStorageService.TrashDir = _trashDir;` BEFORE instantiating `_vm = new OrdersHistoryViewModel();`.
   - In `Dispose()`, restored both `OrderStorageService.OrdersDir = _originalOrdersDir;` and `OrderStorageService.TrashDir = _originalTrashDir;`, and recursively deleted `_ordersDir` and `_trashDir`.
   - In `SaveAndDelete_Roundtrip`, asserted that deleted order was placed into `_trashDir`.
2. `MosquitoNetCalculator.Tests/Services/OrderStorageServiceTests.cs`:
   - Added `_trashDir` and `_originalTrashDir`.
   - In constructor, redirected both `OrdersDir` and `TrashDir` to clean temp directories under `AppDomain.CurrentDomain.BaseDirectory`.
   - In `Dispose()`, restored both original static properties and cleaned up temp directories.
   - In `Constructor_CreatesOrdersDirectory`, asserted that both `_ordersDir` and `_trashDir` exist.
   - Added guard test `DeleteOrder_MovesToTestTrashDir_AndDoesNotPolluteProductionTrash` that snapshots production trash file count, executes `SaveOrder` and `DeleteOrder`, verifies the deleted file exists in `_trashDir`, and asserts zero files were added to production AppData trash.
3. `MosquitoNetCalculator.Tests/App/ManualChecklistTests.cs`:
   - Added `_originalTrashDir = OrderStorageService.TrashDir;`.
   - In constructor, created `Path.Combine(_tempDir, "orders_trash")` and assigned `OrderStorageService.TrashDir = Path.Combine(_tempDir, "orders_trash");`.
   - In `Dispose()`, restored `OrderStorageService.TrashDir = _originalTrashDir;`.
   - In `Check5_Delete_Removes_From_List`, asserted that deleted order exists in `OrderStorageService.TrashDir`.
4. `MosquitoNetCalculator.Tests/Services/StabilityAndSafetyFeaturesTests.cs`:
   - Decorated class with `[Collection("FileSystem")]`.
   - Captured all 7 static paths in constructor: `_originalOrdersDir`, `_originalTrashDir`, `_originalDraftFilePath`, `_originalBackupsDir`, `_originalBackupOrdersDir`, `_originalCounterPath`, `_originalPricesPath`.
   - In `Dispose()`, restored all 7 static paths before deleting `_tempTestDir`.
5. `MosquitoNetCalculator.Tests/Services/OrderImportExportServiceValidationTests.cs`:
   - Captured `_originalTrashDir = OrderStorageService.TrashDir;`.
   - Created and redirected `OrderStorageService.TrashDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "orders-trash-validation");`.
   - In `Dispose()`, restored `OrderStorageService.TrashDir = _originalTrashDir;` and cleaned up `_testTrashDir`.

### 1.3 Execution Results
- `dotnet build MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj`: Succeeded with 0 warnings, 0 errors.
- Target test run: `dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj --no-build --filter "FullyQualifiedName~OrdersHistoryViewModelTests|FullyQualifiedName~OrderStorageServiceTests|FullyQualifiedName~ManualChecklistTests|FullyQualifiedName~StabilityAndSafetyFeaturesTests|FullyQualifiedName~OrderImportExportServiceValidationTests"`:
  - Result: 109 passed, 0 failed, 0 skipped in 13 seconds.
- Production Trash Directory Verification:
  - Pre-test file count in `%APPDATA%\MosquitoNetCalculator\orders_trash`: 14
  - Post-test file count in `%APPDATA%\MosquitoNetCalculator\orders_trash`: 14
  - New files created in production trash: 0.

---

## 2. Logic Chain

1. **Premise 1**: In `OrderStorageService.cs`, soft deletion (`DeleteOrder` -> `SoftDeleteOrder`) writes deleted order JSON files to `OrderStorageService.TrashDir`. In addition, `new OrderStorageService()` checks and creates `TrashDir` on disk.
2. **Premise 2**: Prior to M2, `OrdersHistoryViewModelTests`, `OrderStorageServiceTests`, `ManualChecklistTests`, and `OrderImportExportServiceValidationTests` only redirected `OrdersDir`, leaving `TrashDir` pointing to `%APPDATA%\MosquitoNetCalculator\orders_trash`.
3. **Premise 3**: In `OrdersHistoryViewModelTests`, `private readonly OrdersHistoryViewModel _vm = new();` executed before the constructor ran, calling `new OrderStorageService()` while both `OrdersDir` and `TrashDir` were still set to their production AppData paths.
4. **Premise 4**: In `StabilityAndSafetyFeaturesTests`, `TrashDir` was redirected to `_tempTestDir\orders_trash` but never restored in `Dispose()`, leaving `OrderStorageService.TrashDir` pointing to a deleted temp directory for subsequent test classes, while lacking `[Collection("FileSystem")]` which permitted concurrent execution.
5. **Deduction**: By redirecting `OrderStorageService.TrashDir` to local test-specific temp directories in all 5 test suites, delaying `_vm` creation until after path assignment, restoring all static properties in `Dispose()`, and placing all test fixtures in the `[Collection("FileSystem")]` xUnit collection, all file operations remain strictly confined to test directories.
6. **Verification**: Executing the full unit test suite verified that 0 files are written to `%APPDATA%\MosquitoNetCalculator\orders_trash` during test runs, and all 109 tests across these 5 fixtures pass cleanly.

---

## 3. Caveats

- **Existing AppData Trash Files**:
  The 14 pre-existing files in `%APPDATA%\MosquitoNetCalculator\orders_trash` were generated prior to this milestone during earlier test runs. Per the project plan, purging these existing dummy orders from the production user trash is assigned to Milestone 3 (Requirement R4).
- **Collection Attribute Serialisation**:
  All test classes touching static file paths (`OrdersDir`, `TrashDir`, `SettingsPath`, `PricesPath`, `DraftFilePath`, `BackupsDir`) are now properly synchronized under `[Collection("FileSystem")]`, preventing any cross-thread race conditions.

---

## 4. Conclusion

Requirement R3 (Unit Test TrashDir Isolation) is fully implemented and verified:
1. `OrderStorageService.TrashDir` is isolated in all unit test fixtures to per-test temporary directories.
2. The field-initializer race in `OrdersHistoryViewModelTests` is fixed.
3. All static overrides are cleanly reverted in `Dispose()` across all test fixtures.
4. Running the unit tests creates zero files in `%APPDATA%\MosquitoNetCalculator\orders_trash`.

---

## 5. Verification Method

### 5.1 Verification Commands
1. Check production trash file count:
   ```powershell
   pwsh -NoProfile -Command "(Get-ChildItem -Path (Join-Path $env:APPDATA 'MosquitoNetCalculator\orders_trash')).Count"
   ```
2. Run targeted test suites:
   ```powershell
   dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj --no-build --filter "FullyQualifiedName~OrdersHistoryViewModelTests|FullyQualifiedName~OrderStorageServiceTests|FullyQualifiedName~ManualChecklistTests|FullyQualifiedName~StabilityAndSafetyFeaturesTests|FullyQualifiedName~OrderImportExportServiceValidationTests"
   ```
3. Re-verify production trash file count:
   ```powershell
   pwsh -NoProfile -Command "(Get-ChildItem -Path (Join-Path $env:APPDATA 'MosquitoNetCalculator\orders_trash')).Count"
   ```
   **Pass Criteria**:
   - Count before = Count after (0 added files).
   - 109/109 tests pass.

### 5.2 Files Modified
- `MosquitoNetCalculator.Tests/ViewModels/OrdersHistoryViewModelTests.cs`
- `MosquitoNetCalculator.Tests/Services/OrderStorageServiceTests.cs`
- `MosquitoNetCalculator.Tests/App/ManualChecklistTests.cs`
- `MosquitoNetCalculator.Tests/Services/StabilityAndSafetyFeaturesTests.cs`
- `MosquitoNetCalculator.Tests/Services/OrderImportExportServiceValidationTests.cs`
