## 2026-11-02T11:07:40Z

You are teamwork_worker_m4, an implementation worker for Milestone 4 (R4: Crash Recovery Draft Preservation & Styling).
Your working directory is: c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\teamwork_worker_m4\
Your parent conversation ID is: be00f762-5960-401e-a821-1e550f087071

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

MANDATORY INPUTS:
- Read c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\ORIGINAL_REQUEST.md
- Read c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\orchestrator\PROJECT.md
- Read c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\teamwork_explorer_survey_3\handoff.md (detailed survey and blueprints for R4)

EXCLUSIVE FILE OWNERSHIP (you may only modify these files):
- MosquitoNetCalculator/MainWindow.Items.cs
- MosquitoNetCalculator/MainWindow.Recovery.cs
- MosquitoNetCalculator/MainWindow.xaml.cs
- MosquitoNetCalculator/Controls/ActionBarControl.xaml.cs
- MosquitoNetCalculator/Controls/DraftRecoveryBanner.xaml

TASK INSTRUCTIONS:
1. R4.1: Draft Preservation across Startup:
   - In MainWindow.Items.cs: update StartNewOrder to `internal void StartNewOrder(bool discardDraft = true)`. Only call `DraftService.DiscardDraft();` and `DraftBanner?.HideBanner();` if `discardDraft` is true.
   - In MainWindow.xaml.cs: in constructor around line 190, change `StartNewOrder();` to `StartNewOrder(discardDraft: false);`.
   - Ensure existing draft.json is NOT deleted during application launch.
2. R4.2: Startup Toast Suppression:
   - In MainWindow.Recovery.cs: update `CheckAndShowDraftRecovery(bool silentIfNoDraft = false)`. When no draft exists and `silentIfNoDraft` is true, do NOT call ToastService.ShowToast. In `InitializeRecoveryAndBackup()`, call `CheckAndShowDraftRecovery(silentIfNoDraft: true)`. In `AutosaveStatus_Click` (MainWindow.xaml.cs), call with default `silentIfNoDraft: false` so user clicks still show the toast.
3. R4.3: User Reset Actions:
   - Verify `BtnNewOrder_Click` calls `StartNewOrder()` (which defaults to discardDraft: true).
   - In Controls/ActionBarControl.xaml.cs: in `BtnClearAll_Click`, add explicit draft discard: `mw.DraftService.DiscardDraft(); mw.DraftBanner?.HideBanner();`.
4. R4.4: DraftRecoveryBanner Design Tokens:
   - In Controls/DraftRecoveryBanner.xaml: replace invalid tokens `WarningBg` and `WarningBorder` with valid design tokens:
     - Background="{DynamicResource SectionBg}"
     - BorderBrush="{DynamicResource Border}"
     - Icon container: Background="{DynamicResource AccentLight}"
     - Icon text: Foreground="{DynamicResource AccentText}"
5. Verification:
   - Run `dotnet build MosquitoNetCalculator/MosquitoNetCalculator.csproj -c Release`
   - Run `dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj --filter "FullyQualifiedName~StabilityAndSafetyFeaturesTests|FullyQualifiedName~DesignTokenGuardTests"`
   - Ensure build passes with 0 errors and all tests pass.
6. Record progress in progress.md, compile comprehensive handoff.md, and send completion message via send_message to parent.
