# Handoff Report: Codebase Survey for Requirements R4 and R5

## 1. Observation

### 1.1 Production Trash Directory Content & Test Leakage
A direct query of the production trash directory (`%APPDATA%\MosquitoNetCalculator\orders_trash`) via PowerShell revealed 10 dummy order files:
```powershell
Get-ChildItem -Path "$env:APPDATA\MosquitoNetCalculator\orders_trash" -Filter *.json | ForEach-Object {
    $c = Get-Content $_.FullName | ConvertFrom-Json;
    [PSCustomObject]@{ File=$_.Name; Items=$c.Items.Count; Total=$c.TotalAmount; Client=$c.ClientName; Contract=$c.ContractNumber }
}
```
Result:
| File | Items | Total | Client | Contract |
|---|---|---|---|---|
| `382ac2a8-f567-4875-9e95-7eca0a6b54fb.json` | 0 | 0 | Test | |
| `53f5896f-b449-4a0c-8d38-b9eccb8a50cf.json` | 0 | 0 | | 1-1 |
| `5f1cb3f6-963d-44dd-8da5-a49b031c195f.json` | 0 | 0 | Test | |
| `6fe6c148-c26a-47c2-9667-136bea689ca5.json` | 0 | 0 | Test | |
| `7fd13f5a-3dae-4ab5-86a9-e65f99031298.json` | 0 | 0 | | 1-1 |
| `815602e1-d2af-4cdb-aaed-64c14b7e4549.json` | 0 | 0 | Legacy | |
| `a9fbd609-9c1f-4d7a-afe7-3dfdbc2ae0f8.json` | 0 | 0 | | |
| `caa3f0c5-8356-475d-9dac-34dea375ec80.json` | 0 | 0 | Test | |
| `d0c7cb45-0ec1-4a47-be8f-15ee762d93e1.json` | 0 | 0 | | |
| `d80819b7-ea08-4c23-bf91-555f2da2ced4.json` | 0 | 0 | Legacy | |

Sample file content (`382ac2a8-f567-4875-9e95-7eca0a6b54fb.json`):
```json
{
  "Id": "382ac2a8-f567-4875-9e95-7eca0a6b54fb",
  "CreatedAt": "2026-11-02T14:40:15.7811003+03:00",
  "UpdatedAt": "2026-11-02T14:40:15.7811191+03:00",
  "Status": "Новый",
  "ContractNumber": "",
  "ContractDate": "2026-11-02T00:00:00+03:00",
  "ClientName": "Test",
  "ClientPhone": "",
  "ClientAddress": "",
  "Notes": "",
  "HasAdditionalKp": false,
  "AdditionalKpNumber": "",
  "AdditionalKpAmount": 0,
  "AdditionalKps": [],
  "TotalAmount": 0,
  "Items": []
}
```

### 1.2 Storage Service & Trash Mechanics
In `MosquitoNetCalculator/Services/OrderStorageService.cs`:
- Static directory paths (lines 22-23):
  ```csharp
  public static string OrdersDir { get; set; } = Path.Combine(AppDataDir, "orders");
  public static string TrashDir { get; set; } = Path.Combine(AppDataDir, "orders_trash");
  ```
- Soft delete implementation (lines 223-242):
  ```csharp
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
- Trash loader (lines 271-304):
  ```csharp
  public List<OrderData> LoadTrashOrders()
  {
      var list = new List<OrderData>();
      if (!Directory.Exists(TrashDir))
          return list;

      DateTime threshold = DateTime.Now.AddDays(-30);

      foreach (var file in Directory.GetFiles(TrashDir, "*.json"))
      {
          try
          {
              var fileInfo = new FileInfo(file);
              if (fileInfo.CreationTime < threshold && fileInfo.LastWriteTime < threshold)
              {
                  File.Delete(file);
                  continue;
              }

              string json = File.ReadAllText(file, System.Text.Encoding.UTF8);
              var order = JsonSerializer.Deserialize<OrderData>(json, JsonOptions);
              if (order != null)
              {
                  list.Add(order);
              }
          }
          catch (Exception ex)
          {
              System.Diagnostics.Debug.WriteLine($"[OrderStorage] skip corrupted trash: {file} — {ex.Message}");
          }
      }

      return list.OrderByDescending(o => o.UpdatedAt).ToList();
  }
  ```
- Test leakage origin:
  In `MosquitoNetCalculator.Tests/ViewModels/OrdersHistoryViewModelTests.cs` (lines 30-38) and `MosquitoNetCalculator.Tests/Services/OrderStorageServiceTests.cs` (lines 32-40), tests redirect `OrderStorageService.OrdersDir = _ordersDir;`, but do NOT redirect `OrderStorageService.TrashDir`. When test `SaveAndDelete_Roundtrip` (`OrdersHistoryViewModelTests.cs:452-462`) or `DeleteOrder_RemovesOrder` (`OrderStorageServiceTests.cs:141-147`) runs `DeleteOrder(order.Id)`, it moves the test dummy order (`Items = []`, `TotalAmount = 0`, `ClientName = "Test"`) into the real `%APPDATA%\MosquitoNetCalculator\orders_trash`.

### 1.3 Order Structure & Save Validation
- In `MosquitoNetCalculator/Models/OrderData.cs`:
  - `Items`: `public List<OrderItemData> Items { get; set; } = new();`
  - `TotalAmount`: `public double TotalAmount { get; set; }`
- In `MosquitoNetCalculator/Controls/ActionBarControl.xaml.cs` (lines 109-114):
  ```csharp
  var allItems = mw.OrderItems.Where(i => !string.IsNullOrEmpty(i.Name) && i.Total > 0).ToList();
  if (allItems.Count == 0)
  {
      ToastService.ShowToast("Добавьте хотя бы одну позицию перед сохранением.", ToastType.Warning);
      return;
  }
  ```
  The UI blocks manual saving when items are empty.
- However, `OrderStorageService.SaveOrder` has no validation: any test or background service calling `SaveOrder` directly writes the order file, even with 0 items.

### 1.4 Trash Double-Click & Dialog Infrastructure
- In `MosquitoNetCalculator/MainWindow.xaml.cs` line 301:
  ```csharp
  OrdersHistoryControl.OrdersGrid.MouseDoubleClick += OrdersList_MouseDoubleClick;
  ```
- In `MosquitoNetCalculator/MainWindow.Orders.cs` lines 78-103:
  ```csharp
  private void OrdersList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
  {
      // Header clicks already trigger a sort — don't also open the order.
      if (OrderGridPresenter.IsHeaderClick(e.OriginalSource as DependencyObject))
          return;
      OpenSelectedOrder();
  }

  internal void OpenSelectedOrder()
  {
      if (OrdersHistoryControl.OrdersGrid.SelectedItem is not OrderData order) return;

      if (OrdersHistoryControl.IsTrashViewActive)
      {
          ToastService.ShowToast("Заказ находится в корзине. Восстановите его для открытия.", ToastType.Warning);
          return;
      }
      ...
  ```
  Double-clicking any order while in trash view currently emits a warning toast instead of prompting to restore.
- Dialog Infrastructure in `MosquitoNetCalculator/Services/DialogService.cs` and `MosquitoNetCalculator/Services/DialogBuilder.cs`:
  - Built on `DialogBuilder<T>` and `MessageDialogWindow.xaml`.
  - Has `ShowConfirm`, `ShowConfirmDestructive`, `ShowMessage`, `ShowSaveDiscardCancel`.
  - Exposes test seam `BuildConfirmDestructive` for headless unit testing of button contracts (`Content`, `IsDefault`, `IsCancel`, `StyleResource`).
- Order Loading & Calculation Navigation in `MosquitoNetCalculator/MainWindow.Orders.cs` (lines 122-183):
  Loads order fields into `ClientInfo`, `Sidebar`, invokes `ViewModel.CalcVM.LoadFromOrderData(order, RecalculateAndUpdateTotal)`, clears undo history, marks clean, and calls `NavigateToCalculation()`.

---

## 2. Logic Chain

### 2.1 R4 Logic Chain: Empty Dummy Orders & Trash Sanitization
1. **Observation 1.1 & 1.2** demonstrate that all 10 existing files in the user trash directory have 0 items and 0.00 ₽ sum, originating from tests running `DeleteOrder` against unisolated `TrashDir`.
2. An "empty dummy order" is unambiguously defined as:
   `(order.Items == null || order.Items.Count == 0) && Math.Abs(order.TotalAmount) < 0.001`.
3. To prevent empty dummy orders from entering the trash folder on deletion:
   In `OrderStorageService.SoftDeleteOrder(string orderId)`, before moving the file to `TrashDir`, the service should inspect the order file at `sourcePath`. If it is an empty dummy order, it must NOT be moved to `TrashDir`; instead, it must be deleted permanently via `File.Delete(sourcePath)` from `OrdersDir`.
4. To clean the production user trash folder:
   A cleanup method `OrderStorageService.CleanEmptyTrashOrders()` should iterate over `Directory.GetFiles(TrashDir, "*.json")`, deserialize each file, and delete any file where `(order.Items == null || order.Items.Count == 0) && Math.Abs(order.TotalAmount) < 0.001`.
   This cleanup should run:
   - During `OrderStorageService` initialization / constructor (targeting current `TrashDir`).
   - In `MainWindow.xaml.cs` on startup.
   - Inside `OrderStorageService.LoadTrashOrders()` (analogous to the 30-day auto-purge).

### 2.2 R5 Logic Chain: Interactive Restore Confirmation on Double-Click
1. **Observation 1.4** shows that double-clicking a row triggers `OrdersList_MouseDoubleClick` -> `OpenSelectedOrder()`.
2. In `OpenSelectedOrder()`, if `OrdersHistoryControl.IsTrashViewActive` is true, the current code displays `ToastService.ShowToast("Заказ находится в корзине. Восстановите его для открытия.", ToastType.Warning);`.
3. Following R5 requirements:
   - When `OrdersHistoryControl.IsTrashViewActive` is true, replace the toast warning with an immediate confirmation dialog:
     `«Восстановить заказ «{order.ContractNumber}» из корзины?»`
     with `[ Восстановить ]` (Primary) and `[ Отмена ]` (Ghost) buttons.
   - Use `DialogService`:
     Add `ShowRestoreConfirm(string contractNumber, Window? owner = null)` and `BuildRestoreConfirm(string contractNumber)`.
   - If the user cancels (`false`): return immediately. Do not display any toast notifications; leave the order in trash.
   - If the user confirms (`true`):
     a. If `IsDirty` is true in calculation view, handle unsaved changes via `ShowSaveDiscardCancel`.
     b. Call `ViewModel.OrdersVM.RestoreOrder(order.Id)` (moves file from `TrashDir` to `OrdersDir`).
     c. Call `RefreshOrdersList()` to update the history grid and badge count.
     d. Load the restored order into calculation view using the existing loading logic (`ClientInfo`, `CalcVM.LoadFromOrderData`, `NavigateToCalculation()`).

---

## 3. Caveats

1. **Unit Test Dependency on Numbering Without Items**:
   In `OrderStorageServiceTests.cs` and `ManualChecklistTests.cs`, tests frequently save dummy `OrderData` objects with 0 items (e.g. `_service.SaveOrder(new OrderData { ContractNumber = "1-5" })`) to verify contract number auto-increment logic (`GetNextOrderNumber`, `GenerateCopyContractNumber`).
   Therefore, `OrderStorageService.SaveOrder` must NOT throw an exception or reject saving empty orders to `OrdersDir`, as doing so would break ~20 existing contract numbering tests. Instead, empty order prevention must focus on ensuring that empty dummy orders **cannot enter the trash directory** upon deletion/save/reset.
2. **Dirty State on Restore**:
   If the user has unsaved changes on the Calculation tab, restoring and opening an order from trash should prompt `ShowSaveDiscardCancel` so the user does not inadvertently lose uncommitted calculation inputs.

---

## 4. Conclusion & Proposed Code Design

### 4.1 Requirement R4 Design

#### In `MosquitoNetCalculator/Models/OrderData.cs`:
Add helper property (marked `[JsonIgnore]` so it doesn't write to JSON):
```csharp
[System.Text.Json.Serialization.JsonIgnore]
public bool IsEmptyDummy => (Items == null || Items.Count == 0) && Math.Abs(TotalAmount) < 0.001;
```

#### In `MosquitoNetCalculator/Services/OrderStorageService.cs`:
1. Add trash cleanup method:
```csharp
public static void CleanEmptyTrashOrders(string? trashDir = null)
{
    string targetDir = trashDir ?? TrashDir;
    if (!Directory.Exists(targetDir)) return;

    foreach (var file in Directory.GetFiles(targetDir, "*.json"))
    {
        try
        {
            string json = File.ReadAllText(file, System.Text.Encoding.UTF8);
            var order = JsonSerializer.Deserialize<OrderData>(json, JsonOptions);
            if (order == null || order.IsEmptyDummy)
            {
                File.Delete(file);
            }
        }
        catch
        {
            // Corrupt file in trash — can be pruned or left to LoadTrashOrders
        }
    }
}
```
2. Call `CleanEmptyTrashOrders()` in `OrderStorageService` constructor and in `LoadTrashOrders()`.
3. In `SoftDeleteOrder(string orderId)`:
```csharp
public void SoftDeleteOrder(string orderId)
{
    string sourcePath = ResolveOrderFilePath(orderId);
    string destPath = ResolveTrashFilePath(orderId);

    lock (_cacheLock)
    {
        if (File.Exists(sourcePath))
        {
            OrderData? order = null;
            try
            {
                string json = File.ReadAllText(sourcePath, System.Text.Encoding.UTF8);
                order = JsonSerializer.Deserialize<OrderData>(json, JsonOptions);
            }
            catch { }

            // Empty dummy orders are purged directly and NEVER enter the trash directory:
            if (order != null && order.IsEmptyDummy)
            {
                File.Delete(sourcePath);
                _cachedOrders = null;
                return;
            }

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

### 4.2 Requirement R5 Design

#### In `MosquitoNetCalculator/Services/DialogService.cs`:
Add `ShowRestoreConfirm` and `BuildRestoreConfirm`:
```csharp
public static bool ShowRestoreConfirm(string contractNumber, Window? owner = null)
    => BuildRestoreConfirm(contractNumber).ShowDialog(owner);

internal static DialogBuilder<bool> BuildRestoreConfirm(string contractNumber)
{
    return new DialogBuilder<bool>()
        .Title("Восстановление заказа")
        .Message($"Восстановить заказ «{contractNumber}» из корзины?")
        .WithButton("Отмена", false, isCancel: true, styleResource: "GhostButton")
        .WithButton("Восстановить", true, isDefault: true, styleResource: "PrimaryButton");
}
```

#### In `MosquitoNetCalculator/MainWindow.Orders.cs`:
Extract the order loading logic into `LoadOrderIntoCalculation(OrderData order)` and update `OpenSelectedOrder`:
```csharp
internal void OpenSelectedOrder()
{
    if (OrdersHistoryControl.OrdersGrid.SelectedItem is not OrderData order) return;

    if (OrdersHistoryControl.IsTrashViewActive)
    {
        // Interactive Restore Confirmation on Double-Click (R5)
        if (!DialogService.ShowRestoreConfirm(order.ContractNumber, this))
            return; // Cancel: no toast, leaves order in trash

        if (IsDirty)
        {
            var choice = DialogService.ShowSaveDiscardCancel("В текущем расчёте есть несохранённые изменения.\nСохранить перед открытием другого заказа?", owner: this);
            if (choice == Services.SaveDiscardCancelResult.Cancel)
                return;
            if (choice == Services.SaveDiscardCancelResult.Save)
            {
                ApplicationCommands.Save.Execute(null, this);
                if (IsDirty) return;
            }
        }

        ViewModel.OrdersVM.RestoreOrder(order.Id);
        RefreshOrdersList();
        LoadOrderIntoCalculation(order);
        return;
    }

    // Normal active order opening...
    if (IsDirty)
    {
        var choice = DialogService.ShowSaveDiscardCancel("В текущем расчёте есть несохранённые изменения.\nСохранить перед открытием другого заказа?", owner: this);
        if (choice == Services.SaveDiscardCancelResult.Cancel)
            return;
        if (choice == Services.SaveDiscardCancelResult.Save)
        {
            ApplicationCommands.Save.Execute(null, this);
            if (IsDirty) return;
        }
    }

    var orderDescription = !string.IsNullOrWhiteSpace(order.ClientPhone)
        ? $" — {order.ClientPhone}"
        : (!string.IsNullOrWhiteSpace(order.ClientAddress) ? $" — {order.ClientAddress}" : "");
    if (!DialogService.ShowConfirm($"Открыть заказ «{order.ContractNumber}»{orderDescription}?\n\nТекущие данные будут заменены.", "Открыть заказ", this)) return;

    LoadOrderIntoCalculation(order);
}
```

---

## 5. Verification Method

### 5.1 Verification Commands
1. **Compilation Check**:
   `dotnet build MosquitoNetCalculator/MosquitoNetCalculator.csproj`
2. **Unit Test Suite**:
   `dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj`
   - Baseline verification (verified 2026-11-02T12:29:22Z): Total 2806 tests passed, 0 failed, 0 skipped.
3. **Trash Directory Cleanliness Check**:
   PowerShell command:
   `(Get-ChildItem -Path "$env:APPDATA\MosquitoNetCalculator\orders_trash" -Filter *.json -ErrorAction SilentlyContinue).Count`
   Must output `0` after cleanup.

### 5.2 Unit Tests to Add/Verify
1. `MosquitoNetCalculator.Tests/Services/DialogBuilderTests.cs`:
   - `BuildRestoreConfirm_HasCorrectButtonsAndLabels`: verifies default button is «Восстановить», cancel button is «Отмена», message contains contract number.
2. `MosquitoNetCalculator.Tests/Services/OrderStorageServiceTests.cs`:
   - `SoftDeleteOrder_EmptyDummyOrder_PermanentlyDeletesAndDoesNotEnterTrash`: verifies that calling `SoftDeleteOrder` on an order with 0 items and 0 sum deletes the order from `OrdersDir` and leaves `TrashDir` empty.
   - `CleanEmptyTrashOrders_RemovesOnlyDummyOrders`: verifies that dummy orders are deleted from trash while orders with items/sum are preserved.

### 5.3 Invalidation Conditions
- If `SoftDeleteOrder` still creates a file in `TrashDir` when deleting an order with 0 items.
- If double-clicking a trash item shows a toast warning instead of the confirmation dialog.
- If canceling the confirmation dialog causes an error, a toast notification, or opens the order.
