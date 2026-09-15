namespace Wakeel.Crypto;

/// <summary>How the payload of a container is encrypted.</summary>
public enum PayloadMode
{
    /// <summary>A sub key of the office key, for packets inside one office.</summary>
    OfficeKey,

    /// <summary>A password chosen by a person, stretched with Argon2id.</summary>
    Password,

    /// <summary>A random content key sealed to one recipient X25519 public key.</summary>
    SealedFor,

    /// <summary>A sub key of a session key agreed while pairing.</summary>
    Session,
}

/// <summary>One logical file carried inside the payload, with its plain size and hash.</summary>
public sealed record ContainerEntry(string Name, long Size, string Sha256);

/// <summary>
/// The plain text, signed description of a container. It is read and checked in full
/// before a single byte of the payload is decrypted.
/// </summary>
public sealed record ContainerManifest(
    ContainerKind Type,
    int Version,
    DeviceCertificate Producer,
    DateTimeOffset CreatedAt,
    PayloadMode Mode,
    Argon2Params? Kdf,
    string? SealedKey,

    // The administrator's recovery copy of the content key, sealed to the organisation
    // X25519 public key, so a backup or a correspondence can always be opened for
    // maintenance without the password or the recipient's device.
    string? AdminSealedKey,
    IReadOnlyList<ContainerEntry> Entries)
{
    public const int CurrentVersion = 1;

    /// <summary>Name of the manifest inside the container.</summary>
    public const string FileName = "manifest.json";

    /// <summary>Name of the encrypted payload inside the container.</summary>
    public const string PayloadFileName = "payload.bin";

    /// <summary>Name of the signature inside the container.</summary>
    public const string SignatureFileName = "signature.bin";
}
