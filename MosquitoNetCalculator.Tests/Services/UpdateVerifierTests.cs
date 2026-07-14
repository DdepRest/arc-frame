using System;
using System.IO;
using System.Security.Cryptography;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Tests for <see cref="UpdateVerifier"/> — SHA-256 file verification
    /// extracted from UpdateService (Phase 2).
    /// </summary>
    public class UpdateVerifierTests
    {
        private static string CreateTempFile(byte[] content)
        {
            string path = Path.Combine(Path.GetTempPath(), $"arc-verifier-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(path, content);
            return path;
        }

        private static string ComputeExpectedHash(byte[] content)
        {
            using var stream = new MemoryStream(content);
            byte[] hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        [Fact]
        public void ComputeSha256_ReturnsLowercaseHexString()
        {
            var content = "Hello, World!"u8.ToArray();
            string path = CreateTempFile(content);
            try
            {
                string hash = UpdateVerifier.ComputeSha256(path);
                // SHA-256 produces 32 bytes → 64 hex chars
                Assert.Equal(64, hash.Length);
                Assert.Equal(ComputeExpectedHash(content), hash);
                Assert.DoesNotContain(hash, c => char.IsUpper(c)); // all lowercase
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ComputeSha256_EmptyFile_ReturnsValidHash()
        {
            string path = CreateTempFile(Array.Empty<byte>());
            try
            {
                string hash = UpdateVerifier.ComputeSha256(path);
                Assert.Equal(64, hash.Length);
                // Known SHA-256 of empty input
                Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", hash);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void VerifyHash_MatchingHash_ReturnsTrue()
        {
            var content = "test content for hashing"u8.ToArray();
            string expectedHash = ComputeExpectedHash(content);
            string path = CreateTempFile(content);
            try
            {
                Assert.True(UpdateVerifier.VerifyHash(path, expectedHash));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void VerifyHash_NonMatchingHash_ReturnsFalse()
        {
            var content = "test content"u8.ToArray();
            string path = CreateTempFile(content);
            try
            {
                Assert.False(UpdateVerifier.VerifyHash(path, "0000000000000000000000000000000000000000000000000000000000000000"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void VerifyHash_CaseInsensitive_ReturnsTrue()
        {
            var content = "case test"u8.ToArray();
            string expectedHash = ComputeExpectedHash(content).ToUpperInvariant();
            string path = CreateTempFile(content);
            try
            {
                Assert.True(UpdateVerifier.VerifyHash(path, expectedHash));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void VerifyHash_NullExpectedHash_ReturnsFalse()
        {
            var content = "data"u8.ToArray();
            string path = CreateTempFile(content);
            try
            {
                Assert.False(UpdateVerifier.VerifyHash(path, null!));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void VerifyHash_EmptyExpectedHash_ReturnsFalse()
        {
            var content = "data"u8.ToArray();
            string path = CreateTempFile(content);
            try
            {
                Assert.False(UpdateVerifier.VerifyHash(path, ""));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
