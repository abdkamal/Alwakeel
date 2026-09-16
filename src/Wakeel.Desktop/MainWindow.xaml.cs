using System.Globalization;
using System.Windows;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Wakeel.Desktop.Services;
using Wakeel.UI;
using Wakeel.UI.Services.Account;

namespace Wakeel.Desktop;

/// <summary>Main application window: a single BlazorWebView hosting the Wakeel.UI shell (Routes.razor → MainLayout).</summary>
public partial class MainWindow : Window
{
    private readonly IServiceProvider _services;

    /// <summary>Creates the window and wires the BlazorWebView to the DI container built in App.xaml.cs.</summary>
    /// <param name="services">The host's root service provider, passed to BlazorWebView so its components can resolve Wakeel.UI services.</param>
    public MainWindow(IServiceProvider services)
    {
        _services = services;
        InitializeComponent();

        // The inner WebView2 control does not exist yet at construction time (BlazorWebView creates
        // it lazily), so the user-data folder is supplied through the initializing event, which the
        // control raises just before it creates its CoreWebView2 environment.
        BlazorView.BlazorWebViewInitializing += (_, e) => e.UserDataFolder = WakeelPaths.WebView2UserDataFolder;
        BlazorView.BlazorWebViewInitialized += (_, e) => Engine = e.WebView.CoreWebView2;
        BlazorView.Services = services;

        // Set here rather than declaratively in XAML: the WPF markup compiler's first pass runs
        // before Razor's source generator has produced the Routes class.
        BlazorView.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(Routes) });

        var args = Environment.GetCommandLineArgs();
        ConfigureStartUrl(args);
        ConfigureWindowSize(args);

        DragOver += HandleDragOver;
        Drop += HandleDrop;
    }

    /// <summary>
    /// The browser engine behind the window, once it exists. The print service (the recovery sheet
    /// of W04 and W07) is the only thing that reaches for it.
    /// </summary>
    public CoreWebView2? Engine { get; private set; }

    /// <summary>
    /// Where a start with no <c>--start-url</c> lands: a machine with no installation has nothing
    /// but the first run to show, and one that carries an installation asks who is at the keyboard.
    /// The component gallery stays reachable at <c>/gallery</c> for whoever asks for it by name.
    /// </summary>
    private string DefaultStartPath =>
        _services.GetService<LoginService>()?.IsActivated == true ? "/login" : "/first-run";

    /// <summary>
    /// A setup file dragged out of a folder window and let go over الوكيل (W02). The page's own drop
    /// zone catches a drop that lands inside it; this catches the rest of the window, so letting the
    /// file go anywhere over الوكيل does what the person plainly meant.
    /// </summary>
    private void HandleDragOver(object sender, DragEventArgs e)
    {
        e.Effects = SetupFileOf(e.Data) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void HandleDrop(object sender, DragEventArgs e)
    {
        if (SetupFileOf(e.Data) is not { } path)
        {
            return;
        }

        e.Handled = true;
        _services.GetService<SetupFileDrop>()?.Offer(path);
    }

    /// <summary>The first setup file among what was dropped, or null when none of it is one.</summary>
    private static string? SetupFileOf(IDataObject? data)
    {
        if (data?.GetDataPresent(DataFormats.FileDrop) != true)
        {
            return null;
        }

        return data.GetData(DataFormats.FileDrop) is string[] paths
            ? Array.Find(
                paths,
                path => path.EndsWith(SetupInspectionService.Extension, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    /// <summary>
    /// The command-line flag <c>--start-url=/path</c> (used by the Wakeel.E2E Playwright/CDP tests,
    /// ARCHITECTURE.md §11, to land directly on the screen under test) sets the Blazor Hybrid initial
    /// in-app route via <see cref="BlazorWebView.StartPath"/>. Must be set before the control's
    /// CoreWebView2 environment is created (which happens asynchronously after this constructor
    /// returns), so setting it here is safe. An absent or malformed flag leaves the default ("/").
    /// </summary>
    private void ConfigureStartUrl(string[] args)
    {
        const string prefix = "--start-url=";
        foreach (var arg in args)
        {
            if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = arg[prefix.Length..];
            if (path.Length == 0
                || path[0] != '/'
                || (path.Length > 1 && (path[1] == '/' || path[1] == '\\'))
                || path.Contains('\\', StringComparison.Ordinal)
                || !Uri.TryCreate(path, UriKind.Relative, out _))
            {
                // Reject anything but a genuine relative in-app path: an absent leading '/', a
                // protocol-relative form ("//host/x", which BlazorWebView's WebViewManager resolves
                // as new Uri(appBaseUri, startPath) — escaping the app's origin to an attacker-chosen
                // host entirely, verified live during package review) or a backslash form that some
                // URI parsers treat the same way. Ignore rather than hand Blazor's router (or
                // WebView2) something it cannot safely resolve.
                continue;
            }

            BlazorView.StartPath = path;
            return;
        }

        // No route was asked for, so the installation on disk decides which screen opens.
        BlazorView.StartPath = DefaultStartPath;
    }

    /// <summary>
    /// The command-line flag <c>--window-size=WxH</c> (used by the Wakeel.E2E tests to get a fixed,
    /// reproducible viewport for acceptance screenshots) sets the BlazorWebView control's own pixel
    /// size — not the outer WPF window bounds, which also include the title bar and borders — and
    /// lets the window shrink-wrap to it, so the WebView2/CDP viewport Playwright sees is exactly
    /// WxH regardless of window chrome. An absent or malformed flag leaves the window's default size
    /// (set declaratively in MainWindow.xaml) untouched.
    /// </summary>
    private void ConfigureWindowSize(string[] args)
    {
        const string prefix = "--window-size=";
        foreach (var arg in args)
        {
            if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sizeText = arg[prefix.Length..];
            var parts = sizeText.Split('x', 'X');
            if (parts.Length != 2
                || !double.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
                || !double.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
                || width < 200 || height < 200 || width > 10000 || height > 10000)
            {
                // Not a valid "WxH" pair — ignore rather than size the window to garbage.
                continue;
            }

            BlazorView.Width = width;
            BlazorView.Height = height;
            SizeToContent = SizeToContent.WidthAndHeight;
            return;
        }
    }
}
