# Handoff Report: Codebase Survey for Requirement R3 (Unit Test TrashDir Isolation)

## 1. Observation

### 1.1 Storage Architecture and Static Path Definitions
In `MosquitoNetCalculator/Services/OrderStorageService.cs`:
- **Lines 16–24**:
  ```csharp
  private static readonly string AppDataDir = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
      "MosquitoNetCalculator");
  // Mutable (NOT readonly) so test code can redirect to a temp directory
  // per-test. See AppSettingsService.SettingsPath comment for the rationale —
  // .NET 8 throws FieldAccessException on FieldInfo.SetValue against initonly.
  public static string OrdersDir { get; set; } = Path.Combine(AppDataDir, "orders");
  public static string TrashDir { get; set; } = Path.Combine(AppDataDir, "orders_trash");
  private static readonly string CounterPath = Path.Combine(AppDataDir, "order_counter.json");
  ```
  Both `OrdersDir` and `TrashDir` are public mutable static auto-properties (`public static string { get; set; }`).
  - Default `OrdersDir`: `%APPDATA%\MosquitoNetCalculator\orders`
  - Default `TrashDir`: `%APPDATA%\MosquitoNetCalculator\orders_trash`

- **Lines 41–47 (Constructor Eager Directory Creation)**:
  ```csharp
  public OrderStorageService()
  {
      if (!Directory.Exists(OrdersDir))
          Directory.CreateDirectory(OrdersDir);
      if (!Directory.Exists(TrashDir))
          Directory.CreateDirectory(TrashDir);
  }
  ```
  Whenever `OrderStorageService` is instantiated, it immediately verifies and creates both `OrdersDir` and `TrashDir` on disk if they do not already exist.

- **Lines 215–242 (`DeleteOrder` and `SoftDeleteOrder`)**:
  ```csharp
  public void DeleteOrder(string orderId)
  {
      SoftDeleteOrder(orderId);
  }

  public void SoftDeleteOrder(string orderId)
  {
      string sourcePath = ResolveOrderFilePath(orderId);
      string destPath = ResolveTrashFilePath(orderId);

      lock (_cacheLock)
      {
          if (File.Exists(sourcePath))
          {
              if (!Directory.Exists(TrashDir))
                  Directory.CreateDirectory(TrashDir);

              if (File.Exists(destPath))
                  File.Delete(destPath);

              File.Move(sourcePath, destPath);
          }
          _cachedOrders = null;
      }
  }
  ```
  `DeleteOrder(orderId)` delegates directly to `SoftDeleteOrder(orderId)`. `SoftDeleteOrder` resolves the destination via `ResolveTrashFilePath(orderId)`:
  ```csharp
  string filePath = Path.GetFullPath(Path.Combine(directoryResolved, fileName));
  ```
  where `directoryResolved` is `Path.GetFullPath(TrashDir)`. Any call to `DeleteOrder(id)` moves the active order file from `OrdersDir` into `TrashDir`.

- **Lines 247–334 (Other Trash Operations)**:
  - `RestoreOrder(orderId)`: Moves file from `ResolveTrashFilePath(orderId)` back to `ResolveOrderFilePath(orderId)`.
  - `LoadTrashOrders()`: Reads `Directory.GetFiles(TrashDir, "*.json")` and prunes entries older than 30 days.
  - `PurgeTrashOrder(orderId)`: Calls `File.Delete(ResolveTrashFilePath(orderId))`.
  - `EmptyTrash()`: Deletes all `*.json` files in `TrashDir`.

### 1.2 Inspection of Production `%APPDATA%\MosquitoNetCalculator\orders_trash`
Executing `Get-ChildItem -Path "$env:APPDATA\MosquitoNetCalculator\orders_trash"` revealed 10 dummy order JSON files created during recent test executions.
Inspecting the file contents revealed exact payloads matching test methods:
1. `ClientName: "Test"`, `TotalAmount: 0`, `Items: []`
   - Example file: `382ac2a8-f567-4875-9e95-7eca0a6b54fb.json`, `6fe6c148-c26a-47c2-9667-136bea689ca5.json`
2. `ClientName: ""`, `TotalAmount: 0`, `Items: []`
   - Example file: `53f5896f-b449-4a0c-8d38-b9eccb8a50cf.json`, `d0c7cb45-0ec1-4a47-be8f-15ee762d93e1.json`
3. `ClientName: "Legacy"`, `TotalAmount: 0`, `Items: []`
   - Example file: `815602e1-d2af-4cdb-aaed-64c14b7e4549.json`, `d80819b7-ea08-4c23-bf91-555f2da2ced4.json`
4. `ContractNumber: "1-1"`, `ClientName: ""`, `TotalAmount: 0`, `Items: []`
   - Example file: `5f1cb3f6-963d-44dd-8da5-a49b031c195f.json`, `7fd13f5a-3dae-4ab5-86a9-e65f99031298.json`

### 1.3 Audit of Unit & Integration Tests in `MosquitoNetCalculator.Tests`

#### 1. `MosquitoNetCalculator.Tests/ViewModels/OrdersHistoryViewModelTests.cs`
- **Lines 15–38**:
  ```csharp
  [Collection("FileSystem")]
  public class OrdersHistoryViewModelTests : IDisposable
  {
      private readonly OrdersHistoryViewModel _vm = new();
      private readonly string _ordersDir;
      private readonly string _originalOrdersDir;

      public OrdersHistoryViewModelTests()
      {
          _originalOrdersDir = OrderStorageService.OrdersDir;

          _ordersDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "orders");
          if (Directory.Exists(_ordersDir))
              Directory.Delete(_ordersDir, true);
          Directory.CreateDirectory(_ordersDir);

          OrderStorageService.OrdersDir = _ordersDir;
      }
  ```
- **Lines 40–45 (`Dispose`)**:
  ```csharp
  public void Dispose()
  {
      OrderStorageService.OrdersDir = _originalOrdersDir;
      if (Directory.Exists(_ordersDir))
          Directory.Delete(_ordersDir, true);
  }
  ```
- **Lines 452–462 (`SaveAndDelete_Roundtrip`)**:
  ```csharp
  [Fact]
  public void SaveAndDelete_Roundtrip()
  {
      var order = new OrderData { Id = Guid.NewGuid().ToString(), ClientName = "Test" };
      _vm.SaveOrder(order);
      _vm.DeleteOrder(order.Id);
      ...
  ```
- **Defects observed**:
  1. `_vm = new();` is an inline field initializer. In C#, field initializers run before the constructor body executes. When `_vm = new()` runs, `OrderStorageService.OrdersDir` and `OrderStorageService.TrashDir` are still their production AppData defaults. The `OrderStorageService` constructor immediately creates `%APPDATA%\MosquitoNetCalculator\orders_trash` if missing.
  2. `OrderStorageService.TrashDir` is **never redirected** in this class; it remains `%APPDATA%\MosquitoNetCalculator\orders_trash`.
  3. `SaveAndDelete_Roundtrip` calls `_vm.DeleteOrder(order.Id)`, which calls `SoftDeleteOrder`, moving `order` (`ClientName = "Test"`, 0 items, 0 sum) directly into the production `%APPDATA%\MosquitoNetCalculator\orders_trash` folder.
  4. `Dispose()` restores `OrdersDir`, but does not touch or restore `TrashDir`.

#### 2. `MosquitoNetCalculator.Tests/Services/OrderStorageServiceTests.cs`
- **Lines 30–48**:
  ```csharp
  [Collection("FileSystem")]
  public class OrderStorageServiceTests : IDisposable
  {
      private readonly OrderStorageService _service;
      private readonly string _ordersDir;
      private readonly string _originalOrdersDir;

      public OrderStorageServiceTests()
      {
          _originalOrdersDir = OrderStorageService.OrdersDir;

          _ordersDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "orders");
          if (Directory.Exists(_ordersDir))
              Directory.Delete(_ordersDir, true);
          Directory.CreateDirectory(_ordersDir);

          OrderStorageService.OrdersDir = _ordersDir;
          _service = new OrderStorageService();
      }

      public void Dispose()
      {
          OrderStorageService.OrdersDir = _originalOrdersDir;
          if (Directory.Exists(_ordersDir))
              Directory.Delete(_ordersDir, true);
      }
  ```
- **Lines 141–147 (`DeleteOrder_RemovesOrder`)**:
  ```csharp
  [Fact]
  public void DeleteOrder_RemovesOrder()
  {
      var order = new OrderData { Id = Guid.NewGuid().ToString() };
      _service.SaveOrder(order);
      _service.DeleteOrder(order.Id);
      Assert.Null(_service.LoadOrder(order.Id));
  }
  ```
- **Lines 809–826 (`SaveOrder_LegacyGuidId_LoadsAndDeletes`)**:
  ```csharp
  string legacyId = Guid.NewGuid().ToString("D");
  var order = new OrderData { Id = legacyId, ClientName = "Legacy" };
  string savedPath = _service.SaveOrder(order);
  ...
  _service.DeleteOrder(legacyId);
  ```
- **Defects observed**:
  1. `TrashDir` is **never redirected**; remains pointed to production AppData.
  2. `_service = new OrderStorageService()` triggers eager `Directory.CreateDirectory(TrashDir)` against AppData.
  3. `DeleteOrder_RemovesOrder` moves `{ Id = ..., ClientName = "" }` into production AppData trash.
  4. `SaveOrder_LegacyGuidId_LoadsAndDeletes` moves `{ Id = ..., ClientName = "Legacy" }` into production AppData trash.
  5. `Dispose()` does not restore `TrashDir`.

#### 3. `MosquitoNetCalculator.Tests/App/ManualChecklistTests.cs`
- **Lines 47–74**:
  ```csharp
  [Collection("FileSystem")]
  public class ManualChecklistTests : IDisposable
  {
      ...
      public ManualChecklistTests()
      {
          _tempDir = Path.Combine(Path.GetTempPath(), "mosquito_test_" + Guid.NewGuid().ToString("N"));
          Directory.CreateDirectory(_tempDir);
          Directory.CreateDirectory(Path.Combine(_tempDir, "orders"));

          _originalSettingsPath = AppSettingsService.SettingsPath;
          _originalPricesPath = PriceService.PricesPath;
          _originalOrdersDir = OrderStorageService.OrdersDir;

          AppSettingsService.SettingsPath = Path.Combine(_tempDir, "settings.json");
          PriceService.PricesPath = Path.Combine(_tempDir, "prices.json");
          OrderStorageService.OrdersDir = Path.Combine(_tempDir, "orders");
      }

      public void Dispose()
      {
          AppSettingsService.SettingsPath = _originalSettingsPath;
          PriceService.PricesPath = _originalPricesPath;
          OrderStorageService.OrdersDir = _originalOrdersDir;
          ...
      }
  ```
- **Lines 332–340 (`Check5_Delete_Removes_From_List`)**:
  ```csharp
  [Fact]
  public void Check5_Delete_Removes_From_List()
  {
      var vm = new OrdersHistoryViewModel();
      var order = new OrderData { Id = Guid.NewGuid().ToString(), ContractNumber = "1-1" };
      vm.SaveOrder(order);
      Assert.Single(vm.LoadAllOrders());
      vm.DeleteOrder(order.Id);
      Assert.Empty(vm.LoadAllOrders());
  }
  ```
- **Defects observed**:
  1. `TrashDir` is **never redirected**; remains pointed to production AppData.
  2. `Check5_Delete_Removes_From_List` moves `{ ContractNumber = "1-1" }` into production AppData trash.
  3. Methods `Check5_Save_Roundtrip_PreservesData`, `Check5_Contract_Number_Increments_Per_Prefix`, `Check8_Import_Skips_Duplicates`, `Check8_Import_Overwrites_When_File_Newer` create `new OrderStorageService()` or `new OrdersHistoryViewModel()`, triggering `Directory.CreateDirectory(TrashDir)`.

#### 4. `MosquitoNetCalculator.Tests/Services/StabilityAndSafetyFeaturesTests.cs`
- **Lines 10–36**:
  ```csharp
  public class StabilityAndSafetyFeaturesTests : IDisposable
  {
      private readonly string _tempTestDir;

      public StabilityAndSafetyFeaturesTests()
      {
          _tempTestDir = Path.Combine(Path.GetTempPath(), "ArcSafetyTests_" + Guid.NewGuid().ToString("N"));
          Directory.CreateDirectory(_tempTestDir);

          OrderStorageService.OrdersDir = Path.Combine(_tempTestDir, "orders");
          OrderStorageService.TrashDir = Path.Combine(_tempTestDir, "orders_trash");
          DraftRecoveryService.DraftFilePath = Path.Combine(_tempTestDir, "draft.json");
          OrderBackupService.BackupsDir = Path.Combine(_tempTestDir, "backups");
          OrderBackupService.OrdersDir = Path.Combine(_tempTestDir, "orders");
          OrderBackupService.CounterPath = Path.Combine(_tempTestDir, "order_counter.json");
          OrderBackupService.PricesPath = Path.Combine(_tempTestDir, "prices.json");
      }

      public void Dispose()
      {
          try
          {
              if (Directory.Exists(_tempTestDir))
                  Directory.Delete(_tempTestDir, true);
          }
          catch { }
      }
  ```
- **Defects observed**:
  1. **Missing `[Collection("FileSystem")]`**: xUnit can execute this class concurrently with `OrdersHistoryViewModelTests` or `OrderStorageServiceTests`, causing static property race conditions.
  2. **No restore on Dispose**: Constructor overwrites `OrderStorageService.OrdersDir` and `OrderStorageService.TrashDir`, but `Dispose()` does **not** restore the original static values. It deletes `_tempTestDir` and leaves `OrderStorageService.TrashDir` pointing to a deleted path (`_tempTestDir\orders_trash`).

#### 5. `MosquitoNetCalculator.Tests/Services/OrderImportExportServiceValidationTests.cs`
- **Lines 26–36**:
  ```csharp
  [Collection("FileSystem")]
  public class OrderImportExportServiceValidationTests : IDisposable
  {
      ...
      OrderStorageService.OrdersDir = _testOrdersDir;
      _service = new OrderImportExportService(new OrdersHistoryViewModel());
  ```
- **Defects observed**:
  Does not redirect `TrashDir`. `new OrdersHistoryViewModel()` triggers `OrderStorageService` constructor which touches AppData `TrashDir`.

---

## 2. Logic Chain

1. **Premise (Storage Mechanism)**:
   Per Observation 1.1, `OrderStorageService.TrashDir` is a static property defaulting to `%APPDATA%\MosquitoNetCalculator\orders_trash`. Any call to `DeleteOrder(id)` invokes `SoftDeleteOrder(id)`, which moves files to `TrashDir`. Furthermore, any instantiation of `OrderStorageService` calls `Directory.CreateDirectory(TrashDir)`.
2. **Premise (Test Execution and Contamination Source)**:
   Per Observation 1.2, every single file found in `%APPDATA%\MosquitoNetCalculator\orders_trash` possesses properties uniquely matching the four test methods identified in Observation 1.3:
   - `ClientName = "Test"` (from `OrdersHistoryViewModelTests.SaveAndDelete_Roundtrip`)
   - `ClientName = ""` (from `OrderStorageServiceTests.DeleteOrder_RemovesOrder`)
   - `ClientName = "Legacy"` (from `OrderStorageServiceTests.SaveOrder_LegacyGuidId_LoadsAndDeletes`)
   - `ContractNumber = "1-1"` (from `ManualChecklistTests.Check5_Delete_Removes_From_List`)
3. **Premise (Deficient Isolation in Tests)**:
   - In `OrdersHistoryViewModelTests`, `OrderStorageService.TrashDir` is never modified or redirected. Additionally, `_vm = new()` runs before the constructor sets `OrdersDir`.
   - In `OrderStorageServiceTests`, `OrderStorageService.TrashDir` is never modified or redirected.
   - In `ManualChecklistTests`, `OrderStorageService.TrashDir` is never modified or redirected.
   - In `StabilityAndSafetyFeaturesTests`, `TrashDir` is redirected to a temporary directory, but is never restored on `Dispose`, and the test class lacks the `[Collection("FileSystem")]` attribute, creating cross-test races.
4. **Inference (Root Cause of R3 and R4)**:
   Because tests redirect `OrdersDir` to temp directories but leave `TrashDir` at its default production AppData path, every test that exercises `DeleteOrder` moves temporary test orders straight into the user's production trash folder.
5. **Conclusion**:
   To guarantee that running `dotnet test` generates zero files in `%APPDATA%\MosquitoNetCalculator\orders_trash`, all test fixtures instantiating `OrderStorageService` or `OrdersHistoryViewModel` or calling `DeleteOrder` must redirect both `OrdersDir` AND `TrashDir` to temporary directories, restore them in `Dispose`, and execute within `[Collection("FileSystem")]`.

---

## 3. Caveats

- **Active Process Locking**:
  During test runs (`dotnet test`), if `MosquitoNetCalculator.exe` is currently running on the host system (PID 8676 was observed), MSB3021/MSB3027 warnings/errors will occur during project build. Testing should specify `--no-build` or terminate the running app instance prior to building.
- **Other Static Path Services**:
  `OrderBackupService` has its own `BackupsDir`, `OrdersDir`, `CounterPath`, and `PricesPath`. These are already isolated in `StabilityAndSafetyFeaturesTests`. `OrderBackupService` does not reference `TrashDir`.
- **Eager vs. Lazy Directory Creation**:
  While redirecting `TrashDir` in all unit tests solves the issue for all test runs, in production `OrderStorageService.cs`, `Directory.CreateDirectory(TrashDir)` is called eagerly in the constructor. Making trash folder creation lazy (created only on actual soft-delete, which already checks `if (!Directory.Exists(TrashDir)) Directory.CreateDirectory(TrashDir)`) provides an additional layer of defense against accidental creation by uninitialized callers.

---

## 4. Conclusion

### Summary of Survey Findings

| Test File | Current `OrdersDir` | Current `TrashDir` | Deletes/Moves to Trash? | Issues / Contamination Risk |
|---|---|---|---|---|
| `OrdersHistoryViewModelTests.cs` | Isolated (`AppDomain.BaseDirectory/orders`) | **NOT isolated** (Production `%APPDATA%`) | **YES** (`SaveAndDelete_Roundtrip`) | **Critical**: Leaks dummy order (`ClientName: "Test"`) into user trash on every test run. Also, `_vm = new()` field initializer runs prior to constructor. |
| `OrderStorageServiceTests.cs` | Isolated (`AppDomain.BaseDirectory/orders`) | **NOT isolated** (Production `%APPDATA%`) | **YES** (`DeleteOrder_RemovesOrder`, `SaveOrder_LegacyGuidId_LoadsAndDeletes`) | **Critical**: Leaks 2 dummy orders (`""` and `"Legacy"`) into user trash on every test run. |
| `ManualChecklistTests.cs` | Isolated (`tempDir/orders`) | **NOT isolated** (Production `%APPDATA%`) | **YES** (`Check5_Delete_Removes_From_List`) | **Critical**: Leaks dummy order (`ContractNumber: "1-1"`) into user trash on every test run. |
| `StabilityAndSafetyFeaturesTests.cs` | Isolated (`tempDir/orders`) | Isolated (`tempDir/orders_trash`) | **YES** (`OrderStorageService_SoftDeleteAndRestore_MovesFilesCorrectly`) | **High**: Does NOT restore `OrdersDir` or `TrashDir` in `Dispose()`. Missing `[Collection("FileSystem")]`. |
| `OrderImportExportServiceValidationTests.cs` | Isolated (`AppDomain.BaseDirectory/orders-validation`) | **NOT isolated** (Production `%APPDATA%`) | No | Minor: Instantiates `OrdersHistoryViewModel`, causing eager directory check in AppData. |

### Concrete Action Items for Implementer (Requirement R3)

1. **Update `OrdersHistoryViewModelTests.cs`**:
   - Add fields:
     ```csharp
     private readonly OrdersHistoryViewModel _vm;
     private readonly string _ordersDir;
     private readonly string _trashDir;
     private readonly string _originalOrdersDir;
     private readonly string _originalTrashDir;
     ```
   - In constructor:
     ```csharp
     _originalOrdersDir = OrderStorageService.OrdersDir;
     _originalTrashDir = OrderStorageService.TrashDir;

     _ordersDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "orders");
     _trashDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "orders_trash");

     if (Directory.Exists(_ordersDir)) Directory.Delete(_ordersDir, true);
     if (Directory.Exists(_trashDir)) Directory.Delete(_trashDir, true);

     Directory.CreateDirectory(_ordersDir);
     Directory.CreateDirectory(_trashDir);

     OrderStorageService.OrdersDir = _ordersDir;
     OrderStorageService.TrashDir = _trashDir;

     _vm = new OrdersHistoryViewModel();
     ```
   - In `Dispose()`:
     ```csharp
     OrderStorageService.OrdersDir = _originalOrdersDir;
     OrderStorageService.TrashDir = _originalTrashDir;

     if (Directory.Exists(_ordersDir)) Directory.Delete(_ordersDir, true);
     if (Directory.Exists(_trashDir)) Directory.Delete(_trashDir, true);
     ```

2. **Update `OrderStorageServiceTests.cs`**:
   - Add fields `_trashDir` and `_originalTrashDir`.
   - In constructor, capture `_originalTrashDir = OrderStorageService.TrashDir;`, create `_trashDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "orders_trash");`, assign `OrderStorageService.TrashDir = _trashDir;`.
   - In `Dispose()`, restore `OrderStorageService.TrashDir = _originalTrashDir;` and clean up `_trashDir`.

3. **Update `ManualChecklistTests.cs`**:
   - Add field `_originalTrashDir`.
   - In constructor:
     ```csharp
     _originalTrashDir = OrderStorageService.TrashDir;
     Directory.CreateDirectory(Path.Combine(_tempDir, "orders_trash"));
     OrderStorageService.TrashDir = Path.Combine(_tempDir, "orders_trash");
     ```
   - In `Dispose()`:
     ```csharp
     OrderStorageService.TrashDir = _originalTrashDir;
     ```

4. **Update `StabilityAndSafetyFeaturesTests.cs`**:
   - Add `[Collection("FileSystem")]` attribute to the class.
   - Capture original static paths (`_originalOrdersDir`, `_originalTrashDir`, `_originalDraftPath`, `_originalBackupsDir`, `_originalCounterPath`, `_originalPricesPath`).
   - In `Dispose()`, restore all static values before deleting `_tempTestDir`.

5. **Update `OrderImportExportServiceValidationTests.cs`**:
   - Snapshot `_originalTrashDir = OrderStorageService.TrashDir;`.
   - Set `OrderStorageService.TrashDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "orders-trash-validation");`.
   - In `Dispose()`, restore `OrderStorageService.TrashDir = _originalTrashDir;` and clean up the test trash directory.

6. **Add Guard Test in `MosquitoNetCalculator.Tests`**:
   - Add a test method that records the timestamp or file count of `%APPDATA%\MosquitoNetCalculator\orders_trash` before and after running order operations or asserts that `OrderStorageService.TrashDir` does not point to `AppData` during test runs.

---

## 5. Verification Method

### 5.1 Verification Commands
1. Check existing trash count before running tests:
   ```powershell
   Get-ChildItem -Path "$env:APPDATA\MosquitoNetCalculator\orders_trash" | Measure-Object
   ```
2. Run unit tests:
   ```powershell
   dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj
   ```
3. Check trash count after running tests:
   ```powershell
   Get-ChildItem -Path "$env:APPDATA\MosquitoNetCalculator\orders_trash" | Measure-Object
   ```
   **Pass Condition**: The count of files in `%APPDATA%\MosquitoNetCalculator\orders_trash` does NOT increase after `dotnet test`. Zero files are created in `%APPDATA%\MosquitoNetCalculator\orders_trash`.

### 5.2 Files to Inspect
- `MosquitoNetCalculator.Tests/ViewModels/OrdersHistoryViewModelTests.cs`
- `MosquitoNetCalculator.Tests/Services/OrderStorageServiceTests.cs`
- `MosquitoNetCalculator.Tests/App/ManualChecklistTests.cs`
- `MosquitoNetCalculator.Tests/Services/StabilityAndSafetyFeaturesTests.cs`
- `MosquitoNetCalculator.Tests/Services/OrderImportExportServiceValidationTests.cs`

### 5.3 Invalidation Conditions
- Any new test calling `DeleteOrder`, `SoftDeleteOrder`, `RestoreOrder`, `EmptyTrash`, or instantiating `OrderStorageService` without redirecting `TrashDir`.
- Tests running outside `[Collection("FileSystem")]` that mutate `OrderStorageService.TrashDir` concurrently.
