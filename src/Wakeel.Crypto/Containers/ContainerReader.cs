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

            var manifestBytes = ReadAll(manifestEntry);
            var signature = ReadAll(signatureEntry);
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

    /// <summary>Decrypts every entry straight to disk, without holding the payload in memory.</summary>
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

            using (var source = entry.Open())
            using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var hashing = new HashingStream(output))
            {
                source.CopyTo(hashing);
                hashing.Flush();
                CheckEntry(expected, hashing.BytesWritten, hashing.Hash);
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
            source.CopyTo(hashing);
            hashing.Flush();
            CheckEntry(expected, hashing.BytesWritten, hashing.Hash);
        }

        return buffer.ToArray();
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

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
