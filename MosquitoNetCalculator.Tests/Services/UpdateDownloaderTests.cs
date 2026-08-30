using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MosquitoNetCalculator.Tests.Helpers;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Direct unit tests for <see cref="UpdateDownloader"/> — the update archive
    /// downloader extracted from UpdateService (Phase 2). These tests exercise the
    /// component directly, not through the UpdateService proxy.
    /// </summary>
    public class UpdateDownloaderTests
    {
        private static string GetTempPath() =>
            Path.Combine(Path.GetTempPath(), $"arc-downloader-test-{Guid.NewGuid():N}.tmp");

        // ─── DownloadWithProgressAsync ────────────────────────────────

        [Theory]
        [InlineData("")]
        [InlineData("?t=abc")]
        [InlineData("/relative/update.zip")]
        [InlineData("ftp://example.com/update.zip")]
        public async Task DownloadWithProgressAsync_InvalidUrl_ThrowsClearError(string url)
        {
            using var http = new HttpClient(new TestHttpMessageHandler(_ =>
                throw new InvalidOperationException("HTTP must not be called for an invalid URL")));
            string destination = GetTempPath();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                UpdateDownloader.DownloadWithProgressAsync(url, destination, new Progress<int>(), http));

            Assert.Equal("В манифесте отсутствует корректная ссылка на архив обновления.", ex.Message);
            Assert.False(File.Exists(destination));
        }

        [Fact]
        public async Task DownloadWithProgressAsync_WritesFileAndReportsProgress()
        {
            var expected = "fake zip content"u8.ToArray();
            var handler = new TestHttpMessageHandler(request =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new MemoryStream(expected))
                };
                response.Content.Headers.ContentLength = expected.Length;
                return response;
            });
            using var http = new HttpClient(handler);
            string destination = GetTempPath();
            var progressReports = new System.Collections.Generic.List<int>();
            var progress = new ImmediateProgress(progressReports);

            try
            {
                await UpdateDownloader.DownloadWithProgressAsync(
                    "https://example.com/update.zip", destination, progress, http);

                Assert.True(File.Exists(destination));
                var actual = await File.ReadAllBytesAsync(destination);
                Assert.Equal(expected, actual);
                Assert.Contains(100, progressReports);
            }
            finally
            {
                UpdateDownloader.TryDelete(destination);
            }
        }

        [Fact]
        public async Task DownloadWithProgressAsync_NoContentLength_StillReports100()
        {
            var expected = "no length"u8.ToArray();
            var handler = new TestHttpMessageHandler(_ =>
            {
                // No Content-Length header set
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new MemoryStream(expected))
                };
            });
            using var http = new HttpClient(handler);
            string destination = GetTempPath();
            var progressReports = new System.Collections.Generic.List<int>();
            var progress = new ImmediateProgress(progressReports);

            try
            {
                await UpdateDownloader.DownloadWithProgressAsync(
                    "https://example.com/update.zip", destination, progress, http);

                Assert.True(File.Exists(destination));
                Assert.Contains(100, progressReports);
            }
            finally
            {
                UpdateDownloader.TryDelete(destination);
            }
        }

        [Fact]
        public async Task DownloadWithProgressAsync_TransientFailure_RetriesThenSucceeds()
        {
            var expected = "retry success"u8.ToArray();
            int attempt = 0;
            var handler = new TestHttpMessageHandler(_ =>
            {
                attempt++;
                if (attempt < 2)
                {
                    throw new HttpRequestException("transient");
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new MemoryStream(expected))
                };
            });
            using var http = new HttpClient(handler);
            string destination = GetTempPath();
            var progress = new Progress<int>(_ => { });

            try
            {
                await UpdateDownloader.DownloadWithProgressAsync(
                    "https://example.com/update.zip", destination, progress, http);

                Assert.True(File.Exists(destination));
                var actual = await File.ReadAllBytesAsync(destination);
                Assert.Equal(expected, actual);
                Assert.True(attempt >= 2);
            }
            finally
            {
                UpdateDownloader.TryDelete(destination);
            }
        }

        [Fact]
        public async Task DownloadWithProgressAsync_NonTransientFailure_Throws()
        {
            var handler = new TestHttpMessageHandler(_ =>
                throw new InvalidOperationException("non-transient"));
            using var http = new HttpClient(handler);
            string destination = GetTempPath();
            var progress = new Progress<int>(_ => { });

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                UpdateDownloader.DownloadWithProgressAsync(
                    "https://example.com/update.zip", destination, progress, http));

            UpdateDownloader.TryDelete(destination);
        }

        // ─── IsSupportedUrl ──────────────────────────────────────────

        [Theory]
        [InlineData("https://example.com/update.zip", true)]
        [InlineData("http://example.com/update.zip", true)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData("/relative/update.zip", false)]
        [InlineData("ftp://example.com/update.zip", false)]
        [InlineData("not a url", false)]
        public void IsSupportedUrl_AcceptsOnlyAbsoluteHttpUrls(string url, bool expected)
        {
            Assert.Equal(expected, UpdateDownloader.IsSupportedUrl(url));
        }

        [Fact]
        public void IsSupportedUrl_Null_ReturnsFalse()
        {
            Assert.False(UpdateDownloader.IsSupportedUrl(null));
        }

        private sealed class ImmediateProgress : IProgress<int>
        {
            private readonly System.Collections.Generic.List<int> _reports;

            public ImmediateProgress(System.Collections.Generic.List<int> reports)
            {
                _reports = reports;
            }

            public void Report(int value) => _reports.Add(value);
        }

        // ─── IsTransient ──────────────────────────────────────────────

        [Theory]
        [InlineData(typeof(HttpRequestException), true)]
        [InlineData(typeof(IOException), true)]
        [InlineData(typeof(TaskCanceledException), true)]
        [InlineData(typeof(InvalidOperationException), false)]
        [InlineData(typeof(ArgumentException), false)]
        public void IsTransient_ReturnsExpectedForExceptionType(Type exceptionType, bool expected)
        {
            Exception ex = (Exception)Activator.CreateInstance(exceptionType,
                new object?[] { "test" })!;
            Assert.Equal(expected, UpdateDownloader.IsTransient(ex));
        }

        [Fact]
        public void IsTransient_SocketException_ReturnsTrue()
        {
            Assert.True(UpdateDownloader.IsTransient(new SocketException()));
        }

        // ─── TryDelete ──────────────────────────────────────────────────

        [Fact]
        public void TryDelete_ExistingFile_RemovesFile()
        {
            string path = GetTempPath();
            File.WriteAllText(path, "delete me");

            UpdateDownloader.TryDelete(path);

            Assert.False(File.Exists(path));
        }

        [Fact]
        public void TryDelete_MissingFile_DoesNotThrow()
        {
            string path = GetTempPath();
            var ex = Record.Exception(() => UpdateDownloader.TryDelete(path));
            Assert.Null(ex);
        }

        [Fact]
        public void TryDelete_LockedFile_DoesNotThrow()
        {
            string path = GetTempPath();
            File.WriteAllText(path, "locked");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

            var ex = Record.Exception(() => UpdateDownloader.TryDelete(path));

            Assert.Null(ex);
            Assert.True(File.Exists(path));
        }


    }
}
