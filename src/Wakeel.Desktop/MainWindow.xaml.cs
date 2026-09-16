using System.Globalization;
using System.Windows;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Wakeel.Desktop.Services;
using Wakeel.UI;

namespace Wakeel.Desktop;

/// <summary>Main application window: a single BlazorWebView hosting the Wakeel.UI shell (Routes.razor → MainLayout).</summary>
public partial class MainWindow : Window
{
    /// <summary>Creates the window and wires the BlazorWebView to the DI container built in App.xaml.cs.</summary>
    /// <param name="services">The host's root service provider, passed to BlazorWebView so its components can resolve Wakeel.UI services.</param>
    public MainWindow(IServiceProvider services)
    {
        InitializeComponent();

        // The inner WebView2 control does not exist yet at construction time (BlazorWebView creates
        // it lazily), so the user-data folder is supplied through the initializing event, which the
        // control raises just before it creates its CoreWebView2 environment.
        BlazorView.BlazorWebViewInitializing += (_, e) => e.UserDataFolder = WakeelPaths.WebView2UserDataFolder;
        BlazorView.Services = services;

        // Set here rather than declaratively in XAML: the WPF markup compiler's first pass runs
        // before Razor's source generator has produced the Routes class.
        BlazorView.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(Routes) });

        var args = Environment.GetCommandLineArgs();
        ConfigureStartUrl(args);
        ConfigureWindowSize(args);
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
