using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using Wakeel.Core.Services;

namespace Wakeel.Core.Tests;

/// <summary>
/// The minute pass that turns due events into notifications: the AGREEMENT item 56 lead time
/// (settings default and per-meeting override), the AGREEMENT item 52 cycle reminders, and the
/// idempotency that keeps a repeated tick from repeating a notification.
/// </summary>
public sealed class ReminderSchedulerTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 16, 9, 0, 0, DateTimeKind.Utc);

    private readonly DailyShellWorld _world = new(Now);

    public ReminderSchedulerTests()
    {
        // Every pass also checks the backup reminder, and an installation with no backup at all
        // raises one — which would add a notification to every assertion in this class about some
        // other reminder. A fresh backup silences it; the three tests that are actually about the
        // backup reminder build their own world instead.
        _world.Db.Backups.Add(new Backup { At = Now, FilePath = "b", Size = 1, AppVersion = "0.21.0" });
        _world.Db.SaveChanges();
    }

    public void Dispose() => _world.Dispose();

    // -------------------------------------------------------------------------------------------
    // AGREEMENT item 56 — the meeting lead time.
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task AMeetingWithoutItsOwnLeadTime_UsesTheSettingsDefaultOfFifteenMinutes()
    {
        var starts = Now.AddMinutes(15);
        _world.AddWithStamps(Meeting("اجتماع الإدارة", starts, reminderMinutes: null));

        // One minute too early: the default lead time has not been reached yet.
        var early = await _world.Reminders.RunOnceAsync(Now.AddMinutes(-1));
        Assert.Equal(0, early.Created);

        var run = await _world.Reminders.RunOnceAsync(Now);

        Assert.Equal(1, run.Created);
        var notification = await Single(NotificationKinds.Meeting);
        Assert.Equal("اجتماع قريب: اجتماع الإدارة", notification.Title);
        Assert.Equal(starts, notification.DueAt);
    }

    [Fact]
    public async Task AMeetingsOwnLeadTime_OverridesTheSettingsDefault()
    {
        // Sixty minutes before a meeting that starts in an hour: only the override fires now.
        var starts = Now.AddMinutes(60);
        _world.AddWithStamps(
            Meeting("اجتماع بتذكير خاص", starts, reminderMinutes: 60),
            Meeting("اجتماع بالافتراضي", starts, reminderMinutes: null));

        var run = await _world.Reminders.RunOnceAsync(Now);

        Assert.Equal(1, run.Created);
        var notification = await Single(NotificationKinds.Meeting);
        Assert.Equal("اجتماع قريب: اجتماع بتذكير خاص", notification.Title);
    }

    [Fact]
    public async Task ChangingTheSettingsDefault_ChangesWhenAMeetingWithoutAnOverrideFires()
    {
        await _world.Settings.SetAsync(SettingKeys.MeetingReminderMinutes, 30);
        var starts = Now.AddMinutes(30);
        _world.AddWithStamps(Meeting("اجتماع", starts, reminderMinutes: null));

        Assert.Equal(0, (await _world.Reminders.RunOnceAsync(Now.AddMinutes(-5))).Created);
        Assert.Equal(1, (await _world.Reminders.RunOnceAsync(Now)).Created);
    }

    [Fact]
    public async Task AMeetingAskingForATwoDayHeadsUp_StillFiresTwoDaysBeforeItStarts()
    {
        // reminder_minutes has no upper bound, and item 56 lets the meeting decide. A pass whose
        // horizon stopped at a day would look straight past this meeting at the exact minute its
        // reminder came due, and the reminder would be lost with nothing to show for it.
        var starts = Now.AddDays(2);
        _world.AddWithStamps(Meeting("اجتماع مجلس الإدارة", starts, reminderMinutes: 2880));

        Assert.Equal(0, (await _world.Reminders.RunOnceAsync(Now.AddMinutes(-1))).Created);

        var run = await _world.Reminders.RunOnceAsync(Now);

        Assert.Equal(1, run.Created);
        var notification = await Single(NotificationKinds.Meeting);
        Assert.Equal("اجتماع قريب: اجتماع مجلس الإدارة", notification.Title);
        Assert.Equal(starts, notification.DueAt);
    }

    [Fact]
    public async Task AnAppointmentAskingForATwoDayHeadsUp_FiresToo()
    {
        _world.AddWithStamps(new Appointment
        {
            Title = "موعد المحكمة",
            StartsAt = Now.AddDays(2),
            Status = AppointmentStatus.Planned,
            ReminderMinutes = 2880,
        });

        Assert.Equal(0, (await _world.Reminders.RunOnceAsync(Now.AddMinutes(-1))).Created);
        Assert.Equal(1, (await _world.Reminders.RunOnceAsync(Now)).Created);
    }

    [Fact]
    public async Task AMeetingWhoseReminderIsTurnedOff_NeverFires()
    {
        _world.AddWithStamps(Meeting("اجتماع بلا تنبيه", Now.AddMinutes(15), reminderMinutes: 0));

        Assert.Equal(0, (await _world.Reminders.RunOnceAsync(Now)).Created);
    }

    [Fact]
    public async Task ACancelledMeeting_NeverFires()
    {
        var meeting = Meeting("اجتماع ملغى", Now.AddMinutes(15), reminderMinutes: null);
        meeting.Status = MeetingStatus.Cancelled;
        _world.AddWithStamps(meeting);

        Assert.Equal(0, (await _world.Reminders.RunOnceAsync(Now)).Created);
    }

    [Fact]
    public async Task AnAppointmentFollowsTheSameLeadTimeRuleAsAMeeting()
    {
        _world.AddWithStamps(new Appointment
        {
            Title = "موعد الطبيب",
            StartsAt = Now.AddMinutes(15),
            Status = AppointmentStatus.Planned,
        });

        Assert.Equal(1, (await _world.Reminders.RunOnceAsync(Now)).Created);
        var notification = await Single(NotificationKinds.Appointment);
        Assert.Equal("موعد قريب: موعد الطبيب", notification.Title);
    }

    // -------------------------------------------------------------------------------------------
    // Idempotency.
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task RunningThePassAgainOverTheSameEvent_CreatesNothingTheSecondTime()
    {
        _world.AddWithStamps(Meeting("اجتماع", Now.AddMinutes(15), reminderMinutes: null));

        var first = await _world.Reminders.RunOnceAsync(Now);
        var second = await _world.Reminders.RunOnceAsync(Now);
        var aMinuteLater = await _world.Reminders.RunOnceAsync(Now.AddMinutes(1));

        Assert.Equal(1, first.Created);
        Assert.Equal(0, second.Created);
        Assert.Equal(1, second.Skipped);
        Assert.Equal(0, aMinuteLater.Created);
        Assert.Equal(1, await _world.Db.Notifications.CountAsync(n => n.Kind == NotificationKinds.Meeting));
    }

    [Fact]
    public async Task DismissingAReminder_DoesNotLetTheNextPassRaiseItAgain()
    {
        _world.AddWithStamps(Meeting("اجتماع", Now.AddMinutes(15), reminderMinutes: null));
        await _world.Reminders.RunOnceAsync(Now);
        var raised = await Single(NotificationKinds.Meeting);
        await _world.Notifications.DismissAsync(raised.Id, Now);

        var again = await _world.Reminders.RunOnceAsync(Now.AddMinutes(2));

        Assert.Equal(0, again.Created);
        Assert.Equal(1, await _world.Db.Notifications.CountAsync(n => n.Kind == NotificationKinds.Meeting));
    }

    [Fact]
    public async Task EveryReminderNotificationCarriesItsOwnKey()
    {
        _world.AddWithStamps(
            Meeting("اجتماع أ", Now.AddMinutes(15), reminderMinutes: null),
            Meeting("اجتماع ب", Now.AddMinutes(15), reminderMinutes: null));

        await _world.Reminders.RunOnceAsync(Now);

        var sources = await _world.Db.Notifications.Select(n => n.Source).ToListAsync();
        Assert.Equal(2, sources.Count);
        Assert.Equal(2, sources.Distinct(StringComparer.Ordinal).Count());
        Assert.All(sources, s => Assert.StartsWith(ReminderScheduler.KeyPrefix, s, StringComparison.Ordinal));
    }

    // -------------------------------------------------------------------------------------------
    // Due dates.
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task ATaskAndACommitmentThatFallDue_EachRaiseOneReminder()
    {
        _world.AddWithStamps(
            new TaskItem { Title = "تسليم الكشف", Status = WorkTaskStatus.Open, DueAt = Now.AddMinutes(-1) },
            new Commitment { Title = "دفعة المورّد", Amount = 250000, Status = CommitmentStatus.Open, DueAt = Now.AddMinutes(-1) });

        var run = await _world.Reminders.RunOnceAsync(Now);

        Assert.Equal(2, run.Created);
        Assert.Equal("مهمة مستحقة: تسليم الكشف", (await Single(NotificationKinds.TaskDue)).Title);
        Assert.Equal("التزام مستحق: دفعة المورّد", (await Single(NotificationKinds.CommitmentDue)).Title);
    }

    [Fact]
    public async Task ATaskAlreadyDone_RaisesNothingWhenItsDueDatePasses()
    {
        _world.AddWithStamps(new TaskItem { Title = "منجزة", Status = WorkTaskStatus.Done, DueAt = Now.AddMinutes(-1) });

        Assert.Equal(0, (await _world.Reminders.RunOnceAsync(Now)).Created);
    }

    // -------------------------------------------------------------------------------------------
    // AGREEMENT item 52 — the financial cycle.
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheCycleReminder_FiresTheConfiguredNumberOfDaysBeforeTheEnd()
    {
        // Default report.reminder_days is 3; a cycle ending in five days is still silent.
        _world.AddWithStamps(Cycle("دورة سبتمبر 2026", endsInDays: 5));

        Assert.Equal(0, (await _world.Reminders.RunOnceAsync(Now)).Created);
        Assert.Equal(1, (await _world.Reminders.RunOnceAsync(Now.AddDays(2))).Created);

        var notification = await Single(NotificationKinds.FinancialCycle);
        Assert.Equal("تقترب نهاية دورة سبتمبر 2026", notification.Title);
        Assert.Contains("جهّز التقرير الشهري", notification.Body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnTheEndDayAndEveryDayAfterIt_TheCycleReminderRepeatsUntilTheReportIsIssued()
    {
        var cycle = Cycle("دورة سبتمبر 2026", endsInDays: 0);
        _world.AddWithStamps(cycle);

        var day0 = await _world.Reminders.RunOnceAsync(Now);
        var day0Again = await _world.Reminders.RunOnceAsync(Now.AddHours(3));
        var day1 = await _world.Reminders.RunOnceAsync(Now.AddDays(1));
        var day2 = await _world.Reminders.RunOnceAsync(Now.AddDays(2));

        Assert.Equal(1, day0.Created);
        Assert.Equal(0, day0Again.Created); // same day, same key
        Assert.Equal(1, day1.Created);
        Assert.Equal(1, day2.Created);

        var ended = await _world.Db.Notifications.Where(n => n.Kind == NotificationKinds.FinancialCycle).ToListAsync();
        Assert.Equal(3, ended.Count);
        Assert.All(ended, n => Assert.Equal("انتهت دورة سبتمبر 2026", n.Title));

        // Issuing the report stops it.
        var row = await _world.Db.FinancialCycles.FirstAsync(c => c.Id == cycle.Id);
        row.Status = FinancialCycleStatus.Issued;
        row.IssuedAt = Now.AddDays(2);
        await _world.Db.SaveChangesAsync();

        Assert.Equal(0, (await _world.Reminders.RunOnceAsync(Now.AddDays(3))).Created);
    }

    // -------------------------------------------------------------------------------------------
    // Backup.
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task WithNoBackupAtAll_TheBackupReminderFiresOnceADay()
    {
        using var world = new DailyShellWorld(Now);

        var today = await world.Reminders.RunOnceAsync(Now);
        var todayAgain = await world.Reminders.RunOnceAsync(Now.AddHours(6));
        var tomorrow = await world.Reminders.RunOnceAsync(Now.AddDays(1));

        Assert.Equal(1, today.Created);
        Assert.Equal(0, todayAgain.Created);
        Assert.Equal(1, tomorrow.Created);
        var raised = await world.Db.Notifications.Where(n => n.Kind == NotificationKinds.Backup).OrderBy(n => n.CreatedAt).FirstAsync();
        Assert.Equal("لم تُؤخذ نسخة احتياطية بعد", raised.Body);
    }

    [Fact]
    public async Task ARecentBackup_SilencesTheBackupReminder()
    {
        // The constructor already wrote today's backup.
        Assert.Equal(0, (await _world.Reminders.RunOnceAsync(Now)).Created);
    }

    [Fact]
    public async Task AnOldBackup_RaisesTheReminderWithItsAgeInArabic()
    {
        using var world = new DailyShellWorld(Now);
        world.Db.Backups.Add(new Backup { At = Now.AddDays(-9), FilePath = "b", Size = 1, AppVersion = "0.21.0" });
        await world.Db.SaveChangesAsync();

        Assert.Equal(1, (await world.Reminders.RunOnceAsync(Now)).Created);
        var raised = await world.Db.Notifications.SingleAsync(n => n.Kind == NotificationKinds.Backup);
        Assert.Equal("مضى 9 أيام على آخر نسخة احتياطية", raised.Body);
    }

    // -------------------------------------------------------------------------------------------
    // The tick abstraction.
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheSchedulerRunsOnEveryTickOfItsTicker_AndStopsWhenItIsStopped()
    {
        using var ticker = new ManualMinuteTicker();
        _world.AddWithStamps(Meeting("اجتماع", Now.AddMinutes(15), reminderMinutes: null));

        _world.Reminders.Start(ticker, BackgroundPass.Inline);
        Assert.True(ticker.Started);

        await ticker.TickAsync(Now.AddMinutes(-5));
        Assert.Equal(0, await _world.Db.Notifications.CountAsync(n => n.Kind == NotificationKinds.Meeting));

        await ticker.TickAsync(Now);
        Assert.Equal(1, await _world.Db.Notifications.CountAsync(n => n.Kind == NotificationKinds.Meeting));

        _world.Reminders.Stop();
        Assert.False(ticker.Started);
        Assert.Equal(1, ticker.StopCount);
    }

    [Fact]
    public async Task EveryPassRunsThroughTheDispatcher_SoItNeverTouchesTheSessionOnTheTimersThread()
    {
        using var ticker = new ManualMinuteTicker();
        _world.AddWithStamps(Meeting("اجتماع", Now.AddMinutes(15), reminderMinutes: null));

        var passes = 0;
        _world.Reminders.Start(ticker, pass =>
        {
            passes++;
            return pass();
        });

        await ticker.TickAsync(Now);

        Assert.Equal(1, passes);
        Assert.Equal(1, await _world.Db.Notifications.CountAsync(n => n.Kind == NotificationKinds.Meeting));
        _world.Reminders.Stop();
    }

    [Fact]
    public void Start_RefusesAMissingDispatcher_RatherThanQuietlyUsingTheTimersThread()
    {
        using var ticker = new ManualMinuteTicker();
        Assert.Throws<ArgumentNullException>(() => _world.Reminders.Start(ticker, null!));
    }

    [Fact]
    public async Task APassAndAScreensQuery_ShareTheSessionSafelyWhenBothGoThroughTheShellsDispatcher()
    {
        // This is the shape the shell runs in: one database session, screens querying it, and a
        // minute pass writing to it. Both go through the same dispatcher — in Blazor that is the
        // renderer's InvokeAsync — so they take turns instead of colliding on one context.
        using var gate = new SemaphoreSlim(1, 1);
        async Task Dispatcher(Func<Task> work)
        {
            await gate.WaitAsync();
            try
            {
                await work();
            }
            finally
            {
                gate.Release();
            }
        }

        for (var i = 0; i < 40; i++)
        {
            _world.AddWithStamps(Meeting($"اجتماع {i}", Now.AddMinutes(15 + i), reminderMinutes: 15 + i));
        }

        using var ticker = new ManualMinuteTicker();
        _world.Reminders.Start(ticker, Dispatcher);

        var passes = Task.Run(async () =>
        {
            for (var i = 0; i < 20; i++)
            {
                await ticker.TickAsync(Now.AddMinutes(i));
            }
        });

        var screen = Task.Run(async () =>
        {
            for (var i = 0; i < 20; i++)
            {
                await Dispatcher(async () =>
                {
                    await _world.Db.Meetings.AsNoTracking().CountAsync();
                    await _world.Db.Notifications.AsNoTracking().Take(20).ToListAsync();
                });
            }
        });

        // The assertion is that neither task throws: a second operation on the same context
        // would fail here, and the swallowing catch inside the ticker is bypassed because the
        // test drives the tick directly.
        await Task.WhenAll(passes, screen);
        _world.Reminders.Stop();

        Assert.True(await _world.Db.Notifications.AnyAsync(n => n.Kind == NotificationKinds.Meeting));
    }

    [Fact]
    public async Task ACreatedReminder_TellsTheBadgeServiceItsNumbersAreStale()
    {
        var stale = 0;
        _world.Badges.Changed += (_, e) =>
        {
            if (e.IsStale)
            {
                stale++;
            }
        };
        _world.AddWithStamps(Meeting("اجتماع", Now.AddMinutes(15), reminderMinutes: null));

        await _world.Reminders.RunOnceAsync(Now);
        Assert.Equal(1, stale);

        // A pass that creates nothing does not churn the shell.
        await _world.Reminders.RunOnceAsync(Now);
        Assert.Equal(1, stale);
    }

    [Fact]
    public async Task APassLeavesEveryNotificationThatIsNotAnOldDismissedOne_Alone()
    {
        // The table is never pruned by anything else — a dismissed reminder's key is what keeps
        // the reminder idempotent — so the pass drops only the rows past the retention floor,
        // where no pass can still be looking (Lookback is far shorter). Everything the user has
        // not dismissed stays, at any age: the bell is the user's to empty, not the scheduler's.
        var floor = Now - ReminderScheduler.KeyRetention;
        _world.Db.Notifications.AddRange(
            new Data.Entities.Notification { Kind = "reminder", Title = "قديم ومصروف", CreatedAt = floor.AddDays(-1), DismissedAt = floor.AddDays(-1), Source = "reminder:old" },
            new Data.Entities.Notification { Kind = "reminder", Title = "قديم وغير مصروف", CreatedAt = floor.AddDays(-1), Source = "reminder:kept" },
            new Data.Entities.Notification { Kind = "reminder", Title = "حديث ومصروف", CreatedAt = Now.AddDays(-1), DismissedAt = Now, Source = "reminder:recent" });
        await _world.Db.SaveChangesAsync();

        await _world.Reminders.RunOnceAsync(Now);

        var titles = _world.Db.Notifications.Select(n => n.Title).ToList();
        Assert.DoesNotContain("قديم ومصروف", titles);
        Assert.Contains("قديم وغير مصروف", titles);
        Assert.Contains("حديث ومصروف", titles);
    }

    [Fact]
    public async Task ANonReminderNotificationSharingAnEventsInstant_DoesNotSuppressTheReminder()
    {
        // The pass loads only sources under the reminder prefix. A quick-capture or sync row can
        // never collide with a reminder key, so narrowing the read must not change any outcome.
        var starts = Now.AddMinutes(15);
        _world.AddWithStamps(Meeting("اجتماع الإدارة", starts, reminderMinutes: 15));
        _world.Db.Notifications.Add(new Data.Entities.Notification
        {
            Kind = "quick_capture",
            Title = "التقاط سريع",
            CreatedAt = Now.AddMinutes(-1),
            Source = "quick-capture:whatever",
        });
        await _world.Db.SaveChangesAsync();

        var run = await _world.Reminders.RunOnceAsync(Now);

        Assert.Equal(1, run.Created);
    }

    private static Meeting Meeting(string title, DateTime startsAt, int? reminderMinutes) => new()
    {
        Title = title,
        StartsAt = startsAt,
        DurationMin = 60,
        Status = MeetingStatus.Planned,
        ReminderMinutes = reminderMinutes,
    };

    private static FinancialCycle Cycle(string name, int endsInDays) => new()
    {
        NameAr = name,
        StartDate = Now.Date.AddDays(endsInDays - 29),
        EndDate = Now.Date.AddDays(endsInDays),
        Status = FinancialCycleStatus.Open,
    };

    private async Task<Data.Entities.Notification> Single(string kind) =>
        await _world.Db.Notifications.SingleAsync(n => n.Kind == kind);
}
