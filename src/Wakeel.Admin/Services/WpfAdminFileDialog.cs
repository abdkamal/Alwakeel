using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Microsoft.Win32;
using Wakeel.Admin.UI.Services.Export;

namespace Wakeel.Admin.Services;

/// <summary>
/// The Windows half of <see cref="IAdminFileDialog"/>: this computer's own «save as», «open» and
/// «choose a folder» windows, and its file window.
/// </summary>
/// <remarks>
/// Every one of them is opened on the window's own thread, which is also the thread the screens
/// render on, so a screen may call these straight from a button. Nothing here writes or reads a
/// file: it only answers where, and the screen that asked does the writing, so a chooser that is
/// dismissed costs nothing at all.
/// </remarks>
public sealed class WpfAdminFileDialog : IAdminFileDialog
{
    private readonly Dispatcher _dispatcher;

    public WpfAdminFileDialog(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public AdminFileChoice AskWhereToSave(string suggestedFileName, string filterLabel, string extension) =>
        OnWindowThread(() =>
        {
            var dialog = new SaveFileDialog
            {
                FileName = suggestedFileName,
                DefaultExt = extension,
                Filter = Filter(filterLabel, [extension]),
                OverwritePrompt = true,
                AddExtension = true,
            };

            return dialog.ShowDialog() == true
                ? new AdminFileChoice(true, dialog.FileName)
                : AdminFileChoice.None;
        });

    /// <inheritdoc />
    public AdminFileChoice AskWhichFile(string filterLabel, IReadOnlyList<string> extensions) =>
        OnWindowThread(() =>
        {
            var dialog = new OpenFileDialog
            {
                Filter = Filter(filterLabel, extensions),
                CheckFileExists = true,
                Multiselect = false,
            };

            return dialog.ShowDialog() == true
                ? new AdminFileChoice(true, dialog.FileName)
                : AdminFileChoice.None;
        });

    /// <inheritdoc />
    public AdminFileChoice AskWhichFolder(string prompt) =>
        OnWindowThread(() =>
        {
            var dialog = new OpenFolderDialog
            {
                Title = prompt,
                Multiselect = false,
            };

            return dialog.ShowDialog() == true
                ? new AdminFileChoice(true, dialog.FolderName)
                : AdminFileChoice.None;
        });

    /// <inheritdoc />
    public void Reveal(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            // A file is shown selected inside its folder; a folder is simply opened. The path is one
            // this tool wrote itself, and it is handed over as an argument rather than through a
            // shell command line, so nothing in a file name can become an instruction.
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false })?.Dispose();
            }
            else if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // Not being able to open a file window is never a reason to interrupt the work: the file
            // itself is written and the screen has already said where it is.
        }
    }

    /// <summary>The one filter line a chooser shows, in Arabic, with the extensions behind it.</summary>
    private static string Filter(string label, IReadOnlyList<string> extensions)
    {
        var patterns = string.Join(';', extensions.Select(extension => "*" + extension));
        return $"{label}|{patterns}";
    }

    private static AdminFileChoice Run(Func<AdminFileChoice> ask)
    {
        try
        {
            return ask();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return AdminFileChoice.None;
        }
    }

    private AdminFileChoice OnWindowThread(Func<AdminFileChoice> ask) =>
        _dispatcher.CheckAccess() ? Run(ask) : _dispatcher.Invoke(() => Run(ask));
}
