using System.Globalization;
using Microsoft.Playwright;
using Wakeel.E2E.Support;
using Xunit.Abstractions;

namespace Wakeel.E2E;

/// <summary>
/// B2-daily-shell.md's «القبول» screenshot harness: W08, W09, W10 (panel open), W12, W91, W92 and
/// W94, each in light and dark, over the seeded installation <see cref="DailyShellE2eFixture"/>
/// signs in to. Every capture is diffed against its design export (design/exports/{light,dark}/W)
/// with <see cref="ImageDiff"/>, the percentage is written to <c>tests/Wakeel.E2E/artifacts/</c>
/// and to the test output, and an upper bound is asserted per screen. W11 (the clock banner) is
/// its own file, <see cref="ClockBannerScreenshotTests"/>, because it needs a differently-seeded
/// installation (a skewed clock) rather than another route on this one.
/// </summary>
/// <remarks>
/// One shared launch and one shared theme loop for all seven screens (rather than one launch per
/// screen, as <see cref="AppLaunchTimeoutTests"/>-style isolation would call for): each launch pays
/// for Argon2 sign-in and WebView2 start-up once, and every screen here reads the SAME seeded data
/// through the SAME running session, so there is no isolation to buy by separating them — only
/// several minutes of repeated launches to pay for.
/// </remarks>
public sealed class DailyShellScreenshotTests : IClassFixture<DailyShellE2eFixture>
{
    private const string HeaderTitleSelector = "h1.w-page-header-title";

    private readonly DailyShellE2eFixture _fixture;
    private readonly ITestOutputHelper _output;

    public DailyShellScreenshotTests(DailyShellE2eFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// One screen of this package's screenshot table: its design-export file name and how to bring
    /// it on screen from a cold navigation (a routed page just loads; W10 and W94 are overlays that
    /// need a click first).
    /// </summary>
    private sealed record Screen(string Key, string ExportFileName, Func<IPage, string, Task> ShowAsync)
    {
        /// <summary>The upper bound this screen's measured diff percentage must stay under, in
        /// either theme. Recorded from this package's own host run (see
        /// docs/build/progress/b2-walkthrough.md) with headroom for ordinary sub-pixel
        /// font-rendering drift across machines and runs — the same methodology
        /// GalleryAndAttentionCenterTests.W08ScreenshotDiff already established for W08, extended
        /// here to every screen this package covers instead of leaving the bound unset.</summary>
        public double MaxPercent { get; init; }
    }

    private static Task NavigateAsync(IPage page, string origin, string route) =>
        GotoAndWaitAsync(page, $"{origin}{route}");

    private static async Task GotoAndWaitAsync(IPage page, string url)
    {
        // A real navigation (not a client-side route change) reboots the whole Blazor app from a
        // themeless static document, so the very first paint follows the OS's prefers-color-scheme
        // until the persisted theme choice re-applies — the same race
        // GalleryAndAttentionCenterTests.W08ScreenshotDiff documents and waits out.
        await page.GotoAsync(url);
        await page.WaitForSelectorAsync(HeaderTitleSelector, new PageWaitForSelectorOptions { Timeout = 15000 });
    }

    // Bounds recorded from this package's own host run (see docs/build/progress/b2-walkthrough.md
    // for the light/dark figures each was set against): the higher of the two themes' measured
    // percentages plus headroom for ordinary cross-run/cross-machine sub-pixel font-rendering
    // drift — GalleryAndAttentionCenterTests.W08ScreenshotDiff's own methodology, one bound per
    // screen here because seven screens' rows/cards/tables give seven different real margins
    // rather than one shared guess.
    private static readonly IReadOnlyList<Screen> Screens =
    [
        new Screen("W08", "W08 — مركز الانتباه.png", (page, origin) => NavigateAsync(page, origin, "/w08")) { MaxPercent = 40.0 },
        new Screen("W09", "W09 — مركز الانتباه — المتأخر.png", (page, origin) => NavigateAsync(page, origin, "/w09")) { MaxPercent = 58.0 },
        new Screen("W10", "W10 — لوحة الإشعارات.png", OpenNotificationPanelAsync) { MaxPercent = 45.0 },
        new Screen("W12", "W12 — مركز الصحة.png", (page, origin) => NavigateAsync(page, origin, "/w12")) { MaxPercent = 50.0 },
        new Screen("W91", "W91 — لوحة الحالات القياسية.png", (page, origin) => NavigateAsync(page, origin, "/w91")) { MaxPercent = 25.0 },
        new Screen("W92", "W92 — الحوارات القياسية.png", (page, origin) => NavigateAsync(page, origin, "/w92")) { MaxPercent = 40.0 },
        new Screen("W94", "W94 — الإدخال السريع.png", OpenQuickCaptureAsync) { MaxPercent = 20.0 },
    ];

    private static async Task OpenNotificationPanelAsync(IPage page, string origin)
    {
        await NavigateAsync(page, origin, "/w08");
        await page.ClickAsync(".w-topbar-bell button");
        await page.WaitForSelectorAsync(".w10", new PageWaitForSelectorOptions { Timeout = 5000 });
    }

    private static async Task OpenQuickCaptureAsync(IPage page, string origin)
    {
        await NavigateAsync(page, origin, "/w08");
        await page.ClickAsync(".w08-quick-entry");
        await page.WaitForSelectorAsync(".w94-dialog", new PageWaitForSelectorOptions { Timeout = 5000 });
    }

    [Fact]
    public async Task Light_theme_screens_match_their_design_exports()
    {
        await RunThemeAsync("light", Wakeel.Design.Text.Ar.Gallery.ThemeLight);
    }

    [Fact]
    public async Task Dark_theme_screens_match_their_design_exports()
    {
        await RunThemeAsync("dark", Wakeel.Design.Text.Ar.Gallery.ThemeDark);
    }

    private async Task RunThemeAsync(string theme, string themeTabLabel)
    {
        var page = _fixture.Page;

        await GotoAndWaitAsync(page, $"{_fixture.Origin}/gallery");
        await page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = themeTabLabel }).ClickAsync();
        await page.WaitForFunctionAsync($"() => document.documentElement.getAttribute('data-theme') === '{theme}'");

        var artifactsDir = Path.Combine(RepoRoot.Path, "tests", "Wakeel.E2E", "artifacts");
        Directory.CreateDirectory(artifactsDir);

        var failures = new List<string>();

        foreach (var screen in Screens)
        {
            await screen.ShowAsync(page, _fixture.Origin);
            await page.WaitForFunctionAsync($"() => document.documentElement.getAttribute('data-theme') === '{theme}'");

            // The same short, fixed settle W08ScreenshotDiff uses: a compositor repaint (the
            // sidebar's own background/hover state) that can lag one frame behind the DOM/CSSOM
            // reporting the right theme already, per that test's documented race.
            await page.WaitForTimeoutAsync(250);

            var actualPath = Path.Combine(artifactsDir, $"{screen.Key}-{theme}-actual.png");
            var diffPath = Path.Combine(artifactsDir, $"{screen.Key}-{theme}-diff.png");
            var percentagePath = Path.Combine(artifactsDir, $"{screen.Key}-{theme}-diff-percentage.txt");

            await CaptureAsync(page, actualPath);

            var expectedPath = Path.Combine(RepoRoot.Path, "design", "exports", theme, "W", screen.ExportFileName);
            Assert.True(File.Exists(expectedPath), $"Design export not found: {expectedPath}");

            var percentage = ImageDiff.CompareAndWriteDiff(expectedPath, actualPath, diffPath);
            File.WriteAllText(percentagePath, percentage.ToString("F4", CultureInfo.InvariantCulture));

            _output.WriteLine($"{screen.Key} ({theme}) diff vs design export: {percentage:F2}% (bound {screen.MaxPercent:F1}%).");

            if (percentage > screen.MaxPercent)
            {
                failures.Add($"{screen.Key} ({theme}): {percentage:F2}% exceeds the {screen.MaxPercent:F1}% bound.");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Captures the page at exactly 1366x768 physical pixels regardless of the host display's
    /// raster scale — see GalleryAndAttentionCenterTests.W08ScreenshotDiff's remarks on why
    /// Playwright's own <c>Page.ScreenshotAsync</c> cannot be used for this.
    /// </summary>
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
