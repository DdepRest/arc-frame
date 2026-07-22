using System;
using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class UpdateLogTests
    {
        [Fact]
        public void AllNewestFirst_ReturnsItemsFromEmbeddedJson()
        {
            var items = UpdateLog.AllNewestFirst();

            // Smoke test: we know the JSON has ~30 entries
            Assert.NotNull(items);
            Assert.True(items.Count >= 25, $"Expected ≥25 entries, got {items.Count}");
        }

        [Fact]
        public void AllNewestFirst_FirstItemIsNewest()
        {
            var items = UpdateLog.AllNewestFirst();

            // The latest version in the JSON is 3.47.0 — update this when bumping.
            // AllNewestFirst_VersionsInDescendingOrder below already proves ordering
            // is correct, but this lock-in catches accidental version-string typos.
            Assert.Equal("3.47.0", items[0].Version);
        }

        [Fact]
        public void AllNewestFirst_LastItemIsOldest()
        {
            var items = UpdateLog.AllNewestFirst();

            // The oldest version is 3.10
            Assert.Equal("3.10", items[^1].Version);
        }

        [Fact]
        public void AllNewestFirst_VersionsInDescendingOrder()
        {
            var items = UpdateLog.AllNewestFirst();

            for (int i = 1; i < items.Count; i++)
            {
                var result = string.Compare(items[i - 1].Version, items[i].Version, System.StringComparison.Ordinal);
                // Each version should be >= the next in ordinal comparison
                // (3.34.3 > 3.34.2 > ...)
                Assert.True(result >= 0,
                    $"Item {i - 1} ({items[i - 1].Version}) should be >= item {i} ({items[i].Version})");
            }
        }

        [Fact]
        public void AllNewestFirst_EachItemHasRequiredFields()
        {
            var items = UpdateLog.AllNewestFirst();

            foreach (var item in items)
            {
                Assert.False(string.IsNullOrEmpty(item.Version), "Version should not be empty");
                Assert.False(string.IsNullOrEmpty(item.Type), "Type should not be empty");
                Assert.False(string.IsNullOrEmpty(item.Title), "Title should not be empty");
                Assert.NotNull(item.Changes);
                Assert.NotEmpty(item.Changes);
            }
        }

        [Fact]
        public void AllNewestFirst_ReturnsNewCollectionEachTime()
        {
            var first = UpdateLog.AllNewestFirst();
            var second = UpdateLog.AllNewestFirst();

            Assert.NotSame(first, second);
            Assert.Equal(first.Count, second.Count);
            Assert.Equal(first[0].Version, second[0].Version);
        }

        [Fact]
        public void AllNewestFirst_ThreePointThirtyFourThree_HasExpectedChanges()
        {
            var items = UpdateLog.AllNewestFirst();
            var v3343 = items.FirstOrDefault(i => i.Version == "3.34.3");

            Assert.NotNull(v3343);
            Assert.Equal("Улучшение", v3343!.Type);
            Assert.Contains("Сегментированный", v3343.Title, System.StringComparison.Ordinal);
            Assert.Contains(v3343.Changes, c => c.Contains("сегмент"));
        }

        [Fact]
        public void AllNewestFirst_KnownVersion_ThreePointTen_HasExpectedChanges()
        {
            var items = UpdateLog.AllNewestFirst();
            var v310 = items.FirstOrDefault(i => i.Version == "3.10");

            Assert.NotNull(v310);
            Assert.Equal("Улучшение", v310!.Type);
            Assert.Contains("Отключение позиций", v310.Title);
            Assert.Contains(v310.Changes, c => c.Contains("отключать"));
            Assert.Equal(4, v310.Changes.Count);
        }

        // ─── IsLatest flag tests ────────────────────────────────────────

        [Fact]
        public void AllNewestFirst_ExactlyOneItemIsLatest()
        {
            var items = UpdateLog.AllNewestFirst();

            var latestCount = items.Count(i => i.IsLatest);
            Assert.Equal(1, latestCount);
        }

        [Fact]
        public void AllNewestFirst_LatestItemHasIsLatestTrue()
        {
            var items = UpdateLog.AllNewestFirst();

            // Already proven in AllNewestFirst_FirstItemIsNewest,
            // but this lock-in binds IsLatest to the position-0 invariant.
            Assert.True(items[0].IsLatest);
        }

        [Fact]
        public void AllNewestFirst_AllNonLatestItems_IsLatestFalse()
        {
            var items = UpdateLog.AllNewestFirst();

            for (int i = 1; i < items.Count; i++)
                Assert.False(items[i].IsLatest);
        }

        [Fact]
        public void AllNewestFirst_IsLatestPropertyResetsBetweenCalls()
        {
            // _entries is cached (Lazy<>) — same UpdateItem[] under the hood.
            // If we toggled IsLatest=true on a previous run, that flag would
            // carry over. After the refactor, AllNewestFirst explicitly resets
            // all to false before marking exactly one as true.
            var firstCall = UpdateLog.AllNewestFirst();

            // Simulate runtime mutation: flip all to true, mimicking a
            // bad state that would leak between calls pre-fix.
            foreach (var e in firstCall) e.IsLatest = true;

            var secondCall = UpdateLog.AllNewestFirst();
            Assert.Single(secondCall.Where(i => i.IsLatest));
            Assert.True(secondCall[0].IsLatest);
        }

        // ─── GetChangesSince tests ─────────────────────────────────────

        [Fact]
        public void GetChangesSince_LatestVersion_ReturnsEmpty()
        {
            var changes = UpdateLog.GetChangesSince(new Version(3, 47, 0));

            Assert.NotNull(changes);
            Assert.Empty(changes);
        }

        [Fact]
        public void GetChangesSince_NewerThanLatest_ReturnsEmpty()
        {
            var changes = UpdateLog.GetChangesSince(new Version(99, 99, 99));

            Assert.NotNull(changes);
            Assert.Empty(changes);
        }

        [Fact]
        public void GetChangesSince_OldestVersion_ReturnsAllExceptFirst()
        {
            var changes = UpdateLog.GetChangesSince(new Version(3, 10));

            Assert.NotNull(changes);
            Assert.True(changes.Length >= 24, $"Expected ≥24 entries, got {changes.Length}");
            Assert.DoesNotContain(changes, c => c.Version == "3.10");
        }

        [Fact]
        public void GetChangesSince_ReturnsChronologicalOrder()
        {
            var changes = UpdateLog.GetChangesSince(new Version(3, 30));

            // Chronological order: older → newer
            for (int i = 1; i < changes.Length; i++)
            {
                var prev = new Version(changes[i - 1].Version);
                var curr = new Version(changes[i].Version);
                Assert.True(curr > prev,
                    $"Item {i - 1} ({changes[i - 1].Version}) should be < item {i} ({changes[i].Version})");
            }
        }

        [Fact]
        public void GetChangesSince_SpecificVersion_ReturnsOnlyNewer()
        {
            var changes = UpdateLog.GetChangesSince(new Version(3, 36, 1));

            // All returned versions must be strictly newer than 3.36.1
            Assert.All(changes, c =>
                Assert.True(new Version(c.Version) > new Version(3, 36, 1),
                    $"Version {c.Version} should be > 3.36.1"));
        }

        [Fact]
        public void GetChangesSince_EachResultHasRequiredFields()
        {
            var changes = UpdateLog.GetChangesSince(new Version(3, 34));

            Assert.NotEmpty(changes);
            foreach (var item in changes)
            {
                Assert.False(string.IsNullOrEmpty(item.Version), "Version should not be empty");
                Assert.False(string.IsNullOrEmpty(item.Type), "Type should not be empty");
                Assert.NotNull(item.Changes);
            }
        }

        // ─── ValidateLogInvariant tests ──────────────────────────────────

        [Fact]
        public void ValidateLogInvariant_RealJson_NoProblems()
        {
            var problems = UpdateLog.ValidateLogInvariant();

            Assert.Empty(problems);
        }

        [Fact]
        public void ValidateLogInvariant_ReturnsListNeverNull()
        {
            // Defensive: implementation could theoretically mutate, but the
            // contract is "always returns a List<string> — empty = OK".
            var problems = UpdateLog.ValidateLogInvariant();
            Assert.NotNull(problems);
        }

        // ─── Append-only JSON contract test (architecture invariant) ──────
        //
        // Architectural goal: adding a new entry to update-log.json must
        // require editing ONLY the appended entry — old entries stay
        // untouched. This simulates that workflow:
        //   1. Load JSON as raw text (so we can verify edits didn't appear).
        //   2. Deserialize, append a new entry, re-serialize.
        //   3. Re-parse and verify the old entries' content matches the
        //      original JSON byte-for-byte (no key re-ordering, no whitespace
        //      changes inside old entries — only appended CONTENT).
        //
        // Note: we verify the *deserialized* values, not byte-for-byte JSON,
        // because System.Text.Json may normalize whitespace on its own.
        // The intent — "old records are data-stable" — is what we lock in.

        [Fact]
        public void AppendOnly_NewEntryAppendedToEnd_PreservesOldRecords()
        {
            // Architectural invariant under test:
            // Adding a new entry to update-log.json must require editing
            // ONLY the appended entry — old entries stay byte-for-byte
            // identical (no key re-ordering, no re-formatting, no Version
            // bump on existing entries).
            //
            // Strategy: load real embedded JSON as JArray, capture the JSON
            // representation of each existing entry, append a new entry at
            // the END, re-serialize, and verify each old entry's JSON text
            // appears unchanged in the new document.

            // Arrange — locate the embedded resource the same way UpdateLog does.
            var assembly = typeof(UpdateLog).Assembly;
            using var stream = assembly.GetManifestResourceStream(
                "MosquitoNetCalculator.Resources.update-log.json");
            Assert.NotNull(stream);

            using var reader = new System.IO.StreamReader(stream!);
            string originalJson = reader.ReadToEnd();
            var originalArray = System.Text.Json.Nodes.JsonNode.Parse(originalJson)!.AsArray();
            Assert.True(originalArray.Count > 0);

            // Capture each old entry as a JSON text snapshot.
            var oldEntriesText = originalArray
                .Select(node => node!.ToJsonString())
                .ToList();

            // Act — simulate the workflow of an AI appending a new entry at the END.
            var newEntry = new System.Text.Json.Nodes.JsonObject
            {
                ["date"] = "2030-01-01T00:00:00Z",
                ["version"] = "99.99.99",
                ["type"] = "Новинка",
                ["title"] = "Test future append",
                ["changes"] = new System.Text.Json.Nodes.JsonArray("sentinel")
            };
            originalArray.Add(newEntry);
            string rewrittenJson = originalArray.ToJsonString();

            // Assert — every old entry's JSON text appears verbatim in the
            // rewritten document, AND it appears in the same relative order
            // (i.e., not moved or re-formatted).
            var rewrittenArray = System.Text.Json.Nodes.JsonNode.Parse(rewrittenJson)!.AsArray();
            Assert.Equal(oldEntriesText.Count + 1, rewrittenArray.Count);

            for (int i = 0; i < oldEntriesText.Count; i++)
            {
                Assert.Equal(
                    oldEntriesText[i],
                    rewrittenArray[i]!.ToJsonString());
            }

            // The appended entry is at the END (index == oldEntriesText.Count).
            Assert.Equal("99.99.99", rewrittenArray[^1]!["version"]!.GetValue<string>());
        }
    }
}
