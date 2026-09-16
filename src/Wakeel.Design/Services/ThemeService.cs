using Microsoft.JSInterop;

namespace Wakeel.Design.Services;

/// <summary>The three theme choices exposed to the user in settings.</summary>
public enum UiTheme
{
    /// <summary>Follow the operating system's light/dark preference.</summary>
    System,

    /// <summary>Always render the light palette.</summary>
    Light,

    /// <summary>Always render the dark palette.</summary>
    Dark,
}

/// <summary>
/// Applies the user's light/dark/system theme choice by setting <c>data-theme</c> on the document
/// root, and persists the choice through <see cref="IUiStateStore"/> so it survives a restart.
/// </summary>
public sealed class ThemeService
{
    private const string StoreKey = "theme";

    private readonly IUiStateStore _store;
    private readonly IJSRuntime _js;

    /// <summary>Raised after the active theme changes, so UI (e.g. a theme toggle) can re-render.</summary>
    public event Action? ThemeChanged;

    /// <summary>The user's current theme choice (may be <see cref="UiTheme.System"/>).</summary>
    public UiTheme Theme { get; private set; } = UiTheme.System;

    public ThemeService(IUiStateStore store, IJSRuntime js)
    {
        _store = store;
        _js = js;
    }

    /// <summary>Loads the persisted choice (if any) and applies it to the document.</summary>
    public async Task InitializeAsync()
    {
        var stored = _store.Get(StoreKey);
        Theme = stored switch
        {
            "light" => UiTheme.Light,
            "dark" => UiTheme.Dark,
            _ => UiTheme.System,
        };
        await ApplyAsync();
    }

    /// <summary>Changes the theme, persists it, applies it to the document, and notifies subscribers.</summary>
    public async Task SetThemeAsync(UiTheme theme)
    {
        Theme = theme;
        _store.Set(StoreKey, theme switch
        {
            UiTheme.Light => "light",
            UiTheme.Dark => "dark",
            _ => "system",
        });
        await ApplyAsync();
        ThemeChanged?.Invoke();
    }

    private async Task ApplyAsync()
    {
        var attribute = Theme switch
        {
            UiTheme.Light => "light",
            UiTheme.Dark => "dark",
            _ => null,
        };
        try
        {
            await _js.InvokeVoidAsync("wakeelUi.setTheme", attribute);
        }
        catch (JSException)
        {
            // No document available (e.g. pre-render or a unit test host without the interop shim).
        }
        catch (InvalidOperationException)
        {
            // JS interop not available yet in this render context.
        }
    }
}
