# Execution Plan: MosquitoNetCalculator UI, Usability & Crash Recovery

## Survey Findings Summary (Phase 0 Complete)
1. **R1**:
   - `SidebarPanel` in `MainWindow.xaml` to be moved to overlay stack at `Panel.ZIndex="15"` alongside `OrdersOverlay`, `PricesOverlay`, etc.
   - `_sidebarEntry` to be added to `_overlayManager`.
   - Remove col 1 from `CalculationView`.
   - Remove lines 375–380 from `MainWindow.xaml.cs` (forced startup collapse of NavPanel) to restore 160px default width.
2. **R2**:
   - Register `SurfaceVariant` in `Themes/Brushes.xaml` (`#F0F0F4`) and `Services/ThemeService.cs`.
   - Implement `SegmentedRadioButton` in `Themes/InputStyles.RadioButton.xaml` using `SurfaceVariant`, `Accent`, `Radius.Control`, and `Pad.ButtonSm`.
3. **R3**:
   - In `Controls/InstallationChoiceDialog.xaml`, add `TxtTotalPreview` TextBlock.
   - In `Controls/InstallationChoiceDialog.xaml.cs`, implement `UpdateLiveTotalPreview()` and hook into mode change, sign toggle, adjustment text change.
   - Handle Escape as default acceptance (`DialogResult = true`), Close ('X') as cancel (`DialogResult = false`).
   - In `CalculationViewModel.cs` / `QuickAddControl.AddItem.cs`, create item in memory first without adding to `OrderItems`. Only add to `OrderItems` when dialog confirms or accepts default.
4. **R4**:
   - In `MainWindow.Items.cs`, add `bool discardDraft = true` to `StartNewOrder()`.
   - In `MainWindow.xaml.cs:190`, call `StartNewOrder(discardDraft: false)` on startup.
   - In `MainWindow.Recovery.cs`, add `bool silentIfNoDraft = false` to `CheckAndShowDraftRecovery()`, pass `true` in `InitializeRecoveryAndBackup()`.
   - In `ActionBarControl.xaml.cs:204`, discard draft in `BtnClearAll_Click`.
   - In `DraftRecoveryBanner.xaml`, replace `WarningBg` and `WarningBorder` with `SectionBg`, `Border`, `AccentLight`, `AccentText`.

## Execution Tracks
- **Worker 1 (Milestone 1 & 2)**:
  - Exclusive ownership: `MainWindow.xaml`, `MainWindow.xaml.cs` (R1 changes: overlay & NavPanel), `Themes/Brushes.xaml`, `Services/ThemeService.cs`, `Themes/InputStyles.RadioButton.xaml`, `Controls/OrdersHistoryControl.xaml`.
- **Worker 2 (Milestone 3)**:
  - Exclusive ownership: `Controls/InstallationChoiceDialog.xaml`, `Controls/InstallationChoiceDialog.xaml.cs`, `Controls/QuickAddControl.AddItem.cs`, `ViewModels/CalculationViewModel.cs`.
- **Worker 3 (Milestone 4)**:
  - Run after Worker 1. Exclusive ownership: `MainWindow.Items.cs`, `MainWindow.Recovery.cs`, `MainWindow.xaml.cs` (R4 startup parameter), `Controls/ActionBarControl.xaml.cs`, `Controls/DraftRecoveryBanner.xaml`.
- **Reviewers & Challengers & Auditor (Milestone 5)**:
  - 2 Reviewers independently verify code, layout, tokens, builds, and tests.
  - 2 Challengers test edge cases and verify acceptance criteria.
  - 1 Forensic Auditor verifies integrity and absence of cheats/dummy code.
  - Final Gate check.
