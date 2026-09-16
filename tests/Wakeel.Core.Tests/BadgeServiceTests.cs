using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using Wakeel.Core.Services;

namespace Wakeel.Core.Tests;

/// <summary>
/// Badge counts for the sidebar, the group headers, the bell and the inner tabs (AGREEMENT
/// items 21 and 26) — in particular that a group header never counts the same record twice.
/// </summary>
public sealed class BadgeServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 16, 9, 0, 0, DateTimeKind.Utc);

    private readonly DailyShellWorld _world = new(Now);

    public void Dispose() => _world.Dispose();

    [Fact]
    public async Task ItemBadges_CountThePendingRecordsOfEachSidebarItem()
    {
        _world.AddWithStamps(
            Correspondence("وارد متأخر", InOutDirection.In, Now.AddDays(-4)),
            Correspondence("صادر قريب", InOutDirection.Out, Now.AddDays(1)),
            Task("مهمة متأخرة", Now.AddDays(-2)),
            Commitment("التزام متأخر", Now.AddDays(-2)),
            Case("قضية متأخرة", Now.AddDays(-2)),
            PendingExpense("مصروف"));

        var snapshot = await _world.Badges.RefreshAsync(Now);

        Assert.Equal(2, snapshot.For(BadgeKeys.Correspondence));
        Assert.Equal(2, snapshot.For(BadgeKeys.Tasks)); // a task and a commitment share the item
        Assert.Equal(1, snapshot.For(BadgeKeys.Cases));
        Assert.Equal(1, snapshot.For(BadgeKeys.Finance));
        Assert.Equal(6, snapshot.For(BadgeKeys.Attention));
    }

    [Fact]
    public async Task GroupBadge_CountsEachRecordOnce_EvenWhenSeveralItemsClaimIt()
    {
        // Every one of these six records is pending for «مركز الانتباه» AND for its own item.
        // «مركز الانتباه», «المراسلات», «المهام والمتابعة» and «القضايا» all sit in the «العمل
        // اليومي» group, so summing that group's item badges gives 6 + 2 + 2 + 1 = 11 while only
        // six distinct records exist — the header must show 6 (AGREEMENT item 26). The phone
        // expense is counted once there (through «مركز الانتباه») and once in the finance group,
        // which is correct: it genuinely needs attention in two different places.
        _world.AddWithStamps(
            Correspondence("وارد متأخر", InOutDirection.In, Now.AddDays(-4)),
            Correspondence("صادر قريب", InOutDirection.Out, Now.AddDays(1)),
            Task("مهمة متأخرة", Now.AddDays(-2)),
            Commitment("التزام متأخر", Now.AddDays(-2)),
            Case("قضية متأخرة", Now.AddDays(-2)),
            PendingExpense("مصروف"));

        var snapshot = await _world.Badges.RefreshAsync(Now);

        var dailyWorkItems = BadgeKeys.Groups[BadgeKeys.GroupDailyWork].Sum(snapshot.For);
        Assert.Equal(11, dailyWorkItems);
        Assert.Equal(6, snapshot.For(BadgeKeys.GroupDailyWork));
        Assert.True(snapshot.For(BadgeKeys.GroupDailyWork) < dailyWorkItems);

        Assert.Equal(1, snapshot.For(BadgeKeys.GroupFinanceReports));
    }

    [Fact]
    public async Task ARecordInSeveralBucketsOverTime_IsStillOneRecordInTheGroupBadge()
    {
        // The same correspondence item is overdue and untouched. It is one record, so the item
        // badge is 1 and the group badge is 1 — not 2.
        var row = Correspondence("وارد منسي", InOutDirection.In, Now.AddDays(-30));
        row.UpdatedAt = Now.AddDays(-60);
        _world.AddWithStamps(row);

        var snapshot = await _world.Badges.RefreshAsync(Now);

        Assert.Equal(1, snapshot.For(BadgeKeys.Correspondence));
        Assert.Equal(1, snapshot.For(BadgeKeys.Attention));
        Assert.Equal(1, snapshot.For(BadgeKeys.GroupDailyWork));
    }

    [Fact]
    public async Task AnItemWithNothingPending_HasNoBadgeAtAll()
    {
        var snapshot = await _world.Badges.RefreshAsync(Now);

        Assert.Empty(snapshot.Items);
        Assert.Empty(snapshot.Groups);
        Assert.Equal(0, snapshot.For(BadgeKeys.Documents));
        Assert.Equal(0, snapshot.Bell);
    }

    [Fact]
    public async Task BellBadge_CountsUnreadUndismissedNotifications()
    {
        await _world.Notifications.CreateAsync(NotificationKinds.General, "أول");
        var second = await _world.Notifications.CreateAsync(NotificationKinds.General, "ثانٍ");
        var third = await _world.Notifications.CreateAsync(NotificationKinds.General, "ثالث");
        await _world.Notifications.MarkReadAsync(second.Id, Now);
        await _world.Notifications.DismissAsync(third.Id, Now);

        var snapshot = await _world.Badges.RefreshAsync(Now);

        Assert.Equal(1, snapshot.Bell);
        Assert.Equal(1, snapshot.For(BadgeKeys.Bell));
    }

    [Fact]
    public async Task RefreshRaisesChangedWithTheSnapshot_AndInvalidateRaisesAStaleEvent()
    {
        var events = new List<BadgeChangedEventArgs>();
        _world.Badges.Changed += (_, e) => events.Add(e);

        var snapshot = await _world.Badges.RefreshAsync(Now);
        _world.Badges.Invalidate();

        Assert.Equal(2, events.Count);
        Assert.Same(snapshot, events[0].Snapshot);
        Assert.False(events[0].IsStale);
        Assert.True(events[1].IsStale);
        Assert.Same(snapshot, _world.Badges.Current);
    }

    [Fact]
    public async Task TabCounts_ForTheLateScreen_MatchTheFourIndicators()
    {
        _world.AddWithStamps(
            Task("متأخرة", Now.AddDays(-2)),
            Task("قريبة", Now.AddDays(1)),
            Stale("راكدة"),
            PendingExpense("بانتظار"));

        var tabs = await _world.Badges.GetTabCountsAsync(BadgeScreens.Attention, Now);

        Assert.Equal(1, tabs[BadgeTabs.Late]);
        Assert.Equal(1, tabs[BadgeTabs.Near]);
        Assert.Equal(1, tabs[BadgeTabs.Stale]);
        Assert.Equal(1, tabs[BadgeTabs.PendingConfirmation]);
    }

    [Fact]
    public async Task TabCounts_ForTheNotificationPanel_SplitAllFromUnread()
    {
        await _world.Notifications.CreateAsync(NotificationKinds.General, "أول");
        var read = await _world.Notifications.CreateAsync(NotificationKinds.General, "ثانٍ");
        await _world.Notifications.MarkReadAsync(read.Id, Now);

        var tabs = await _world.Badges.GetTabCountsAsync(BadgeScreens.Notifications, Now);

        Assert.Equal(2, tabs[BadgeTabs.All]);
        Assert.Equal(1, tabs[BadgeTabs.Unread]);
    }

    [Fact]
    public async Task TabCounts_ForFinance_SplitPhoneExpensesByStatus()
    {
        var deviceId = _world.EnsureDevice();
        _world.AddWithStamps(
            new PhoneExpense { PhoneDeviceId = deviceId, Amount = 100, Purpose = "أ", At = Now, Status = PhoneExpenseStatus.Pending },
            new PhoneExpense { PhoneDeviceId = deviceId, Amount = 200, Purpose = "ب", At = Now, Status = PhoneExpenseStatus.Pending },
            new PhoneExpense { PhoneDeviceId = deviceId, Amount = 300, Purpose = "ج", At = Now, Status = PhoneExpenseStatus.Confirmed },
            new PhoneExpense { PhoneDeviceId = deviceId, Amount = 400, Purpose = "د", At = Now, Status = PhoneExpenseStatus.Rejected });

        var tabs = await _world.Badges.GetTabCountsAsync(BadgeScreens.Finance, Now);

        Assert.Equal(2, tabs[BadgeTabs.PendingConfirmation]);
        Assert.Equal(1, tabs[BadgeTabs.Confirmed]);
        Assert.Equal(1, tabs[BadgeTabs.Rejected]);
    }

    [Fact]
    public async Task TabCounts_ForAnUnknownScreen_AreEmptyRatherThanAFailedRender()
    {
        var tabs = await _world.Badges.GetTabCountsAsync("no-such-screen", Now);
        Assert.Empty(tabs);
    }

    [Fact]
    public async Task ReportsBadge_CountsCyclesStillAwaitingTheirReport()
    {
        _world.AddWithStamps(
            new FinancialCycle { NameAr = "دورة أغسطس 2026", StartDate = Now.AddDays(-60).Date, EndDate = Now.AddDays(-31).Date, Status = FinancialCycleStatus.AwaitingIssue },
            new FinancialCycle { NameAr = "دورة سبتمبر 2026", StartDate = Now.AddDays(-30).Date, EndDate = Now.AddDays(-1).Date, Status = FinancialCycleStatus.AwaitingIssue },
            new FinancialCycle { NameAr = "دورة يوليو 2026", StartDate = Now.AddDays(-90).Date, EndDate = Now.AddDays(-61).Date, Status = FinancialCycleStatus.Issued, IssuedAt = Now.AddDays(-55) });

        var snapshot = await _world.Badges.RefreshAsync(Now);

        Assert.Equal(2, snapshot.For(BadgeKeys.Reports));
        Assert.Equal(2, snapshot.For(BadgeKeys.GroupFinanceReports));
    }

    private Correspondence Correspondence(string subject, InOutDirection direction, DateTime due) => new()
    {
        Subject = subject,
        Direction = direction,
        Status = CorrespondenceStatus.InProgress,
        DueAt = due,
        UpdatedAt = Now.AddHours(-1),
    };

    private TaskItem Task(string title, DateTime due) => new()
    {
        Title = title,
        Status = WorkTaskStatus.Open,
        DueAt = due,
        UpdatedAt = Now.AddHours(-1),
    };

    private TaskItem Stale(string title) => new()
    {
        Title = title,
        Status = WorkTaskStatus.Open,
        UpdatedAt = Now.AddDays(-40),
    };

    private Commitment Commitment(string title, DateTime due) => new()
    {
        Title = title,
        Amount = 1000,
        Status = CommitmentStatus.Open,
        DueAt = due,
        UpdatedAt = Now.AddHours(-1),
    };

    private Case Case(string title, DateTime hearing) => new()
    {
        CaseNumber = "ق/1",
        Title = title,
        Status = CaseStatus.Open,
        NextHearingAt = hearing,
        UpdatedAt = Now.AddHours(-1),
    };

    private PhoneExpense PendingExpense(string purpose) => new()
    {
        PhoneDeviceId = _world.EnsureDevice(),
        Amount = 4500,
        Purpose = purpose,
        At = Now.AddDays(-1),
        Status = PhoneExpenseStatus.Pending,
        UpdatedAt = Now.AddHours(-1),
    };
}
