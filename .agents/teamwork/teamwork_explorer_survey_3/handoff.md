# Handoff Report: R4 Crash Recovery & Test Suite Investigation

## 1. Observation

### 1.1 R4 Crash Recovery: Root Cause of Draft Deletion on Startup
- **Constructor invocation**: In `MosquitoNetCalculator/MainWindow.xaml.cs` lines 189–192:
  ```csharp
  189:             LoadPrices();
  190:             StartNewOrder();
  191:             RefreshComboBoxColumns();
  ```
- **Draft discarding inside `StartNewOrder`**: In `MosquitoNetCalculator/MainWindow.Items.cs` lines 41–74:
  ```csharp
  41:         internal void StartNewOrder()
  42:         {
  43:             ViewModel.UndoRedo.SuppressDirtyChanges(() =>
  ...
  68:             });
  69:             MarkClean();
  70:             DraftService.DiscardDraft();
  71:             DraftBanner?.HideBanner();
  72:             ViewModel.UndoRedo.Clear();
  73:             UpdateUndoRedoHint();
  74:         }
  ```
  On application launch, `MainWindow()` executes `StartNewOrder()`, which calls `DraftService.DiscardDraft()`. This unconditionally deletes `draft.json` from `%AppData%/MosquitoNetCalculator/draft.json` before `Window_Loaded` runs.

### 1.2 R4 Crash Recovery: Unwanted Startup Toast Notification
- **Recovery initialization on Loaded**: In `MosquitoNetCalculator/MainWindow.xaml.cs` lines 428–437:
  ```csharp
  428:         private void MainWindow_Loaded(object sender, RoutedEventArgs e)
  429:         {
  ...
  436:             InitializeRecoveryAndBackup();
  437: 
  ```
- **Unconditional toast when draft is absent**: In `MosquitoNetCalculator/MainWindow.Recovery.cs` lines 44–64:
  ```csharp
  44:             // Check if there is an unsaved pending draft from a previous session
  45:             CheckAndShowDraftRecovery();
  ...
  48:         internal void CheckAndShowDraftRecovery()
  49:         {
  50:             if (DraftBanner == null) return;
  51: 
  52:             if (DraftService.HasPendingDraft())
  53:             {
  54:                 var draft = DraftService.LoadDraft();
  55:                 if (draft != null)
  56:                 {
  57:                     DraftBanner.DisplayDraft(draft);
  58:                 }
  59:             }
  60:             else
  61:             {
  62:                 ToastService.ShowToast("Несохранённых черновиков нет.", ToastType.Info);
  63:             }
  64:         }
  ```
  Because `StartNewOrder()` wiped `draft.json`, `HasPendingDraft()` is always `false` on startup, firing `ToastService.ShowToast("Несохранённых черновиков нет.", ToastType.Info)` every time the app opens.
- **User-triggered check**: In `MosquitoNetCalculator/MainWindow.xaml.cs` lines 691–694:
  ```csharp
  691:         private void AutosaveStatus_Click(object sender, MouseButtonEventArgs e)
  692:         {
  693:             CheckAndShowDraftRecovery();
  694:         }
  ```
  Clicking the autosave status bar item is an explicit user action and should retain the toast feedback.

### 1.3 R4 Crash Recovery: Draft Discarding on Explicit User Actions
- **"New Order" action**: In `MosquitoNetCalculator/Controls/ActionBarControl.xaml.cs` lines 158–169:
  ```csharp
  158:         private void BtnNewOrder_Click(object sender, RoutedEventArgs e)
  159:         {
  ...
  167:             mw.StartNewOrder();
  168:             mw.UpdateEmptyState();
  169:         }
  ```
  When user explicitly clicks `BtnNewOrder`, calling `StartNewOrder()` (with default `discardDraft = true`) discards the draft as required.
- **"Clear All" action**: In `MosquitoNetCalculator/Controls/ActionBarControl.xaml.cs` lines 204–225:
  ```csharp
  204:         private void BtnClearAll_Click(object sender, RoutedEventArgs e)
  205:         {
  206:             if (!TryGetMainWindow(nameof(BtnClearAll_Click), out var mw)) return;
  207:             if (mw.OrderItems.Count == 0) return;
  208: 
  209:             if (DialogService.ShowConfirmDestructive("Очистить все позиции расчёта?", "Очистить", "Очистить всё", mw))
  210:             {
  211:                 mw.PushUndo();
  212:                 mw.CalcVM.UnsubscribeAll(mw.UpdateTotal);
  213:                 mw.CalcVM.ClearAll();
  214:                 mw.UpdateTotal();
  215:                 mw.UpdateEmptyState();
  216: 
  217:                 // v3.50: undo toast ...
  ```
  `BtnClearAll_Click` currently does NOT discard the draft or hide the banner. Per requirement R4 ("Only discard drafts upon explicit user reset actions ('New Order' / 'Clear All')"), `mw.DraftService.DiscardDraft()` and `mw.DraftBanner?.HideBanner()` must be invoked here.

### 1.4 R4 Crash Recovery: DraftRecoveryBanner Design Tokens
- In `MosquitoNetCalculator/Controls/DraftRecoveryBanner.xaml` lines 12–37:
  ```xaml
  12:     <Border x:Name="BannerBorder"
  13:             Background="{DynamicResource WarningBg}"
  14:             BorderBrush="{DynamicResource Border}"
  15:             BorderThickness="0,0,0,1"
  16:             Padding="{DynamicResource Pad.Control}">
  ...
  26:                 <Border Width="24" Height="24" CornerRadius="{DynamicResource Radius.Sm}"
  27:                         Background="{DynamicResource WarningBorder}"
  28:                         VerticalAlignment="Center" Margin="{DynamicResource Gap.Inline}">
  29:                     <TextBlock Text="&#xE81C;" FontFamily="{DynamicResource Font.Icon}"
  30:                                FontSize="{StaticResource Type.Caption}" Foreground="{DynamicResource TextPrimary}"
  31:                                HorizontalAlignment="Center" VerticalAlignment="Center"/>
  32:                 </Border>
  ```
- Neither `WarningBg` nor `WarningBorder` exist in `MosquitoNetCalculator/Themes/Brushes.xaml`. At runtime, they evaluate to empty/transparent brushes.
- In `Themes/Brushes.xaml`, the valid tokens exist:
  - `SectionBg` (#F8F9FB) at line 25
  - `Border` (#D9D9D9) at line 32
  - `AccentLight` (#EBF3FD) at line 12
  - `AccentText` (#3574C3) at line 13

### 1.5 Verification Resources and Existing Tests
- **Build Status**: Command `dotnet build MosquitoNetCalculator/MosquitoNetCalculator.csproj -c Release` completed with 0 errors and 0 warnings.
- **Documentation Validation**: Command `powershell -ExecutionPolicy Bypass -File agents/scripts/validate-docs.ps1` completed with code 0 (12 checks passed, 0 issues, 0 warnings).
- **Targeted Test Execution**: Command:
  `dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj --filter "FullyQualifiedName~InstallationChoiceDialog|FullyQualifiedName~StabilityAndSafetyFeaturesTests|FullyQualifiedName~QuickAddControlTests|FullyQualifiedName~DesignTokenGuardTests"`
  Result: 101 tests passed, 0 failed, 0 skipped.
- **Test Inventory**:
  1. `InstallationChoiceDialogTests.cs`: 8 tests covering mode selection, dimension descriptions, labor deductions/rates, custom amounts, and numeric hotkey handling. (R3.1 will add live total preview assertions for `TxtTotalPreview`).
  2. `StabilityAndSafetyFeaturesTests.cs`: 5 tests covering `DraftRecoveryService` atomic saving/loading, soft delete/restore in `OrderStorageService`, backup archive creation, draft saved event firing, and DTO roundtrip conversion.
  3. `QuickAddControlTests.cs`: 17 tests covering anticat toggle button visibility and state, thousands-separated price parsing, price resolution fallback to catalog, and required field validation matrix.
  4. `DesignTokenGuardTests.cs`: 19 tests enforcing design system tokens (font size, corner radius, margin/padding scale, hex color constraints, token definitions, and Win10 font fallbacks).

---

## 2. Logic Chain

1. **Premise 1**: On startup, `MainWindow()` executes `StartNewOrder()` at line 190.
2. **Premise 2**: `StartNewOrder()` unconditionally calls `DraftService.DiscardDraft()` at line 70 of `MainWindow.Items.cs`, which deletes `draft.json`.
3. **Deduction 1**: Any draft left by an unexpected crash or previous session is destroyed during window construction before recovery has a chance to check it.
4. **Resolution 1**: Add an optional parameter `StartNewOrder(bool discardDraft = true)`. On startup in `MainWindow.xaml.cs`, call `StartNewOrder(discardDraft: false)`. This preserves `draft.json` across launches.
5. **Premise 3**: In `MainWindow_Loaded`, `InitializeRecoveryAndBackup()` calls `CheckAndShowDraftRecovery()`. Line 62 unconditionally fires a toast if `HasPendingDraft()` is false.
6. **Deduction 2**: Silent application launch alerts the user unnecessarily about the absence of drafts.
7. **Resolution 2**: Add `CheckAndShowDraftRecovery(bool silentIfNoDraft = false)`. In `InitializeRecoveryAndBackup()`, pass `silentIfNoDraft: true`. In `AutosaveStatus_Click()`, keep `silentIfNoDraft: false` so explicit user clicks still display `"Несохранённых черновиков нет."`.
8. **Premise 4**: User reset actions consist of "New Order" and "Clear All". `BtnNewOrder_Click` already calls `StartNewOrder()`, which with `discardDraft = true` clears the draft. `BtnClearAll_Click` currently only clears `CalcVM.ClearAll()`.
9. **Resolution 3**: Add `mw.DraftService.DiscardDraft();` and `mw.DraftBanner?.HideBanner();` to `BtnClearAll_Click` after user confirmation.
10. **Premise 5**: `DraftRecoveryBanner.xaml` references nonexistent tokens `WarningBg` and `WarningBorder`.
11. **Resolution 4**: In `DraftRecoveryBanner.xaml`:
    - `BannerBorder.Background` -> `{DynamicResource SectionBg}`
    - `BannerBorder.BorderBrush` -> `{DynamicResource Border}`
    - Icon container `Border.Background` -> `{DynamicResource AccentLight}`
    - Icon glyph `TextBlock.Foreground` -> `{DynamicResource AccentText}`
12. **Deduction 3**: These changes satisfy all clauses of Requirement R4 without breaking existing behavior, regressions, or design token guards.

---

## 3. Caveats

1. **Multiple instances**: If two instances of the application are launched concurrently, both read the single `%AppData%/MosquitoNetCalculator/draft.json`. Atomic writes via `.tmp` file move prevent file corruption, but last-write wins on simultaneous autosaves.
2. **Debounce interval**: `DraftRecoveryService.DebounceMs` is set to 800 ms. An unhandled OS kill or power cut within 800 ms of the last keystroke may miss the most recent edit.
3. **Test STA requirement**: WPF UI controls (`InstallationChoiceDialog`, `DraftRecoveryBanner`) must be tested on STA threads via `WpfTestHelper.RunOnSta`.

---

## 4. Conclusion

The implementation path for Requirement R4 and verification is straightforward and localized:
1. **`MainWindow.Items.cs`**:
   - Change line 41 signature to `internal void StartNewOrder(bool discardDraft = true)`
   - Wrap lines 70–71 with `if (discardDraft) { DraftService.DiscardDraft(); DraftBanner?.HideBanner(); }`
2. **`MainWindow.xaml.cs`**:
   - Line 190: Call `StartNewOrder(discardDraft: false);`
3. **`Controls/ActionBarControl.xaml.cs`**:
   - Lines 215–216 (`BtnClearAll_Click`): Add `mw.DraftService.DiscardDraft(); mw.DraftBanner?.HideBanner();`
4. **`MainWindow.Recovery.cs`**:
   - Line 45: Call `CheckAndShowDraftRecovery(silentIfNoDraft: true);`
   - Line 48: Change signature to `internal void CheckAndShowDraftRecovery(bool silentIfNoDraft = false)`
   - Line 60: Wrap toast in `if (!silentIfNoDraft) { ToastService.ShowToast("Несохранённых черновиков нет.", ToastType.Info); }`
5. **`Controls/DraftRecoveryBanner.xaml`**:
   - Line 13: `Background="{DynamicResource SectionBg}"`
   - Line 27: `Background="{DynamicResource AccentLight}"`
   - Line 30: `Foreground="{DynamicResource AccentText}"`
6. **Tests to add/update**:
   - In `StabilityAndSafetyFeaturesTests.cs`, add tests verifying:
     - `StartNewOrder(discardDraft: false)` does not delete existing draft file.
     - `StartNewOrder(discardDraft: true)` deletes existing draft file.
     - `CheckAndShowDraftRecovery(silentIfNoDraft: true)` does not emit toast when draft is missing.

---

## 5. Verification Method

To independently verify the investigation findings and subsequent implementation:

1. **Release Build**:
   ```pwsh
   dotnet build MosquitoNetCalculator/MosquitoNetCalculator.csproj -c Release
   ```
   *Expected result*: Build succeeds with 0 errors and 0 warnings.

2. **Targeted Unit & Regression Tests**:
   ```pwsh
   dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj --filter "FullyQualifiedName~InstallationChoiceDialog|FullyQualifiedName~StabilityAndSafetyFeaturesTests|FullyQualifiedName~QuickAddControlTests|FullyQualifiedName~DesignTokenGuardTests"
   ```
   *Expected result*: All 101+ tests pass cleanly.

3. **Design System Token Audit**:
   `DesignTokenGuardTests.EveryTokenReferencedInXaml_IsDefined` and `HardcodedHexColors_LiveOnlyInTheColorLayer` will pass, verifying `SectionBg`, `Border`, `AccentLight`, and `AccentText`.

4. **Documentation & Architecture Validation**:
   ```pwsh
   powershell -ExecutionPolicy Bypass -File agents/scripts/validate-docs.ps1
   ```
   *Expected result*: `RESULT: ALL CHECKS PASSED (Issues: 0, Warnings: 0)`.
