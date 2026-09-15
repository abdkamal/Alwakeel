namespace Wakeel.Crypto;

/// <summary>
/// Tells the writer how to protect a payload, and the reader how to open one.
/// </summary>
public sealed class ContainerKeySource
{
    /// <summary>Context label of the administrator's recovery copy of the content key.</summary>
    internal const string AdminKeyLabel = "wakeel.container.adminkey";

    private const string SubKeyLabel = "wakeel.container";
    private const string SealedKeyLabel = "wakeel.container.key";

    private ContainerKeySource(PayloadMode mode)
    {
        Mode = mode;
    }

    public PayloadMode Mode { get; }

    internal byte[]? RawKey { get; private init; }

    internal string? Password { get; private init; }

    internal Argon2Params? Kdf { get; private init; }

    internal byte[]? RecipientPublicKey { get; private init; }

    internal DeviceIdentity? Recipient { get; private init; }

    /// <summary>Set when this source opens the administrator's recovery copy of the key.</summary>
    internal bool IsAdminCopy { get; private init; }

    /// <summary>The office key shared by every device of one office.</summary>
    public static ContainerKeySource OfficeKey(byte[] officeKey)
    {
        ArgumentNullException.ThrowIfNull(officeKey);
        return new ContainerKeySource(PayloadMode.OfficeKey) { RawKey = (byte[])officeKey.Clone() };
    }

    /// <summary>A session key agreed between the computer and the phone.</summary>
    public static ContainerKeySource SessionKey(byte[] sessionKey)
    {
        ArgumentNullException.ThrowIfNull(sessionKey);
        return new ContainerKeySource(PayloadMode.Session) { RawKey = (byte[])sessionKey.Clone() };
    }

    /// <summary>A password typed by a person. The writer stores the cost parameters in the manifest.</summary>
    public static ContainerKeySource FromPassword(string password, Argon2Params? kdf = null)
    {
        ArgumentNullException.ThrowIfNull(password);
        return new ContainerKeySource(PayloadMode.Password) { Password = password, Kdf = kdf };
    }

    /// <summary>Write side: seal a fresh content key to one recipient.</summary>
    public static ContainerKeySource SealFor(byte[] recipientAgreementPublicKey)
    {
        ArgumentNullException.ThrowIfNull(recipientAgreementPublicKey);
        return new ContainerKeySource(PayloadMode.SealedFor)
        {
            RecipientPublicKey = (byte[])recipientAgreementPublicKey.Clone(),
        };
    }

    /// <summary>Read side: open a sealed content key with the recipient's own identity.</summary>
    public static ContainerKeySource ForRecipient(DeviceIdentity recipient)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        return new ContainerKeySource(PayloadMode.SealedFor) { Recipient = recipient };
    }

    /// <summary>
    /// Read side, administration tool only: opens any container that carries the
    /// administrator's recovery copy, whatever way the payload is otherwise protected.
    /// </summary>
    public static ContainerKeySource ForAdmin(DeviceIdentity orgIdentity)
    {
        ArgumentNullException.ThrowIfNull(orgIdentity);
        return new ContainerKeySource(PayloadMode.SealedFor) { Recipient = orgIdentity, IsAdminCopy = true };
    }

    /// <summary>Seals a content key to the organisation, for the administrator's copy.</summary>
    internal static string SealForAdmin(ReadOnlySpan<byte> orgAgreementPublicKey, ReadOnlySpan<byte> contentKey) =>
        Base64Url.Encode(DeviceIdentity.SealFor(orgAgreementPublicKey, contentKey, AdminKeyLabel));

    /// <summary>Produces the content key used to encrypt a new payload.</summary>
    internal byte[] CreateContentKey(ContainerKind kind, out Argon2Params? kdf, out string? sealedKey)
    {
        kdf = null;
        sealedKey = null;

        switch (Mode)
        {
            case PayloadMode.OfficeKey:
            case PayloadMode.Session:
                return SubKey(RawKey!, kind);

            case PayloadMode.Password:
                kdf = (Kdf ?? Argon2Params.CreateDefault()).WithFreshSalt();
                return Argon2Kdf.DeriveKey(Password!, kdf, Aead.KeySize);

            case PayloadMode.SealedFor:
                if (RecipientPublicKey is null)
                {
                    throw new CryptoException(ErrorCode.Corrupt, "No recipient was given for this sealed container.");
                }

                var contentKey = RandomBytes.Next(Aead.KeySize);
                sealedKey = Base64Url.Encode(DeviceIdentity.SealFor(RecipientPublicKey, contentKey, SealedKeyLabel));
                return contentKey;

            default:
                throw new CryptoException(ErrorCode.UnknownKind, "This payload mode is not known to this version.");
        }
    }

    /// <summary>Recovers the content key of an existing payload.</summary>
    internal byte[] OpenContentKey(ContainerManifest manifest)
    {
        if (IsAdminCopy)
        {
            if (string.IsNullOrEmpty(manifest.AdminSealedKey))
            {
                throw new CryptoException(ErrorCode.Corrupt, "This file carries no copy for the administrator.");
            }

            try
            {
                return Recipient!.Open(Base64Url.Decode(manifest.AdminSealedKey), AdminKeyLabel);
            }
            catch (CryptoException exception) when (exception.Code == ErrorCode.Tampered)
            {
                throw new CryptoException(ErrorCode.WrongPassword, "This file was not made for these administration keys.", exception);
            }
        }

        if (manifest.Mode != Mode)
        {
            throw new CryptoException(ErrorCode.UnknownKind, "This file is not protected the way you are trying to open it.");
        }

        switch (Mode)
        {
            case PayloadMode.OfficeKey:
            case PayloadMode.Session:
                return SubKey(RawKey!, manifest.Type);

            case PayloadMode.Password:
                if (manifest.Kdf is null)
                {
                    throw new CryptoException(ErrorCode.Corrupt, "The file does not carry its key derivation settings.");
                }

                return Argon2Kdf.DeriveKey(Password!, manifest.Kdf, Aead.KeySize);

            case PayloadMode.SealedFor:
                if (Recipient is null)
                {
                    throw new CryptoException(ErrorCode.Corrupt, "Opening a sealed file needs the recipient device keys.");
                }

                if (string.IsNullOrEmpty(manifest.SealedKey))
                {
                    throw new CryptoException(ErrorCode.Corrupt, "The file does not carry a sealed content key.");
                }

                try
                {
                    return Recipient.Open(Base64Url.Decode(manifest.SealedKey), SealedKeyLabel);
                }
                catch (CryptoException exception) when (exception.Code == ErrorCode.Tampered)
                {
                    throw new CryptoException(ErrorCode.WrongPassword, "This file is not addressed to this device.", exception);
                }

            default:
                throw new CryptoException(ErrorCode.UnknownKind, "This payload mode is not known to this version.");
        }
    }

    private static byte[] SubKey(byte[] rootKey, ContainerKind kind)
    {
        if (rootKey.Length != Aead.KeySize)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The supplied key must be exactly thirty two bytes.");
        }

        return Hkdf.DeriveKey(rootKey, Aead.KeySize, $"{SubKeyLabel}|{ContainerKinds.Token(kind)}");
    }
}
