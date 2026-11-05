# Handoff Report — Survey 2: R3 Installation Choice Usability and QuickAdd Staging Guard

## 1. Observation

### 1.1 Current Implementation in `InstallationChoiceDialog`
- **File**: `MosquitoNetCalculator/Controls/InstallationChoiceDialog.xaml` (384 lines)
  - Lines 164–320: Three card buttons define the installation modes:
    - `BtnMode0`: "Монтаж включён" (Mode 0)
    - `BtnMode2`: "В конструкцию" (Mode 2)
    - `BtnMode1`: "Без монтажа" (Mode 1)
  - Lines 323–350: Manual adjustment controls:
    - `LblAdjustment` (`TextBlock`): label showing "Корректировка, ₽:" (or "₽/м.п.:")
    - `ChkSign` (`CheckBox` with `SignToggleCheckBox` style): toggles between positive `+` and negative `−`
    - `TxtAdjustment` (`TextBox`): input field for numeric adjustment value
  - Lines 353–380: Dialog footer:
    - `ChkRememberDefault` (`CheckBox`): "Запомнить выбор по умолчанию"
    - `BtnApply` (`Button` with `DialogPrimaryButton` style): "Применить"
  - **Missing Element**: There is **no** total price preview control (`TxtTotalPreview`) anywhere in `InstallationChoiceDialog.xaml`.

- **File**: `MosquitoNetCalculator/Controls/InstallationChoiceDialog.xaml.cs` (331 lines)
  - Lines 13–17: State fields:
    ```csharp
    private readonly OrderItem _item;
    private bool _userManuallyEditedAmount = false;
    public int SelectedMode { get; private set; } = 0;
    public double? CustomAmount { get; private set; }
    public bool RememberAsDefault => ChkRememberDefault?.IsChecked == true;
    ```
  - Lines 81–106: Sign and text change handlers:
    - `ChkSign.Click`: toggles text between `+` and `-`. Sets `_userManuallyEditedAmount = true;`.
    - `TxtAdjustment.TextChanged`: updates `ChkSign.IsChecked` if input starts with `-` or `+`.
  - Lines 156–181: `SelectMode(int mode)`:
    - Sets `SelectedMode = mode;`
    - Calls `UpdateSelectionVisual(mode);`
    - If `!_userManuallyEditedAmount`, sets `TxtAdjustment.Text` to mode defaults:
      - Mode 0: `OrderItem.GetDefaultInstallationAdjustment(_item.Name)`
      - Mode 1: `OrderItem.GetDefaultInstallationDeduction(_item.Name)`
      - Mode 2: `OrderItem.GetDefaultInstallationSurcharge(_item.Name)`
  - Lines 220–224: `BtnClose_Click`:
    ```csharp
    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        try { DialogResult = false; } catch (InvalidOperationException) { }
        Close();
    }
    ```
  - Lines 279–285: `PickMode(int mode)`:
    ```csharp
    private void PickMode(int mode)
    {
        SelectedMode = mode;
        CommitAdjustment();
        try { DialogResult = true; } catch (InvalidOperationException) { }
        Close();
    }
    ```
  - Lines 287–294: `Window_PreviewKeyDown`:
    ```csharp
    if (e.Key == Key.Escape)
    {
        BtnClose_Click(sender, e);
        e.Handled = true;
        return;
    }
    ```
    Currently, pressing Escape triggers `BtnClose_Click`, setting `DialogResult = false`.

### 1.2 Current Item Creation and Dialog Invocation in `QuickAddControl`
- **File**: `MosquitoNetCalculator/Controls/QuickAddControl.AddItem.cs` (580 lines)
  - Lines 398–448:
    ```csharp
    string effectiveType = (type == "Работа" && !string.IsNullOrWhiteSpace(TxtCustomName.Text))
        ? TxtCustomName.Text.Trim()
        : type;
    var item2 = mw.CalcVM.AddItem(effectiveType, color ?? string.Empty, (int)width, (int)height, qty, price, SelectedAnwisMode, isLabor: (type == "Работа"));
    if (item2 != null && type == "Работа") item2.IsLabor = true;
    if (item2 != null)
    {
        if (ProductCatalog.IsInstallationApplicable(type))
        {
            if (AppSettingsService.LoadAskInstallationModeOnAdd())
            {
                var dlg = new InstallationChoiceDialog(item2, item2.InstallationMode) { Owner = mw };
                if (dlg.ShowDialog() == true)
                {
                    item2.InstallationMode = dlg.SelectedMode;
                    if (dlg.CustomAmount.HasValue)
                    {
                        item2.SetCurrentInstallationAmount(dlg.CustomAmount.Value);
                    }
                    if (dlg.RememberAsDefault)
                    {
                        AppSettingsService.SaveDefaultInstallationMode(dlg.SelectedMode);
                        AppSettingsService.SaveAskInstallationModeOnAdd(false);
                        ToastService.ShowToast("Режим сохранён по умолчанию. Окно выбора отключено (можно включить в Настройках).", ToastType.Info);
                    }
                }
            }
            ...
        }
        ...
        item2.RecalculateRequested += mw.RecalculateAndUpdateTotal;
        mw.RecalculateAndUpdateTotal();
        mw.MarkDirty();
        StoreLastAdded();
    }
    ```
- **File**: `MosquitoNetCalculator/ViewModels/CalculationViewModel.cs`
  - Lines 60–120: `public OrderItem? AddItem(string type, string color, int width, int height, double qty, double price, Models.AnwisSizeMode anwisMode = Models.AnwisSizeMode.Брусбокс60, bool isLabor = false)`:
    - Line 118: `OrderItems.Add(item);`
    - Directly adds the item to `OrderItems` **before** `QuickAddItem` shows `InstallationChoiceDialog`.
    - Because `OrderItems` is an `ObservableCollection<OrderItem>` bound to the DataGrid, the item is immediately displayed in the grid before the user ever touches `InstallationChoiceDialog`.
    - If the user clicks `BtnClose` ('X') or presses Escape, `dlg.ShowDialog()` returns `false`, but the item is **never removed** from `OrderItems`. It remains in the order with its unconfirmed default state.

### 1.3 OrderItem Price Calculation Contract
- **File**: `MosquitoNetCalculator/Models/OrderItem.Installation.cs`
  - Lines 367–382:
    ```csharp
    public double TotalWithDeduction
    {
        get
        {
            if (!IsInstallationApplicable) return Total;
            double factor = InstallationLinearMeters * Quantity;
            return _installationMode switch
            {
                0 => Math.Round(Math.Max(0, Total + InstallationAdjustment * factor), 2),
                1 => Math.Round(Math.Max(0, Total + InstallationDeduction * factor), 2),
                2 => Math.Round(Math.Max(0, Total + InstallationSurcharge * factor), 2),
                _ => Total
            };
        }
    }
    ```
  - For all modes, effective row total is `Total + [adjustment] * factor`, where:
    - `Total` is the base line-item price (already calculated upon setting width/height/qty/price).
    - `factor = InstallationLinearMeters * Quantity`.
    - `adjustment` is the signed per-unit fee in `TxtAdjustment`.

### 1.4 Test Baseline
- Command: `dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj --filter "FullyQualifiedName~InstallationChoiceDialog|FullyQualifiedName~StabilityAndSafetyFeaturesTests|FullyQualifiedName~QuickAddControlTests|FullyQualifiedName~DesignTokenGuardTests"`
- Result: **101 passed, 0 failed, 0 skipped** (5 s duration).

---

## 2. Logic Chain

### 2.1 Live Total Preview (`TxtTotalPreview`)
1. **Placement**: In `InstallationChoiceDialog.xaml`, inside the main body `StackPanel` (`Grid.Row="1"`), directly below the adjustment row (`Grid Margin="{DynamicResource Gap.BlockTopLg}"`).
   - XAML structure:
     ```xaml
     <!-- Предпросмотр итоговой стоимости позиции -->
     <Grid Margin="{DynamicResource Gap.BlockTopLg}">
         <Grid.ColumnDefinitions>
             <ColumnDefinition Width="*"/>
             <ColumnDefinition Width="Auto"/>
         </Grid.ColumnDefinitions>
         <TextBlock Grid.Column="0"
                    Text="Итоговая сумма позиции:"
                    FontSize="{StaticResource Type.Body}"
                    FontWeight="{StaticResource Weight.Medium}"
                    Foreground="{DynamicResource TextSecondary}"
                    VerticalAlignment="Center"/>
         <TextBlock Grid.Column="1"
                    x:Name="TxtTotalPreview"
                    FontSize="{StaticResource Type.BodyLg}"
                    FontWeight="{StaticResource Weight.SemiBold}"
                    Foreground="{DynamicResource AccentText}"
                    VerticalAlignment="Center"/>
     </Grid>
     ```
   - All styling tokens (`Gap.BlockTopLg`, `Type.Body`, `Type.BodyLg`, `Weight.Medium`, `Weight.SemiBold`, `TextSecondary`, `AccentText`) exist in `Tokens.Spacing.xaml`, `Tokens.Typography.xaml`, and `Brushes.xaml`, adhering strictly to `DesignTokenGuardTests` ratchet rules.
2. **Formula & Parsing**:
   - Parse the current user-entered adjustment from `TxtAdjustment` and `ChkSign`:
     ```csharp
     private double ParseCurrentAdjustment()
     {
         if (TxtAdjustment == null) return 0;
         string rawText = (TxtAdjustment.Text ?? "").Trim();
         if (string.IsNullOrWhiteSpace(rawText)) return 0;

         bool hasExplicitMinus = rawText.StartsWith("-") || rawText.StartsWith("−") || ChkSign?.IsChecked == false;
         string cleanText = rawText.TrimStart('+', '-', '−', ' ').Trim();

         if (MoneyFormatService.TryParse(cleanText, out double numVal) && double.IsFinite(numVal))
         {
             return hasExplicitMinus ? -Math.Abs(numVal) : Math.Abs(numVal);
         }
         return 0;
     }
     ```
   - Calculate preview amount:
     ```csharp
     private void UpdateTotalPreview()
     {
         if (TxtTotalPreview == null) return;
         if (_item == null)
         {
             TxtTotalPreview.Text = "—";
             return;
         }

         if (!_item.IsInstallationApplicable)
         {
             TxtTotalPreview.Text = $"{MoneyFormatService.Format(_item.Total)} ₽";
             return;
         }

         double factor = _item.InstallationLinearMeters * _item.Quantity;
         double adj = ParseCurrentAdjustment();
         double previewTotal = Math.Round(Math.Max(0, _item.Total + adj * factor), 2);
         TxtTotalPreview.Text = $"{MoneyFormatService.Format(previewTotal)} ₽";
     }
     ```
3. **Event Wiring**:
   - Call `UpdateTotalPreview()`:
     - In constructor (initial preview display).
     - At the end of `SelectMode(int mode)`.
     - In `ChkSign.Click` event handler.
     - In `TxtAdjustment.TextChanged` event handler.

### 2.2 QuickAdd In-Memory Staging Guard & Escape Semantics
1. **Problem with Current Flow**:
   - `mw.CalcVM.AddItem(...)` immediately inserts the item into `OrderItems` at line 118 of `CalculationViewModel.cs`.
   - If `InstallationChoiceDialog` is dismissed with cancel ('X') or aborted, the item has already been added to the table.
2. **Architecture Solution**:
   - Decompose `CalculationViewModel.AddItem` into:
     - `public OrderItem? CreateItem(...)`: constructs and initializes `OrderItem` in memory (including calculated Anwis dimensions, default rates, and modes) without calling `OrderItems.Add(item)`.
     - `public void AddItem(OrderItem item)`: assigns `item.RowNumber = OrderItems.Count + 1` and calls `OrderItems.Add(item)`.
     - `public OrderItem? AddItem(...)` overload: calls `CreateItem(...)` and then `AddItem(...)` to preserve 100% backward compatibility for all existing callers and unit tests.
3. **Staging Flow in `QuickAddItem`**:
   - Call `var stagedItem = mw.CalcVM.CreateItem(...)`.
   - If null, return.
   - Configure non-installation flags (e.g. `IsAnticat`, default catalog price).
   - If `ProductCatalog.IsInstallationApplicable(type)`:
     - If `AppSettingsService.LoadAskInstallationModeOnAdd()` is true:
       - Show `InstallationChoiceDialog(stagedItem, stagedItem.InstallationMode)`.
       - Check `dialogResult`:
         - If user cancelled ('X' button or closed without confirmation): **Do NOT add** `stagedItem`. Return immediately.
         - If confirmed ("Применить" / Enter): apply `dlg.SelectedMode` and `dlg.CustomAmount`.
         - If default accepted on Escape: apply `dlg.SelectedMode` (default mode).
     - If `AppSettingsService.LoadAskInstallationModeOnAdd()` is false: apply default mode directly.
   - Only after confirmation/default acceptance, call `mw.CalcVM.AddItem(stagedItem);`.
   - Wire `stagedItem.RecalculateRequested += mw.RecalculateAndUpdateTotal;`, call `mw.RecalculateAndUpdateTotal()`, `mw.MarkDirty()`, `StoreLastAdded()`, and animate success.
4. **Escape vs. Cancel Semantics in `InstallationChoiceDialog`**:
   - Requirement specifies: "adding the item to OrderItems only after dialog confirmation or default acceptance on Escape" and "Items requiring installation choice are not added to the CalculationView table until the dialog is accepted or dismissed with default."
   - **Clicking 'X' (BtnClose)**: explicit cancel action. Sets `IsCancelled = true`, `DialogResult = false`. Staged item is discarded.
   - **Pressing Escape**: "dismissed with default". The user accepts the default installation mode without custom tweaking. Sets `DefaultAcceptedOnEscape = true`, keeps default `SelectedMode`, sets `DialogResult = true`, closes dialog. Staged item is added with default installation option.

---

## 3. Caveats
1. **Single-Item Staging**: Only items added via `QuickAddControl` use the interactive `InstallationChoiceDialog`. Custom products ("Свой товар") do not support installation (`ProductCatalog.IsInstallationApplicable` returns false).
2. **Template Window & Batch Adds**: `TemplatesWindow.Apply.cs` adds items programmatically without showing the UI dialog. Refactoring `CalculationViewModel` into `CreateItem` + `AddItem(OrderItem)` preserves existing behavior for templates.
3. **Culture / Format**: `MoneyFormatService.Format` uses Russian number formatting (`1 800,00`). The unit suffix is `" ₽"`, matching other dialogs in the application.

---

## 4. Conclusion
The implementation plan for R3 is fully mapped:
1. `InstallationChoiceDialog.xaml`: Add `TxtTotalPreview` inside the body StackPanel below the adjustment row, styled with design tokens.
2. `InstallationChoiceDialog.xaml.cs`: Wire `UpdateTotalPreview()` to mode selection, adjustment text edits, and sign clicks. Handle Escape as accepting the default installation option (`DialogResult = true`), while 'X' button cancels (`DialogResult = false`).
3. `CalculationViewModel.cs`: Expose `CreateItem(...)` and `AddItem(OrderItem item)` alongside the existing `AddItem(...)` overload.
4. `QuickAddControl.AddItem.cs`: Stage `stagedItem` in memory using `CreateItem(...)`. Add to `OrderItems` only upon confirmation or default acceptance on Escape. Discard on dialog cancellation.
5. Unit tests: Add tests in `InstallationChoiceDialogTests.cs` and `QuickAddControlTests.cs` verifying live preview updates, staging protection on cancel, and default acceptance on Escape.

---

## 5. Verification Method

### 5.1 Compilation and Test Execution
1. **Release Build**:
   ```pwsh
   dotnet build MosquitoNetCalculator/MosquitoNetCalculator.csproj -c Release
   ```
2. **Targeted Test Filter**:
   ```pwsh
   dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj --filter "FullyQualifiedName~InstallationChoiceDialog|FullyQualifiedName~StabilityAndSafetyFeaturesTests|FullyQualifiedName~QuickAddControlTests|FullyQualifiedName~DesignTokenGuardTests"
   ```
   *Expected: All tests pass without regression.*
3. **Design Token Validation**:
   ```pwsh
   dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj --filter "FullyQualifiedName~DesignTokenGuardTests"
   ```
   *Expected: 0 design token violations.*
4. **Documentation Validation Script**:
   ```pwsh
   powershell -ExecutionPolicy Bypass -File agents/scripts/validate-docs.ps1
   ```
   *Expected: 0 issues reported.*

### 5.2 Invalidation Conditions
- If `TxtTotalPreview` does not update when user toggles `ChkSign` or types in `TxtAdjustment`.
- If an item is added to `OrderItems` before `InstallationChoiceDialog` is confirmed.
- If clicking the 'X' button adds the item to the order.
- If pressing Escape fails to add the item with default installation.
