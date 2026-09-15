using System.Security.Cryptography;

namespace Wakeel.Crypto;

/// <summary>
/// SHA-256 over buffers and streams, with hex and base64url renderings.
/// </summary>
public static class Sha256
{
    public const int HashSize = 32;

    public static byte[] Hash(ReadOnlySpan<byte> data) => SHA256.HashData(data);

    public static byte[] Hash(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return SHA256.HashData(stream);
    }

    public static async Task<byte[]> HashAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    public static string HashHex(ReadOnlySpan<byte> data) => ToHex(Hash(data));

    public static string HashHex(Stream stream) => ToHex(Hash(stream));

    public static string HashBase64Url(ReadOnlySpan<byte> data) => Base64Url.Encode(Hash(data));

    public static string HashBase64Url(Stream stream) => Base64Url.Encode(Hash(stream));

    /// <summary>Lower case hexadecimal rendering, the form stored in container manifests.</summary>
    public static string ToHex(ReadOnlySpan<byte> hash) => Convert.ToHexString(hash).ToLowerInvariant();

    /// <summary>Constant time comparison of two hashes or tags.</summary>
    public static bool FixedTimeEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) =>
        CryptographicOperations.FixedTimeEquals(left, right);
}
