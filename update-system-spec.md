# Спецификация: Замена Velopack на кастомную систему автообновления

**Дата:** 2026-06-22
**Статус:** Draft
**Версия приложения:** 3.33.0 (текущая) → 3.34.0 (после реализации)
**GitHub репозиторий:** https://github.com/DdepRest/arc-frame

---

## 1. Цель

Полностью удалить Velopack из проекта и заменить его на лёгкую кастомную систему автообновления. Приложение — WPF .NET 8 self-contained (win-x64).

**Хостинг (финальное решение):** GitHub Releases — бесплатно, без кредитной карты, без обязательной 2FA, прямые download URL, идеально для бинарных обновлений.

**Резерв-варианты (отклонены):**
- ❌ Cloudflare R2 — требует кредитную карту
- ❌ Backblaze B2 — требует $1 верификацию для Public bucket
- ❌ Firebase Hosting — требует Google аккаунт с 2FA

---

## 2. Архитектура системы

### 2.1 Хостинг: GitHub Releases

| Параметр | Значение |
|----------|----------|
| Провайдер | GitHub Releases |
| GitHub Owner | `DdepRest` |
| GitHub Repo | `arc-frame` |
| Repo URL | https://github.com/DdepRest/arc-frame |
| Бесплатный тир | Public repo: безлимит по storage и bandwidth, 2 ГБ на файл |
| Manifest URL | https://raw.githubusercontent.com/DdepRest/arc-frame/main/releases.json |
| Latest API | https://api.github.com/repos/DdepRest/arc-frame/releases/latest |
| Asset URL | https://github.com/DdepRest/arc-frame/releases/latest/download/ARC-Frame-{version}-full.zip |
| Auth | Personal Access Token (PAT), scope `repo`, передается через env var `%GITHUB_TOKEN%` |

### 2.2 Структура репозитория `DdepRest/arc-frame`

```
repo DdepRest/arc-frame/                            (Public)
├── README.md                                       (описание приложения)
├── releases.json                                   (manifest, коммитится при каждом релизе)
├── .gitignore                                      (node_modules, firebase и т.п. — не наш случай)
└── Releases (по тегам):
    └── v3.34.0 — release
        └── ARC-Frame-3.34.0-full.zip               (asset — бинарный архив)
    └── v3.33.0 — release
        └── ARC-Frame-3.33.0-full.zip
```

⚠️ **Важно:** `releases.json` лежит в репозитории как обычный файл (не как release asset), чтобы:
- легко обновлять через `git commit`
- `raw.githubusercontent.com` отдаёт его мгновенно
- каждая версия приложения содержит ссылку на актуальный manifest в своём коде

### 2.3 Манифест `releases.json`

```json
{
  "latest": "3.34.0",
  "minRequired": "3.30.0",
  "releases": [
    {
      "version": "3.34.0",
      "date": "2026-06-22",
      "type": "Улучшение",
      "title": "Новая система автообновления (без Velopack)",
      "changes": [
        "Полная замена Velopack на кастомную систему автообновления",
        "ZIP-архивы скачиваются напрямую с GitHub Releases",
        "Резервное копирование .exe перед обновлением"
      ],
      "url": "https://github.com/DdepRest/arc-frame/releases/download/v3.34.0/ARC-Frame-3.34.0-full.zip",
      "size": 83886080,
      "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    }
  ]
}
```

**Поля:**

| Поле | Тип | Описание |
|------|-----|----------|
| `latest` | string | Версия последнего релиза (для быстрого сравнения) |
| `minRequired` | string | Минимальная версия для автообновления. Пустая = без ограничений |
| `releases[].version` | string | SemVer |
| `releases[].date` | string | YYYY-MM-DD |
| `releases[].type` | string | `Новинка` / `Улучшение` / `Исправление` |
| `releases[].title` | string | Краткое описание релиза |
| `releases[].changes` | string[] | Список изменений |
| `releases[].url` | string | URL архива для этой версии |
| `releases[].size` | long | Размер для progress bar |
| `releases[].sha256` | string | SHA-256 для верификации |

### 2.4 Формат ZIP-архива

`ARC-Frame-3.34.0-full.zip` содержит:
- `MosquitoNetCalculator.exe` (основной, single-file)
- `.dll` зависимости (если не full-single-file)
- НЕ содержит: settings.json, prices.json, orders/ (живут в %AppData%)

---

## 3. Процесс обновления (клиент)

### 3.1 Этапы

```
App startup → Fetch manifest (raw.githubusercontent.com) → Compare version
                  → If newer: Toast «Доступна версия» → User clicks «Обновить»
                  → Download ZIP → SHA-256 verify
                  → Backup current .exe → .exe.bak
                  → Watchdog .bat → Start new .exe → Shutdown old
                  → (Next start) → Self-test → Delete .exe.bak if OK
                  → (If crashed) → Watchdog restores from .bak
```

### 3.2 Детальная реализация каждого этапа

**Этап 1: Проверка (при запуске + по кнопке)**

```csharp
const string ManifestUrl = "https://raw.githubusercontent.com/DdepRest/arc-frame/main/releases.json";

HttpResponseMessage resp = await httpClient.GetAsync(ManifestUrl, HttpCompletionOption.ResponseHeadersRead);
if (!resp.IsSuccessStatusCode) return null;  // тихая ошибка
var json = await resp.Content.ReadAsStringAsync();
var manifest = JsonSerializer.Deserialize<UpdateManifest>(json);

// Сравнение версий
Version current = Assembly.GetEntryAssembly().GetName().Version;
Version latestVersion = Version.Parse(manifest.Latest);
if (latestVersion > current) {
    AppSettingsService.SavePendingUpdateVersion(manifest.Latest);
    return manifest.Releases[0];  // первый релиз в массиве — самый свежий
}
```

**Этап 2: Скачивание (после подтверждения пользователя)**

```csharp
using var stream = await httpClient.GetStreamAsync(release.Url, HttpCompletionOption.ResponseHeadersRead);
using var fileStream = File.Create(tempPath);
await stream.CopyToAsync(fileStream, progress);  // progress reporter в progress bar

// Verify
string hash = SHA256.HashFile(tempPath);
if (hash != release.Sha256) throw new Exception("Hash mismatch — corrupted");
```

**Этап 3: Установка (горячая замена)**

```csharp
// Backup current EXE
File.Copy("MosquitoNetCalculator.exe", "MosquitoNetCalculator.exe.bak", overwrite: true);

// Create watchdog.bat (next start will check it)
File.WriteAllText("watchdog.bat", WatchdogTemplate);

// Extract ZIP to <temp>
// Copy extracted files, EXCEPT MosquitoNetCalculator.exe (locked!)
// Copy other DLLs now, EXE will be replaced on next start)

// Launch watchdog .bat (waits for our process exit, replaces EXE, starts new EXE)
Process.Start(new ProcessStartInfo("watchdog.bat") { UseShellExecute = true });

// Shutdown
Application.Current.Shutdown();
```

**Этап 4: Rollback (при следующем запуске)**

`watchdog.bat` runs before main app:
1. Wait for old .exe to exit
2. Try new .exe with `MosquitoNetCalculator.exe --self-test` (verifies assembly load, exits with code 0 or non-zero)
3. If self-test fails → restore from `.exe.bak`
4. Run new .exe (or restored old one) normally
5. Delete `watchdog.bat` itself

---

## 4. Деплой — одна команда

### 4.1 `build.bat` pipeline

```bat
@echo off
SETLOCAL EnableDelayedExpansion
echo ===============================================
echo   ARC-Frame Build + Deploy to GitHub Releases
echo ===============================================

REM === ENV ===
if "%GITHUB_TOKEN%"=="" (
    echo ERROR: set GITHUB_TOKEN=ghp_... first
    echo Get one at: https://github.com/settings/tokens
    exit /b 1
)
set GITHUB_OWNER=DdepRest
set GITHUB_REPO=arc-frame

REM === 1. Read version from .csproj ===
for /f "tokens=*" %%i in ('dotnet msbuild MosquitoNetCalculator\MosquitoNetCalculator.csproj -getProperty:Version -nologo') do set "APP_VERSION=%%i"
echo App version: %APP_VERSION%
echo.

REM === 2. Publish app ===
echo Publishing...
dotnet publish MosquitoNetCalculator\MosquitoNetCalculator.csproj ^
    -c Release -r win-x64 --self-contained ^
    -p:PublishSingleFile=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:PublishTrimmed=true ^
    -o publish --nologo
if %errorlevel% neq 0 goto :end
echo.

REM === 3. Extract release notes from UpdateLog.cs ===
powershell -NoProfile -ExecutionPolicy Bypass -File extract-release-notes.ps1 ^
    -SourceFile "MosquitoNetCalculator\Services\UpdateLog.cs" ^
    -OutputFile "publish\release-notes.md" ^
    -ExpectedVersion "%APP_VERSION%" 2>nul
echo.

REM === 4. Create ZIP ===
echo Creating ZIP archive...
powershell -NoProfile -Command "Compress-Archive -Force -Path 'publish\MosquitoNetCalculator.exe','publish\*.dll' -DestinationPath 'publish\ARC-Frame-%APP_VERSION%-full.zip'" 2>nul
if not exist "publish\ARC-Frame-%APP_VERSION%-full.zip" (
    echo ERROR: ZIP archive creation failed.
    goto :end
)
echo.

REM === 5. Compute SHA-256 (for manifest) ===
for /f "tokens=*" %%h in ('powershell -NoProfile -Command "(Get-FileHash 'publish\ARC-Frame-%APP_VERSION%-full.zip' -Algorithm SHA256).Hash"') do set "ZIP_SHA=%%h"
set ZIP_SIZE=
for %%s in ('powershell -NoProfile -Command "(Get-Item 'publish\ARC-Frame-%APP_VERSION%-full.zip').Length"') do set ZIP_SIZE=%%s
echo.

REM === 6. Update releases.json ===
powershell -NoProfile -ExecutionPolicy Bypass -File update-releases-json.ps1 ^
    -Repository "%GITHUB_OWNER%/%GITHUB_REPO%" ^
    -Version "%APP_VERSION%" ^
    -Size "%ZIP_SIZE%" ^
    -Sha256 "%ZIP_SHA%"
echo.

REM === 7. Push updated releases.json to repo ===
git add releases.json
git commit -m "release: v%APP_VERSION%"
git push origin main
echo.

REM === 8. Create GitHub Release with ZIP asset ===
gh release create v%APP_VERSION% ^
    "publish\ARC-Frame-%APP_VERSION%-full.zip" ^
    --repo "%GITHUB_OWNER%/%GITHUB_REPO%" ^
    --title "ARC-Frame %APP_VERSION%" ^
    --notes-file "publish\release-notes.md"
if %errorlevel% neq 0 goto :end
echo.

REM === 9. Optional: build Inno Setup installer ===
if exist compile-installer.bat (
    echo Building Inno Setup installer...
    call compile-installer.bat
)
echo.

echo ===============================================
echo   DEPLOY SUCCESSFUL
echo   Release: https://github.com/%GITHUB_OWNER%/%GITHUB_REPO%/releases/tag/v%APP_VERSION%
echo   Manifest: https://raw.githubusercontent.com/%GITHUB_OWNER%/%GITHUB_REPO%/main/releases.json
echo ===============================================

:end
ENDLOCAL
```

### 4.2 Вспомогательные файлы

**`update-releases-json.ps1`** — скрипт обновления манифеста (создаёт новую запись или обновляет существующую):

```powershell
param(
    [Parameter(Mandatory)] [string] $Repository,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [long] $Size,
    [Parameter(Mandatory)] [string] $Sha256
)

$manifestPath = "releases.json"
$manifestUrl = "https://github.com/$Repository/releases/download/v$Version/ARC-Frame-$Version-full.zip"

# Try to fetch existing manifest via git
if (Test-Path $manifestPath) {
    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
} else {
    $manifest = [PSCustomObject]@{
        latest = ""
        minRequired = ""
        releases = @()
    }
}

# Remove existing entry for this version (if any)
$manifest.releases = @($manifest.releases | Where-Object { $_.version -ne $Version })

# Add new release
$newRelease = [PSCustomObject]@{
    version = $Version
    date = (Get-Date -Format "yyyy-MM-dd")
    type = "Улучшение"
    title = "ARC-Frame $Version"
    changes = @()
    url = $manifestUrl
    size = $Size
    sha256 = $Sha256
}
$manifest.releases = @($newRelease) + @($manifest.releases)
$manifest.latest = $Version

# Save
$manifest | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 $manifestPath
Write-Host "Updated manifest for v$Version"
```

---

## 5. Новые/изменённые файлы

### 5.1 `MosquitoNetCalculator/Services/UpdateService.cs` (переписать)

Полностью заменить Velopack-зависимости на HTTP-клиент. Содержит:
- `FetchManifestAsync()` — читает с raw.githubusercontent.com
- `DownloadAndVerifyAsync()` — стримит ZIP, проверяет SHA-256
- `ApplyUpdate()` — backup + extract + launch watchdog
- `CheckOnStartupAsync()` — тихая проверка
- `CheckAndApplyAsync()` — интерактивный flow

(API классов сохранён — UI не сломается)

### 5.2 `MosquitoNetCalculator/Models/UpdateManifest.cs` (новый)

DTO:
- `class UpdateManifest { string Latest, MinRequired, List<ReleaseInfo> Releases }`
- `class ReleaseInfo { string Version, Date, Type, Title, List<string> Changes, Url, Size, Sha256 }`

### 5.3 `MosquitoNetCalculator/Services/WatchdogService.cs` (новый)

- `CreateWatchdogBat()` — генерирует .bat-посредник перед обновлением
- `HandleStartup()` — вызывается из App.OnStartup, проверяет есть ли watchdog.bat
- `--self-test` режим — вход в App.OnStartup проверяет флаг, валидирует сборку и выходит

### 5.4 `MosquitoNetCalculator/App.xaml.cs` (изменить)

- Удалить `Velopack.VelopackApp.Build().Run()`
- Добавить `WatchdogService.HandleStartup()` (вместо него)
- Остальное — без изменений

### 5.5 `MosquitoNetCalculator/Services/AppSettingsService.cs` (изменить)

Удалить:
- `IsFlowEnabled()`, `SetFlowEnabled()`, поле `UseVelopackFlow`

Оставить:
- `LoadUpdateUrl()`, `SaveUpdateUrl()`, `LoadPendingUpdateVersion()`, `SavePendingUpdateVersion()`

### 5.6 `MosquitoNetCalculator/Controls/ActionBarControl.xaml(.cs)` (изменить)

Удалить:
- `<MenuItem x:Name="MenuFlowToggle" ... />`
- Обработчик `MenuFlowToggle_Click`
- `IsChecked` синхронизацию

В UpdateSettingsMenu() убрать строку про Velopack toggle.

### 5.7 `MosquitoNetCalculator.csproj` (изменить)

Удалить:
```xml
<PackageReference Include="Velopack" Version="1.2.0" />
```

### 5.8 `installer.iss` (изменить)

Удалить:
- velopack-setup.exe из Sources
- блок Velopack в CurStepChanged
- cleanup-velopack в UninstallRun

Оставить:
- все проверки deps (.NET, WebView2, VC++)
- ярлыки (теперь всегда → {app}, не динамические)

### 5.9 Удалить файлы

- `deploy-flow.bat`
- `tools/FlowProbe/`

---

## 6. Безопасность

| Мера | Реализация |
|------|-----------|
| Целостность ZIP | SHA-256 после скачивания, mismatch → отмена |
| HTTPS | Все API через HTTPS, GitHub CDN |
| MITM защита | GitHub certificate pinning (опционально, не критично для MVP) |
| Rollback | `.exe.bak` + watchdog .bat |
| Min version | Поле `minRequired` в манифесте |
| GitHub PAT | Только через env var `%GITHUB_TOKEN%`, не в .bat файле |

⚠️ **Важно о PAT:**
- Использовать **Fine-grained PAT** с scope только `Contents: Read and Write` для `DdepRest/arc-frame`
- Expiration: 30-90 дней
- После реализации — отозвать и сгенерировать новый

---

## 7. Миграция с Velopack

### 7.1 Для пользователей с Velopack установкой

1. Старая версия с Velopack продолжает работать (если не обновится)
2. После первого запуска новой версии без Velopack — `VelopackApp.Build().Run()` отсутствует
3. `%LocalAppData%\ARC-Frame\` папка Velopack — оставляем (можно удалять вручную если хочется)
4. В settings.json поле `UseVelopackFlow` тихо игнорируется
5. Новая версия проверяет обновления через GitHub Releases

### 7.2 Чистая установка

1. Inno Setup → устанавливает .exe в `{app}`
2. Нет Velopack компонентов
3. Первый запуск → создаёт `%AppData%\MosquitoNetCalculator\`
4. Автообновление начинает работать сразу

---

## 8. Структура и порядок внедрения

| Шаг | Файл | Действие |
|-----|------|----------|
| 1 | `Models/UpdateManifest.cs` | Создать новый файл |
| 2 | `Services/UpdateService.cs` | Полностью переписать (HTTP, без Velopack) |
| 3 | `Services/WatchdogService.cs` | Создать новый файл |
| 4 | `Services/AppSettingsService.cs` | Удалить Velopack-методы |
| 5 | `App.xaml.cs` | Удалить VelopackApp, добавить WatchdogService.HandleStartup() |
| 6 | `Controls/ActionBarControl.xaml(.cs)` | Удалить MenuFlowToggle |
| 7 | `MosquitoNetCalculator.csproj` | Удалить PackageReference Velopack |
| 8 | `installer.iss` | Удалить Velopack logic |
| 9 | `deploy-flow.bat`, `tools/FlowProbe/` | Удалить файлы |
| 10 | `build.bat` | Добавить GitHub Release step |
| 11 | `extract-release-notes.ps1` | Уже существует, проверить |
| 12 | `update-releases-json.ps1` | Создать новый скрипт |
| 13 | `releases.json` | Инициализировать пустой манифест |
| 14 | Тесты (UpdateServiceTests, AppSettingsServiceTests) | Обновить под новый API |
| 15 | `UpdateLog.cs` | Добавить entry про миграцию |

---

## 9. Размер приложения

| Параметр | Текущий | Целевой |
|----------|---------|---------|
| Publish | self-contained, single-file, compressed | + trimmed |
| Размер | ~141 МБ | ~80-100 МБ |
| ZIP | N/A | ~70-90 МБ |

⚠️ **Trim может сломать reflection** — нужно проверить `AnwisSizeMode`, `ThemeService`, dynamic JSON loading.

---

## 10. Тестирование

### 10.1 Юнит-тесты (обновить)

- **`UpdateServiceTests.cs`** — mock HttpClient, тестировать парсинг манифеста, сравнение версий, обработку ошибок сети
- **`WatchdogServiceTests.cs`** — новый, проверять генерацию .bat и self-test логику
- **`AppSettingsServiceTests.cs`** — обновить, удалить тесты для Velopack методов

### 10.2 Интеграционные тесты

1. Чистая установка через Inno Setup → запустить
2. AppData миграция выполнилась
3. Запустить app → toast «Доступна новая версия» на тестовом релизе
4. Нажать «Обновить» → скачать → установить → перезапустить
5. Скачать повреждённый ZIP → убедиться что .bak восстанавливается

---

## 11. Точки принятия решений (требуют ответа)

- [ ] `releases.json` хранить в репозитории (текущий план) или как release asset (усложняет обновление)
- [ ] Один ZIP = весь .exe + DLLs, или отдельно .exe и DLL (для дельт в будущем)
- [ ] Тег `v3.34.0` или `3.34.0` (с/без `v` префикса)
- [ ] Какой формат `minVersion` — пустая строка или integer?
- [ ] Inno Setup инсталлер — добавить проверку что установленная версия >= minRequired

---

## 12. Текущий статус

| Задача | Статус |
|--------|--------|
| Спецификация | ✅ Завершена |
| GitHub repo создан | ✅ https://github.com/DdepRest/arc-frame |
| gh CLI установлен | ✅ v2.95.0 |
| PAT получен | ❌ Ожидается от пользователя |
| UpdateService (новый) | ❌ Не начат |
| WatchdogService | ❌ Не начат |
| build.bat GitHub deploy | ❌ Не начат |
| UpdateLog entry | ❌ Не добавлена |

**Блокер:** нужен PAT от пользователя для тестирования build.bat end-to-end.
