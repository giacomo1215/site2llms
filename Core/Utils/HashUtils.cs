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
        var byteCount = Encoding.UTF8.GetByteCount(text ?? "");
        var buffer    = System.Buffers.ArrayPool<byte>.Shared.Rent(byteCount);

        try
        {
            var bytesWritten = Encoding.UTF8.GetBytes(text ?? "", buffer);
            var hashBytes    = SHA256.HashData(buffer.AsSpan(0, bytesWritten));
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
        catch
        {
            // In case of any unexpected error, return a fallback hash value.
            return "";
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }

        // var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? ""));
        // return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}