# Handoff Report: Codebase Survey for Requirements R1 and R2

## 1. Observation

### 1.1 DraftRecoveryBanner Findings
- **File Locations**:
  - `MosquitoNetCalculator/Controls/DraftRecoveryBanner.xaml`
  - `MosquitoNetCalculator/Controls/DraftRecoveryBanner.xaml.cs`
  - Instantiation in `MosquitoNetCalculator/MainWindow.xaml`:
    ```xaml
    177:         <controls:DraftRecoveryBanner x:Name="DraftBanner"
    178:                                       Grid.Row="2"/>
    ```
  - Wiring in `MosquitoNetCalculator/MainWindow.Recovery.cs`:
    ```csharp
    21:             if (DraftBanner != null)
    22:             {
    23:                 DraftBanner.RestoreRequested += (_, _) => RestorePendingDraft();
    24:                 DraftBanner.SelectiveRestoreRequested += (_, _) => RestoreSelectiveDraft();
    25:                 DraftBanner.PostponeRequested += (_, _) => PostponePendingDraft();
    26:                 DraftBanner.DiscardRequested += (_, _) => DiscardPendingDraft();
    27:             }
    ```
- **Current Visual Layout & Structure (`DraftRecoveryBanner.xaml`)**:
  - Line 12–16: Outer border:
    ```xaml
    <Border x:Name="BannerBorder"
            Background="{DynamicResource SectionBg}"
            BorderBrush="{DynamicResource Border}"
            BorderThickness="0,0,0,1"
            Padding="{DynamicResource Pad.Control}">
    ```
  - Line 25–37: Left StackPanel:
    ```xaml
    <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center">
        <Border Width="24" Height="24" CornerRadius="{DynamicResource Radius.Sm}"
                Background="{DynamicResource AccentLight}"
                VerticalAlignment="Center" Margin="{DynamicResource Gap.Inline}">
            <TextBlock Text="&#xE81C;" FontFamily="{DynamicResource Font.Icon}"
                       FontSize="{StaticResource Type.Caption}" Foreground="{DynamicResource AccentText}"
                       HorizontalAlignment="Center" VerticalAlignment="Center"/>
        </Border>
        <TextBlock Text="Восстановление документа:"
                   FontWeight="SemiBold" FontSize="{StaticResource Type.Body}"
                   Foreground="{DynamicResource TextPrimary}"
                   VerticalAlignment="Center" Margin="{DynamicResource Gap.Inline}"/>
    </StackPanel>
    ```
  - Line 40–45: Middle description:
    ```xaml
    <TextBlock x:Name="TxtDraftInfo" Grid.Column="1"
               Text="Найден несохранённый черновик"
               FontSize="{StaticResource Type.Body}"
               Foreground="{DynamicResource TextPrimary}"
               TextTrimming="CharacterEllipsis"
               VerticalAlignment="Center" Margin="{DynamicResource Gap.InlineLg}"/>
    ```
  - Line 32 in `DraftRecoveryBanner.xaml.cs`:
    ```csharp
    TxtDraftInfo.Text = $"Найден несохранённый черновик от {time} (позиций: {count}, клиент: {client})";
    ```
- **Button Definitions & Critical Theme Bug (`DraftRecoveryBanner.xaml`)**:
  - Line 50–60: `BtnRestore` uses `Style="{DynamicResource PrimaryButton}"` (Accent button).
  - Line 63–73: `BtnSelective` uses `Style="{DynamicResource SecondaryButton}"`.
    - **Defect Observation**: Grep across the entire project for `SecondaryButton` returned only this single occurrence in `DraftRecoveryBanner.xaml`:
      `{"File":"c:\\Users\\Asus\\Desktop\\A.R.C. Frame\\gwga\\MosquitoNetCalculator\\Controls\\DraftRecoveryBanner.xaml","LineNumber":64,"LineContent":" Style=\"{DynamicResource SecondaryButton}\""}`
      `SecondaryButton` does NOT exist in `Themes/ButtonStyles.xaml` or any other resource dictionary! At runtime, WPF fails silently and renders default button chrome.
    - In `Themes/ButtonStyles.xaml` line 107–123, the canonical Fluent secondary/outline button style is `GhostButton`:
      `<!-- Ghost — Fluent secondary, neutral -->`
      `<Style x:Key="GhostButton" TargetType="Button" BasedOn="{StaticResource ActionButtonBase}">`
  - Line 76–83: `BtnPostpone` (`[ Отложить ]`):
    - Redundant standalone button marked for removal in R1.
  - Line 86–92: `BtnDiscard` (`[ Удалить черновик ]`):
    - Uses `Style="{DynamicResource GhostButton}"`.
    - Currently lacks any icon (only `<TextBlock Text="Удалить черновик" Foreground="{DynamicResource TextMuted}"/>`).
    - Standard trash glyph in Fluent Icon font used across project (`OrdersHistoryControl.xaml:67`, `AiAssistantControl.xaml:1040`) is `&#xE74D;`.
  - Line 95–101: `BtnClose` (`[ ✕ ]`):
    - Uses `Style="{DynamicResource DialogCloseButton}"` with glyph `&#xE8BB;`.
    - In `DraftRecoveryBanner.xaml.cs`:
      ```csharp
      66:         private void BtnClose_Click(object sender, RoutedEventArgs e)
      67:         {
      68:             BtnPostpone_Click(sender, e);
      69:         }
      ```
      `BtnPostpone_Click` calls `HideBanner(); PostponeRequested?.Invoke(this, EventArgs.Empty);`.
      In `MainWindow.Recovery.cs`:
      ```csharp
      205:         private void PostponePendingDraft()
      206:         {
      207:             DraftBanner?.HideBanner();
      208:             if (TxtAutosaveStatus != null)
      209:                 TxtAutosaveStatus.Text = "🕒 Черновик отложен (нажмите для открытия)";
      210:             ToastService.ShowToast("Черновик отложен. Вы можете открыть его в любое время.", ToastType.Info);
      211:         }
      ```
      The draft on disk (`DraftRecoveryService.DraftFilePath`) is **retained** without deletion when closed/postponed.

---

### 1.2 DraftItemsPickerDialog Findings
- **File Locations**:
  - `MosquitoNetCalculator/Controls/DraftItemsPickerDialog.xaml`
  - `MosquitoNetCalculator/Controls/DraftItemsPickerDialog.xaml.cs`
  - Instantiation in `MosquitoNetCalculator/MainWindow.Recovery.cs:185`:
    `var dlg = new Controls.DraftItemsPickerDialog(draft) { Owner = this };`
- **Current Window Sizing (`DraftItemsPickerDialog.xaml`)**:
  - Line 6: `Width="580" Height="480"`
  - Fixed `Height="480"` prevents dynamic sizing; if a draft contains 1 item, a massive empty space is rendered.
  - Requirement R2 mandates: `SizeToContent="Height"` with `MaxHeight="520"`.
- **Display Name Formatting & Missing Space Typo**:
  - Line 31–32 in `DraftItemsPickerDialog.xaml.cs`:
    ```csharp
    public string Name => ItemData.Name;
    public string ColorSuffix => string.IsNullOrWhiteSpace(ItemData.Color) ? "" : $"({ItemData.Color})";
    ```
  - Line 95–101 in `DraftItemsPickerDialog.xaml`:
    ```xaml
    <StackPanel Orientation="Horizontal">
        <TextBlock Text="{Binding Name}" FontWeight="SemiBold"
                   FontSize="{StaticResource Type.Body}" Foreground="{DynamicResource TextPrimary}"/>
        <TextBlock Text="{Binding ColorSuffix}"
                   FontSize="{StaticResource Type.Body}" Foreground="{DynamicResource TextSecondary}"
                   Margin="{DynamicResource Gap.InlineSm}"/>
    </StackPanel>
    ```
  - Spacing token in `Themes/Tokens.Spacing.xaml:54`:
    `<Thickness x:Key="Gap.InlineSm">0,0,4,0</Thickness>`
  - Exact Mechanism of Bug:
    1. `Name` = `"Anwis"`.
    2. `ColorSuffix` = `"({ItemData.Color})"` = `"({Белый})"`. No space before `(`.
    3. `Gap.InlineSm` is a RIGHT margin (`0,0,4,0`), meaning the LEFT margin between `Name` and `ColorSuffix` is 0.
    4. WPF renders `Name` and `ColorSuffix` directly adjacent: `Anwis(Белый)`.
- **Card Styling & Tokens**:
  - Line 79–83 in `DraftItemsPickerDialog.xaml`:
    ```xaml
    <Border Background="{DynamicResource SurfaceVariant}"
            BorderBrush="{DynamicResource Border}" BorderThickness="1"
            CornerRadius="{DynamicResource Radius.Sm}"
            Margin="{DynamicResource Gap.BlockSm}"
            Padding="{DynamicResource Pad.Control}">
    ```
  - `Radius.Sm` is 4px (`Tokens.Radius.xaml:24`). Standard rounded card token across the system is `Radius.Control` = 8px (`Tokens.Radius.xaml:46`).
  - `Gap.BlockSm` is 4px bottom margin; `Gap.Block` = 8px bottom margin provides better breathing room.

---

### 1.3 Test Coverage Findings
- Searching `MosquitoNetCalculator.Tests` for `DraftRecoveryBanner` and `DraftItemsPickerDialog` yielded **0 test files**.
- Existing draft tests: Only `MosquitoNetCalculator.Tests/Services/StabilityAndSafetyFeaturesTests.cs` (6 tests total, passing), which tests atomic disk persistence in `DraftRecoveryService`.
- Reference for WPF Control tests on STA: `MosquitoNetCalculator.Tests/Controls/LocationCheckBannerTests.cs` using `TestAppThemes.RunOnSta(...)` and `[Collection("WPF_UI")]`.

---

## 2. Logic Chain

1. **R1 Layout Architecture**:
   - `DraftRecoveryBanner` is placed in Row 2 of `MainWindow.xaml` and acts as a non-modal crash/draft recovery indicator.
   - Requirement R1 specifies a left side containing: (a) icon in soft background container, (b) title «Восстановление черновика», and (c) summary information (date, item count, client name).
   - Currently, the title is `"Восстановление документа:"` and summary repeats `"Найден несохранённый черновик..."`.
   - By structuring Column 0 of the banner as a 2-column or 3-column Grid (`Auto`, `Auto`, `*`), the icon container (`AccentLight` 24x24 `Radius.Sm` or `Radius.Pill`) and title `Восстановление черновика` (`SemiBold` `TextPrimary`) remain unclipped, while `TxtDraftInfo` (`от {time} (позиций: {count}, клиент: {client})`) takes the remaining space with `TextTrimming="CharacterEllipsis"` in `TextSecondary`.
   - On the right side, buttons must be:
     - `BtnRestore`: `PrimaryButton` (Accent).
     - `BtnSelective`: `GhostButton` (resolving the broken `SecondaryButton` reference).
     - `BtnDiscard`: `GhostButton` with icon `&#xE74D;` (`FluentIconButton`).
     - `BtnClose`: `DialogCloseButton` with icon `&#xE8BB;` (24x24 or 28x28).
     - `BtnPostpone` is removed from XAML. In code-behind, `BtnClose_Click` calls `BtnPostpone_Click` (or invokes `PostponeRequested`), ensuring draft state remains intact on disk.

2. **R2 Window Sizing & Layout**:
   - Setting `Width="580" SizeToContent="Height" MaxHeight="520"` on `DraftItemsPickerDialog`:
     - When few items exist, `SizeToContent="Height"` measures Row 0 (Header), Row 1 (Bar), Row 2 (Items), Row 3 (Footer), rendering a compact window without empty void.
     - When many items exist, `MaxHeight="520"` bounds the window, and Row 2's `ScrollViewer` (`VerticalScrollBarVisibility="Auto"`) enables vertical scrolling.
   - Fixing the typo:
     - In `DraftItemPickerViewModel`:
       `public string ColorSuffix => string.IsNullOrWhiteSpace(ItemData.Color) ? "" : $" ({ItemData.Color})";`
       (or exposing `DisplayName => string.IsNullOrWhiteSpace(ItemData.Color) ? Name : $"{Name} ({ItemData.Color})";`).
       This directly ensures the space before the parenthesis is present regardless of layout margins.
   - Item Card Styling:
     - Changing `CornerRadius="{DynamicResource Radius.Sm}"` (4px) to `CornerRadius="{DynamicResource Radius.Control}"` (8px).
     - Using `Background="{DynamicResource SurfaceVariant}"` or `Surface` with `BorderBrush="{DynamicResource Border}"` maintains high-contrast readability (AA/AAA compliant).
     - Spacing between cards upgraded to `Margin="{DynamicResource Gap.Block}"` (8px).

3. **Testing Requirements**:
   - New STA unit tests should be added to `MosquitoNetCalculator.Tests/Controls/`:
     - `DraftRecoveryBannerTests.cs`: verifies collapsed by default, `DisplayDraft` updates title/metadata, `BtnRestore` raises `RestoreRequested`, `BtnSelective` raises `SelectiveRestoreRequested`, `BtnDiscard` raises `DiscardRequested`, `BtnClose` raises `PostponeRequested` without deleting draft.
     - `DraftItemsPickerDialogTests.cs`: verifies `DraftItemPickerViewModel` formatting (`Anwis (Белый)`), `SizeToContent` property, item selection toggling.

---

## 3. Caveats

1. **Process Locking**: During the build check, `MosquitoNetCalculator.dll` in `obj/Debug` was briefly locked by a background test runner/process (`CS2012`), but subsequently completed cleanly. Any build/test commands in implementation should terminate background test hosts if locked.
2. **Event Naming**: `DraftRecoveryBanner.PostponeRequested` is used by `MainWindow.Recovery.cs`. Even though the button `BtnPostpone` is deleted from UI, the event `PostponeRequested` must be retained because `BtnClose` fires it.
3. **Empty Item Colors**: Products without color (e.g. `ProductCatalog.NoColorProducts`) have `ItemData.Color == ""`. `ColorSuffix` should evaluate to `""` without creating trailing empty parentheses `" ()"`.

---

## 4. Conclusion

The implementation path for R1 and R2 is clear and isolated:
- **R1 (`DraftRecoveryBanner`)**:
  - Update title in XAML from «Восстановление документа:» to «Восстановление черновика».
  - Format `TxtDraftInfo` to display summary metadata `от {time} (позиций: {count}, клиент: {client})`.
  - Fix `BtnSelective` style to `GhostButton` (replaces nonexistent `SecondaryButton`).
  - Remove `BtnPostpone` button from XAML; route `BtnClose` to invoke `PostponeRequested`.
  - Add trash glyph `&#xE74D;` to `BtnDiscard`.
- **R2 (`DraftItemsPickerDialog`)**:
  - Remove `Height="480"` from `Window`, add `SizeToContent="Height"` and `MaxHeight="520"`.
  - Fix `ColorSuffix` in `DraftItemPickerViewModel` to `$" ({ItemData.Color})"`.
  - Update item cards to `CornerRadius="{DynamicResource Radius.Control}"` and `Margin="{DynamicResource Gap.Block}"`.
- **Tests**:
  - Add unit tests for both controls to ensure regression protection.

---

## 5. Verification Method

1. **Compilation**:
   `dotnet build MosquitoNetCalculator/MosquitoNetCalculator.csproj`
2. **Unit Tests**:
   `dotnet test --filter StabilityAndSafetyFeaturesTests`
   `dotnet test --filter DraftRecovery`
   `dotnet test --filter DraftItemsPicker`
3. **Inspection Points**:
   - Verify `SecondaryButton` is nowhere in `MosquitoNetCalculator/Controls/DraftRecoveryBanner.xaml`.
   - Verify `BtnPostpone` is absent from `DraftRecoveryBanner.xaml`.
   - Verify `ColorSuffix` in `DraftItemsPickerDialog.xaml.cs` contains a leading space `$" ({ItemData.Color})"`.
   - Verify `DraftItemsPickerDialog.xaml` contains `SizeToContent="Height"` and `MaxHeight="520"`.
