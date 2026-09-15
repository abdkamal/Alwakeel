using System.Security.Cryptography;
using System.Text;

namespace Wakeel.Crypto;

/// <summary>
/// HKDF-SHA256. Every derived key in the product carries a context label so that
/// the same input key material can never produce the same sub key for two purposes.
/// </summary>
public static class Hkdf
{
    public const int PseudoRandomKeySize = 32;

    public static byte[] DeriveKey(
        ReadOnlySpan<byte> inputKeyMaterial,
        int length,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> info)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, 0);
        var output = new byte[length];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, inputKeyMaterial, output, salt, info);
        return output;
    }

    public static byte[] DeriveKey(
        ReadOnlySpan<byte> inputKeyMaterial,
        int length,
        ReadOnlySpan<byte> salt,
        string info) =>
        DeriveKey(inputKeyMaterial, length, salt, Encoding.UTF8.GetBytes(info ?? string.Empty));

    public static byte[] DeriveKey(ReadOnlySpan<byte> inputKeyMaterial, int length, string info) =>
        DeriveKey(inputKeyMaterial, length, ReadOnlySpan<byte>.Empty, Encoding.UTF8.GetBytes(info ?? string.Empty));

    public static byte[] Extract(ReadOnlySpan<byte> inputKeyMaterial, ReadOnlySpan<byte> salt)
    {
        var prk = new byte[PseudoRandomKeySize];
        HKDF.Extract(HashAlgorithmName.SHA256, inputKeyMaterial, salt, prk);
        return prk;
    }

    public static byte[] Expand(ReadOnlySpan<byte> pseudoRandomKey, int length, ReadOnlySpan<byte> info)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, 0);
        var output = new byte[length];
        HKDF.Expand(HashAlgorithmName.SHA256, pseudoRandomKey, output, info);
        return output;
    }
}
