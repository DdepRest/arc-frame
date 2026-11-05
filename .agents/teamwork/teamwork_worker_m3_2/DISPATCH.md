## 2026-11-02T10:50:45Z
You are teamwork_worker_m3_2, a replacement implementation worker for Milestone 3 (R3: Installation Choice Usability and QuickAdd Staging Guard).
Your working directory is: c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\teamwork_worker_m3_2\
Your parent conversation ID is: be00f762-5960-401e-a821-1e550f087071

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

MANDATORY INPUTS:
- Read c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\ORIGINAL_REQUEST.md
- Read c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\orchestrator\PROJECT.md
- Read c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\teamwork_explorer_survey_2\handoff.md (detailed blueprints and line citations)

EXCLUSIVE FILE OWNERSHIP (you may only modify these files):
- MosquitoNetCalculator/Controls/InstallationChoiceDialog.xaml
- MosquitoNetCalculator/Controls/InstallationChoiceDialog.xaml.cs
- MosquitoNetCalculator/Controls/QuickAddControl.AddItem.cs
- MosquitoNetCalculator/ViewModels/CalculationViewModel.cs

TASK INSTRUCTIONS:
1. Live Total Preview in InstallationChoiceDialog:
   - In InstallationChoiceDialog.xaml: add TextBlock x:Name="TxtTotalPreview" below the adjustment controls, styled with typography tokens (Type.BodyLg, Weight.SemiBold, AccentText).
   - In InstallationChoiceDialog.xaml.cs: implement live preview calculation:
     factor = _item.InstallationLinearMeters * _item.Quantity (or 1.0 if linear meters <= 0);
     preview = Math.Round(Math.Max(0, _item.Total + adjustment * factor), 2);
     Display preview formatted as currency/rubles.
     Hook this update into SelectMode, ChkSign.Click, and TxtAdjustment.TextChanged.
2. Escape vs Close Semantics:
   - Close button 'X' (BtnClose_Click): cancels with DialogResult = false.
   - Escape key in Window_PreviewKeyDown: accepts with default option (DialogResult = true).
3. QuickAdd In-Memory Staging Guard:
   - In CalculationViewModel: provide CreateItem(...) so an OrderItem can be constructed in memory without immediately adding it to OrderItems, and AddItem(OrderItem item) to append an existing OrderItem.
   - In QuickAddControl.AddItem.cs: stage the item in memory. If ProductCatalog.IsInstallationApplicable and AskInstallationModeOnAdd is true, show InstallationChoiceDialog.
   - ONLY add the staged item to OrderItems if the dialog returns true (confirmed or default on Escape).
   - If dialog is cancelled (DialogResult == false / closed via 'X'), discard the staged item and DO NOT add it to OrderItems.
4. Verification:
   - Run `dotnet build MosquitoNetCalculator/MosquitoNetCalculator.csproj -c Release`
   - Run `dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj --filter "FullyQualifiedName~InstallationChoiceDialog|FullyQualifiedName~QuickAddControlTests"`
   - Ensure build passes with 0 errors and tests pass.
5. Record progress in progress.md, compile comprehensive handoff.md, and send completion message via send_message to parent.
