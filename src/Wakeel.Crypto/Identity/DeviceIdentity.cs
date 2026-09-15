using System.Text;
using NSec.Cryptography;
using NKey = NSec.Cryptography.Key;
using NPublicKey = NSec.Cryptography.PublicKey;

namespace Wakeel.Crypto;

/// <summary>
/// A device key pair set: Ed25519 for signing everything the device produces and
/// X25519 for agreeing session keys and opening sealed boxes.
/// The organisation key and the office computer key use the same type.
/// </summary>
public sealed class DeviceIdentity : IDisposable
{
    public const int PublicKeySize = 32;
    public const int SignatureSize = 64;

    private const string AgreeLabel = "wakeel.agree";
    private const string SealLabel = "wakeel.seal";

    private readonly NKey _signingKey;
    private readonly NKey _agreementKey;
    private bool _disposed;

    private DeviceIdentity(NKey signingKey, NKey agreementKey)
    {
        _signingKey = signingKey;
        _agreementKey = agreementKey;
        SigningPublicKey = signingKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        AgreementPublicKey = agreementKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);
    }

    /// <summary>Raw Ed25519 public key.</summary>
    public byte[] SigningPublicKey { get; }

    /// <summary>Raw X25519 public key.</summary>
    public byte[] AgreementPublicKey { get; }

    /// <summary>Ed25519 public key in the base64url form stored in certificates.</summary>
    public string SigningPublicKeyText => Base64Url.Encode(SigningPublicKey);

    /// <summary>X25519 public key in the base64url form stored in certificates.</summary>
    public string AgreementPublicKeyText => Base64Url.Encode(AgreementPublicKey);

    public static DeviceIdentity Generate() =>
        Import(new DeviceSeeds(RandomBytes.Next(DeviceSeeds.SeedSize), RandomBytes.Next(DeviceSeeds.SeedSize)));

    public static DeviceIdentity Import(DeviceSeeds seeds)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        seeds.Validate();

        var creation = new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
        NKey? signing = null;
        NKey? agreement = null;
        try
        {
            signing = NKey.Import(SignatureAlgorithm.Ed25519, seeds.SigningSeed, KeyBlobFormat.RawPrivateKey, in creation);
            agreement = NKey.Import(KeyAgreementAlgorithm.X25519, seeds.AgreementSeed, KeyBlobFormat.RawPrivateKey, in creation);
            return new DeviceIdentity(signing, agreement);
        }
        catch (FormatException exception)
        {
            signing?.Dispose();
            agreement?.Dispose();
            throw new CryptoException(ErrorCode.Corrupt, "The device seeds could not be imported.", exception);
        }
    }

    /// <summary>Exports the private seeds so they can be wrapped and stored.</summary>
    public DeviceSeeds Export()
    {
        ThrowIfDisposed();
        return new DeviceSeeds(
            _signingKey.Export(KeyBlobFormat.RawPrivateKey),
            _agreementKey.Export(KeyBlobFormat.RawPrivateKey));
    }

    public byte[] Sign(ReadOnlySpan<byte> data)
    {
        ThrowIfDisposed();
        return SignatureAlgorithm.Ed25519.Sign(_signingKey, data);
    }

    /// <summary>Signs and returns the base64url text stored next to a signed body.</summary>
    public string SignText(ReadOnlySpan<byte> data) => Base64Url.Encode(Sign(data));

    public static bool Verify(ReadOnlySpan<byte> signingPublicKey, ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        if (signingPublicKey.Length != PublicKeySize || signature.Length != SignatureSize)
        {
            return false;
        }

        NPublicKey publicKey;
        try
        {
            publicKey = NPublicKey.Import(SignatureAlgorithm.Ed25519, signingPublicKey, KeyBlobFormat.RawPublicKey);
        }
        catch (FormatException)
        {
            return false;
        }

        return SignatureAlgorithm.Ed25519.Verify(publicKey, data, signature);
    }

    public static bool Verify(string signingPublicKeyText, ReadOnlySpan<byte> data, string signatureText)
    {
        if (!Base64Url.TryDecode(signingPublicKeyText, out var publicKey)
            || !Base64Url.TryDecode(signatureText, out var signature))
        {
            return false;
        }

        return Verify(publicKey, data, signature);
    }

    /// <summary>
    /// Derives a shared session key with a peer. Both sides get the same bytes because the
    /// salt orders the two public keys, so neither side needs to know who started.
    /// </summary>
    public byte[] Agree(ReadOnlySpan<byte> peerAgreementPublicKey, string context, int length = 32)
    {
        ThrowIfDisposed();
        var salt = OrderedSalt(AgreementPublicKey, peerAgreementPublicKey);
        var info = Encoding.UTF8.GetBytes($"{AgreeLabel}|{context}");
        return Derive(_agreementKey, peerAgreementPublicKey, salt, info, length);
    }

    /// <summary>
    /// Sealed box: an ephemeral X25519 key pair encrypts a payload to a recipient public key.
    /// The ephemeral public key is the first thirty two bytes of the result.
    /// </summary>
    public static byte[] SealFor(ReadOnlySpan<byte> recipientAgreementPublicKey, ReadOnlySpan<byte> payload, string context)
    {
        if (recipientAgreementPublicKey.Length != PublicKeySize)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The recipient public key has the wrong length.");
        }

        var creation = new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
        using var ephemeral = NKey.Create(KeyAgreementAlgorithm.X25519, in creation);
        var ephemeralPublic = ephemeral.PublicKey.Export(KeyBlobFormat.RawPublicKey);

        var salt = Concat(ephemeralPublic, recipientAgreementPublicKey);
        var info = Encoding.UTF8.GetBytes($"{SealLabel}|{context}");
        var key = Derive(ephemeral, recipientAgreementPublicKey, salt, info, Aead.KeySize);

        var sealedPayload = Aead.Encrypt(key, payload, salt);
        var result = new byte[ephemeralPublic.Length + sealedPayload.Length];
        ephemeralPublic.CopyTo(result, 0);
        sealedPayload.CopyTo(result, ephemeralPublic.Length);
        return result;
    }

    /// <summary>Opens a sealed box addressed to this identity's X25519 public key.</summary>
    public byte[] Open(ReadOnlySpan<byte> sealedBox, string context)
    {
        ThrowIfDisposed();
        if (sealedBox.Length <= PublicKeySize)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The sealed block is too short to be valid.");
        }

        var ephemeralPublic = sealedBox[..PublicKeySize];
        var body = sealedBox[PublicKeySize..];
        var salt = Concat(ephemeralPublic, AgreementPublicKey);
        var info = Encoding.UTF8.GetBytes($"{SealLabel}|{context}");
        var key = Derive(_agreementKey, ephemeralPublic, salt, info, Aead.KeySize);
        return Aead.Decrypt(key, body, salt);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _signingKey.Dispose();
        _agreementKey.Dispose();
    }

    private static byte[] Derive(
        NKey privateKey,
        ReadOnlySpan<byte> peerPublicKey,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> info,
        int length)
    {
        if (peerPublicKey.Length != PublicKeySize)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The peer public key has the wrong length.");
        }

        NPublicKey peer;
        try
        {
            peer = NPublicKey.Import(KeyAgreementAlgorithm.X25519, peerPublicKey, KeyBlobFormat.RawPublicKey);
        }
        catch (FormatException exception)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The peer public key could not be read.", exception);
        }

        using var shared = KeyAgreementAlgorithm.X25519.Agree(privateKey, peer)
            ?? throw new CryptoException(ErrorCode.Corrupt, "The two devices could not agree on a shared key.");

        return KeyDerivationAlgorithm.HkdfSha256.DeriveBytes(shared, salt, info, length);
    }

    private static byte[] OrderedSalt(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second) =>
        first.SequenceCompareTo(second) <= 0 ? Concat(first, second) : Concat(second, first);

    private static byte[] Concat(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        var buffer = new byte[first.Length + second.Length];
        first.CopyTo(buffer);
        second.CopyTo(buffer.AsSpan(first.Length));
        return buffer;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
