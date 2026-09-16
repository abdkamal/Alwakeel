using Bunit;
using Wakeel.Design.Services;

namespace Wakeel.UI.Tests;

/// <summary>ThemeService: persistence through IUiStateStore and the data-theme attribute it asks the JS interop layer to apply.</summary>
public class ThemeServiceTests : WakeelTestContext
{
    [Fact]
    public async Task InitializeAsync_DefaultsToSystem_WhenNothingPersisted()
    {
        var store = new InMemoryUiStateStore();
        var service = new ThemeService(store, JSInterop.JSRuntime);

        await service.InitializeAsync();

        Assert.Equal(UiTheme.System, service.Theme);
    }

    [Theory]
    [InlineData("light", UiTheme.Light)]
    [InlineData("dark", UiTheme.Dark)]
    public async Task InitializeAsync_RestoresPersistedChoice(string stored, UiTheme expected)
    {
        var store = new InMemoryUiStateStore();
        store.Set("theme", stored);
        var service = new ThemeService(store, JSInterop.JSRuntime);

        await service.InitializeAsync();

        Assert.Equal(expected, service.Theme);
    }

    [Fact]
    public async Task SetThemeAsync_PersistsChoice_SoItSurvivesANewInstance()
    {
        var store = new InMemoryUiStateStore();
        var first = new ThemeService(store, JSInterop.JSRuntime);
        await first.SetThemeAsync(UiTheme.Dark);

        var second = new ThemeService(store, JSInterop.JSRuntime);
        await second.InitializeAsync();

        Assert.Equal(UiTheme.Dark, second.Theme);
    }

    [Fact]
    public async Task SetThemeAsync_RaisesThemeChanged()
    {
        var store = new InMemoryUiStateStore();
        var service = new ThemeService(store, JSInterop.JSRuntime);
        var raised = false;
        service.ThemeChanged += () => raised = true;

        await service.SetThemeAsync(UiTheme.Dark);

        Assert.True(raised);
    }

    [Fact]
    public async Task SetThemeAsync_InvokesJsSetTheme_WithResolvedAttributeValue()
    {
        var store = new InMemoryUiStateStore();
        var service = new ThemeService(store, JSInterop.JSRuntime);

        await service.SetThemeAsync(UiTheme.Dark);

        var invocation = Assert.Single(JSInterop.Invocations, i => i.Identifier == "wakeelUi.setTheme");
        Assert.Equal("dark", invocation.Arguments[0]);
    }
}
