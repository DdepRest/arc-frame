# Dispatch Log

## 2026-11-02T10:05:45Z
Implement the approved UI layout, installation choice usability, and crash recovery plan for MosquitoNetCalculator in production.

Integrity mode: development

## Requirements

### R1. Sidebar Overlay and Expanded Navigation
Restore the right sidebar to a slide-out overlay (`SidebarOverlay`) accessible via the customer button, removing it from persistent display in `CalculationView`. Keep the left navigation panel (`NavPanel`) expanded by default (160px width with icons and labels), allowing manual collapse by the user.

### R2. Segmented Pill Tabs for Orders History
Replace the default Windows radio buttons for the order list / cart toggle (`Все заказы` / `Корзина`) in `OrdersHistoryControl` with segmented pill tabs styled according to the design system tokens (`SurfaceVariant` container, `Accent` background for active tab, `Radius.Control`, and `Pad.ButtonSm`).

### R3. Installation Choice Usability and QuickAdd Staging Guard
Add a live total price preview (`TxtTotalPreview`) inside `InstallationChoiceDialog` that recalculates in real time when switching installation modes, modifying adjustment amounts, or toggling the adjustment sign. In `QuickAddControl`, stage the item in memory before adding it to the calculation table, displaying `InstallationChoiceDialog` if needed and adding the item to `OrderItems` only after dialog confirmation or default acceptance on Escape.

### R4. Crash Recovery Draft Preservation
Prevent `MainWindow` startup initialization from clearing `draft.json` so existing drafts are preserved across crashes and restarts. Only discard drafts upon explicit user reset actions ("New Order" / "Clear All"). Suppress toast notifications during silent application startup when no draft exists, showing them only when explicitly requested by user action. Update `DraftRecoveryBanner` styling to use valid theme tokens (`SectionBg`, `Border`, `AccentLight`, `AccentText`).

## Verification Resources
- Build command: `dotnet build MosquitoNetCalculator/MosquitoNetCalculator.csproj -c Release`
- Test filter: `dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj --filter "FullyQualifiedName~InstallationChoiceDialog|FullyQualifiedName~StabilityAndSafetyFeaturesTests|FullyQualifiedName~QuickAddControlTests|FullyQualifiedName~DesignTokenGuardTests"`
- Documentation validation: `powershell -ExecutionPolicy Bypass -File agents/scripts/validate-docs.ps1`

## Acceptance Criteria

### UI Layout & Navigation
- [ ] Right sidebar is displayed as an overlay modal/panel rather than occupying permanent grid space in `CalculationView`.
- [ ] Left navigation panel opens expanded (160px width) on application startup.
- [ ] Orders history cart switcher renders as segmented pill tabs without Windows standard radio button circles.

### Installation Choice Usability
- [ ] `InstallationChoiceDialog` displays a real-time recalculating total amount preview reflecting mode, adjustment value, and sign.
- [ ] Items requiring installation choice are not added to the `CalculationView` table until the dialog is accepted or dismissed with default.

### Crash Recovery
- [ ] Launching the application does not delete existing `draft.json`.
- [ ] If a draft exists, `DraftRecoveryBanner` is displayed using valid theme tokens and styling.
- [ ] Explicitly starting a new order or clearing all clears the draft as expected.

### Verification & Stability
- [ ] Project builds cleanly in Release configuration without compilation errors.
- [ ] All targeted unit and regression tests pass.
- [ ] `agents/scripts/validate-docs.ps1` completes with 0 errors.


## 2026-11-02T12:16:38Z
Improve the visual styling of the draft recovery banner and draft items picker dialog, isolate unit test trash directories, clean empty dummy orders from the trash folder, prevent empty orders from being trashed, and add an interactive restore confirmation dialog on double-click in the trash view.

Integrity mode: development

## Requirements

### R1. Visual Redesign of DraftRecoveryBanner
Update the layout and visual presentation of `DraftRecoveryBanner` to a clean Excel-style design:
- Left side: Display an icon in a soft background container, the title «Восстановление черновика», and summary information (date, item count, client name).
- Right side: Provide action buttons: `[ Восстановить всё ]` (Accent), `[ Выбрать позиции… ]` (Secondary/Outline), `[ Удалить черновик ]` (Ghost with trash/reset icon), and a close button `[ ✕ ]` that closes/postpones the banner without deleting the draft. Remove the redundant «Отложить» button.
- Apply consistent spacing and padding according to the design system tokens.

### R2. Sizing and Styling of DraftItemsPickerDialog
Update `DraftItemsPickerDialog`:
- Set window sizing to `SizeToContent="Height"` with `MaxHeight="520"` so the window height adapts to the number of items instead of showing a large empty box.
- Fix the display name spacing typo: ensure `Anwis (Белый)` has a space before the parenthesis containing the color.
- Style item cards with rounded corners and clear, readable contrast.

### R3. Unit Test TrashDir Isolation
Isolate `OrderStorageService.TrashDir` in all unit tests (such as `OrdersHistoryViewModelTests` and others) to a temporary directory matching `OrdersDir`, ensuring test runs do not create dummy files in the production user trash folder (`%APPDATA%\MosquitoNetCalculator\orders_trash`).

### R4. Trash Folder Cleanup and Empty Order Validation
- Clean the production user trash folder (`%APPDATA%\MosquitoNetCalculator\orders_trash`) of empty dummy test order files (orders with 0 items and 0.00 ₽ sum).
- Add validation/filtering so that empty dummy orders without items and with a sum of 0 cannot be saved or moved into the trash folder upon save or reset.

### R5. Interactive Restore Confirmation on Trash Double-Click
When a user double-clicks an order in the trash list, do not display repetitive toast warnings. Instead, immediately display a confirmation dialog: «Восстановить заказ «{номер}» из корзины?» with `[ Восстановить ]` and `[ Отмена ]` buttons. Upon confirmation, restore the order from trash and open it in the calculation view.

## Verification Resources
- Build command: `dotnet build MosquitoNetCalculator/MosquitoNetCalculator.csproj`
- Test suite: `dotnet test`

## Acceptance Criteria

### Draft Recovery UI
- [ ] `DraftRecoveryBanner` presents the title, icon in soft container, and summary metadata on the left, and action buttons (`Восстановить всё`, `Выбрать позиции…`, `Удалить черновик`, and `✕`) on the right.
- [ ] The standalone «Отложить» button is removed from `DraftRecoveryBanner`; clicking `✕` closes/postpones the banner without deleting the draft.
- [ ] `DraftItemsPickerDialog` dynamic height adapts to content (`SizeToContent="Height"`, `MaxHeight="520"`).
- [ ] The name format has a space before parenthesis: `Anwis (Белый)`.
- [ ] Item cards in `DraftItemsPickerDialog` render with rounded corners and legible contrast.

### Test Isolation & Trash Sanitization
- [ ] All unit tests isolate both `OrderStorageService.OrdersDir` and `OrderStorageService.TrashDir` to temporary directories; running tests creates zero files in `%APPDATA%\MosquitoNetCalculator\orders_trash`.
- [ ] Existing dummy test files with 0 items and 0.00 ₽ sum are removed from the user trash directory.
- [ ] Empty orders with 0 items and 0.00 ₽ sum cannot enter the trash folder on save, reset, or deletion.

### Trash Usability
- [ ] Double-clicking an item in the trash list opens a restore confirmation dialog showing the order number and [ Восстановить ] / [ Отмена ] options without spamming toast notifications.
- [ ] Confirming the restore dialog restores the order and opens it in calculation view.
- [ ] Canceling the restore dialog leaves the order in trash without error or toast spam.

### Verification
- [ ] Project builds cleanly (`dotnet build`) without compilation errors.
- [ ] All unit tests pass cleanly via `dotnet test`.
