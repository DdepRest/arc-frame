# DOCUMENTATION_MATRIX.md

> Auto-generated from documentation-matrix.json. Edit the JSON, then run agents/scripts/render-matrix.ps1.

## File -> Docs mapping

Use on the **Document** phase. If you changed a file in the left column, update all files in the right column.

Or run: agents/scripts/what-to-update.ps1 (git diff --name-only) -- the script reads documentation-matrix.json.

### Models

| Changed file | Update docs |
|---|---|
| `Models/OrderItem.Calculations.cs` | `CALCULATION_LOGIC.md`, `CALCULATION_TEST_CASES.md`, `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Models/OrderItem.cs` | `CALCULATION_LOGIC.md`, `GOTCHAS.md#1`, `CURRENT_STATE.md`, `CHANGELOG.md` (Width/Height setter'ы, ШиринаВвод/ВысотаВвод) |
| `Models/OrderItem.Installation.cs` | `CALCULATION_LOGIC.md#монтаж`, `GOTCHAS.md#11`, `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Models/AnwisSize.cs` | `CALCULATION_LOGIC.md`, `CALCULATION_TEST_CASES.md`, `GOTCHAS.md#1`, `CHANGELOG.md` (Phase 5: delegates to AnwisSizeCalculator) |
| `Models/ProductCatalog.cs` | `CALCULATION_LOGIC.md`, `CURRENT_STATE.md`, `CHANGELOG.md` (Phase 5: product category HashSets) |
| `Models/SlopeCalculationExtensions.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (Phase 5: DeepClone extension for SlopeCalculation) |
| `Models/AnwisSizeMode.cs` | `GOTCHAS.md#1` (enum values 0-4 — breaking change) |
| `Models/OrderData.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Models/OrderItem.Dto.cs` | `GOTCHAS.md#3`, `CURRENT_STATE.md`, `CHANGELOG.md` (derived-поля!) |
| `Models/ClientInfo.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Models/PriceItem.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Models/UpdateManifest.cs` | `AUTO_UPDATE.md`, `RELEASE_PROCESS.md`, `CHANGELOG.md` |
| `Models/LocationOptions.cs` | `CURRENT_STATE.md` |
| `Models/OrderSnapshot.cs` | `CURRENT_STATE.md` (Undo/Redo) |
| `Models/UpdateItem.cs` | `CURRENT_STATE.md` |
| `Models/AdditionalKpItem.cs` | `CURRENT_STATE.md` |
| `Models/AiChatMessage.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (persisted AI model/provider badge + plan state (awaiting confirmation/executed/cancelled)) |
| `Models/AiClarificationForm.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (interactive parameter card for AI clarification replies) |
| `Models/AiActionPlan.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (AI Agent Mode: action plan with steps, confirmation and execution state) |
| `Models/AiOrderContext.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (AI Agent Mode: rich order snapshot context sent to the LLM) |
| `Models/AiRequestMetrics.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (AI Agent Mode: per-request metrics (provider, attempt, fallback)) |
| `Models/AiCalculationExplanationContext.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (AI Agent Mode: calculation explanation context for /объясни) |
| `Models/OfficeReport.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (Админ-панель: отчёт устройства офиса (gist office-{prefix}-{deviceId}.json, DeviceId/DeviceName)) |
| `Models/OfficeDeviceRow.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (Админ-панель: одно устройство офиса (версия/статус/имя ПК)) |
| `Models/OfficeStatusRow.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (Админ-панель: статусы офисов (UpToDate/Outdated/NoData)) |
| `Models/OfficeStatsRow.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (Админ-панель «Статистика»: строка с кол-вом заказов офиса) |

### ViewModels

| Changed file | Update docs |
|---|---|
| `ViewModels/CalculationViewModel.cs` | `CALCULATION_LOGIC.md#итоги`, `CURRENT_STATE.md`, `CHANGELOG.md` |
| `ViewModels/MainWindowViewModel.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` |
| `ViewModels/OrdersHistoryViewModel.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` |
| `ViewModels/PricesViewModel.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` |
| `ViewModels/AiAssistantViewModel.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (AI Agent Mode: background streaming, slash routing, plan→confirm→execute, undo/redo requests) |

### Services

| Changed file | Update docs |
|---|---|
| `Services/PriceService.cs` | `GOTCHAS.md#4`, `CALCULATION_LOGIC.md#цены`, `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Services/PrintService.cs` | `GOTCHAS.md#6`, `CALCULATION_LOGIC.md#КП`, `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Services/FactoryTextService.cs` | `GOTCHAS.md#завод`, `CALCULATION_LOGIC.md#завод`, `CALCULATION_TEST_CASES.md`, `CHANGELOG.md` |
| `Services/UpdateService.cs` | `AUTO_UPDATE.md`, `GOTCHAS.md#5`, `GOTCHAS.md#8`, `RELEASE_PROCESS.md`, `CHANGELOG.md` |
| `Services/WatchdogService.cs` | `AUTO_UPDATE.md`, `GOTCHAS.md#5`, `RELEASE_PROCESS.md`, `CHANGELOG.md` |
| `Services/UpdateLog.cs` | `CURRENT_STATE.md` |
| `Services/OrderStorageService.cs` | `GOTCHAS.md#3`, `GOTCHAS.md#9`, `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Services/AppSettingsService.cs` | `GOTCHAS.md#9`, `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Services/AnwisSizeService.cs` | `CURRENT_STATE.md` |
| `Services/AnwisSizeCalculator.cs` | `CALCULATION_LOGIC.md`, `CALCULATION_TEST_CASES.md`, `CURRENT_STATE.md`, `CHANGELOG.md` (Phase 5: pure Anwis size calculation functions) |
| `Services/AmountInWordsService.cs` | `CALCULATION_TEST_CASES.md`, `CHANGELOG.md` (Case 11) |
| `Services/MoneyFormatService.cs` | `CURRENT_STATE.md` |
| `Services/ThemeService.cs` | `GOTCHAS.md#7`, `DECISIONS.md#10`, `CHANGELOG.md` |
| `Services/DialogService.cs` | `CURRENT_STATE.md` |
| `Services/ToastService.cs` | `CURRENT_STATE.md` |
| `Services/UndoRedoService.cs` | `GOTCHAS.md#10`, `CURRENT_STATE.md` |
| `Services/NotesFormatter.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (lightweight notes markup parser) |
| `Services/NotesRenderer.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (WPF inline renderer for formatted notes) |
| `Services/AiAssistantService.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (AI streaming, model routing and free-provider fallback) |
| `Services/AiModelSelector.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (task-based free-model ranking) |
| `Services/AiTaskClassifier.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (local task classification for model selection) |
| `Services/AiCommandParser.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (add-item JSON parsing + plan-mode (steps); GetDefaultPrice/GenerateActionConfirmation reused by clarification form) |
| `Services/AiPlanBuilder.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (AI Agent Mode: converts parsed commands into validated action plans) |
| `Services/AiPlanValidator.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (AI Agent Mode: local parameter validation before execution) |
| `Services/AiPlanExecutor.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (AI Agent Mode: atomic batch execution with rollback) |
| `Services/AiOrderContextBuilder.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (AI Agent Mode: builds AiOrderContext from live order state) |
| `Services/AiExplanationContextBuilder.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (AI Agent Mode: builds explanation context from actual totals) |
| `Services/AiTelemetryService.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (AI Agent Mode: request metrics and in-memory stats) |
| `Services/AiLocalCommandRouter.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (AI Agent Mode: offline slash commands (/товары /итоги /объясни etc.)) |
| `Services/OfficeReportService.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (Админ-панель: обмен отчётами через секретный GitHub Gist, очистка дублей устройств (кнопка «Очистить дубли»)) |
| `Services/OfficeStatusCalculator.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (Админ-панель: чистая логика статусов (порог свежести 72ч)) |
| `Services/OfficeStatsCalculator.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (Админ-панель «Статистика»: чистая логика кол-ва заказов по офисам) |
| `Services/OfficeDeviceGrouping.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (Админ-панель: дедупликация устройств офиса по имени машины (один ПК = одно устройство, легаси-записи)) |
| `Services/OfficeReportScheduler.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (Админ-панель: периодическая отправка отчёта офиса каждые 30 мин (живые статусы/статистика)) |

### Controls (WPF UI)

| Changed file | Update docs |
|---|---|
| `Controls/QuickAddControl.*` | `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Controls/OrderItemsControl.*` | `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Controls/SidebarControl.*` | `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Controls/ActionBarControl.*` | `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Controls/OrdersHistoryControl.*` | `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Controls/PricesControl.*` | `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Controls/UpdatesTabControl.*` | `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Controls/TotalCardControl.*` | `CURRENT_STATE.md` |
| `Controls/SendToFactoryWindow.*` | `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Controls/AnwisContextMenuBuilder.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Controls/AiAssistantControl.*` | `CURRENT_STATE.md`, `CHANGELOG.md` (streaming chat UI, model badges, plan preview card with Выполнить/Отмена and Отменить действие) |
| `Controls/AiApiKeyDialog.*` | `CURRENT_STATE.md`, `CHANGELOG.md` (AI keys, model catalog and auto-select UX) |
| `Controls/AdminPanelControl.xaml` | `CURRENT_STATE.md`, `CHANGELOG.md` (Админ-панель: UI статусов офисов) |
| `Controls/AdminPanelControl.xaml.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (Админ-панель: RefreshAsync (отчёты + последняя версия)) |
| `Controls/AdminPasswordWindow.xaml` | `CURRENT_STATE.md`, `CHANGELOG.md` (Админ-панель: окно входа/установки пароля) |
| `Controls/AdminPasswordWindow.xaml.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (Админ-панель: EnterMode/SetMode диалога пароля) |

### Themes

| Changed file | Update docs |
|---|---|
| `Themes/Brushes.xaml` | `DECISIONS.md#10`, `GOTCHAS.md#7`, `CHANGELOG.md` |
| `Themes/*.xaml` | `CHANGELOG.md` (стили) |

### Resources

| Changed file | Update docs |
|---|---|
| `Resources/update-log.json` | `CURRENT_STATE.md`, `RELEASE_PROCESS.md` |

### Project / Config

| Changed file | Update docs |
|---|---|
| `MosquitoNetCalculator.csproj` | `RELEASE_PROCESS.md`, `CURRENT_STATE.md`, `CHANGELOG.md`, `AUTO_UPDATE.md` (версия) |
| `releases.json` | `AUTO_UPDATE.md`, `RELEASE_PROCESS.md`, `CURRENT_STATE.md` |
| `build.bat` | `RELEASE_PROCESS.md`, `CHANGELOG.md` |
| `installer.iss` | `RELEASE_PROCESS.md`, `CHANGELOG.md` |

### Tests

| Changed file | Update docs |
|---|---|
| `*.Tests.cs` | `CURRENT_STATE.md` (количество тестов, если изменилось (включая ProductCatalogTests, AnwisSizeCalculatorTests, SlopeCalculationExtensionsTests для Phase 5)) |

### Other

| Changed file | Update docs |
|---|---|
| `agents/docs/PROJECT_OVERVIEW.md` | `CURRENT_STATE.md` (project description and tech stack) |
| `agents/docs/INTENTS.md` | `CURRENT_STATE.md` (A.R.C. v4: intent-to-file routing) |
| `agents/docs/SYMBOL_INDEX.md` | `CURRENT_STATE.md` (A.R.C. v4: auto-generated symbol index) |
| `README.md` | `CURRENT_STATE.md` (project overview for GitHub) |
| `agents/docs/REFACTORING_PLAN.md` | `CURRENT_STATE.md`, `MULTI_AGENT_ARC_CALC_CONTROL.md`, `AGENTS.md` (system refactoring plan) |
| `agents/scripts/gensymbols.ps1` | `CURRENT_STATE.md` (A.R.C. v4: symbol index generator) |
| `agents/scripts/arc-check.ps1` | `CURRENT_STATE.md` (A.R.C. v4: pre-commit doc sync check) |
| `MosquitoNetCalculator/MainWindow.xaml` | `CURRENT_STATE.md`, `CHANGELOG.md` (AI BETA navigation and panel badge) |
| `MosquitoNetCalculator/Converters/BoolVisibilityConverter.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (AI model badge visibility converter) |
| `MosquitoNetCalculator/MainWindow.xaml.cs` | `REFACTORING_PLAN.md`, `CURRENT_STATE.md`, `CHANGELOG.md` (Phase 1: NavigationService, OverlayManager, SlopeOverlayCoordinator) |
| `MosquitoNetCalculator/MainWindow.Orders.cs` | `REFACTORING_PLAN.md`, `CURRENT_STATE.md`, `CHANGELOG.md` (Phase 6: OrderImportExportService, OrderDialogService, OrderGridPresenter) |
| `MosquitoNetCalculator/Services/UpdateService.cs` | `REFACTORING_PLAN.md`, `AUTO_UPDATE.md`, `GOTCHAS.md#5`, `GOTCHAS.md#8`, `RELEASE_PROCESS.md`, `CURRENT_STATE.md`, `CHANGELOG.md` (Phase 2: manifest/version/download/verify/presenter) |
| `MosquitoNetCalculator/Services/PrintService.cs` | `REFACTORING_PLAN.md`, `GOTCHAS.md#6`, `CALCULATION_LOGIC.md#КП`, `CURRENT_STATE.md`, `CHANGELOG.md` (Phase 3: queue resolver, fixed document builder, orchestrator) |
| `MosquitoNetCalculator/Services/DialogService.cs` | `REFACTORING_PLAN.md`, `CURRENT_STATE.md`, `CHANGELOG.md` (Phase 4: XAML templates + DialogBuilder) |
| `MosquitoNetCalculator/Models/OrderItem.cs` | `REFACTORING_PLAN.md`, `CALCULATION_LOGIC.md`, `GOTCHAS.md#1`, `CURRENT_STATE.md`, `CHANGELOG.md` (Phase 5: ProductCatalog, AnwisSizeCalculator, SlopeCalculationExtensions) |
| `MosquitoNetCalculator/MainWindow.AI.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (AI Agent Mode: rich order context, plan execution with single undo snapshot, guarded undo/redo) |
| `MosquitoNetCalculator/Models/AiCommand.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (AI Agent Mode: plan-mode AiResponse.Plan + update/delete command params) |
| `agents/scripts/validate-docs.ps1` | `CURRENT_STATE.md` (A.R.C.: doc consistency validator (10 checks, incl. soft #10 self-maintenance)) |
| `AGENTS.md` | `CHEATSHEET.md`, `CURRENT_STATE.md` (thin wrapper; self-maintenance duty CONTROL#13) |
| `agents/docs/CHEATSHEET.md` | `CURRENT_STATE.md` (critical rules + routing table) |
| `agents/docs/MULTI_AGENT_ARC_CALC_CONTROL.md` | `CHEATSHEET.md`, `CURRENT_STATE.md`, `AGENTS.md` (canonical source of truth; CONTROL#13 self-maintenance) |
| `agents/scripts/sync-version.ps1` | `CHEATSHEET.md`, `MULTI_AGENT_ARC_CALC_CONTROL.md` (sync-version.ps1: auto-sync csproj version to all agents/docs Last verified) |
| `MosquitoNetCalculator/Converters/OfficeStatusConverters.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` (Админ-панель: бейджи статусов (BadgeSuccess/Warning/Danger)) |

---

## Auto-update of 'Last verified'

Use agents/scripts/what-to-update.ps1 to get the list of docs to update - the script reads documentation-matrix.json directly.

## Source files

- agents/docs/documentation-matrix.json -- machine-readable source (edit this!)
- agents/scripts/render-matrix.ps1 -- generates this file from JSON

## Last verified

2026-08-17 (generated from JSON)