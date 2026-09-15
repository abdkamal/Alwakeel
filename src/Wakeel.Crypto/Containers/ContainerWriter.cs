using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Wakeel.Crypto;

/// <summary>Everything the writer needs to produce one container.</summary>
public sealed class ContainerWriteRequest
{
    public required ContainerKind Kind { get; init; }

    /// <summary>The certificate of the device producing the file; it travels with the file.</summary>
    public required DeviceCertificate Producer { get; init; }

    /// <summary>The producing device's own keys; must match the certificate.</summary>
    public required DeviceIdentity Signer { get; init; }

    public required ContainerKeySource Key { get; init; }

    public required IReadOnlyList<ContainerEntrySource> Entries { get; init; }

    /// <summary>
    /// The organisation X25519 public key, which arrives inside the setup file. The content
    /// key is sealed to it as well, so the administrator can open the file for maintenance.
    /// It is required for every kind listed in <see cref="ContainerKinds.RequiresAdminCopy"/>.
    /// </summary>
    public byte[]? OrgAgreementPublicKey { get; init; }

    /// <summary>
    /// Where the plain text of the payload is staged while the file is being built. The host
    /// points this at the product's own protected folder; when it is not set the system
    /// temporary folder is used. Either way the staging file is opened so that the operating
    /// system removes it even if the program stops abruptly.
    /// </summary>
    public string? StagingDirectory { get; init; }

    public TimeProvider Time { get; init; } = TimeProvider.System;

    public int Version { get; init; } = ContainerManifest.CurrentVersion;
}

/// <summary>
/// Produces the ZIP shaped containers: <c>manifest.json</c>, <c>payload.bin</c> and
/// <c>signature.bin</c>. The payload is written through a staging file, so a backup of
/// several gigabytes never has to fit in memory.
/// </summary>
public static class ContainerWriter
{
    private const string PayloadLabel = "wakeel.payload";

    /// <summary>
    /// Writes the container to a file. The bytes go to a neighbouring temporary name and are
    /// moved into place only once the file is complete, so an interrupted export can never
    /// leave a half written file that looks like a good one.
    /// </summary>
    public static ContainerManifest Write(string path, ContainerWriteRequest request)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(request);

        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = full + ".tmp";
        try
        {
            ContainerManifest manifest;
            using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                manifest = Write(output, request);
                output.Flush(flushToDisk: true);
            }

            File.Move(temporary, full, overwrite: true);
            return manifest;
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    public static ContainerManifest Write(Stream output, ContainerWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        // The staging file holds the payload in the clear. It is opened so that the operating
        // system deletes it when the handle closes, including on an abnormal exit.
        using var staging = CreateStagingFile(request.StagingDirectory);
        var entries = BuildPayload(staging, request.Entries);
        staging.Position = 0;

        var contentKey = request.Key.CreateContentKey(request.Kind, out var kdf, out var sealedKey);
        try
        {
            var adminSealedKey = request.OrgAgreementPublicKey is { } orgKey
                ? ContainerKeySource.SealForAdmin(orgKey, contentKey)
                : null;

            byte[] payloadHash;
            byte[] manifestBytes;
            ContainerManifest manifest;

            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                var payloadEntry = archive.CreateEntry(ContainerManifest.PayloadFileName, CompressionLevel.NoCompression);
                using (var payloadStream = payloadEntry.Open())
                using (var hashing = new HashingStream(payloadStream))
                {
                    Aead.EncryptStream(contentKey, staging, hashing, PayloadAssociatedData(request.Kind, request.Version));
                    hashing.Flush();
                    payloadHash = hashing.Hash;
                }

                manifest = new ContainerManifest(
                    request.Kind,
                    request.Version,
                    request.Producer,
                    request.Time.GetUtcNow(),
                    request.Key.Mode,
                    kdf,
                    sealedKey,
                    adminSealedKey,
                    entries);

                manifestBytes = CanonicalJson.SerializeToUtf8Bytes(manifest);
                var manifestEntry = archive.CreateEntry(ContainerManifest.FileName, CompressionLevel.Optimal);
                using (var manifestStream = manifestEntry.Open())
                {
                    manifestStream.Write(manifestBytes);
                }

                var signature = request.Signer.Sign(SigningInput(Sha256.Hash(manifestBytes), payloadHash));
                var signatureEntry = archive.CreateEntry(ContainerManifest.SignatureFileName, CompressionLevel.NoCompression);
                using (var signatureStream = signatureEntry.Open())
                {
                    signatureStream.Write(signature);
                }
            }

            return manifest;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
        }
    }

    /// <summary>
    /// The bytes an Ed25519 signature covers: a constant label, then the two hashes in order.
    /// The label keeps a container signature from ever being read as a signature over one of
    /// the other structures the same device key signs.
    /// </summary>
    internal static byte[] SigningInput(ReadOnlySpan<byte> manifestHash, ReadOnlySpan<byte> payloadHash)
    {
        var hashes = new byte[manifestHash.Length + payloadHash.Length];
        manifestHash.CopyTo(hashes);
        payloadHash.CopyTo(hashes.AsSpan(manifestHash.Length));
        return DomainSeparation.Wrap(DomainSeparation.Container, hashes);
    }

    internal static byte[] PayloadAssociatedData(ContainerKind kind, int version) =>
        Encoding.UTF8.GetBytes($"{PayloadLabel}|{ContainerKinds.Token(kind)}|v{version}");

    /// <summary>Opens a staging file that the operating system removes when it is closed.</summary>
    internal static FileStream CreateStagingFile(string? stagingDirectory)
    {
        var directory = string.IsNullOrEmpty(stagingDirectory) ? Path.GetTempPath() : stagingDirectory;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "wakeel-" + Path.GetRandomFileName());

        return new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.DeleteOnClose);
    }

    private static IReadOnlyList<ContainerEntry> BuildPayload(Stream staging, IReadOnlyList<ContainerEntrySource> sources)
    {
        var entries = new List<ContainerEntry>(sources.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        using (var archive = new ZipArchive(staging, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var source in sources)
            {
                if (!seen.Add(source.Name))
                {
                    throw new CryptoException(ErrorCode.Corrupt, "Two container entries carry the same name.");
                }

                var entry = archive.CreateEntry(source.Name, CompressionLevel.Optimal);
                using var target = entry.Open();
                using var hashing = new HashingStream(target);
                using (var input = source.OpenRead())
                {
                    input.CopyTo(hashing);
                }

                hashing.Flush();
                entries.Add(new ContainerEntry(source.Name, hashing.BytesWritten, Sha256.ToHex(hashing.Hash)));
            }
        }

        staging.Flush();
        entries.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
        return entries;
    }

    private static void Validate(ContainerWriteRequest request)
    {
        if (request.Entries.Count == 0)
        {
            throw new CryptoException(ErrorCode.Corrupt, "A container must carry at least one entry.");
        }

        if (request.Version < 1)
        {
            throw new CryptoException(ErrorCode.UnknownKind, "The container format version is not usable.");
        }

        if (request.Key.IsAdminCopy)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The administration keys open files; they do not make them.");
        }

        if (request.Key.Mode == PayloadMode.SealedFor && request.Key.RecipientPublicKey is null)
        {
            throw new CryptoException(ErrorCode.Corrupt, "Sealing a container needs the recipient public key.");
        }

        if (request.OrgAgreementPublicKey is { } supplied && supplied.Length != DeviceIdentity.PublicKeySize)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The organisation public key has the wrong length.");
        }

        if (ContainerKinds.RequiresAdminCopy(request.Kind) && request.OrgAgreementPublicKey is null)
        {
            throw new CryptoException(
                ErrorCode.Corrupt,
                "This kind of file must also carry a copy that the administration tool can open.");
        }

        if (request.Producer?.Body is null
            || !Base64Url.TryDecode(request.Producer.Body.Ed25519Pub, out var declared)
            || !declared.AsSpan().SequenceEqual(request.Signer.SigningPublicKey))
        {
            throw new CryptoException(ErrorCode.BadSignature, "The signing keys do not match the producer certificate.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A leftover temporary file is harmless; the operating system clears it later.
        }
        catch (UnauthorizedAccessException)
        {
            // Same reasoning.
        }
    }
}
