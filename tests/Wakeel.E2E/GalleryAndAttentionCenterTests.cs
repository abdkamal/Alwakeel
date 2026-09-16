using System.Drawing;
using System.Globalization;
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
        // page.GotoAsync is a real browser navigation, not a client-side route change, so the whole
        // Blazor app reboots from a static document that carries no data-theme attribute yet: the
        // very first paint follows the OS's prefers-color-scheme until the persisted "light" choice
        // is re-applied. Re-checking the attribute here (found live, while diagnosing an intermittent
        // W08ScreenshotDiff failure — see the note further below) closes most, but not quite all, of
        // that race before the capture.
        await page.GotoAsync($"{_fixture.Origin}/w08");
        await page.WaitForSelectorAsync(HeaderTitleSelector);
        await page.WaitForFunctionAsync("() => document.documentElement.getAttribute('data-theme') === 'light'");

        // A short, fixed settle: WSidebar.razor's own background/hover-state repaint (an internal
        // Blazor render pass following the attribute above, not something this package's allowed
        // paths can reach) was found, live, to occasionally still be one frame behind even after the
        // attribute check resolves and two requestAnimationFrame round trips pass — giving it real
        // wall-clock time to finish is the simplest mitigation that does not hard-code an assumption
        // about which pixel or colour is involved.
        await page.WaitForTimeoutAsync(250);

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
            // The override is undone and the session is detached in the same breath: leaving a CDP
            // session attached for the rest of the fixture's lifetime is harmless for this one
            // screenshot (the app is killed at teardown and the assembly runs sequentially), but a
            // later package taking many more screenshots off this same pattern would otherwise
            // accumulate one leaked attachment per shot.
            await cdp.SendAsync("Emulation.clearDeviceMetricsOverride");
            await cdp.DetachAsync();
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
        // (later B1-B8 packages define acceptance thresholds per screen). The artefacts the spec
        // asks for (the diff image and the recorded percentage file) must exist.
        _output.WriteLine($"W08 screenshot diff vs design export: {percentage:F2}% of pixels differ (no threshold enforced).");
        Assert.True(File.Exists(diffPath));

        // What is worth pinning here — instead of Assert.Equal(percentage.ToString(...),
        // File.ReadAllText(percentagePath)), which this same test would write and then read straight
        // back, provable only against File.WriteAllText/ReadAllText themselves — is that the
        // measurement lands near a known baseline rather than drifting arbitrarily. Sub-pixel font
        // rendering differs slightly across machines and even across runs on the same machine (this
        // package's own verification runs measured 33.3045% and 33.3047%), so the tolerance below is
        // documented and wide enough to absorb that drift while still catching an actual regression:
        // the wrong design export, W08's layout changing outright, or the capture pipeline breaking.
        // Later B1-B8 packages that set a real pass/fail budget per screen should express it the same
        // way — an upper bound with tolerance, never equality against one recorded figure.
        //
        // 2026-09-16 (b2-carryover): independently of every fix this package made, this test was
        // found to measure ~33.97% instead of ~33.30% on a reproducible fraction of runs (system
        // idle, no concurrent build) — traced to the W08AttentionCenter route's sidebar
        // (WSidebar.razor) occasionally still painting a stale colour at the moment CDP's
        // Page.captureScreenshot reads the composited frame, even once the DOM/CSSOM already report
        // the correct light theme (getComputedStyle is synchronous with the CSSOM and so cannot
        // detect a compositor paint that has not caught up to it yet). That race lives in
        // WSidebar.razor / the app's theme-application startup path, both outside this package's
        // allowed paths — the mitigation available here is the settle wait above (data-theme
        // attribute confirmed, then a fixed real delay before the capture), which brought 12/12
        // repeated runs back to the ~33.30% baseline. See docs/build/progress/b2-carryover.md for the
        // full diagnosis; a package that touches WSidebar.razor should look at removing the wait.
        Assert.InRange(percentage, BaselineDiffPercentage - BaselineDiffTolerance, BaselineDiffPercentage + BaselineDiffTolerance);

        Assert.True(
            double.TryParse(File.ReadAllText(percentagePath), NumberStyles.Float, CultureInfo.InvariantCulture, out _),
            "The recorded percentage file must hold a parseable number for a later package to consume.");
    }

    /// <summary>The known-good W08 diff percentage this harness has repeatedly measured, in percent.</summary>
    private const double BaselineDiffPercentage = 33.30;

    /// <summary>
    /// How far the measured percentage may drift from <see cref="BaselineDiffPercentage"/> before the
    /// test treats it as a regression rather than ordinary sub-pixel font-rendering variance between
    /// machines and runs.
    /// </summary>
    private const double BaselineDiffTolerance = 0.5;
}
