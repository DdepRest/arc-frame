using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Чистые тесты атомарного переезда устройства между офисами:
    /// ComputeOldOfficeFilesToDelete + ComputeStaleBindingsToDelete.
    /// Гарантия системы: устройство числится ровно в одном офисе.
    /// </summary>
    public class OfficeRebindingPureTests
    {
        private static OfficeReportFile File(
            string fileName, string prefix, string deviceId, string deviceName, string reportedAt,
            string version = "3.49.0") => new(fileName, new OfficeReport
        {
            Prefix = prefix,
            DeviceId = deviceId,
            DeviceName = deviceName,
            Version = version,
            ReportedAt = reportedAt,
        });

        // ─── ComputeOldOfficeFilesToDelete: переезд = удалить старые файлы ───

        [Fact]
        public void MoveBetweenOffices_DeletesNamedAndLegacyOldOfficeFiles()
        {
            var toDelete = OfficeReportService.ComputeOldOfficeFilesToDelete("1", "2", "devA");
            Assert.Equal(new[] { "office-1-devA.json", "office-1.json" }, toDelete);
        }

        [Fact]
        public void SameOffice_ReturnsEmpty()
        {
            Assert.Empty(OfficeReportService.ComputeOldOfficeFilesToDelete("2", "2", "devA"));
        }

        [Fact]
        public void EmptyLastPrefix_FirstReport_ReturnsEmpty()
        {
            Assert.Empty(OfficeReportService.ComputeOldOfficeFilesToDelete("", "2", "devA"));
            Assert.Empty(OfficeReportService.ComputeOldOfficeFilesToDelete("   ", "2", "devA"));
        }

        // ─── ComputeStaleBindingsToDelete: забытые привязки к чужим офисам ───

        [Fact]
        public void CrossOfficeDuplicate_NewestWins_FreshLoserKept()
        {
            // Устройство PK-1 отчиталось в офис 2 (новее) и офис 1 (свежий дубль <24 ч) —
            // свежий чужой файл НЕ удаляется (две живые копии на одном ПК).
            var now = DateTimeOffset.Parse("2026-09-06T12:00:00Z");
            var files = new[]
            {
                File("office-2-pk1.json", "2", "pk1", "PK-1", "2026-09-06T11:00:00Z"),
                File("office-1-pk1.json", "1", "pk1", "PK-1", "2026-09-06T10:00:00Z"),
            };

            var toDelete = OfficeReportService.ComputeStaleBindingsToDelete(files, now, TimeSpan.FromHours(24));

            Assert.Empty(toDelete);
        }

        [Fact]
        public void CrossOfficeDuplicate_StaleLoserDeleted()
        {
            var now = DateTimeOffset.Parse("2026-09-06T12:00:00Z");
            var files = new[]
            {
                File("office-2-pk1.json", "2", "pk1", "PK-1", "2026-09-06T11:00:00Z"),
                File("office-1-pk1.json", "1", "pk1", "PK-1", "2026-09-04T10:00:00Z"), // >24 ч
            };

            var toDelete = OfficeReportService.ComputeStaleBindingsToDelete(files, now, TimeSpan.FromHours(24));

            Assert.Equal(new[] { "office-1-pk1.json" }, toDelete);
        }

        [Fact]
        public void SingleOfficeDevice_Untouched()
        {
            var now = DateTimeOffset.Parse("2026-09-06T12:00:00Z");
            var files = new[]
            {
                File("office-1-a.json", "1", "a", "PK-1", "2026-08-01T10:00:00Z"),
                File("office-2-b.json", "2", "b", "PK-2", "2026-08-01T10:00:00Z"),
            };

            Assert.Empty(OfficeReportService.ComputeStaleBindingsToDelete(files, now, TimeSpan.FromHours(24)));
        }

        [Fact]
        public void LegacyFilesWithoutDevice_Untouched()
        {
            // Легаси-файлы (без deviceId и имени) не привязать к машине — не трогаем.
            var now = DateTimeOffset.Parse("2026-09-06T12:00:00Z");
            var files = new[]
            {
                File("office-1.json", "1", "", "", "2026-08-01T10:00:00Z"),
                File("office-2.json", "2", "", "", "2026-08-01T10:00:00Z"),
            };

            Assert.Empty(OfficeReportService.ComputeStaleBindingsToDelete(files, now, TimeSpan.FromHours(24)));
        }

        [Fact]
        public void UnreadableTime_TreatedStale()
        {
            var now = DateTimeOffset.Parse("2026-09-06T12:00:00Z");
            var files = new[]
            {
                File("office-2-pk1.json", "2", "pk1", "PK-1", "2026-09-06T11:00:00Z"),
                File("office-1-pk1.json", "1", "pk1", "PK-1", ""),
            };

            var toDelete = OfficeReportService.ComputeStaleBindingsToDelete(files, now, TimeSpan.FromHours(24));

            Assert.Equal(new[] { "office-1-pk1.json" }, toDelete);
        }

        [Fact]
        public void GroupingByName_SameDeviceDifferentIds_CrossOfficeCleanup()
        {
            // Тот же ПК (имя PK-1) с двумя deviceId в двух офисах: новейший офис 2,
            // старый офис 1 молчит 30 ч → удаляется (дедупликация по имени машины).
            var now = DateTimeOffset.Parse("2026-09-06T12:00:00Z");
            var files = new[]
            {
                File("office-2-guidA.json", "2", "guidA", "PK-1", "2026-09-06T11:00:00Z"),
                File("office-1-guidB.json", "1", "guidB", "PK-1", "2026-09-05T06:00:00Z"),
            };

            var toDelete = OfficeReportService.ComputeStaleBindingsToDelete(files, now, TimeSpan.FromHours(24));

            Assert.Equal(new[] { "office-1-guidB.json" }, toDelete);
        }

        [Fact]
        public void UnionWithStaleDuplicates_NoFileNameCollisions()
        {
            // Регресс к реализации автоочистки: объединение двух вычислений
            // (дубли + чужие офисы) не дублирует имена в PATCH.
            var now = DateTimeOffset.UtcNow;
            string fresh = now.AddHours(-1).ToString("o");
            string stale = now.AddHours(-30).ToString("o");
            var files = new[]
            {
                File("office-1-new.json", "1", "guidB", "PK-1", fresh),
                File("office-1-dup.json", "1", "guidA", "PK-1", stale),
                File("office-2-guidA.json", "2", "guidA", "PK-1", stale),
            };

            var combined = OfficeReportService.ComputeStaleDuplicateFilesToDelete(files, now, TimeSpan.FromHours(24))
                .Union(OfficeReportService.ComputeStaleBindingsToDelete(files, now, TimeSpan.FromHours(24)))
                .ToList();

            Assert.Equal(2, combined.Count);
            Assert.Equal(combined.Count, combined.Distinct().Count());
            Assert.DoesNotContain("office-1-new.json", combined);
        }
    }

    /// <summary>
    /// HTTP-тесты переезда: SendReportAsync удаляет файл старого офиса тем же
    /// PATCH, которым пишет новый; LastReportedPrefix сохраняется только при успехе.
    /// Коллекция "FileSystem" — общий SettingsPath с другими тестами настроек.
    /// </summary>
    [Collection("FileSystem")]
    public class OfficeRebindingHttpTests : IDisposable
    {
        /// <summary>
        /// Симулятор удалённого хранилища (gist): ПРИМЕНЯЕТ тело каждого PATCH
        /// к своему словарю файлов (create/delete) — и только при успешном
        /// ответе, как настоящий gist. Позволяет проверять ИТОГОВОЕ состояние
        /// удалённого хранилища после КАЖДОГО из последовательных отчётов,
        /// а не только тело одного запроса (сквозная сходимость переезда).
        /// </summary>
        private sealed class RemoteGist
        {
            /// <summary>Файлы хранилища: имя → содержимое отчёта.</summary>
            public Dictionary<string, string> Files { get; } = new();

            /// <summary>Все принятые PATCH: (тело, созданные, удалённые).</summary>
            public List<(string Body, List<string> Created, List<string> Deleted)> Requests { get; } = new();

            /// <summary>Ответ на СЛЕДУЮЩИЙ запрос (подмена для сценария сбоя).</summary>
            public Func<HttpResponseMessage> NextResponse { get; set; } =
                () => new HttpResponseMessage(HttpStatusCode.OK);

            public TestHttpMessageHandler Handler() => new(req =>
            {
                var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                var response = NextResponse();

                // Успешный ответ = изменения применены (как у gist); сбой = хранилище не тронуто.
                if (response.IsSuccessStatusCode)
                {
                    var created = new List<string>();
                    var deleted = new List<string>();
                    using var doc = JsonDocument.Parse(body);
                    foreach (var p in doc.RootElement.GetProperty("files").EnumerateObject())
                    {
                        if (p.Value.ValueKind == JsonValueKind.Null)
                        {
                            Files.Remove(p.Name);
                            deleted.Add(p.Name);
                        }
                        else
                        {
                            Files[p.Name] = p.Value.GetProperty("content").GetString()!;
                            created.Add(p.Name);
                        }
                    }
                    Requests.Add((body, created, deleted));
                }
                return response;
            });

            /// <summary>Все файлы, чьё имя указывает на это устройство
            /// («office-N-{deviceId}.json»): инвариант «ровно один».</summary>
            public List<string> FileNamesFor(string deviceId) =>
                Files.Keys.Where(k => k.Contains(deviceId, StringComparison.Ordinal)).OrderBy(k => k).ToList();
        }
        private readonly string _tempDir;
        private readonly string _originalSettingsPath;
        private readonly string _originalToken;
        private readonly string _originalOrdersDir;
        private readonly string _originalPrefix;
        private readonly string _originalLastReportedPrefix;

        public OfficeRebindingHttpTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "mosquito_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            _originalSettingsPath = AppSettingsService.SettingsPath;
            AppSettingsService.SettingsPath = Path.Combine(_tempDir, "settings.json");

            _originalToken = AppSettingsService.LoadOfficeReportToken();
            AppSettingsService.SaveOfficeReportToken("test-gist-token-123");

            _originalOrdersDir = OrderStorageService.OrdersDir;
            OrderStorageService.OrdersDir = Path.Combine(_tempDir, "orders");

            _originalPrefix = AppSettingsService.LoadContractPrefix();
            _originalLastReportedPrefix = AppSettingsService.LoadLastReportedPrefix();
        }

        public void Dispose()
        {
            OrderStorageService.OrdersDir = _originalOrdersDir;
            AppSettingsService.SaveOfficeReportToken(_originalToken);
            AppSettingsService.SaveContractPrefix(_originalPrefix);
            AppSettingsService.SaveLastReportedPrefix(_originalLastReportedPrefix);
            AppSettingsService.SettingsPath = _originalSettingsPath;
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }

        [Fact]
        public async Task SendReportAsync_AfterOfficeMove_PatchCreatesNewAndDeletesOldAtomically()
        {
            AppSettingsService.SaveContractPrefix("2");
            AppSettingsService.SaveLastReportedPrefix("1");

            string? patchBody = null;
            var handler = new TestHttpMessageHandler(req =>
            {
                patchBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            using var http = new HttpClient(handler);

            var ok = await OfficeReportService.SendReportAsync(http);

            Assert.True(ok);
            Assert.NotNull(patchBody);
            var deviceId = AppSettingsService.LoadOrCreateDeviceId();

            // Создание нового файла офиса 2 и УДАЛЕНИЕ старого офиса 1 — одним PATCH.
            Assert.Contains(OfficeReportService.ReportFileName("2", deviceId), patchBody!);
            Assert.Contains($"\"office-1-{deviceId}.json\":null", patchBody!.Replace("\\u0022", "\""));
            Assert.Contains("\"office-1.json\":null", patchBody.Replace("\\u0022", "\""));

            // Префикс запомнен только после успеха.
            Assert.Equal("2", AppSettingsService.LoadLastReportedPrefix());
        }

        [Fact]
        public async Task SendReportAsync_SameOffice_NoDeleteEntries()
        {
            AppSettingsService.SaveContractPrefix("3");
            AppSettingsService.SaveLastReportedPrefix("3");

            string? patchBody = null;
            var handler = new TestHttpMessageHandler(req =>
            {
                patchBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            using var http = new HttpClient(handler);

            await OfficeReportService.SendReportAsync(http);

            Assert.DoesNotContain("null", patchBody);
            Assert.Equal("3", AppSettingsService.LoadLastReportedPrefix());
        }

        [Fact]
        public async Task SendReportAsync_ServerError_LastReportedPrefixNotSaved()
        {
            AppSettingsService.SaveContractPrefix("2");
            AppSettingsService.SaveLastReportedPrefix("1");

            var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
            using var http = new HttpClient(handler);

            var ok = await OfficeReportService.SendReportAsync(http);

            Assert.False(ok);
            // Старая привязка сохранена — переезд повторится на следующем отчёте.
            Assert.Equal("1", AppSettingsService.LoadLastReportedPrefix());
        }

        // ─── Сквозная сходимость: последовательные реальные SendReportAsync
        // против симулятора хранилища; инвариант «ровно один файл устройства».

        [Fact]
        public async Task Convergence_MoveBetweenOffices_TwoReports_RemoteHoldsExactlyOneFilePerDevice()
        {
            AppSettingsService.SaveContractPrefix("1");
            AppSettingsService.SaveLastReportedPrefix(""); // первое появление устройства
            var gist = new RemoteGist();
            using var http = new HttpClient(gist.Handler());

            // Отчёт 1: устройство появляется в офисе 1.
            Assert.True(await OfficeReportService.SendReportAsync(http));
            var deviceId = AppSettingsService.LoadOrCreateDeviceId();
            Assert.Equal(new[] { $"office-1-{deviceId}.json" }, gist.FileNamesFor(deviceId));
            Assert.Single(gist.Files);
            Assert.Equal("1", AppSettingsService.LoadLastReportedPrefix());

            // Устройство ПЕРЕЕХАЛО: админ сменил офис на этом ПК (как в продукте —
            // только локальная настройка, никаких прямых правок хранилища).
            AppSettingsService.SaveContractPrefix("2");

            // Отчёт 2: переезд ОДНИМ PATCH — create нового и delete старого в одном теле
            // (одна ревизия gist, частичное применение невозможно).
            Assert.True(await OfficeReportService.SendReportAsync(http));

            // Итоговое состояние хранилища: ровно один файл устройства.
            Assert.Equal(new[] { $"office-2-{deviceId}.json" }, gist.FileNamesFor(deviceId));
            Assert.Single(gist.Files);

            // Переезд выполнен во втором запросе одним телом.
            Assert.Equal(2, gist.Requests.Count);
            Assert.Contains($"office-2-{deviceId}.json", gist.Requests[1].Created);
            Assert.Contains($"office-1-{deviceId}.json", gist.Requests[1].Deleted);
            Assert.Equal("2", AppSettingsService.LoadLastReportedPrefix());
        }

        [Fact]
        public async Task Convergence_FailedPatchBetweenReports_KeepsOldBinding_NextSuccessPerformsMove()
        {
            AppSettingsService.SaveContractPrefix("1");
            AppSettingsService.SaveLastReportedPrefix("");
            var gist = new RemoteGist();
            using var http = new HttpClient(gist.Handler());

            // Отчёт 1: устройство в офисе 1.
            Assert.True(await OfficeReportService.SendReportAsync(http));
            var deviceId = AppSettingsService.LoadOrCreateDeviceId();

            // Переезд в офис 2, но сеть/хранилище легли.
            AppSettingsService.SaveContractPrefix("2");
            gist.NextResponse = () => new HttpResponseMessage(HttpStatusCode.InternalServerError);

            Assert.False(await OfficeReportService.SendReportAsync(http));

            // Сбой посреди переезда: хранилище НЕ тронуто — устройство всё ещё
            // ТОЛЬКО в офисе 1; локальный префикс не переписан (переезд повторится).
            Assert.Equal(new[] { $"office-1-{deviceId}.json" }, gist.FileNamesFor(deviceId));
            Assert.Single(gist.Files);
            Assert.Equal("1", AppSettingsService.LoadLastReportedPrefix());

            // Связь вернулась: следующий штатный отчёт выполняет переезд.
            gist.NextResponse = () => new HttpResponseMessage(HttpStatusCode.OK);
            Assert.True(await OfficeReportService.SendReportAsync(http));

            Assert.Equal(new[] { $"office-2-{deviceId}.json" }, gist.FileNamesFor(deviceId));
            Assert.Single(gist.Files);
            Assert.Contains($"office-1-{deviceId}.json", gist.Requests[^1].Deleted);
        }
    }
}
