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
using Wakeel.Desktop.Services;
using Wakeel.UI.Services;
using Wakeel.UI.Services.Account;

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

                services.AddWakeelCore(options => options.Paths = installation);

                // The two things only Windows can do, registered before AddWakeelAccount so its
                // TryAdd leaves them alone: sealing a key to this machine and this account, and
                // turning what is on screen into paper or a PDF.
                services.AddSingleton<IPlatformProtector, DpapiPlatformProtector>();
                services.AddSingleton<IImagePixels, WindowsImagePixels>();
                services.AddSingleton<IPrintService>(_ => new WebView2PrintService(
                    () => window?.Engine,
                    Current.Dispatcher));

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
