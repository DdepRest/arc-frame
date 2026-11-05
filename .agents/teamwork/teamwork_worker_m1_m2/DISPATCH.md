## 2026-11-02T10:31:17Z
You are teamwork_worker_m1_m2, an implementation worker.
Your working directory is: c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\teamwork_worker_m1_m2\
Your parent conversation ID is: be00f762-5960-401e-a821-1e550f087071

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

MANDATORY INPUTS:
- Read c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\ORIGINAL_REQUEST.md
- Read c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\orchestrator\PROJECT.md
- Read c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\teamwork_explorer_survey_1\handoff.md (detailed survey and blueprints)

EXCLUSIVE FILE OWNERSHIP (you may only modify these files):
- MosquitoNetCalculator/MainWindow.xaml
- MosquitoNetCalculator/MainWindow.xaml.cs (R1 lines only: overlay registration, ToggleSidebarOverlay, and removing startup forced nav collapse)
- MosquitoNetCalculator/Themes/Brushes.xaml
- MosquitoNetCalculator/Services/ThemeService.cs
- MosquitoNetCalculator/Themes/InputStyles.RadioButton.xaml
- MosquitoNetCalculator/Controls/OrdersHistoryControl.xaml

TASK INSTRUCTIONS:
1. R1.1: Restore right sidebar to slide-out overlay (SidebarOverlay) accessible via customer button:
   - In MainWindow.xaml: move SidebarPanel out of CalculationView into an overlay Grid named SidebarOverlay at Panel.ZIndex="15" with SidebarBackdrop, SidebarSlideTransform, and SidebarControl (following the pattern of OrdersOverlay / PricesOverlay). Remove column 1 and Grid.ColumnDefinitions from CalculationView so it occupies full width.
   - In MainWindow.xaml.cs: declare `_sidebarEntry`, register it with `_overlayManager`, and update `ToggleSidebarOverlay()` to show/hide via `_overlayManager`.
2. R1.2: Left navigation panel (NavPanel) expanded by default:
   - In MainWindow.xaml.cs: remove lines 375–380 that forcibly collapsed `_navService` on startup. The XAML definition (Width="160", IsExpanded=true) will now take effect on startup, with icons and labels visible, while user manual toggle via BtnToggleNav remains functional.
3. R2: Segmented Pill Tabs for Orders History:
   - In Themes/Brushes.xaml: add `<SolidColorBrush x:Key="SurfaceVariant" Color="#F0F0F4"/>`.
   - In Services/ThemeService.cs: add `["SurfaceVariant"] = "#F0F0F4"` to LightColors and `["SurfaceVariant"] = "#2A2A2E"` to DarkColors.
   - In Themes/InputStyles.RadioButton.xaml: define `SegmentedRadioButton` style with `Radius.Control`, `Pad.ButtonSm`, `Accent` active background, `OnAccent` active text, without default Windows radio button circles or bullet dots.
   - In Controls/OrdersHistoryControl.xaml: update container Border CornerRadius to `{DynamicResource Radius.Control}` and ensure RadioButtons use `Style="{DynamicResource SegmentedRadioButton}"` and `GroupName="OrderHistoryViewMode"`.
4. Verification:
   - Run `dotnet build MosquitoNetCalculator/MosquitoNetCalculator.csproj -c Release`
   - Run `dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj --filter "FullyQualifiedName~DesignTokenGuardTests"`
   - Ensure build passes with 0 errors and tests pass.
5. Record progress in progress.md, compile comprehensive handoff.md, and send completion message via send_message to parent.
