using System;
using System.Reflection;
using System.Reflection.Emit;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Tests for <see cref="VersionResolver"/> — the version parsing,
    /// comparison, and resolution logic extracted from UpdateService (Phase 2).
    ///
    /// These tests mirror the original UpdateServiceTests but call the
    /// extracted <see cref="VersionResolver"/> directly, verifying that
    /// the delegation preserves all behavior.
    /// </summary>
    public class VersionResolverTests
    {
        // ─── ParseSafe ──────────────────────────────────────────────

        [Fact]
        public void ParseSafe_3Part_Returns3PartVersion()
        {
            var v = VersionResolver.ParseSafe("3.34.4");
            Assert.NotNull(v);
            Assert.Equal(3, v!.Major);
            Assert.Equal(34, v.Minor);
            Assert.Equal(4, v.Build);
            Assert.Equal(-1, v.Revision);
        }

        [Fact]
        public void ParseSafe_4Part_Returns4PartVersion()
        {
            var v = VersionResolver.ParseSafe("3.34.4.0");
            Assert.NotNull(v);
            Assert.Equal(3, v!.Major);
            Assert.Equal(34, v.Minor);
            Assert.Equal(4, v.Build);
            Assert.Equal(0, v.Revision);
        }

        [Fact]
        public void ParseSafe_NullInput_ReturnsNull()
        {
            Assert.Null(VersionResolver.ParseSafe(null));
        }

        [Fact]
        public void ParseSafe_EmptyInput_ReturnsNull()
        {
            Assert.Null(VersionResolver.ParseSafe(string.Empty));
        }

        [Fact]
        public void ParseSafe_WhitespaceInput_ReturnsNull()
        {
            Assert.Null(VersionResolver.ParseSafe("   "));
        }

        [Fact]
        public void ParseSafe_GarbageInput_ReturnsNull()
        {
            Assert.Null(VersionResolver.ParseSafe("not-a-version"));
        }

        // ─── StripVersionSuffix ─────────────────────────────────────

        [Theory]
        [InlineData("3.34.4", "3.34.4")]
        [InlineData("3.34.4+abc123", "3.34.4")]
        [InlineData("3.34.4+abcdef1234567890abcdef1234", "3.34.4")]
        [InlineData("3.34.4-beta.1", "3.34.4")]
        [InlineData("3.34.4-beta+abc", "3.34.4")]
        [InlineData("1.0", "1.0")]
        public void StripVersionSuffix_StripsHashAndPreRelease(string raw, string expected)
        {
            Assert.Equal(expected, VersionResolver.StripVersionSuffix(raw));
        }

        [Fact]
        public void StripVersionSuffix_NullInput_ReturnsNull()
        {
            Assert.Null(VersionResolver.StripVersionSuffix(null));
        }

        [Fact]
        public void StripVersionSuffix_EmptyInput_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, VersionResolver.StripVersionSuffix(string.Empty));
        }

        // ─── IsBrokenForAutoUpdate ──────────────────────────────────

        [Theory]
        [InlineData("3.40.0", true)]
        [InlineData("3.40.0.0", true)]
        [InlineData("3.40.1", true)]
        [InlineData("3.40.1.5", true)]
        [InlineData("3.40.2", false)]
        [InlineData("3.40.2.0", false)]
        [InlineData("3.40.3", false)]
        [InlineData("3.39.0", false)]
        [InlineData("2.40.1", false)]
        [InlineData("4.0.0", false)]
        [InlineData("0.0.0", false)]
        public void IsBrokenForAutoUpdate_RangeBoundaries(string raw, bool expected)
        {
            Assert.Equal(expected, VersionResolver.IsBrokenForAutoUpdate(new Version(raw)));
        }

        [Fact]
        public void IsBrokenForAutoUpdate_NullVersion_ReturnsFalse()
        {
            Assert.False(VersionResolver.IsBrokenForAutoUpdate(null));
        }

        // ─── GetAvailableUpdate ─────────────────────────────────────

        [Fact]
        public void GetAvailableUpdate_NullManifest_ReturnsNull()
        {
            Assert.Null(VersionResolver.GetAvailableUpdate(null, new Version(3, 36, 0)));
        }

        [Fact]
        public void GetAvailableUpdate_EmptyReleases_ReturnsNull()
        {
            var manifest = new UpdateManifest { Latest = "3.36.2", Releases = new() };
            Assert.Null(VersionResolver.GetAvailableUpdate(manifest, new Version(3, 36, 0)));
        }

        [Fact]
        public void GetAvailableUpdate_LatestGreaterThanCurrent_ReturnsRelease()
        {
            var manifest = new UpdateManifest
            {
                Latest = "3.36.2",
                Releases = new() { new ReleaseInfo { Version = "3.36.2", Url = "http://example.com/zip", Sha256 = "abc" } }
            };
            var result = VersionResolver.GetAvailableUpdate(manifest, new Version(3, 36, 1));
            Assert.NotNull(result);
            Assert.Equal("3.36.2", result!.Version);
        }

        [Fact]
        public void GetAvailableUpdate_LatestEqualToCurrent_ReturnsNull()
        {
            var manifest = new UpdateManifest
            {
                Latest = "3.36.2",
                Releases = new() { new ReleaseInfo { Version = "3.36.2", Url = "http://x" } }
            };
            Assert.Null(VersionResolver.GetAvailableUpdate(manifest, new Version(3, 36, 2)));
        }

        [Fact]
        public void GetAvailableUpdate_ZeroVersion_ReturnsNull()
        {
            var manifest = new UpdateManifest
            {
                Latest = "3.36.2",
                Releases = new() { new ReleaseInfo { Version = "3.36.2", Url = "http://x" } }
            };
            Assert.Null(VersionResolver.GetAvailableUpdate(manifest, new Version(0, 0, 0)));
        }

        [Fact]
        public void GetAvailableUpdate_UnparseableLatest_ReturnsNull()
        {
            var manifest = new UpdateManifest
            {
                Latest = "not-a-version",
                Releases = new() { new ReleaseInfo { Version = "x", Url = "http://x" } }
            };
            Assert.Null(VersionResolver.GetAvailableUpdate(manifest, new Version(3, 36, 0)));
        }

        // ─── ResolveVersion ─────────────────────────────────────────

        [Fact]
        public void ResolveVersion_InformationalVersionWithGitHash_StripsAndReturns3Part()
        {
            var asm = BuildDynamicAssembly(informationalVersion: "3.34.5+abcdef1234567890");
            var v = VersionResolver.ResolveVersion(asm);
            Assert.NotNull(v);
            Assert.Equal(new Version(3, 34, 5), v);
        }

        [Fact]
        public void ResolveVersion_InformationalVersionPreRelease_StripsDashSuffix()
        {
            var asm = BuildDynamicAssembly(informationalVersion: "1.0.0-rc.1");
            var v = VersionResolver.ResolveVersion(asm);
            Assert.NotNull(v);
            Assert.Equal(new Version(1, 0, 0), v);
        }

        [Fact]
        public void ResolveVersion_OnlyFileVersion_FallsBack()
        {
            var asm = BuildDynamicAssembly(fileVersion: "4.5.6.0");
            var v = VersionResolver.ResolveVersion(asm);
            Assert.NotNull(v);
            Assert.Equal(new Version(4, 5, 6, 0), v);
        }

        [Fact]
        public void ResolveVersion_NullAssembly_ReturnsNull()
        {
            Assert.Null(VersionResolver.ResolveVersion(null));
        }

        // ─── Test helper ────────────────────────────────────────────

        private static Assembly BuildDynamicAssembly(
            string? informationalVersion = null,
            string? fileVersion = null,
            Version? assemblyVersion = null)
        {
            var name = new AssemblyName("VersionResolverTest_" + Guid.NewGuid().ToString("N"));
            if (assemblyVersion != null) name.Version = assemblyVersion;

            var builder = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);

            if (informationalVersion != null)
            {
                var ctor = typeof(AssemblyInformationalVersionAttribute)
                    .GetConstructor(new[] { typeof(string) })!;
                builder.SetCustomAttribute(new CustomAttributeBuilder(ctor, new object[] { informationalVersion }));
            }

            if (fileVersion != null)
            {
                var ctor = typeof(AssemblyFileVersionAttribute)
                    .GetConstructor(new[] { typeof(string) })!;
                builder.SetCustomAttribute(new CustomAttributeBuilder(ctor, new object[] { fileVersion }));
            }

            var module = builder.DefineDynamicModule("Main");
            var type = module.DefineType("Stub", TypeAttributes.Class | TypeAttributes.Public);
            return type.CreateType()!.Assembly;
        }
    }
}
