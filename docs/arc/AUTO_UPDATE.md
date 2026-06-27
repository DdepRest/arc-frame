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

---

## GitHub URL/API

**Манифест:** `https://raw.githubusercontent.com/DdepRest/arc-frame/main/releases.json`

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

---

## ⚠️ `releases.json` — это рубильник автообновления

Файл `releases.json` живёт в ветке `main` и читается напрямую с `raw.githubusercontent.com`.

Это значит:

- Как только `releases.json` опубликован в `main` с новой версией — **все старые программы видят обновление**.
- Если в `releases.json` указана версия, а соответствующий ZIP ещё не загружен в GitHub Release — пользователи получат ошибку скачивания.
- Поэтому `releases.json` и GitHub Release **должны публиковаться синхронно** (или GitHub Release строго раньше).

**Безопасный порядок публикации:**

1. Собрать ZIP.
2. Посчитать SHA-256.
3. Создать GitHub Release и загрузить ZIP-asset.
4. **Только после этого** опубликовать/запушить обновлённый `releases.json` в `main`.

Подробнее — в `docs/arc/RELEASE_PROCESS.md`, раздел «Безопасный порядок публикации».

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

## Last verified

2026-06-27 (update notification rework: RunUpdateFlowAsync, changelog dialog, TitleBar progress, toast filtering, isAutomatic)
