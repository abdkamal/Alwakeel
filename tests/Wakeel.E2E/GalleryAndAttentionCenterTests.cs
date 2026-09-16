using System.Drawing;
using Microsoft.Playwright;
using Wakeel.Design.Text;
using Wakeel.E2E.Support;
using Xunit.Abstractions;

namespace Wakeel.E2E;

/// <summary>GalleryOpens, ThemeToggles and W08ScreenshotDiff (ARCHITECTURE.md §11 / this package's
/// spec) all drive the single WebView2 page <see cref="WakeelE2eFixture"/> starts up, so they share
/// one instance — each test still navigates to (and asserts) the route/theme it needs itself rather
/// than depending on another test having run first, since xunit does not guarantee method order.</summary>
public sealed class GalleryAndAttentionCenterTests : IClassFixture<WakeelE2eFixture>
{
    private const string HeaderTitleSelector = "h1.w-page-header-title";

    private readonly WakeelE2eFixture _fixture;
    private readonly ITestOutputHelper _output;

    public GalleryAndAttentionCenterTests(WakeelE2eFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task GalleryOpens()
    {
        var page = _fixture.Page;
        await page.GotoAsync($"{_fixture.Origin}/gallery");
        await page.WaitForSelectorAsync(HeaderTitleSelector);

        var dir = await page.EvalOnSelectorAsync<string>("html", "el => el.getAttribute('dir')");
        Assert.Equal("rtl", dir);

        var heading = await page.TextContentAsync(HeaderTitleSelector);
        Assert.Equal(Ar.Gallery.Title, heading);
    }

    [Fact]
    public async Task ThemeToggles()
    {
        var page = _fixture.Page;
        await page.GotoAsync($"{_fixture.Origin}/gallery");
        await page.WaitForSelectorAsync(HeaderTitleSelector);

        // No restore-to-"system" here: FileUiStateStore (src/Wakeel.Desktop/Services) persists this
        // choice under the developer's real %LocalAppData%\Wakeel\ui-state.json, and that file lies
        // outside this package's allowed edit paths, so there is no isolated per-test location to
        // point it at instead. WakeelE2eFixture captures whatever theme was in effect before any
        // test ran and restores exactly that once, in its DisposeAsync, covering every test in this
        // class (including W08ScreenshotDiff, which also changes theme) in one place.
        await page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = Ar.Gallery.ThemeDark }).ClickAsync();
        await page.WaitForFunctionAsync("() => document.documentElement.getAttribute('data-theme') === 'dark'");

        var themeAttribute = await page.EvalOnSelectorAsync<string>("html", "el => el.getAttribute('data-theme')");
        Assert.Equal("dark", themeAttribute);

        // tokens.css: :root[data-theme="dark"] { --w-bg: #121A1E; ... } — confirms the dark
        // palette actually applied, not just the attribute.
        var bg = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.documentElement).getPropertyValue('--w-bg').trim()");
        Assert.Equal("#121a1e", bg, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task W08ScreenshotDiff()
    {
        var page = _fixture.Page;

        // Force the light theme first: the design export compared against is the light-theme
        // screenshot, and the previous test (or a leftover ui-state.json from a prior run) may have
        // left the app in dark mode.
        await page.GotoAsync($"{_fixture.Origin}/gallery");
        await page.WaitForSelectorAsync(HeaderTitleSelector);
        await page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = Ar.Gallery.ThemeLight }).ClickAsync();
        await page.WaitForFunctionAsync("() => document.documentElement.getAttribute('data-theme') === 'light'");

        // W08 (مركز الانتباه) — the attention-center placeholder route this package's spec points at.
        await page.GotoAsync($"{_fixture.Origin}/w08");
        await page.WaitForSelectorAsync(HeaderTitleSelector);
        var heading = await page.TextContentAsync(HeaderTitleSelector);
        Assert.Equal(Ar.AttentionCenter.Title, heading);

        var artifactsDir = Path.Combine(RepoRoot.Path, "tests", "Wakeel.E2E", "artifacts");
        Directory.CreateDirectory(artifactsDir);
        var actualPath = Path.Combine(artifactsDir, "W08-actual.png");
        var diffPath = Path.Combine(artifactsDir, "W08-diff.png");
        var percentagePath = Path.Combine(artifactsDir, "W08-diff-percentage.txt");

        // --window-size=1366x768 (WakeelE2eFixture) sets the BlazorView control's size in WPF DIPs,
        // but WPF DIPs are scaled by the host display's raster scale (e.g. 125%), so the physical
        // pixels WebView2 actually renders are NOT 1366x768 on a scaled display — only on one running
        // at exactly 100%. Pinning Emulation.setDeviceMetricsOverride is not enough by itself: routing
        // the capture through Playwright's own Page.ScreenshotAsync still yields the host's native
        // physical size (verified on this 125%-scaled machine — Playwright's screenshot pipeline
        // resets emulation from its own internally tracked viewport state, which is unset for a
        // CDP-attached page, undoing the override). Bypassing that pipeline and calling
        // Page.captureScreenshot directly on our own CDP session, after the override, does honour it.
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
            await File.WriteAllBytesAsync(actualPath, Convert.FromBase64String(base64Png));
        }
        finally
        {
            await cdp.SendAsync("Emulation.clearDeviceMetricsOverride");
        }

        using (var actualImage = new Bitmap(actualPath))
        {
            Assert.Equal(1366, actualImage.Width);
            Assert.Equal(768, actualImage.Height);
        }

        var expectedPath = Path.Combine(RepoRoot.Path, "design", "exports", "light", "W", "W08 — مركز الانتباه.png");
        Assert.True(File.Exists(expectedPath), $"Design export not found: {expectedPath}");

        var percentage = ImageDiff.CompareAndWriteDiff(expectedPath, actualPath, diffPath);
        File.WriteAllText(percentagePath, percentage.ToString("F4"));

        // Recorded only — this package prototypes the harness; no pass/fail threshold is set yet
        // (later B1-B8 packages define acceptance thresholds per screen). The percentage itself
        // cannot be asserted against a bound here for the same reason, but the artefacts the spec
        // asks for (the diff image and the recorded percentage file) can and must exist.
        _output.WriteLine($"W08 screenshot diff vs design export: {percentage:F2}% of pixels differ (no threshold enforced).");
        Assert.True(File.Exists(diffPath));
        Assert.Equal(percentage.ToString("F4"), File.ReadAllText(percentagePath));
    }
}
