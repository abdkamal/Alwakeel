using System.IO;
using System.Windows;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Wakeel.Core.Services;
using Wakeel.Crypto;
using Wakeel.Design;
using Wakeel.Design.Services;
using Wakeel.Core.Services.Correspondence;
using Wakeel.Core.Services.Documents;
using Wakeel.Desktop.Services;
using Wakeel.Ocr;
using Wakeel.Reports.Letters;
using Wakeel.UI.Services;
using Wakeel.UI.Services.Account;
using Wakeel.UI.Services.Shell;

namespace Wakeel.Desktop;

/// <summary>
/// Interaction logic for App.xaml. Builds the generic host (DI + Blazor Hybrid services), wires
/// Serilog file logging, and shows <see cref="MainWindow"/> hosting the BlazorWebView shell.
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Read before anything opens a file: --data-folder moves the whole installation, the browser
        // profile and the log folder somewhere else, and the log folder is chosen two lines below.
        WakeelPaths.Configure(e.Args);

        ConfigureRemoteDebugging(e.Args);
        ConfigureLogging();
        Log.Information("Wakeel desktop host starting up");

        var installation = WakeelPaths.CreateInstallationPaths();
        MainWindow? window = null;

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddWpfBlazorWebView();
#if DEBUG
                services.AddBlazorWebViewDeveloperTools();
#endif
                // Registered before AddWakeelDesign() so its TryAdd leaves this durable, file-backed
                // store in place instead of the design system's default in-memory one.
                services.AddSingleton<IUiStateStore>(_ => new FileUiStateStore(WakeelPaths.UiStateFilePath));
                services.AddWakeelDesign();
                services.AddScoped<PageHeaderState>();

                // The health center's Windows probes, registered BEFORE AddWakeelCore so its
                // TryAdd leaves them in place instead of the "nothing is available" answers Core
                // falls back to: whether Word is installed, whether a scanner is attached, how
                // much room is left on the volume, and whether the display components are present.
                services.AddSingleton<IWordProbe, WindowsWordProbe>();
                services.AddSingleton<IScannerProbe, WiaScannerProbe>();
                services.AddSingleton<IDiskSpaceProbe, WindowsDiskSpaceProbe>();
                services.AddSingleton<IRuntimeProbe, WindowsRuntimeProbe>();

                services.AddWakeelCore(options => options.Paths = installation);

                // The two things only Windows can do, registered before AddWakeelAccount so its
                // TryAdd leaves them alone: sealing a key to this machine and this account, and
                // turning what is on screen into paper or a PDF.
                services.AddSingleton<IPlatformProtector, DpapiPlatformProtector>();
                services.AddSingleton<IImagePixels, WindowsImagePixels>();
                services.AddSingleton<IPrintService>(_ => new WebView2PrintService(
                    () => window?.Engine,
                    Current.Dispatcher));

                // The two things the daily shell needs from Windows: the date-and-time settings
                // page W11's «تصحيح الساعة» opens, and the save dialog W12's «تصدير تقرير الصحة»
                // writes through. Registered before AddWakeelAccount so its TryAdd leaves them in
                // place instead of the "this machine cannot" answers Wakeel.UI falls back to.
                services.AddSingleton<ISystemSettingsLauncher, WindowsSystemSettings>();
                services.AddSingleton<IFileSaveService>(_ => new WindowsFileSaveService(Current));

                // B3-1b — the official letter (AGREEMENT items 10 and 57, ARCHITECTURE §9).
                // Filling a template and reading its marks need nothing from Windows; opening a
                // letter in Word and turning one into a PDF do, and are wired here. Word not being
                // installed is a state, not a failure: the composer, the internal editor and the
                // HTML rendering all work without it.
                services.AddSingleton<ILetterTemplateInspector, LetterTemplateInspector>();
                services.AddSingleton<ILetterComposer, LetterComposer>();
                services.AddSingleton<IBuiltInLetterTemplate, BuiltInLetterTemplate>();
                services.AddSingleton<ITemplateService, TemplateService>();
                services.AddSingleton(_ => new WordAutomation(installation));
                services.AddSingleton<IWordAutomation>(sp => sp.GetRequiredService<WordAutomation>());
                services.AddSingleton<ILetterPdfWriter>(sp => new LetterPdfWriter(
                    sp.GetRequiredService<WordAutomation>(),
                    installation,
                    Current.Dispatcher));

                // B3-2 — the vault, the documents, the scanner and the reader. The scanner and the
                // page binder are registered BEFORE AddWakeelOcr and are what Core's optional
                // dependencies resolve to; the vault's key provider bridges Core to the account
                // session, which is the only thing that holds the key while it is unlocked.
                services.AddSingleton<IScanner, WiaScanner>();
                services.AddScoped<IVaultKeyProvider, SessionVaultKeyProvider>();
                services.AddWakeelOcr();

                // B3-1b's last two services, which were left unregistered because they need the
                // vault: the letter package's window onto it comes from AddWakeelCore, and the
                // builder that writes the referral print copy of AGREEMENT item 31 sits on top.
                services.AddScoped<IDerivedDocumentBuilder, DerivedDocumentBuilder>();

                services.AddWakeelAccount();
            })
            .Build();

        window = new MainWindow(_host.Services);
        MainWindow = window;
        window.Show();
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Wakeel desktop host shutting down");
        Log.CloseAndFlush();
        _host?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// The command-line flag <c>--remote-debugging-port=&lt;port&gt;</c> enables WebView2 remote
    /// debugging (used by the Wakeel.E2E Playwright/CDP tests, ARCHITECTURE.md §11) by forwarding it
    /// to the underlying Chromium process through the <c>WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS</c>
    /// environment variable, which must be set before the WebView2 environment is created.
    /// </summary>
    private static void ConfigureRemoteDebugging(string[] args)
    {
        // Clear unconditionally first: WebView2 honours this variable from the *inherited* process
        // environment too, not just what this method sets below, so a value left over in the parent
        // process's environment could enable remote debugging (or smuggle other Chromium switches,
        // e.g. --remote-allow-origins / --disable-web-security, which the flag path below
        // deliberately refuses to forward) without any command-line flag being passed to this
        // process at all. Only a validated --remote-debugging-port from our own argv may set it
        // again below.
        //
        // This is the right security default, but it is also a silent one: a support engineer who
        // deliberately sets WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS in the environment before launch —
        // --disable-gpu to work around a rendering fault on a customer machine, say — will find it
        // has no effect, with nothing in the log explaining why (this runs before ConfigureLogging,
        // so a warning here would currently have nowhere to go without reordering the two calls in
        // OnStartup). Kept as-is deliberately: refusing to forward an unvalidated, inherited Chromium
        // switch list outweighs that one debugging inconvenience.
        Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", null);

        const string prefix = "--remote-debugging-port=";
        foreach (var arg in args)
        {
            if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var port = arg[prefix.Length..];
            if (!ushort.TryParse(port, out var parsedPort) || parsedPort == 0)
            {
                // Not a valid port number — ignore rather than forward an attacker-controlled string
                // (it could otherwise smuggle arbitrary extra Chromium switches into the renderer).
                continue;
            }

            Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", $"--remote-debugging-port={parsedPort}");
            return;
        }
    }

    private static void ConfigureLogging()
    {
        var logFolder = WakeelPaths.ResolveLogFolder();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logFolder, "wakeel-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31)
            .CreateLogger();

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled exception");
        Current.DispatcherUnhandledException += (_, args) =>
            Log.Error(args.Exception, "Unhandled UI-thread exception");
    }
}
