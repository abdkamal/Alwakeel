using System.Globalization;
using System.Windows;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Wakeel.Admin.Services;
using Wakeel.Admin.UI;
using Wakeel.Admin.UI.Services;
using Wakeel.Admin.UI.Services.Account;

namespace Wakeel.Admin;

/// <summary>
/// The tool's window: one BlazorWebView holding the administration shell
/// (<c>Routes.razor</c> → <c>AdminLayout</c>).
/// </summary>
public partial class MainWindow : Window
{
    private readonly IServiceProvider _services;

    /// <summary>Creates the window and points the BlazorWebView at the container built in App.xaml.cs.</summary>
    /// <param name="services">The host's service provider, so the screens can resolve the tool's services.</param>
    public MainWindow(IServiceProvider services)
    {
        _services = services;
        InitializeComponent();

        // The inner engine control does not exist yet at construction time (BlazorWebView creates it
        // lazily), so the profile folder is supplied through the initializing event, which the
        // control raises just before it creates its environment.
        BlazorView.BlazorWebViewInitializing += (_, e) => e.UserDataFolder = AdminHostPaths.WebView2UserDataFolder;
        BlazorView.BlazorWebViewInitialized += (_, e) => Engine = e.WebView.CoreWebView2;
        BlazorView.Services = services;

        // Set here rather than declaratively in XAML: the WPF markup compiler's first pass runs
        // before Razor's source generator has produced the Routes class.
        BlazorView.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(Routes) });

        // The title bar's own close button bypasses everything the screens draw, so it asks the
        // screen first: A01's recovery sheet is the only copy of the organisation's recovery code,
        // and closing the window over it destroys it as surely as the footer's «الخروج» would.
        Closing += OnClosing;

        var args = Environment.GetCommandLineArgs();
        ConfigureStartUrl(args);
        ConfigureWindowSize(args);
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_services.GetService<IAdminWindow>() is WpfAdminWindow window && !window.MayClose())
        {
            e.Cancel = true;
        }
    }

    /// <summary>
    /// The browser engine behind the window, once it exists. Printing the recovery sheet of A01 is
    /// the only thing in this sub-package that reaches for it.
    /// </summary>
    public CoreWebView2? Engine { get; private set; }

    /// <summary>
    /// Where a start with no <c>--start-url</c> lands: a computer with no administrator account has
    /// nothing but the first run to offer, and one that already carries an organisation asks who is
    /// at the keyboard.
    /// </summary>
    private string DefaultStartPath =>
        _services.GetService<AdminAccountService>()?.IsCreated == true
            ? AdminRoutes.SignIn
            : AdminRoutes.FirstRun;

    /// <summary>
    /// The command-line flag <c>--start-url=/path</c> (used by the acceptance harness to land
    /// directly on the screen under test) sets the initial in-app route. It must be set before the
    /// control's engine environment is created, which happens after this constructor returns, so
    /// setting it here is safe. An absent or malformed flag leaves the default.
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
                // Reject anything but a genuine relative in-app path: a missing leading '/', a
                // protocol-relative form ("//host/x", which the WebView manager resolves against the
                // app's base address — leaving the app's own origin for a host somebody else chose)
                // or a backslash form that some parsers treat the same way.
                continue;
            }

            BlazorView.StartPath = path;
            return;
        }

        // No route was asked for, so what is on disk decides which screen opens.
        BlazorView.StartPath = DefaultStartPath;
    }

    /// <summary>
    /// The command-line flag <c>--window-size=WxH</c> (used to get a fixed, repeatable viewport for
    /// acceptance screenshots) sizes the BlazorWebView control itself — not the outer window, which
    /// also carries a title bar and borders — and lets the window shrink-wrap to it, so the viewport
    /// is exactly WxH whatever the window chrome. An absent or malformed flag leaves the size set in
    /// MainWindow.xaml alone.
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

            var parts = arg[prefix.Length..].Split('x', 'X');
            if (parts.Length != 2
                || !double.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
                || !double.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
                || width < 200 || height < 200 || width > 10000 || height > 10000)
            {
                // Not a valid "WxH" pair — ignore rather than size the window to nonsense.
                continue;
            }

            BlazorView.Width = width;
            BlazorView.Height = height;
            MinWidth = 0;
            MinHeight = 0;
            SizeToContent = SizeToContent.WidthAndHeight;
            return;
        }
    }
}
