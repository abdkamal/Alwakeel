using System.IO;
using System.Windows;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Wakeel.Design;
using Wakeel.Design.Services;
using Wakeel.Desktop.Services;
using Wakeel.UI.Services;

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

        ConfigureRemoteDebugging(e.Args);
        ConfigureLogging();
        Log.Information("Wakeel desktop host starting up");

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
            })
            .Build();

        var mainWindow = new MainWindow(_host.Services);
        MainWindow = mainWindow;
        mainWindow.Show();
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
