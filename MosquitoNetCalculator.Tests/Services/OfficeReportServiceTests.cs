using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Чистые тесты OfficeReportService: парсинг ответа gist, конфигурация —
    /// без сети и настроек.
    /// </summary>
    public class OfficeReportServicePureTests
    {
        [Fact]
        public void ReportFileName_FormatsPerDevice()
        {
            Assert.Equal("office-2-abc123def456.json", OfficeReportService.ReportFileName("2", "abc123def456"));
        }

        [Fact]
        public void ReportFileName_EmptyDeviceId_FallsBackToLegacyOfficeFile()
        {
            // Легаси-отчёты без deviceId пишутся в старый файл по офису.
            Assert.Equal("office-2.json", OfficeReportService.ReportFileName("2", ""));
            Assert.Equal("office-2.json", OfficeReportService.ReportFileName("2", "   "));
        }

        // NB: «IsConfigured при отсутствии токена» намеренно НЕ тестируется здесь —
        // встроенный токен зависит от окружения сборки (csproj GenerateOfficeReportToken:
        // env OFFICE_REPORT_TOKEN или локальный файл .office-report-token).
        // Детерминированная проверка — в FileSystem-коллекции (IsConfigured_True_WhenTokenFromSettings).

        [Fact]
        public void ParseReports_ValidGistResponse_ReturnsReports()
        {
            var json = """
            {
              "id": "abc",
              "files": {
                "office-1.json": { "content": "{\"prefix\":\"1\",\"version\":\"3.47.4\",\"reportedAt\":\"2026-08-13T10:00:00Z\",\"orderCount\":11}" },
                "office-2.json": { "content": "{\"prefix\":\"2\",\"version\":\"3.46.1\",\"reportedAt\":\"2026-08-12T09:00:00Z\"}" }
              }
            }
            """;

            var reports = OfficeReportService.ParseReports(json);

            Assert.Equal(2, reports.Count);
            Assert.Equal("1", reports[0].Prefix);
            Assert.Equal("3.47.4", reports[0].Version);
            Assert.Equal(11, reports[0].OrderCount);
            Assert.Equal(0, reports[1].OrderCount); // старое поле отсутствует — 0
            Assert.NotNull(reports[0].ReportedAtUtc);
        }

        [Fact]
        public void ParseReports_NonOfficeFiles_Skipped()
        {
            var json = """
            {
              "files": {
                "README.md": { "content": "hello" },
                "office-3.json": { "content": "{\"prefix\":\"3\",\"version\":\"3.47.4\"}" }
              }
            }
            """;

            var reports = OfficeReportService.ParseReports(json);

            Assert.Single(reports);
            Assert.Equal("3", reports[0].Prefix);
        }

        [Fact]
        public void ParseReports_DeviceNamedFiles_ParsedWithDeviceId()
        {
            var json = """
            {
              "files": {
                "office-1-deviceA.json": { "content": "{\"prefix\":\"1\",\"deviceId\":\"deviceA\",\"deviceName\":\"PK-1\",\"version\":\"3.47.4\"}" },
                "office-1-deviceB.json": { "content": "{\"prefix\":\"1\",\"deviceId\":\"deviceB\",\"version\":\"3.46.1\"}" }
              }
            }
            """;

            var reports = OfficeReportService.ParseReports(json);

            Assert.Equal(2, reports.Count);
            Assert.Equal("deviceA", reports[0].DeviceId);
            Assert.Equal("PK-1", reports[0].DeviceName);
            Assert.Equal("deviceB", reports[1].DeviceId);
            Assert.Equal("", reports[1].DeviceName); // нет поля — пустая строка
        }

        [Fact]
        public void ParseReports_BrokenContent_Skipped()
        {
            var json = """
            {
              "files": {
                "office-1.json": { "content": "not json" },
                "office-2.json": { "content": "{\"prefix\":\"2\",\"version\":\"3.47.4\"}" }
              }
            }
            """;

            var reports = OfficeReportService.ParseReports(json);

            Assert.Single(reports);
            Assert.Equal("2", reports[0].Prefix);
        }

        [Fact]
        public void ParseReports_InvalidJson_ReturnsEmpty()
        {
            var reports = OfficeReportService.ParseReports("not json at all");

            Assert.Empty(reports);
        }

        [Fact]
        public void ParseReports_NoFilesProperty_ReturnsEmpty()
        {
            var reports = OfficeReportService.ParseReports("{\"id\":\"abc\"}");

            Assert.Empty(reports);
        }

        [Fact]
        public void ParseReportFiles_ReturnsFileNamesWithReports()
        {
            var json = """
            {
              "files": {
                "office-1-new.json": { "content": "{\"prefix\":\"1\",\"deviceId\":\"guidB\",\"deviceName\":\"PK-1\",\"version\":\"3.47.4\"}" },
                "README.md": { "content": "hello" }
              }
            }
            """;

            var files = OfficeReportService.ParseReportFiles(json);

            var single = Assert.Single(files);
            Assert.Equal("office-1-new.json", single.FileName);
            Assert.Equal("guidB", single.Report.DeviceId);
        }

        // ─── Вычисление дублей (чистая логика) ───────────────────────────────

        private static OfficeReportFile File(string name, string prefix, string deviceId, string deviceName, string reportedAt) => new(
            name,
            new OfficeReport
            {
                Prefix = prefix,
                DeviceId = deviceId,
                DeviceName = deviceName,
                Version = "3.47.4",
                ReportedAt = reportedAt,
            });

        [Fact]
        public void ComputeDuplicateFilesToDelete_SameMachineTwoFiles_DeletesOlder()
        {
            // Один ПК (PK-1) с двумя deviceId — обычная версия + dev: лишний старый файл.
            var files = new[]
            {
                File("office-1-new.json", "1", "guidB", "PK-1", "2026-08-13T10:00:00Z"),
                File("office-1-dup.json", "1", "guidA", "PK-1", "2026-08-13T09:00:00Z"),
            };

            var toDelete = OfficeReportService.ComputeDuplicateFilesToDelete(files);

            var deleted = Assert.Single(toDelete);
            Assert.Equal("office-1-dup.json", deleted);
        }

        [Fact]
        public void ComputeDuplicateFilesToDelete_LegacyPlusNamed_DeletesLegacy()
        {
            var files = new[]
            {
                File("office-1.json", "1", "", "", "2026-08-13T10:00:00Z"), // легаси (старая сборка)
                File("office-1-guidA.json", "1", "guidA", "PK-1", "2026-08-13T11:00:00Z"),
            };

            var toDelete = OfficeReportService.ComputeDuplicateFilesToDelete(files);

            var deleted = Assert.Single(toDelete);
            Assert.Equal("office-1.json", deleted);
        }

        [Fact]
        public void ComputeDuplicateFilesToDelete_DistinctMachines_NothingToDelete()
        {
            var files = new[]
            {
                File("office-1-a.json", "1", "guidA", "PK-1", "2026-08-13T10:00:00Z"),
                File("office-1-b.json", "1", "guidB", "PK-2", "2026-08-13T09:00:00Z"),
                File("office-2.json", "2", "", "", "2026-08-13T08:00:00Z"), // легаси без именованных
            };

            Assert.Empty(OfficeReportService.ComputeDuplicateFilesToDelete(files));
        }

        [Fact]
        public void ComputeDuplicateFilesToDelete_DuplicatesPerPrefixIndependent()
        {
            var files = new[]
            {
                File("office-1-a.json", "1", "guidA", "PK-1", "2026-08-13T10:00:00Z"),
                File("office-1-b.json", "1", "guidA", "PK-1", "2026-08-13T09:00:00Z"), // дубль офиса 1
                File("office-2-c.json", "2", "guidC", "PK-3", "2026-08-13T10:00:00Z"),
                File("office-2-d.json", "2", "guidD", "PK-3", "2026-08-13T09:00:00Z"), // дубль офиса 2
            };

            var toDelete = OfficeReportService.ComputeDuplicateFilesToDelete(files);

            Assert.Equal(2, toDelete.Count);
            Assert.Contains("office-1-b.json", toDelete);
            Assert.Contains("office-2-d.json", toDelete);
        }
    }

    /// <summary>
    /// Тесты с сетью (фейковый handler) и настройками — в коллекции "FileSystem",
    /// чтобы не конфликтовать с AppSettingsServiceTests за mutable SettingsPath.
    /// </summary>
    [Collection("FileSystem")]
    public class OfficeReportServiceHttpTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _originalSettingsPath;
        private readonly string _originalToken;
        private readonly string _originalOrdersDir;

        public OfficeReportServiceHttpTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "mosquito_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            _originalSettingsPath = AppSettingsService.SettingsPath;
            AppSettingsService.SettingsPath = Path.Combine(_tempDir, "settings.json");

            _originalToken = AppSettingsService.LoadOfficeReportToken();
            AppSettingsService.SaveOfficeReportToken("test-gist-token-123");

            // Папка заказов — в temp (пустая) → счётчик заказов в отчёте детерминирован (0).
            _originalOrdersDir = OrderStorageService.OrdersDir;
            OrderStorageService.OrdersDir = Path.Combine(_tempDir, "orders");
        }

        public void Dispose()
        {
            OrderStorageService.OrdersDir = _originalOrdersDir;
            AppSettingsService.SaveOfficeReportToken(_originalToken);
            AppSettingsService.SettingsPath = _originalSettingsPath;
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }

        [Fact]
        public void IsConfigured_True_WhenTokenFromSettings()
        {
            Assert.True(OfficeReportService.IsConfigured);
        }

        [Fact]
        public void GistToken_PrefersSettingsOverride()
        {
            // settings.json-переопределение имеет приоритет над встроенным токеном
            // (детерминированно проверяется только этот путь).
            Assert.Equal("test-gist-token-123", OfficeReportService.GistToken);
        }

        [Fact]
        public async Task SendReportAsync_Success_PatchesOwnDeviceFileWithBearerToken()
        {
            HttpRequestMessage? captured = null;
            string? body = null;
            var handler = new TestHttpMessageHandler(req =>
            {
                captured = req;
                // Content читаем здесь: сервис диспозит request (и его content) после SendAsync.
                body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            using var http = new HttpClient(handler);

            var ok = await OfficeReportService.SendReportAsync(http);

            Assert.True(ok);
            Assert.NotNull(captured);
            Assert.Equal(HttpMethod.Patch, captured!.Method);
            Assert.Equal("Bearer test-gist-token-123", captured.Headers.Authorization!.ToString());
            Assert.Contains("/gists/" + OfficeReportService.GistId, captured.RequestUri!.ToString());

            Assert.NotNull(body);
            // Имя файла — по устройству: office-{prefix}-{deviceId}.json
            var deviceId = AppSettingsService.LoadOrCreateDeviceId();
            Assert.Contains(OfficeReportService.ReportFileName("1", deviceId), body);

            // Внутренний JSON отчёта экранируется (\u0022 вместо кавычек) — проверяем ключи.
            // Папка заказов пуста (temp) → счётчик 0, но значение не экранируется.
            Assert.Contains("orderCount", body);
            Assert.Contains("\"orderCount\":0", body.Replace("\\u0022", "\""));
            // Отчёт несёт deviceId и имя машины.
            var inner = body.Replace("\\u0022", "\"");
            Assert.Contains("deviceId", inner);
            Assert.Contains(deviceId, inner);
            Assert.Contains("deviceName", inner);
        }

        [Fact]
        public async Task SendReportAsync_ServerError_ReturnsFalse()
        {
            var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
            using var http = new HttpClient(handler);

            var ok = await OfficeReportService.SendReportAsync(http);

            Assert.False(ok);
        }

        [Fact]
        public async Task SendReportAsync_NetworkError_ReturnsFalse()
        {
            var handler = new TestHttpMessageHandler(_ => throw new HttpRequestException("down"));
            using var http = new HttpClient(handler);

            var ok = await OfficeReportService.SendReportAsync(http);

            Assert.False(ok);
        }

        [Fact]
        public async Task FetchReportsAsync_Success_ReturnsParsedReports()
        {
            var json = """
            {
              "files": {
                "office-1-aaabbb.json": { "content": "{\"prefix\":\"1\",\"deviceId\":\"aaabbb\",\"version\":\"3.47.4\",\"reportedAt\":\"2026-08-13T10:00:00Z\"}" },
                "office-2.json": { "content": "{\"prefix\":\"2\",\"version\":\"3.46.1\",\"reportedAt\":\"2026-08-12T09:00:00Z\"}" }
              }
            }
            """;
            var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json),
            });
            using var http = new HttpClient(handler);

            var reports = await OfficeReportService.FetchReportsAsync(http);

            Assert.Equal(2, reports.Count);
            Assert.Equal("3.47.4", reports[0].Version);
        }

        [Fact]
        public async Task FetchReportsAsync_Unauthorized_ReturnsEmpty()
        {
            var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
            using var http = new HttpClient(handler);

            var reports = await OfficeReportService.FetchReportsAsync(http);

            Assert.Empty(reports);
        }

        [Fact]
        public async Task CleanupDuplicatesAsync_PatchesNullForDuplicateFiles()
        {
            string? patchBody = null;
            int patchCount = 0;
            var gistJson = """
            {
              "files": {
                "office-1-new.json": { "content": "{\"prefix\":\"1\",\"deviceId\":\"guidB\",\"deviceName\":\"PK-1\",\"version\":\"3.47.4\",\"reportedAt\":\"2026-08-13T10:00:00Z\"}" },
                "office-1-dup.json": { "content": "{\"prefix\":\"1\",\"deviceId\":\"guidA\",\"deviceName\":\"PK-1\",\"version\":\"3.47.4\",\"reportedAt\":\"2026-08-13T09:00:00Z\"}" },
                "office-2.json": { "content": "{\"prefix\":\"2\",\"version\":\"3.46.1\",\"reportedAt\":\"2026-08-12T09:00:00Z\"}" }
              }
            }
            """;
            var handler = new TestHttpMessageHandler(req =>
            {
                if (req.Method == HttpMethod.Patch)
                {
                    patchCount++;
                    patchBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(gistJson) };
            });
            using var http = new HttpClient(handler);

            int deleted = await OfficeReportService.CleanupDuplicatesAsync(http);

            Assert.Equal(1, deleted);
            Assert.Equal(1, patchCount);
            Assert.NotNull(patchBody);
            // PATCH удаляет только дубль (content: null), новейший файл не трогаем.
            Assert.Contains("\"office-1-dup.json\":null", patchBody!);
            Assert.DoesNotContain("office-1-new.json", patchBody);
            Assert.DoesNotContain("office-2.json", patchBody);
        }

        [Fact]
        public async Task CleanupDuplicatesAsync_NoDuplicates_NoPatch()
        {
            int patchCount = 0;
            var gistJson = """
            {
              "files": {
                "office-1-a.json": { "content": "{\"prefix\":\"1\",\"deviceId\":\"guidA\",\"deviceName\":\"PK-1\",\"reportedAt\":\"2026-08-13T10:00:00Z\"}" },
                "office-1-b.json": { "content": "{\"prefix\":\"1\",\"deviceId\":\"guidB\",\"deviceName\":\"PK-2\",\"reportedAt\":\"2026-08-13T09:00:00Z\"}" }
              }
            }
            """;
            var handler = new TestHttpMessageHandler(req =>
            {
                if (req.Method == HttpMethod.Patch)
                {
                    patchCount++;
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(gistJson) };
            });
            using var http = new HttpClient(handler);

            int deleted = await OfficeReportService.CleanupDuplicatesAsync(http);

            Assert.Equal(0, deleted);
            Assert.Equal(0, patchCount);
        }

        [Fact]
        public async Task CleanupDuplicatesAsync_ServerError_ReturnsMinusOne()
        {
            // В gist есть дубль (иначе PATCH не отправляется и нечему падать).
            var gistJson = """
            {
              "files": {
                "office-1-new.json": { "content": "{\"prefix\":\"1\",\"deviceId\":\"guidB\",\"deviceName\":\"PK-1\",\"reportedAt\":\"2026-08-13T10:00:00Z\"}" },
                "office-1-dup.json": { "content": "{\"prefix\":\"1\",\"deviceId\":\"guidA\",\"deviceName\":\"PK-1\",\"reportedAt\":\"2026-08-13T09:00:00Z\"}" }
              }
            }
            """;
            var handler = new TestHttpMessageHandler(req =>
                req.Method == HttpMethod.Patch
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(gistJson) });
            using var http = new HttpClient(handler);

            int deleted = await OfficeReportService.CleanupDuplicatesAsync(http);

            Assert.Equal(-1, deleted);
        }
    }
}
