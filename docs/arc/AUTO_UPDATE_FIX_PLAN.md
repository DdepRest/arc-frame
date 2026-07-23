# AUTO_UPDATE — План исправлений (v3.47.2 → v3.48.x)

> Многоэтапный план устранения 9 проблем, выявленных в brainstorm-анализе
> (`docs/arc/AUTO_UPDATE.md` + обзор кода на 2026-07-22).
> Общий бюджет: **12–18 часов разработки + интеграция по 3 релизам**.
> Приоритет этапов 2 и 4 (UAC/AV и Security hardening) — **HIGHEST**.

---

## Executive summary

| Phase | Фокус | Файлов | Effort | Impact | Готовность к ship |
|------:|-------|--------|--------|--------|-------------------|
| 0 | Observability + расширенные tests | 3 | S | Low | после Phase 1 |
| 1 | Quick Wins (A–F) | 4 | S | Medium | 3.48.0 |
| **2** | **UAC + AV race** | 3 | **M** | **High** | **3.48.0** |
| 3 | HttpClient pool | 2 | S | High | 3.48.0 |
| **4** | **Ed25519 подпись ZIP** | 5 | **M** | **High** | **3.49.0** |
| 5 | UX: success toast, minRequired, WeakEvent | 3 | S | Medium | 3.50.0 |
| 6 | Light telemetry в settings.json | 2 | S | Low | 3.50.0 |
| 7 | Release-cycle refit | docs | S | n/a | каждый релиз |

**Рекомендованный first ship**: Phase 1 + Phase 2 + Phase 3 одной пачкой в
**v3.48.0**. Phase 4 — отдельный feature-релиз **v3.49.0** с security focus.
Phase 5–6 — бакаут в **v3.50.0**.

---

## Phase 0 — Observability & test infra (preparation)

*Цель*: до фикса убедиться, что можем воспроизводить проблемы и замерять эффект.

### 0.1. Расширить MockHttpMessageHandler
**Файл:** `MosquitoNetCalculator.Tests/Helpers/TestHttpMessageHandler.cs`.

Добавить хелперы:
- `WithHttpStatus(403)` — тест для rate-limit detection (Phase 1.C).
- `WithTimeout(TimeSpan)` — Timeout-cancellation (Phase 1.E).
- `WithTransientFailure(3)` — для retry-логики (Phase 1.C).

### 0.2. Добавить unit-тест «AV-симулятор»
**Файл:** `MosquitoNetCalculator.Tests/Services/WatchdogServiceAvRaceTests.cs` (new).

Fake-handler, который бросает IOException на первом `ExtractToFile` и
проходит на втором (эмулирует AV lock на 250 ms). Без Phase 2.B этот тест
падает → ловит регрессию.

### 0.3. Добавить unit-тест «UAC cancelled»
**Файл:** `MosquitoNetCalculator.Tests/Services/UpdateServiceUacTests.cs` (new).

Подменить `Process.Start` через `Process.Start` interception ref (`IProcessStarter`)
→ бросить `Win32Exception` (Cancel). Без Phase 2.A этот сценарий приводит к
silent fallback → тест должен упасть на 3.47.x baseline.

### 0.4. Foundation для telemetry (deferred Phase 6)
Заложить `List<UpdateHistoryRecord>` placeholder в `AppSettingsService.Settings`
class, **пустой** массив. Без UI-части — только структура данных.

**Effort**: S (~1 час). **Impact**: foundational. **Tests**: 0 новых (только
infrastructure).

---

## Phase 1 — Quick Wins (багаут в v3.48.0)

Шесть мелких фиксов. Каждый <30 мин кода, не зависит друг от друга, валится
в один релиз. Эффект суммарно значительный.

### 1.A — Singleton HttpClient + SocketsHttpHandler

**Файлы:** `UpdateManifestClient.cs`, `UpdateDownloader.cs`.

**Сейчас (UpdateDownloader.cs:25):**
```csharp
var http = httpClient ?? UpdateManifestClient.CreateConfiguredHttpClient(
    TimeSpan.FromMinutes(10));
```

**Стало:**
```csharp
// UpdateManifestClient.cs
private static readonly Lazy<HttpClient> _sharedClient = new(() =>
{
    var handler = new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        SslOptions = new SslClientAuthenticationOptions
        {
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        }
    };
    var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("MosquitoNetCalculator/3.0");
    return http;
}, LazyThreadSafetyMode.ExecutionAndPublication);

public static HttpClient SharedClient => _sharedClient.Value;
```

- `UpdateManifestClient.FetchManifestAsync`: при `httpClient == null`
  использовать `SharedClient`. Сохранить `ownsClient` для тестов с mock.
- `UpdateDownloader.DownloadWithProgressAsync`: то же самое.
- При выходе из AppDomain — клиент умирает с процессом (нормально).

**Effort**: S. **Impact**: high (waste → predictable performance).

**Tests:** `UpdateDownloaderTests` — `SharedClient_IsReused_AcrossInvocations`
тест (через reflection на внутренний счётчик).

### 1.B — MZ / PE self-test в WatchdogService

**Файл:** `WatchdogService.cs:RunSelfTest`.

**Стало:**
```csharp
private static int RunSelfTest()
{
    try
    {
        var fi = new FileInfo(ExePath);
        if (!fi.Exists) return 1;

        var totalLen = fi.Length;
        // Sanity: .NET single-file app + minimum manifest overhead ≈ >2MB
        if (totalLen < 2_000_000) return 1;

        // MZ header check (PE format magic)
        Span<byte> mz = stackalloc byte[2];
        using (var fs = File.OpenRead(ExePath))
        {
            int read = fs.Read(mz);
            if (read < 2 || mz[0] != (byte)'M' || mz[1] != (byte)'Z') return 1;

            // PE header offset validation: PE\0\0 at e_lfanew
            fs.Seek(0x3C, SeekOrigin.Begin);
            Span<byte> peOffsetBytes = stackalloc byte[4];
            fs.Read(peOffsetBytes);
            int peOffset = BitConverter.ToInt32(peOffsetBytes);
            if (peOffset <= 0 || peOffset > totalLen - 4) return 1;

            fs.Seek(peOffset, SeekOrigin.Begin);
            Span<byte> peSig = stackalloc byte[4];
            fs.Read(peSig);
            if (peSig[0] != (byte)'P' || peSig[1] != (byte)'E') return 1;
        }

        // Already-loaded assembly check
        if (Assembly.GetEntryAssembly() == null) return 1;
        return 0;
    }
    catch { return 1; }
}
```

**Effort**: S. **Impact**: medium (ловля mid-file corruption).

**Tests:** `WatchdogServiceTests` — добавить тесты с фиктивным
`.exe` файлом каждого «плохого» варианта (truncated, non-PE header, valid PE
+ valid MZ). Пока нет — добавить отдельно.

### 1.C — 403/5xx detection + retry

**Файл:** `UpdateManifestClient.cs:FetchManifestAsync`.

```csharp
public class ManifestFetchResult
{
    public UpdateManifest? Manifest { get; init; }
    public string? FailureReason { get; init; } // null|"network"|"rate_limit"|"parse"
}

public static async Task<ManifestFetchResult> FetchManifestResultAsync(HttpClient? httpClient = null)
```

Расширить существующий `FetchManifestAsync` (возвращающий только `UpdateManifest?`)
новым методом, который возвращает причину ошибки.
В production-коде постепенный переход на новый.

**Tests:** `UpdateManifestClientTests`:
- `FetchManifestResultAsync_403RateLimit_ReturnsRateLimitReason`
- `FetchManifestResultAsync_500ServerError_RetrySucceeds`

**Effort**: S. **Impact**: low-medium (диагностика).

### 1.D — Atomic settings.json write

**Файл:** `AppSettingsService.cs:SaveSettings`.

```csharp
private static void SaveSettings(Settings settings)
{
    var tmpPath = SettingsPath + ".tmp";
    var finalPath = SettingsPath;
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        File.WriteAllText(tmpPath, JsonSerializer.Serialize(settings, ...));
        // On .NET 8+, File.Replace handles cross-volume fallback
        if (File.Exists(finalPath))
            File.Replace(tmpPath, finalPath, null);
        else
            File.Move(tmpPath, finalPath);
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"[AppSettings] save failed: {ex.Message}");
        try { File.Delete(tmpPath); } catch { }
    }
}
```

**Effort**: S. **Impact**: low (защита от corrupt-config на внезапном отключении).

**Tests:** `AppSettingsServiceTests` — kill mid-write sim через
`Mock<IOException>` (File.WriteAllText). Без этого фикса — файл остаётся
truncated. С этим — `settings.json` остаётся валидным.

### 1.E — Manifest timeout 30 сек + 5xx retry

**Файл:** `UpdateManifestClient.cs:CreateConfiguredHttpClient`.

```csharp
TimeSpan.FromSeconds(30)
// вместо
TimeSpan.FromSeconds(15)
```

+ ретрай на 5xx: wrap `GetAsync` в for-loop с 2 retry на 503/504 (transient
по GitHub CDN статусу).

**Effort**: S. **Impact**: low (в среднем стабильность).

### 1.F — minRequired enforcement

**Файл:** `VersionResolver.cs:GetAvailableUpdate`, `Models/UpdateManifest.cs`
(добавить `MinRequired` уже есть, использовать).

```csharp
// После проверки currentVersion > 0
if (!string.IsNullOrWhiteSpace(manifest.MinRequired))
{
    var min = ParseSafe(manifest.MinRequired);
    if (min != null && currentVersion < min)
    {
        // Required migration — no auto-update, signal user to download manually
        return ReleaseForcedUpdate(manifest, min);
    }
}
```

Нужна новая вариация возврата: `ReleaseInfo?` для нормального пути,
отдельный `ForceUpdateRequired` boolean для случая minRequired. UI в
`DialogService.ShowUpdateAvailable` покажет вместо "Скачать и установить"
кнопку "Скачать вручную с сайта" → `Process.Start(release.Url)`.

**Effort**: S. **Impact**: future-critical (нужен для security-багфиксов).

---

## Phase 2 — UAC + AV race (HIGH EMPHASIS, ключевой release)

**Цель**: устранить два самых «опасных» сценария отказа install-flow.

### 2.A — UAC: без silent fallback

**Файл:** `UpdateService.cs` (блок Process.Start около строки ~ 410).

**Текущий код:**
```csharp
try
{
    Process.Start(new ProcessStartInfo(WatchdogService.WatchdogPath) {
        UseShellExecute = true, Verb = "runas",
        WindowStyle = ProcessWindowStyle.Hidden
    });
}
catch (Exception ex)
{
    Debug.WriteLine($"[UpdateService] Failed to launch watchdog elevated: {ex.Message}");
    // Fallback: try WITHOUT elevation (user may have write access)
    try
    {
        Process.Start(new ProcessStartInfo(WatchdogService.WatchdogPath) {
            UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden
        });
    }
    catch (Exception ex2) { ... }
}
```

**Что плохо:** при `Program Files` (read-only), если пользователь откажет UAC,
fallback без elevation даже не пытается copy → watchdog уходит в «wait loop»
на 120 сек → «ABORTED» message в логе → пользователь видит «установка»
без эффекта.

**Новый код:**
```csharp
try
{
    var psi = new ProcessStartInfo(WatchdogService.WatchdogPath)
    {
        UseShellExecute = true,
        Verb = "runas",
        WindowStyle = ProcessWindowStyle.Hidden
    };
    Process.Start(psi);
    _watchdogLaunchFailed = false;
}
catch (System.ComponentModel.Win32Exception ex)
{
    // 1223 = ERROR_CANCELLED (user declined UAC)
    // 1313 = ERROR_NO_SUCH_PRIVILEGE (no admin token available)
    // 740  = ERROR_ELEVATION_REQUIRED
    Debug.WriteLine($"[UpdateService] UAC elevation failed: {NativeError(ex.NativeErrorCode)}");
    _watchdogLaunchFailed = true;
}
// + pre-flight check: writes test file to %AppData%\arc-write-test.tmp
//   If fails, set _installNeedsAdmin = true → MessageBox.Show with clear hint.
```

После блока — независимо от UAC-результата:

```csharp
if (_watchdogLaunchFailed)
{
    WatchdogService.CleanupStagedUpdate(); // удаляет wolfed .zip
    AppSettingsService.SavePendingUpdateVersion(manifest.Latest); // retry next boot
    ToastService.ShowToast(
        "Не удалось установить обновление: программа запущена из защищённой папки, а права администратора не предоставлены. Повторите из-под администратора или установите вручную.",
        ToastType.Error);
    return;
}
Application.Current.Shutdown();
```

**Pre-flight check в WatchdogService.StageUpdate:**
```csharp
// Пробуем записать test-файл ДО extract, чтобы понять write-permissions
public static (bool CanWriteInstallDir, string? FailureReason) PreFlightCheck()
{
    try
    {
        var testPath = Path.Combine(UpdateDataDir, $".write-{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(testPath, new byte[] { 1 });
        File.Delete(testPath);
        // Также проверим что HERE writе-able (target kopirovania)
        var hereTest = Path.Combine(BasePath, $".write-{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(hereTest, new byte[] { 1 });
        File.Delete(hereTest);
        return (true, null);
    }
    catch (UnauthorizedAccessException ex)
    {
        return (false, $"Нет прав записи: {ex.Message}");
    }
    catch (Exception ex)
    {
        return (false, $"Ошибка доступа: {ex.Message}");
    }
}
```

Вызывается до `Process.Start` в `UpdateService.CheckAndApplyAsync`. Если
`PreFlightCheck.CanWriteInstallDir == false` → сразу показать user-friendly
error → НЕ запускать download даже.

### 2.B — AV Race: per-entry extract с retry + cleanup осиротевших

**Файл:** `WatchdogService.cs:StageUpdate`.

**Текущий код:**
```csharp
var tmpDir = Path.Combine(UpdateDataDir, $"arc-stage-{Guid.NewGuid():N}");
ZipFile.ExtractToDirectory(downloadedZipPath, tmpDir);
if (Directory.Exists(StageDir))
    Directory.Delete(StageDir, recursive: true);
Directory.Move(tmpDir, StageDir);
```

**Проблемы:**
- Если AV лочит **один** entry в середине extract → IOException →
  ZipFile бросает → **tmpDir не удаляется** → в AppData растёт мусор.
- Если параллельно работает старый watchdog (нештатно после краша) →
  Directory.Delete(StageDir) → не удастся → молча в catch → tmpDir
  остаётся, StageDir остаётся → новая версия не разворачивается.

**Новый код:**
```csharp
private const int AvRetryAttempts = 4;
private static readonly int[] AvRetryBackoffMs = { 250, 750, 1500 };

public static void StageUpdate(string downloadedZipPath)
{
    if (!File.Exists(downloadedZipPath))
        throw new FileNotFoundException("Update ZIP not found", downloadedZipPath);

    Directory.CreateDirectory(UpdateDataDir);

    // PRE-FLIGHT: cleanup arc-stage-* orphans older than 1 hour (kick off concurrent watchers)
    CleanupOrphanedStageDirectories();

    // 1. Write the watchdog .bat FIRST (references StageDir).
    File.WriteAllText(WatchdogPath, BuildWatchdogBat(BasePath, StageDir));

    // 2. Per-entry extract with AV-retry, atomic swap
    var tmpDir = Path.Combine(UpdateDataDir, $"arc-stage-{Guid.NewGuid():N}");
    try
    {
        using var archive = ZipFile.OpenRead(downloadedZipPath);
        Directory.CreateDirectory(tmpDir);
        foreach (var entry in archive.Entries)
        {
            AttemptExtract(entry, tmpDir); // throws if all retries fail
        }
    }
    catch
    {
        TryDeleteDir(tmpDir); // cleanup partial
        throw;
    }

    // Atomic swap: delete old StageDir, move new one in. Lock-aware (AV may be in flight).
    SwapStageDirectory(tmpDir);

    // 3. Backup current .exe
    if (File.Exists(ExePath))
        File.Copy(ExePath, ExeBakPath, overwrite: true);
}

private static void AttemptExtract(ZipArchiveEntry entry, string targetDir)
{
    var destPath = Path.Combine(targetDir, entry.FullName);
    var parentDir = Path.GetDirectoryName(destPath);
    if (!string.IsNullOrEmpty(parentDir)) Directory.CreateDirectory(parentDir);

    // Defense against zip-slip (entry.FullName with "..")
    var resolvedDest = Path.GetFullPath(destPath);
    if (!resolvedDest.StartsWith(Path.GetFullPath(targetDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException($"Zip-slip detected in entry: {entry.FullName}");

    Exception? lastEx = null;
    for (int attempt = 0; attempt < AvRetryAttempts; attempt++)
    {
        try
        {
            entry.ExtractToFile(resolvedDest, overwrite: true);
            return; // success
        }
        catch (IOException ex) when (attempt < AvRetryAttempts - 1)
        {
            lastEx = ex; // AV lock retry
            Thread.Sleep(AvRetryBackoffMs[attempt]);
        }
    }
    throw lastEx ?? new IOException($"Failed to extract {entry.FullName}");
}

private static void SwapStageDirectory(string newDir)
{
    if (Directory.Exists(StageDir))
    {
        TryDeleteDir(StageDir); // может кинуть IOException (lock)
        // Если не получилось - оставляем старый, новая версия не применится.
        // Watchdog обнаружит, что в staging = old = abort.
        // (см. watchdog.bat:rollback)
    }
    Directory.Move(newDir, StageDir);
}

internal static void CleanupOrphanedStageDirectories()
{
    foreach (var d in Directory.EnumerateDirectories(UpdateDataDir, "arc-stage-*"))
    {
        try
        {
            var age = DateTime.UtcNow - new DirectoryInfo(d).CreationTimeUtc;
            if (age > TimeSpan.FromHours(1))
                Directory.Delete(d, recursive: true);
        }
        catch { /* best-effort cleanup */ }
    }
}

internal static void CleanupStagedUpdate()
{
    TryDeleteDir(StageDir);
    TryDeleteFile(WatchdogPath);
    TryDeleteFile(Path.Combine(UpdateDataDir, "arc-update.zip"));
}
```

**Effort**: M (~3-4 часа). **Impact**: high (фикс двух реальных user-visible
багов).

### 2.C — Watchdog .bat: явное pre-flight + развёрнутая диагностика

**Файл:** `WatchdogService.cs:BuildWatchdogBat`.

Добавить в начало .bat:
```bat
REM --- 0. Pre-flight: проверяем что HERE writable
echo [WATCHDOG] Pre-flight write test on %HERE% >> "%~dp0watchdog.log"
echo test > "%HERE%\.write.tmp" 2>nul
if errorlevel 1 (
    echo [WATCHDOG] ROLLBACK: %HERE% не writable. Прав не хватает. >> "%~dp0watchdog.log"
    start "" "%HERE%\%EXE%" /msg "Для обновления нужны права администратора. Запустите программу от администратора."
    del "%~f0" 2>nul
    exit /b 1
)
del "%HERE%\.write.tmp" 2>nul
```

Также сделать видным для пользователя сообщение об ошибке:
- `.bat` пишет `watchdog.log` в `%AppData%` (`%~dp0watchdog.log`).
- `App.OnStartup` может подхватить этот файл (если остался) и показать
  сообщение.

### 2.D — App.OnStartup подхват watchdog.log

**Файл:** `WatchdogService.cs:HandleStartup`.

```csharp
public static bool HandleStartup(string[] args)
{
    try
    {
        if (args != null && Array.Exists(args, a =>
            string.Equals(a, SelfTestArg, StringComparison.OrdinalIgnoreCase)))
        {
            int code = RunSelfTest();
            Environment.Exit(code);
            return false;
        }

        // Если watchdog.bat крашнулся на прошлом запуске — найдём лог
        var logPath = Path.Combine(UpdateDataDir, "watchdog.log");
        if (File.Exists(logPath))
        {
            var logContents = File.ReadAllText(logPath);
            if (logContents.Contains("ROLLBACK", StringComparison.OrdinalIgnoreCase))
            {
                // Установить pendingUpdateVersion = previous, чтобы повторно предложить
                var pending = AppSettingsService.LoadPendingUpdateVersion();
                if (!string.IsNullOrEmpty(pending))
                    ToastService.ShowToast(
                        "Прошлое обновление не удалось. Повторите или установите вручную.",
                        ToastType.Warning,
                        durationMs: 8000);
            }
            File.Delete(logPath);
        }

        // ... существующий cleanup
    }
}
```

**Effort** для всей Phase 2: M (3-4 часа) + tests (1-2 часа).

---

## Phase 3 — HttpClient pool (включено в Phase 1.A выше)

После Phase 1.A будет покрыто. Отдельно здесь добавляем:

### 3.1 — Connection pool observability

**Файл:** `UpdateLog.cs` (telemetry phase 0.4 foundation).

При каждой manifest-check писать:
```csharp
UpdateLog.RecordEvent(new UpdateHistoryRecord
{
    At = DateTime.UtcNow,
    Kind = "manifest_ok",
    Detail = $"CDN={CDN_REACHED}, RTT={rttMs}ms",
});
```

Чтобы можно было посмотреть «сколько раз у нас 503 были за последнюю
неделю».

---

## Phase 4 — Ed25519 подпись (security hardening, отдельный релиз 3.49.0)

### 4.1 — Выбор библиотеки и генерация ключей

| NuGet | Плюсы | Минусы | Рекомендация |
|-------|-------|--------|--------------|
| **Chaos.NaCl** (0.1.4) | Минималистичная, BSD, нет зависимостей | Только Ed25519+X25519 | ✅ Best for single-purpose |
| NSec.Cryptography | Production-grade, поддержка Argon2id | Больше (~1MB) | overkill |
| BouncyCastle.Cryptography | Полный Swiss-army-knife | Огромный, медленный, не Mono-friendly | ❌ |

**Решение:** `Chaos.NaCl`.

**Генерация ключей:** OFFLINE на dev-machine, не в репо, не в CI.
```bash
# PowerShell wrapper around Chaos.NaCl CLI (или Python Ed25519)
dotnet run --project tools/keygen -- --out keys\arc-frame-ed25519-{env}.json
# Outputs: {publicKey: base64, privateKey: base64}
```

### 4.2 — Хранение ключей

- **Public key** — захардкоженный `byte[32]` в `UpdateVerifier.cs`.
- **Private key** — в GitHub Secrets `RELEASE_SIGNING_KEY_BASE64` для CI;
  для ручного релиза — в локальной папке разработчика (НЕ в репо).

### 4.3 — Расширение ReleaseInfo

**Файл:** `Models/UpdateManifest.cs:ReleaseInfo`.

```csharp
/// <summary>Ed25519 подпись SHA-256 файла ZIP в base64. Опциональна в первом релизе,
/// обязательна через 2 релиза после активации (grace period для старых клиентов).</summary>
[JsonPropertyName("signature")]
public string Signature { get; set; } = "";
```

### 4.4 — Расширение UpdateVerifier

```csharp
// UpdateVerifier.cs
private static readonly byte[] PublicKey = new byte[32] { /* hardcoded */ };

public static bool VerifySignature(string filePath, string expectedBase64)
{
    if (string.IsNullOrEmpty(expectedBase64)) return false;
    if (expectedBase64.Length != 86) return false; // base64(64) -> 88 chars; allow 86 trimmed

    byte[]? signature;
    try { signature = Convert.FromBase64String(expectedBase64); }
    catch { return false; }
    if (signature.Length != 64) return false; // Ed25519 sig is 64 bytes

    byte[] hash;
    using (var stream = File.OpenRead(filePath))
        hash = SHA256.HashData(stream);

    return Chaos.NaCl.Ed25519.Verify(signature, hash, PublicKey);
}
```

### 4.5 — Включение в RunUpdateFlowAsync

```csharp
// После VerifyHash (SHA-256) добавить:

if (!string.IsNullOrEmpty(release.Signature))
{
    if (!UpdateVerifier.VerifySignature(tempZip, release.Signature))
    {
        TryDelete(tempZip);
        var msg = "Подпись ZIP не прошла проверку. Возможно, обновление подменено.";
        // ...display user + abort
        return;
    }
}
else
{
    // Warns but allows (grace period for migration)
    ToastService.ShowToast(
        "Подпись обновления отсутствует — установите из проверенного источника.",
        ToastType.Warning);
}
```

### 4.6 — Расширение release.yml

```yaml
- name: Sign ZIP
  env:
    RELEASE_SIGNING_KEY_BASE64: ${{ secrets.RELEASE_SIGNING_KEY_BASE64 }}
  shell: pwsh
  run: |
    # Compute SHA-256 (already done in step 9, but need bytes for signing)
    $sha = (Get-FileHash -Algorithm SHA256 $zipPath).Hash
    $bytes = [System.Text.Encoding]::ASCII.GetBytes($sha.ToLower())
    
    # Decode key + sign
    $keyBytes = [Convert]::FromBase64String($env:RELEASE_SIGNING_KEY_BASE64)
    # ...Chaos.NaCl invocation (out-of-process or via dotnet-script)

- name: Update releases.json
  # ... add signature field to entry, like size/sha256
```

### 4.7 — Migration / grace period

В v3.48.x (после Ed25519 готов): добавить поле `signature` в schema.
В v3.49.x: считать `signature` обязательным при публикации нового манифеста.
В v3.50.x: считать `signature` обязательным для установки (отказ без подписи).

### **Effort**: M (1-2 дня включая тесты). **Impact**: high.

---

## Phase 5 — UX polish (v3.50.0)

### 5.1 — Startup success toast

**Файл:** `UpdateService.cs:CheckOnStartupAsync`.

```csharp
public static async Task CheckOnStartupAsync()
{
    var owner = Application.Current?.MainWindow;
    if (owner == null) return;

    var manifest = await FetchManifestAsync(...);
    var release = GetAvailableUpdate(manifest, CurrentVersion);

    if (release == null && manifest != null)
    {
        // Дисплейный, не modal — через 6 сек после старта
        _ = DelayAndShow(
            TimeSpan.FromSeconds(6),
            () => ToastService.ShowToast(
                "✓ Автообновление активно. Вы на последней версии.",
                ToastType.Success,
                durationMs: 3500));
    }
}
```

### 5.2 — WeakEventManager для UpdateDetected

**Файл:** `UpdateService.cs:UpdateDetected`.

```csharp
// Before:
public static event Action<UpdateItem>? UpdateDetected;

// After: keep API, use static WeakReference list internally
private static readonly List<WeakReference<Action<UpdateItem>>> _subs = new();
public static event Action<UpdateItem>? UpdateDetected
{
    add { lock (_subs) _subs.Add(new WeakReference<Action<UpdateItem>>(value)); }
    remove { /* skip; weak semantics */ }
}

internal static void FireUpdateDetected(UpdateItem item)
{
    var alive = new List<Action<UpdateItem>>();
    lock (_subs)
    {
        foreach (var weak in _subs)
            if (weak.TryGetTarget(out var h)) alive.Add(h);
    }
    foreach (var h in alive) try { h(item); } catch { }
}
```

### 5.3 — ForceUpdate required detection (UI part)

`DialogService.ShowUpdateAvailable` уже умеет показывать «требуется
миграция» — добавить кнопку «Открыть страницу загрузки».

---

## Phase 6 — Light telemetry (v3.50.0)

### 6.1 — settings.json: UpdateHistory append-only

**Файл:** `AppSettingsService.cs`.

```csharp
public class Settings {
    // ... existing
    public List<UpdateHistoryRecord> UpdateHistory { get; set; } = new();
}

public class UpdateHistoryRecord
{
    public DateTime At { get; set; }
    public string Kind { get; set; } = ""; // manifest_ok|manifest_timeout|manifest_ratelimit|download_ok|download_fail|install_ok|install_rollback|install_uac_fail
    public string? Version { get; set; }
    public string? Detail { get; set; }
}

public static void RecordUpdateEvent(UpdateHistoryRecord record)
{
    lock (_lock)
    {
        var settings = LoadSettings();
        settings.UpdateHistory.Add(record);
        if (settings.UpdateHistory.Count > 50)
            settings.UpdateHistory.RemoveAt(0); // cap
        SaveSettings(settings);
    }
}
```

### 6.2 — UI: «Журнал автообновлений»

Дополнительная вкладка рядом с «Что нового». Простой ListView с датой +
Kind + Detail.

---

## Phase 7 — Release cycle (применяется каждый релиз)

Стандартный pipeline, как в `docs/arc/RELEASE_PROCESS.md`. После применения
Phase 1+2+3 → v3.48.0; Phase 4 → v3.49.0; Phase 5+6 → v3.50.0.

---

## Risk matrix (после-применения)

| Risk | Severity | Likelihood | Mitigation |
|------|----------|-----------|-----------|
| Phase 2.A: UAC cancelled → user не понимает что произошло | M | M | Toast с инструкцией + .bat log pickup |
| Phase 2.B: per-entry extract дольше full ExtractToDirectory | L | M | Параллелизация (Task.WhenAll) если >5 entries |
| Phase 3: HttpClient singleton leaks DNS stale entries | L | L | PooledConnectionLifetime=5min |
| Phase 4: потеря приватного ключа = stuck | H | L | Резервная пара ключей в password manager |
| Phase 4: старые клиенты не понимают signature в JSON | L | M | System.Text.Json толерантен, unknown fields ignored |
| Phase 5.2: WeakEvent semantics ломает пользовательский код | L | L | API остаётся event, только internals меняются |

---

## Migration & backward-compat

- **Settings schema:** добавление новых полей (`UpdateHistory`, etc.)
  backward-compat — System.Text.Json заполняет defaults.
- **releases.json schema:** новое поле `signature` — добавляется как
  optional, optional в v3.48.x, required после v3.50.0.
- **AppData layout:** `arc-stage-*` папки GUID-prefix уже защищает от
  false-positive cleanup. Новый prefix `arc-write-*` (тестовые файлы из
  pre-flight) тоже. Не должно конфликтовать с пользовательскими файлами.
- **Watchdog.bat:** изменение `.bat` текста → все install на новых версиях
  получат новый `.bat`. Старые `.bat` в AppData автоматически очищаются в
  HandleStartup.
- **HttpClient singleton:** для тестов остаётся `ownsClient` параметр —
  unit-tests с MockHttpMessageHandler продолжат работать с собственным
  disposable клиентом.

---

## ETA & first ship

| Phase | Разработка | Tests | Reviews | Total | Target version |
|-------|-----------|-------|---------|-------|---------------|
| 0    | 1h  | 0h  | 0h  | 1h | (none — infra) |
| 1    | 3h  | 2h  | 1h  | 6h | v3.48.0 |
| 2    | 4h  | 2h  | 1h  | 7h | v3.48.0 |
| 3    | 1h  | 1h  | 0.5h | 2.5h | v3.48.0 |
| 4    | 8h  | 3h  | 2h  | 13h | v3.49.0 |
| 5    | 3h  | 1h  | 1h  | 5h | v3.50.0 |
| 6    | 4h  | 1h  | 1h  | 6h | v3.50.0 |

**v3.48.0 total = ~15.5ч** (Phase 0+1+2+3).
**v3.49.0** добавит Ed25519: +13ч.
**v3.50.0** добавит UX+telemetry: +11ч.

Все этапы могут идти параллельно в 2 workstream'ах (security + UX).

---

## Что НЕ входит в этот план

- Полный переход на Velopack/Squirrel — overkill для single-binary app.
- Dedicated статус-page на внешнем хосте — отложено.
- Auto-update channel'ы (beta/stable) — не нужно для single-track.
- Plugin-system for app — выходит за scope.

---

## Last verified

2026-07-22 — v3.47.2 baseline. После применения плана: целевой архитектурный
pattern «manifest → signed ZIP → atomic stage swap → UAC-aware watchdog →
per-entry retry», durable defense against:
- silent fallthroughs на Program Files
- AV file locks
- single-owner trust assumption
- configurational corruption.
