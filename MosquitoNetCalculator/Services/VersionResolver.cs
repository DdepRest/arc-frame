using System;
using System.Diagnostics;
using System.Reflection;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Version parsing, comparison, and resolution logic.
    ///
    /// Extracted from <see cref="UpdateService"/> (Phase 2 refactoring).
    /// All methods are pure functions with no side effects — safe to call
    /// from any thread, no static state, no dependencies on WPF or network.
    /// </summary>
    public static class VersionResolver
    {
        // ─── Broken-version detection (startup banner) ──────────────
        //
        // Versions in the half-open interval [BrokenVersionStart, BrokenVersionEnd)
        // ship with a known-broken auto-update flow. MainWindow_Loaded shows
        // a Warning toast with a manual-install URL for users in this range.
        //
        // Maintenance: bump BrokenVersionEnd every time a fix is published.
        //
        // History:
        //   • v3.40.0 — Original broken release (ResourceReferenceKeyNotFound
        //                Exception in OnUpdateProgressChanged aborts the
        //                auto-update flow).
        //   • v3.40.1 — First patch (FindResource → TryFindResource;
        //                moved Storyboards to Window.Resources).
        //   • v3.40.2 — Belt-and-suspenders try/catch + this banner.
        //
        // Why compare on (Major, Minor, Build) only? `Version(3,40,1,0) >
        // Version(3,40,1)` is true because Revision 0 > -1, even though both
        // represent the same release (InformationalVersion vs. FileVersion
        // parsing paths). Comparing on (Major, Minor, Build) only avoids the
        // footgun and produces the expected equal-or-greater result for both
        // 3-part and 4-part Version objects.
        private const int BrokenVersionMajor = 3;
        private const int BrokenVersionMinor = 40;
        private const int BrokenVersionStartBuild = 0;
        private const int BrokenVersionEndBuild   = 2;

        /// <summary>
        /// Safely parses a version string, returning null on failure
        /// (null input, empty whitespace, or malformed value).
        /// </summary>
        public static Version? ParseSafe(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return null;
            try { return new Version(version); }
            catch { return null; }
        }

        /// <summary>
        /// Strips the git commit hash suffix (e.g., "3.34.4+abc123" → "3.34.4")
        /// that .NET SDK automatically appends to
        /// <c>AssemblyInformationalVersionAttribute</c> when source control
        /// info is available. Also strips any pre-release suffix after '-'.
        /// </summary>
        public static string? StripVersionSuffix(string? version)
        {
            if (string.IsNullOrEmpty(version))
                return version;
            int plusIdx = version.IndexOf('+');
            if (plusIdx >= 0)
                version = version.Substring(0, plusIdx);
            int dashIdx = version.IndexOf('-');
            if (dashIdx >= 0)
                version = version.Substring(0, dashIdx);
            return version;
        }

        /// <summary>
        /// Returns <c>true</c> if <paramref name="version"/> falls into the
        /// known-broken-for-auto-update half-open interval
        /// <c>[3.40.0, 3.40.2)</c>.
        ///
        /// Comparison ignores <c>Revision</c> on purpose — we look only at
        /// <c>(Major, Minor, Build)</c>. This avoids the <see cref="Version"/>
        /// comparison footgun where <c>Version(3,40,1,0) &gt; Version(3,40,1)</c>
        /// because Revision <c>0 &gt; -1</c>, even though both represent the
        /// same release.
        /// </summary>
        public static bool IsBrokenForAutoUpdate(Version? version)
        {
            if (version == null) return false;

            return version.Major == BrokenVersionMajor
                && version.Minor == BrokenVersionMinor
                && version.Build >= BrokenVersionStartBuild
                && version.Build < BrokenVersionEndBuild;
        }

        /// <summary>
        /// Резолвит версию из произвольной сборки.
        /// Приоритет источников (от качественного к фоллбэку):
        /// 1. <c>AssemblyInformationalVersionAttribute</c> — содержит «3.34.4+gitHash»
        ///    при наличии source control, отсекаем суффикс → чистый 3-part.
        /// 2. <c>AssemblyFileVersionAttribute</c> — без git hash, но содержит 4-ю часть.
        /// 3. <c>Assembly.GetName().Version</c> — legacy fallback (в single-file обычно null).
        /// При ошибке — null (caller подставит 0.0.0 + запишет в Debug).
        /// </summary>
        public static Version? ResolveVersion(Assembly? assembly)
        {
            try
            {
                if (assembly == null) return null;

                // 1. InformationalVersion (3.34.4+gitHash) — лучший display-источник.
                var infoAttr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                var vInfo = ParseSafe(StripVersionSuffix(infoAttr?.InformationalVersion));
                if (vInfo != null)
                {
                    Debug.WriteLine($"[VersionResolver] Resolved via InformationalVersion='{infoAttr?.InformationalVersion}' -> {vInfo}");
                    return vInfo;
                }

                // 2. FileVersion (3.34.4.0) — fallback.
                var fileAttr = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
                var vFile = ParseSafe(fileAttr?.Version);
                if (vFile != null)
                {
                    Debug.WriteLine($"[VersionResolver] Resolved via FileVersion='{fileAttr?.Version}' -> {vFile}");
                    return vFile;
                }

                // 3. GetName().Version — legacy fallback.
                var nameVer = assembly.GetName().Version;
                if (nameVer != null)
                {
                    Debug.WriteLine($"[VersionResolver] Resolved via GetName().Version -> {nameVer}");
                    return nameVer;
                }

                Debug.WriteLine("[VersionResolver] All version sources failed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VersionResolver] Version resolve exception: {ex}");
            }
            return null;
        }

        /// <summary>
        /// Analyzes an update manifest against a reference version and returns
        /// the newest <see cref="ReleaseInfo"/> if an update is available,
        /// or <c>null</c> if the app is up-to-date or the manifest is invalid.
        /// </summary>
        public static ReleaseInfo? GetAvailableUpdate(UpdateManifest? manifest, Version currentVersion)
        {
            if (manifest == null || manifest.Releases.Count == 0)
                return null;

            // Guard against an unresolved (0.0.0) current version — otherwise every
            // manifest with a parseable latest version would look like an update.
            if (currentVersion.Major == 0 && currentVersion.Minor == 0 && currentVersion.Build == 0)
                return null;

            Version? latestVersion = ParseSafe(manifest.Latest);
            if (latestVersion == null)
                return null;

            if (latestVersion <= currentVersion)
                return null;

            return manifest.Releases[0]; // newest first
        }
    }
}
