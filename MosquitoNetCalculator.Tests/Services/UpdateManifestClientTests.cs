using System;
using System.Net;
using System.Net.Http;
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


    }
}
