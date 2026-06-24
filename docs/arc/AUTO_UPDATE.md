# AUTO_UPDATE.md

## Как работает автообновление

Приложение проверяет наличие новой версии на GitHub Releases и автоматически скачивает и устанавливает обновление.

---

## Архитектура

```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│  App startup    │────▶│ CheckOnStartup   │────▶│ Fetch manifest  │
│                 │     │ (silent, bg)     │     │ from GitHub     │
└─────────────────┘     └──────────────────┘     └─────────────────┘
                                                           │
                              ┌────────────────────────────┘
                              ▼
                    ┌──────────────────┐
                    │ Compare versions │
                    │ Current vs Latest│
                    └──────────────────┘
                           │
              ┌────────────┴────────────┐
              ▼                         ▼
        ┌──────────┐              ┌──────────┐
        │ Update   │              │ No update│
        │ available│              │          │
        └────┬─────┘              └──────────┘
             │
             ▼
┌─────────────────────────┐
│ User clicks "Update"    │
│ → CheckAndApplyAsync    │
└─────────────────────────┘
             │
             ▼
┌─────────────────────────┐
│ 1. Download ZIP         │
│ 2. Verify SHA-256       │
│ 3. Stage update         │
│ 4. Launch watchdog.bat  │
│ 5. Application.Shutdown │
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

---

## Где находится код

| Компонент | Файл |
|-----------|------|
| Проверка обновлений | `MosquitoNetCalculator/Services/UpdateService.cs` |
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
3. Берёт `manifest.Latest` и `manifest.Releases[0]`.

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
Version? latestVersion = ParseSafe(manifest.Latest);
if (latestVersion == null || latestVersion <= CurrentVersion)
    return; // Нет обновления
```

`CurrentVersion` — это `UpdateService.TryResolveCurrentVersion()`, который читает `AssemblyInformationalVersionAttribute`.

**Важно:** Версия в `.csproj` (`<Version>3.35.0</Version>`) = единственный источник правды.

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
4. Убедиться, что тост "Доступна новая версия" появился.
5. Нажать "Проверить обновления" → скачать → установить.
6. Убедиться, что приложение перезапустилось и показывает новую версию.
7. **Важно:** Вернуть версию в `.csproj` обратно перед коммитом!

---

## Source files

- `MosquitoNetCalculator/Services/UpdateService.cs`
- `MosquitoNetCalculator/Services/WatchdogService.cs`
- `MosquitoNetCalculator/Services/AppSettingsService.cs`
- `MosquitoNetCalculator/Models/UpdateManifest.cs`
- `releases.json`
- `build.bat`

## Last verified

2026-06-23 (версия 3.35.0)
