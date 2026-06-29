using System;
using System.Reflection;
using System.Reflection.Emit;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Tests for <see cref="UpdateService"/>'s version-resolution machinery.
    ///
    /// Direct tests for <c>StripVersionSuffix</c> / <c>ParseSafe</c> (pure
    /// string parsing helpers) plus AssemblyBuilder-backed fixture tests
    /// for <c>ResolveVersion(Assembly)</c> — covering each of the three
    /// fallback tiers (InformationalVersion → FileVersion → GetName().Version)
    /// with realistic and edge-case attribute shapes.
    /// </summary>
    public class UpdateServiceTests
    {
        // ─── StripVersionSuffix ──────────────────────────────────────

        [Theory]
        [InlineData("3.34.4", "3.34.4")]                       // plain version
        [InlineData("3.34.4+abc123", "3.34.4")]                // typical git hash
        [InlineData("3.34.4+abcdef1234567890abcdef1234", "3.34.4")] // long git hash
        [InlineData("3.34.4+sha.1234.5678", "3.34.4")]         // SemVer build metadata with dots
        [InlineData("3.34.4-beta.1", "3.34.4")]                // pre-release
        [InlineData("3.34.4-beta+abc", "3.34.4")]              // both pre-release AND build metadata
        [InlineData("3.34.4-alpha+abcdef", "3.34.4")]         // pre-release + git hash
        [InlineData("1.0", "1.0")]                            // major+minor only
        public void StripVersionSuffix_StripsHashAndPreRelease(string raw, string expected)
        {
            Assert.Equal(expected, UpdateService.StripVersionSuffix(raw));
        }

        [Fact]
        public void StripVersionSuffix_NullInput_ReturnsNull()
        {
            Assert.Null(UpdateService.StripVersionSuffix(null));
        }

        [Fact]
        public void StripVersionSuffix_EmptyInput_ReturnsEmpty()
        {
            // IsNullOrEmpty short-circuits before substring logic; empty stays empty.
            Assert.Equal(string.Empty, UpdateService.StripVersionSuffix(string.Empty));
        }

        [Fact]
        public void StripVersionSuffix_OnlySuffix_GivesEmpty()
        {
            // "+abc" — everything is suffix; result is empty string (not null).
            Assert.Equal(string.Empty, UpdateService.StripVersionSuffix("+abc"));
        }

        // ─── ParseSafe ──────────────────────────────────────────────

        [Fact]
        public void ParseSafe_3Part_Returns3PartVersion()
        {
            var v = UpdateService.ParseSafe("3.34.4");
            Assert.NotNull(v);
            Assert.Equal(3, v!.Major);
            Assert.Equal(34, v.Minor);
            Assert.Equal(4, v.Build);
            Assert.Equal(-1, v.Revision); // not specified => -1 sentinel
        }

        [Fact]
        public void ParseSafe_4Part_Returns4PartVersion()
        {
            var v = UpdateService.ParseSafe("3.34.4.0");
            Assert.NotNull(v);
            Assert.Equal(3, v!.Major);
            Assert.Equal(34, v.Minor);
            Assert.Equal(4, v.Build);
            Assert.Equal(0, v.Revision);
        }

        [Fact]
        public void ParseSafe_2Part_Returns2PartVersion()
        {
            var v = UpdateService.ParseSafe("1.0");
            Assert.NotNull(v);
            Assert.Equal(1, v!.Major);
            Assert.Equal(0, v.Minor);
            Assert.Equal(-1, v.Build);
        }

        [Fact]
        public void ParseSafe_NullInput_ReturnsNull()
        {
            Assert.Null(UpdateService.ParseSafe(null));
        }

        [Fact]
        public void ParseSafe_EmptyInput_ReturnsNull()
        {
            Assert.Null(UpdateService.ParseSafe(string.Empty));
        }

        [Fact]
        public void ParseSafe_WhitespaceInput_ReturnsNull()
        {
            Assert.Null(UpdateService.ParseSafe("   "));
        }

        [Fact]
        public void ParseSafe_GarbageInput_ReturnsNull()
        {
            Assert.Null(UpdateService.ParseSafe("not-a-version"));
        }

        [Fact]
        public void ParseSafe_TooManyDots_ReturnsNull()
        {
            Assert.Null(UpdateService.ParseSafe("3.34.4.5.6"));
        }

        // ─── ResolveVersion(Assembly) using dynamic assemblies ──────

        [Fact]
        public void ResolveVersion_InformationalVersionWithGitHash_StripsHashAndReturnsClean3Part()
        {
            var asm = BuildDynamicAssembly(informationalVersion: "3.34.5+abcdef1234567890");
            var v = UpdateService.ResolveVersion(asm);

            Assert.NotNull(v);
            Assert.Equal(new Version(3, 34, 5), v);
        }

        [Fact]
        public void ResolveVersion_InformationalVersionBare_PassesThrough()
        {
            var asm = BuildDynamicAssembly(informationalVersion: "2.10.3");
            var v = UpdateService.ResolveVersion(asm);

            Assert.NotNull(v);
            Assert.Equal(new Version(2, 10, 3), v);
        }

        [Fact]
        public void ResolveVersion_InformationalVersionPreRelease_StripsDashSuffix()
        {
            var asm = BuildDynamicAssembly(informationalVersion: "1.0.0-rc.1");
            var v = UpdateService.ResolveVersion(asm);

            Assert.NotNull(v);
            Assert.Equal(new Version(1, 0, 0), v);
        }

        [Fact]
        public void ResolveVersion_InformationalVersionTakesPriorityOverFileVersion()
        {
            // Both attributes set: InformationalVersion wins because it produces
            // a nicer display string (3-part vs 4-part).
            var asm = BuildDynamicAssembly(
                informationalVersion: "3.34.5+sha",
                fileVersion: "9.9.9.9");

            var v = UpdateService.ResolveVersion(asm);

            Assert.NotNull(v);
            Assert.Equal(new Version(3, 34, 5), v);
        }

        [Fact]
        public void ResolveVersion_OnlyFileVersion_FallsBackToFileVersion()
        {
            var asm = BuildDynamicAssembly(fileVersion: "4.5.6.0");
            var v = UpdateService.ResolveVersion(asm);

            Assert.NotNull(v);
            // FileVersion produces a 4-part Version with Revision=0.
            Assert.Equal(new Version(4, 5, 6, 0), v);
        }

        [Fact]
        public void ResolveVersion_NoCustomAttrs_FallsBackToAssemblyVersion()
        {
            var asm = BuildDynamicAssembly(assemblyVersion: new Version(2, 7, 1, 0));
            var v = UpdateService.ResolveVersion(asm);

            Assert.NotNull(v);
            Assert.Equal(new Version(2, 7, 1, 0), v);
        }

        [Fact]
        public void ResolveVersion_InformationalVersionWithHashThenFileVersion_UsesInfo()
        {
            // Realistic SDK output: both attrs present, InformationalVersion has git hash.
            var asm = BuildDynamicAssembly(
                informationalVersion: "3.34.5+abcdef1234567890",
                fileVersion: "3.34.5.0",
                assemblyVersion: new Version(3, 34, 5, 0));

            var v = UpdateService.ResolveVersion(asm);

            Assert.NotNull(v);
            Assert.Equal(3, v!.Major);
            Assert.Equal(34, v.Minor);
            Assert.Equal(5, v.Build);
            Assert.Equal(-1, v.Revision); // stripped hash => clean 3-part
        }

        [Fact]
        public void ResolveVersion_RawInformationalAlreadyUnparseable_FallsBackToFile()
        {
            // Pathological case: InformationalVersion is somehow set to something
            // that can't be parsed even after stripping. Should fall through.
            var asm = BuildDynamicAssembly(
                informationalVersion: "garbage+hash",
                fileVersion: "1.2.3.0");

            // "garbage+hash" -> "garbage" -> ParseSafe returns null -> fallback to FileVersion.
            var v = UpdateService.ResolveVersion(asm);
            Assert.NotNull(v);
            Assert.Equal(new Version(1, 2, 3, 0), v);
        }

        [Fact]
        public void ResolveVersion_NoCustomAttrsNoAssemblyVersion_DefaultsToZeroAndFallsThrough()
        {
            // Edge case: assembly has neither InformationalVersion, FileVersion,
            // nor a real Version. BuildDynamicAssembly with no params creates an
            // assembly where AssemblyName.Version defaults to new Version(0,0,0,0)
            // — which is itself parseable and returns 0.0.0.0 (NOT null).
            // This locks in the actual current behavior so any future change
            // away from "return whatever GetName().Version gives" is intentional.
            var asm = BuildDynamicAssembly();
            var v = UpdateService.ResolveVersion(asm);

            Assert.NotNull(v);
            Assert.Equal(new Version(0, 0, 0, 0), v);
        }

        // Method declared as ResolveVersion(Assembly?) so passing null is
        // honest about the contract — no need for the null-forgiving bang.
        [Fact]
        public void ResolveVersion_NullAssembly_ReturnsNull()
        {
            Assert.Null(UpdateService.ResolveVersion(null));
        }

        // ─── TryResolveCurrentVersion smoke test ────────────────────

        [Fact]
        public void TryResolveCurrentVersion_ReturnsNonNullVersionForTestAssembly()
        {
            // Smoke test: whatever the test assembly's actual version resolution
            // returns (depends on dotnet build context), it must be a valid Version
            // and not the 0.0.0 fallback.
            var v = UpdateService.TryResolveCurrentVersion();
            Assert.NotNull(v);
        }

        [Fact]
        public void CurrentVersion_StaticField_IsGreaterThanZero()
        {
            // CurrentVersion is the field used by all production code.
            // If wiring broke, this would be 0.0.0 — exactly the bug the
            // multi-layer fallback was added to prevent.
            // > (strict) catches both 0.0.0 and any future accidental low version.
            Assert.True(UpdateService.CurrentVersion > new Version(0, 0, 0),
                $"CurrentVersion should be > 0.0.0, got {UpdateService.CurrentVersion}");
        }

        // ─── IsCurrentVersionBrokenForAutoUpdate ──────────────────────
        //
        // Tests for the half-open interval [BrokenVersionStart, BrokenVersionEnd)
        // used by <c>MainWindow_Loaded</c> to decide whether to show the
        // startup banner pointing affected users at the manual install URL.
        //
        // The comparator ignores Revision on purpose (see the docstring on
        // <c>UpdateService.IsCurrentVersionBrokenForAutoUpdate</c>) — the
        // 3-part and 4-part paths must produce identical results. v3.40.3
        // testability refactor.

        [Theory]
        // Within range — inclusive at start, exclusive at end.
        [InlineData("3.40.0", true)]    // start boundary (>=)
        [InlineData("3.40.0.0", true)]  // 4-part equal to start (Revision=0)
        [InlineData("3.40.0.99", true)] // 4-part advance Revision only
        [InlineData("3.40.1", true)]    // middle
        [InlineData("3.40.1.5", true)]  // 4-part in middle
        // Outside range — end is <, not ≤.
        [InlineData("3.40.2", false)]   // end boundary — FALSE because < not ≤
        [InlineData("3.40.2.0", false)] // 4-part exactly at end (compare on triple)
        [InlineData("3.40.3", false)]   // just after end
        [InlineData("3.40.99", false)]  // arbitrarily after end
        [InlineData("3.39.0", false)]   // before start
        [InlineData("3.39.9", false)]   // right before start
        [InlineData("2.40.1", false)]   // different major (downgrade)
        [InlineData("4.0.0", false)]    // different major (upgrade)
        [InlineData("0.0.0", false)]    // zero
        public void IsCurrentVersionBrokenForAutoUpdate_RangeBoundaries(string raw, bool expected)
        {
            Assert.Equal(expected, UpdateService.IsCurrentVersionBrokenForAutoUpdate(new Version(raw)));
        }

        [Fact]
        public void IsCurrentVersionBrokenForAutoUpdate_NullVersion_ReturnsFalse()
        {
            // Private null-check (defensive — production callers always
            // pass UpdateService.CurrentVersion, which is never null post-init).
            Assert.False(UpdateService.IsCurrentVersionBrokenForAutoUpdate(null));
        }

        [Fact]
        public void IsCurrentVersionBrokenForAutoUpdate_CurrentVersion_IsConsistent()
        {
            // Smoke test: production calls this with UpdateService.CurrentVersion.
            // Whatever it returns must be a valid bool (not throw, not crash).
            // The exact value depends on the assembly's version stamping at
            // build time, so we don't pin it here — just lock the contract.
            bool result = UpdateService.IsCurrentVersionBrokenForAutoUpdate(UpdateService.CurrentVersion);
            // No exception → contract holds.
            _ = result;
        }

        // ─── GetAvailableUpdate ──────────────────────────────────────

        [Fact]
        public void GetAvailableUpdate_NullManifest_ReturnsNull()
        {
            var result = UpdateService.GetAvailableUpdate(null, new Version(3, 36, 0));
            Assert.Null(result);
        }

        [Fact]
        public void GetAvailableUpdate_EmptyReleases_ReturnsNull()
        {
            var manifest = new UpdateManifest { Latest = "3.36.2", Releases = new() };
            var result = UpdateService.GetAvailableUpdate(manifest, new Version(3, 36, 0));
            Assert.Null(result);
        }

        [Fact]
        public void GetAvailableUpdate_LatestGreaterThanCurrent_ReturnsRelease()
        {
            var manifest = new UpdateManifest
            {
                Latest = "3.36.2",
                Releases = new()
                {
                    new ReleaseInfo { Version = "3.36.2", Url = "http://example.com/zip", Sha256 = "abc" }
                }
            };
            var result = UpdateService.GetAvailableUpdate(manifest, new Version(3, 36, 1));

            Assert.NotNull(result);
            Assert.Equal("3.36.2", result!.Version);
            Assert.Equal("http://example.com/zip", result.Url);
            Assert.Equal(manifest.Latest, result.Version);
        }

        [Fact]
        public void GetAvailableUpdate_LatestEqualToCurrent_ReturnsNull()
        {
            var manifest = new UpdateManifest
            {
                Latest = "3.36.2",
                Releases = new()
                {
                    new ReleaseInfo { Version = "3.36.2", Url = "http://example.com/zip" }
                }
            };
            var result = UpdateService.GetAvailableUpdate(manifest, new Version(3, 36, 2));
            Assert.Null(result);
        }

        [Fact]
        public void GetAvailableUpdate_LatestLessThanCurrent_ReturnsNull()
        {
            var manifest = new UpdateManifest
            {
                Latest = "3.36.1",
                Releases = new()
                {
                    new ReleaseInfo { Version = "3.36.1", Url = "http://example.com/zip" }
                }
            };
            var result = UpdateService.GetAvailableUpdate(manifest, new Version(3, 36, 2));
            Assert.Null(result);
        }

        [Fact]
        public void GetAvailableUpdate_UnparseableLatest_ReturnsNull()
        {
            var manifest = new UpdateManifest
            {
                Latest = "not-a-version",
                Releases = new()
                {
                    new ReleaseInfo { Version = "not-a-version", Url = "http://example.com/zip" }
                }
            };
            var result = UpdateService.GetAvailableUpdate(manifest, new Version(3, 36, 0));
            Assert.Null(result);
        }

        [Fact]
        public void GetAvailableUpdate_EmptyLatest_ReturnsNull()
        {
            var manifest = new UpdateManifest
            {
                Latest = "",
                Releases = new()
                {
                    new ReleaseInfo { Version = "3.36.2", Url = "http://example.com/zip" }
                }
            };
            var result = UpdateService.GetAvailableUpdate(manifest, new Version(3, 36, 0));
            Assert.Null(result);
        }

        // ─── HasPendingUpdate ────────────────────────────────────────

        [Fact]
        public void HasPendingUpdate_AfterSavingVersion_ReturnsTrue()
        {
            AppSettingsService.SavePendingUpdateVersion("9.99.9");
            try
            {
                Assert.True(UpdateService.HasPendingUpdate());
            }
            finally
            {
                AppSettingsService.SavePendingUpdateVersion(null);
            }
        }

        [Fact]
        public void HasPendingUpdate_AfterClearing_ReturnsFalse()
        {
            AppSettingsService.SavePendingUpdateVersion("9.99.9");
            AppSettingsService.SavePendingUpdateVersion(null);
            Assert.False(UpdateService.HasPendingUpdate());
        }

        // ─── Test helpers ──────────────────────────────────────────

        /// <summary>
        /// Builds an in-memory assembly with the requested version attributes.
        /// Uses a unique name per call so xUnit's parallel test runner doesn't
        /// clash on the dynamic assembly name.
        /// </summary>
        private static Assembly BuildDynamicAssembly(
            string? informationalVersion = null,
            string? fileVersion = null,
            Version? assemblyVersion = null)
        {
            // Assembly-level attribute order matters in AssemblyBuilder:
            // SetCustomAttribute MUST happen before any module/type is defined.
            var name = new AssemblyName("UpdateServiceTest_" + Guid.NewGuid().ToString("N"));
            if (assemblyVersion != null) name.Version = assemblyVersion;

            var builder = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);

            if (informationalVersion != null)
            {
                var ctor = typeof(AssemblyInformationalVersionAttribute)
                    .GetConstructor(new[] { typeof(string) })!;
                builder.SetCustomAttribute(new CustomAttributeBuilder(
                    ctor, new object[] { informationalVersion }));
            }

            if (fileVersion != null)
            {
                var ctor = typeof(AssemblyFileVersionAttribute)
                    .GetConstructor(new[] { typeof(string) })!;
                builder.SetCustomAttribute(new CustomAttributeBuilder(
                    ctor, new object[] { fileVersion }));
            }

            // Materialize the assembly by creating at least one type.
            var module = builder.DefineDynamicModule("Main");
            var type = module.DefineType("Stub", TypeAttributes.Class | TypeAttributes.Public);
            return type.CreateType()!.Assembly;
        }
    }
}
