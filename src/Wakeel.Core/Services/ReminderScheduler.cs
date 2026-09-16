using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;

namespace Wakeel.Core.Services;

/// <summary>
/// The minute tick the reminder scheduler runs on, abstracted so tests drive it directly instead
/// of waiting on wall-clock time.
/// </summary>
public interface IMinuteTicker : IDisposable
{
    /// <summary>Starts calling <paramref name="onTick"/> once a minute with the current instant.</summary>
    /// <remarks>
    /// Calling this a second time REPLACES the callback: the ticker stops calling the previous
    /// one and starts calling <paramref name="onTick"/>. That is what makes a sign-out followed
    /// by a sign-in tick the new session's scheduler instead of the one whose database session
    /// has already closed.
    /// </remarks>
    void Start(Func<DateTime, CancellationToken, Task> onTick);

    /// <summary>Stops ticking. Safe to call when not started.</summary>
    void Stop();
}

/// <summary>A minute tick driven by a <see cref="TimeProvider"/> (the system clock in production).</summary>
/// <remarks>
/// Ticks never overlap: a tick that is still running when the next one is due is not re-entered,
/// and the skipped minute is simply picked up by the following tick — the scheduler is idempotent
/// and its windows are wide enough that a missed minute loses no event. Exceptions from a tick
/// are swallowed here rather than left to crash the timer's thread pool callback and silence the
/// scheduler for the rest of the session; the callback itself is responsible for reporting.
/// </remarks>
public sealed class TimeProviderMinuteTicker(TimeProvider timeProvider) : IMinuteTicker
{
    /// <summary>The tick interval; the scheduler's windows are sized against this.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly Lock _gate = new();
    private ITimer? _timer;
    private CancellationTokenSource? _cts;
    private int _running;

    public void Start(Func<DateTime, CancellationToken, Task> onTick)
    {
        ArgumentNullException.ThrowIfNull(onTick);
        lock (_gate)
        {
            // Rebind rather than no-op. Refusing the second Start would leave the ticker bound to
            // a callback that closes over a closed session: every tick would then throw into the
            // catch below and no reminder would ever be raised again.
            StopCore();

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _timer = timeProvider.CreateTimer(
                async _ =>
                {
                    // Interlocked, not the lock: a long tick must be skipped, never queued behind
                    // a lock the timer thread would then hold across an await.
                    if (Interlocked.Exchange(ref _running, 1) == 1)
                    {
                        return;
                    }

                    try
                    {
                        await onTick(timeProvider.GetUtcNow().UtcDateTime, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Stop() was called mid-tick; nothing to report.
                    }
                    catch (Exception)
                    {
                        // Never let a failed tick tear down the timer: the next minute retries.
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _running, 0);
                    }
                },
                state: null,
                dueTime: Interval,
                period: Interval);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopCore();
        }
    }

    public void Dispose() => Stop();

    /// <summary>Tears down the current timer. The caller holds <see cref="_gate"/>.</summary>
    private void StopCore()
    {
        _cts?.Cancel();
        _timer?.Dispose();
        _timer = null;
        _cts?.Dispose();
        _cts = null;
    }
}

/// <summary>What one scheduler pass produced.</summary>
/// <param name="Created">Notifications actually written by this pass.</param>
/// <param name="Skipped">Events that were due but already had a notification (the idempotency key matched).</param>
public sealed record ReminderRun(int Created, int Skipped)
{
    /// <summary>Nothing happened.</summary>
    public static ReminderRun None { get; } = new(0, 0);
}

/// <summary>
/// The once-a-minute background pass that turns due events into bell notifications: meetings and
/// appointments (AGREEMENT item 56), task and commitment due dates, the financial-cycle reminders
/// of AGREEMENT item 52, and the backup reminder.
/// </summary>
public interface IReminderScheduler
{
    /// <summary>Runs one pass for <paramref name="now"/>. Idempotent: running it again changes nothing.</summary>
    Task<ReminderRun> RunOnceAsync(DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Starts running a pass on every tick of <paramref name="ticker"/>.</summary>
    /// <param name="ticker">The minute tick to run on.</param>
    /// <param name="dispatcher">
    /// Where each pass is executed. A pass reads six tables and writes notifications through the
    /// same database session the screens are using, so the shell must pass its renderer's
    /// dispatcher (<c>ComponentBase.InvokeAsync</c>); <see cref="BackgroundPass.Inline"/> is only
    /// for a caller that owns the session alone. See <see cref="BackgroundPassDispatcher"/>.
    /// </param>
    void Start(IMinuteTicker ticker, BackgroundPassDispatcher dispatcher);

    /// <summary>Stops the ticker started by <see cref="Start"/>.</summary>
    void Stop();
}

/// <inheritdoc cref="IReminderScheduler"/>
/// <remarks>
/// <para>
/// <b>Idempotency.</b> Every event has a key — <c>reminder:&lt;kind&gt;:&lt;id&gt;:&lt;slot&gt;</c>,
/// where the slot is the exact instant (or the day) the reminder belongs to — stored in the
/// notification's <c>source</c> column. A pass loads the keys already used inside its own lookback
/// window once and tests against that set, so the number of database round-trips does not grow
/// with the number of due events. Re-running a pass, or running two passes a minute apart over the
/// same event, creates nothing the second time.
/// </para>
/// <para>
/// <b>Why a slot and not just the record id.</b> A meeting whose reminder time is edited moves to
/// a different slot and is legitimately reminded again; the financial-cycle reminder of AGREEMENT
/// item 52 repeats DAILY until the report is issued, and each day is its own slot. Keying on the
/// record id alone would silence both.
/// </para>
/// <para>
/// <b>Lookback.</b> A pass looks back <see cref="Lookback"/> so a machine that was asleep, or a
/// tick that was skipped because the previous one was still running, still raises the reminder
/// (late, but raised) rather than losing the event. The same window bounds how far back the
/// already-used keys are loaded from.
/// </para>
/// <para>
/// <b>A pass never runs on the timer's own thread.</b> One pass reads six tables and writes
/// notifications through the single database session the screens are also using, and a database
/// context serves one operation at a time. <see cref="Start"/> therefore takes a
/// <see cref="BackgroundPassDispatcher"/> and runs every pass through it, so the shell can put
/// the pass exactly where it puts its own queries. <see cref="RunOnceAsync"/> stays dispatcher-free
/// on purpose: a caller invoking it directly is already on its own context.
/// </para>
/// </remarks>
public sealed class ReminderScheduler(
    WakeelDb db,
    INotificationService notifications,
    ISettingsService settings,
    IBadgeService badges) : IReminderScheduler
{
    /// <summary>How far back a pass will still raise a reminder it missed.</summary>
    public static readonly TimeSpan Lookback = TimeSpan.FromHours(12);

    /// <summary>Days without a backup before the backup reminder fires (once a day thereafter).</summary>
    public const int BackupReminderDays = 7;

    /// <summary>
    /// How far back reminder keys are loaded for the duplicate check. Well beyond
    /// <see cref="Lookback"/> so a key can never be re-used for an event this pass could still
    /// raise, while keeping the loaded set small on a long-lived database.
    /// </summary>
    public static readonly TimeSpan KeyRetention = TimeSpan.FromDays(400);

    /// <summary>Key prefix every reminder notification's <c>source</c> carries.</summary>
    public const string KeyPrefix = "reminder:";

    private IMinuteTicker? _ticker;

    public void Start(IMinuteTicker ticker, BackgroundPassDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(ticker);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _ticker = ticker;
        ticker.Start((now, token) => dispatcher(() => RunOnceAsync(now, token)));
    }

    public void Stop()
    {
        _ticker?.Stop();
        _ticker = null;
    }

    public async Task<ReminderRun> RunOnceAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var utcNow = ArabicRelativeTime.ToUtc(now);
        var from = utcNow - Lookback;
        var zone = TimeZoneInfo.Local;

        // Only reminder keys are loaded, and only recent ones: every other notification source
        // (quick capture, sync, health …) can never collide with a reminder key, so pulling them
        // in would grow this per-minute read for the life of the installation with no benefit.
        // ix_notifications_source and ix_notifications_created_at make the narrowed read a scan
        // over the prefix range rather than over the table.
        var keyFloor = utcNow - KeyRetention;
        var usedKeys = await db.Notifications.AsNoTracking()
            .Where(n => n.Source != null && n.Source.StartsWith(KeyPrefix) && n.CreatedAt >= keyFloor)
            .Select(n => n.Source!)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var used = new HashSet<string>(usedKeys, StringComparer.Ordinal);

        var created = 0;
        var skipped = 0;

        async Task RaiseAsync(string key, string kind, string title, string? body, string? entityType, Guid? entityId, DateTime? dueAt)
        {
            if (!used.Add(key))
            {
                skipped++;
                return;
            }

            await notifications.CreateAsync(kind, title, body, entityType, entityId, dueAt, key, utcNow, cancellationToken).ConfigureAwait(false);
            created++;
        }

        var defaultMinutes = await settings.GetMeetingReminderMinutesAsync(cancellationToken).ConfigureAwait(false);

        // --- Meetings (AGREEMENT item 56) -------------------------------------------------------
        // A reminder is due when meeting start - reminder minutes falls inside the pass's window.
        // The per-meeting reminder_minutes wins over the settings default when it is set; a
        // reminder of 0 or less is "disabled" and raises nothing.
        var meetingHorizon = utcNow.AddMinutes(await MaxLeadMinutesAsync(from, defaultMinutes, cancellationToken).ConfigureAwait(false));
        var meetings = await db.Meetings.AsNoTracking()
            .Where(m => m.Status == MeetingStatus.Planned && m.StartsAt >= from && m.StartsAt <= meetingHorizon)
            .Select(m => new { m.Id, m.Title, m.StartsAt, m.Location, m.ReminderMinutes })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var meeting in meetings)
        {
            var minutes = meeting.ReminderMinutes ?? defaultMinutes;
            if (minutes <= 0)
            {
                continue;
            }

            var starts = ArabicRelativeTime.ToUtc(meeting.StartsAt);
            var fireAt = starts.AddMinutes(-minutes);
            if (fireAt > utcNow || fireAt < from || starts < utcNow)
            {
                continue;
            }

            await RaiseAsync(
                Key("meeting", meeting.Id, fireAt),
                NotificationKinds.Meeting,
                CoreAr.MeetingReminderTitle(meeting.Title),
                CoreAr.MeetingReminderBody(ArabicRelativeTime.TimeText(starts, zone), meeting.Location),
                "meetings",
                meeting.Id,
                starts).ConfigureAwait(false);
        }

        // --- Appointments (same rule; they are the manually-added half of the calendar) ---------
        var appointments = await db.Appointments.AsNoTracking()
            .Where(a => a.Status == AppointmentStatus.Planned && a.StartsAt >= from && a.StartsAt <= meetingHorizon)
            .Select(a => new { a.Id, a.Title, a.StartsAt, a.ReminderMinutes })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var appointment in appointments)
        {
            var minutes = appointment.ReminderMinutes ?? defaultMinutes;
            if (minutes <= 0)
            {
                continue;
            }

            var starts = ArabicRelativeTime.ToUtc(appointment.StartsAt);
            var fireAt = starts.AddMinutes(-minutes);
            if (fireAt > utcNow || fireAt < from || starts < utcNow)
            {
                continue;
            }

            await RaiseAsync(
                Key("appointment", appointment.Id, fireAt),
                NotificationKinds.Appointment,
                CoreAr.AppointmentReminderTitle(appointment.Title),
                CoreAr.AppointmentReminderBody(ArabicRelativeTime.TimeText(starts, zone)),
                "appointments",
                appointment.Id,
                starts).ConfigureAwait(false);
        }

        // --- Task due dates ---------------------------------------------------------------------
        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => t.DueAt != null && t.DueAt >= from && t.DueAt <= utcNow
                && t.Status != WorkTaskStatus.Done && t.Status != WorkTaskStatus.Transferred)
            .Select(t => new { t.Id, t.Title, t.DueAt })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var task in tasks)
        {
            var due = ArabicRelativeTime.ToUtc(task.DueAt!.Value);
            await RaiseAsync(
                Key("task", task.Id, due),
                NotificationKinds.TaskDue,
                CoreAr.TaskDueTitle(task.Title),
                CoreAr.TaskDueBody,
                "tasks",
                task.Id,
                due).ConfigureAwait(false);
        }

        // --- Commitment due dates ---------------------------------------------------------------
        var commitments = await db.Commitments.AsNoTracking()
            .Where(c => c.DueAt != null && c.DueAt >= from && c.DueAt <= utcNow && c.Status != CommitmentStatus.Paid)
            .Select(c => new { c.Id, c.Title, c.DueAt })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var commitment in commitments)
        {
            var due = ArabicRelativeTime.ToUtc(commitment.DueAt!.Value);
            await RaiseAsync(
                Key("commitment", commitment.Id, due),
                NotificationKinds.CommitmentDue,
                CoreAr.CommitmentDueTitle(commitment.Title),
                CoreAr.CommitmentDueBody,
                "commitments",
                commitment.Id,
                due).ConfigureAwait(false);
        }

        // --- Financial cycle (AGREEMENT item 52) --------------------------------------------------
        // N days before the end, then on the end day, then every day until the report is issued.
        // Each day is its own key, which is exactly what "يوميًا حتى الإصدار" requires.
        var reminderDays = await settings.GetReportReminderDaysAsync(cancellationToken).ConfigureAwait(false);
        var today = TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone).Date;
        var cycles = await db.FinancialCycles.AsNoTracking()
            .Where(c => c.IssuedAt == null && c.Status != FinancialCycleStatus.Issued)
            .Select(c => new { c.Id, c.NameAr, c.StartDate, c.EndDate })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var cycle in cycles)
        {
            var endDate = ArabicRelativeTime.ToUtc(cycle.EndDate).Date;
            var daysLeft = (int)(endDate - today).TotalDays;
            var range = $"{ArabicRelativeTime.Date(ArabicRelativeTime.ToUtc(cycle.StartDate).Date)} — {ArabicRelativeTime.Date(endDate)}";

            if (daysLeft > 0 && daysLeft <= Math.Max(reminderDays, 0))
            {
                await RaiseAsync(
                    KeyForDay("cycle-soon", cycle.Id, today),
                    NotificationKinds.FinancialCycle,
                    CoreAr.CycleEndingSoonTitle(cycle.NameAr),
                    CoreAr.CycleEndingSoonBody(daysLeft, range),
                    "financial_cycles",
                    cycle.Id,
                    endDate).ConfigureAwait(false);
            }
            else if (daysLeft <= 0)
            {
                await RaiseAsync(
                    KeyForDay("cycle-ended", cycle.Id, today),
                    NotificationKinds.FinancialCycle,
                    CoreAr.CycleEndedTitle(cycle.NameAr),
                    CoreAr.CycleEndedBody,
                    "financial_cycles",
                    cycle.Id,
                    endDate).ConfigureAwait(false);
            }
        }

        // --- Backup reminder ----------------------------------------------------------------------
        var lastBackup = await db.Backups.AsNoTracking()
            .Select(b => (DateTime?)b.At)
            .MaxAsync(cancellationToken).ConfigureAwait(false);
        // Counted in the user's own days, the same way W08's «مضى N أيام» counts them, so the
        // reminder and the card on screen can never disagree about how old the last backup is.
        var backupAgeDays = lastBackup is null
            ? int.MaxValue
            : AttentionService.BackupAgeInDays(lastBackup.Value, utcNow, zone);
        if (backupAgeDays >= BackupReminderDays)
        {
            await RaiseAsync(
                KeyForDay("backup", today),
                NotificationKinds.Backup,
                CoreAr.BackupReminderTitle,
                lastBackup is null ? CoreAr.BackupNeverReminderBody : CoreAr.BackupReminderBody(backupAgeDays),
                entityType: null,
                entityId: null,
                dueAt: null).ConfigureAwait(false);
        }

        if (created > 0)
        {
            badges.Invalidate();
        }

        await SweepAsync(keyFloor, cancellationToken).ConfigureAwait(false);
        return created == 0 && skipped == 0 ? ReminderRun.None : new ReminderRun(created, skipped);
    }

    /// <summary>
    /// Removes notifications the user has already dismissed and that are older than
    /// <see cref="KeyRetention"/>, so the table stops growing without bound.
    /// </summary>
    /// <remarks>
    /// A dismissed row is normally kept on purpose — its <c>source</c> is what makes a reminder
    /// idempotent, so deleting it would raise the same reminder a second time. Past the retention
    /// floor that risk is gone: <see cref="Lookback"/> is far shorter, so no pass can ever look
    /// at an event old enough for its key to be swept. Rows the user has not dismissed are never
    /// touched at any age — the bell is the user's, not the scheduler's, to empty.
    /// </remarks>
    private async Task SweepAsync(DateTime keyFloor, CancellationToken cancellationToken)
    {
        await db.Notifications
            .Where(n => n.DismissedAt != null && n.CreatedAt < keyFloor)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The widest lead time any reminder in the database can have, so the pass's horizon covers
    /// every meeting and appointment that could be due now.
    /// </summary>
    /// <remarks>
    /// Read from the data rather than capped at a constant. AGREEMENT item 56 lets a single
    /// meeting carry its own lead time with no upper bound, so a two-day heads-up on a meeting
    /// three days out would sit outside a one-day horizon at exactly the moment its reminder came
    /// due, and would then be silently lost. Two scalar queries over the planned rows the pass is
    /// about to read anyway cost far less than the reminder they protect.
    /// </remarks>
    private async Task<int> MaxLeadMinutesAsync(DateTime from, int defaultMinutes, CancellationToken cancellationToken)
    {
        var meetingLead = await db.Meetings.AsNoTracking()
            .Where(m => m.Status == MeetingStatus.Planned && m.StartsAt >= from && m.ReminderMinutes != null)
            .MaxAsync(m => m.ReminderMinutes, cancellationToken).ConfigureAwait(false) ?? 0;

        var appointmentLead = await db.Appointments.AsNoTracking()
            .Where(a => a.Status == AppointmentStatus.Planned && a.StartsAt >= from && a.ReminderMinutes != null)
            .MaxAsync(a => a.ReminderMinutes, cancellationToken).ConfigureAwait(false) ?? 0;

        return Math.Max(Math.Max(defaultMinutes, meetingLead), Math.Max(appointmentLead, 0));
    }

    /// <summary>The idempotency key for an event that fires at one exact instant.</summary>
    public static string Key(string kind, Guid entityId, DateTime slot) =>
        $"{KeyPrefix}{kind}:{entityId:D}:{ArabicRelativeTime.ToUtc(slot):yyyyMMddTHHmm}";

    /// <summary>
    /// The idempotency key for an event that repeats once a day until it is dealt with (the
    /// financial-cycle reminders of AGREEMENT item 52). Deliberately a differently-named method
    /// rather than another <see cref="Key(string, Guid, DateTime)"/> overload: two
    /// <see cref="DateTime"/> overloads would differ only by intent, and picking the wrong one
    /// would silently change how often a reminder repeats.
    /// </summary>
    public static string KeyForDay(string kind, Guid entityId, DateTime day) =>
        $"{KeyPrefix}{kind}:{entityId:D}:{day:yyyyMMdd}";

    /// <summary>The idempotency key for a once-a-day event with no record behind it (the backup reminder).</summary>
    public static string KeyForDay(string kind, DateTime day) =>
        $"{KeyPrefix}{kind}:{day:yyyyMMdd}";
}
