using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Downloads and deserializes the releases.json manifest from GitHub.
    ///
    /// Extracted from <see cref="UpdateService"/> (Phase 2 refactoring).
    /// Handles cache-busting, TLS configuration, and HTTP client lifecycle.
    /// </summary>
    public static class UpdateManifestClient
    {
        private const string ManifestUrl =
            "https://raw.githubusercontent.com/DdepRest/arc-frame/main/releases.json";

        /// <summary>
        /// Build a cache-busted URL by appending a unique query parameter.
        /// raw.githubusercontent.com uses a 5-minute CDN edge cache;
        /// without this, users may fetch a stale manifest or hit a
        /// race where the manifest updated but the ZIP binary hasn't
        /// propagated to all CDN nodes yet.
        /// </summary>
        public static string CacheBustUrl(string url) =>
            url + "?t=" + DateTime.UtcNow.Ticks.ToString("x");

        /// <summary>
        /// Creates a production <see cref="HttpClient"/> with explicit TLS 1.2/1.3
        /// configuration. Some Windows environments (older .NET runtimes, corporate
        /// proxies, antivirus with SSL inspection) fail to negotiate TLS when the
        /// protocol is left at the OS default. Explicitly setting SslProtocols on
        /// the handler resolves "SSL connection" errors when fetching from GitHub.
        /// </summary>
        public static HttpClient CreateConfiguredHttpClient(TimeSpan timeout)
        {
            var handler = new HttpClientHandler
            {
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12
                    | System.Security.Authentication.SslProtocols.Tls13
            };
            var http = new HttpClient(handler) { Timeout = timeout };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MosquitoNetCalculator/3.0");
            return http;
        }

        /// <summary>
        /// Fetches and deserializes the releases.json manifest from GitHub.
        /// Returns null on any failure (network, parse, etc.).
        /// <paramref name="httpClient"/> allows injection of a mock for testing.
        /// </summary>
        public static async Task<UpdateManifest?> FetchManifestAsync(HttpClient? httpClient = null)
        {
            try
            {
                var ownsClient = httpClient == null;
                var http = httpClient ?? CreateConfiguredHttpClient(TimeSpan.FromSeconds(15));
                try
                {
                    var response = await http.GetAsync(CacheBustUrl(ManifestUrl),
                        HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                        return null;

                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return JsonSerializer.Deserialize<UpdateManifest>(json);
                }
                finally
                {
                    if (ownsClient) http.Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateManifestClient] FetchManifest failed: {ex.Message}");
                return null;
            }
        }
    }
}
