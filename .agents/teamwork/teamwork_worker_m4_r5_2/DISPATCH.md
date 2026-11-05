## 2026-11-02T13:04:59Z
You are teamwork_worker_m4_2 (replacement worker for Milestone 4).
Your working directory is: c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\teamwork_worker_m4_r5_2
Your parent is: 5194245e-d76b-4446-9341-e73f2b5a09dc

MANDATORY: Read the original user request before starting work:
c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\ORIGINAL_REQUEST.md

Read the project scope and explorer findings:
- PROJECT.md: c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\orchestrator\PROJECT.md
- Survey findings: c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\teamwork_explorer_s3\handoff.md

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

EXCLUSIVE WRITE OWNERSHIP (you may only edit these files):
- MosquitoNetCalculator/Services/DialogService.cs
- MosquitoNetCalculator/MainWindow.Orders.cs
- MosquitoNetCalculator.Tests/Services/DialogBuilderTests.cs

Task: Implement Milestone 4 (Requirement R5: Interactive Restore Confirmation on Trash Double-Click):
1. In `MosquitoNetCalculator/Services/DialogService.cs`:
   - Add `ShowRestoreConfirm(string contractNumber, Window? owner = null)` and `BuildRestoreConfirm(string contractNumber)`.
   - Title: "Восстановление заказа"
   - Message: `$"Восстановить заказ «{contractNumber}» из корзины?"`
   - Buttons:
     * `[ Восстановить ]`: `value: true, isDefault: true, styleResource: "PrimaryButton"`
     * `[ Отмена ]`: `value: false, isCancel: true, styleResource: "GhostButton"`
2. In `MosquitoNetCalculator/MainWindow.Orders.cs`:
   - In `OpenSelectedOrder()`:
     When double-clicking an order in trash (`OrdersHistoryControl.IsTrashViewActive` is true):
     - Replace the warning toast with an immediate confirmation dialog: `DialogService.ShowRestoreConfirm(order.ContractNumber, this)`.
     - If the user cancels: return immediately without displaying any toast warnings; leave the order in trash.
     - If the user confirms:
       * Check `IsDirty`: if dirty, prompt `ShowSaveDiscardCancel` so unsaved work isn't lost.
       * Restore the order: `ViewModel.OrdersVM.RestoreOrder(order.Id)`.
       * Refresh list: `RefreshOrdersList()`.
       * Open the restored order in calculation view (load data into `ClientInfo`, `Sidebar`, `ViewModel.CalcVM.LoadFromOrderData(order, RecalculateAndUpdateTotal)`, reset undo stack, and call `NavigateToCalculation()`).
3. In `MosquitoNetCalculator.Tests/Services/DialogBuilderTests.cs`:
   - Add unit test `BuildRestoreConfirm_HasCorrectButtonsAndLabels` verifying the dialog structure, message, and button configurations.
4. Build and verify:
   - `dotnet build MosquitoNetCalculator/MosquitoNetCalculator.csproj`
   - `dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj --filter DialogBuilderTests`

Write your complete handoff report to:
c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\teamwork_worker_m4_r5_2\handoff.md
Update progress.md in your directory as you work.
When finished, send a message to parent with summary and test results.
