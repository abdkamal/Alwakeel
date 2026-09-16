using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Wakeel.Admin.UI.Data;

namespace Wakeel.Admin.Services;

/// <summary>
/// Resolves the folders the administration host writes to: the tool's own root
/// (<c>C:\ProgramData\WakeelAdmin</c> with <c>keys\</c>, <c>exports\</c>, <c>staging\</c> and
/// <c>logs\</c>), a per-user root for the browser-engine profile and the remembered UI state, and
/// the folder the log goes to.
/// </summary>
/// <remarks>
/// The root can be moved with <c>--data-folder=&lt;path&gt;</c>. That flag exists for the tests and
/// the screenshot runs, which have to drive a first run and an established organisation side by
/// side on one computer without touching the real one; a shipped tool never passes it.
/// </remarks>
internal static class AdminHostPaths
{
    private const string AppFolderName = "WakeelAdmin";
    private const string DataFolderFlag = "--data-folder=";

    /// <summary>%LocalAppData%\WakeelAdmin.</summary>
    public static string LocalAppDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);

    // Declared after LocalAppDataRoot on purpose: static initialisers run in declaration order, so
    // reading it above would read a null that has not been assigned yet.
    private static string _root = AdminPaths.ProgramDataRoot;
    private static string _profileRoot = LocalAppDataRoot;

    /// <summary>The root in force for this run.</summary>
    public static string Root => _root;

    /// <summary>
    /// Reads <c>--data-folder=&lt;path&gt;</c> off the command line and points this run's whole
    /// layout — the tool's folder, the engine profile and the remembered UI state — at it instead.
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
                // Not a path this computer can express — ignore it rather than write somewhere
                // unpredictable; the shipped location is always a safe landing.
                continue;
            }

            _root = full;

            // A run pointed at its own folder keeps everything there, so nothing it does can be
            // mistaken for, or disturb, the organisation this computer actually carries.
            _profileRoot = full;
            return;
        }
    }

    /// <summary>The tool's layout, with every folder created and — as far as this process may — writable.</summary>
    public static AdminPaths CreatePaths()
    {
        var paths = AdminPaths.ForRoot(_root);

        try
        {
            Directory.CreateDirectory(_root);
            paths.EnsureDirectories();

            // Only the folders whose contents are meant to be carried around are opened up. The key
            // file and the tool's own data keep the access list they inherit, so the account that
            // created the organisation — and an administrator — remain the only ones who may write
            // to them.
            GrantUsersWriteAccess(paths.ExportsFolder);
            GrantUsersWriteAccess(paths.StagingFolder);
            GrantUsersWriteAccess(paths.LogsFolder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A computer whose ProgramData an ordinary account may not create is exactly what the
            // installer is for. The screens say so in words when a write later fails, and starting
            // up is never the moment to stop with a message nobody can act on.
        }

        return paths;
    }

    /// <summary>
    /// Where the log goes: the tool's own <c>logs\</c> when that can be created and written to,
    /// otherwise a per-user one (a standard Windows account on a computer no installer has touched).
    /// </summary>
    public static string ResolveLogFolder()
    {
        var toolLogs = Path.Combine(_root, "logs");
        if (TryEnsureWritableDirectory(toolLogs))
        {
            return toolLogs;
        }

        var localLogs = Path.Combine(_profileRoot, "logs");
        Directory.CreateDirectory(localLogs);
        return localLogs;
    }

    /// <summary>The browser-engine profile folder, where no elevation is needed.</summary>
    public static string WebView2UserDataFolder
    {
        get
        {
            var folder = Path.Combine(_profileRoot, "WebView2");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    /// <summary>Path of the JSON file that remembers the light/dark choice between runs.</summary>
    public static string UiStateFilePath
    {
        get
        {
            Directory.CreateDirectory(_profileRoot);
            return Path.Combine(_profileRoot, "ui-state.json");
        }
    }

    /// <summary>
    /// Lets everybody who signs in to this computer read and write inside one of the tool's
    /// hand-around folders — the setup files it writes out, the staging area and the log. The
    /// installer does this properly; doing it here as well is what makes a copy unpacked by hand —
    /// or a folder created by a test — behave like an installed one.
    /// </summary>
    /// <remarks>
    /// Never called on the tool's root, and so never on <c>keys\</c> or on the tool's own data.
    /// Those hold the only two wraps of the key that opens the organisation, together with the
    /// counter that slows a wrong password down: anybody who could edit them could undo the wait,
    /// or destroy the organisation's signing keys outright. One person runs this tool, and that
    /// person is the one who created the folder, so the inherited access list is exactly right.
    /// </remarks>
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
