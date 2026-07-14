using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Downloads update archives with progress reporting and retry logic.
    ///
    /// Extracted from <see cref="UpdateService"/> (Phase 2 refactoring).
    /// Handles HTTP download, progress reporting (0–100%), and transient
    /// error retries with exponential backoff.
    /// </summary>
    public static class UpdateDownloader
    {
        private const int DownloadRetries = 3;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Downloads a file from <paramref name="url"/> to <paramref name="destinationPath"/>,
        /// reporting progress 0–100 via <paramref name="progress"/>.
        /// <paramref name="httpClient"/> allows injection of a mock for testing.
        /// </summary>
        public static async Task DownloadWithProgressAsync(
            string url, string destinationPath, IProgress<int> progress, HttpClient? httpClient = null)
        {
            var ownsClient = httpClient == null;
            var http = httpClient ?? UpdateManifestClient.CreateConfiguredHttpClient(TimeSpan.FromMinutes(10));
            try
            {
                for (int attempt = 0; attempt <= DownloadRetries; attempt++)
                {
                    try
                    {
                        using var response = await http.GetAsync(UpdateManifestClient.CacheBustUrl(url),
                            HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

                        response.EnsureSuccessStatusCode();

                        long? totalBytes = response.Content.Headers.ContentLength;

                        using var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                        using var fileStream = new FileStream(destinationPath, FileMode.Create,
                            FileAccess.Write, FileShare.None, 8192, useAsync: true);

                        var buffer = new byte[8192];
                        long totalRead = 0;
                        int bytesRead;
                        int lastPercent = -1;

                        while ((bytesRead = await contentStream.ReadAsync(
                            buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead).ConfigureAwait(false);
                            totalRead += bytesRead;

                            if (totalBytes.HasValue && totalBytes.Value > 0)
                            {
                                int percent = (int)(totalRead * 100 / totalBytes.Value);
                                if (percent != lastPercent)
                                {
                                    lastPercent = percent;
                                    progress.Report(percent);
                                }
                            }
                        }

                        if (!totalBytes.HasValue || totalBytes.Value == 0)
                            progress.Report(100);

                        return; // success
                    }
                    catch (Exception ex) when (attempt < DownloadRetries && IsTransient(ex))
                    {
                        TryDelete(destinationPath);
                        Debug.WriteLine($"[UpdateDownloader] Download attempt {attempt + 1}/{DownloadRetries + 1} failed (transient): {ex.Message}");
                        await Task.Delay(RetryDelay * (attempt + 1)).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                if (ownsClient) http.Dispose();
            }
        }

        /// <summary>
        /// Determines whether an exception is transient (worth retrying).
        /// </summary>
        public static bool IsTransient(Exception ex) =>
            ex is HttpRequestException
            || ex is IOException
            || ex is SocketException
            || ex is TaskCanceledException;

        /// <summary>
        /// Tries to delete a file, swallowing any exceptions.
        /// </summary>
        public static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* best effort */ }
        }
    }
}
