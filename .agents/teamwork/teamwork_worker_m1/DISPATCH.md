## 2026-11-02T12:34:41Z
You are teamwork_worker_m1.
Your working directory is: c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\teamwork_worker_m1
Your parent is: 5194245e-d76b-4446-9341-e73f2b5a09dc

MANDATORY: Read the original user request before starting work:
c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\ORIGINAL_REQUEST.md

Read the project scope and explorer findings:
- PROJECT.md: c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\orchestrator\PROJECT.md
- Survey findings: c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\teamwork_explorer_s1\handoff.md

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

EXCLUSIVE WRITE OWNERSHIP (you may only edit or create these files):
- MosquitoNetCalculator/Controls/DraftRecoveryBanner.xaml
- MosquitoNetCalculator/Controls/DraftRecoveryBanner.xaml.cs
- MosquitoNetCalculator/Controls/DraftItemsPickerDialog.xaml
- MosquitoNetCalculator/Controls/DraftItemsPickerDialog.xaml.cs
- MosquitoNetCalculator.Tests/Controls/DraftRecoveryBannerTests.cs
- MosquitoNetCalculator.Tests/Controls/DraftItemsPickerDialogTests.cs

Task: Implement Milestone 1 (Requirements R1 and R2):
1. Visual Redesign of DraftRecoveryBanner:
   - Layout: Clean Excel-style banner design.
   - Left side: Display icon in soft background container (`Border` 24x24 `AccentLight` `Radius.Sm`, icon `&#xE81C;` `AccentText`), title «Восстановление черновика», and summary information `от {time} (позиций: {count}, клиент: {client})`.
   - Right side: Action buttons:
     * `[ Восстановить всё ]`: `PrimaryButton` (Accent).
     * `[ Выбрать позиции… ]`: `GhostButton` (resolves broken `SecondaryButton` reference).
     * `[ Удалить черновик ]`: `GhostButton` with trash icon `&#xE74D;` (`FontFamily="{DynamicResource Font.Icon}"`).
     * `[ ✕ ]` close button: `DialogCloseButton` with glyph `&#xE8BB;`. Clicking `[ ✕ ]` triggers `PostponeRequested` (closes banner without deleting draft).
   - Remove redundant standalone «Отложить» button from XAML.
   - Apply consistent spacing and padding according to design tokens.
2. Sizing and Styling of DraftItemsPickerDialog:
   - Window sizing: Set `Width="580" SizeToContent="Height" MaxHeight="520"`. (Remove fixed `Height="480"`).
   - Fix display name spacing typo: In `DraftItemPickerViewModel.ColorSuffix`, ensure leading space before parenthesis: `public string ColorSuffix => string.IsNullOrWhiteSpace(ItemData.Color) ? "" : $" ({ItemData.Color})";` so it renders `Anwis (Белый)`.
   - Style item cards with rounded corners (`CornerRadius="{DynamicResource Radius.Control}"`, 8px) and clear, readable contrast (`Background="{DynamicResource SurfaceVariant}"`, `BorderBrush="{DynamicResource Border}"`, `Margin="{DynamicResource Gap.Block}"`).
3. Add unit tests for both controls in `MosquitoNetCalculator.Tests/Controls/DraftRecoveryBannerTests.cs` and `DraftItemsPickerDialogTests.cs` (reference `LocationCheckBannerTests.cs` for STA / WPF UI test pattern).
4. Build and verify:
   - `dotnet build MosquitoNetCalculator/MosquitoNetCalculator.csproj`
   - Run unit tests for the modified controls.
