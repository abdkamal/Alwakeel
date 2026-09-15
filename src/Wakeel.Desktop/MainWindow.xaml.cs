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
    }
}
