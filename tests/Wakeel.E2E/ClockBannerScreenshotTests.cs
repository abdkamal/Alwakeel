using System.Globalization;
using Microsoft.Playwright;
using Wakeel.E2E.Support;

namespace Wakeel.E2E;

/// <summary>
/// W11's screenshot, over its own seeded installation: <see cref="WalkthroughDataFolder"/> is asked
/// to skew the clock (a ClockCheck row dated two days ahead), so the moment this session signs in
/// and the shell runs its startup check, the check finds the real clock behind what it last saw and
/// the banner rises exactly as it would for a machine whose clock was actually turned back — the
/// only way to photograph W11 without touching this machine's own clock (mirrors
/// tests/Wakeel.UI.Tests/Shell/HostDataFolderSeed.cs's own <c>WAKEEL_HOST_SKEW_CLOCK</c>). Kept out
/// of <see cref="DailyShellScreenshotTests"/> because that harness's installation must NOT carry the
/// banner — it would show up as noise on every one of its seven other screens.
/// </summary>
public sealed class ClockBannerScreenshotTests
{
    private const int CdpPort = 9333;
    private const string LoginPath = "/login";
    private const string HeaderTitleSelector = "h1.w-page-header-title";

    /// <summary>
    /// See <see cref="DailyShellScreenshotTests.Screen.MaxPercent"/> for the methodology; set from
    /// this package's own host run (light 43.06%, dark 49.74% — see
    /// docs/build/progress/b2-walkthrough.md), with headroom above the higher figure.
    /// </summary>
    private const double MaxPercent = 56.0;

    [Fact]
    public async Task W11_banner_matches_its_design_export_in_both_themes()
    {
        var dataFolder = Path.Combine(Path.GetTempPath(), "wakeel-e2e-w11", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dataFolder);
        await WalkthroughDataFolder.ActivateAsync(dataFolder, skewClock: true);

        // NOT `await using`: that would defer LaunchedWakeelApp's Dispose to the end of this method
        // — after the Directory.Delete below — leaving Wakeel.Desktop.exe (and its lock on the
        // WebView2 profile under dataFolder) still alive at the moment the delete runs. The app is
        // killed explicitly, in the finally below, BEFORE the folder is ever touched.
        var app = await LaunchedWakeelApp.StartAsync(
            remoteDebuggingPort: CdpPort,
            pollPort: CdpPort,
            windowSize: "1366x768",
            startUrl: null,
            readyTimeout: TimeSpan.FromSeconds(30),
            dataFolder: dataFolder);

        var playwright = await Playwright.CreateAsync();
        try
        {
            var browser = await playwright.Chromium.ConnectOverCDPAsync(app.CdpEndpoint);
            try
            {
                var page = await WaitForPageAsync(browser, LoginPath, TimeSpan.FromSeconds(15));
                var origin = new Uri(page.Url).GetLeftPart(UriPartial.Authority);

                await page.FillAsync("input[type=password]", WalkthroughDataFolder.Password);
                await page.ClickAsync(".w05-submit");
                await page.WaitForURLAsync($"{origin}/w08", new PageWaitForURLOptions { Timeout = 15000 });
                await page.WaitForSelectorAsync(HeaderTitleSelector, new PageWaitForSelectorOptions { Timeout = 15000 });

                // The clock guard's startup check runs on sign-in; the banner it raises sits above
                // .w-page-content on every screen (MainLayout.razor), W08's included.
                await page.WaitForSelectorAsync(".w11", new PageWaitForSelectorOptions { Timeout = 15000 });

                var artifactsDir = Path.Combine(RepoRoot.Path, "tests", "Wakeel.E2E", "artifacts");
                Directory.CreateDirectory(artifactsDir);

                var failures = new List<string>();
                foreach (var theme in new[] { "light", "dark" })
                {
                    await page.GotoAsync($"{origin}/gallery");
                    await page.WaitForSelectorAsync(HeaderTitleSelector, new PageWaitForSelectorOptions { Timeout = 15000 });
                    var tabLabel = theme == "light" ? Wakeel.Design.Text.Ar.Gallery.ThemeLight : Wakeel.Design.Text.Ar.Gallery.ThemeDark;
                    await page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = tabLabel }).ClickAsync();
                    await page.WaitForFunctionAsync($"() => document.documentElement.getAttribute('data-theme') === '{theme}'");

                    await page.GotoAsync($"{origin}/w08");
                    await page.WaitForSelectorAsync(HeaderTitleSelector, new PageWaitForSelectorOptions { Timeout = 15000 });
                    await page.WaitForFunctionAsync($"() => document.documentElement.getAttribute('data-theme') === '{theme}'");
                    await page.WaitForSelectorAsync(".w11", new PageWaitForSelectorOptions { Timeout = 15000 });
                    await page.WaitForTimeoutAsync(250);

                    var actualPath = Path.Combine(artifactsDir, $"W11-{theme}-actual.png");
                    var diffPath = Path.Combine(artifactsDir, $"W11-{theme}-diff.png");
                    var percentagePath = Path.Combine(artifactsDir, $"W11-{theme}-diff-percentage.txt");

                    await CaptureAsync(page, actualPath);

                    var expectedPath = Path.Combine(RepoRoot.Path, "design", "exports", theme, "W", "W11 — تنبيه الساعة.png");
                    Assert.True(File.Exists(expectedPath), $"Design export not found: {expectedPath}");

                    var percentage = ImageDiff.CompareAndWriteDiff(expectedPath, actualPath, diffPath);
                    File.WriteAllText(percentagePath, percentage.ToString("F4", CultureInfo.InvariantCulture));

                    if (percentage > MaxPercent)
                    {
                        failures.Add($"W11 ({theme}): {percentage:F2}% exceeds the {MaxPercent:F1}% bound.");
                    }
                }

                Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
            }
            finally
            {
                await browser.CloseAsync();
            }
        }
        finally
        {
            playwright.Dispose();
            await app.DisposeAsync();
        }

        await TryDeleteWithRetryAsync(dataFolder);
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

    private static async Task CaptureAsync(IPage page, string savePath)
    {
        var cdp = await page.Context.NewCDPSessionAsync(page);
        try
        {
            await cdp.SendAsync("Emulation.setDeviceMetricsOverride", new Dictionary<string, object>
            {
                ["width"] = 1366,
                ["height"] = 768,
                ["deviceScaleFactor"] = 1,
                ["mobile"] = false,
            });

            var capture = await cdp.SendAsync("Page.captureScreenshot", new Dictionary<string, object>
            {
                ["format"] = "png",
            });
            var base64Png = capture!.Value.GetProperty("data").GetString()!;
            await File.WriteAllBytesAsync(savePath, Convert.FromBase64String(base64Png));
        }
        finally
        {
            await cdp.SendAsync("Emulation.clearDeviceMetricsOverride");
            await cdp.DetachAsync();
        }
    }
}
