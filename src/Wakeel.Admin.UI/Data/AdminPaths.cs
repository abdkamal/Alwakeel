namespace Wakeel.Admin.UI.Data;

/// <summary>
/// Where the administration tool keeps its things: the database, the key file, the setup files it
/// exports, the folder it assembles them in, and its log.
/// </summary>
/// <remarks>
/// A shipped tool lands on <c>C:\ProgramData\WakeelAdmin</c>. The host moves the whole layout
/// somewhere else with <c>--data-folder=&lt;path&gt;</c>, which is how the tests and the screenshot
/// runs work on a temporary folder without touching the real organisation on this machine.
/// </remarks>
public sealed class AdminPaths
{
    private const string AppFolderName = "WakeelAdmin";

    private AdminPaths(string root)
    {
        Root = root;
    }

    /// <summary>The folder everything below lives in.</summary>
    public string Root { get; }

    /// <summary>The SQLCipher database of DATA-MODEL.md §13.</summary>
    public string DatabaseFile => Path.Combine(Root, "admin.db");

    /// <summary>Wrapped copies of the database key, one per way of opening it.</summary>
    public string KeyFile => Path.Combine(KeysFolder, "admin.key");

    /// <summary>Holds the key file.</summary>
    public string KeysFolder => Path.Combine(Root, "keys");

    /// <summary>Where finished setup files are offered to be saved.</summary>
    public string ExportsFolder => Path.Combine(Root, "exports");

    /// <summary>Where a setup file is assembled before it becomes one.</summary>
    public string StagingFolder => Path.Combine(Root, "staging");

    /// <summary>Where the tool writes its log.</summary>
    public string LogsFolder => Path.Combine(Root, "logs");

    /// <summary>The shipped location: <c>%ProgramData%\WakeelAdmin</c>.</summary>
    public static string ProgramDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppFolderName);

    /// <summary>The layout under a chosen root.</summary>
    public static AdminPaths ForRoot(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        return new AdminPaths(Path.GetFullPath(root));
    }

    /// <summary>The shipped layout.</summary>
    public static AdminPaths Default() => ForRoot(ProgramDataRoot);

    /// <summary>Creates every folder of the layout that does not exist yet.</summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(KeysFolder);
        Directory.CreateDirectory(ExportsFolder);
        Directory.CreateDirectory(StagingFolder);
        Directory.CreateDirectory(LogsFolder);
    }
}
