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
            var progress = new Progress<int>(p => progressReports.Add(p));

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
            var progress = new Progress<int>(p => progressReports.Add(p));

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
