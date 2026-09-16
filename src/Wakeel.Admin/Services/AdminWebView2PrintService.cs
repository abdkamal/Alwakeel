using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Wakeel.Admin.UI.Services.Account;

namespace Wakeel.Admin.Services;

/// <summary>
/// The Windows half of <see cref="IAdminPrintService"/>: the organisation recovery sheet of A01 is
/// printed, or saved as a PDF, straight out of the page that is on screen.
/// </summary>
/// <remarks>
/// ARCHITECTURE.md §9 keeps the internal sheets away from Word entirely — they are HTML, and the
/// browser engine already in the window turns them into paper or into a PDF. Printing the live page
/// rather than a second rendering of it is deliberate: what comes out of the printer is exactly
/// what the person just read and ticked «طبعتها وحفظتها» for, and the page's own print stylesheet
/// (the <c>a-no-print</c> class) decides what belongs on the sheet.
/// </remarks>
public sealed class AdminWebView2PrintService : IAdminPrintService
{
    private readonly Func<CoreWebView2?> _webView;
    private readonly Dispatcher _dispatcher;

    public AdminWebView2PrintService(Func<CoreWebView2?> webView, Dispatcher dispatcher)
    {
        _webView = webView;
        _dispatcher = dispatcher;
    }

    /// <inheritdoc />
    public bool IsAvailable => _webView() is not null;

    /// <inheritdoc />
    public async Task<AdminPrintOutcome> PrintAsync(CancellationToken cancellationToken = default)
    {
        if (_webView() is null)
        {
            return AdminPrintOutcome.Unavailable;
        }

        try
        {
            // This computer's own print dialogue: the person picks the printer, the paper and how
            // many copies, exactly as they would from anywhere else. The engine opens it and returns
            // at once — it never says what the person then chose — so the screen only reports that
            // the dialogue was opened, and says nothing about what came out of the printer.
            await _dispatcher.InvokeAsync(() => _webView()?.ShowPrintUI(CoreWebView2PrintDialogKind.System));

            return AdminPrintOutcome.DialogOpened;
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException
                                              or ObjectDisposedException)
        {
            return AdminPrintOutcome.Failed;
        }
    }

    /// <inheritdoc />
    public async Task<AdminPrintResult> SaveAsPdfAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        if (_webView() is null)
        {
            return new AdminPrintResult(AdminPrintOutcome.Unavailable);
        }

        var path = await _dispatcher.InvokeAsync(() => AskWhereToSave(suggestedFileName));
        if (path is null)
        {
            return new AdminPrintResult(AdminPrintOutcome.Cancelled);
        }

        try
        {
            var written = await OnUiThreadAsync(view => view.PrintToPdfAsync(path)).ConfigureAwait(false);

            return written
                ? new AdminPrintResult(AdminPrintOutcome.Saved, path)
                : new AdminPrintResult(AdminPrintOutcome.Failed);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or COMException or InvalidOperationException
                                              or ObjectDisposedException)
        {
            return new AdminPrintResult(AdminPrintOutcome.Failed);
        }
    }

    /// <summary>
    /// Runs one browser-engine call on the thread that owns the window. The engine may only be
    /// spoken to from there, and the screens push their slow work off the drawing thread.
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
