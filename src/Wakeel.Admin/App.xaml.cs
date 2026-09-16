using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Wakeel.Admin.Services;
using Wakeel.Admin.UI;
using Wakeel.Admin.UI.Services;
using Wakeel.Admin.UI.Services.Account;
using Wakeel.Crypto;
using Wakeel.Design.Services;

namespace Wakeel.Admin;

/// <summary>
/// Interaction logic for App.xaml. Builds the host (dependency injection plus the Blazor Hybrid
/// services), wires the file log, and shows <see cref="MainWindow"/> with the administration shell
/// inside it.
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Before anything opens a file: a run started with --data-folder keeps its whole layout
        // there instead of touching the organisation this computer carries.
        AdminHostPaths.Configure(e.Args);
        ConfigureRemoteDebugging(e.Args);
        ConfigureLogging();
        Log.Information("Wakeel admin host starting up");

        var paths = AdminHostPaths.CreatePaths();
        MainWindow? window = null;

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddWpfBlazorWebView();
#if DEBUG
                services.AddBlazorWebViewDeveloperTools();
#endif
                // Registered before AddWakeelAdmin() so its TryAdd leaves these — the real ones —
                // in place instead of the do-nothing defaults meant for a test host.
                services.AddSingleton<IUiStateStore>(_ => new AdminFileUiStateStore(AdminHostPaths.UiStateFilePath));
                services.AddSingleton<IPlatformProtector, AdminDpapiProtector>();
                services.AddSingleton<IAdminImagePixels, AdminWindowsImagePixels>();
                services.AddSingleton<IAdminWindow>(_ => new WpfAdminWindow(Dispatcher));
                services.AddSingleton<IAdminPrintService>(_ =>
                    new AdminWebView2PrintService(() => window?.Engine, Dispatcher));

                services.AddWakeelAdmin(paths);
            })
            .Build();

        window = new MainWindow(_host.Services);
        MainWindow = window;
        window.Show();
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Wakeel admin host shutting down");
        Log.CloseAndFlush();
        _host?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// The command-line flag <c>--remote-debugging-port=&lt;port&gt;</c> lets the acceptance harness
    /// drive and photograph the tool (ARCHITECTURE.md §11). It reaches the browser engine through
    /// the <c>WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS</c> environment variable, which has to be set
    /// before the engine's environment is created.
    /// </summary>
    private static void ConfigureRemoteDebugging(string[] args)
    {
        // Cleared unconditionally first: the engine honours this variable from the inherited process
        // environment too, so a value left over in a parent process could switch remote debugging on
        // (or smuggle other engine switches) without any flag on this process's own command line.
        // Only a validated port from our own arguments may set it again below.
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
                // Not a valid port number — ignore rather than forward a string somebody else chose,
                // which could otherwise carry arbitrary extra engine switches into the renderer.
                continue;
            }

            Environment.SetEnvironmentVariable(
                "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", $"--remote-debugging-port={parsedPort}");
            return;
        }
    }

    private static void ConfigureLogging()
    {
        var logFolder = AdminHostPaths.ResolveLogFolder();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logFolder, "wakeel-admin-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31)
            .CreateLogger();

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled exception");
        Current.DispatcherUnhandledException += (_, args) =>
            Log.Error(args.Exception, "Unhandled UI-thread exception");
    }
}
