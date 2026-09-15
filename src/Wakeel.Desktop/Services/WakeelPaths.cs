using System.IO;

namespace Wakeel.Desktop.Services;

/// <summary>
/// Resolves the on-disk folders the desktop host writes to: a per-machine ProgramData root used for
/// logs when writable, falling back to the per-user LocalAppData root; and a per-user LocalAppData
/// root used for the WebView2 user-data folder and the persisted UI-state file.
/// </summary>
internal static class WakeelPaths
{
    private const string AppFolderName = "Wakeel";

    /// <summary>%LocalAppData%\Wakeel.</summary>
    public static string LocalAppDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);

    /// <summary>%ProgramData%\Wakeel.</summary>
    public static string ProgramDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppFolderName);

    /// <summary>
    /// The folder Serilog should write to: <c>%ProgramData%\Wakeel\logs</c> when that root can be
    /// created and written to, otherwise <c>%LocalAppData%\Wakeel\logs</c> (e.g. a standard, non-admin
    /// Windows user account on first run before an installer has provisioned ProgramData permissions).
    /// </summary>
    public static string ResolveLogFolder()
    {
        var programDataLogs = Path.Combine(ProgramDataRoot, "logs");
        if (TryEnsureWritableDirectory(programDataLogs))
        {
            return programDataLogs;
        }

        var localLogs = Path.Combine(LocalAppDataRoot, "logs");
        Directory.CreateDirectory(localLogs);
        return localLogs;
    }

    /// <summary>The WebView2 user-data folder (cache, cookies, profile), under LocalAppData so it works without elevation.</summary>
    public static string WebView2UserDataFolder
    {
        get
        {
            var folder = Path.Combine(LocalAppDataRoot, "WebView2");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    /// <summary>Path of the JSON file backing <see cref="FileUiStateStore"/>.</summary>
    public static string UiStateFilePath
    {
        get
        {
            Directory.CreateDirectory(LocalAppDataRoot);
            return Path.Combine(LocalAppDataRoot, "ui-state.json");
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
