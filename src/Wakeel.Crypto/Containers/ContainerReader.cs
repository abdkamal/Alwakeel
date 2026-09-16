using System.IO.Compression;
using System.Security.Cryptography;

namespace Wakeel.Crypto;

/// <summary>What the reader is allowed to accept.</summary>
public sealed class ContainerOpenOptions
{
    /// <summary>The kind the caller asked for; a different kind is refused.</summary>
    public ContainerKind? ExpectedKind { get; init; }

    /// <summary>
    /// The organisation root key, the single root of trust. It is required unless the caller
    /// deliberately turns <see cref="VerifyCertificateChain"/> off.
    /// </summary>
    public byte[]? OrgSigningPublicKey { get; init; }

    /// <summary>The computer certificate, needed when the producer is a phone.</summary>
    public DeviceCertificate? IssuerCertificate { get; init; }

    public RevocationList? Revocations { get; init; }

    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>How far ahead of this machine's clock a creation instant may sit.</summary>
    public TimeSpan MaxFutureSkew { get; init; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Whether the producer's certificate chain is checked against the organisation key. It is
    /// on by default; turning it off is only for a caller that has established trust some other
    /// way, and it has to be written out explicitly.
    /// </summary>
    public bool VerifyCertificateChain { get; init; } = true;

    /// <summary>
    /// Where the decrypted payload is staged while entries are read. The host points this at
    /// the product's own protected folder; when it is not set the system temporary folder is
    /// used. Either way the staging file is removed when its handle closes.
    /// </summary>
    public string? StagingDirectory { get; init; }
}

/// <summary>
/// Opens a container. The signature, the certificate chain, the kind and the creation
/// instant are all checked while opening, before any payload byte is decrypted.
/// </summary>
public sealed class ContainerReader : IDisposable
{
    /// <summary>
    /// The most <c>manifest.json</c> or <c>signature.bin</c> this reader will ever read: both
    /// are tiny by construction and neither has been checked against anything yet when they are
    /// read, so a crafted entry that inflates past this bound is refused outright rather than
    /// exhausting memory while the file is merely being found out whether it is worth trusting.
    /// </summary>
    private const long MaxMetadataEntrySize = 1024 * 1024;

    private readonly ZipArchive _archive;
    private readonly Stream? _ownedStream;
    private readonly ContainerOpenOptions _options;
    private bool _disposed;

    private ContainerReader(
        ZipArchive archive,
        Stream? ownedStream,
        ContainerManifest manifest,
        ContainerOpenOptions options)
    {
        _archive = archive;
        _ownedStream = ownedStream;
        _options = options;
        Manifest = manifest;
    }

    public ContainerManifest Manifest { get; }

    public static ContainerReader Open(string path, ContainerOpenOptions options)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(options);

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            return Open(stream, options, ownsStream: true);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public static ContainerReader Open(Stream stream, ContainerOpenOptions options, bool ownsStream = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);

        ZipArchive archive;
        try
        {
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException exception)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The file is not one of the product's own files.", exception);
        }

        try
        {
            var manifestEntry = Require(archive, ContainerManifest.FileName);
            var payloadEntry = Require(archive, ContainerManifest.PayloadFileName);
            var signatureEntry = Require(archive, ContainerManifest.SignatureFileName);

            var manifestBytes = ReadAll(manifestEntry, MaxMetadataEntrySize);
            var signature = ReadAll(signatureEntry, MaxMetadataEntrySize);
            var manifest = CanonicalJson.Deserialize<ContainerManifest>(manifestBytes);

            // Nothing in the manifest has been checked yet, so its shape is established first:
            // every later step reads these fields, and a hostile file may simply leave them out.
            ValidateShape(manifest);

            byte[] payloadHash;
            using (var payloadStream = payloadEntry.Open())
            {
                payloadHash = Sha256.Hash(payloadStream);
            }

            var signingInput = ContainerWriter.SigningInput(Sha256.Hash(manifestBytes), payloadHash);
            if (!Base64Url.TryDecode(manifest.Producer.Body.Ed25519Pub, out var producerKey)
                || !DeviceIdentity.Verify(producerKey, signingInput, signature))
            {
                throw new CryptoException(ErrorCode.BadSignature, "The file signature does not match its contents.");
            }

            var now = options.Time.GetUtcNow();

            if (options.VerifyCertificateChain)
            {
                // The signature above only proves that the file matches the key inside its own
                // certificate, and anyone can make one of those. The chain is what ties that
                // certificate to the organisation, so a missing organisation key is a refusal
                // and never a silent skip.
                if (options.OrgSigningPublicKey is not { Length: DeviceIdentity.PublicKeySize } orgKey)
                {
                    throw new CryptoException(
                        ErrorCode.BadSignature,
                        "The organisation key is needed to check who produced this file.");
                }

                CertificateChain.Verify(
                    manifest.Producer,
                    orgKey,
                    options.Revocations,
                    now,
                    options.IssuerCertificate);
            }

            // The organisation root exists to sign the one file the organisation itself makes.
            // Letting it stand as the producer of a sync or message packet would hand whoever
            // holds the root key a second, unaudited identity inside an office it never joined.
            if (manifest.Producer.Body.Kind == DeviceKind.Org && manifest.Type != ContainerKind.Setup)
            {
                throw new CryptoException(ErrorCode.BadSignature, "Only a setup file may be produced by the organisation itself.");
            }

            if (options.ExpectedKind is { } expected && manifest.Type != expected)
            {
                throw new CryptoException(ErrorCode.UnknownKind, "This file is not of the kind the operation expects.");
            }

            if (manifest.Version > ContainerManifest.CurrentVersion || manifest.Version < 1)
            {
                throw new CryptoException(ErrorCode.UnknownKind, "The file was produced by a newer version of the product.");
            }

            if (manifest.CreatedAt > now + options.MaxFutureSkew)
            {
                throw new CryptoException(ErrorCode.Expired, "The file is dated in the future.");
            }

            return new ContainerReader(archive, ownsStream ? stream : null, manifest, options);
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Decrypts and returns every entry, checking each recorded size and hash. The whole
    /// content ends up in memory, so this is meant for the small packets; a backup is read
    /// with <see cref="ExtractTo"/>, or one item at a time with <see cref="ReadEntry"/>.
    /// </summary>
    public IReadOnlyDictionary<string, byte[]> ReadEntries(ContainerKeySource key)
    {
        ArgumentNullException.ThrowIfNull(key);
        ThrowIfDisposed();

        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using var plain = DecryptPayload(key);
        using var inner = OpenInner(plain);

        foreach (var expected in Manifest.Entries)
        {
            result[expected.Name] = ReadOne(inner, expected);
        }

        return result;
    }

    /// <summary>
    /// Decrypts the payload once and returns a single item, without ever holding the other
    /// items in memory. This is how one small file is taken out of a very large backup.
    /// </summary>
    public byte[] ReadEntry(string name, ContainerKeySource key)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(key);
        ThrowIfDisposed();

        var expected = Manifest.Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.Ordinal))
            ?? throw new CryptoException(ErrorCode.Corrupt, "The file does not contain that item.");

        using var plain = DecryptPayload(key);
        using var inner = OpenInner(plain);
        return ReadOne(inner, expected);
    }

    /// <summary>
    /// Decrypts one entry straight into <paramref name="destination"/>, checking its recorded
    /// size and hash on the way, without ever holding the whole entry in memory at once. This
    /// is how a caller streams a single multi-megabyte item out of a container that also
    /// carries much smaller ones, instead of paying for a byte array the size of the entry.
    /// On a <see cref="CryptoException"/> the destination has already received unverified
    /// bytes — the check only runs once the copy is done — so the caller must discard whatever
    /// it wrote rather than trust any part of it.
    /// </summary>
    public void ReadEntryTo(string name, ContainerKeySource key, Stream destination)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(destination);
        ThrowIfDisposed();

        var expected = Manifest.Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.Ordinal))
            ?? throw new CryptoException(ErrorCode.Corrupt, "The file does not contain that item.");

        using var plain = DecryptPayload(key);
        using var inner = OpenInner(plain);
        var entry = inner.GetEntry(expected.Name)
            ?? throw new CryptoException(ErrorCode.Tampered, "The file is missing one of the contents it lists.");

        using var source = entry.Open();
        using var hashing = new HashingStream(destination);
        CopyWithLimit(source, hashing, expected.Size);
        hashing.Flush();
        CheckEntry(expected, hashing.BytesWritten, hashing.Hash);
    }

    /// <summary>
    /// Decrypts every entry straight to disk, without holding the payload in memory. When one
    /// entry fails its size or hash check, the file just written for it is deleted before the
    /// exception propagates, so a refused container never leaves a half written file behind;
    /// entries fully written before the failing one are left in place.
    /// </summary>
    public void ExtractTo(string directory, ContainerKeySource key)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentNullException.ThrowIfNull(key);
        ThrowIfDisposed();

        var root = Path.GetFullPath(directory);
        Directory.CreateDirectory(root);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        using var plain = DecryptPayload(key);
        using var inner = OpenInner(plain);

        foreach (var expected in Manifest.Entries)
        {
            var entry = inner.GetEntry(expected.Name)
                ?? throw new CryptoException(ErrorCode.Tampered, "The file is missing one of the contents it lists.");

            // The names were already checked while opening; this is the second lock on the same
            // door, because writing outside the chosen folder must never become possible.
            var target = Path.GetFullPath(Path.Combine(root, expected.Name));
            if (!target.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new CryptoException(ErrorCode.Corrupt, "The file tries to write outside the chosen folder.");
            }

            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            try
            {
                using (var source = entry.Open())
                using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var hashing = new HashingStream(output))
                {
                    CopyWithLimit(source, hashing, expected.Size);
                    hashing.Flush();
                    CheckEntry(expected, hashing.BytesWritten, hashing.Hash);
                }
            }
            catch
            {
                // The file handle above is already closed by the time this runs — the using
                // block unwound before the catch — so the delete below never races the write.
                TryDelete(target);
                throw;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _archive.Dispose();
        _ownedStream?.Dispose();
    }

    private static void ValidateShape(ContainerManifest manifest)
    {
        if (manifest.Producer?.Body is null || string.IsNullOrEmpty(manifest.Producer.Signature))
        {
            throw new CryptoException(ErrorCode.Corrupt, "The file does not name the device that produced it.");
        }

        if (manifest.Entries is null || manifest.Entries.Count == 0)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The file lists no contents.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest.Entries)
        {
            if (entry is null || string.IsNullOrEmpty(entry.Sha256) || entry.Size < 0)
            {
                throw new CryptoException(ErrorCode.Corrupt, "The file describes one of its contents incorrectly.");
            }

            if (!ContainerEntrySource.IsAcceptableName(entry.Name))
            {
                throw new CryptoException(ErrorCode.Corrupt, "The file names one of its contents in a way that is not allowed.");
            }

            if (!seen.Add(entry.Name))
            {
                throw new CryptoException(ErrorCode.Corrupt, "The file lists the same item twice.");
            }
        }
    }

    private static byte[] ReadOne(ZipArchive inner, ContainerEntry expected)
    {
        var entry = inner.GetEntry(expected.Name)
            ?? throw new CryptoException(ErrorCode.Tampered, "The file is missing one of the contents it lists.");

        using var buffer = new MemoryStream();
        using (var source = entry.Open())
        using (var hashing = new HashingStream(buffer))
        {
            CopyWithLimit(source, hashing, expected.Size);
            hashing.Flush();
            CheckEntry(expected, hashing.BytesWritten, hashing.Hash);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="destination"/>, refusing as soon as
    /// more than <paramref name="limit"/> bytes have been read. The manifest's declared size is
    /// otherwise only checked after the whole entry has been buffered, which lets an inner zip
    /// entry that lies about its own size exhaust memory (or disk) before the check ever runs.
    /// </summary>
    private static void CopyWithLimit(Stream source, Stream destination, long limit)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > limit)
            {
                throw new CryptoException(ErrorCode.Tampered, "One of the contents is larger than the file declares.");
            }

            destination.Write(buffer, 0, read);
        }
    }

    private Stream DecryptPayload(ContainerKeySource key)
    {
        var contentKey = key.OpenContentKey(Manifest);
        var associated = ContainerWriter.PayloadAssociatedData(Manifest.Type, Manifest.Version);
        var output = ContainerWriter.CreateStagingFile(_options.StagingDirectory);

        try
        {
            var payloadEntry = _archive.GetEntry(ContainerManifest.PayloadFileName)
                ?? throw new CryptoException(ErrorCode.Corrupt, "The file carries no contents.");

            using (var encrypted = payloadEntry.Open())
            {
                try
                {
                    Aead.DecryptStream(contentKey, encrypted, output, associated);
                }
                catch (CryptoException exception)
                    when (exception.Code == ErrorCode.Tampered && Manifest.Mode == PayloadMode.Password)
                {
                    // A password that does not open the payload is a wrong password, not damage.
                    throw new CryptoException(ErrorCode.WrongPassword, "The password does not open this file.", exception);
                }
            }

            output.Position = 0;
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
        }
    }

    private static ZipArchive OpenInner(Stream plain)
    {
        try
        {
            return new ZipArchive(plain, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException exception)
        {
            throw new CryptoException(ErrorCode.Tampered, "The contents of the file could not be read.", exception);
        }
    }

    private static void CheckEntry(ContainerEntry expected, long size, ReadOnlySpan<byte> hash)
    {
        if (size != expected.Size
            || !string.Equals(Sha256.ToHex(hash), expected.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new CryptoException(ErrorCode.Tampered, "One of the contents does not match what the file declares.");
        }
    }

    private static ZipArchiveEntry Require(ZipArchive archive, string name) =>
        archive.GetEntry(name)
        ?? throw new CryptoException(ErrorCode.Corrupt, "The file is missing one of its required parts.");

    /// <summary>
    /// Removes a partially written extraction target on a best effort basis. The file carries
    /// no key material — only content that already failed its own check — so a delete that
    /// itself cannot complete leaves nothing worse than an orphaned file, and must not mask the
    /// real exception that is already unwinding through the caller.
    /// </summary>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Reads a whole entry, refusing as soon as more than <paramref name="limit"/> bytes have
    /// come out of it. Declared size alone is not a defence here — a hostile entry can under
    /// state it in the zip's own metadata just as easily as it can over state one the reader
    /// already trusts — so the bound is enforced while the bytes are still being decompressed.
    /// </summary>
    private static byte[] ReadAll(ZipArchiveEntry entry, long limit)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            total += read;
            if (total > limit)
            {
                throw new CryptoException(ErrorCode.Corrupt, "One of the file's required parts is larger than the product ever writes.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
