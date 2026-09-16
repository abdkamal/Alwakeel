using Wakeel.Core.Data;
using Wakeel.Crypto;

namespace Wakeel.UI.Services.Account;

/// <summary>
/// The encrypted file store under <c>vault\</c> (ARCHITECTURE.md §2 and §8): every file is kept
/// under the SHA-256 of its original bytes, encrypted with a sub key derived from the vault key, so
/// two files never share an encryption key and the same content is never stored twice.
/// </summary>
/// <remarks>
/// The hash is taken of the file as it arrived, before encryption, which is what lets the health
/// centre later prove that a stored original is still exactly the original.
/// </remarks>
public static class VaultStore
{
    /// <summary>Label separating vault file keys from every other key derived from the vault key.</summary>
    private const string SubKeyLabel = "wakeel.vault.file";

    /// <summary>Writes one file into the vault and returns the hash it is stored under.</summary>
    public static string Write(WakeelPaths paths, ReadOnlySpan<byte> vaultKey, ReadOnlySpan<byte> content)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var hashHex = Sha256.HashHex(content);
        var target = paths.VaultFilePath(hashHex);
        var directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(target))
        {
            return hashHex;
        }

        var sealedBytes = Aead.Encrypt(SubKey(vaultKey, hashHex), content, hashHex);

        // Through a temporary name so a half written file can never be mistaken for a stored one.
        var temporary = target + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(sealedBytes);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, target, overwrite: true);
        return hashHex;
    }

    /// <summary>Reads one file back, or returns null when the vault does not hold it.</summary>
    public static byte[]? Read(WakeelPaths paths, ReadOnlySpan<byte> vaultKey, string sha256Hex)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256Hex);

        var path = paths.VaultFilePath(sha256Hex);
        if (!File.Exists(path))
        {
            return null;
        }

        return Aead.Decrypt(SubKey(vaultKey, sha256Hex), File.ReadAllBytes(path), sha256Hex);
    }

    private static byte[] SubKey(ReadOnlySpan<byte> vaultKey, string sha256Hex) =>
        Hkdf.DeriveKey(vaultKey, Aead.KeySize, Convert.FromHexString(sha256Hex), SubKeyLabel);
}
