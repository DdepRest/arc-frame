# Handoff Report — Milestone 3 (R3: Installation Choice Usability and QuickAdd Staging Guard)

## 1. Observation
- **Scope & Ownership**:
  - `MosquitoNetCalculator/Controls/InstallationChoiceDialog.xaml`
  - `MosquitoNetCalculator/Controls/InstallationChoiceDialog.xaml.cs`
  - `MosquitoNetCalculator/Controls/QuickAddControl.AddItem.cs`
  - `MosquitoNetCalculator/ViewModels/CalculationViewModel.cs`

- **Verbatim Code & Implementations Verified**:
  - **Live Preview Element in XAML** (`MosquitoNetCalculator/Controls/InstallationChoiceDialog.xaml` lines 351–369):
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
  - **Live Preview Calculation & Event Hooks** (`MosquitoNetCalculator/Controls/InstallationChoiceDialog.xaml.cs` lines 80–110, 160–187, 281–297):
    - `UpdateTotalPreview()` computes:
      ```csharp
      double factor = (_item.InstallationLinearMeters > 0)
          ? (_item.InstallationLinearMeters * (_item.Quantity > 0 ? _item.Quantity : 1.0))
          : 1.0;
      double adjustment = ParseCurrentAdjustment();
      double preview = Math.Round(Math.Max(0, _item.Total + adjustment * factor), 2);
      TxtTotalPreview.Text = $"{MoneyFormatService.Format(preview)} ₽";
      ```
    - Hooked to `SelectMode(mode)` (line 186), `ChkSign.Click` (line 90), `TxtAdjustment.TextChanged` (line 108), and dialog constructor (line 136).
  - **Escape vs Close Semantics** (`MosquitoNetCalculator/Controls/InstallationChoiceDialog.xaml.cs` lines 226–230, 320–328):
    - `BtnClose_Click` sets `DialogResult = false; Close();` (cancels).
    - `Window_PreviewKeyDown` on `Key.Escape` sets `DefaultAcceptedOnEscape = true; PickMode(SelectedMode);` which commits adjustment with `DialogResult = true; Close();` (accepts default).
  - **In-Memory Staging Guard & Item Separation** (`MosquitoNetCalculator/ViewModels/CalculationViewModel.cs` lines 59–146 and `MosquitoNetCalculator/Controls/QuickAddControl.AddItem.cs` lines 401–450):
    - `CalculationViewModel` exposes `public OrderItem? CreateItem(...)` that initializes the item in memory without adding it to `OrderItems`, `public void AddItem(OrderItem item)` that stamps `item.RowNumber` and calls `OrderItems.Add(item)`, and the overloaded `AddItem(...)` for backward compatibility.
    - `QuickAddControl.AddItem.cs` calls `var item2 = mw.CalcVM.CreateItem(...)`. If installation is applicable and `AskInstallationModeOnAdd` is enabled, it shows `InstallationChoiceDialog`.
    - If `dlg.ShowDialog() != true` (e.g. cancelled via 'X'), it returns immediately without modifying `OrderItems`.
    - If confirmed or default-accepted on Escape (`ShowDialog() == true`), it updates mode and custom amount, and only then calls `mw.CalcVM.AddItem(item2)`.

- **Build and Test Observations**:
  - `dotnet build MosquitoNetCalculator/MosquitoNetCalculator.csproj -c Release`: 0 warnings, 0 errors, Exit code 0.
  - `dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj --filter "FullyQualifiedName~InstallationChoiceDialog|FullyQualifiedName~QuickAddControlTests"`: Passed: 77, Failed: 0, Skipped: 0, Duration 1 s.
  - `dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj --filter "FullyQualifiedName~InstallationChoiceDialog|FullyQualifiedName~StabilityAndSafetyFeaturesTests|FullyQualifiedName~QuickAddControlTests|FullyQualifiedName~DesignTokenGuardTests"`: Passed: 101, Failed: 0, Skipped: 0, Duration 4 s.
  - `powershell -ExecutionPolicy Bypass -File agents/scripts/validate-docs.ps1`: ALL CHECKS PASSED, Issues: 0, Warnings: 0.

## 2. Logic Chain
1. Adding `TxtTotalPreview` to `InstallationChoiceDialog.xaml` with semantic tokens (`Type.BodyLg`, `Weight.SemiBold`, `AccentText`) guarantees token compliance with `DesignTokenGuardTests` and visual consistency with the application design system.
2. The formula in `UpdateTotalPreview()` accounts for both piece-based nets (`InstallationLinearMeters` defaults to 1.0) and linear-meter items (`Отлив`, `Козырёк`, `Уплотнение`), multiplied by `Quantity`, matching `OrderItem.TotalWithDeduction`.
3. Wiring `UpdateTotalPreview()` into mode switches, sign toggling, and text input guarantees real-time visual feedback for every user interaction.
4. Distinguishing between the 'X' button (`DialogResult = false`) and the Escape key (`DialogResult = true` accepting default mode) satisfies the user requirement that pressing Escape accepts default parameters while clicking close cancels the addition.
5. Decomposing `CalculationViewModel.AddItem` into `CreateItem` and `AddItem(OrderItem)` allows `QuickAddControl` to hold the candidate row in memory while the dialog is open. Cancelling discards the staged instance cleanly without polluting `OrderItems`, avoiding spurious collection mutations, visual flickers in the DataGrid, and dirty state triggers.

## 3. Caveats
- `InstallationLinearMeters` handles both single piece and cut-to-length items accurately according to product definitions in `OrderItem.Installation.cs`.
- If `AskInstallationModeOnAdd` is disabled in application settings, `QuickAddControl` automatically assigns the configured default installation mode without showing the dialog, then appends the staged item to `OrderItems`.
- No files outside the exclusive ownership list were altered.

## 4. Conclusion
All requirements for Milestone 3 (R3: Installation Choice Usability and QuickAdd Staging Guard) are fully implemented and verified. The solution is genuine, adheres to design tokens, preserves backward compatibility, and passes all targeted and safety tests.

## 5. Verification Method
- **Release Build**:
  ```pwsh
  dotnet build MosquitoNetCalculator/MosquitoNetCalculator.csproj -c Release
  ```
- **Targeted Unit Tests**:
  ```pwsh
  dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj --filter "FullyQualifiedName~InstallationChoiceDialog|FullyQualifiedName~QuickAddControlTests"
  ```
- **Full Acceptance & Safety Filter**:
  ```pwsh
  dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj --filter "FullyQualifiedName~InstallationChoiceDialog|FullyQualifiedName~StabilityAndSafetyFeaturesTests|FullyQualifiedName~QuickAddControlTests|FullyQualifiedName~DesignTokenGuardTests"
  ```
- **Doc Validation**:
  ```pwsh
  powershell -ExecutionPolicy Bypass -File agents/scripts/validate-docs.ps1
  ```
- **Invalidation Conditions**:
  - `TxtTotalPreview` missing or failing to recalculate when toggling sign or typing adjustment.
  - Pressing Escape setting `DialogResult = false` instead of accepting default.
  - Closing dialog with 'X' leaving or adding staged item to `OrderItems`.
