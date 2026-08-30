using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MosquitoNetCalculator.Tests.Helpers;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Direct unit tests for <see cref="UpdateManifestClient"/> — the manifest
    /// fetcher extracted from UpdateService (Phase 2). These tests exercise the
    /// component directly, not through the UpdateService proxy.
    /// </summary>
    public class UpdateManifestClientTests
    {
        // ─── CacheBustUrl ───────────────────────────────────────────

        [Fact]
        public void CacheBustUrl_AppendsTimestampQueryParameter()
        {
            const string url = "https://example.com/releases.json";
            var result = UpdateManifestClient.CacheBustUrl(url);

            Assert.StartsWith(url + "?t=", result);
            Assert.True(result.Length > url.Length + 3);
        }

        [Fact]
        public void CacheBustUrl_DifferentCallsProduceDifferentUrls()
        {
            var a = UpdateManifestClient.CacheBustUrl("https://example.com/x");
            Thread.Sleep(1); // ensure ticks differ
            var b = UpdateManifestClient.CacheBustUrl("https://example.com/x");

            Assert.NotEqual(a, b);
        }

        // ─── CreateConfiguredHttpClient ───────────────────────────────

        [Fact]
        public void CreateConfiguredHttpClient_SetsTimeout()
        {
            var timeout = TimeSpan.FromSeconds(42);
            using var http = UpdateManifestClient.CreateConfiguredHttpClient(timeout);

            Assert.Equal(timeout, http.Timeout);
        }

        [Fact]
        public void CreateConfiguredHttpClient_SetsUserAgent()
        {
            using var http = UpdateManifestClient.CreateConfiguredHttpClient(TimeSpan.FromSeconds(1));

            Assert.Contains("MosquitoNetCalculator", http.DefaultRequestHeaders.UserAgent.ToString());
        }

        // ─── FetchManifestAsync ───────────────────────────────────────

        [Fact]
        public async Task FetchManifestAsync_Success_ReturnsDeserializedManifest()
        {
            var manifest = new UpdateManifest
            {
                Latest = "3.36.2",
                Releases = new()
                {
                    new ReleaseInfo { Version = "3.36.2", Url = "https://example.com/v.zip", Sha256 = "abc" }
                }
            };
            var json = JsonSerializer.Serialize(manifest);
            var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
            using var http = new HttpClient(handler);

            var result = await UpdateManifestClient.FetchManifestAsync(http);

            Assert.NotNull(result);
            Assert.Equal("3.36.2", result!.Latest);
            Assert.Single(result.Releases);
            Assert.Equal("3.36.2", result.Releases[0].Version);
        }

        [Fact]
        public async Task FetchManifestAsync_NonSuccessStatusCode_ReturnsNull()
        {
            var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
            using var http = new HttpClient(handler);

            var result = await UpdateManifestClient.FetchManifestAsync(http);

            Assert.Null(result);
        }

        [Fact]
        public async Task FetchManifestAsync_HttpException_ReturnsNull()
        {
            var handler = new TestHttpMessageHandler(_ => throw new HttpRequestException("network down"));
            using var http = new HttpClient(handler);

            var result = await UpdateManifestClient.FetchManifestAsync(http);

            Assert.Null(result);
        }

        [Fact]
        public async Task FetchManifestAsync_InvalidJson_ReturnsNull()
        {
            var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not json")
            });
            using var http = new HttpClient(handler);

            var result = await UpdateManifestClient.FetchManifestAsync(http);

            Assert.Null(result);
        }

        // ─── Устойчивость: raw → api.github.com → raw ────────────────────────

        private static string ManifestJson(string latest) => JsonSerializer.Serialize(new UpdateManifest
        {
            Latest = latest,
            Releases = new() { new ReleaseInfo { Version = latest, Url = $"https://example.com/{latest}.zip" } }
        });

        private static string ApiEnvelope(string innerJson) => JsonSerializer.Serialize(new
        {
            content = Convert.ToBase64String(Encoding.UTF8.GetBytes(innerJson)),
            encoding = "base64",
        });

        private static IEnumerable<string> SplitChunks(string s, int size)
        {
            for (int i = 0; i < s.Length; i += size)
                yield return s.Substring(i, Math.Min(size, s.Length - i));
        }

        [Fact]
        public async Task FetchManifestAsync_RawFails_ApiFallback_ReturnsManifestFromApi()
        {
            int rawCalls = 0, apiCalls = 0;
            var handler = new TestHttpMessageHandler(req =>
            {
                if (req.RequestUri?.ToString().Contains("api.github.com") == true)
                {
                    apiCalls++;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(ApiEnvelope(ManifestJson("3.48.4")))
                    };
                }
                rawCalls++;
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            });
            using var http = new HttpClient(handler);

            var result = await UpdateManifestClient.FetchManifestAsync(http);

            Assert.NotNull(result);
            Assert.Equal("3.48.4", result!.Latest);
            Assert.Equal(1, rawCalls);  // первая raw-попытка
            Assert.Equal(1, apiCalls);  // фолбэк сработал, третьей raw-попытки нет
        }

        [Fact]
        public async Task FetchManifestDiagnosticsAsync_AllAttemptsFail_ReturnsNullWithError()
        {
            int calls = 0;
            var handler = new TestHttpMessageHandler(_ =>
            {
                calls++;
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            });
            using var http = new HttpClient(handler);

            var result = await UpdateManifestClient.FetchManifestDiagnosticsAsync(http);

            Assert.Null(result.Manifest);
            Assert.NotNull(result.Error);
            Assert.Contains("HTTP 500", result.Error!);
            Assert.Equal(4, result.Attempts.Count); // raw → api → raw → jsDelivr
        }

        [Fact]
        public async Task FetchManifestDiagnosticsAsync_GitHubBlocked_JsDelivrLastResort_ReturnsManifest()
        {
            // VPN-сценарий: провайдер блокирует GitHub-домены целиком (raw и api
            // падают), а не-GitHub CDN jsDelivr отдаёт манифест — детект без VPN.
            int rawCalls = 0, apiCalls = 0, jsdCalls = 0;
            var handler = new TestHttpMessageHandler(req =>
            {
                var url = req.RequestUri?.ToString() ?? "";
                if (url.Contains("jsdelivr"))
                {
                    jsdCalls++;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(ManifestJson("3.48.5"))
                    };
                }
                if (url.Contains("api.github.com"))
                {
                    apiCalls++;
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }
                rawCalls++;
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            });
            using var http = new HttpClient(handler);

            var result = await UpdateManifestClient.FetchManifestDiagnosticsAsync(http);

            Assert.NotNull(result.Manifest);
            Assert.Equal("3.48.5", result.Manifest!.Latest);
            Assert.Equal(4, result.Attempts.Count); // порядок: raw → api → raw → jsDelivr
            Assert.Equal(2, rawCalls);
            Assert.Equal(1, apiCalls);
            Assert.Equal(1, jsdCalls);
            Assert.True(result.Attempts[3].Ok, "jsDelivr — последняя попытка и единственный успех");
        }

        [Fact]
        public async Task FetchManifestAsync_Success_ShortCircuits_SingleRequest()
        {
            int calls = 0;
            var handler = new TestHttpMessageHandler(_ =>
            {
                calls++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(ManifestJson("3.48.4"))
                };
            });
            using var http = new HttpClient(handler);

            var result = await UpdateManifestClient.FetchManifestAsync(http);

            Assert.NotNull(result);
            Assert.Equal(1, calls); // успех с первой попытки — api не вызывается
        }

        [Fact]
        public async Task FetchManifestDiagnosticsAsync_RawTimeout_ApiFallback_TimeoutReasonInAttempts()
        {
            var handler = new TestHttpMessageHandler(req =>
            {
                if (req.RequestUri?.ToString().Contains("api.github.com") == true)
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(ApiEnvelope(ManifestJson("3.48.5")))
                    };
                throw new TaskCanceledException("simulated timeout"); // так же выглядит таймаут HttpClient
            });
            using var http = new HttpClient(handler);

            var result = await UpdateManifestClient.FetchManifestDiagnosticsAsync(http);

            Assert.NotNull(result.Manifest);
            Assert.Equal("3.48.5", result.Manifest!.Latest);
            Assert.Equal("таймаут 10 с", result.Attempts[0].Detail);
        }

        // ─── Разбор base64-конверта api.github.com ────────────────────────────

        [Fact]
        public void ParseApiEnvelope_ValidBase64_ReturnsManifest()
        {
            var parsed = UpdateManifestClient.ParseApiEnvelope(ApiEnvelope(ManifestJson("3.48.4")));

            Assert.NotNull(parsed);
            Assert.Equal("3.48.4", parsed!.Latest);
        }

        [Fact]
        public void ParseApiEnvelope_MissingContent_ReturnsNull()
        {
            Assert.Null(UpdateManifestClient.ParseApiEnvelope("{\"name\":\"releases.json\"}"));
        }

        [Fact]
        public void ParseApiEnvelope_NotBase64Encoding_ReturnsNull()
        {
            Assert.Null(UpdateManifestClient.ParseApiEnvelope(
                "{\"content\":\"e30=\",\"encoding\":\"none\"}"));
        }

        [Fact]
        public void ParseApiEnvelope_BadBase64_ReturnsNull()
        {
            Assert.Null(UpdateManifestClient.ParseApiEnvelope(
                "{\"content\":\"!!!not-base64!!!\",\"encoding\":\"base64\"}"));
        }

        [Fact]
        public void ParseApiEnvelope_Base64WithNewlines_DecodesFine()
        {
            // GitHub API переносит строки внутри base64 — FromBase64String это терпит.
            var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(ManifestJson("3.48.4")));
            var wrapped = string.Join("\n", SplitChunks(b64, 60));
            var json = JsonSerializer.Serialize(new { content = wrapped, encoding = "base64" });

            var parsed = UpdateManifestClient.ParseApiEnvelope(json);

            Assert.NotNull(parsed);
            Assert.Equal("3.48.4", parsed!.Latest);
        }
    }
}
