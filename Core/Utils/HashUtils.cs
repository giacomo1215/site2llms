using System.Security.Cryptography;
using System.Text;

namespace site2llms.Core.Utils;

/// <summary>
/// Hash helpers used for deterministic cache identity.
/// </summary>
public static class HashUtils
{
    /// <summary>
    /// Computes a lowercase SHA-256 hex digest for the provided text.
    /// </summary>
    /// <param name="text">Input content; null is treated as empty string.</param>
    /// <returns>Lowercase hexadecimal SHA-256 digest.</returns>
    public static string Sha256(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? ""));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}