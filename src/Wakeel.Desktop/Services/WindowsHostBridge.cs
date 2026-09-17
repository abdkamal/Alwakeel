using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using Serilog;
using Wakeel.UI.Services.Shell;

namespace Wakeel.Desktop.Services;

/// <summary>
/// Opens the Windows date-and-time settings for W11's «تصحيح الساعة».
/// </summary>
/// <remarks>
/// Launched through the shell as the fixed <c>ms-settings:dateandtime</c> URI with
/// <c>UseShellExecute</c>, which is what hands it to the Settings app. Nothing about the string is
/// taken from data, from the user or from a record: it is a compile-time constant, so there is no
/// path by which a stored value could become a command line.
/// </remarks>
public sealed class WindowsSystemSettings : ISystemSettingsLauncher
{
    private const string DateAndTimeUri = "ms-settings:dateandtime";

    public bool OpenDateAndTime()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(DateAndTimeUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            // A machine with no Settings app, or a policy that blocks it. The screen says so in
            // Arabic; the reason belongs here, in the host log, and nowhere near the person.
            Log.Warning(exception, "Could not open the Windows date and time settings");
            return false;
        }
    }
}

/// <summary>Saves the health report (W12's «تصدير تقرير الصحة») wherever the person chooses.</summary>
/// <remarks>
/// The dialog runs on the WPF dispatcher, because a common file dialog must be shown from the UI
/// thread, and the write itself is plain UTF-8 text. Nothing but the report's own Arabic sentences
/// reaches the file — <see cref="Wakeel.Core.Services.IHealthService.ExportText"/> builds it — so no
/// key, path or identifier leaves the machine with it.
/// </remarks>
public sealed class WindowsFileSaveService(Application application) : IFileSaveService
{
    public async Task<string?> SaveTextAsync(string suggestedFileName, string contents, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);
        ArgumentNullException.ThrowIfNull(contents);

        var path = await application.Dispatcher.InvokeAsync(() =>
        {
            var dialog = new SaveFileDialog
            {
                FileName = suggestedFileName,
                DefaultExt = Path.GetExtension(suggestedFileName),
                AddExtension = true,
                OverwritePrompt = true,
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        });

        if (path is null)
        {
            return null;
        }

        try
        {
            // A BOM, because the file is meant to be opened in a text editor on a Windows machine
            // and Arabic without one is read as the wrong code page often enough to matter.
            await File.WriteAllTextAsync(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken)
                .ConfigureAwait(false);
            return path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A null return means "the person said no"; a folder that refuses the write is a
            // different answer and the screen must say so. The reason stays in the host log.
            Log.Warning(exception, "Could not write the exported health report");
            throw new IOException("The exported report could not be written.", exception);
        }
    }
}
