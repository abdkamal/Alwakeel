using System.Text.Json.Serialization;

namespace Wakeel.Crypto;

/// <summary>
/// The contents of <c>keys\installation.key</c>: every wrap of the database key and the
/// vault key, plus the device private seeds encrypted with the database key.
/// </summary>
public sealed class InstallationKeyFile
{
    public const int CurrentVersion = 1;

    /// <summary>Associated data label of the encrypted device seeds.</summary>
    public const string DeviceSeedsContext = "wakeel.deviceseeds";

    public int Version { get; set; } = CurrentVersion;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<KeyWrap> DbKeyWraps { get; set; } = [];

    public List<KeyWrap> VaultKeyWraps { get; set; } = [];

    /// <summary>The device seeds sealed with the database key, or null before activation.</summary>
    public byte[]? EncryptedDeviceSeeds { get; set; }

    public static InstallationKeyFile Create(TimeProvider? timeProvider = null)
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        return new InstallationKeyFile { CreatedAt = now, UpdatedAt = now };
    }

    public static InstallationKeyFile Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!File.Exists(path))
        {
            throw new CryptoException(ErrorCode.Corrupt, "The installation key file is missing.");
        }

        var bytes = File.ReadAllBytes(path);
        var file = CanonicalJson.Deserialize<InstallationKeyFile>(bytes);
        if (file.Version != CurrentVersion)
        {
            throw new CryptoException(ErrorCode.UnknownKind, "The installation key file uses an unsupported format version.");
        }

        return file;
    }

    /// <summary>
    /// Writes through a temporary file and keeps the previous contents as a <c>.bak</c> copy,
    /// so an interrupted write can never leave the installation without its keys.
    /// </summary>
    public void Save(string path, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        UpdatedAt = (timeProvider ?? TimeProvider.System).GetUtcNow();

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var bytes = CanonicalJson.SerializeToUtf8Bytes(this);
        var temporary = path + ".tmp";
        var backup = path + ".bak";

        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(path))
        {
            File.Copy(path, backup, overwrite: true);
        }

        File.Move(temporary, path, overwrite: true);
    }

    public KeyWrap? FindDbKeyWrap(KeyWrapKind kind) => Find(DbKeyWraps, kind);

    public KeyWrap? FindVaultKeyWrap(KeyWrapKind kind) => Find(VaultKeyWraps, kind);

    public void SetDbKeyWrap(KeyWrap wrap) => Set(DbKeyWraps, wrap);

    public void SetVaultKeyWrap(KeyWrap wrap) => Set(VaultKeyWraps, wrap);

    public void RemoveDbKeyWrap(KeyWrapKind kind) => DbKeyWraps.RemoveAll(w => w.Kind == kind);

    public void RemoveVaultKeyWrap(KeyWrapKind kind) => VaultKeyWraps.RemoveAll(w => w.Kind == kind);

    /// <summary>Seals the device private seeds with the database key.</summary>
    public void SetDeviceSeeds(ReadOnlySpan<byte> dbKey, DeviceSeeds seeds)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        seeds.Validate();
        var plain = CanonicalJson.SerializeToUtf8Bytes(seeds);
        EncryptedDeviceSeeds = Aead.Encrypt(dbKey, plain, DeviceSeedsContext);
    }

    /// <summary>Opens the device private seeds with the database key.</summary>
    public DeviceSeeds ReadDeviceSeeds(ReadOnlySpan<byte> dbKey)
    {
        if (EncryptedDeviceSeeds is null || EncryptedDeviceSeeds.Length == 0)
        {
            throw new CryptoException(ErrorCode.Corrupt, "This installation has no stored device seeds.");
        }

        var plain = Aead.Decrypt(dbKey, EncryptedDeviceSeeds, DeviceSeedsContext);
        var seeds = CanonicalJson.Deserialize<DeviceSeeds>(plain);
        seeds.Validate();
        return seeds;
    }

    [JsonIgnore]
    public bool HasDeviceSeeds => EncryptedDeviceSeeds is { Length: > 0 };

    private static KeyWrap? Find(List<KeyWrap> wraps, KeyWrapKind kind) =>
        wraps.FirstOrDefault(w => w.Kind == kind);

    private static void Set(List<KeyWrap> wraps, KeyWrap wrap)
    {
        ArgumentNullException.ThrowIfNull(wrap);
        wraps.RemoveAll(w => w.Kind == wrap.Kind);
        wraps.Add(wrap);
        wraps.Sort((left, right) => left.Kind.CompareTo(right.Kind));
    }
}
