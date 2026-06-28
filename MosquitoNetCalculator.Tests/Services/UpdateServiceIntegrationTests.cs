using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Integration tests for <see cref="UpdateService"/> network layer.
    /// Uses mocked <see cref="HttpMessageHandler"/> to avoid real HTTP calls.
    /// </summary>
    public class UpdateServiceIntegrationTests
    {
        private static HttpClient CreateMockClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            var mockHandler = new MockHttpMessageHandler(handler);
            return new HttpClient(mockHandler);
        }

        private static string GetTempFilePath()
            => Path.Combine(Path.GetTempPath(), $"arc-test-{Guid.NewGuid():N}.tmp");

        /// <summary>
        /// Synchronous IProgress&lt;T&gt; that records values immediately — avoids
        /// SynchronizationContext-posting flakiness that Progress&lt;T&gt; can introduce.
        /// </summary>
        private class ImmediateProgress : IProgress<int>
        {
            public System.Collections.Generic.List<int> Values { get; } = new();
            public void Report(int value) => Values.Add(value);
        }

        // ─── FetchManifestAsync tests ──────────────────────────

        [Fact]
        public async Task FetchManifestAsync_ReturnsManifest_OnSuccess()
        {
            var manifest = new UpdateManifest
            {
                Latest = "3.37.2",
                Releases = new() { new ReleaseInfo { Version = "3.37.2", Url = "http://example.com/zip", Sha256 = "abc" } }
            };
            var json = JsonSerializer.Serialize(manifest);
            var client = CreateMockClient((req, ct) =>
            {
                Assert.Contains("releases.json", req.RequestUri!.ToString());
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json)
                });
            });

            var result = await UpdateService.FetchManifestAsync(client);

            Assert.NotNull(result);
            Assert.Equal("3.37.2", result!.Latest);
            Assert.Single(result.Releases);
        }

        [Fact]
        public async Task FetchManifestAsync_ReturnsNull_OnNonSuccessStatus()
        {
            var client = CreateMockClient((req, ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

            var result = await UpdateService.FetchManifestAsync(client);

            Assert.Null(result);
        }

        [Fact]
        public async Task FetchManifestAsync_ReturnsNull_OnException()
        {
            var client = CreateMockClient((req, ct) =>
                throw new HttpRequestException("Network error"));

            var result = await UpdateService.FetchManifestAsync(client);

            Assert.Null(result);
        }

        // ─── DownloadWithProgressAsync tests ───────────────────

        [Fact]
        public async Task DownloadWithProgressAsync_DownloadsFile_AndReportsProgress()
        {
            // Use a payload larger than one buffer (8192 bytes) so progress
            // reports intermediate percentages, not just a single 100%.
            var expectedContent = new byte[20_000];
            new Random(42).NextBytes(expectedContent);
            var client = CreateMockClient((req, ct) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = new ByteArrayContent(expectedContent);
                response.Content.Headers.ContentLength = expectedContent.Length;
                return Task.FromResult(response);
            });

            var tempPath = GetTempFilePath();
            try
            {
                var progress = new ImmediateProgress();

                await UpdateService.DownloadWithProgressAsync(
                    "http://example.com/test.zip", tempPath, progress, client);

                Assert.True(File.Exists(tempPath));
                var actualContent = await File.ReadAllBytesAsync(tempPath);
                Assert.Equal(expectedContent, actualContent);
                Assert.Contains(100, progress.Values);
                // Should have reported at least one intermediate percentage
                Assert.True(progress.Values.Count > 1, "Progress should report intermediate values for a 20KB payload");
                Assert.All(progress.Values, p => Assert.InRange(p, 0, 100));
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public async Task DownloadWithProgressAsync_Throws_OnNonSuccess()
        {
            var client = CreateMockClient((req, ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

            var tempPath = GetTempFilePath();
            try
            {
                await Assert.ThrowsAsync<HttpRequestException>(() =>
                    UpdateService.DownloadWithProgressAsync(
                        "http://example.com/test.zip", tempPath, new ImmediateProgress(), client));
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public async Task DownloadWithProgressAsync_Reports100_WhenNoContentLength()
        {
            var expectedContent = "No content length provided"u8.ToArray();
            var client = CreateMockClient((req, ct) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = new ByteArrayContent(expectedContent);
                // No Content-Length header set
                return Task.FromResult(response);
            });

            var tempPath = GetTempFilePath();
            try
            {
                var progress = new ImmediateProgress();

                await UpdateService.DownloadWithProgressAsync(
                    "http://example.com/test.zip", tempPath, progress, client);

                Assert.True(File.Exists(tempPath));
                Assert.Contains(100, progress.Values);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public async Task DownloadWithProgressAsync_Reports100_WhenZeroContentLength()
        {
            var client = CreateMockClient((req, ct) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = new ByteArrayContent(Array.Empty<byte>());
                response.Content.Headers.ContentLength = 0;
                return Task.FromResult(response);
            });

            var tempPath = GetTempFilePath();
            try
            {
                var progress = new ImmediateProgress();

                await UpdateService.DownloadWithProgressAsync(
                    "http://example.com/test.zip", tempPath, progress, client);

                Assert.True(File.Exists(tempPath));
                Assert.Equal(0, new FileInfo(tempPath).Length);
                Assert.Contains(100, progress.Values);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        // ─── Mock helper ───────────────────────────────────────

        private class MockHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;

            public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
            {
                _sendAsync = sendAsync;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return _sendAsync(request, cancellationToken);
            }
        }
    }
}
