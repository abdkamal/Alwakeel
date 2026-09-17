using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using CorePaths = Wakeel.Core.Data.WakeelPaths;
using Wakeel.Core.Services.Correspondence;
using Wakeel.Reports.Letters;

namespace Wakeel.Desktop.Services;

/// <inheritdoc cref="ILetterPdfWriter"/>
/// <remarks>
/// <para>
/// ARCHITECTURE §9: a letter becomes a PDF through Word when Word is installed, because that is
/// the file the office will compare against what comes out of its own printer. When Word is not
/// there, the same letter is rendered as HTML laid out like the template and printed through a
/// browser engine, so that "Word غير متوفر" never stops anybody printing an approved letter.
/// </para>
/// <para>
/// That fallback starts an engine of its own, off screen, and never touches the one the window is
/// running on. The window's engine is the application: navigating it away to print a letter would
/// throw away every screen's state, and the wizard the user is standing in — with a letter typed
/// into it and not yet saved — would come back empty. The private engine lives on a window that
/// is created but never shown, prints, and is closed again; the user sees nothing but the saved
/// file.
/// </para>
/// <para>
/// The print settings carry the letter's own paper. A browser lays a page out to whatever the
/// print job says and ignores the size declared in the document's style sheet, so an A5 letter
/// printed with the defaults would come out on a foreign page with two thirds of it blank.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class LetterPdfWriter : ILetterPdfWriter
{
    /// <summary>How long the off-screen engine is given to load and print one letter.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly WordAutomation _word;
    private readonly CorePaths _paths;
    private readonly Dispatcher _dispatcher;

    /// <summary>Creates the writer.</summary>
    /// <param name="word">The Word half, used first when Word is installed.</param>
    /// <param name="paths">The installation layout; the private engine keeps its files under it.</param>
    /// <param name="dispatcher">The window's thread, which a browser engine must be created on.</param>
    public LetterPdfWriter(WordAutomation word, CorePaths paths, Dispatcher dispatcher)
    {
        _word = word ?? throw new ArgumentNullException(nameof(word));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <inheritdoc />
    public bool IsAvailable => _word.IsAvailable || BrowserEnginePresent();

    /// <inheritdoc />
    public async Task<byte[]?> ToPdfAsync(
        byte[] docx,
        string html,
        LetterPageSize pageSize = LetterPageSize.A4,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(docx);

        if (_word.IsAvailable)
        {
            var throughWord = await _word.ToPdfAsync(docx, "letter.docx", cancellationToken).ConfigureAwait(false);
            if (throughWord is { Length: > 0 })
            {
                return throughWord;
            }
        }

        return string.IsNullOrEmpty(html)
            ? null
            : await ThroughWebViewAsync(html, pageSize, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Whether a browser engine is installed on this machine at all.</summary>
    private static bool BrowserEnginePresent()
    {
        try
        {
            return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (Exception exception) when (exception is WebView2RuntimeNotFoundException
                                              or DllNotFoundException
                                              or COMException)
        {
            return false;
        }
    }

    private async Task<byte[]?> ThroughWebViewAsync(
        string html,
        LetterPageSize pageSize,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _dispatcher
                .InvokeAsync(() => PrintOffScreenAsync(html, pageSize, cancellationToken))
                .Task
                .Unwrap()
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is COMException
                                              or InvalidOperationException
                                              or ObjectDisposedException
                                              or IOException
                                              or UnauthorizedAccessException
                                              or WebView2RuntimeNotFoundException
                                              or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Loads the letter into an engine of its own on a window that is never shown, prints it onto
    /// the letter's paper, and takes both down again.
    /// </summary>
    private async Task<byte[]?> PrintOffScreenAsync(
        string html,
        LetterPageSize pageSize,
        CancellationToken cancellationToken)
    {
        var host = new Window
        {
            Width = 1,
            Height = 1,
            Left = -32000,
            Top = -32000,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Visibility = Visibility.Hidden,
        };

        CoreWebView2Controller? controller = null;
        try
        {
            // EnsureHandle gives the engine something to live on without the window ever appearing.
            var handle = new WindowInteropHelper(host).EnsureHandle();

            var folder = Path.Combine(_paths.StagingDir, "print");
            Directory.CreateDirectory(folder);
            var environment = await CoreWebView2Environment
                .CreateAsync(browserExecutableFolder: null, userDataFolder: folder)
                .ConfigureAwait(true);

            controller = await environment.CreateCoreWebView2ControllerAsync(handle).ConfigureAwait(true);
            controller.IsVisible = false;

            // A page still has to be laid out to be printed, so the engine is given the letter's
            // own proportions at roughly screen resolution rather than a zero-sized window.
            var (widthInches, heightInches) = LetterPageLayout.SizeInInches(pageSize);
            controller.Bounds = new System.Drawing.Rectangle(
                0,
                0,
                (int)Math.Round(widthInches * 96),
                (int)Math.Round(heightInches * 96));

            var view = controller.CoreWebView2;
            if (!await LoadAsync(view, html, cancellationToken).ConfigureAwait(true))
            {
                return null;
            }

            var settings = PageSetup(environment.CreatePrintSettings(), pageSize);
            using var buffer = new MemoryStream();
            var printed = await view.PrintToPdfStreamAsync(settings).ConfigureAwait(true);
            if (printed is null)
            {
                return null;
            }

            await printed.CopyToAsync(buffer, cancellationToken).ConfigureAwait(true);
            return buffer.ToArray();
        }
        finally
        {
            controller?.Close();
            host.Close();
        }
    }

    /// <summary>Navigates the private engine to the letter and waits for it to finish loading.</summary>
    private static async Task<bool> LoadAsync(
        CoreWebView2 view,
        string html,
        CancellationToken cancellationToken)
    {
        var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnNavigated(object? sender, CoreWebView2NavigationCompletedEventArgs args) =>
            loaded.TrySetResult(args.IsSuccess);

        view.NavigationCompleted += OnNavigated;
        try
        {
            using var patience = new CancellationTokenSource(Patience);
            using var both = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, patience.Token);
            using var registration = both.Token.Register(() => loaded.TrySetResult(false));

            view.NavigateToString(html);
            return await loaded.Task.ConfigureAwait(true);
        }
        finally
        {
            view.NavigationCompleted -= OnNavigated;
        }
    }

    /// <summary>
    /// Puts the letter's paper into the print settings: the exact page, no margins of the
    /// printer's own — the template's own margins are already part of the rendering — and the
    /// letterhead's background printed rather than dropped.
    /// </summary>
    /// <param name="settings">The settings the engine handed out.</param>
    /// <param name="pageSize">The paper the letter was written for.</param>
    internal static CoreWebView2PrintSettings PageSetup(
        CoreWebView2PrintSettings settings,
        LetterPageSize pageSize)
    {
        var (width, height) = LetterPageLayout.SizeInInches(pageSize);
        settings.PageWidth = width;
        settings.PageHeight = height;
        settings.MarginTop = 0;
        settings.MarginBottom = 0;
        settings.MarginLeft = 0;
        settings.MarginRight = 0;
        settings.ShouldPrintBackgrounds = true;
        settings.ShouldPrintHeaderAndFooter = false;
        return settings;
    }
}
