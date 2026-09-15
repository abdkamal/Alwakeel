using System.Windows;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Web.WebView2.Wpf;
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

        // Must be set before the control creates its CoreWebView2 environment (first navigation).
        BlazorView.WebView.CreationProperties = new CoreWebView2CreationProperties
        {
            UserDataFolder = WakeelPaths.WebView2UserDataFolder,
        };
        BlazorView.Services = services;

        // Set here rather than declaratively in XAML: the WPF markup compiler's first pass runs
        // before Razor's source generator has produced the Routes class.
        BlazorView.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(Routes) });
    }
}
