using Microsoft.Playwright;

namespace Wakeel.E2E.Support;

/// <summary>
/// Seeds a temporary activated installation (<see cref="WalkthroughDataFolder"/>), launches
/// Wakeel.Desktop.exe on it (CDP port 9333, 1366x768) and signs in over CDP through the real W05
/// login screen — the shared setup <see cref="DailyShellScreenshotTests"/> drives every one of its
/// screens through. Disposal always kills the app and removes the seeded folder, even if
/// <see cref="InitializeAsync"/> itself failed partway through.
/// </summary>
public sealed class DailyShellE2eFixture : IAsyncLifetime
{
    private const int CdpPort = 9333;
    private const string LoginPath = "/login";
    private const string HeaderTitleSelector = "h1.w-page-header-title";

    private LaunchedWakeelApp? _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private string? _dataFolder;

    /// <summary>The one WebView2 page/target the app opens, signed in and sitting on W08.</summary>
    public IPage Page { get; private set; } = null!;

    /// <summary>The page's origin, so tests can build other in-app URLs.</summary>
    public string Origin { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _dataFolder = Path.Combine(Path.GetTempPath(), "wakeel-e2e-daily", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dataFolder);
        await WalkthroughDataFolder.ActivateAsync(_dataFolder);

        _app = await LaunchedWakeelApp.StartAsync(
            remoteDebuggingPort: CdpPort,
            pollPort: CdpPort,
            windowSize: "1366x768",
            startUrl: null,
            readyTimeout: TimeSpan.FromSeconds(30),
            dataFolder: _dataFolder);

        try
        {
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.ConnectOverCDPAsync(_app.CdpEndpoint);

            Page = await WaitForPageAsync(_browser, LoginPath, TimeSpan.FromSeconds(15));
            Origin = new Uri(Page.Url).GetLeftPart(UriPartial.Authority);

            await Page.FillAsync("input[type=password]", WalkthroughDataFolder.Password);
            await Page.ClickAsync(".w05-submit");

            // W05Login.SignInAsync navigates to /w08 on success.
            await Page.WaitForURLAsync($"{Origin}/w08", new PageWaitForURLOptions { Timeout = 15000 });
            await Page.WaitForSelectorAsync(HeaderTitleSelector, new PageWaitForSelectorOptions { Timeout = 15000 });
        }
        catch
        {
            // Playwright/CDP handshake or sign-in failed after the process was already up — still
            // kill it and remove the folder rather than leaking either behind.
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (_browser is not null)
            {
                await _browser.CloseAsync();
            }
        }
        finally
        {
            _playwright?.Dispose();
        }

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        if (_dataFolder is not null && Directory.Exists(_dataFolder))
        {
            await TryDeleteWithRetryAsync(_dataFolder);
        }
    }

    /// <summary>
    /// Deletes the seeded data folder, retrying briefly: even after <see cref="LaunchedWakeelApp"/>
    /// confirms Wakeel.Desktop.exe has exited, a Chromium helper process WebView2 spawns under it
    /// (a crash handler, in particular) can detach from the process tree on purpose so it survives
    /// its parent, and it can hold the profile folder open for a moment past that. Best-effort past
    /// this many attempts — the machine's temp folder is cleaned by the machine regardless.
    /// </summary>
    private static async Task TryDeleteWithRetryAsync(string dataFolder)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                Directory.Delete(dataFolder, recursive: true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == 5)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }
    }

    private static async Task<IPage> WaitForPageAsync(IBrowser browser, string expectedPath, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var observedUrls = Array.Empty<string>();

        while (DateTime.UtcNow < deadline)
        {
            var pages = browser.Contexts.SelectMany(c => c.Pages).ToList();
            observedUrls = pages.Select(p => p.Url).ToArray();

            var page = pages.FirstOrDefault(p =>
                Uri.TryCreate(p.Url, UriKind.Absolute, out var uri) && uri.AbsolutePath == expectedPath);
            if (page is not null)
            {
                return page;
            }

            await Task.Delay(200);
        }

        throw new InvalidOperationException(
            $"Connected to Wakeel.Desktop.exe over CDP, but no page/target at path '{expectedPath}' appeared in time. Observed URLs: [{string.Join(", ", observedUrls)}].");
    }
}
