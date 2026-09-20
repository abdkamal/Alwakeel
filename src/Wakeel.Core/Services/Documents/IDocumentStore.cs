namespace Wakeel.Core.Services.Documents;

/// <summary>
/// How the vault answered a request for one stored file. Every caller in الوكيل branches on this
/// rather than on an exception, because two of the four answers are ordinary states the user is
/// shown as a card (W46), not failures: «الخزنة غير متاحة» and «الملف تالف — الأصل محفوظ».
/// </summary>
public enum VaultState
{
    /// <summary>The file was found, decrypted and matched its hash.</summary>
    Ok,

    /// <summary>
    /// The vault folder itself could not be reached — an external drive that is not plugged in,
    /// a folder that was moved, a permission that was withdrawn. Nothing is known about the file.
    /// </summary>
    Unavailable,

    /// <summary>The vault is reachable but holds no file under this hash.</summary>
    Missing,

    /// <summary>
    /// The stored bytes failed their integrity check: either the sealed block did not
    /// authenticate, or it decrypted to content whose SHA-256 is not the one it is filed under.
    /// </summary>
    Corrupt,
}

/// <summary>The result of a vault read that produced bytes.</summary>
/// <param name="State">What the vault answered.</param>
/// <param name="Content">The original bytes, only when <paramref name="State"/> is <see cref="VaultState.Ok"/>.</param>
public sealed record VaultReadResult(VaultState State, byte[]? Content)
{
    /// <summary>Whether the read produced the original bytes.</summary>
    public bool IsOk => State == VaultState.Ok && Content is not null;

    public static VaultReadResult Unavailable { get; } = new(VaultState.Unavailable, null);

    public static VaultReadResult Missing { get; } = new(VaultState.Missing, null);

    public static VaultReadResult Corrupt { get; } = new(VaultState.Corrupt, null);
}

/// <summary>
/// The result of a vault read that streamed into a destination the caller supplied.
/// </summary>
/// <param name="State">What the vault answered.</param>
/// <param name="BytesWritten">
/// How many bytes reached the destination. A <see cref="VaultState.Corrupt"/> answer can still
/// have written bytes: the damage is only provable once the last frame has been read, so a caller
/// writing to a file must discard what it wrote when the state is not <see cref="VaultState.Ok"/>.
/// </param>
public sealed record VaultCopyResult(VaultState State, long BytesWritten)
{
    public bool IsOk => State == VaultState.Ok;
}

/// <summary>What one write left behind.</summary>
/// <param name="Sha256Hex">The hash the content is filed under, lower-case hex.</param>
/// <param name="Size">The size of the original content in bytes.</param>
/// <param name="AlreadyStored">
/// True when the vault already held exactly these bytes and nothing new was written — the
/// de-duplication that makes the same attachment on two letters cost one copy.
/// </param>
public sealed record VaultWriteResult(string Sha256Hex, long Size, bool AlreadyStored);

/// <summary>
/// The encrypted file store under <c>vault\</c> (ARCHITECTURE.md §2 and §8, B3-2). Files are kept
/// under the SHA-256 of their ORIGINAL bytes, each sealed with its own sub key, so two files never
/// share an encryption key, the same content is never stored twice, and a stored original can
/// later be proved to still be exactly the original.
/// </summary>
/// <remarks>
/// The store never throws for a missing, unreachable or damaged file; it answers with a
/// <see cref="VaultState"/>. It does throw when the caller is at fault (no session key, a
/// malformed hash) — that is a defect, not a state a user can be in.
/// </remarks>
public interface IDocumentStore
{
    /// <summary>
    /// Whether the vault folder can be reached right now. False is the «الخزنة غير متاحة» card;
    /// it never means the files are gone, only that they cannot be reached from here.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>Whether the vault holds a file under this hash. False when the vault is unreachable.</summary>
    bool Exists(string sha256Hex);

    /// <summary>
    /// Stores <paramref name="content"/> and returns the hash it is filed under. The content is
    /// hashed as it arrived, before encryption. The write is atomic: a reader never sees a
    /// half-written file, and a failure leaves nothing behind.
    /// </summary>
    Task<VaultWriteResult> WriteAsync(Stream content, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="WriteAsync(Stream, CancellationToken)"/>
    Task<VaultWriteResult> WriteAsync(ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams one stored file into <paramref name="destination"/> without ever holding it whole
    /// in memory, verifying both the seal and the hash as it goes.
    /// </summary>
    Task<VaultCopyResult> CopyToAsync(string sha256Hex, Stream destination, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one stored file whole. For the sizes B3 allows (≤ 20 MB) this is the convenient
    /// form; the viewer and the export path use <see cref="CopyToAsync"/> instead.
    /// </summary>
    Task<VaultReadResult> ReadAsync(string sha256Hex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks one stored file without producing its bytes — what the health centre and the
    /// documents list's "السلامة" column ask.
    /// </summary>
    Task<VaultState> VerifyAsync(string sha256Hex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one file, e.g. to undo a write made during an activation that failed before it
    /// committed. Missing files and files this process cannot touch are left alone.
    /// </summary>
    void Delete(string sha256Hex);
}

/// <summary>
/// Supplies the vault key of the open session. Core cannot own the key: it is unwrapped from the
/// installation key file by the account layer when the person signs in and wiped when the session
/// locks, so Core asks for it at the moment of use and never keeps a copy.
/// </summary>
public interface IVaultKeyProvider
{
    /// <summary>
    /// The vault key of the open session. Throws when no session is open — reading a document
    /// while signed out is a defect in the caller, not a state to render.
    /// </summary>
    ReadOnlySpan<byte> VaultKey { get; }
}
