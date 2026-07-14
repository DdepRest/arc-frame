using System;
using System.IO;
using System.Security.Cryptography;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// SHA-256 hash verification for downloaded update archives.
    ///
    /// Extracted from <see cref="UpdateService"/> (Phase 2 refactoring).
    /// Pure file-I/O + crypto — no network, no UI, no static state.
    /// </summary>
    public static class UpdateVerifier
    {
        /// <summary>
        /// Computes the SHA-256 hash of a file as a lowercase hex string.
        /// Throws if the file does not exist or cannot be read.
        /// </summary>
        public static string ComputeSha256(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            byte[] hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// Verifies that a file's SHA-256 hash matches the expected value.
        /// Comparison is case-insensitive (both sides are lowercased).
        /// </summary>
        /// <param name="filePath">Path to the file to verify.</param>
        /// <param name="expectedSha256">Expected hash as a hex string (case-insensitive).</param>
        /// <returns>True if the computed hash matches the expected hash.</returns>
        public static bool VerifyHash(string filePath, string expectedSha256)
        {
            if (string.IsNullOrEmpty(expectedSha256))
                return false;

            string actualHash = ComputeSha256(filePath);
            return string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
    }
}
