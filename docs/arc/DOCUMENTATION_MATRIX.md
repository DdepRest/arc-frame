# DOCUMENTATION_MATRIX.md

> Auto-generated from documentation-matrix.json. Edit the JSON, then run render-matrix.ps1.

## File -> Docs mapping

Use on the **Document** phase. If you changed a file in the left column, update all files in the right column.

Or run: what-to-update.ps1 (git diff --name-only) -- the script reads documentation-matrix.json.

### Models

| Changed file | Update docs |
|---|---|
| `Models/OrderItem.Calculations.cs` | `CALCULATION_LOGIC.md`, `CALCULATION_TEST_CASES.md`, `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Models/OrderItem.cs` | `CALCULATION_LOGIC.md`, `GOTCHAS.md#1`, `CURRENT_STATE.md`, `CHANGELOG.md` (Width/Height setter'С‹, РЁРёСЂРёРЅР°Р’РІРѕРґ/Р’С‹СЃРѕС‚Р°Р’РІРѕРґ) |
| `Models/OrderItem.Installation.cs` | `CALCULATION_LOGIC.md#РјРѕРЅС‚Р°Р¶`, `GOTCHAS.md#11`, `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Models/AnwisSize.cs` | `CALCULATION_LOGIC.md`, `CALCULATION_TEST_CASES.md`, `GOTCHAS.md#1`, `CHANGELOG.md` |
| `Models/AnwisSizeMode.cs` | `GOTCHAS.md#1` (enum values 0-4 вЂ” breaking change) |
| `Models/OrderData.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Models/OrderItem.Dto.cs` | `GOTCHAS.md#3`, `CURRENT_STATE.md`, `CHANGELOG.md` (derived-РїРѕР»СЏ!) |
| `Models/ClientInfo.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Models/PriceItem.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Models/UpdateManifest.cs` | `AUTO_UPDATE.md`, `RELEASE_PROCESS.md`, `CHANGELOG.md` |
| `Models/LocationOptions.cs` | `CURRENT_STATE.md` |
| `Models/OrderSnapshot.cs` | `CURRENT_STATE.md` (Undo/Redo) |
| `Models/UpdateItem.cs` | `CURRENT_STATE.md` |
| `Models/AdditionalKpItem.cs` | `CURRENT_STATE.md` |

### ViewModels

| Changed file | Update docs |
|---|---|
| `ViewModels/CalculationViewModel.cs` | `CALCULATION_LOGIC.md#РёС‚РѕРіРё`, `CURRENT_STATE.md`, `CHANGELOG.md` |
| `ViewModels/MainWindowViewModel.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` |
| `ViewModels/OrdersHistoryViewModel.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` |
| `ViewModels/PricesViewModel.cs` | `CURRENT_STATE.md`, `CHANGELOG.md` |

### Services

| Changed file | Update docs |
|---|---|
| `Services/PriceService.cs` | `GOTCHAS.md#4`, `CALCULATION_LOGIC.md#С†РµРЅС‹`, `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Services/PrintService.cs` | `GOTCHAS.md#6`, `CALCULATION_LOGIC.md#РљРџ`, `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Services/FactoryTextService.cs` | `GOTCHAS.md#Р·Р°РІРѕРґ`, `CALCULATION_LOGIC.md#Р·Р°РІРѕРґ`, `CALCULATION_TEST_CASES.md`, `CHANGELOG.md` |
| `Services/UpdateService.cs` | `AUTO_UPDATE.md`, `GOTCHAS.md#5`, `GOTCHAS.md#8`, `RELEASE_PROCESS.md`, `CHANGELOG.md` |
| `Services/WatchdogService.cs` | `AUTO_UPDATE.md`, `GOTCHAS.md#5`, `RELEASE_PROCESS.md`, `CHANGELOG.md` |
| `Services/UpdateLog.cs` | `CURRENT_STATE.md` |
| `Services/OrderStorageService.cs` | `GOTCHAS.md#3`, `GOTCHAS.md#9`, `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Services/AppSettingsService.cs` | `GOTCHAS.md#9`, `CURRENT_STATE.md`, `CHANGELOG.md` |
| `Services/AnwisSizeService.cs` | `CURRENT_STATE.md` |
| `Services/AmountInWordsService.cs` | `CALCULATION_TEST_CASES.md`, `CHANGELOG.md` (Case 11) |
| `Services/MoneyFormatService.cs` | `CURRENT_STATE.md` |
| `Services/ThemeService.cs` | `GOTCHAS.md#7`, `DECISIONS.md#10`, `CHANGELOG.md` |
| `Services/DialogService.cs` | `CURRENT_STATE.md` |
| `Services/ToastService.cs` | `CURRENT_STATE.md` |
| `Services/UndoRedoService.cs` | `GOTCHAS.md#10`, `CURRENT_STATE.md` |

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

### Themes

| Changed file | Update docs |
|---|---|
| `Themes/Brushes.xaml` | `DECISIONS.md#10`, `GOTCHAS.md#7`, `CHANGELOG.md` |
| `Themes/*.xaml` | `CHANGELOG.md` (СЃС‚РёР»Рё) |

### Resources

| Changed file | Update docs |
|---|---|
| `Resources/print_template.html` | `GOTCHAS.md#6`, `CALCULATION_LOGIC.md#РљРџ`, `CHANGELOG.md` |
| `Resources/update-log.json` | `CURRENT_STATE.md`, `RELEASE_PROCESS.md` |

### Project / Config

| Changed file | Update docs |
|---|---|
| `MosquitoNetCalculator.csproj` | `RELEASE_PROCESS.md`, `CURRENT_STATE.md`, `CHANGELOG.md`, `AUTO_UPDATE.md` (РІРµСЂСЃРёСЏ) |
| `releases.json` | `AUTO_UPDATE.md`, `RELEASE_PROCESS.md`, `CURRENT_STATE.md` |
| `build.bat` | `RELEASE_PROCESS.md`, `CHANGELOG.md` |
| `installer.iss` | `RELEASE_PROCESS.md`, `CHANGELOG.md` |

### Tests

| Changed file | Update docs |
|---|---|
| `*.Tests.cs` | `CURRENT_STATE.md` (РєРѕР»РёС‡РµСЃС‚РІРѕ С‚РµСЃС‚РѕРІ, РµСЃР»Рё РёР·РјРµРЅРёР»РѕСЃСЊ) |

---

## Auto-update of 'Last verified'

Use what-to-update.ps1 to get the list of docs to update - the script reads documentation-matrix.json directly.

## Source files

- docs/arc/documentation-matrix.json -- machine-readable source (edit this!)
- render-matrix.ps1 -- generates this file from JSON

## Last verified

2026-06-24 (generated from JSON)