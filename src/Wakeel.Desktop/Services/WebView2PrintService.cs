using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Wakeel.UI.Services.Account;

namespace Wakeel.Desktop.Services;

/// <summary>
/// The Windows half of <see cref="IPrintService"/>: the recovery sheet of W04 and W07 is printed,
/// or saved as a PDF, straight out of the page that is on screen.
/// </summary>
/// <remarks>
/// ARCHITECTURE.md §9 keeps the internal sheets away from Word entirely — they are HTML, and the
/// browser engine already in the window turns them into paper or into a PDF. Printing the live page
/// rather than a second rendering of it is deliberate: what comes out of the printer is exactly
/// what the person just read and ticked «طبعتها وحفظتها» for, and the page's own print stylesheet
/// (the <c>w-no-print</c> class) decides what belongs on the sheet.
/// </remarks>
public sealed class WebView2PrintService : IPrintService
{
    private readonly Func<CoreWebView2?> _webView;
    private readonly Dispatcher _dispatcher;

    public WebView2PrintService(Func<CoreWebView2?> webView, Dispatcher dispatcher)
    {
        _webView = webView;
        _dispatcher = dispatcher;
    }

    /// <inheritdoc />
    public bool IsAvailable => _webView() is not null;

    /// <inheritdoc />
    public async Task<PrintOutcome> PrintAsync(CancellationToken cancellationToken = default)
    {
        if (_webView() is null)
        {
            return PrintOutcome.Unavailable;
        }

        try
        {
            // The machine's own print dialogue: the person picks the printer, the paper and how many
            // copies, exactly as they would from anywhere else on this computer. Opening it is all
            // the engine reports back — what the person then does inside it (print, or close it) is
            // the operating system's business — so the screen only ever says the sheet was handed to
            // the printer, never that paper came out.
            await OnUiThreadAsync(view =>
            {
                view.ShowPrintUI(CoreWebView2PrintDialogKind.System);
                return Task.FromResult(true);
            }).ConfigureAwait(false);

            return PrintOutcome.Saved;
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException
                                              or ObjectDisposedException)
        {
            return PrintOutcome.Failed;
        }
    }

    /// <inheritdoc />
    public async Task<PrintResult> SaveAsPdfAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        if (_webView() is null)
        {
            return new PrintResult(PrintOutcome.Unavailable);
        }

        var path = await _dispatcher.InvokeAsync(() => AskWhereToSave(suggestedFileName));
        if (path is null)
        {
            return new PrintResult(PrintOutcome.Cancelled);
        }

        try
        {
            var written = await OnUiThreadAsync(view => view.PrintToPdfAsync(path)).ConfigureAwait(false);

            return written
                ? new PrintResult(PrintOutcome.Saved, path)
                : new PrintResult(PrintOutcome.Failed);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or COMException or InvalidOperationException
                                              or ObjectDisposedException)
        {
            return new PrintResult(PrintOutcome.Failed);
        }
    }

    /// <summary>
    /// Runs one browser-engine call on the thread that owns the window. The engine may only be
    /// spoken to from there, and every caller of this service arrives from a background task
    /// (the screens push their slow work off the drawing thread).
    /// </summary>
    private async Task<bool> OnUiThreadAsync(Func<CoreWebView2, Task<bool>> call)
    {
        var operation = _dispatcher.InvokeAsync(() =>
        {
            var view = _webView();
            return view is null ? Task.FromResult(false) : call(view);
        });

        return await await operation.Task.ConfigureAwait(false);
    }

    private static string? AskWhereToSave(string suggestedFileName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = suggestedFileName,
            DefaultExt = ".pdf",
            Filter = "PDF|*.pdf",
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };

        var owner = Application.Current?.MainWindow;
        var chosen = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        return chosen == true ? dialog.FileName : null;
    }
}
