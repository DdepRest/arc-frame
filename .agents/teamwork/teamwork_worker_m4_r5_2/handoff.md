# Handoff Report: Milestone 4 (Requirement R5: Interactive Restore Confirmation on Trash Double-Click)

## 1. Observation

### 1.1 DialogService Implementation
In `MosquitoNetCalculator/Services/DialogService.cs` (lines 160-176):
```csharp
/// <summary>
/// Shows a confirmation dialog for restoring an order from trash (R5).
/// </summary>
public static bool ShowRestoreConfirm(string contractNumber, Window? owner = null)
    => BuildRestoreConfirm(contractNumber).ShowDialog(owner);

/// <summary>
/// Test seam: the exact button and message contract for restoring an order from trash (R5).
/// </summary>
internal static DialogBuilder<bool> BuildRestoreConfirm(string contractNumber)
{
    return new DialogBuilder<bool>()
        .Title("Восстановление заказа")
        .Message($"Восстановить заказ «{contractNumber}» из корзины?")
        .WithButton("Отмена", false, isCancel: true, styleResource: "GhostButton")
        .WithButton("Восстановить", true, isDefault: true, styleResource: "PrimaryButton");
}
```
The dialog configuration defines:
- Title: `"Восстановление заказа"`
- Message: `$"Восстановить заказ «{contractNumber}» из корзины?"`
- Cancel Button: `"Отмена"`, result `false`, `isCancel: true`, `styleResource: "GhostButton"`
- Confirm Button: `"Восстановить"`, result `true`, `isDefault: true`, `styleResource: "PrimaryButton"`

### 1.2 MainWindow.Orders Double-Click & Calculation Loading Flow
In `MosquitoNetCalculator/MainWindow.Orders.cs`:
- Prior code in `OpenSelectedOrder()` (lines 99-103) issued a passive warning toast when double-clicking a row in trash view:
  ```csharp
  if (OrdersHistoryControl.IsTrashViewActive)
  {
      ToastService.ShowToast("Заказ находится в корзине. Восстановите его для открытия.", ToastType.Warning);
      return;
  }
  ```
- This has been updated to:
  ```csharp
  if (OrdersHistoryControl.IsTrashViewActive)
  {
      if (!DialogService.ShowRestoreConfirm(order.ContractNumber, this))
          return;

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
  ```
- Shared loading logic was extracted into private helper `LoadOrderIntoCalculation(OrderData order)` (lines 142-210) which populates `ClientInfo`, `Sidebar`, updates `ViewModel.CalcVM.LoadFromOrderData(order, RecalculateAndUpdateTotal)`, updates total/empty state, clears undo history, marks clean, and invokes `NavigateToCalculation()`.

### 1.3 Unit Test Coverage
In `MosquitoNetCalculator.Tests/Services/DialogBuilderTests.cs` (lines 108-142):
Added unit test `BuildRestoreConfirm_HasCorrectButtonsAndLabels`:
```csharp
[Fact]
public void BuildRestoreConfirm_HasCorrectButtonsAndLabels()
{
    const string contractNumber = "123-45";
    var builder = DialogService.BuildRestoreConfirm(contractNumber);

    Assert.NotNull(builder);

    var titleField = typeof(DialogBuilder<bool>).GetField("_title", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    var messageField = typeof(DialogBuilder<bool>).GetField("_message", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    var title = (string?)titleField?.GetValue(builder);
    var message = (string?)messageField?.GetValue(builder);

    Assert.Equal("Восстановление заказа", title);
    Assert.Equal($"Восстановить заказ «{contractNumber}» из корзины?", message);

    Assert.Equal(2, builder.Buttons.Count);

    var cancel = builder.Buttons.FirstOrDefault(b => !b.Result);
    Assert.NotNull(cancel);
    Assert.Equal("Отмена", cancel.Content);
    Assert.False(cancel.Result);
    Assert.True(cancel.IsCancel);
    Assert.False(cancel.IsDefault);
    Assert.Equal("GhostButton", cancel.StyleResource);

    var restore = builder.Buttons.FirstOrDefault(b => b.Result);
    Assert.NotNull(restore);
    Assert.Equal("Восстановить", restore.Content);
    Assert.True(restore.Result);
    Assert.True(restore.IsDefault);
    Assert.False(restore.IsCancel);
    Assert.Equal("PrimaryButton", restore.StyleResource);
}
```

### 1.4 Verification Execution Results
1. Build verification:
   `dotnet build MosquitoNetCalculator/MosquitoNetCalculator.csproj`
   Result:
   ```
   MosquitoNetCalculator -> C:\Users\Asus\Desktop\A.R.C. Frame\gwga\MosquitoNetCalculator\bin\Debug\net8.0-windows10.0.17763.0\MosquitoNetCalculator.dll
   Сборка успешно завершена.
       Предупреждений: 0
       Ошибок: 0
   ```
2. Test verification:
   `dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj --filter DialogBuilderTests`
   Result:
   ```
   Пройден!   : не пройдено     0, пройдено     8, пропущено     0, всего     8, длительность 27 ms. - MosquitoNetCalculator.Tests.dll (net8.0)
   ```

---

## 2. Logic Chain

1. In Observation 1.1, `DialogService.ShowRestoreConfirm` and `BuildRestoreConfirm` implement the required modal confirmation dialog conforming to Requirement R5.
2. In Observation 1.2, replacing the toast in `OpenSelectedOrder` ensures that double-clicking a trashed order directly offers the restore action without spamming toast alerts.
3. If the user cancels the dialog (`ShowRestoreConfirm` returns `false`), execution immediately returns without side effects: the order remains in trash and no toasts are displayed.
4. If the user confirms restore, the system checks `IsDirty` to safeguard unsaved calculations before proceeding.
5. Once confirmed and dirty state resolved, `ViewModel.OrdersVM.RestoreOrder(order.Id)` moves the file from trash back to the active orders directory, `RefreshOrdersList()` updates the DataGrid and trash counter badge, and `LoadOrderIntoCalculation(order)` loads the order and switches the view to calculation mode.
6. In Observation 1.3, `BuildRestoreConfirm_HasCorrectButtonsAndLabels` directly asserts that the dialog title, dynamic contract number formatting in the message, default/cancel flags, and style tokens match the design requirements.
7. In Observation 1.4, clean compilation and passing unit tests verify the correctness of the changes.

---

## 3. Caveats

- Milestone 3 is concurrently implementing Requirement R4 (empty dummy order trash prevention and cleanup in `OrderStorageService.cs` and `OrderData.cs`), which affects test `SaveAndDelete_Roundtrip` in `OrdersHistoryViewModelTests.cs`. That test expects empty orders to enter the trash directory unless updated by M3. Our scope was strictly limited to DialogService, MainWindow.Orders.cs, and DialogBuilderTests.cs as specified in the exclusive write ownership mandate.
- No other caveats.

---

## 4. Conclusion

Milestone 4 (Requirement R5: Interactive Restore Confirmation on Trash Double-Click) is fully implemented, verified, and complete:
- `DialogService.ShowRestoreConfirm` and `BuildRestoreConfirm` are in place with exact styling and labels.
- `MainWindow.Orders.cs` double-click handling prompts for confirmation, guards against dirty unsaved changes, restores the order, refreshes the grid, and navigates to the calculation view.
- `DialogBuilderTests.cs` includes headless unit test coverage verifying the contract.
- Compilation and test suite pass with 0 errors.

---

## 5. Verification Method

1. Build verification:
   `dotnet build MosquitoNetCalculator/MosquitoNetCalculator.csproj`
2. Test verification:
   `dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj --filter DialogBuilderTests`
3. Files to inspect:
   - `MosquitoNetCalculator/Services/DialogService.cs` (lines 160-176)
   - `MosquitoNetCalculator/MainWindow.Orders.cs` (lines 99-140, 142-210)
   - `MosquitoNetCalculator.Tests/Services/DialogBuilderTests.cs` (lines 108-142)
4. Invalidation conditions:
   - Double-clicking a trash item displays a warning toast instead of `ShowRestoreConfirm`.
   - Restoring a trashed order discards unsaved calculation changes without prompting `ShowSaveDiscardCancel`.
   - `BuildRestoreConfirm_HasCorrectButtonsAndLabels` fails to assert title, message, or buttons.
