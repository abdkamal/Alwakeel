namespace Wakeel.UI.Services.Shell;

/// <summary>
/// The two things the daily shell needs from the machine it runs on and a web view cannot do:
/// opening the Windows date-and-time settings (W11's «تصحيح الساعة») and asking the person where to
/// put a file it has just produced (W12's «تصدير تقرير الصحة»).
/// </summary>
/// <remarks>
/// Kept as interfaces so Wakeel.UI stays free of Windows APIs and both screens can be driven from a
/// bUnit test with a fake. The desktop host registers the real implementations; anything that does
/// not (a test host) gets <see cref="NoHostBridge"/>, which says honestly that it cannot rather
/// than pretending it did.
/// </remarks>
public interface ISystemSettingsLauncher
{
    /// <summary>Opens the Windows date-and-time settings page. False when this machine cannot.</summary>
    bool OpenDateAndTime();
}

/// <summary>Writes a file the person chooses the place and name of.</summary>
public interface IFileSaveService
{
    /// <summary>
    /// Asks where to save, then writes <paramref name="contents"/> as UTF-8 text.
    /// Returns the chosen path, or null when the person cancelled or no dialog is available.
    /// </summary>
    /// <param name="suggestedFileName">The name offered in the dialog, extension included.</param>
    /// <param name="contents">The text to write.</param>
    Task<string?> SaveTextAsync(string suggestedFileName, string contents, CancellationToken cancellationToken = default);
}

/// <summary>The "this machine cannot do that" answers, used wherever the desktop host is absent.</summary>
public sealed class NoHostBridge : ISystemSettingsLauncher, IFileSaveService
{
    public bool OpenDateAndTime() => false;

    public Task<string?> SaveTextAsync(string suggestedFileName, string contents, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
