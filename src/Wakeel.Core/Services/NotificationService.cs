using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;

namespace Wakeel.Core.Services;

/// <summary>Well-known <c>notifications.kind</c> values; the panel picks the row's icon from these.</summary>
public static class NotificationKinds
{
    public const string Meeting = "meeting";
    public const string Appointment = "appointment";
    public const string TaskDue = "task_due";
    public const string CommitmentDue = "commitment_due";
    public const string FinancialCycle = "financial_cycle";
    public const string Backup = "backup";
    public const string Clock = "clock";
    public const string PhoneExpense = "phone_expense";
    public const string Sync = "sync";
    public const string General = "general";
}

/// <summary>Which day-group a notification falls into in the panel (W10).</summary>
public enum NotificationDayGroup
{
    /// <summary>«اليوم».</summary>
    Today,

    /// <summary>«أمس».</summary>
    Yesterday,

    /// <summary>«أقدم».</summary>
    Older,
}

/// <summary>One notification, ready to render.</summary>
/// <param name="Id">Row id.</param>
/// <param name="Kind">One of <see cref="NotificationKinds"/>, used for the icon.</param>
/// <param name="TitleAr">Arabic title.</param>
/// <param name="BodyAr">Arabic body, when the notification has one.</param>
/// <param name="EntityType">Table the notification points at, when it points at a record.</param>
/// <param name="EntityId">Id of that record, so clicking the row opens it.</param>
/// <param name="CreatedAt">When it was raised, UTC.</param>
/// <param name="DueAt">The event's own instant, when it has one (a meeting's start, a due date).</param>
/// <param name="IsRead">Whether it has been read.</param>
/// <param name="RelativeAr">«الآن» / «قبل 10 دقائق» / «أمس 16:40» / «dd/MM/yyyy HH:mm».</param>
/// <param name="Group">Day group for the panel's headers.</param>
public sealed record NotificationView(
    Guid Id,
    string Kind,
    string TitleAr,
    string? BodyAr,
    string? EntityType,
    Guid? EntityId,
    DateTime CreatedAt,
    DateTime? DueAt,
    bool IsRead,
    string RelativeAr,
    NotificationDayGroup Group);

/// <summary>The notification panel's content: the day groups, the unread count and whether to play a sound.</summary>
/// <param name="Groups">Day group to its notifications, newest first; an empty group is omitted.</param>
/// <param name="Total">
/// Every notification the panel's filter matches, not only the page held in <paramref name="Groups"/>:
/// the «الكل» tab on W10 must keep counting past the page size. <paramref name="Groups"/> carries at
/// most the requested limit; this is the real number behind it.
/// </param>
/// <param name="Unread">
/// Every unread, undismissed notification — the bell's badge, counted over the whole table rather
/// than over the fetched page, so the panel's own header agrees with the tab badges.
/// </param>
/// <param name="SoundEnabled">The current «صوت التنبيهات» setting, so the caller knows whether to play one.</param>
public sealed record NotificationPanel(
    IReadOnlyDictionary<NotificationDayGroup, IReadOnlyList<NotificationView>> Groups,
    int Total,
    int Unread,
    bool SoundEnabled);

/// <summary>Creating, reading and dismissing bell notifications (W10).</summary>
public interface INotificationService
{
    /// <summary>
    /// Creates a notification. When <paramref name="uniqueKey"/> is given and a notification with
    /// that key already exists, nothing is written and the existing row is returned — this is how
    /// <see cref="IReminderScheduler"/> stays idempotent across ticks.
    /// </summary>
    /// <param name="createdAt">
    /// The instant to stamp the row with; defaults to the wall clock. The reminder scheduler
    /// passes the instant its tick ran at, so a notification always carries the same clock that
    /// decided its event was due (and a test that drives the tick gets deterministic rows).
    /// </param>
    Task<Notification> CreateAsync(
        string kind,
        string titleAr,
        string? bodyAr = null,
        string? entityType = null,
        Guid? entityId = null,
        DateTime? dueAt = null,
        string? uniqueKey = null,
        DateTime? createdAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>The panel's content for <paramref name="now"/>, newest first within each day group.</summary>
    Task<NotificationPanel> GetPanelAsync(DateTime now, bool unreadOnly = false, int limit = DefaultLimit, CancellationToken cancellationToken = default);

    /// <summary>Unread, undismissed notifications — the bell's badge.</summary>
    Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default);

    /// <summary>Marks one notification read; returns false when it does not exist.</summary>
    Task<bool> MarkReadAsync(Guid id, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>«تحديد الكل كمقروء»; returns how many rows changed.</summary>
    Task<int> MarkAllReadAsync(DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Dismisses one notification (it leaves the panel but is not deleted); returns false when it does not exist.</summary>
    Task<bool> DismissAsync(Guid id, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>The default page size of <see cref="GetPanelAsync"/>.</summary>
    public const int DefaultLimit = 100;
}

/// <inheritdoc cref="INotificationService"/>
/// <remarks>
/// <para>
/// <b>Idempotency.</b> <c>notifications</c> (DATA-MODEL.md §1) has no unique key of its own, so
/// the event key a caller passes as <c>uniqueKey</c> is stored in the row's <c>source</c> column
/// and checked before every insert. One office database is written by exactly one process, so a
/// check-then-insert is sufficient; the check is a single indexed-by-time lookup, and the
/// scheduler batches it further by pre-loading the keys it is about to test (see
/// <see cref="ReminderScheduler"/>).
/// </para>
/// <para>
/// <b>Dismissed rows stay.</b> Dismissing sets <c>dismissed_at</c>; nothing here deletes a row,
/// so a dismissed reminder's key still blocks a duplicate of the same event.
/// </para>
/// </remarks>
public sealed class NotificationService(WakeelDb db, ISettingsService settings, IIdGenerator ids, IClock clock) : INotificationService
{
    public async Task<Notification> CreateAsync(
        string kind,
        string titleAr,
        string? bodyAr = null,
        string? entityType = null,
        Guid? entityId = null,
        DateTime? dueAt = null,
        string? uniqueKey = null,
        DateTime? createdAt = null,
        CancellationToken cancellationToken = default)
    {
        if (uniqueKey is { Length: > 0 })
        {
            var existing = await db.Notifications.AsNoTracking()
                .FirstOrDefaultAsync(n => n.Source == uniqueKey, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                return existing;
            }
        }

        var row = new Notification
        {
            Id = ids.NewId(),
            Kind = kind,
            Title = titleAr,
            Body = bodyAr,
            EntityType = entityType,
            EntityId = entityId,
            CreatedAt = createdAt is null ? clock.UtcNow : ArabicRelativeTime.ToUtc(createdAt.Value),
            DueAt = dueAt is null ? null : ArabicRelativeTime.ToUtc(dueAt.Value),
            Source = uniqueKey,
        };
        db.Notifications.Add(row);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return row;
    }

    public async Task<NotificationPanel> GetPanelAsync(DateTime now, bool unreadOnly = false, int limit = INotificationService.DefaultLimit, CancellationToken cancellationToken = default)
    {
        var utcNow = ArabicRelativeTime.ToUtc(now);
        var zone = TimeZoneInfo.Local;

        var query = db.Notifications.AsNoTracking().Where(n => n.DismissedAt == null);
        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        // Both figures are counted over the table, never over the page: a panel that read its own
        // header off the fetched rows would silently stop growing at the limit.
        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var unread = await db.Notifications.AsNoTracking()
            .CountAsync(n => n.DismissedAt == null && n.ReadAt == null, cancellationToken).ConfigureAwait(false);

        var rows = await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var localToday = TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone).Date;
        var groups = new Dictionary<NotificationDayGroup, List<NotificationView>>();
        foreach (var row in rows)
        {
            var created = ArabicRelativeTime.ToUtc(row.CreatedAt);
            var localDay = TimeZoneInfo.ConvertTimeFromUtc(created, zone).Date;
            var group = localDay == localToday
                ? NotificationDayGroup.Today
                : localDay == localToday.AddDays(-1) ? NotificationDayGroup.Yesterday : NotificationDayGroup.Older;

            var view = new NotificationView(
                row.Id,
                row.Kind,
                row.Title,
                row.Body,
                row.EntityType,
                row.EntityId,
                created,
                row.DueAt,
                row.ReadAt is not null,
                ArabicRelativeTime.Describe(created, utcNow, zone),
                group);

            if (!groups.TryGetValue(group, out var list))
            {
                list = [];
                groups[group] = list;
            }

            list.Add(view);
        }

        var soundEnabled = await settings.GetSoundsEnabledAsync(cancellationToken).ConfigureAwait(false);
        var readOnlyGroups = groups.ToDictionary(g => g.Key, g => (IReadOnlyList<NotificationView>)g.Value);
        return new NotificationPanel(readOnlyGroups, total, unread, soundEnabled);
    }

    public Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default) =>
        db.Notifications.AsNoTracking().CountAsync(n => n.ReadAt == null && n.DismissedAt == null, cancellationToken);

    public async Task<bool> MarkReadAsync(Guid id, DateTime now, CancellationToken cancellationToken = default)
    {
        var row = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }

        if (row.ReadAt is null)
        {
            row.ReadAt = ArabicRelativeTime.ToUtc(now);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    public async Task<int> MarkAllReadAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var utcNow = ArabicRelativeTime.ToUtc(now);
        var rows = await db.Notifications
            .Where(n => n.ReadAt == null && n.DismissedAt == null)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var row in rows)
        {
            row.ReadAt = utcNow;
        }

        if (rows.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return rows.Count;
    }

    public async Task<bool> DismissAsync(Guid id, DateTime now, CancellationToken cancellationToken = default)
    {
        var row = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }

        if (row.DismissedAt is null)
        {
            var utcNow = ArabicRelativeTime.ToUtc(now);
            row.DismissedAt = utcNow;

            // A dismissed notification is out of the panel, so it can no longer be "unread": it
            // would otherwise keep the bell's badge lit forever with a row nobody can reach.
            row.ReadAt ??= utcNow;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return true;
    }
}
