using Wakeel.E2E.Support;

namespace Wakeel.E2E;

/// <summary>Proves the fixture's launch/readiness logic (<see cref="LaunchedWakeelApp.StartAsync"/>,
/// used by <see cref="WakeelE2eFixture"/>) kills Wakeel.Desktop.exe rather than leaking it when the
/// CDP port never opens.</summary>
public sealed class AppLaunchTimeoutTests
{
    [Fact]
    public async Task StartAsync_KillsTheProcess_WhenCdpPortNeverOpens()
    {
        // Omitting --remote-debugging-port (remoteDebuggingPort: null) means the app never enables
        // WebView2 CDP at all, so polling any port for /json/version is guaranteed to time out —
        // deterministic, and doesn't depend on some other process happening to occupy the port.
        const int neverListensPort = 19821;

        var exception = await Assert.ThrowsAsync<WakeelAppLaunchException>(() =>
            LaunchedWakeelApp.StartAsync(
                remoteDebuggingPort: null,
                pollPort: neverListensPort,
                windowSize: null,
                startUrl: null,
                readyTimeout: TimeSpan.FromSeconds(5)));

        Assert.Contains("Timed out", exception.Message);
        Assert.True(exception.ProcessWasKilled, "The launcher must confirm the process exited after killing it.");
    }
}
