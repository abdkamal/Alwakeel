using System.Security.Cryptography;
using System.Text;
using Wakeel.Core.Data;
using Wakeel.Crypto;

namespace Wakeel.Core.Services.Documents;

/// <summary>
/// Thrown when the vault folder itself cannot be reached while something had to be written into
/// it. Reads answer with <see cref="VaultState.Unavailable"/> instead; a write has nowhere to put
/// its bytes, so it must not pretend to have succeeded.
/// </summary>
public sealed class VaultUnavailableException : Exception
{
    public VaultUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The encrypted file store under <c>vault\</c> (ARCHITECTURE.md §2 and §8). Moved into Core by
/// B3-2 from <c>Wakeel.UI/Services/Account/VaultStore.cs</c>, where activation was its only
/// caller, and extended with the streaming read, the integrity check and the two states the
/// documents package needs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Layout.</b> A file is filed under the SHA-256 of its original bytes at
/// <c>vault\&lt;first two hex chars&gt;\&lt;sha256&gt;.bin</c>. The hash is taken of the file as
/// it arrived, before encryption, which is what lets the health centre later prove that a stored
/// original is still exactly the original, and what makes storing the same content twice a no-op.
/// </para>
/// <para>
/// <b>Key.</b> Each file is sealed with its own sub key, HKDF-derived from the vault key with the
/// file's hash as the salt, so two files never share an encryption key and no single key
/// compromise unlocks the vault in one step.
/// </para>
/// <para>
/// <b>Format.</b> The sealed bytes use <see cref="Aead.EncryptStream"/>'s chunked frames rather
/// than a single block, so reading back a 20 MB scan costs one chunk of memory instead of twenty
/// megabytes twice over, and a truncated file is detectable. Every frame authenticates the file's
/// hash as associated data, so a sealed file cannot be moved to another hash's path unnoticed.
/// </para>
/// <para>
/// <b>Integrity.</b> A read verifies twice: the AEAD tag of every frame, and then the SHA-256 of
/// what came out against the name the file is filed under. Either failure is
/// <see cref="VaultState.Corrupt"/> — the «الملف تالف — الأصل محفوظ» card — and never an
/// exception, because a damaged scan is a state the office has to be able to look at, not a crash.
/// </para>
/// <para>
/// <b>Keys in memory.</b> The vault key is fetched from <see cref="IVaultKeyProvider"/> at the
/// moment of use and the working copy is zeroed in a <c>finally</c>; nothing here keeps a key
/// between calls, and no key or plaintext is ever logged.
/// </para>
/// </remarks>
public sealed class VaultStore : IDocumentStore
{
    /// <summary>Label separating vault file keys from every other key derived from the vault key.</summary>
    private const string SubKeyLabel = "wakeel.vault.file";

    /// <summary>Frame size of the sealed stream; one chunk is the store's whole memory cost.</summary>
    private const int ChunkSize = 256 * 1024;

    private readonly WakeelPaths _paths;
    private readonly IVaultKeyProvider _keys;

    public VaultStore(WakeelPaths paths, IVaultKeyProvider keys)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(keys);
        _paths = paths;
        _keys = keys;
    }

    /// <inheritdoc />
    public bool IsAvailable
    {
        get
        {
            try
            {
                Directory.CreateDirectory(_paths.VaultDir);
                return Directory.Exists(_paths.VaultDir);
            }
            catch (Exception exception) when (IsFolderProblem(exception))
            {
                return false;
            }
        }
    }

    /// <inheritdoc />
    public bool Exists(string sha256Hex)
    {
        CheckHash(sha256Hex);
        try
        {
            return File.Exists(_paths.VaultFilePath(sha256Hex));
        }
        catch (Exception exception) when (IsFolderProblem(exception))
        {
            return false;
        }
    }

    /// <inheritdoc />
    public Task<VaultWriteResult> WriteAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Task.Run(() => WriteCore(content), cancellationToken);
    }

    /// <inheritdoc />
    public Task<VaultWriteResult> WriteAsync(ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
    {
        // The copy is made on the caller's thread so the memory the caller owns is not read from
        // a pool thread after the caller may already have reused it.
        var buffer = content.ToArray();
        return Task.Run(
            () =>
            {
                using var stream = new MemoryStream(buffer, writable: false);
                return WriteCore(stream);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<VaultCopyResult> CopyToAsync(string sha256Hex, Stream destination, CancellationToken cancellationToken = default)
    {
        CheckHash(sha256Hex);
        ArgumentNullException.ThrowIfNull(destination);
        return Task.Run(() => CopyToCore(sha256Hex, destination), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<VaultReadResult> ReadAsync(string sha256Hex, CancellationToken cancellationToken = default)
    {
        CheckHash(sha256Hex);
        using var buffer = new MemoryStream();
        var copy = await CopyToAsync(sha256Hex, buffer, cancellationToken).ConfigureAwait(false);
        if (!copy.IsOk)
        {
            // Whatever reached the buffer before the damage was found is not the original and
            // must not leave this method.
            return new VaultReadResult(copy.State, null);
        }

        return new VaultReadResult(VaultState.Ok, buffer.ToArray());
    }

    /// <inheritdoc />
    public async Task<VaultState> VerifyAsync(string sha256Hex, CancellationToken cancellationToken = default)
    {
        CheckHash(sha256Hex);
        using var sink = new CountingSink();
        var copy = await CopyToAsync(sha256Hex, sink, cancellationToken).ConfigureAwait(false);
        return copy.State;
    }

    /// <inheritdoc />
    public void Delete(string sha256Hex) => Delete(_paths, sha256Hex);

    // -------------------------------------------------------------------------------------------
    // The static form activation uses. During activation there is no session yet — the vault key
    // has just been generated and is wiped again a few lines later — so there is nothing for an
    // IVaultKeyProvider to answer with, and the key travels as an argument instead.
    // -------------------------------------------------------------------------------------------

    /// <summary>Writes one file into the vault and returns the hash it is stored under.</summary>
    public static string Write(WakeelPaths paths, ReadOnlySpan<byte> vaultKey, ReadOnlySpan<byte> content)
    {
        ArgumentNullException.ThrowIfNull(paths);
        using var stream = new MemoryStream(content.ToArray(), writable: false);
        return WriteCore(paths, vaultKey, stream).Sha256Hex;
    }

    /// <summary>Reads one file back, or returns null when the vault does not hold it or it is damaged.</summary>
    public static byte[]? Read(WakeelPaths paths, ReadOnlySpan<byte> vaultKey, string sha256Hex)
    {
        ArgumentNullException.ThrowIfNull(paths);
        CheckHash(sha256Hex);

        using var buffer = new MemoryStream();
        var state = CopyToCore(paths, vaultKey, sha256Hex, buffer);
        return state.IsOk ? buffer.ToArray() : null;
    }

    /// <summary>
    /// Removes one file from the vault, e.g. to undo a write made during an activation that failed
    /// before it committed. Missing files and files the process cannot touch are left alone rather
    /// than allowed to hide the failure that brought the caller here.
    /// </summary>
    public static void Delete(WakeelPaths paths, string sha256Hex)
    {
        ArgumentNullException.ThrowIfNull(paths);
        CheckHash(sha256Hex);

        try
        {
            File.Delete(paths.VaultFilePath(sha256Hex));
        }
        catch (Exception exception) when (IsFolderProblem(exception))
        {
        }
    }

    // -------------------------------------------------------------------------------------------

    private VaultWriteResult WriteCore(Stream content)
    {
        var key = _keys.VaultKey.ToArray();
        try
        {
            return WriteCore(_paths, key, content);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private VaultCopyResult CopyToCore(string sha256Hex, Stream destination)
    {
        var key = _keys.VaultKey.ToArray();
        try
        {
            return CopyToCore(_paths, key, sha256Hex, destination);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static VaultWriteResult WriteCore(WakeelPaths paths, ReadOnlySpan<byte> vaultKey, Stream content)
    {
        // The hash has to be known before the target path is known, so the content is read once to
        // hash it and once to seal it. A stream that can rewind is read twice; one that cannot is
        // buffered in memory rather than spooled to disk, because a spooled copy would leave the
        // original readable on the volume for as long as the write lasts.
        Stream source;
        MemoryStream? buffered = null;
        try
        {
            if (content.CanSeek)
            {
                source = content;
            }
            else
            {
                buffered = new MemoryStream();
                content.CopyTo(buffered);
                buffered.Position = 0;
                source = buffered;
            }

            var start = source.Position;
            var hashHex = Sha256.HashHex(source);
            var size = source.Position - start;
            source.Position = start;

            var target = paths.VaultFilePath(hashHex);
            string temporary;
            try
            {
                var directory = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (File.Exists(target))
                {
                    // The same bytes are already stored, under the same name, sealed with the same
                    // sub key. Re-writing them would only risk replacing a good file with a worse
                    // one if this write failed halfway.
                    return new VaultWriteResult(hashHex, size, AlreadyStored: true);
                }

                // A name nobody else can be writing, so two imports of the same file at the same
                // moment cannot tread on each other's temporary.
                temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            }
            catch (Exception exception) when (IsFolderProblem(exception))
            {
                throw new VaultUnavailableException(CoreAr.Documents.VaultUnavailableTitle, exception);
            }

            var key = SubKey(vaultKey, hashHex);
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    Aead.EncryptStream(key, source, stream, Encoding.UTF8.GetBytes(hashHex), ChunkSize);
                    stream.Flush(flushToDisk: true);
                }

                // Through a temporary name so a half written file can never be mistaken for a
                // stored one: the move is the only step a reader can observe.
                File.Move(temporary, target, overwrite: false);
                return new VaultWriteResult(hashHex, size, AlreadyStored: false);
            }
            catch (IOException) when (File.Exists(target))
            {
                // Another writer won the race with identical content; theirs is as good as ours.
                DeleteQuietly(temporary);
                return new VaultWriteResult(hashHex, size, AlreadyStored: true);
            }
            catch (Exception exception)
            {
                // The write or the move failed; do not leave sealed, unaccountable bytes behind
                // under a key the caller may abandon (e.g. a vault key wiped after a failed
                // activation).
                DeleteQuietly(temporary);
                if (IsFolderProblem(exception))
                {
                    throw new VaultUnavailableException(CoreAr.Documents.VaultUnavailableTitle, exception);
                }

                throw;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
        finally
        {
            buffered?.Dispose();
        }
    }

    private static VaultCopyResult CopyToCore(WakeelPaths paths, ReadOnlySpan<byte> vaultKey, string sha256Hex, Stream destination)
    {
        string path;
        try
        {
            path = paths.VaultFilePath(sha256Hex);
            if (!Directory.Exists(paths.VaultDir))
            {
                return new VaultCopyResult(VaultState.Unavailable, 0);
            }

            if (!File.Exists(path))
            {
                return new VaultCopyResult(VaultState.Missing, 0);
            }
        }
        catch (Exception exception) when (IsFolderProblem(exception))
        {
            return new VaultCopyResult(VaultState.Unavailable, 0);
        }

        var key = SubKey(vaultKey, sha256Hex);
        try
        {
            using var sealedFile = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            // The hash of what comes out is computed on the way through, so verifying the original
            // costs nothing beyond the read itself and never needs the file whole in memory.
            using var verifier = new VerifyingStream(destination);
            Aead.DecryptStream(key, sealedFile, verifier, Encoding.UTF8.GetBytes(sha256Hex));

            var actual = verifier.Finish();
            if (!string.Equals(actual, sha256Hex, StringComparison.OrdinalIgnoreCase))
            {
                // The seal held but the content is not what this name promises. Filed as damage
                // rather than as a missing file: the office is told the stored original can no
                // longer be shown, not that it was never there.
                return new VaultCopyResult(VaultState.Corrupt, verifier.BytesWritten);
            }

            return new VaultCopyResult(VaultState.Ok, verifier.BytesWritten);
        }
        catch (CryptoException)
        {
            // A broken tag, a truncated file, trailing bytes: all of it is the same sentence for
            // the person looking at the screen.
            return new VaultCopyResult(VaultState.Corrupt, 0);
        }
        catch (Exception exception) when (IsFolderProblem(exception))
        {
            return new VaultCopyResult(VaultState.Unavailable, 0);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] SubKey(ReadOnlySpan<byte> vaultKey, string sha256Hex) =>
        Hkdf.DeriveKey(vaultKey, Aead.KeySize, Convert.FromHexString(sha256Hex), SubKeyLabel);

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (IsFolderProblem(exception))
        {
        }
    }

    /// <summary>
    /// Whether an exception is the folder being out of reach rather than a defect: a drive that is
    /// not plugged in, a path that no longer resolves, a permission that was withdrawn.
    /// </summary>
    private static bool IsFolderProblem(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException;

    private static void CheckHash(string sha256Hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256Hex);

        // The length alone is not enough: every path here is built from this value, so a 64
        // character name carrying a separator would resolve outside the vault folder, and a name
        // that is not hexadecimal would only fail much later, inside the key derivation.
        if (sha256Hex.Length != 64 || !sha256Hex.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("A vault file is named by a 64 character SHA-256 hex string.", nameof(sha256Hex));
        }
    }

    /// <summary>Forwards everything written to an inner stream while hashing it on the way past.</summary>
    private sealed class VerifyingStream(Stream inner) : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        public long BytesWritten { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => BytesWritten;
            set => throw new NotSupportedException();
        }

        public string Finish() => Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _hash.AppendData(buffer);
            inner.Write(buffer);
            BytesWritten += buffer.Length;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hash.Dispose();
            }

            // The inner stream belongs to the caller; only the hash is ours to release.
            base.Dispose(disposing);
        }
    }

    /// <summary>Discards everything written to it; used by the integrity check, which wants no bytes.</summary>
    private sealed class CountingSink : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
