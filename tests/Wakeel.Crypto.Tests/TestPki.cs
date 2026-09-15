using System.IO.Compression;

namespace Wakeel.Crypto.Tests;

/// <summary>An organisation, one office computer and one phone, ready to sign things.</summary>
internal sealed class TestPki : IDisposable
{
    public const string OrgId = "ORG-1";
    public const string OfficeId = "OFFICE-1";
    public const string PcDeviceId = "PC-1";
    public const string PhoneDeviceId = "PHONE-1";

    private TestPki(DateTimeOffset issuedAt)
    {
        Org = DeviceIdentity.Generate();
        Pc = DeviceIdentity.Generate();
        Phone = DeviceIdentity.Generate();
        IssuedAt = issuedAt;

        PcCertificate = DeviceCertificate.Issue(
            new DeviceCertificateBody(
                OrgId,
                OfficeId,
                PcDeviceId,
                1,
                2,
                "secretary",
                DeviceKind.Pc,
                Pc.SigningPublicKeyText,
                Pc.AgreementPublicKeyText,
                issuedAt,
                OrgId),
            Org);

        PhoneCertificate = DeviceCertificate.Issue(
            new DeviceCertificateBody(
                OrgId,
                OfficeId,
                PhoneDeviceId,
                1,
                2,
                "secretary",
                DeviceKind.Phone,
                Phone.SigningPublicKeyText,
                Phone.AgreementPublicKeyText,
                issuedAt,
                PcDeviceId),
            Pc);
    }

    public DeviceIdentity Org { get; }

    public DeviceIdentity Pc { get; }

    public DeviceIdentity Phone { get; }

    public DeviceCertificate PcCertificate { get; }

    public DeviceCertificate PhoneCertificate { get; }

    public DateTimeOffset IssuedAt { get; }

    public static TestPki Create(DateTimeOffset? issuedAt = null) =>
        new(issuedAt ?? new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));

    public RevocationList Revoke(string deviceId, DateTimeOffset revokedAt) =>
        RevocationList.Issue(
            new RevocationListBody(OrgId, revokedAt, [new RevocationEntry(deviceId, revokedAt)]),
            Org);

    public RevocationList NoRevocations(DateTimeOffset issuedAt) => RevocationList.Empty(OrgId, issuedAt, Org);

    public void Dispose()
    {
        Org.Dispose();
        Pc.Dispose();
        Phone.Dispose();
    }
}

/// <summary>A scratch folder that cleans itself up.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wakeel-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Leaving a scratch folder behind never fails a test.
        }
    }
}

/// <summary>Rewrites one part of a container so the suite can prove tampering is caught.</summary>
internal static class ZipSurgery
{
    public static Dictionary<string, byte[]> ReadAll(string path)
    {
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using var archive = ZipFile.OpenRead(path);
        foreach (var entry in archive.Entries)
        {
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            entries[entry.FullName] = buffer.ToArray();
        }

        return entries;
    }

    public static void WriteAll(string path, Dictionary<string, byte[]> entries)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var pair in entries)
        {
            var entry = archive.CreateEntry(pair.Key, CompressionLevel.NoCompression);
            using var stream = entry.Open();
            stream.Write(pair.Value);
        }
    }

    public static void Replace(string path, string name, Func<byte[], byte[]> transform)
    {
        var entries = ReadAll(path);
        entries[name] = transform(entries[name]);
        WriteAll(path, entries);
    }

    public static byte[] FlipLastByte(byte[] data)
    {
        var copy = (byte[])data.Clone();
        copy[^1] ^= 0xFF;
        return copy;
    }
}
