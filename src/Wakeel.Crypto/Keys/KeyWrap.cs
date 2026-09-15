using System.Text;

namespace Wakeel.Crypto;

/// <summary>How a wrapped key can be opened again.</summary>
public enum KeyWrapKind
{
    /// <summary>Opened with the account password through Argon2id.</summary>
    Password,

    /// <summary>Opened with the printed recovery code through Argon2id.</summary>
    Recovery,

    /// <summary>Opened by the machine itself, for re-entry after an automatic lock only.</summary>
    Machine,

    /// <summary>Sealed to the organisation X25519 public key so the administrator can open the copy.</summary>
    Admin,
}

/// <summary>
/// One wrapped copy of a symmetric key. Several wraps of the same key live side by side
/// in the installation key file, one per way of opening it.
/// </summary>
public sealed record KeyWrap(
    KeyWrapKind Kind,
    Argon2Params? Kdf,
    byte[] Salt,
    byte[] Nonce,
    byte[] Ciphertext,
    int Version)
{
    public const int CurrentVersion = 1;

    public bool Equals(KeyWrap? other) =>
        other is not null
        && Kind == other.Kind
        && Version == other.Version
        && Equals(Kdf, other.Kdf)
        && BytesEqual(Salt, other.Salt)
        && BytesEqual(Nonce, other.Nonce)
        && BytesEqual(Ciphertext, other.Ciphertext);

    public override int GetHashCode() =>
        HashCode.Combine(Kind, Version, Kdf, Convert.ToHexString(Ciphertext ?? []).GetHashCode(StringComparison.Ordinal));

    private static bool BytesEqual(byte[]? left, byte[]? right) =>
        left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);
}

/// <summary>Builds and opens <see cref="KeyWrap"/> values.</summary>
public static class KeyWraps
{
    /// <summary>Context label of the database key, part of every wrap's associated data.</summary>
    public const string DbKeyContext = "wakeel.dbkey";

    /// <summary>Context label of the vault key.</summary>
    public const string VaultKeyContext = "wakeel.vaultkey";

    public static KeyWrap FromPassword(string password, ReadOnlySpan<byte> keyToWrap, Argon2Params kdf, string context)
    {
        ArgumentNullException.ThrowIfNull(kdf);
        return FromSecret(KeyWrapKind.Password, Encoding.UTF8.GetBytes(password ?? string.Empty), keyToWrap, kdf, context);
    }

    public static KeyWrap FromRecoveryCode(RecoveryCode code, ReadOnlySpan<byte> keyToWrap, Argon2Params kdf, string context)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(kdf);
        return FromSecret(KeyWrapKind.Recovery, code.KeyMaterial, keyToWrap, kdf, context);
    }

    public static KeyWrap FromMachine(IPlatformProtector protector, ReadOnlySpan<byte> keyToWrap, string context)
    {
        ArgumentNullException.ThrowIfNull(protector);
        var entropy = Entropy(KeyWrapKind.Machine, context);
        var protectedBytes = protector.Protect(keyToWrap, entropy);
        return new KeyWrap(KeyWrapKind.Machine, null, [], [], protectedBytes, KeyWrap.CurrentVersion);
    }

    public static KeyWrap ForAdmin(ReadOnlySpan<byte> orgAgreementPublicKey, ReadOnlySpan<byte> keyToWrap, string context)
    {
        var sealedKey = DeviceIdentity.SealFor(orgAgreementPublicKey, keyToWrap, AdminLabel(context));
        return new KeyWrap(KeyWrapKind.Admin, null, [], [], sealedKey, KeyWrap.CurrentVersion);
    }

    public static byte[] OpenWithPassword(KeyWrap wrap, string password, string context) =>
        OpenWithSecret(wrap, KeyWrapKind.Password, Encoding.UTF8.GetBytes(password ?? string.Empty), context);

    public static byte[] OpenWithRecoveryCode(KeyWrap wrap, RecoveryCode code, string context)
    {
        ArgumentNullException.ThrowIfNull(code);
        return OpenWithSecret(wrap, KeyWrapKind.Recovery, code.KeyMaterial, context);
    }

    public static byte[] OpenWithMachine(KeyWrap wrap, IPlatformProtector protector, string context)
    {
        ArgumentNullException.ThrowIfNull(wrap);
        ArgumentNullException.ThrowIfNull(protector);
        Expect(wrap, KeyWrapKind.Machine);

        try
        {
            return protector.Unprotect(wrap.Ciphertext, Entropy(KeyWrapKind.Machine, context));
        }
        catch (CryptoException exception) when (exception.Code == ErrorCode.Tampered)
        {
            throw new CryptoException(ErrorCode.WrongPassword, "This machine cannot open the stored key.", exception);
        }
    }

    public static byte[] OpenAsAdmin(KeyWrap wrap, DeviceIdentity orgIdentity, string context)
    {
        ArgumentNullException.ThrowIfNull(wrap);
        ArgumentNullException.ThrowIfNull(orgIdentity);
        Expect(wrap, KeyWrapKind.Admin);

        try
        {
            return orgIdentity.Open(wrap.Ciphertext, AdminLabel(context));
        }
        catch (CryptoException exception) when (exception.Code == ErrorCode.Tampered)
        {
            throw new CryptoException(ErrorCode.WrongPassword, "These administration keys do not open this wrap.", exception);
        }
    }

    private static KeyWrap FromSecret(
        KeyWrapKind kind,
        byte[] secret,
        ReadOnlySpan<byte> keyToWrap,
        Argon2Params kdf,
        string context)
    {
        kdf.Validate();
        var derived = Argon2Kdf.DeriveKey(secret, kdf, Aead.KeySize);
        var nonce = RandomBytes.Next(Aead.NonceSize);
        var ciphertext = Aead.EncryptWithNonce(derived, nonce, keyToWrap, Entropy(kind, context));
        return new KeyWrap(kind, kdf, (byte[])kdf.Salt.Clone(), nonce, ciphertext, KeyWrap.CurrentVersion);
    }

    private static byte[] OpenWithSecret(KeyWrap wrap, KeyWrapKind kind, byte[] secret, string context)
    {
        ArgumentNullException.ThrowIfNull(wrap);
        Expect(wrap, kind);
        if (wrap.Kdf is null)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The stored key is missing its derivation settings.");
        }

        var derived = Argon2Kdf.DeriveKey(secret, wrap.Kdf, Aead.KeySize);
        try
        {
            return Aead.DecryptWithNonce(derived, wrap.Nonce, wrap.Ciphertext, Entropy(kind, context));
        }
        catch (CryptoException exception) when (exception.Code == ErrorCode.Tampered)
        {
            throw new CryptoException(ErrorCode.WrongPassword, "The secret does not open this key.", exception);
        }
    }

    private static void Expect(KeyWrap wrap, KeyWrapKind kind)
    {
        if (wrap.Version != KeyWrap.CurrentVersion)
        {
            throw new CryptoException(ErrorCode.UnknownKind, "The stored key uses an unsupported format version.");
        }

        if (wrap.Kind != kind)
        {
            throw new CryptoException(ErrorCode.UnknownKind, "The stored key cannot be opened this way.");
        }

        // The salt is carried twice: once on its own and once inside the derivation settings.
        // Only the settings are ever used, so a disagreement means the file was edited.
        if (wrap.Kdf is not null
            && wrap.Kdf.Salt is not null
            && !(wrap.Salt ?? []).AsSpan().SequenceEqual(wrap.Kdf.Salt))
        {
            throw new CryptoException(ErrorCode.Corrupt, "The stored key carries two different salts.");
        }
    }

    private static byte[] Entropy(KeyWrapKind kind, string context) =>
        Encoding.UTF8.GetBytes($"{context}|{kind.ToString().ToLowerInvariant()}|v{KeyWrap.CurrentVersion}");

    private static string AdminLabel(string context) => $"wakeel.adminwrap|{context}";
}
