using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Wakeel.UI.Services;

namespace Wakeel.UI.Tests;

/// <summary>
/// Shared bUnit context that registers the DI services every Wakeel.UI component expects to find
/// (mirroring what Wakeel.Desktop's App.xaml.cs registers): an in-memory IUiStateStore, ThemeService,
/// IToastService, and PageHeaderState. IJSRuntime and NavigationManager are already provided by bUnit
/// itself (Loose-mode JSInterop returns defaults for calls this test suite does not explicitly set up).
/// </summary>
public abstract class WakeelTestContext : BunitContext
{
    protected WakeelTestContext()
    {
        // Loose mode lets JS interop calls (theme application, dialog focus trap, etc.) go through with
        // default return values instead of throwing, while still recording each invocation so a test can
        // assert against it (see Gallery_ThemeToggle_ChangesDataThemeViaJsInterop).
        JSInterop.Mode = Bunit.JSRuntimeMode.Loose;

        Services.AddSingleton<IUiStateStore, InMemoryUiStateStore>();
        Services.AddScoped<ThemeService>();
        Services.AddScoped<IToastService, ToastService>();
        Services.AddScoped<PageHeaderState>();
    }
}
