using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;

namespace Wakeel.Core.Services;

/// <summary>
/// Stable sidebar item and group keys the badges are published under. They are the same strings
/// as <c>WSidebar.Keys</c> in <c>Wakeel.Design</c> — repeated here rather than referenced,
/// because Core sits below the design library and cannot reference it. A test in
/// <c>Wakeel.UI.Tests</c> is the right place to assert the two lists stay identical.
/// </summary>
public static class BadgeKeys
{
    public const string GroupDailyWork = "daily-work";
    public const string GroupRecords = "records";
    public const string GroupFinanceReports = "finance-reports";

    public const string Attention = "attention";
    public const string Correspondence = "correspondence";
    public const string Tasks = "tasks";
    public const string Meetings = "meetings";
    public const string Calendar = "calendar";
    public const string Cases = "cases";
    public const string Documents = "documents";
    public const string Parties = "parties";
    public const string Employees = "employees";
    public const string Assets = "assets";
    public const string Finance = "finance";
    public const string Reports = "reports";

    /// <summary>The bell (AGREEMENT item 21); not a sidebar item, published separately.</summary>
    public const string Bell = "bell";

    /// <summary>Which items belong to which group header, in sidebar order.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Groups { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [GroupDailyWork] = [Attention, Correspondence, Tasks, Meetings, Calendar, Cases],
            [GroupRecords] = [Documents, Parties, Employees, Assets],
            [GroupFinanceReports] = [Finance, Reports],
        };
}

/// <summary>Screens whose inner tabs carry their own badges (AGREEMENT item 26).</summary>
public static class BadgeScreens
{
    /// <summary>W09 — «متأخر / قريب / راكد / بانتظار تأكيدي».</summary>
    public const string Attention = "w09";

    /// <summary>W10 — «الكل / غير المقروء».</summary>
    public const string Notifications = "w10";

    /// <summary>Finance — phone expenses by status (AGREEMENT item 50).</summary>
    public const string Finance = "finance";
}

/// <summary>Tab keys used by <see cref="IBadgeService.GetTabCountsAsync"/>.</summary>
public static class BadgeTabs
{
    public const string Late = "late";
    public const string Near = "near";
    public const string Stale = "stale";
    public const string PendingConfirmation = "pending";

    public const string All = "all";
    public const string Unread = "unread";

    public const string Confirmed = "confirmed";
    public const string Rejected = "rejected";
}

/// <summary>One computed set of badge numbers.</summary>
/// <param name="Items">Count per sidebar item key (<see cref="BadgeKeys"/>); an item with nothing pending is absent.</param>
/// <param name="Groups">Count per group header key, counting each record once (AGREEMENT item 26).</param>
/// <param name="Bell">Unread, undismissed notifications.</param>
/// <param name="ComputedAt">When this snapshot was computed, UTC.</param>
public sealed record BadgeSnapshot(
    IReadOnlyDictionary<string, int> Items,
    IReadOnlyDictionary<string, int> Groups,
    int Bell,
    DateTime ComputedAt)
{
    /// <summary>All-zero badges, used before the first refresh.</summary>
    public static BadgeSnapshot Empty { get; } = new(
        new Dictionary<string, int>(StringComparer.Ordinal),
        new Dictionary<string, int>(StringComparer.Ordinal),
        0,
        DateTime.UnixEpoch);

    /// <summary>The badge for one sidebar item or group key; 0 when nothing is pending.</summary>
    public int For(string key) =>
        Items.TryGetValue(key, out var item) ? item
        : Groups.TryGetValue(key, out var group) ? group
        : key == BadgeKeys.Bell ? Bell
        : 0;
}

/// <summary>
/// Raised when the badges change. <see cref="Snapshot"/> carries the freshly computed numbers;
/// it is <c>null</c> when a writer merely told the badge service its numbers are now out of date
/// (<see cref="IBadgeService.Invalidate"/>) without paying for a recomputation — the shell then
/// decides when to call <see cref="IBadgeService.RefreshAsync"/>.
/// </summary>
public sealed class BadgeChangedEventArgs(BadgeSnapshot? snapshot) : EventArgs
{
    /// <summary>The new numbers, or <c>null</c> when this is only an invalidation.</summary>
    public BadgeSnapshot? Snapshot { get; } = snapshot;

    /// <summary>True when the badges are known to be out of date but have not been recomputed.</summary>
    public bool IsStale => Snapshot is null;
}

/// <summary>
/// Badge counts for the sidebar items, the group headers, the bell and the inner tabs
/// (AGREEMENT items 21 and 26).
/// </summary>
public interface IBadgeService
{
    /// <summary>The most recently computed snapshot, or <see cref="BadgeSnapshot.Empty"/> before the first refresh.</summary>
    BadgeSnapshot Current { get; }

    /// <summary>Raised after every <see cref="RefreshAsync"/> and every <see cref="Invalidate"/>.</summary>
    /// <remarks>
    /// The handler may run on a background thread: the reminder scheduler invalidates the badges
    /// from its minute pass, which lands wherever that pass's
    /// <see cref="BackgroundPassDispatcher"/> put it. A Blazor subscriber must marshal through
    /// <c>ComponentBase.InvokeAsync</c> before touching component state or calling
    /// <c>StateHasChanged</c>.
    /// </remarks>
    event EventHandler<BadgeChangedEventArgs>? Changed;

    /// <summary>Recomputes every badge and raises <see cref="Changed"/>.</summary>
    Task<BadgeSnapshot> RefreshAsync(DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Counts for one screen's inner tabs (<see cref="BadgeScreens"/>).</summary>
    Task<IReadOnlyDictionary<string, int>> GetTabCountsAsync(string screen, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Tells the shell the badges are out of date, without recomputing them.</summary>
    void Invalidate();
}

/// <inheritdoc cref="IBadgeService"/>
/// <remarks>
/// <para>
/// <b>No record is counted twice (AGREEMENT item 26).</b> Each sidebar item's badge is the number
/// of DISTINCT records pending for it, and each group header's badge is the size of the UNION of
/// its items' record sets — not the sum of their badges. That distinction is not academic here:
/// «مركز الانتباه» deliberately shows everything that needs action today, so every overdue
/// correspondence item is pending for both «مركز الانتباه» and «المراسلات» and every overdue task
/// for both «مركز الانتباه» and «المهام والمتابعة» — all three inside the «العمل اليومي» group.
/// Summing the item badges would count each of those records twice or three times; taking the
/// union counts it once, which is what the group header must show.
/// </para>
/// <para>
/// Records are identified across tables by (table, id), so two rows that happen to share a
/// <see cref="Guid"/> in different tables are still two records.
/// </para>
/// </remarks>
public sealed class BadgeService(WakeelDb db, IAttentionService attention) : IBadgeService
{
    private BadgeSnapshot _current = BadgeSnapshot.Empty;

    public BadgeSnapshot Current => _current;

    public event EventHandler<BadgeChangedEventArgs>? Changed;

    public void Invalidate() => Changed?.Invoke(this, new BadgeChangedEventArgs(null));

    public async Task<BadgeSnapshot> RefreshAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var utcNow = ArabicRelativeTime.ToUtc(now);

        // ONE attention pass feeds most of the sidebar (the four buckets are slices of it, taken
        // in memory — asking the attention service for each bucket separately would re-run the
        // same six queries four times over); the rest are three narrow counts.
        var all = await attention.GetAllAsync(utcNow, cancellationToken).ConfigureAwait(false);
        var todayMeetings = await attention.GetTodayMeetingsAsync(utcNow, cancellationToken).ConfigureAwait(false);

        // Record sets per sidebar item. A record can legitimately land in more than one set.
        var sets = new Dictionary<string, HashSet<RecordRef>>(StringComparer.Ordinal)
        {
            [BadgeKeys.Attention] = [],
            [BadgeKeys.Correspondence] = [],
            [BadgeKeys.Tasks] = [],
            [BadgeKeys.Cases] = [],
            [BadgeKeys.Meetings] = [],
            [BadgeKeys.Finance] = [],
            [BadgeKeys.Reports] = [],
        };

        foreach (var item in all)
        {
            var reference = RecordRef.For(item);
            sets[BadgeKeys.Attention].Add(reference);
            switch (item.Kind)
            {
                case AttentionEntityKind.CorrespondenceIn:
                case AttentionEntityKind.CorrespondenceOut:
                    sets[BadgeKeys.Correspondence].Add(reference);
                    break;
                case AttentionEntityKind.Task:
                case AttentionEntityKind.Commitment:
                case AttentionEntityKind.Decision:
                    sets[BadgeKeys.Tasks].Add(reference);
                    break;
                case AttentionEntityKind.Case:
                    sets[BadgeKeys.Cases].Add(reference);
                    break;
                case AttentionEntityKind.PhoneExpense:
                    // AGREEMENT item 50: phone expenses awaiting confirmation carry the finance badge.
                    sets[BadgeKeys.Finance].Add(reference);
                    break;
            }
        }

        // Today's meetings that have not been held yet.
        foreach (var meeting in todayMeetings)
        {
            if (meeting.StartsAt >= utcNow)
            {
                sets[BadgeKeys.Meetings].Add(new RecordRef(RecordRef.Meetings, meeting.Id));
            }
        }

        // Cycles whose report is still to be issued (AGREEMENT item 52).
        var awaitingIssue = await db.FinancialCycles.AsNoTracking()
            .Where(c => c.Status == FinancialCycleStatus.AwaitingIssue && c.IssuedAt == null)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var id in awaitingIssue)
        {
            sets[BadgeKeys.Reports].Add(new RecordRef(RecordRef.FinancialCycles, id));
        }

        var items = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, set) in sets)
        {
            if (set.Count > 0)
            {
                items[key] = set.Count;
            }
        }

        var groups = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (groupKey, memberKeys) in BadgeKeys.Groups)
        {
            var union = new HashSet<RecordRef>();
            foreach (var memberKey in memberKeys)
            {
                if (sets.TryGetValue(memberKey, out var set))
                {
                    union.UnionWith(set);
                }
            }

            if (union.Count > 0)
            {
                groups[groupKey] = union.Count;
            }
        }

        var bell = await db.Notifications.AsNoTracking()
            .CountAsync(n => n.ReadAt == null && n.DismissedAt == null, cancellationToken).ConfigureAwait(false);

        var snapshot = new BadgeSnapshot(items, groups, bell, utcNow);
        _current = snapshot;
        Changed?.Invoke(this, new BadgeChangedEventArgs(snapshot));
        return snapshot;
    }

    public async Task<IReadOnlyDictionary<string, int>> GetTabCountsAsync(string screen, DateTime now, CancellationToken cancellationToken = default)
    {
        var utcNow = ArabicRelativeTime.ToUtc(now);
        switch (screen)
        {
            case BadgeScreens.Attention:
            {
                var counts = await attention.GetCountsAsync(utcNow, cancellationToken).ConfigureAwait(false);
                return new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    [BadgeTabs.Late] = counts.Late,
                    [BadgeTabs.Near] = counts.Near,
                    [BadgeTabs.Stale] = counts.Stale,
                    [BadgeTabs.PendingConfirmation] = counts.PendingConfirmation,
                };
            }

            case BadgeScreens.Notifications:
            {
                var all = await db.Notifications.AsNoTracking()
                    .CountAsync(n => n.DismissedAt == null, cancellationToken).ConfigureAwait(false);
                var unread = await db.Notifications.AsNoTracking()
                    .CountAsync(n => n.DismissedAt == null && n.ReadAt == null, cancellationToken).ConfigureAwait(false);
                return new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    [BadgeTabs.All] = all,
                    [BadgeTabs.Unread] = unread,
                };
            }

            case BadgeScreens.Finance:
            {
                var byStatus = await db.PhoneExpenses.AsNoTracking()
                    .GroupBy(e => e.Status)
                    .Select(g => new { Status = g.Key, Count = g.Count() })
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                var map = byStatus.ToDictionary(x => x.Status, x => x.Count);
                return new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    [BadgeTabs.PendingConfirmation] = map.GetValueOrDefault(PhoneExpenseStatus.Pending),
                    [BadgeTabs.Confirmed] = map.GetValueOrDefault(PhoneExpenseStatus.Confirmed),
                    [BadgeTabs.Rejected] = map.GetValueOrDefault(PhoneExpenseStatus.Rejected),
                };
            }

            default:
                // An unknown screen key is a caller bug, not something a user can act on, so it
                // returns nothing rather than throwing into a render.
                return new Dictionary<string, int>(StringComparer.Ordinal);
        }
    }
}

/// <summary>
/// Identifies one record across tables, so a union can count it exactly once. The table name is
/// part of the identity: two rows in different tables are two records even in the (practically
/// impossible, but free to rule out) case of a shared <see cref="Guid"/>.
/// </summary>
internal readonly record struct RecordRef(string Table, Guid Id)
{
    public const string Correspondence = "correspondence";
    public const string Tasks = "tasks";
    public const string Commitments = "commitments";
    public const string Decisions = "decisions";
    public const string Cases = "cases";
    public const string PhoneExpenses = "phone_expenses";
    public const string Meetings = "meetings";
    public const string FinancialCycles = "financial_cycles";

    public static RecordRef For(AttentionItem item) => new(item.Kind switch
    {
        AttentionEntityKind.CorrespondenceIn or AttentionEntityKind.CorrespondenceOut => Correspondence,
        AttentionEntityKind.Task => Tasks,
        AttentionEntityKind.Commitment => Commitments,
        AttentionEntityKind.Decision => Decisions,
        AttentionEntityKind.Case => Cases,
        _ => PhoneExpenses,
    }, item.Id);
}
