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
| `PrintService.cs` | Генерация HTML для КП из шаблона, SVG-чертежи для каждого товара. | HTML-шаблон, формулы заполнения, EscapeHtml — это лицо программы для клиента. |
| `FactoryTextService.cs` | Формирование текста "На завод" с группировкой по типам товаров. | Группировка, формат размеров — производство работает по этому тексту. |
| `UpdateService.cs` | Проверка обновлений, скачивание ZIP, верификация SHA-256, запуск watchdog. | ManifestUrl, логика сравнения версий, имена файлов — влияет на автообновление. |
| `WatchdogService.cs` | .bat-скрипт для замены .exe после выхода приложения. | BuildWatchdogBat — ошибка = сломанное обновление. |
| `UpdateLog.cs` | Загрузка истории обновлений из embedded update-log.json. | — |
| `OrderStorageService.cs` | Сохранение/загрузка заказов в JSON в %AppData%. | JsonOptions, пути — влияет на сохранность данных клиентов. |
| `AppSettingsService.cs` | Настройки (тема, префикс договора, точка установки, pending update). | Пути к %AppData%, формат settings.json — влияет на все пользователи. |
| `AnwisSizeService.cs` | UI-словари для Anwis (метки, подсказки, описания режимов). | Тексты подсказок — не критично, но должны соответствовать формулам в AnwisSize. |
| `AmountInWordsService.cs` | Сумма прописью для КП. | Тексты — влияет на официальный документ. |
| `MoneyFormatService.cs` | Форматирование денежных сумм (разделитель тысяч, копейки). | — |
| `ThemeService.cs` | Переключение и сохранение темы (светлая/тёмная). | — |
| `DialogService.cs` | Fluent-диалоги (подтверждение, ввод, уведомления). | — |
| `ToastService.cs` | Всплывающие тост-уведомления. | — |
| `UndoRedoService.cs` | Undo/Redo через стек снимков. | — |

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
| `UpdatesTabControl` | Вкладка "Обновления" — история версий. |
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
| `PrintServiceTests.cs` | Генерация HTML КП. |
| `FactoryTextServiceTests.cs` | Текст "На завод". |
| `UpdateServiceTests.cs` | Парсинг версий, fallback'ы. |
| `UpdateLogTests.cs` | Загрузка истории обновлений. |
| `PriceServiceTests.cs` | Загрузка цен, миграции. |
| `OrderStorageServiceTests.cs` | Сохранение/загрузка заказов. |
| `AppSettingsServiceTests.cs` | Настройки. |
| `ManualChecklistTests.cs` | Интеграционные проверки. |

## Source files

- Вся структура `MosquitoNetCalculator/` и `MosquitoNetCalculator.Tests/`.

## Last verified

2026-06-24
