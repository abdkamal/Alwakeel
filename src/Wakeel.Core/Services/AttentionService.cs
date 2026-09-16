using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;

namespace Wakeel.Core.Services;

/// <summary>Which of the four attention-center indicators a record falls into (AGREEMENT item 23).</summary>
public enum AttentionBucket
{
    /// <summary>«متأخر» — the due date has passed by at least the configured number of days.</summary>
    Late,

    /// <summary>«قريب الاستحقاق» — due within the configured number of days (today included).</summary>
    Near,

    /// <summary>«راكد» — still open and untouched for at least the configured number of days.</summary>
    Stale,

    /// <summary>«بانتظار تأكيدي» — phone-captured expenses and other items awaiting the user's own confirmation (AGREEMENT item 50).</summary>
    PendingConfirmation,
}

/// <summary>Which table an attention row came from; the UI renders this as the row's type chip.</summary>
public enum AttentionEntityKind
{
    CorrespondenceIn,
    CorrespondenceOut,
    Task,
    Commitment,
    Case,
    Decision,
    PhoneExpense,
}

/// <summary>One row of the «يحتاج إجراءً اليوم» list / one row of a W09 tab.</summary>
/// <param name="Kind">Source table, rendered as the type chip.</param>
/// <param name="Id">Row id, so the UI can open the record.</param>
/// <param name="KindLabelAr">Arabic name of <paramref name="Kind"/>.</param>
/// <param name="TitleAr">Subject / title / text of the record.</param>
/// <param name="NumberAr">
/// Official number, case number or amount text; <c>null</c> when the record has none. It is a
/// Latin-digit run inside an Arabic row, so the screen must isolate it — <c>&lt;bdi&gt;</c> or
/// <c>unicode-bidi: isolate</c> — or the surrounding text reorders around it (AGREEMENT item 55).
/// </param>
/// <param name="PartyAr">Counterparty or owning side, when known.</param>
/// <param name="AssigneeAr">Who the record is on, when known.</param>
/// <param name="DueAt">The due instant that put the row in its bucket; <c>null</c> for a stale row.</param>
/// <param name="DaysLate">Whole days past due; 0 when not late.</param>
/// <param name="Bucket">The single bucket this row belongs to.</param>
/// <param name="NextStepAr">The record's own "next step" text, when it has one.</param>
/// <param name="UpdatedAt">Last change to the record, used for the staleness rule and as a tiebreaker.</param>
public sealed record AttentionItem(
    AttentionEntityKind Kind,
    Guid Id,
    string KindLabelAr,
    string TitleAr,
    string? NumberAr,
    string? PartyAr,
    string? AssigneeAr,
    DateTime? DueAt,
    int DaysLate,
    AttentionBucket Bucket,
    string? NextStepAr,
    DateTime UpdatedAt);

/// <summary>The four KPI numbers of W08, one per indicator.</summary>
public sealed record AttentionCounts(int Late, int Near, int Stale, int PendingConfirmation)
{
    /// <summary>Total records needing attention; the four buckets are mutually exclusive, so this is their sum.</summary>
    public int Total => Late + Near + Stale + PendingConfirmation;

    /// <summary>Empty counts, for an empty database or a failed read.</summary>
    public static AttentionCounts Empty { get; } = new(0, 0, 0, 0);
}

/// <summary>One of today's meetings, for the attention center's side column.</summary>
public sealed record TodayMeeting(Guid Id, string TitleAr, DateTime StartsAt, int DurationMin, string? LocationAr);

/// <summary>Backup state for the attention center's side column.</summary>
/// <param name="LastAt">When the last backup was taken; <c>null</c> when none was.</param>
/// <param name="DaysSince">Whole days since that backup; <c>null</c> when none was taken.</param>
/// <param name="Overdue">True when no backup exists or the last one is older than the reminder interval.</param>
/// <param name="MessageAr">Ready-to-render Arabic sentence.</param>
public sealed record BackupState(DateTime? LastAt, int? DaysSince, bool Overdue, string MessageAr);

/// <summary>Last successful sync, per AGREEMENT item 21 (shown in the sidebar footer and on W08).</summary>
/// <param name="LastAt">The most recent sync across every known device; <c>null</c> when none synced yet.</param>
/// <param name="MessageAr">Ready-to-render Arabic sentence.</param>
public sealed record SyncState(DateTime? LastAt, string MessageAr);

/// <summary>Everything W08 needs, read in one pass.</summary>
public sealed record AttentionSnapshot(
    AttentionCounts Counts,
    IReadOnlyList<AttentionItem> NeedsActionToday,
    IReadOnlyList<TodayMeeting> TodayMeetings,
    SyncState Sync,
    BackupState Backup,
    DateTime ComputedAt);

/// <summary>The settings thresholds the attention center runs on (AGREEMENT item 23).</summary>
public sealed record AttentionThresholds(int LateDays, int NearDays, int StaleDays);

/// <summary>
/// Computes the attention center (W08) and the late/near/stale/pending lists (W09) from the
/// settings thresholds of AGREEMENT item 23.
/// </summary>
public interface IAttentionService
{
    /// <summary>Reads the current thresholds from settings.</summary>
    Task<AttentionThresholds> GetThresholdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Every record in any of the four buckets, unordered and uncapped. One database pass;
    /// callers that need several views of the same instant (the badge service needs all four
    /// buckets plus the totals) should read once through this and slice the result in memory
    /// rather than call the bucket/count methods repeatedly.
    /// </summary>
    Task<IReadOnlyList<AttentionItem>> GetAllAsync(DateTime now, CancellationToken cancellationToken = default);

    /// <summary>The four KPI counts for <paramref name="now"/>.</summary>
    Task<AttentionCounts> GetCountsAsync(DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Today's meetings (device-local day), earliest first.</summary>
    Task<IReadOnlyList<TodayMeeting>> GetTodayMeetingsAsync(DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Orders a set of attention rows the way the «يحتاج إجراءً اليوم» list does.</summary>
    IReadOnlyList<AttentionItem> OrderByPriority(IEnumerable<AttentionItem> items);

    /// <summary>The rows of one bucket, ordered by priority.</summary>
    Task<IReadOnlyList<AttentionItem>> GetBucketAsync(AttentionBucket bucket, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Everything W08 needs: counts, the «يحتاج إجراءً اليوم» list, today's meetings, last sync and backup state.</summary>
    Task<AttentionSnapshot> GetSnapshotAsync(DateTime now, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IAttentionService"/>
/// <remarks>
/// <para>
/// <b>Buckets are mutually exclusive.</b> A record is classified once, in this order:
/// pending-confirmation (a different table entirely), then late, then near, then stale. Without
/// that, an overdue task untouched for a fortnight would be counted in both «متأخر» and «راكد»,
/// the four KPI numbers would not add up to the size of the list beneath them, and the same row
/// would appear on two W09 tabs.
/// </para>
/// <para>
/// <b>Performance (spec: under 200 ms on 10k rows).</b> Every query is <c>AsNoTracking</c>, is
/// filtered on an indexed column (<c>ix_*_due_at</c>, <c>ix_*_status</c>,
/// <c>ix_phone_expenses_status</c>, <c>ix_meetings_starts_at</c>) and projects only the columns
/// the list actually renders, so no entity graph is materialized. The date arithmetic is done in
/// C# on pre-computed UTC boundaries rather than in SQL, so SQLite can use those indexes on the
/// stored ISO-8601 text instead of having to compute a date per row.
/// </para>
/// <para>
/// <b>Soft deletes.</b> Every query goes through the default query filter, so soft-deleted
/// records (DATA-MODEL.md §0 — official records are never physically deleted) never reach the
/// attention center.
/// </para>
/// </remarks>
public sealed class AttentionService(WakeelDb db, ISettingsService settings) : IAttentionService
{
    /// <summary>
    /// How many rows the «يحتاج إجراءً اليوم» list returns. The list is a worklist, not an
    /// archive: beyond this the user works from W09's filtered, paged tabs (ARCHITECTURE.md §12
    /// pages lists at 20). The cap also keeps the snapshot's cost flat as the database grows.
    /// </summary>
    public const int NeedsActionLimit = 200;

    /// <summary>Days without a backup before <see cref="BackupState.Overdue"/> is raised.</summary>
    public const int BackupOverdueDays = 7;

    public async Task<AttentionThresholds> GetThresholdsAsync(CancellationToken cancellationToken = default)
    {
        var late = await settings.GetAttentionLateDaysAsync(cancellationToken).ConfigureAwait(false);
        var near = await settings.GetAttentionNearDaysAsync(cancellationToken).ConfigureAwait(false);
        var stale = await settings.GetAttentionStaleDaysAsync(cancellationToken).ConfigureAwait(false);
        return new AttentionThresholds(late, near, stale);
    }

    public async Task<IReadOnlyList<AttentionItem>> GetAllAsync(DateTime now, CancellationToken cancellationToken = default) =>
        await ReadAllAsync(now, cancellationToken).ConfigureAwait(false);

    public async Task<AttentionCounts> GetCountsAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var items = await ReadAllAsync(now, cancellationToken).ConfigureAwait(false);
        return Count(items);
    }

    public Task<IReadOnlyList<TodayMeeting>> GetTodayMeetingsAsync(DateTime now, CancellationToken cancellationToken = default) =>
        ReadTodayMeetingsAsync(ArabicRelativeTime.ToUtc(now), cancellationToken);

    public IReadOnlyList<AttentionItem> OrderByPriority(IEnumerable<AttentionItem> items) =>
        [.. items.OrderBy(SortKey).ThenBy(i => i.TitleAr, StringComparer.Ordinal)];

    public async Task<IReadOnlyList<AttentionItem>> GetBucketAsync(AttentionBucket bucket, DateTime now, CancellationToken cancellationToken = default)
    {
        var items = await ReadAllAsync(now, cancellationToken).ConfigureAwait(false);
        return OrderByPriority(items.Where(i => i.Bucket == bucket));
    }

    public async Task<AttentionSnapshot> GetSnapshotAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var utcNow = ArabicRelativeTime.ToUtc(now);
        var items = await ReadAllAsync(utcNow, cancellationToken).ConfigureAwait(false);
        var counts = Count(items);
        var needsAction = OrderByPriority(items).Take(NeedsActionLimit).ToList();

        var meetings = await ReadTodayMeetingsAsync(utcNow, cancellationToken).ConfigureAwait(false);
        var sync = await ReadSyncStateAsync(utcNow, cancellationToken).ConfigureAwait(false);
        var backup = await ReadBackupStateAsync(utcNow, cancellationToken).ConfigureAwait(false);

        return new AttentionSnapshot(counts, needsAction, meetings, sync, backup, utcNow);
    }

    /// <summary>
    /// Priority order of the «يحتاج إجراءً اليوم» list: overdue records first (most overdue
    /// first), then the phone expenses the user must confirm before they can be booked
    /// (AGREEMENT item 50), then what falls due next, then what has gone quiet longest.
    /// </summary>
    private static (int BucketRank, long Tiebreak) SortKey(AttentionItem item) => item.Bucket switch
    {
        AttentionBucket.Late => (0, -item.DaysLate),
        AttentionBucket.PendingConfirmation => (1, item.DueAt?.Ticks ?? item.UpdatedAt.Ticks),
        AttentionBucket.Near => (2, item.DueAt?.Ticks ?? long.MaxValue),
        _ => (3, item.UpdatedAt.Ticks),
    };

    private static AttentionCounts Count(IReadOnlyList<AttentionItem> items)
    {
        var late = 0;
        var near = 0;
        var stale = 0;
        var pending = 0;
        foreach (var item in items)
        {
            switch (item.Bucket)
            {
                case AttentionBucket.Late: late++; break;
                case AttentionBucket.Near: near++; break;
                case AttentionBucket.Stale: stale++; break;
                default: pending++; break;
            }
        }

        return new AttentionCounts(late, near, stale, pending);
    }

    /// <summary>
    /// Reads every record that is in any of the four buckets. One pass over five source tables
    /// plus the phone expenses, each query narrowed by an indexed predicate so the scan is over
    /// the candidates, not the table.
    /// </summary>
    private async Task<List<AttentionItem>> ReadAllAsync(DateTime now, CancellationToken cancellationToken)
    {
        var utcNow = ArabicRelativeTime.ToUtc(now);
        var thresholds = await GetThresholdsAsync(cancellationToken).ConfigureAwait(false);
        var window = AttentionWindow.For(utcNow, thresholds);

        var items = new List<AttentionItem>(256);

        // Commitments and cases carry a party id rather than a name snapshot, but «الجهة» is a
        // column of both the W08 worklist and the W09 tabs. The ids are collected here and
        // resolved in ONE extra query after the six reads, so the column is filled without
        // turning the pass into a per-row lookup.
        var partyIds = new HashSet<Guid>();
        var partyPlaceholders = new List<(int Index, Guid PartyId)>();

        // --- Correspondence: open items with a due date, or open items gone quiet. ------------
        var correspondence = await db.Correspondence.AsNoTracking()
            .Where(c => (c.Status == CorrespondenceStatus.New
                    || c.Status == CorrespondenceStatus.InProgress
                    || c.Status == CorrespondenceStatus.AwaitingReply)
                && ((c.DueAt != null && c.DueAt < window.NearCutoff) || c.UpdatedAt < window.StaleCutoff))
            .Select(c => new
            {
                c.Id,
                c.Direction,
                c.Subject,
                c.OfficialNumber,
                c.ExternalNumber,
                c.PartyNameSnapshot,
                c.NextStepAr,
                c.DueAt,
                c.UpdatedAt,
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var row in correspondence)
        {
            var bucket = window.Classify(row.DueAt, row.UpdatedAt);
            if (bucket is null)
            {
                continue;
            }

            items.Add(new AttentionItem(
                row.Direction == InOutDirection.In ? AttentionEntityKind.CorrespondenceIn : AttentionEntityKind.CorrespondenceOut,
                row.Id,
                row.Direction == InOutDirection.In ? CoreAr.KindCorrespondenceIn : CoreAr.KindCorrespondenceOut,
                row.Subject,
                row.OfficialNumber ?? row.ExternalNumber,
                row.PartyNameSnapshot,
                AssigneeAr: null,
                row.DueAt,
                window.DaysLate(row.DueAt),
                bucket.Value,
                row.NextStepAr,
                row.UpdatedAt));
        }

        // --- Tasks ---------------------------------------------------------------------------
        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => (t.Status == WorkTaskStatus.Open
                    || t.Status == WorkTaskStatus.InProgress
                    || t.Status == WorkTaskStatus.Postponed)
                && ((t.DueAt != null && t.DueAt < window.NearCutoff) || t.UpdatedAt < window.StaleCutoff))
            .Select(t => new { t.Id, t.Title, t.AssigneeName, t.DueAt, t.UpdatedAt })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var row in tasks)
        {
            var bucket = window.Classify(row.DueAt, row.UpdatedAt);
            if (bucket is null)
            {
                continue;
            }

            items.Add(new AttentionItem(
                AttentionEntityKind.Task,
                row.Id,
                CoreAr.KindTask,
                row.Title,
                NumberAr: null,
                PartyAr: null,
                row.AssigneeName,
                row.DueAt,
                window.DaysLate(row.DueAt),
                bucket.Value,
                NextStepAr: null,
                row.UpdatedAt));
        }

        // --- Commitments -----------------------------------------------------------------------
        var commitments = await db.Commitments.AsNoTracking()
            .Where(c => c.Status != CommitmentStatus.Paid
                && ((c.DueAt != null && c.DueAt < window.NearCutoff) || c.UpdatedAt < window.StaleCutoff))
            .Select(c => new { c.Id, c.Title, c.RequiredText, c.PartyId, c.DueAt, c.UpdatedAt })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var row in commitments)
        {
            var bucket = window.Classify(row.DueAt, row.UpdatedAt);
            if (bucket is null)
            {
                continue;
            }

            items.Add(new AttentionItem(
                AttentionEntityKind.Commitment,
                row.Id,
                CoreAr.KindCommitment,
                row.Title,
                NumberAr: null,
                PartyAr: null,
                AssigneeAr: null,
                row.DueAt,
                window.DaysLate(row.DueAt),
                bucket.Value,
                row.RequiredText,
                row.UpdatedAt));
            Remember(row.PartyId);
        }

        // --- Cases: the next hearing is the due date. ------------------------------------------
        var cases = await db.Cases.AsNoTracking()
            .Where(c => c.Status != CaseStatus.Closed
                && ((c.NextHearingAt != null && c.NextHearingAt < window.NearCutoff) || c.UpdatedAt < window.StaleCutoff))
            .Select(c => new { c.Id, c.Title, c.CaseNumber, c.ResponsibleName, c.PartyId, c.NextHearingAt, c.UpdatedAt, c.Stage })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var row in cases)
        {
            var bucket = window.Classify(row.NextHearingAt, row.UpdatedAt);
            if (bucket is null)
            {
                continue;
            }

            items.Add(new AttentionItem(
                AttentionEntityKind.Case,
                row.Id,
                CoreAr.KindCase,
                row.Title,
                row.CaseNumber,
                PartyAr: null,
                row.ResponsibleName,
                row.NextHearingAt,
                window.DaysLate(row.NextHearingAt),
                bucket.Value,
                row.Stage,
                row.UpdatedAt));
            Remember(row.PartyId);
        }

        // --- Decisions -------------------------------------------------------------------------
        var decisions = await db.Decisions.AsNoTracking()
            .Where(d => d.Status != DecisionStatus.Done
                && ((d.DueAt != null && d.DueAt < window.NearCutoff) || d.UpdatedAt < window.StaleCutoff))
            .Select(d => new { d.Id, d.Text, d.OwnerName, d.DueAt, d.UpdatedAt })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var row in decisions)
        {
            var bucket = window.Classify(row.DueAt, row.UpdatedAt);
            if (bucket is null)
            {
                continue;
            }

            items.Add(new AttentionItem(
                AttentionEntityKind.Decision,
                row.Id,
                CoreAr.KindDecision,
                row.Text,
                NumberAr: null,
                PartyAr: null,
                row.OwnerName,
                row.DueAt,
                window.DaysLate(row.DueAt),
                bucket.Value,
                NextStepAr: null,
                row.UpdatedAt));
        }

        // --- Phone expenses awaiting confirmation (AGREEMENT item 50). --------------------------
        var expenses = await db.PhoneExpenses.AsNoTracking()
            .Where(e => e.Status == PhoneExpenseStatus.Pending)
            .Select(e => new { e.Id, e.Purpose, e.Amount, e.At, e.UpdatedAt })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var row in expenses)
        {
            items.Add(new AttentionItem(
                AttentionEntityKind.PhoneExpense,
                row.Id,
                CoreAr.KindPhoneExpense,
                row.Purpose,
                Money.Shekels(row.Amount),
                PartyAr: null,
                AssigneeAr: null,
                row.At,
                DaysLate: 0,
                AttentionBucket.PendingConfirmation,
                NextStepAr: null,
                row.UpdatedAt));
        }

        await FillPartyNamesAsync(items, partyIds, partyPlaceholders, cancellationToken).ConfigureAwait(false);
        return items;

        // Records the party of the row just appended, so it can be named after the reads.
        void Remember(Guid? partyId)
        {
            if (partyId is not Guid id || id == Guid.Empty)
            {
                return;
            }

            partyIds.Add(id);
            partyPlaceholders.Add((items.Count - 1, id));
        }
    }

    /// <summary>
    /// Names the «الجهة» column of the rows that reference a party by id. One query for the whole
    /// pass; a row whose party has since been removed keeps a null party rather than a placeholder,
    /// because W08/W09 render the column empty in that case instead of inventing a name.
    /// </summary>
    private async Task FillPartyNamesAsync(
        List<AttentionItem> items,
        HashSet<Guid> partyIds,
        List<(int Index, Guid PartyId)> placeholders,
        CancellationToken cancellationToken)
    {
        if (partyIds.Count == 0)
        {
            return;
        }

        var ids = partyIds.ToList();
        var names = await db.Parties.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken).ConfigureAwait(false);

        foreach (var (index, partyId) in placeholders)
        {
            if (names.TryGetValue(partyId, out var name) && !string.IsNullOrWhiteSpace(name))
            {
                items[index] = items[index] with { PartyAr = name };
            }
        }
    }

    private async Task<IReadOnlyList<TodayMeeting>> ReadTodayMeetingsAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        // "Today" is the device's local day (AGREEMENT item 20 shows local time), converted to
        // the UTC range actually stored, so the indexed starts_at comparison stays a range scan.
        var zone = TimeZoneInfo.Local;
        var localToday = TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone).Date;
        var from = AttentionWindow.StartOfLocalDay(localToday, zone);
        var to = AttentionWindow.StartOfLocalDay(localToday.AddDays(1), zone);

        var rows = await db.Meetings.AsNoTracking()
            .Where(m => m.Status != MeetingStatus.Cancelled && m.StartsAt >= from && m.StartsAt < to)
            .OrderBy(m => m.StartsAt)
            .Select(m => new TodayMeeting(m.Id, m.Title, m.StartsAt, m.DurationMin, m.Location))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows;
    }

    private async Task<SyncState> ReadSyncStateAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        // Revoked devices are excluded, exactly as the health centre's sync card excludes them:
        // W08 must never report «آخر مزامنة» from hardware the office no longer trusts, and the
        // two surfaces disagreeing about the same fact is worse than either answer alone.
        var last = await db.Devices.AsNoTracking()
            .Where(d => d.RevokedAt == null && d.LastSyncAt != null)
            .Select(d => d.LastSyncAt)
            .MaxAsync(cancellationToken).ConfigureAwait(false);
        return last is null
            ? new SyncState(null, CoreAr.SyncNever)
            : new SyncState(last, CoreAr.SyncAt(ArabicRelativeTime.Describe(last.Value, utcNow)));
    }

    private async Task<BackupState> ReadBackupStateAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var last = await db.Backups.AsNoTracking()
            .Select(b => (DateTime?)b.At)
            .MaxAsync(cancellationToken).ConfigureAwait(false);
        if (last is null)
        {
            return new BackupState(null, null, Overdue: true, CoreAr.BackupNever);
        }

        var days = BackupAgeInDays(last.Value, utcNow);
        var overdue = days >= BackupOverdueDays;
        return new BackupState(last, days, overdue, CoreAr.BackupTakenAt(ArabicRelativeTime.Describe(last.Value, utcNow)));
    }

    /// <summary>
    /// Whole days between the last backup and today, counted in the user's own days.
    /// </summary>
    /// <remarks>
    /// The count is local for the same reason <see cref="AttentionWindow.DaysLate"/> is: measured
    /// against UTC dates, «مضى 7 أيام» and the overdue flag would change a few hours after
    /// «متأخر» does on the very same screen, because in a zone ahead of UTC the local day starts
    /// before the UTC day. Between local midnight and the offset the two would simply disagree.
    /// </remarks>
    internal static int BackupAgeInDays(DateTime lastBackupAt, DateTime utcNow, TimeZoneInfo? zone = null)
    {
        var timeZone = zone ?? TimeZoneInfo.Local;
        var todayLocal = TimeZoneInfo.ConvertTimeFromUtc(ArabicRelativeTime.ToUtc(utcNow), timeZone).Date;
        var backupLocal = TimeZoneInfo.ConvertTimeFromUtc(ArabicRelativeTime.ToUtc(lastBackupAt), timeZone).Date;
        var days = (int)(todayLocal - backupLocal).TotalDays;
        return days > 0 ? days : 0;
    }
}

/// <summary>
/// The pre-computed UTC boundaries one attention pass classifies against, so the arithmetic
/// happens once per pass instead of once per row (and never inside a SQL translation, where it
/// would defeat the due-date indexes).
/// </summary>
/// <param name="Now">The instant the pass runs at, UTC.</param>
/// <param name="LateCutoff">A due date strictly before this is «متأخر».</param>
/// <param name="NearCutoff">A due date before this (and not before <paramref name="LateCutoff"/>) is «قريب الاستحقاق».</param>
/// <param name="StaleCutoff">A record last updated before this is «راكد» when nothing else claims it.</param>
/// <param name="LocalToday">The user's current day in <paramref name="Zone"/>, date only.</param>
/// <param name="Zone">The time zone «اليوم» is measured in; the device's own zone in production.</param>
/// <remarks>
/// <b>The day is the user's day, not UTC's.</b> «اليوم» on W08 means the day the office is
/// working through, and the same day is what the meetings list, the notification groups and the
/// financial-cycle reminders use. Deriving the boundaries from <c>utcNow.Date</c> instead would
/// move every «متأخر»/«قريب الاستحقاق» boundary by a day between local midnight and the zone's
/// offset — the small hours in Palestine — so the KPI numbers would change on their own a few
/// hours later with no data having changed.
/// </remarks>
internal readonly record struct AttentionWindow(
    DateTime Now,
    DateTime LateCutoff,
    DateTime NearCutoff,
    DateTime StaleCutoff,
    DateTime LocalToday,
    TimeZoneInfo Zone)
{
    public static AttentionWindow For(DateTime utcNow, AttentionThresholds thresholds, TimeZoneInfo? zone = null)
    {
        // Thresholds are day counts and due dates are day-grained, so the boundaries are days:
        // with the default lateDays = 1, an item due yesterday is late and one due today is not
        // (it is still «قريب الاستحقاق», the user's working day is not over). A non-positive
        // lateDays makes anything already past its due instant late.
        var timeZone = zone ?? TimeZoneInfo.Local;
        var localToday = TimeZoneInfo.ConvertTimeFromUtc(utcNow, timeZone).Date;
        var lateDays = Math.Max(thresholds.LateDays, 0);
        var nearDays = Math.Max(thresholds.NearDays, 0);
        var staleDays = Math.Max(thresholds.StaleDays, 0);

        // Cutoffs are computed on local days and converted back, because the rows store UTC.
        var lateCutoff = lateDays == 0 ? utcNow : StartOfLocalDay(localToday.AddDays(-(lateDays - 1)), timeZone);
        var nearCutoff = StartOfLocalDay(localToday.AddDays(nearDays + 1), timeZone);
        var staleCutoff = utcNow.AddDays(-staleDays);
        return new AttentionWindow(utcNow, lateCutoff, nearCutoff, staleCutoff, localToday, timeZone);
    }

    /// <summary>The UTC instant a local calendar day begins at.</summary>
    /// <remarks>
    /// Some zones — Palestine's among them — begin summer time exactly at midnight, so on that
    /// one night midnight itself does not exist. The day then starts at the first minute that
    /// does, rather than the conversion throwing and taking the whole attention pass with it.
    /// </remarks>
    internal static DateTime StartOfLocalDay(DateTime localDate, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    /// <summary>
    /// The single bucket a record belongs to, or <c>null</c> when it belongs to none. Order
    /// matters: a record that is both overdue and untouched is overdue, counted once.
    /// </summary>
    public AttentionBucket? Classify(DateTime? dueAt, DateTime updatedAt)
    {
        if (dueAt is not null)
        {
            var due = ArabicRelativeTime.ToUtc(dueAt.Value);
            if (due < LateCutoff)
            {
                return AttentionBucket.Late;
            }

            if (due < NearCutoff)
            {
                return AttentionBucket.Near;
            }
        }

        return ArabicRelativeTime.ToUtc(updatedAt) < StaleCutoff ? AttentionBucket.Stale : null;
    }

    /// <summary>Whole days between the due date and today; 0 when not yet due.</summary>
    public int DaysLate(DateTime? dueAt)
    {
        if (dueAt is null)
        {
            return 0;
        }

        // Both sides are measured in the user's own day, for the same reason the cutoffs are:
        // «متأخر يومين» must not become «متأخر 3 أيام» simply because the pass ran before dawn.
        var dueLocal = TimeZoneInfo.ConvertTimeFromUtc(ArabicRelativeTime.ToUtc(dueAt.Value), Zone).Date;
        var days = (int)(LocalToday - dueLocal).TotalDays;
        return days > 0 ? days : 0;
    }
}

/// <summary>Money rendering for Core-produced text: agorot to «₪ 1,234.50» with western digits (ARCHITECTURE.md §12).</summary>
public static class Money
{
    /// <summary>Formats <paramref name="agorot"/> as shekels with a thousands separator and the ₪ sign.</summary>
    /// <remarks>
    /// The sign leads the amount, as the phone-expense rows of the W08 mockup render it. The
    /// order is not cosmetic: ₪ is a currency terminator in the bidirectional algorithm, so it
    /// joins the digit run either way, and writing it first is what puts it on the side the
    /// mockup shows once the row is laid out right-to-left.
    /// </remarks>
    public static string Shekels(long agorot) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"₪ {agorot / 100m:#,0.00}");
}
