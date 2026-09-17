using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Services;

namespace Wakeel.Core.Tests;

/// <summary>Creating, grouping, reading and dismissing bell notifications (W10).</summary>
public sealed class NotificationServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 16, 9, 0, 0, DateTimeKind.Utc);

    private readonly DailyShellWorld _world = new(Now);

    public void Dispose() => _world.Dispose();

    [Fact]
    public async Task ANewNotification_IsUnreadAndCarriesWhatTheRowClickOpens()
    {
        var entityId = Guid.CreateVersion7();

        var created = await _world.Notifications.CreateAsync(
            NotificationKinds.TaskDue,
            "مهمة مستحقة: تجهيز الملف",
            "حان موعد إنجاز هذه المهمة",
            entityType: "tasks",
            entityId: entityId,
            dueAt: Now,
            createdAt: Now);

        Assert.Null(created.ReadAt);
        Assert.Null(created.DismissedAt);
        Assert.Equal("tasks", created.EntityType);
        Assert.Equal(entityId, created.EntityId);
        Assert.Equal(1, await _world.Notifications.GetUnreadCountAsync());
    }

    [Fact]
    public async Task AUniqueKey_MakesASecondCreateReturnTheSameRowWithoutWritingOne()
    {
        var first = await _world.Notifications.CreateAsync(NotificationKinds.Backup, "حان وقت النسخ الاحتياطي", uniqueKey: "reminder:backup:20260916");
        var second = await _world.Notifications.CreateAsync(NotificationKinds.Backup, "حان وقت النسخ الاحتياطي", uniqueKey: "reminder:backup:20260916");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await _world.Db.Notifications.CountAsync());
    }

    [Fact]
    public async Task ThePanel_GroupsByTodayYesterdayAndOlder_InTheDevicesOwnDay()
    {
        var zone = TimeZoneInfo.Local;
        var localToday = TimeZoneInfo.ConvertTimeFromUtc(Now, zone).Date;
        var todayUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localToday.AddHours(8), DateTimeKind.Unspecified), zone);
        var yesterdayUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localToday.AddDays(-1).AddHours(16).AddMinutes(40), DateTimeKind.Unspecified), zone);
        var olderUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localToday.AddDays(-5).AddHours(11), DateTimeKind.Unspecified), zone);

        await _world.Notifications.CreateAsync(NotificationKinds.General, "اليوم", createdAt: todayUtc);
        await _world.Notifications.CreateAsync(NotificationKinds.General, "أمس", createdAt: yesterdayUtc);
        await _world.Notifications.CreateAsync(NotificationKinds.General, "أقدم", createdAt: olderUtc);

        var panel = await _world.Notifications.GetPanelAsync(Now);

        Assert.Equal(3, panel.Total);
        Assert.Equal("اليوم", Assert.Single(panel.Groups[NotificationDayGroup.Today]).TitleAr);
        var yesterday = Assert.Single(panel.Groups[NotificationDayGroup.Yesterday]);
        Assert.Equal("أمس", yesterday.TitleAr);
        Assert.Equal("أمس 16:40", yesterday.RelativeAr);
        Assert.Equal("أقدم", Assert.Single(panel.Groups[NotificationDayGroup.Older]).TitleAr);
    }

    [Fact]
    public async Task ThePanel_ShowsNewestFirstWithinAGroup()
    {
        await _world.Notifications.CreateAsync(NotificationKinds.General, "الأقدم", createdAt: Now.AddMinutes(-30));
        await _world.Notifications.CreateAsync(NotificationKinds.General, "الأحدث", createdAt: Now.AddMinutes(-5));

        var panel = await _world.Notifications.GetPanelAsync(Now);
        var today = panel.Groups[NotificationDayGroup.Today];

        Assert.Equal(new[] { "الأحدث", "الأقدم" }, today.Select(n => n.TitleAr));
        Assert.Equal("قبل 5 دقائق", today[0].RelativeAr);
        Assert.Equal("قبل 30 دقيقة", today[1].RelativeAr);
    }

    [Fact]
    public async Task UnreadOnly_HidesTheReadRows()
    {
        var read = await _world.Notifications.CreateAsync(NotificationKinds.General, "مقروء", createdAt: Now.AddMinutes(-10));
        await _world.Notifications.CreateAsync(NotificationKinds.General, "غير مقروء", createdAt: Now.AddMinutes(-5));
        await _world.Notifications.MarkReadAsync(read.Id, Now);

        var all = await _world.Notifications.GetPanelAsync(Now);
        var unreadOnly = await _world.Notifications.GetPanelAsync(Now, unreadOnly: true);

        Assert.Equal(2, all.Total);
        Assert.Equal(1, all.Unread);
        Assert.Equal(1, unreadOnly.Total);
        Assert.Equal("غير مقروء", unreadOnly.Groups[NotificationDayGroup.Today][0].TitleAr);
    }

    [Fact]
    public async Task MarkAllRead_ClearsTheBellAndReportsHowManyItChanged()
    {
        await _world.Notifications.CreateAsync(NotificationKinds.General, "أول");
        await _world.Notifications.CreateAsync(NotificationKinds.General, "ثانٍ");
        var alreadyRead = await _world.Notifications.CreateAsync(NotificationKinds.General, "ثالث");
        await _world.Notifications.MarkReadAsync(alreadyRead.Id, Now);

        var changed = await _world.Notifications.MarkAllReadAsync(Now);

        Assert.Equal(2, changed);
        Assert.Equal(0, await _world.Notifications.GetUnreadCountAsync());
        Assert.Equal(0, await _world.Notifications.MarkAllReadAsync(Now));
    }

    [Fact]
    public async Task Dismissing_TakesTheRowOutOfThePanel_AndOutOfTheBell()
    {
        var row = await _world.Notifications.CreateAsync(NotificationKinds.General, "سيُتجاهل");

        Assert.True(await _world.Notifications.DismissAsync(row.Id, Now));

        var panel = await _world.Notifications.GetPanelAsync(Now);
        Assert.Equal(0, panel.Total);
        Assert.Equal(0, await _world.Notifications.GetUnreadCountAsync());

        // Dismissed, not deleted: the row survives so its idempotency key still holds.
        Assert.Equal(1, await _world.Db.Notifications.CountAsync());
    }

    [Fact]
    public async Task MarkingOrDismissingAnUnknownNotification_ReportsFailureInsteadOfThrowing()
    {
        Assert.False(await _world.Notifications.MarkReadAsync(Guid.CreateVersion7(), Now));
        Assert.False(await _world.Notifications.DismissAsync(Guid.CreateVersion7(), Now));
    }

    [Fact]
    public async Task ThePanel_CarriesTheSoundSettingSoTheCallerKnowsWhetherToPlayOne()
    {
        Assert.True((await _world.Notifications.GetPanelAsync(Now)).SoundEnabled);

        await _world.Settings.SetAsync(SettingKeys.SoundsEnabled, false);

        Assert.False((await _world.Notifications.GetPanelAsync(Now)).SoundEnabled);
    }

    [Fact]
    public async Task ThePanel_IsPagedSoALongHistoryNeverLoadsWholesale()
    {
        for (var i = 0; i < 30; i++)
        {
            await _world.Notifications.CreateAsync(NotificationKinds.General, $"إشعار {i}", createdAt: Now.AddMinutes(-i));
        }

        var panel = await _world.Notifications.GetPanelAsync(Now, limit: 10);

        Assert.Equal(10, panel.Groups.Values.Sum(g => g.Count));
        Assert.Equal("إشعار 0", panel.Groups[NotificationDayGroup.Today][0].TitleAr);
    }

    [Fact]
    public async Task ThePanelsTotalCountsEveryRow_NotOnlyThePageItFetched()
    {
        // W10's «الكل N» tab reads the panel's own total. Counted over the fetched page it would
        // stop growing at the limit and quietly report a hundred notifications forever.
        const int Limit = 10;
        for (var i = 0; i < Limit + 5; i++)
        {
            await _world.Notifications.CreateAsync(NotificationKinds.General, $"إشعار {i}", createdAt: Now.AddMinutes(-i));
        }

        var panel = await _world.Notifications.GetPanelAsync(Now, limit: Limit);

        Assert.Equal(Limit + 5, panel.Total);
        Assert.Equal(Limit + 5, panel.Unread);
        Assert.Equal(Limit, panel.Groups.Values.Sum(g => g.Count));

        // The bell agrees with the panel's own header: both count the table, not a page of it.
        Assert.Equal(await _world.Notifications.GetUnreadCountAsync(), panel.Unread);
    }
}
