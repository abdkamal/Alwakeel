using System.Text;

namespace Wakeel.Crypto;

/// <summary>
/// Platform bound protection of a small secret. The Windows host supplies a DPAPI backed
/// implementation; this project only defines the contract so it stays portable.
/// </summary>
public interface IPlatformProtector
{
    /// <summary>Short stable name recorded next to a machine wrap.</summary>
    string Name { get; }

    byte[] Protect(ReadOnlySpan<byte> data, ReadOnlySpan<byte> entropy);

    byte[] Unprotect(ReadOnlySpan<byte> protectedData, ReadOnlySpan<byte> entropy);
}

/// <summary>
/// A protector that binds nothing to the machine. It exists so that tests and
/// non Windows builds can exercise the machine wrap path.
/// It must never be used in a shipped installation.
/// </summary>
public sealed class NullPlatformProtector : IPlatformProtector
{
    private static readonly byte[] Root = Encoding.UTF8.GetBytes("wakeel.null-platform-protector.v1");

    public string Name => "none";

    public byte[] Protect(ReadOnlySpan<byte> data, ReadOnlySpan<byte> entropy)
    {
        var key = DeriveKey(entropy);
        return Aead.Encrypt(key, data, entropy);
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedData, ReadOnlySpan<byte> entropy)
    {
        var key = DeriveKey(entropy);
        return Aead.Decrypt(key, protectedData, entropy);
    }

    private static byte[] DeriveKey(ReadOnlySpan<byte> entropy) =>
        Hkdf.DeriveKey(Root, Aead.KeySize, entropy, "wakeel.null-protector");
}
