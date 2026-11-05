# Project: MosquitoNetCalculator Draft Recovery UI, Test Trash Isolation & Trash UX

## Architecture
WPF / .NET desktop application for mosquito net manufacturing calculations.
Key components touched by this mission:
- `MosquitoNetCalculator/Controls/DraftRecoveryBanner.xaml` & `.xaml.cs` (Visual redesign, Excel-style banner layout, metadata display, action buttons, close button postpone logic, removal of redundant postpone button)
- `MosquitoNetCalculator/Controls/DraftItemsPickerDialog.xaml` & `.xaml.cs` (Window sizing SizeToContent="Height", MaxHeight="520", spacing typo fix `Anwis (Белый)`, item cards styling with Radius.Control)
- `MosquitoNetCalculator/Services/OrderStorageService.cs` & `Models/OrderData.cs` (Empty dummy order definition, empty order trash prevention in SoftDeleteOrder, CleanEmptyTrashOrders cleanup method)
- `MosquitoNetCalculator/Services/DialogService.cs` & `MainWindow.Orders.cs` (Interactive confirmation dialog on double-click in trash view, order restoration and opening in calculation view)
- `MosquitoNetCalculator.Tests` (Isolation of both OrdersDir and TrashDir to temporary directories across all test suites, cleanup of %APPDATA%\MosquitoNetCalculator\orders_trash)

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | R1.1 Banner Layout & Tokens | Left side: icon in soft background container, title «Восстановление черновика», summary metadata `от {time} (позиций: {count}, клиент: {client})`. Right side: [Восстановить всё] (PrimaryButton), [Выбрать позиции…] (GhostButton replacing missing SecondaryButton), [Удалить черновик] (GhostButton with &#xE74D; trash icon), [✕] close button | M1 | ORIGINAL_REQUEST §R1 |
| 2 | R1.2 Banner Postpone / Close | Standalone «Отложить» button removed from banner; clicking [✕] closes/postpones banner and keeps draft intact on disk | M1 | ORIGINAL_REQUEST §R1 |
| 3 | R2.1 Picker Sizing | `DraftItemsPickerDialog` dynamic height adapts to content (`SizeToContent="Height"`, `MaxHeight="520"`) | M1 | ORIGINAL_REQUEST §R2 |
| 4 | R2.2 Picker Spacing Typo | Fix spacing typo in item name formatting: ensure space before parenthesis: `Anwis (Белый)` | M1 | ORIGINAL_REQUEST §R2 |
| 5 | R2.3 Picker Card Styling | Style item cards with rounded corners (`Radius.Control` = 8px) and clear, legible contrast | M1 | ORIGINAL_REQUEST §R2 |
| 6 | R3 Unit Test TrashDir Isolation | Isolate `OrderStorageService.TrashDir` in all unit tests (`OrdersHistoryViewModelTests`, `OrderStorageServiceTests`, `ManualChecklistTests`, `StabilityAndSafetyFeaturesTests`, `OrderImportExportServiceValidationTests`) so running tests creates zero files in `%APPDATA%\MosquitoNetCalculator\orders_trash` | M2 | ORIGINAL_REQUEST §R3 |
| 7 | R4.1 Trash Folder Cleanup | Clean production user trash folder (`%APPDATA%\MosquitoNetCalculator\orders_trash`) of existing empty dummy test order files (0 items and 0.00 ₽ sum) | M3 | ORIGINAL_REQUEST §R4 |
| 8 | R4.2 Empty Order Trash Prevention | Prevent empty dummy orders (0 items and 0.00 ₽ sum) from entering trash folder on save, reset, or deletion | M3 | ORIGINAL_REQUEST §R4 |
| 9 | R5.1 Restore Dialog on Double-Click | Double-clicking an item in trash list displays confirmation dialog: «Восстановить заказ «{номер}» из корзины?» with [Восстановить] and [Отмена] buttons without toast spam | M4 | ORIGINAL_REQUEST §R5 |
| 10 | R5.2 Restore Flow Execution | Confirming restore dialog restores order from trash and opens in calculation view; canceling leaves order in trash without error or toast spam | M4 | ORIGINAL_REQUEST §R5 |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M0 | Survey & Codebase Mapping | Deep dive across R1-R5 files and test suites | none | DONE |
| M1 | Draft Recovery UI (R1 & R2) | DraftRecoveryBanner layout & DraftItemsPickerDialog sizing/styling/typo | M0 | DONE |
| M2 | Test TrashDir Isolation (R3) | Isolate TrashDir in all test suites and fix field init races | M0 | DONE |
| M3 | Trash Sanitization & Empty Order Guard (R4) | Empty dummy order definition, SoftDeleteOrder guard, CleanEmptyTrashOrders | M2 | DONE |
| M4 | Trash Restore Confirmation Dialog (R5) | DialogService.ShowRestoreConfirm, double-click handler in MainWindow.Orders.cs | M3 | DONE |
| M5 | Verification & Forensic Audit | Clean builds, full test suite, reviewers, challengers, auditor | M1, M2, M3, M4 | IN_PROGRESS |

## Interface Contracts
### `DraftRecoveryBanner` ↔ `MainWindow.Recovery.cs`
- Banner exposes events: `RestoreRequested`, `SelectiveRestoreRequested`, `PostponeRequested`, `DiscardRequested`.
- Clicking `BtnClose` [ ✕ ] fires `PostponeRequested` (draft file remains intact).
- `BtnSelective` uses `GhostButton` (resolves broken `SecondaryButton` reference).
- `BtnDiscard` uses `GhostButton` with icon `&#xE74D;`.

### `DraftItemsPickerDialog`
- Window uses `Width="580" SizeToContent="Height" MaxHeight="520"`.
- Item name formatting: `DraftItemPickerViewModel.ColorSuffix` returns `$" ({ItemData.Color})"` (space before parenthesis).
- Card container uses `CornerRadius="{DynamicResource Radius.Control}"` (8px).

### `OrderStorageService` & Tests
- `OrderStorageService.TrashDir`: Default `%APPDATA%\MosquitoNetCalculator\orders_trash`.
- In unit tests: All test classes redirect both `OrdersDir` and `TrashDir` to temp directories in constructor, restore them in `Dispose()`, and use `[Collection("FileSystem")]`.
- `OrderData.IsEmptyDummy`: `(Items == null || Items.Count == 0) && Math.Abs(TotalAmount) < 0.001`.
- `OrderStorageService.SoftDeleteOrder`: If order `IsEmptyDummy`, deletes from `OrdersDir` directly; does NOT move to `TrashDir`.
- `OrderStorageService.CleanEmptyTrashOrders()`: Scans `TrashDir` and purges any empty dummy order files.

### `DialogService` ↔ `MainWindow.Orders.cs`
- `DialogService.ShowRestoreConfirm(string contractNumber, Window? owner = null)`: returns `bool`.
- Double-clicking order in trash view prompts restore dialog. If true: restores order, updates history, opens in calculation. If false: does nothing (no toast).

## Code Layout
- `MosquitoNetCalculator/Controls/DraftRecoveryBanner.xaml` & `.xaml.cs` (Owned by M1 — DONE)
- `MosquitoNetCalculator/Controls/DraftItemsPickerDialog.xaml` & `.xaml.cs` (Owned by M1 — DONE)
- `MosquitoNetCalculator.Tests/Controls/DraftRecoveryBannerTests.cs` (Owned by M1 — DONE)
- `MosquitoNetCalculator.Tests/Controls/DraftItemsPickerDialogTests.cs` (Owned by M1 — DONE)
- `MosquitoNetCalculator.Tests/ViewModels/OrdersHistoryViewModelTests.cs` (Owned by M2 — DONE)
- `MosquitoNetCalculator.Tests/Services/StabilityAndSafetyFeaturesTests.cs` (Owned by M2 — DONE)
- `MosquitoNetCalculator.Tests/App/ManualChecklistTests.cs` (Owned by M2 — DONE)
- `MosquitoNetCalculator.Tests/Services/OrderImportExportServiceValidationTests.cs` (Owned by M2 — DONE)
- `MosquitoNetCalculator/Models/OrderData.cs` (Owned by M3 — DONE)
- `MosquitoNetCalculator/Services/OrderStorageService.cs` (Owned by M3 — DONE)
- `MosquitoNetCalculator.Tests/Services/OrderStorageServiceTests.cs` (Owned by M2/M3 — DONE)
- `MosquitoNetCalculator/Services/DialogService.cs` (Owned by M4 — DONE)
- `MosquitoNetCalculator/MainWindow.Orders.cs` (Owned by M4 — DONE)
- `MosquitoNetCalculator.Tests/Services/DialogBuilderTests.cs` (Owned by M4 — DONE)
