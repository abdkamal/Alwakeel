using System.Diagnostics;
using System.Globalization;

namespace Wakeel.E2E.Support;

/// <summary>Thrown when Wakeel.Desktop.exe could not be brought up (or exited early) before its
/// WebView2 CDP endpoint responded.</summary>
internal sealed class WakeelAppLaunchException : Exception
{
    public WakeelAppLaunchException(string message, bool processWasKilled) : base(message)
    {
        ProcessWasKilled = processWasKilled;
    }

    /// <summary>True once the launcher confirmed (via <see cref="Process.HasExited"/> after killing
    /// it) that no Wakeel.Desktop.exe process was left running.</summary>
    public bool ProcessWasKilled { get; }
}

/// <summary>A running Wakeel.Desktop.exe process, connected up to the point where its WebView2 CDP
/// endpoint is confirmed responsive. Disposing always kills the process — never leave one behind,
/// including when a test fails partway through using it.</summary>
internal sealed class LaunchedWakeelApp : IAsyncDisposable
{
    private readonly Process _process;

    private LaunchedWakeelApp(Process process, string cdpEndpoint)
    {
        _process = process;
        CdpEndpoint = cdpEndpoint;
    }

    /// <summary>e.g. <c>http://127.0.0.1:9333</c> — pass to <c>IBrowserType.ConnectOverCDPAsync</c>.</summary>
    public string CdpEndpoint { get; }

    /// <summary>
    /// Starts Wakeel.Desktop.exe and waits for <c>http://127.0.0.1:&lt;pollPort&gt;/json/version</c>
    /// to respond. <paramref name="remoteDebuggingPort"/> is normally the same as
    /// <paramref name="pollPort"/> (the port the app is told to open CDP on); passing
    /// <see langword="null"/> omits the <c>--remote-debugging-port</c> flag entirely, so CDP never
    /// comes up — used by the test that proves this method kills the process on timeout. On timeout,
    /// or if the process exits on its own first, the process is killed (whole tree) and a
    /// <see cref="WakeelAppLaunchException"/> is thrown carrying the app's log tail.
    /// </summary>
    public static async Task<LaunchedWakeelApp> StartAsync(
        int? remoteDebuggingPort,
        int pollPort,
        string? windowSize,
        string? startUrl,
        TimeSpan readyTimeout,
        string? dataFolder = null,
        CancellationToken cancellationToken = default)
    {
        var endpoint = $"http://127.0.0.1:{pollPort.ToString(CultureInfo.InvariantCulture)}";
        var versionUrl = $"{endpoint}/json/version";

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        // The whole point of this launcher is a deterministic, single-instance app: Wakeel.Desktop
        // shares one fixed WebView2 user-data folder (and therefore one CDP endpoint) across every
        // instance, so if something is already answering on pollPort — a stale process left behind
        // by an aborted previous run (Ctrl+C, a crashed test host) — starting a second instance would
        // leave both alive and later CDP target selection would become ambiguous/racy. Fail fast
        // instead, naming the port so the developer can find and kill the leftover process.
        if (await IsRespondingAsync(http, versionUrl, cancellationToken))
        {
            throw new WakeelAppLaunchException(
                $"Refusing to start Wakeel.Desktop.exe: {versionUrl} already answers, which means a leftover Wakeel.Desktop.exe instance (from a previous aborted run) is still listening on port {pollPort.ToString(CultureInfo.InvariantCulture)}. Kill it and retry.",
                processWasKilled: false);
        }

        var startInfo = new ProcessStartInfo(WakeelDesktopLocator.ExePath)
        {
            UseShellExecute = false,
            CreateNoWindow = false,
        };

        if (remoteDebuggingPort is { } port)
        {
            startInfo.ArgumentList.Add($"--remote-debugging-port={port.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!string.IsNullOrEmpty(windowSize))
        {
            startInfo.ArgumentList.Add($"--window-size={windowSize}");
        }

        if (!string.IsNullOrEmpty(startUrl))
        {
            startInfo.ArgumentList.Add($"--start-url={startUrl}");
        }

        if (!string.IsNullOrEmpty(dataFolder))
        {
            // Wakeel.Desktop.Services.WakeelPaths.Configure reads this and moves the whole
            // installation (and its WebView2 profile, so a data-folder run never shares a locked
            // profile with the plain-launch tests) there instead of the real machine's ProgramData.
            startInfo.ArgumentList.Add($"--data-folder={dataFolder}");
        }

        var process = Process.Start(startInfo)
            ?? throw new WakeelAppLaunchException($"Failed to start process: {WakeelDesktopLocator.ExePath}", processWasKilled: true);

        try
        {
            var deadline = DateTime.UtcNow + readyTimeout;

            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    throw new WakeelAppLaunchException(
                        $"Wakeel.Desktop.exe exited on its own (code {process.ExitCode}) before {versionUrl} responded.{Environment.NewLine}Log tail:{Environment.NewLine}{WakeelLogs.TailOrPlaceholder()}",
                        processWasKilled: true);
                }

                if (await IsReadyAsync(http, versionUrl, cancellationToken))
                {
                    return new LaunchedWakeelApp(process, endpoint);
                }

                await Task.Delay(250, cancellationToken);
            }

            var killed = await KillAsync(process);
            throw new WakeelAppLaunchException(
                $"Timed out after {readyTimeout} waiting for {versionUrl} to respond.{Environment.NewLine}Log tail:{Environment.NewLine}{WakeelLogs.TailOrPlaceholder()}",
                processWasKilled: killed);
        }
        catch
        {
            // Any exception here — including OperationCanceledException from a cancelled
            // cancellationToken, which would otherwise propagate straight out with the process still
            // running — must still kill the process before propagating. KillAsync is a no-op past
            // the point the process has already exited, so re-running it after the timeout branch
            // above (which already killed it) is harmless.
            await KillAsync(process);
            process.Dispose();
            throw;
        }
    }

    /// <summary>True if a GET to <paramref name="versionUrl"/> gets any HTTP response at all (status
    /// code is irrelevant — used only to detect a leftover process already occupying the port before
    /// this method starts a new one).</summary>
    private static async Task<bool> IsRespondingAsync(HttpClient http, string versionUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(versionUrl, cancellationToken);
            return true;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Per-request timeout, not the caller's token — nothing answered in time.
            return false;
        }
    }

    /// <summary>True once <paramref name="versionUrl"/> answers with a successful status code — the
    /// readiness bar for the polling loop, stricter than <see cref="IsRespondingAsync"/>.</summary>
    private static async Task<bool> IsReadyAsync(HttpClient http, string versionUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(versionUrl, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Per-request timeout, not the caller's token — keep polling.
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await KillAsync(_process);
        _process.Dispose();
    }

    private static async Task<bool> KillAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Gave it 10s to die after Kill(); fall through and report whatever HasExited says.
            }
        }
        catch (InvalidOperationException)
        {
            // No associated process (never started, or already reaped).
        }

        return process.HasExited;
    }
}
