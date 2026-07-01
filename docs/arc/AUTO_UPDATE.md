# AUTO_UPDATE.md

## Как работает автообновление

Приложение проверяет наличие новой версии на GitHub Releases и автоматически скачивает и устанавливает обновление.

---

## Архитектура

### Обновлённая архитектура (unreleased rework)

```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│  App startup    │────▶│ CheckOnStartup   │────▶│ Fetch manifest  │
│                 │     │ (auto-dialog)    │     │ from GitHub     │
└─────────────────┘     └──────────────────┘     └─────────────────┘
                                                           │
                              ┌────────────────────────────┘
                              ▼
                    ┌──────────────────┐
                    │ GetAvailableUpdate│
                    │ (pure logic)     │
                    └──────────────────┘
                           │
              ┌────────────┴────────────┐
              ▼                         ▼
        ┌──────────┐              ┌──────────┐
        │ Update   │              │ No update│
        │ available│              │ (silent) │
        └────┬─────┘              └──────────┘
             │
             ▼
┌─────────────────────────┐
│ ShowUpdateAvailable()   │
│ (dialog + changelog)    │
└─────────────────────────┘
             │
    ┌────────┴────────┐
    ▼                 ▼
┌────────┐      ┌────────────┐
│ Скачать│      │ Отмена     │
└───┬────┘      │ → pending  │
    │           │   badge    │
    ▼           └────────────┘
┌─────────────────────────┐
│ TitleBar progress bar   │
│ (3px, фоновое скачив.)  │
└─────────────────────────┘
             │
             ▼
┌─────────────────────────┐
│ 1. Verify SHA-256       │
│ 2. Stage update         │
│ 3. Launch watchdog.bat  │
│ 4. Application.Shutdown │
└─────────────────────────┘
             │
             ▼
┌─────────────────────────┐
│ watchdog.bat waits for  │
│ app to exit             │
└─────────────────────────┘
             │
             ▼
┌─────────────────────────┐
│ Extract ZIP             │
│ Self-test new .exe      │
│ Replace .exe            │
│ Launch new version      │
└─────────────────────────┘
```

### Ключевые изменения (update notification rework)

| Аспект | Было | Стало |
|--------|------|-------|
| Уведомление при запуске | Toast "Доступна новая версия" | Автоматический диалог с changelog |
| Диалог | Только версия, без контекста | Changelog пропущенных версий + type-бейджи |
| Прогресс скачивания | `DownloadProgressPanel` в ActionBar | Тонкая полоска (3px) под TitleBar |
| Toast (авто) | 3–4 toast'а подряд | Только ошибки + важные статусы |
| Отклонённое обновление | Забывалось до ручной проверки | Показывается снова при каждом запуске |

---

## Где находится код

| Компонент | Файл |
|-----------|------|
| Проверка обновлений | `MosquitoNetCalculator/Services/UpdateService.cs` |
| Диалог обновления | `MosquitoNetCalculator/Services/DialogService.cs` |
| Changelog (embedded) | `MosquitoNetCalculator/Services/UpdateLog.cs` |
| TitleBar-прогресс | `MosquitoNetCalculator/MainWindow.xaml` + `.xaml.cs` |
| Watchdog (.bat) | `MosquitoNetCalculator/Services/WatchdogService.cs` |
| Манифест релизов | `releases.json` (в корне репозитория) |
| Настройки обновления | `MosquitoNetCalculator/Services/AppSettingsService.cs` |
| Интеграционные тесты | `MosquitoNetCalculator.Tests/Services/UpdateServiceIntegrationTests.cs` |

---

## GitHub URL/API

**Манифест (используется программой в runtime):** `https://raw.githubusercontent.com/DdepRest/arc-frame/main/releases.json`

> ⚠️ Этот URL кэшируется на GitHub CDN. После `git push` файла в `main` обновление видно на этом URL через **5-15 минут** (диапазон совпадает с `docs/arc/RELEASE_PROCESS.md`). Если нужна нулевая задержка диагностики — используйте `https://api.github.com/repos/DdepRest/arc-frame/contents/releases.json` (НЕ кэшируется, но требует авторизации для частых запросов).

**Диагностический endpoint (НЕ кэшируется):** `https://api.github.com/repos/DdepRest/arc-frame/contents/releases.json`

Это публичный raw-URL к `releases.json` в репозитории.

**Формат манифеста (`UpdateManifest`):**
```json
{
    "latest": "3.35.0",
    "minRequired": "",
    "releases": [
        {
            "version": "3.35.0",
            "date": "2026-06-23",
            "type": "Исправление",
            "title": "...",
            "changes": ["..."],
            "url": "https://github.com/DdepRest/arc-frame/releases/download/v3.35.0/ARC-Frame-3.35.0-full.zip",
            "size": 66659897,
            "sha256": "..."
        }
    ]
}
```

---

## Как программа получает последний релиз

1. Скачивает `releases.json` через `HttpClient` (таймаут 15 сек).
2. Десериализует в `UpdateManifest`.
3. Вызывает `GetAvailableUpdate(manifest, CurrentVersion)` — pure-метод, возвращает `ReleaseInfo?` если обновление доступно.
4. Если `null` — приложение up-to-date или манифест невалиден.

`GetAvailableUpdate` вынесен как `internal static` для юнит-тестирования (не зависит от UI).

**Инъекция `HttpClient` для тестирования (unreleased):**
`FetchManifestAsync` и `DownloadWithProgressAsync` теперь принимают опциональный `HttpClient? httpClient = null`.
Паттерн `ownsClient` (`httpClient == null ? создать : использовать переданный`) гарантирует, что внешний клиент (например, из теста с `MockHttpMessageHandler`) не будет `Dispose`'нут. Production-настройки (timeout 15 сек, User-Agent) применяются только к самоуправляемому `HttpClient`.

---

## ⚠️ `releases.json` — это рубильник автообновления

Файл `releases.json` живёт в ветке `main` и читается напрямую с `raw.githubusercontent.com`. Публикация новой версии в нём **необратимо** запускает обновление у всех пользователей. Если в `releases.json` указана версия, а соответствующий ZIP ещё не загружен в GitHub Release — пользователи получат ошибку скачивания.

> ⚠️ **Канонический дом:** полный release pipeline, ⚠️ правило безопасности, 4 этапа (ZIP → GitHub Release → push `releases.json`) и git push sequence — в `docs/arc/RELEASE_PROCESS.md` (раздел «Канонический Pipeline релиза»).
> Этот файл описывает только **runtime-поведение манифеста** (как программа его получает, поведение CDN-кэша, диагностику «не видит обновление») — НЕ процедуру релиза.

---

## Как сравниваются версии

```csharp
ReleaseInfo? release = GetAvailableUpdate(manifest, CurrentVersion);
if (release == null)
    return; // Нет обновления или манифест невалиден
```

`GetAvailableUpdate` проверяет:
1. `manifest != null && manifest.Releases.Count > 0`
2. `ParseSafe(manifest.Latest) != null`
3. `latestVersion > currentVersion`

Возвращает `manifest.Releases[0]` (новейший релиз, предполагается newest-first).

`CurrentVersion` — это `UpdateService.TryResolveCurrentVersion()`, который читает `AssemblyInformationalVersionAttribute`.

**Важно:** Версия в `.csproj` (`<Version>3.35.0</Version>`) = единственный источник правды.

---

## Диалог с changelog

При обнаружении новой версии показывается `DialogService.ShowUpdateAvailable()`:

1. **Заголовок:** badge с номером версии (`Accent` background).
2. **Changelog:** `ScrollViewer` (max 220px) с компактными карточками версий:
   - Type-бейдж с цветовой кодировкой: `Новинка` → Success (зелёный), `Исправление` → Danger (красный), остальное → Warning (оранжевый).
   - Заголовок версии (bold bullet) + список изменений.
   - Данные берутся из `UpdateLog.GetChangesSince(CurrentVersion)` — фильтрует embedded `update-log.json` по версии, возвращает хронологический порядок (старая → новая).
3. **Fallback:** если changelog пуст — показывается "Список изменений недоступен".
4. **Кнопки:** "Отмена" + "Скачать и установить".

**Что показывать:** при обновлении с 3.34.0 → 3.36.2 показываются изменения **всех** пропущенных версий (3.34.1, 3.35.0, 3.36.0, 3.36.1, 3.36.2).

---

## TitleBar-полоска прогресса

Вместо `DownloadProgressPanel` в ActionBar используется тонкая полоска под TitleBar:

- **Расположение:** `MainWindow.xaml`, строка Grid `Auto` (3px) между TitleBar и TabControl.
- **Стиль:** `Height="3"`, `BorderThickness="0"`, `Background="Transparent"`, `Foreground="{DynamicResource Accent}"`.
- **Видимость:** только при `UpdateService.IsDownloading == true`.
- **Подписка:** `UpdateService.ProgressChanged` в `MainWindow.xaml.cs`.
- **Анимация:** fade-in (200 мс, CubicEase EaseOut) и fade-out (250 мс, CubicEase EaseIn) реализованы через `Storyboard` ресурсы в `Grid.Resources` `MainWindow.xaml`. Code-behind использует `FindResource` + `Clone()` для запуска. `From` для fade-out задаётся динамически из текущего `Opacity`, чтобы избежать визуального скачка при прерывании fade-in (например, быстрое завершение скачивания).

Пользователь может закрыть диалог во время скачивания — загрузка продолжается в фоне, прогресс виден в полоске.

---

## Toast-фильтрация

Флаг `isAutomatic` в `CheckAndApplyAsync` / `RunUpdateFlowAsync` различает авто- и ручную проверку:

| Toast | Авто | Ручная |
|-------|------|--------|
| "Проверка наличия обновлений..." | ❌ Убран | ✅ Оставлен |
| "Доступна новая версия..." | ❌ Заменён диалогом | ❌ Заменён диалогом |
| "Обновлений нет ✓" | ❌ Молча | ✅ Success |
| Ошибка получения манифеста (сеть, парсинг) | ❌ Молча | ✅ Warning toast |
| Ошибка скачивания / SHA-256 | ✅ Toast Error | ✅ MessageBox Error |
| "Проверка целостности..." | ✅ Info | ✅ Info |
| "Установка обновления..." | ✅ Info | ✅ Info |

При `isAutomatic == true` ошибки скачивания/проверки показываются через `ToastService.Error` вместо `MessageBox`.

---

## Какой файл скачивается

`ReleaseInfo.Url` → ZIP-файл с GitHub Releases.

Пример: `ARC-Frame-3.35.0-full.zip`

Внутри ZIP:
- `MosquitoNetCalculator.exe` (single-file)
- Зависимые DLL (WebView2 и др.)

**Zero-byte edge case (unreleased fix):**
Если сервер возвращает `Content-Length: 0` или не возвращает заголовок, `DownloadWithProgressAsync` теперь корректно отчитывает 100% прогресс после завершения скачивания. Ранее полоска оставалась на 0%, потому что тело цикла `while` не выполнялось, а финальный `Report(100)` не вызывался.

---

## Как запускается установка

1. `UpdateService.CheckAndApplyAsync` скачивает ZIP во временную папку (`%TEMP%\arc-update-{version}-{guid}.zip`).
2. Проверяет SHA-256.
3. Вызывает `WatchdogService.StageUpdate(tempZipPath)`:
   - Пишет `arc-update-watchdog.bat` рядом с .exe.
   - Копирует ZIP как `arc-update.zip`.
   - Делает бэкап текущего `.exe` → `.exe.bak`.
4. Запускает `watchdog.bat` с `UseShellExecute = true`.
5. Вызывает `Application.Current.Shutdown()`.

**Watchdog .bat делает:**
1. Ждёт до 120 секунд, пока приложение закроется.
2. Распаковывает ZIP во временную папку через PowerShell `Expand-Archive`.
3. Запускает новый .exe с `--self-test`.
4. Если self-test прошёл (exit code 0):
   - Копирует новые файлы в BaseDirectory.
   - Удаляет ZIP, .bak, .bat.
   - Запускает обновлённое приложение.
5. Если self-test НЕ прошёл:
   - Удаляет ZIP.
   - Запускает старый .exe (из .bak или текущий).

---

## Как программа перезапускается после обновления

Watchdog .bat запускает обновлённый `MosquitoNetCalculator.exe` через `start "" "%HERE%%EXE%"`.

При следующем запуске `App.OnStartup` → `WatchdogService.HandleStartup` удаляет оставшийся `watchdog.bat` (если предыдущий update крашнулся).

---

## Риски

| Риск | Описание | Митигация |
|------|----------|-----------|
| GitHub недоступен | Не скачается releases.json | Тост-уведомление, повторная проверка вручную |
| ZIP повреждён | SHA-256 не совпадёт | Проверка хеша перед установкой |
| Новый .exe не запускается | Self-test провалится | Откат к `.exe.bak`, запуск старой версии |
| Watchdog не удалился | `.bat` остался после краша | Очистка при следующем запуске |
| Версия в .csproj ≠ releases.json | Обновление не увидят | Всегда синхронизировать оба файла |
| Single-file publish + версия | `GetName().Version` = null | Многослойный fallback (v3.34.5) |

---

## Как вручную проверить автообновление

1. Собрать проект (`build.bat`).
2. Временно понизить версию в `.csproj` (например, `3.34.0`).
3. Запустить приложение.
4. Убедиться, что **диалог обновления** появился автоматически (с changelog).
5. Нажать "Скачать и установить" → убедиться, что TitleBar-полоска показывает прогресс.
6. Закрыть диалог во время скачивания → убедиться, что полоска остаётся видна.
7. Дождаться перезапуска и проверить новую версию.
8. **Важно:** Вернуть версию в `.csproj` обратно перед коммитом!

---

## Source files

- `MosquitoNetCalculator/Services/UpdateService.cs`
- `MosquitoNetCalculator/Services/DialogService.cs`
- `MosquitoNetCalculator/Services/UpdateLog.cs`
- `MosquitoNetCalculator/Services/WatchdogService.cs`
- `MosquitoNetCalculator/Services/AppSettingsService.cs`
- `MosquitoNetCalculator/MainWindow.xaml`
- `MosquitoNetCalculator/MainWindow.xaml.cs`
- `MosquitoNetCalculator/Models/UpdateManifest.cs`
- `MosquitoNetCalculator/Models/UpdateItem.cs`
- `releases.json`
- `build.bat`
- `MosquitoNetCalculator.Tests/Services/UpdateServiceIntegrationTests.cs`

---

## Idle/Periodic background checks (`UpdateCheckScheduler`)

В дополнение к startup-проверке и ручной проверке из меню Настроек, приложение теперь проверяет обновления в фоне по двум стратегиям:

| Стратегия | Триггер | Поведение |
|-----------|---------|-----------|
| **Periodic** | Каждые 30 мин от последней проверки | Независимо от активности пользователя. Production-default: `CheckInterval = 30 min`. |
| **Idle**     | 10 мин непрерывного простоя | `MainWindow.PreviewMouseMove` / `PreviewKeyDown` сбрасывают таймер. Production-default: `IdleThreshold = 10 min`. |
| **Throttle** | Минимум 2 мин между двумя реальными проверками | Анти-спам: periodic + idle одновременно сработать два раза подряд не могут. Production-default: `MinGap = 2 min`. |

### Архитектура

```
┌────────────────────┐   ┌──────────────────────┐   ┌────────────────────┐
│ DispatcherTimer     │──▶│ ShouldCheckAt()      │──▶│ OnCheckDue (Func
│ TickInterval=60 sec │   │ — pure logic         │   │  <Task>)            │
└────────────────────┘   │ — throttle/periodic/ │   └────────────────────┘
        ▲                │   idle gates          │            │
        │                └──────────────────────┘            ▼
        │                                            ┌──────────────────────┐
        │                                            │ UpdateService.       │
        │                                            │   CheckInBackgroundAsync
        │                                            └──────────────────────┘
        │                                                      │
        │                                                      ▼
        │                                            ┌──────────────────────┐
        │                                            │ ToastService.        │
        │                                            │   ShowUpdateNotify   │
        │                                            │ — persistent toast  │
        │                                            │ — [Обновить][Позже] │
        │                                            └──────────────────────┘
        │
┌──────────────────────────────────────────────────────────────────┐
│ Activity tracking:                                                │
│ • MainWindow.PreviewMouseMove → NotifyActivity                   │
│ • MainWindow.PreviewKeyDown   → NotifyActivity                   │
└──────────────────────────────────────────────────────────────────┘
```

### Контракт scheduler'а

1. **Start()** идемпотентен. Ставит `_lastCheckTime = Now()`, что подавляет немедленную проверку после первого тика (startup-чек уже отработал в `App.CheckOnStartupAsync`).
2. **ShouldCheckAt(now)** — pure-метод. Алгоритм:
   1. `now - _lastCheckTime < MinGap` → **throttle**, false.
   2. `now - _lastCheckTime ≥ CheckInterval` → **periodic**, true.
   3. `now - _lastActivityTime ≥ IdleThreshold` → **idle**, true.
   4. Иначе false.
3. **ShouldSkipCheck** callback (`Func<bool>`) — production-wire: `() => UpdateService.IsChecking || UpdateService.IsDownloading`. Если возвращает `true`, tick игнорируется (не запускаем вторую параллельную проверку).
4. **OnCheckDue** callback (`Func<Task>`) — production-wire: `() => UpdateService.CheckInBackgroundAsync()`. Scheduler вызывает его через fire-and-forget после `MarkChecked()`. Синхронные броски безопасны — обёртка `SafeInvoke` в scheduler'е логирует исключения в `Debug.WriteLine`.

### Уведомление в фоне (`ShowUpdateNotification`)

Плашка появляется в правом нижнем углу и **не исчезает сама** — пользователь обязан выбрать одно из двух действий:

| Действие | Эффект |
|----------|--------|
| **Обновить** | `CloseAndDispatch`: сначала `RemoveToast(toast)`, потом `CheckAndApplyAsync(owner, isAutomatic: false)` → existing modal-dialog from `DialogService.ShowUpdateAvailable` (с changelog) → download. После успешного pending обнуляется, `_lastNotifiedVersion = null` (чтобы при ошибке загрузки следующий tick смог предложить снова). |
| **Позже** | `CloseAndDispatch`: `RemoveToast`, nothing more. Pending остаётся в settings.json — следующая фоновая проверка НЕ покажет плашку снова для того же релиза (guard `if (_lastNotifiedVersion == release.Version) return;` в `CheckInBackgroundAsync`). |

**Известное ограничение:** `ToastCanvas.IsHitTestVisible="False"` (в `MainWindow.xaml`) пробрасывает наследуемое свойство вниз по дереву и блокирует клики на всех children. Notification toast явно ставит `toast.IsHitTestVisible = true` — НЕ удаляйте эту строку, иначе кнопки «Обновить» / «Позже» перестанут получать `Click`.

### Анти-флуд

- `_lastNotifiedVersion` (static в `UpdateService`) — гарантирует, что плашка не показывается дважды для одной версии за сессию.
- `MinGap` (production 2 мин) — не позволяет двум стратегиям стартовать две проверки подряд.
- Periodic в любом случае срабатывает раз в 30 мин даже если пользователь весь день кликает мышью. Idle отрабатывает только если пользователь действительно отошёл.

### Source files (new + changed)

- `MosquitoNetCalculator/Services/UpdateCheckScheduler.cs` (NEW)
- `MosquitoNetCalculator/Services/UpdateService.cs` (`CheckInBackgroundAsync` + `_lastNotifiedVersion`)
- `MosquitoNetCalculator/Services/ToastService.cs` (`ShowUpdateNotification` + `ShowUpdateNotification` constants)
- `MosquitoNetCalculator/MainWindow.xaml.cs` (scheduler lifecycle + activity hooks)
- `MosquitoNetCalculator.Tests/Services/UpdateCheckSchedulerTests.cs` (NEW, 20+ tests)

## Last verified

2026-07-01 (v3.41.0 release run: CDN-cache diagnostic + api.github.com endpoint documented for «не видит обновление» support)
