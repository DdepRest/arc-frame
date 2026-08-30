# MODULES.md

## Карта модулей проекта

### 1. Models — модели данных

**Где:** `MosquitoNetCalculator/Models/`

| Файл | За что отвечает | Что нельзя менять без осторожности |
|------|----------------|-----------------------------------|
| `OrderItem.cs` | Основная модель строки заказа. Свойства: Name, Color, Width, Height, Quantity, Price, IsActive, AnwisSizeMode. | ШиринаВвод/ВысотаВвод setter'ы, логика AnwisSizeMode setter — риск утечки формул на не-Anwis товары. |
| `OrderItem.Calculations.cs` | Расчёт CalculatedValue и Total для каждого товара. | Формулы расчёта (площадь, периметр, штуки) — это деньги. |
| `OrderItem.Installation.cs` | Логика монтажа: режимы "включён", "без монтажа", "в конструкцию" и вычеты. | Суммы вычетов (по умолчанию 500 руб) — влияет на итоговую цену. |
| `OrderItem.Dto.cs` | DTO для сериализации заказа в JSON. | Структура полей — влияет на совместимость сохранённых заказов. |
| `AnwisSize.cs` | Трёхслойная система размеров Anwis: Отображение → Расчёт → Завод. | Все формулы ApplyCalcWidth/Height, ReverseCalcWidth/Height — сердце коррекции размеров. |
| `AnwisSizeMode.cs` | Перечисление 5 режимов Anwis. | Значения enum (0-4) сохраняются в JSON — менять нельзя без миграции. |
| `OrderData.cs` | Модель заказа целиком (клиент, позиции, дата, статус). | Структура полей — влияет на сохранение/загрузку. |
| `OrderSnapshot.cs` | Снимок для Undo/Redo. | — |
| `ClientInfo.cs` | Данные клиента, Доп.КП, примечания. | — |
| `PriceItem.cs` | Модель цены (Name, Color, Price). | — |
| `UpdateItem.cs` | Модель записи "Обновления". | — |
| `UpdateManifest.cs` | DTO для releases.json (версия, URL, SHA-256). | Поля должны соответствовать releases.json. |
| `AdditionalKpItem.cs` | Модель дополнительного КП. | — |
| `LocationOptions.cs` | Список точек установки для экрана приветствия. | — |
| `OfficeReport.cs` | Отчёт УСТРОЙСТВА офиса (версия, время, кол-во заказов, deviceId, deviceName) — содержимое файла `office-{prefix}-{deviceId}.json` в секретном gist. | Поля сериализуются в gist — панель читает их по именам JSON-полей; новые поля добавляются обратносовместимо (старые отчёты читаются с дефолтами). |
| `OfficeDeviceRow.cs` | Одно устройство офиса в админ-панели: имя ПК, версия, статус (✓/⚠/❓), время отчёта, display-свойства. | — |
| `OfficeStatsRow.cs` | Строка секции «Статистика» админ-панели: офис + кол-во заказов (сумма по устройствам) + `DeviceCount`. | — |
| `OfficeStatusRow.cs` | Строка админ-панели: офис + статус (UpToDate/Outdated/NoData) + display-свойства. | — |
| `AiChatMessage.cs` | Модель сообщения чата AI (текст, статус, модель, временная метка) + состояние плана (ожидает подтверждения / выполнен / отменён) и `MessageId` для защиты от повторного выполнения. | `MessageId` и `PlanId` — идентичность сообщения; нельзя выполнить план дважды. |
| `AiClarificationForm.cs` | Интерактивная форма уточнения параметров товара в AI-чате (тип, размеры, цвет, количество, режим Anwis, монтаж); `TryBuildCommand()` собирает `AiCommand` без повторного запроса к LLM. | Каталоги списков (типы/цвета/режимы) должны совпадать с `PriceService` и `AnwisSizeMode`. |
| `AiCommand.cs` | Команды AI (`AiCommand`, `AiCommandParams`, `AiCommandType`) + `AiResponse` с полем `Plan` (plan-mode JSON от LLM). | Поля `Params` — контракт с парсером; без валидации не выполнять. |
| `AiActionPlan.cs` | План действий AI: список шагов `AiActionStep`, статус (`Draft`/`AwaitingConfirmation`/`Executed`/`Cancelled`), `RequestId`/`PlanId`/`SourceMessageId` для защиты от дублей, `RequiresConfirmation`. | План — единственный разрешённый путь изменения заказа через AI; каждое выполнение — один Undo-снимок. |
| `AiOrderContext.cs` | Богатый контекст заказа для LLM: позиции, итоги, клиент, дополнительные КП. | — |
| `AiRequestMetrics.cs` | Метрики запроса: провайдер, модель, номер попытки, fallback, задержки. | — |
| `AiCalculationExplanationContext.cs` | Контекст для объяснения расчёта: фактические позиции, итоги, монтаж, откосы. | Должен строиться из фактических значений, а не догадок AI. |

### 2. ViewModels — ViewModels (логика экранов)

**Где:** `MosquitoNetCalculator/ViewModels/`

| Файл | За что отвечает |
|------|----------------|
| `CalculationViewModel.cs` | Добавление/удаление позиций, подсчёт итогов (TotalInfo). Главная VM расчётов. |
| `MainWindowViewModel.cs` | Общая VM главного окна: цены, печать, экспорт. |
| `OrdersHistoryViewModel.cs` | Загрузка, сохранение, экспорт, импорт заказов. |
| `PricesViewModel.cs` | Загрузка, сохранение, сброс цен. |

### 3. Services — сервисы (бизнес-логика)

**Где:** `MosquitoNetCalculator/Services/`

| Файл | За что отвечает | Что нельзя менять без осторожности |
|------|----------------|-----------------------------------|
| `PriceService.cs` | Загрузка/сохранение цен из prices.json, поиск цены по Name+Color. | DefaultPrices (стартовый каталог), ApplyMigrations — влияет на цены всех пользователей. |
| `PrintService.cs` | Тонкий фасад; делегирует печать/экспорт в `FlowDocumentBuilder`, `DrawingService`, `FixedDocumentBuilder`, `PrintQueueManager`, `PdfExportService`. | HTML-шаблон заменён на `FlowDocument`; формулы заполнения, чертежи и форматирование — это лицо программы для клиента. |
| `FactoryTextService.cs` | Формирование текста "На завод" с группировкой по типам товаров. | Группировка, формат размеров — производство работает по этому тексту. |
| `UpdateService.cs` | Проверка обновлений, скачивание ZIP, верификация SHA-256, запуск watchdog. Оркестрация через `RunUpdateFlowAsync`: тост ручной проверки содержит ТОЧНУЮ причину сбоя манифеста (таймаут/HTTP-код/сеть) вместо обезличенного «Не удалось получить список обновлений». | ManifestUrl, логика сравнения версий, имена файлов — влияет на автообновление. |
| `UpdateManifestClient.cs` | Скачивание releases.json с устойчивостью к сбоям канала: цепочка raw.githubusercontent.com (с cache-bust) → api.github.com/contents (base64-конверт, независимый канал без edge-CDN кэша) → повтор raw → jsDelivr (последний рубеж: не-GitHub CDN, работает при полной блокировке GitHub/«нужен VPN»; задержка кэша замерена 2026-08-30: ≤23 мин, worst case 12 ч по s-maxage, `?t=` не бустит — потому канал последний). Диагностический fetch возвращает причины попыток; загрузка обновления дополнительно требует абсолютный HTTP(S)-URL и выбор явно совпадающей с `latest` записи. | URL каналов, таймаут попытки, формат манифеста — влияет на автообновление всех машин. |
| `WatchdogService.cs` | .bat-скрипт для замены .exe после выхода приложения. | BuildWatchdogBat — ошибка = сломанное обновление. |
| `UpdateLog.cs` | Загрузка истории обновлений из embedded update-log.json. | — |
| `OrderStorageService.cs` | Сохранение/загрузка заказов в JSON в %AppData%. | JsonOptions, пути — влияет на сохранность данных клиентов. |
| `AppSettingsService.cs` | Настройки (тема, префикс договора, точка установки, pending update, вшитый пароль админ-панели, переопределение токена/ID gist, стабильный deviceId через `LoadOrCreateDeviceId` — кросс-процессно безопасно через файл `device-id`). | Пути к %AppData%, формат settings.json — влияет на все пользователи. |
| `OfficeReportService.cs` | Обмен отчётами через секретный GitHub Gist: PATCH своего файла (по устройству `office-{prefix}-{deviceId}.json`) при старте/проверках обновлений/по расписанию, чтение всего gist для панели, очистка дублей устройств (PATCH с content:null для лишних файлов) — вручную кнопкой и АВТОМАТОМ при каждом рефреше панели (`CleanupStaleDuplicatesAsync`: только дубли, молчащие >24 ч, `StaleDuplicateAfter`). | Gist ID/токен — единственная «облачная» точка; токен встраивается в сборку при релизе — в т.ч. в официальные CI-релизы (release.yml: env `OFFICE_REPORT_TOKEN` из секрета). |
| `OfficeDeviceGrouping.cs` | Дедупликация устройств офиса: один ПК = одно устройство (по имени машины, иначе по deviceId), легаси-записи без имени отбрасываются при наличии именованных. | Идентичность устройства — поведение статусов и статистики при нескольких копиях программы на ПК. |
| `OfficeStatusCalculator.cs` | Чистая логика статусов офисов: свежесть отчёта (порог 72ч) + сравнение версий по УСТРОЙСТВАМ (статус офиса агрегируется по свежим устройствам, `DeviceCount`/`Devices`). | Порог свежести и правила статусов — поведение панели. |
| `OfficeStatsCalculator.cs` | Чистая логика секции «Статистика»: кол-во заказов по офисам (сумма по всем устройствам) из тех же отчётов. | — |
| `OfficeReportScheduler.cs` | Периодическая отправка отчёта офиса каждые 2 часа, пока программа открыта (редкие считки: запуск программы + раз в 2 ч). | Контракт Start/Stop/ShouldSendAt — по образцу UpdateCheckScheduler. |
| `AnwisSizeService.cs` | UI-словари для Anwis (метки, подсказки, описания режимов). | Тексты подсказок — не критично, но должны соответствовать формулам в AnwisSize. |
| `AmountInWordsService.cs` | Сумма прописью для КП. | Тексты — влияет на официальный документ. |
| `MoneyFormatService.cs` | Форматирование денежных сумм (разделитель тысяч, копейки). | — |
| `ThemeService.cs` | Переключение и сохранение темы (светлая/тёмная). | — |
| `DialogService.cs` | Fluent-диалоги (подтверждение, ввод, уведомления). | — |
| `ToastService.cs` | Всплывающие тост-уведомления. | — |
| `UndoRedoService.cs` | Undo/Redo через стек снимков. | — |
| `AiAssistantService.cs` | Сетевые вызовы к OpenRouter/NVIDIA: стриминг, ретраи до 3 раз, гарантированный NVIDIA-фолбэк, стандартная модель `openrouter/free` (Free Models Router, иммунитет к кэшу недоступности), авто-обновление каталога при старте и self-heal (второй проход после `forceRefresh` при полном отказе), колбэк `onStreamInfo` с метриками. | Ключи, URL провайдеров, попытки — влияют на доступность AI. |
| `AiCommandParser.cs` | Парсинг JSON-ответа LLM: legacy-формат (single action) и plan-mode (`mode`/`steps`). | Контракт JSON — синхронизировать с промптом LLM. |
| `AiPlanBuilder.cs` | Собирает `AiActionPlan` из команд (одиночной или пакета), строит preview-текст шагов, определяет `RequiresConfirmation`. | Preview должен показывать пользователю, что именно будет выполнено. |
| `AiPlanValidator.cs` | Локальная проверка параметров команд до выполнения (каталог, цвет, размеры, цель обновления/удаления). | Валидация — последний рубеж перед мутацией заказа. |
| `AiPlanExecutor.cs` | Атомарное выполнение плана: применяет шаги через `CommandHandler`, при ошибке откатывает через сохранённый снимок (`RolledBack`). | Один вызов = либо всё применено, либо откат к исходному состоянию. |
| `AiOrderContextBuilder.cs` | Строит `AiOrderContext` из живого состояния заказа (позиции, итоги). | Итоги должны совпадать с `CalculationViewModel.CalculateTotal`. |
| `AiExplanationContextBuilder.cs` | Строит `AiCalculationExplanationContext` (фактические итоги/монтаж/откосы) и текстовые сводки для `/объясни`. | — |
| `AiTelemetryService.cs` | Сбор метрик запросов в памяти: количество запросов, попыток, fallback'ов. | — |
| `AiLocalCommandRouter.cs` | Локальные slash-команды без LLM: `/товары`, `/цены`, `/итоги`, `/статус`, `/последняя`, `/отменить`, `/повторить`, `/очистить`, `/объясни`. | `/очистить` при пустом расчёте отвечает без запроса подтверждения. |

### 4. Controls — пользовательские WPF-контролы

**Где:** `MosquitoNetCalculator/Controls/`

| Файл | За что отвечает |
|------|----------------|
| `QuickAddControl` | Панель быстрого добавления товара (тип, цвет, размеры, цена, режим Anwis). |
| `OrderItemsControl` | Таблица позиций заказа (DataGrid). |
| `SidebarControl` | Боковая панель с данными клиента, Доп.КП, примечания. |
| `ActionBarControl` | Нижняя панель: итоги, кнопки Печать, На завод, Сохранить, Обновления. |
| `TitleBarControl` | Кастомный заголовок окна (кнопки свернуть/развернуть/закрыть). |
| `TotalCardControl` | Карточка итоговой суммы. |
| `OrdersHistoryControl` | Вкладка "Заказы" — список, поиск, импорт/экспорт. |
| `PricesControl` | Вкладка "Цены" — редактирование прайс-листа. |
| `UpdatesTabControl` | Вкладка «Обновления» — история версий + кнопка «Диагностика связи»: пробует три канала (raw, api.github.com, jsDelivr), показывает тайминги/причины и вердикт (кто виноват — программа, блокировка провайдера, «нужен VPN» или сеть ПК). |
| `AdminPanelControl` | Админ-панель — контейнер секций (вкладки): «Обновления» (статусы версий) и «Статистика» (кол-во заказов). Новые секции = новый TabItem + свой список строк. Авто-рефреш каждые 15 мин, пока панель открыта; каждый рефреш шлёт свой отчёт и тихо чистит устаревшие (>24 ч) дубли gist. | Читает gist — без сети показывает "нет связи"; формат отчёта расширяется обратносовместимо. |
| `AdminPasswordWindow` | Окно ввода пароля админ-панели (вшитый пароль, единый для всех офисов). | — |
| `SendToFactoryWindow` | Диалог "Отправить на завод" с чекбоксами. |
| `AdditionalKpsControl` | Блок дополнительных КП. |

### 5. Themes — стили и темы

**Где:** `MosquitoNetCalculator/Themes/`

| Файл | За что отвечает |
|------|----------------|
| `Brushes.xaml` | Цветовая палитра (светлая + тёмная тема). |
| `ButtonStyles.xaml`, `CardStyles.xaml`, `DataGridStyles.xaml`, etc. | Стили для всех элементов интерфейса. |

### 6. Resources — встроенные ресурсы

**Где:** `MosquitoNetCalculator/Resources/`

| Файл | За что отвечает |
|------|----------------|
| `print_template.html` | HTML-шаблон для печати КП. |
| `update-log.json` | История изменений для вкладки "Обновления". |
| `app_icon.ico` | Иконка приложения. |

### 7. Tests — юнит-тесты

**Где:** `MosquitoNetCalculator.Tests/`

| Файл | Что тестирует |
|------|--------------|
| `CalculationViewModelTests.cs` | Добавление позиций, расчёт итогов. |
| `OrderItemTests.cs` | Формулы расчёта, Anwis-режимы, монтаж. |
| `AnwisSizeTests.cs` | Формулы коррекции размеров Anwis. |
| `AnwisContextMenuBuilderTests.cs` | UI-меню Anwis. |
| `PrintServiceTests.cs` | Генерация FlowDocument КП, чертежи, PDF-экспорт. |
| `FactoryTextServiceTests.cs` | Текст "На завод". |
| `UpdateServiceTests.cs` | Парсинг версий, fallback'ы. |
| `UpdateLogTests.cs` | Загрузка истории обновлений. |
| `PriceServiceTests.cs` | Загрузка цен, миграции. |
| `OrderStorageServiceTests.cs` | Сохранение/загрузка заказов. |
| `AppSettingsServiceTests.cs` | Настройки. |
| `ManualChecklistTests.cs` | Интеграционные проверки. |
| `AiAssistantViewModelTests.cs` | VM AI-ассистента: отправка, стриминг, `SubmitClarificationForm`, план→подтверждение→выполнение, slash-команды не блокируют композер. |
| `AiClarificationFormTests.cs` | Модель формы уточнения: списки, валидация, сборка команды. |
| `AiPlanValidatorTests.cs` | Валидатор плана: каталог, цвета, размеры, цели обновления/удаления. |
| `AiPlanExecutorTests.cs` | Атомарное выполнение и rollback при ошибке. |
| `AiLocalCommandRouterTests.cs` | Slash-команды: маршрутизация, форматирование итогов. |
| `AiOrderContextBuilderTests.cs` | Сборка контекста заказа из позиций и итогов. |
| `AiTelemetryServiceTests.cs` | Метрики запросов. |
| `AiExplanationContextTests.cs` | Контекст и тексты объяснения расчёта. |
| `AiCommandParserPlanModeTests.cs` | Парсинг plan-mode JSON (steps, batch). |
| `AiGoldenCaseTests.cs` | Golden-кейсы реальных фраз менеджеров из `AI/golden-cases.json`. |
| `OfficeStatusCalculatorTests.cs` | Логика статусов офисов (свежесть, сравнение версий, неизвестные офисы). |
| `OfficeReportServiceTests.cs` | Парсинг ответа gist + HTTP-обмен (фейковый handler, Bearer-токен) + автоочистка устаревших дублей (`ComputeStaleDuplicateFilesToDelete`/`CleanupStaleDuplicatesAsync`). |
| `OfficeAdminPasswordTests.cs` | Вшитый пароль админ-панели. |
| `OfficeReportSchedulerTests.cs` | Периодическая отправка: интервалы, Start/Stop, fire-once контракт. |

## Source files

- Вся структура `MosquitoNetCalculator/` и `MosquitoNetCalculator.Tests/`.

## Last verified
2026-08-30 (v3.48.7) — auto-synced from csproj (sync-version.ps1, CONTROL#13).

2026-08-30 (v3.48.6) — auto-synced from csproj (sync-version.ps1, CONTROL#13).

2026-08-30 (v3.48.4) — AdminPanelControl: авто-рефреш 15 мин; OfficeReportService: автоочистка устаревших (>24 ч) дублей gist; release.yml: OFFICE_REPORT_TOKEN в Publish.


2026-08-04 — AI Agent Mode: добавлены модули плана и сервисы AI-агентности (`AiActionPlan`, `AiPlanBuilder`/`AiPlanValidator`/`AiPlanExecutor`, `AiOrderContextBuilder`, `AiExplanationContextBuilder`, `AiTelemetryService`, `AiLocalCommandRouter`); карта модулей расширена моделями AI-контекста и тестами.

2026-06-27
