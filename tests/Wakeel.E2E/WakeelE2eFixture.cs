using Microsoft.Playwright;
using Wakeel.Design.Text;
using Wakeel.E2E.Support;

namespace Wakeel.E2E;

/// <summary>
/// Launches Wakeel.Desktop.exe once (<c>--remote-debugging-port=9333 --window-size=1366x768
/// --start-url=/gallery</c>, ARCHITECTURE.md §11), connects Playwright to it over CDP, and exposes
/// the single WebView2 page so tests can drive it. Shared across every test in the class(es) it is
/// bound to via <c>IClassFixture</c> — the whole assembly runs sequentially (see AssemblyInfo.cs)
/// so nothing races to use it. Disposal always kills the app, even if a test threw or
/// <see cref="InitializeAsync"/> itself failed partway through.
/// </summary>
public sealed class WakeelE2eFixture : IAsyncLifetime
{
    private const int CdpPort = 9333;
    private const string StartUrl = "/gallery";
    private const string HeaderTitleSelector = "h1.w-page-header-title";

    private LaunchedWakeelApp? _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    /// <summary>The document's <c>data-theme</c> attribute as observed right after startup, before
    /// any test touched it — <see langword="null"/> means "system" (no attribute set). Captured so
    /// <see cref="DisposeAsync"/> can restore it: tests persist theme choices to the developer's
    /// real <c>%LocalAppData%\Wakeel\ui-state.json</c> (there is no isolated per-test location — see
    /// FileUiStateStore, src/Wakeel.Desktop/Services, which lies outside this package's allowed edit
    /// paths), so a test run must not permanently change a developer's desktop app theme.</summary>
    private string? _originalThemeAttribute;

    /// <summary>The one WebView2 page/target the app opens.</summary>
    public IPage Page { get; private set; } = null!;

    /// <summary>The page's origin (e.g. <c>https://0.0.0.1</c>) — BlazorWebView's virtual host, read
    /// back from the live page rather than hardcoded, so tests can build other in-app URLs
    /// (<c>$"{Origin}/w08"</c>) without guessing the scheme/host BlazorWebView happens to use.</summary>
    public string Origin { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _app = await LaunchedWakeelApp.StartAsync(
            remoteDebuggingPort: CdpPort,
            pollPort: CdpPort,
            windowSize: "1366x768",
            startUrl: StartUrl,
            readyTimeout: TimeSpan.FromSeconds(30));

        try
        {
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.ConnectOverCDPAsync(_app.CdpEndpoint);

            Page = await WaitForPageAsync(_browser, StartUrl, TimeSpan.FromSeconds(15));
            await Page.WaitForSelectorAsync(HeaderTitleSelector, new PageWaitForSelectorOptions { Timeout = 15000 });

            Origin = new Uri(Page.Url).GetLeftPart(UriPartial.Authority);
            _originalThemeAttribute = await Page.GetAttributeAsync("html", "data-theme");
        }
        catch
        {
            // Playwright/CDP handshake failed after the process was already up — still kill it
            // rather than leaking a Wakeel.Desktop.exe (and its WebView2 profile lock) behind.
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            await RestoreOriginalThemeAsync();
        }
        catch
        {
            // Best-effort — a developer's ui-state.json ending up on the wrong theme is regrettable
            // but must never prevent killing the app below.
        }

        try
        {
            try
            {
                if (_browser is not null)
                {
                    // For a CDP-attached browser this just disconnects Playwright; it does not
                    // terminate the real app — that happens explicitly below, always.
                    await _browser.CloseAsync();
                }
            }
            finally
            {
                _playwright?.Dispose();
            }
        }
        finally
        {
            if (_app is not null)
            {
                await _app.DisposeAsync();
            }
        }
    }

    /// <summary>Clicks the theme tab matching whatever <c>data-theme</c> was set when
    /// <see cref="InitializeAsync"/> first captured it, undoing whatever the test run's last theme
    /// change left behind — centralized here (once per test run) rather than in each test, so every
    /// test that touches the theme (ThemeToggles, W08ScreenshotDiff) is covered automatically.</summary>
    private async Task RestoreOriginalThemeAsync()
    {
        if (Page is null || Page.IsClosed)
        {
            return;
        }

        var targetLabel = _originalThemeAttribute switch
        {
            "dark" => Ar.Gallery.ThemeDark,
            "light" => Ar.Gallery.ThemeLight,
            _ => Ar.Gallery.ThemeSystem,
        };

        await Page.GotoAsync($"{Origin}{StartUrl}");
        await Page.WaitForSelectorAsync(HeaderTitleSelector, new PageWaitForSelectorOptions { Timeout = 15000 });
        await Page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = targetLabel }).ClickAsync();
    }

    private static async Task<IPage> WaitForPageAsync(IBrowser browser, string expectedPath, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var observedUrls = Array.Empty<string>();

        while (DateTime.UtcNow < deadline)
        {
            var pages = browser.Contexts.SelectMany(c => c.Pages).ToList();
            observedUrls = pages.Select(p => p.Url).ToArray();

            // Match on path rather than taking the first page found: Wakeel.Desktop.exe instances
            // share one fixed WebView2 user-data folder and therefore one CDP endpoint, so a stale
            // instance left behind by an aborted previous run could otherwise leave a second,
            // unrelated page/target visible here (see LaunchedWakeelApp's leftover-process guard,
            // which normally prevents this — this check is the second line of defense).
            var page = pages.FirstOrDefault(p =>
                Uri.TryCreate(p.Url, UriKind.Absolute, out var uri) && uri.AbsolutePath == expectedPath);
            if (page is not null)
            {
                return page;
            }

            await Task.Delay(200);
        }

        // A plain exception, not WakeelAppLaunchException: this failure is about which page/target
        // was found among an already-connected browser's pages, not about launching or killing the
        // process — the app was already up and CDP already answered by the time this runs. Whether
        // the process ends up killed afterwards is InitializeAsync's catch block's doing (it always
        // calls DisposeAsync on any failure here), not a fact this call site could report correctly.
        throw new InvalidOperationException(
            $"Connected to Wakeel.Desktop.exe over CDP, but no page/target at path '{expectedPath}' appeared in time. Observed URLs: [{string.Join(", ", observedUrls)}].");
    }
}
