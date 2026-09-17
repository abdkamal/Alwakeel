using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;

namespace Wakeel.Core.Services.Correspondence;

/// <summary>One entry of the follow-up timeline (W23), ready to render.</summary>
/// <param name="Id">The follow-up row.</param>
/// <param name="CorrespondenceId">The correspondence it belongs to.</param>
/// <param name="Kind">Call, visit, reply, note, or a status change.</param>
/// <param name="KindAr">«اتصال» / «زيارة» / «رد» / «ملاحظة» / «تغيير الحالة».</param>
/// <param name="NoteAr">What was written.</param>
/// <param name="NextAt">The next date agreed, when one was.</param>
/// <param name="NextAr">That date as «خلال 3 أيام».</param>
/// <param name="ReminderAt">When the bell should ring, when it should.</param>
/// <param name="StatusFrom">The status before, for a status change.</param>
/// <param name="StatusTo">The status after, for a status change.</param>
/// <param name="StatusChangeAr">«من «جديد» إلى «بانتظار رد»», or <c>null</c> when nothing moved.</param>
/// <param name="At">When the entry was written.</param>
/// <param name="AtAr">That instant as «أمس 16:40».</param>
public sealed record FollowupView(
    Guid Id,
    Guid CorrespondenceId,
    FollowupKind Kind,
    string KindAr,
    string? NoteAr,
    DateTime? NextAt,
    string? NextAr,
    DateTime? ReminderAt,
    CorrespondenceStatus? StatusFrom,
    CorrespondenceStatus? StatusTo,
    string? StatusChangeAr,
    DateTime At,
    string AtAr);

/// <summary>The «بانتظار رد منذ N أيام» card of the correspondence screen and of the daily shell.</summary>
/// <param name="CorrespondenceId">The waiting item.</param>
/// <param name="Subject">Its subject.</param>
/// <param name="OfficialNumber">Its official number, when it has one.</param>
/// <param name="PartyNameAr">Who is being waited on.</param>
/// <param name="Days">Whole days since the item entered «بانتظار رد».</param>
/// <param name="TextAr">The finished sentence, e.g. «بانتظار رد منذ 5 أيام».</param>
/// <param name="Since">The instant the wait started.</param>
/// <param name="DueAt">The date a reply was expected, when one was set.</param>
/// <param name="IsOverdue">True when that expected date has passed.</param>
public sealed record AwaitingReplyCard(
    Guid CorrespondenceId,
    string Subject,
    string? OfficialNumber,
    string? PartyNameAr,
    int Days,
    string TextAr,
    DateTime Since,
    DateTime? DueAt,
    bool IsOverdue);

/// <summary>
/// The follow-up timeline of a correspondence item (B3-1 «FollowUpService»): calls, visits,
/// replies and notes, the next date, a reminder, the status change that goes with the entry, and
/// the data behind the «بانتظار رد منذ N أيام» card.
/// </summary>
public interface IFollowUpService
{
    /// <summary>
    /// Adds one entry. When <paramref name="statusTo"/> is given the item moves too — the move is
    /// checked against <see cref="CorrespondenceStateMachine"/>, recorded on the same entry
    /// (<c>status_from</c>/<c>status_to</c>) and written to the audit log, so one office action
    /// produces one timeline row rather than two. «ملغى», «مغلق» and «مؤرشف» are refused here:
    /// each of them stores a field of its own and belongs to its own command on
    /// <see cref="ICorrespondenceService"/>.
    /// </summary>
    Task<FollowupView> AddAsync(
        Guid correspondenceId,
        FollowupKind kind,
        string? noteAr,
        DateTime now,
        DateTime? nextAt = null,
        DateTime? reminderAt = null,
        CorrespondenceStatus? statusTo = null,
        CancellationToken cancellationToken = default);

    /// <summary>The timeline of one item, newest first.</summary>
    Task<IReadOnlyList<FollowupView>> ListAsync(Guid correspondenceId, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>The waiting card of one item, or <c>null</c> when it is not waiting for a reply.</summary>
    Task<AwaitingReplyCard?> GetAwaitingReplyCardAsync(Guid correspondenceId, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Every item waiting for a reply, longest wait first.</summary>
    Task<IReadOnlyList<AwaitingReplyCard>> ListAwaitingReplyAsync(DateTime now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Turns every follow-up reminder that has fallen due into a bell notification. Idempotent
    /// through the same <c>reminder:</c> key convention the rest of the reminders use, so running
    /// it twice over one due entry creates one notification.
    /// </summary>
    Task<ReminderRun> RunDueRemindersAsync(DateTime now, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IFollowUpService"/>
public sealed class FollowUpService(
    WakeelDb db,
    IAuditService audit,
    INotificationService notifications) : IFollowUpService
{
    /// <summary>The <c>kind</c> written on a follow-up reminder notification.</summary>
    public const string ReminderKind = "correspondence_followup";

    /// <summary>The slot prefix of a follow-up reminder's idempotency key.</summary>
    public const string ReminderKeyKind = "followup";

    /// <summary>
    /// How far back a reminder is still raised after its instant passed — the machine may have
    /// been asleep. Deliberately the same window <see cref="ReminderScheduler"/> uses, so a
    /// follow-up reminder behaves exactly like a meeting or a task reminder.
    /// </summary>
    public static TimeSpan Lookback => ReminderScheduler.Lookback;

    public async Task<FollowupView> AddAsync(
        Guid correspondenceId,
        FollowupKind kind,
        string? noteAr,
        DateTime now,
        DateTime? nextAt = null,
        DateTime? reminderAt = null,
        CorrespondenceStatus? statusTo = null,
        CancellationToken cancellationToken = default)
    {
        var row = await db.Correspondence.FirstOrDefaultAsync(c => c.Id == correspondenceId, cancellationToken).ConfigureAwait(false)
            ?? throw new CorrespondenceRefusedException(CoreAr.CorrRefusedNotFound);

        var from = row.Status;
        if (statusTo is { } target)
        {
            // «ملغى», «مغلق» and «مؤرشف» each store a field of their own — the cancellation
            // reason (AGREEMENT item 19), the closing note, the archiving instant — and only
            // ICorrespondenceService writes them. Letting the timeline set those states here
            // would produce a cancelled numbered letter with no reason on it, so the three are
            // refused and the caller is sent to the command that asks for what they need.
            if (target is CorrespondenceStatus.Cancelled
                or CorrespondenceStatus.Closed
                or CorrespondenceStatus.Archived)
            {
                throw new CorrespondenceRefusedException(CoreAr.CorrRefusedFollowupStatus);
            }

            // A draft is moved out of «مسودة» only by registration or approval, which are the
            // operations that issue its number; a timeline entry must never promote it silently.
            if (from == CorrespondenceStatus.Draft)
            {
                throw new CorrespondenceRefusedException(CoreAr.CorrRefusedDraftNeedsNumbering);
            }

            CorrespondenceStateMachine.EnsureTransition(from, target);
            row.Status = target;
        }

        var followup = new Followup
        {
            CorrespondenceId = correspondenceId,
            Kind = kind,
            Note = string.IsNullOrWhiteSpace(noteAr) ? null : noteAr.Trim(),
            NextAt = nextAt,
            ReminderAt = reminderAt ?? nextAt,
            StatusFrom = statusTo is null ? null : from,
            StatusTo = statusTo,
        };
        db.Followups.Add(followup);

        // The next agreed date is what the attention center and the badges read as the item's due
        // date; keeping them in two places would let the worklist disagree with the timeline.
        if (nextAt is not null)
        {
            row.DueAt = nextAt;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (statusTo is { } moved)
        {
            var actor = await db.Installation.AsNoTracking().Select(i => i.EmployeeName)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
            await audit.LogAsync(
                actor,
                CorrespondenceService.AuditActionTransition,
                CoreAr.CorrAuditTransition(CorrespondenceAr.Status(from), CorrespondenceAr.Status(moved)),
                CorrespondenceService.AuditEntityType,
                correspondenceId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return ToView(followup, now);
    }

    public async Task<IReadOnlyList<FollowupView>> ListAsync(Guid correspondenceId, DateTime now, CancellationToken cancellationToken = default)
    {
        var rows = await db.Followups.AsNoTracking()
            .Where(f => f.CorrespondenceId == correspondenceId)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(f => ToView(f, now))];
    }

    public async Task<AwaitingReplyCard?> GetAwaitingReplyCardAsync(Guid correspondenceId, DateTime now, CancellationToken cancellationToken = default)
    {
        var row = await db.Correspondence.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == correspondenceId && c.Status == CorrespondenceStatus.AwaitingReply, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var since = await WaitingSinceAsync(row.Id, row.UpdatedAt, cancellationToken).ConfigureAwait(false);
        return Card(row.Id, row.Subject, row.OfficialNumber, row.PartyNameSnapshot, row.DueAt, since, now);
    }

    public async Task<IReadOnlyList<AwaitingReplyCard>> ListAwaitingReplyAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var rows = await db.Correspondence.AsNoTracking()
            .Where(c => c.Status == CorrespondenceStatus.AwaitingReply)
            .Select(c => new { c.Id, c.Subject, c.OfficialNumber, c.PartyNameSnapshot, c.DueAt, c.UpdatedAt })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return [];
        }

        // One read for the whole set: the instant each item started waiting is the newest
        // follow-up that moved it into «بانتظار رد». Doing this per card would be one query per
        // row on the daily shell.
        var ids = rows.Select(r => r.Id).ToList();
        var starts = await db.Followups.AsNoTracking()
            .Where(f => ids.Contains(f.CorrespondenceId) && f.StatusTo == CorrespondenceStatus.AwaitingReply)
            .GroupBy(f => f.CorrespondenceId)
            .Select(g => new { CorrespondenceId = g.Key, At = g.Max(f => f.CreatedAt) })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var cards = rows.Select(r =>
        {
            var start = starts.FirstOrDefault(s => s.CorrespondenceId == r.Id)?.At ?? r.UpdatedAt;
            return Card(r.Id, r.Subject, r.OfficialNumber, r.PartyNameSnapshot, r.DueAt, start, now);
        });

        return [.. cards.OrderByDescending(c => c.Days)];
    }

    public async Task<ReminderRun> RunDueRemindersAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var utcNow = ArabicRelativeTime.ToUtc(now);
        var from = utcNow - Lookback;

        var due = await db.Followups.AsNoTracking()
            .Where(f => f.ReminderAt != null && f.ReminderAt >= from && f.ReminderAt <= utcNow)
            .Select(f => new { f.Id, f.CorrespondenceId, f.ReminderAt })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (due.Count == 0)
        {
            return ReminderRun.None;
        }

        // Only items still worth chasing, and only the ones a reminder is actually due for: a
        // reminder written before the file was closed must not ring after it was, and this pass
        // runs every minute forever, so it must never read the whole open work of the office.
        var dueIds = due.Select(d => d.CorrespondenceId).Distinct().ToList();
        var openIds = await db.Correspondence.AsNoTracking()
            .Where(c => dueIds.Contains(c.Id)
                && (c.Status == CorrespondenceStatus.New
                    || c.Status == CorrespondenceStatus.InProgress
                    || c.Status == CorrespondenceStatus.AwaitingReply))
            .Select(c => new { c.Id, c.Subject })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var subjects = openIds.ToDictionary(c => c.Id, c => c.Subject);

        // The keys already used, in one read rather than one AnyAsync per due entry. The window
        // is the same lookback the pass itself works in, so the set stays small whatever the age
        // of the installation.
        var keyFloor = from - Lookback;
        var usedKeys = await db.Notifications.AsNoTracking()
            .Where(n => n.Source != null && n.Source.StartsWith(ReminderScheduler.KeyPrefix) && n.CreatedAt >= keyFloor)
            .Select(n => n.Source!)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var used = new HashSet<string>(usedKeys, StringComparer.Ordinal);

        var created = 0;
        var skipped = 0;
        foreach (var entry in due)
        {
            if (!subjects.TryGetValue(entry.CorrespondenceId, out var subject))
            {
                continue;
            }

            var key = ReminderScheduler.Key(ReminderKeyKind, entry.Id, entry.ReminderAt!.Value);
            if (!used.Add(key))
            {
                skipped++;
                continue;
            }

            await notifications.CreateAsync(
                ReminderKind,
                CoreAr.CorrFollowupReminderTitle,
                CoreAr.CorrFollowupReminderBody(subject),
                CorrespondenceService.AuditEntityType,
                entry.CorrespondenceId,
                entry.ReminderAt,
                key,
                utcNow,
                cancellationToken).ConfigureAwait(false);
            created++;
        }

        return new ReminderRun(created, skipped);
    }

    /// <summary>
    /// The instant an item started waiting: the newest follow-up that moved it into
    /// «بانتظار رد». Falls back to the row's own last-update stamp when the item reached that
    /// status some other way (an import, or a status set before this package existed).
    /// </summary>
    private async Task<DateTime> WaitingSinceAsync(Guid correspondenceId, DateTime fallback, CancellationToken cancellationToken)
    {
        var at = await db.Followups.AsNoTracking()
            .Where(f => f.CorrespondenceId == correspondenceId && f.StatusTo == CorrespondenceStatus.AwaitingReply)
            .Select(f => (DateTime?)f.CreatedAt)
            .MaxAsync(cancellationToken).ConfigureAwait(false);
        return at ?? fallback;
    }

    /// <summary>
    /// Whole days waited, floored: a letter sent this morning is «بانتظار رد منذ أقل من يوم», not
    /// «منذ يوم واحد». A negative span (a clock that moved backwards) counts as zero rather than
    /// producing a card that says the wait started in the future.
    /// </summary>
    private static AwaitingReplyCard Card(
        Guid id,
        string subject,
        string? officialNumber,
        string? partyName,
        DateTime? dueAt,
        DateTime since,
        DateTime now)
    {
        var days = (int)Math.Max(0, Math.Floor((ArabicRelativeTime.ToUtc(now) - ArabicRelativeTime.ToUtc(since)).TotalDays));
        return new AwaitingReplyCard(
            id,
            subject,
            officialNumber,
            partyName,
            days,
            CoreAr.CorrAwaitingReplySince(days),
            since,
            dueAt,
            dueAt is { } due && ArabicRelativeTime.ToUtc(due) < ArabicRelativeTime.ToUtc(now));
    }

    private static FollowupView ToView(Followup followup, DateTime now) => new(
        followup.Id,
        followup.CorrespondenceId,
        followup.Kind,
        CorrespondenceAr.Followup(followup.Kind),
        followup.Note,
        followup.NextAt,
        followup.NextAt is null ? null : ArabicRelativeTime.Describe(followup.NextAt.Value, now),
        followup.ReminderAt,
        followup.StatusFrom,
        followup.StatusTo,
        followup.StatusFrom is { } from && followup.StatusTo is { } to
            ? CoreAr.CorrAuditTransition(CorrespondenceAr.Status(from), CorrespondenceAr.Status(to))
            : null,
        followup.CreatedAt,
        ArabicRelativeTime.Describe(followup.CreatedAt, now));
}
