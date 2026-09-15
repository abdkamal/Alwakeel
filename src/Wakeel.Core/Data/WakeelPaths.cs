namespace Wakeel.Core.Data;

/// <summary>
/// The on-disk layout of one الوكيل installation (ARCHITECTURE.md §2). The root defaults to
/// <c>%ProgramData%\Wakeel</c> but can be overridden (e.g. by tests, or by the "مدير نظام
/// الوكيل" tool for its own <c>WakeelAdmin</c> layout) via <see cref="WakeelPaths(string)"/>.
/// </summary>
public sealed class WakeelPaths
{
    public WakeelPaths(string root)
    {
        Root = root;
    }

    /// <summary>The installation's data root, e.g. <c>C:\ProgramData\Wakeel</c>.</summary>
    public string Root { get; }

    public string DataDir => Path.Combine(Root, "data");

    /// <summary>The SQLCipher-encrypted database file.</summary>
    public string DbPath => Path.Combine(DataDir, "wakeel.db");

    public string VaultDir => Path.Combine(Root, "vault");

    public string KeysDir => Path.Combine(Root, "keys");

    public string InstallationKeyPath => Path.Combine(KeysDir, "installation.key");

    public string ModelsDir => Path.Combine(Root, "models");

    public string PackagesDir => Path.Combine(Root, "packages");

    public string PackagesOutboxDir => Path.Combine(PackagesDir, "outbox");

    public string PackagesInboxDir => Path.Combine(PackagesDir, "inbox");

    public string BackupsDir => Path.Combine(Root, "backups");

    public string LogsDir => Path.Combine(Root, "logs");

    /// <summary>Default installation root under the machine's ProgramData folder.</summary>
    public static WakeelPaths Default() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Wakeel"));

    /// <summary>Creates an isolated root under a temp/test directory; convenient for tests.</summary>
    public static WakeelPaths ForRoot(string root) => new(root);

    /// <summary>The vault path for a document's original bytes: <c>vault\&lt;first two hex chars&gt;\&lt;sha256&gt;.bin</c>.</summary>
    public string VaultFilePath(string sha256Hex)
    {
        if (string.IsNullOrWhiteSpace(sha256Hex) || sha256Hex.Length < 2)
        {
            throw new ArgumentException("SHA-256 hex string is required.", nameof(sha256Hex));
        }

        return Path.Combine(VaultDir, sha256Hex[..2], sha256Hex + ".bin");
    }

    /// <summary>Creates every directory in the layout if missing.</summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(VaultDir);
        Directory.CreateDirectory(KeysDir);
        Directory.CreateDirectory(ModelsDir);
        Directory.CreateDirectory(PackagesOutboxDir);
        Directory.CreateDirectory(PackagesInboxDir);
        Directory.CreateDirectory(BackupsDir);
        Directory.CreateDirectory(LogsDir);
    }
}
