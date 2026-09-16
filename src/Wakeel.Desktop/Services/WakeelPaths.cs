using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Wakeel.Desktop.Services;

/// <summary>
/// Resolves the on-disk folders the desktop host writes to: the installation root of
/// ARCHITECTURE.md §2 (<c>C:\ProgramData\Wakeel</c> with <c>data\</c>, <c>vault\</c>, <c>keys\</c>,
/// <c>models\</c>, <c>packages\</c>, <c>backups\</c>, <c>logs\</c> and <c>staging\</c>), a per-user
/// root for the WebView2 profile and the persisted UI state, and the folder Serilog writes to.
/// </summary>
/// <remarks>
/// The installation root can be moved with <c>--data-folder=&lt;path&gt;</c>. That flag exists for
/// the automated walkthrough and the acceptance harness, which have to drive a first run and an
/// activated installation side by side on one machine without touching the real one; a shipped
/// installation never passes it and lands on ProgramData.
/// </remarks>
internal static class WakeelPaths
{
    private const string AppFolderName = "Wakeel";

    /// <summary>The command-line flag that moves the whole installation somewhere else.</summary>
    private const string DataFolderFlag = "--data-folder=";

    /// <summary>%LocalAppData%\Wakeel.</summary>
    public static string LocalAppDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);

    /// <summary>%ProgramData%\Wakeel.</summary>
    public static string ProgramDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppFolderName);

    // Declared after the two roots on purpose: a static field initialiser placed above them would
    // read a property whose own initialiser has not run yet.
    private static string _installationRoot = ProgramDataRoot;

    private static string _profileRoot = LocalAppDataRoot;

    /// <summary>The installation root in force for this run.</summary>
    public static string InstallationRoot => _installationRoot;

    /// <summary>
    /// Reads <c>--data-folder=&lt;path&gt;</c> off the command line and points this run's whole
    /// layout — the installation, the WebView2 profile and the UI state — at that folder instead.
    /// Called once, before anything opens a file.
    /// </summary>
    public static void Configure(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        foreach (var arg in args)
        {
            if (!arg.StartsWith(DataFolderFlag, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = arg[DataFolderFlag.Length..].Trim('"');
            if (value.Length == 0)
            {
                continue;
            }

            string full;
            try
            {
                full = Path.GetFullPath(value);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException
                                                  or PathTooLongException)
            {
                // Not a path this machine can express — ignore it rather than write somewhere
                // unpredictable; the shipped location is always a safe landing.
                continue;
            }

            _installationRoot = full;

            // A run pointed at its own folder keeps everything there, so nothing it does can be
            // mistaken for, or disturb, the installation this machine actually carries.
            _profileRoot = full;
            return;
        }
    }

    /// <summary>
    /// The installation layout, with every folder of ARCHITECTURE.md §2 created and — as far as
    /// this process is allowed to — writable by the people who use this computer.
    /// </summary>
    public static Core.Data.WakeelPaths CreateInstallationPaths()
    {
        var paths = Core.Data.WakeelPaths.ForRoot(_installationRoot);

        try
        {
            Directory.CreateDirectory(_installationRoot);
            GrantUsersWriteAccess(_installationRoot);
            paths.EnsureDirectories();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A machine whose ProgramData an ordinary account may not create is exactly the case the
            // installer exists for. The screens say so in words when a write later fails, and
            // starting up is never the moment to stop with a message nobody can act on.
        }

        return paths;
    }

    /// <summary>
    /// The folder Serilog should write to: the installation's own <c>logs\</c> when that can be
    /// created and written to, otherwise a per-user <c>logs</c> folder (a standard, non-admin Windows
    /// account on first run, before an installer has provisioned ProgramData permissions).
    /// </summary>
    public static string ResolveLogFolder()
    {
        var installationLogs = Path.Combine(_installationRoot, "logs");
        if (TryEnsureWritableDirectory(installationLogs))
        {
            return installationLogs;
        }

        var localLogs = Path.Combine(_profileRoot, "logs");
        Directory.CreateDirectory(localLogs);
        return localLogs;
    }

    /// <summary>The WebView2 user-data folder (cache, cookies, profile), where no elevation is needed.</summary>
    public static string WebView2UserDataFolder
    {
        get
        {
            var folder = Path.Combine(_profileRoot, "WebView2");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    /// <summary>Path of the JSON file backing <see cref="FileUiStateStore"/>.</summary>
    public static string UiStateFilePath
    {
        get
        {
            Directory.CreateDirectory(_profileRoot);
            return Path.Combine(_profileRoot, "ui-state.json");
        }
    }

    /// <summary>
    /// Lets everybody who signs in to this computer read and write inside the installation folder.
    /// ARCHITECTURE.md §2 puts that on the installer, and the installer does it properly; doing it
    /// here as well is what makes a copy unpacked by hand — or a walkthrough folder created by a
    /// test — behave like an installed one. Every secret inside is encrypted, so read access to the
    /// folder is not read access to the work.
    /// </summary>
    private static void GrantUsersWriteAccess(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var directory = new DirectoryInfo(path);
            var security = directory.GetAccessControl();
            var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

            security.AddAccessRule(new FileSystemAccessRule(
                users,
                FileSystemRights.Modify | FileSystemRights.Synchronize,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            directory.SetAccessControl(security);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException
                                              or PlatformNotSupportedException
                                              or InvalidOperationException
                                              or IdentityNotMappedException)
        {
            // Only an administrator may change an access list. A standard account simply keeps
            // whatever the installer left, which is the arrangement that ships anyway.
        }
    }

    private static bool TryEnsureWritableDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probePath = Path.Combine(path, $".write-check-{Guid.NewGuid():N}");
            File.WriteAllText(probePath, string.Empty);
            File.Delete(probePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
