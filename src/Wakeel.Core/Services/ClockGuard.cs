using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;

namespace Wakeel.Core.Services;

/// <summary>Why a clock check ran; recorded so the health center and the audit trail can tell them apart.</summary>
public enum ClockCheckTrigger
{
    /// <summary>The application just started (ARCHITECTURE.md §9).</summary>
    Startup,

    /// <summary>The ten-minute background re-check.</summary>
    Interval,

    /// <summary>A sync package was imported, bringing other devices' timestamps with it.</summary>
    Import,

    /// <summary>The user asked for a re-check (the banner's «تصحيح الساعة» flow, or W12's «فحص شامل الآن»).</summary>
    Manual,
}

/// <summary>The clock banner's state (W11).</summary>
/// <param name="Verdict">The last verdict from <see cref="IClockCheckService"/>.</param>
/// <param name="Visible">Whether the banner should be on screen right now.</param>
/// <param name="NumberingBlocked">Whether official numbering is refused (AGREEMENT item 20).</param>
/// <param name="TitleAr">Banner title; empty when there is nothing to show.</param>
/// <param name="BodyAr">Banner explanation; empty when there is nothing to show.</param>
/// <param name="NumberingNoticeAr">«لن تُصدر أرقام رسمية حتى التصحيح»; empty when numbering is not blocked.</param>
/// <param name="CheckedAt">When the state was last computed, UTC.</param>
public sealed record ClockBannerState(
    ClockVerdict Verdict,
    bool Visible,
    bool NumberingBlocked,
    string TitleAr,
    string BodyAr,
    string NumberingNoticeAr,
    DateTime CheckedAt)
{
    /// <summary>The state before any check has run: nothing shown, nothing blocked.</summary>
    public static ClockBannerState Unknown { get; } =
        new(ClockVerdict.Ok, false, false, string.Empty, string.Empty, string.Empty, DateTime.UnixEpoch);
}

/// <summary>
/// Runs the clock check at startup, every ten minutes and on every import; raises the W11 banner
/// and blocks official numbering until the clock is corrected (ARCHITECTURE.md §9, AGREEMENT
/// item 20).
/// </summary>
public interface IClockGuard
{
    /// <summary>The current banner state.</summary>
    ClockBannerState State { get; }

    /// <summary>Raised whenever <see cref="State"/> changes.</summary>
    /// <remarks>
    /// The handler may run on a background thread: the ten-minute re-check raises it from the
    /// timer's pass, which lands wherever the <see cref="BackgroundPassDispatcher"/> given to
    /// <see cref="StartAsync"/> puts it. A Blazor subscriber must marshal through
    /// <c>ComponentBase.InvokeAsync</c> before touching component state or calling
    /// <c>StateHasChanged</c>.
    /// </remarks>
    event EventHandler<ClockBannerState>? StateChanged;

    /// <summary>
    /// True while official numbering must be refused. Unaffected by
    /// <see cref="DismissForSession"/>: hiding the banner does not fix the clock.
    /// </summary>
    bool NumberingBlocked { get; }

    /// <summary>Runs a check and updates <see cref="State"/>.</summary>
    /// <param name="trigger">Why the check is running.</param>
    /// <param name="now">The instant to check; normally the device clock.</param>
    /// <param name="otherDeviceTimestamps">Timestamps seen in an imported package, for <see cref="ClockCheckTrigger.Import"/>.</param>
    Task<ClockBannerState> CheckAsync(
        ClockCheckTrigger trigger,
        DateTime now,
        IReadOnlyCollection<DateTime>? otherDeviceTimestamps = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// «تجاهل مؤقتًا» — hides the banner for this session only. The next check that still finds a
    /// bad clock re-shows it, and numbering stays blocked throughout.
    /// </summary>
    void DismissForSession();

    /// <summary>Starts the ten-minute re-check and runs the startup check immediately.</summary>
    /// <param name="now">The instant the startup check runs at.</param>
    /// <param name="dispatcher">
    /// Where every check this guard runs by itself is executed. The guard shares one database
    /// session with the screens, so the shell must pass its renderer's dispatcher
    /// (<c>ComponentBase.InvokeAsync</c>); <see cref="BackgroundPass.Inline"/> is only for a
    /// caller that owns the session alone. See <see cref="BackgroundPassDispatcher"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the startup check.</param>
    Task StartAsync(DateTime now, BackgroundPassDispatcher dispatcher, CancellationToken cancellationToken = default);

    /// <summary>Stops the ten-minute re-check.</summary>
    void Stop();

    /// <summary>
    /// Throws when numbering is blocked, so a caller about to issue an official number fails
    /// before it opens a transaction rather than deep inside one.
    /// </summary>
    void EnsureNumberingAllowed();
}

/// <inheritdoc cref="IClockGuard"/>
/// <remarks>
/// <para>
/// <b>Dismissal is per session and never unblocks numbering.</b> «تجاهل مؤقتًا» sets a flag held
/// only in memory, so the banner comes back the next time the application starts, and
/// <see cref="NumberingBlocked"/> is derived from the verdict alone — a user who hides the banner
/// still cannot issue an official number with a wrong clock (AGREEMENT item 20). A check that
/// comes back <see cref="ClockVerdict.Ok"/> clears the dismissal, so a later problem is shown
/// again rather than silently suppressed by a dismissal from hours earlier.
/// </para>
/// <para>
/// <b>Numbering is blocked on anything but Ok.</b> <see cref="ClockVerdict.Suspect"/> (this
/// device's clock disagrees with another device's) blocks too: an official number carries a date,
/// and a number issued from a clock we already suspect is exactly what AGREEMENT item 20 exists
/// to prevent. <see cref="OfficialNumberService"/> enforces the same rule independently, so a
/// caller that bypasses the guard is still refused.
/// </para>
/// <para>
/// <b>The ten-minute re-check never touches the database on its own thread.</b> The check writes
/// a row to <c>clock_checks</c> through the same session the screens are using, so the timer
/// hands its pass to the <see cref="BackgroundPassDispatcher"/> given to
/// <see cref="StartAsync(DateTime, BackgroundPassDispatcher, CancellationToken)"/> and the shell
/// runs it where it runs its own queries. A failed pass is swallowed and retried at the next
/// interval — a database locked by a running backup must not stop the guard for the session —
/// and <see cref="StateChanged"/> is raised from wherever that pass ran.
/// </para>
/// </remarks>
public sealed class ClockGuard(IClockCheckService clockCheck, TimeProvider timeProvider) : IClockGuard, IDisposable
{
    /// <summary>ARCHITECTURE.md §9: re-check every ten minutes.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(10);

    private readonly Lock _gate = new();
    private ClockBannerState _state = ClockBannerState.Unknown;
    private bool _dismissedForSession;
    private ITimer? _timer;
    private CancellationTokenSource? _cts;
    private int _running;

    public ClockBannerState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public bool NumberingBlocked => State.NumberingBlocked;

    public event EventHandler<ClockBannerState>? StateChanged;

    public async Task<ClockBannerState> CheckAsync(
        ClockCheckTrigger trigger,
        DateTime now,
        IReadOnlyCollection<DateTime>? otherDeviceTimestamps = null,
        CancellationToken cancellationToken = default)
    {
        var check = await clockCheck.CheckAsync(now, otherDeviceTimestamps, cancellationToken).ConfigureAwait(false);
        return Apply(check, trigger);
    }

    public async Task StartAsync(DateTime now, BackgroundPassDispatcher dispatcher, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        // Even the startup check goes through the dispatcher: by the time the shell starts the
        // guard it is already rendering, and the check reads and writes the same session.
        await dispatcher(() => CheckAsync(ClockCheckTrigger.Startup, now, cancellationToken: cancellationToken)).ConfigureAwait(false);

        lock (_gate)
        {
            // A second Start rebinds rather than silently keeping the first dispatcher: after a
            // lock/unlock the shell hands over a new renderer, and a guard still checking through
            // the old one would write through a session that is on its way out.
            StopCore();

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _timer = timeProvider.CreateTimer(
                async _ =>
                {
                    // A slow check must never be entered twice; the next interval retries.
                    if (Interlocked.Exchange(ref _running, 1) == 1)
                    {
                        return;
                    }

                    try
                    {
                        await dispatcher(() => CheckAsync(ClockCheckTrigger.Interval, timeProvider.GetUtcNow().UtcDateTime, cancellationToken: token)).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Stop() was called mid-check — sign-out or a lock is tearing the session
                        // down. The pass drops what it was doing instead of reading and writing a
                        // database session that is already closing.
                    }
                    catch (Exception)
                    {
                        // The clock guard must not tear down its own timer on a transient failure
                        // (a locked database during a backup, say); the next interval retries.
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _running, 0);
                    }
                },
                state: null,
                dueTime: CheckInterval,
                period: CheckInterval);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopCore();
        }
    }

    /// <summary>
    /// Tears down the current timer and cancels a check that is already running. The caller holds
    /// <see cref="_gate"/>.
    /// </summary>
    /// <remarks>
    /// Disposing the timer alone would only stop the NEXT check: a pass already inside the
    /// dispatcher would carry on reading and writing the very session the sign-out is closing.
    /// </remarks>
    private void StopCore()
    {
        _cts?.Cancel();
        _timer?.Dispose();
        _timer = null;
        _cts?.Dispose();
        _cts = null;
    }

    public void DismissForSession()
    {
        ClockBannerState? changed = null;
        lock (_gate)
        {
            if (_dismissedForSession || !_state.Visible)
            {
                return;
            }

            _dismissedForSession = true;
            _state = _state with { Visible = false };
            changed = _state;
        }

        StateChanged?.Invoke(this, changed);
    }

    public void EnsureNumberingAllowed()
    {
        if (NumberingBlocked)
        {
            // Core-to-caller contract, like OfficialNumberService's own refusals: the user sees
            // the Arabic banner (CoreAr.ClockBannerNumberingBlocked), never this message.
            throw new InvalidOperationException("official numbering refused: the clock check did not pass");
        }
    }

    public void Dispose() => Stop();

    private ClockBannerState Apply(ClockCheck check, ClockCheckTrigger trigger)
    {
        ClockBannerState next;
        lock (_gate)
        {
            if (check.Verdict == ClockVerdict.Ok)
            {
                // A clean clock clears any earlier dismissal, so a problem appearing later is
                // shown rather than suppressed by a dismissal from an unrelated earlier problem.
                _dismissedForSession = false;
                next = new ClockBannerState(
                    ClockVerdict.Ok,
                    Visible: false,
                    NumberingBlocked: false,
                    TitleAr: string.Empty,
                    BodyAr: string.Empty,
                    NumberingNoticeAr: string.Empty,
                    CheckedAt: check.At);
            }
            else
            {
                // An import that finds a problem is worth re-showing even after a dismissal: it is
                // new evidence from another device, not the same complaint the user waved away.
                if (trigger == ClockCheckTrigger.Import)
                {
                    _dismissedForSession = false;
                }

                next = new ClockBannerState(
                    check.Verdict,
                    Visible: !_dismissedForSession,
                    NumberingBlocked: true,
                    CoreAr.ClockBannerTitle,
                    check.Verdict == ClockVerdict.Suspect ? CoreAr.ClockBannerBodySuspect : CoreAr.ClockBannerBodyBad,
                    CoreAr.ClockBannerNumberingBlocked,
                    check.At);
            }

            // The timestamp changes on every single check, so comparing whole records would fire
            // StateChanged every ten minutes forever and re-render the shell for nothing. Only a
            // change in what the user can actually see or do counts as a change; the new
            // timestamp is still stored, so the health center reads the latest one.
            var sameToTheUser = next.Verdict == _state.Verdict
                && next.Visible == _state.Visible
                && next.NumberingBlocked == _state.NumberingBlocked
                && string.Equals(next.TitleAr, _state.TitleAr, StringComparison.Ordinal)
                && string.Equals(next.BodyAr, _state.BodyAr, StringComparison.Ordinal)
                && string.Equals(next.NumberingNoticeAr, _state.NumberingNoticeAr, StringComparison.Ordinal);

            _state = next;
            if (sameToTheUser)
            {
                return _state;
            }
        }

        StateChanged?.Invoke(this, next);
        return next;
    }
}
