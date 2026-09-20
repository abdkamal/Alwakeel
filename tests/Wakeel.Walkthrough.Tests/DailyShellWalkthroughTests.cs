using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using Wakeel.Core.Services;

namespace Wakeel.Walkthrough.Tests;

/// <summary>
/// One office's daily work, driven end to end through <see cref="Wakeel.UI.Services.Shell.ShellServices"/>
/// — the exact class the real shell resolves — over a temporary activated installation reused from
/// <see cref="FirstRunWorld"/>. This is B2-daily-shell.md's «القبول» walkthrough: overdue, near-due,
/// stale and pending-confirmation records land in the right attention buckets and badges; a meeting
/// reminder fires in the bell at its exact minute, both at the settings default and at a per-meeting
/// override (AGREEMENT item 56); the clock guard blocks official numbering the instant the clock is
/// set back and releases it the instant it is corrected; and a quick capture saves immediately, with
/// its undo removing the record again only inside the undo window.
/// </summary>
public sealed class DailyShellWalkthroughTests
{
    [Fact]
    public async Task Attention_counts_and_badges_match_one_record_seeded_in_each_bucket()
    {
        using var world = new FirstRunWorld();
        await ActivateAsync(world);
        var shell = world.Shell;
        Assert.True(shell.IsOpen);

        var db = world.Session.Db;
        var thresholds = await shell.Attention.GetThresholdsAsync();
        var now = world.Time.GetUtcNow().UtcDateTime;

        // One task in each of the three date-driven buckets (AGREEMENT item 23): overdue past the
        // late threshold, due within the near threshold, and stale — open, no due date, untouched
        // well past the stale threshold.
        using (db.SuppressAuditStamps())
        {
            db.Tasks.Add(new TaskItem
            {
                Title = "مهمة متأخرة عن كشف مصروفات أغسطس",
                DueAt = now.AddDays(-(thresholds.LateDays + 1)),
                Status = WorkTaskStatus.Open,
                Priority = TaskPriority.Normal,
                CreatedAt = now.AddDays(-10),
                UpdatedAt = now.AddDays(-10),
            });
            db.Tasks.Add(new TaskItem
            {
                Title = "مهمة قريبة الاستحقاق لمتابعة الاتفاقية",
                DueAt = now.AddDays(thresholds.NearDays),
                Status = WorkTaskStatus.Open,
                Priority = TaskPriority.Normal,
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.Tasks.Add(new TaskItem
            {
                Title = "مهمة راكدة بلا استحقاق منذ مدة طويلة",
                DueAt = null,
                Status = WorkTaskStatus.Open,
                Priority = TaskPriority.Normal,
                CreatedAt = now.AddDays(-(thresholds.StaleDays + 30)),
                UpdatedAt = now.AddDays(-(thresholds.StaleDays + 30)),
            });
            db.SaveChanges();
        }

        // Two phone expenses awaiting the user's own confirmation (AGREEMENT item 50) — a device
        // has to exist to hold them, the same shape HostDataFolderSeed uses for the host preview.
        var phone = new Device
        {
            DeviceNo = 2,
            EmployeeNo = 2,
            EmployeeName = "سامر أبو غزالة",
            Role = InstallationRole.Secretary,
            Kind = DeviceKind.Phone,
            IssuedAt = now.AddMonths(-6),
        };
        db.Devices.Add(phone);
        db.SaveChanges();

        using (db.SuppressAuditStamps())
        {
            db.PhoneExpenses.Add(new PhoneExpense
            {
                PhoneDeviceId = phone.Id,
                Amount = 1500,
                Purpose = "اتصالات دولية",
                At = now.AddHours(-3),
                Status = PhoneExpenseStatus.Pending,
                CreatedAt = now.AddHours(-3),
                UpdatedAt = now.AddHours(-3),
            });
            db.PhoneExpenses.Add(new PhoneExpense
            {
                PhoneDeviceId = phone.Id,
                Amount = 900,
                Purpose = "رصيد شحن",
                At = now.AddHours(-1),
                Status = PhoneExpenseStatus.Pending,
                CreatedAt = now.AddHours(-1),
                UpdatedAt = now.AddHours(-1),
            });
            db.SaveChanges();
        }

        // ---- AttentionService: the four KPI counts (W08) -----------------------------------------
        var counts = await shell.Attention.GetCountsAsync(now);
        Assert.Equal(1, counts.Late);
        Assert.Equal(1, counts.Near);
        Assert.Equal(1, counts.Stale);
        Assert.Equal(2, counts.PendingConfirmation);
        Assert.Equal(5, counts.Total);

        // ---- Same counts, read the way W09's tab badges read them ------------------------------
        var tabCounts = await shell.Badges.GetTabCountsAsync(BadgeScreens.Attention, now);
        Assert.Equal(1, tabCounts[BadgeTabs.Late]);
        Assert.Equal(1, tabCounts[BadgeTabs.Near]);
        Assert.Equal(1, tabCounts[BadgeTabs.Stale]);
        Assert.Equal(2, tabCounts[BadgeTabs.PendingConfirmation]);

        // ---- BadgeService: no record counted twice (AGREEMENT item 26) --------------------------
        var badges = await shell.Badges.RefreshAsync(now);
        Assert.Equal(5, badges.Items[BadgeKeys.Attention]);
        Assert.Equal(3, badges.Items[BadgeKeys.Tasks]);
        Assert.Equal(2, badges.Items[BadgeKeys.Finance]);

        // The daily-work group's badge is the UNION of its items' record sets, not their sum: with
        // every one of the five records already inside the attention set, the group reads exactly
        // the same five, not 3 (tasks) + 2 (finance) + 5 (attention) = 10.
        Assert.Equal(5, badges.Groups[BadgeKeys.GroupDailyWork]);
    }

    [Fact]
    public async Task A_meeting_reminder_fires_in_the_bell_at_its_exact_minute_at_the_default_and_at_an_override()
    {
        using var world = new FirstRunWorld();
        await ActivateAsync(world);
        var shell = world.Shell;
        var db = world.Session.Db;

        // A recent backup, so the scheduler's own backup reminder (which would otherwise fire on
        // every pass of the day — nothing ever backed up this installation up) does not show up as
        // noise in the "nothing due yet" assertion below.
        db.Backups.Add(new Backup { At = world.Time.GetUtcNow().UtcDateTime, FilePath = "-", Size = 1, AppVersion = "0.21" });
        db.SaveChanges();

        var baseline = world.Time.GetUtcNow().UtcDateTime;
        var defaultMinutes = await shell.Settings.GetMeetingReminderMinutesAsync();

        // Meeting 1 carries no reminder of its own: it fires at the settings DEFAULT (item 56).
        var defaultMeeting = new Meeting
        {
            Title = "لجنة المشتريات — عطاء صيانة المركبات",
            StartsAt = baseline.AddMinutes(20),
            DurationMin = 30,
            Location = "قاعة الاجتماعات",
            Status = MeetingStatus.Planned,
            ReminderMinutes = null,
        };

        // Meeting 2 carries its OWN reminder lead time, different from the default (item 56).
        var overriddenMeeting = new Meeting
        {
            Title = "متابعة اتفاقية الصرف الصحي",
            StartsAt = baseline.AddMinutes(50),
            DurationMin = 45,
            Location = "بلدية رام الله",
            Status = MeetingStatus.Planned,
            ReminderMinutes = 40,
        };

        db.Meetings.Add(defaultMeeting);
        db.Meetings.Add(overriddenMeeting);
        db.SaveChanges();

        var defaultFireAt = defaultMeeting.StartsAt.AddMinutes(-defaultMinutes);
        var overrideFireAt = overriddenMeeting.StartsAt.AddMinutes(-overriddenMeeting.ReminderMinutes!.Value);
        Assert.NotEqual(defaultFireAt, overrideFireAt);

        // One minute before the earlier of the two fire instants: neither reminder is due yet.
        var early = await shell.Reminders.RunOnceAsync(defaultFireAt.AddMinutes(-1));
        Assert.Equal(0, early.Created);

        // The default reminder fires at its EXACT minute; the override has not come due yet.
        var atDefault = await shell.Reminders.RunOnceAsync(defaultFireAt);
        Assert.Equal(1, atDefault.Created);

        var panelAfterDefault = await shell.Notifications.GetPanelAsync(defaultFireAt);
        var rowsAfterDefault = panelAfterDefault.Groups.SelectMany(g => g.Value).ToList();
        Assert.Contains(rowsAfterDefault, n => n.EntityId == defaultMeeting.Id && n.Kind == NotificationKinds.Meeting);
        Assert.DoesNotContain(rowsAfterDefault, n => n.EntityId == overriddenMeeting.Id);

        // Re-running the very same minute raises nothing a second time — the scheduler's
        // idempotency key, not merely "there happens to be one row already".
        var repeat = await shell.Reminders.RunOnceAsync(defaultFireAt);
        Assert.Equal(0, repeat.Created);
        Assert.Equal(1, repeat.Skipped);

        // The override fires at ITS OWN exact minute, forty minutes before start rather than
        // fifteen — the per-meeting lead time winning over the settings default.
        var atOverride = await shell.Reminders.RunOnceAsync(overrideFireAt);
        Assert.Equal(1, atOverride.Created);

        var panelAfterOverride = await shell.Notifications.GetPanelAsync(overrideFireAt);
        var rowsAfterOverride = panelAfterOverride.Groups.SelectMany(g => g.Value).ToList();
        Assert.Contains(rowsAfterOverride, n => n.EntityId == overriddenMeeting.Id && n.Kind == NotificationKinds.Meeting);
        Assert.Equal(2, panelAfterOverride.Total);
    }

    [Fact]
    public async Task The_clock_guard_blocks_numbering_the_instant_the_clock_is_set_back_and_releases_it_after_correction()
    {
        using var world = new FirstRunWorld();
        await ActivateAsync(world);
        var shell = world.Shell;

        var baseline = world.Time.GetUtcNow().UtcDateTime.AddHours(1);
        var ok = await shell.Clock.CheckAsync(ClockCheckTrigger.Manual, baseline);
        Assert.Equal(ClockVerdict.Ok, ok.Verdict);
        Assert.False(shell.Clock.NumberingBlocked);
        Assert.False(shell.Clock.State.Visible);
        shell.Clock.EnsureNumberingAllowed();

        // The clock is turned back past the tolerance the check allows against the last recorded
        // activity — exactly what "the clock is set back" means to the check.
        var setBack = baseline - Wakeel.Core.Services.ClockCheckService.BackwardTolerance - TimeSpan.FromMinutes(1);
        var bad = await shell.Clock.CheckAsync(ClockCheckTrigger.Manual, setBack);
        Assert.NotEqual(ClockVerdict.Ok, bad.Verdict);
        Assert.True(shell.Clock.NumberingBlocked);
        Assert.True(shell.Clock.State.Visible);
        Assert.Throws<InvalidOperationException>(shell.Clock.EnsureNumberingAllowed);

        // Corrected: the clock moves forward again, past the activity it was found behind.
        var corrected = baseline.AddHours(1);
        var fixedCheck = await shell.Clock.CheckAsync(ClockCheckTrigger.Manual, corrected);
        Assert.Equal(ClockVerdict.Ok, fixedCheck.Verdict);
        Assert.False(shell.Clock.NumberingBlocked);
        Assert.False(shell.Clock.State.Visible);
        shell.Clock.EnsureNumberingAllowed();
    }

    [Fact]
    public async Task Quick_capture_saves_immediately_and_its_undo_removes_it_only_inside_the_undo_window()
    {
        using var world = new FirstRunWorld();
        await ActivateAsync(world);
        var shell = world.Shell;
        var db = world.Session.Db;

        var captured = await shell.QuickCapture.CaptureTaskAsync("مهمة من الإدخال السريع", assigneeAr: "أحمد الخطيب");
        Assert.False(string.IsNullOrWhiteSpace(captured.MessageAr));

        var saved = await db.Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == captured.Id);
        Assert.NotNull(saved);
        Assert.Equal("مهمة من الإدخال السريع", saved!.Title);

        var undone = await shell.QuickCapture.UndoAsync(captured.UndoToken);
        Assert.True(undone);

        // Soft-deleted (DATA-MODEL.md §0 — never physically removed) and so invisible to the same
        // query the moment before, because every query goes through the soft-delete filter.
        var afterUndo = await db.Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == captured.Id);
        Assert.Null(afterUndo);

        // The same token cannot undo twice.
        Assert.False(await shell.QuickCapture.UndoAsync(captured.UndoToken));

        // A second capture whose undo token has expired by the time it is used is refused, and the
        // record it made is left exactly as it was — undo is a grace window, not a standing power.
        var second = await shell.QuickCapture.CaptureTaskAsync("مهمة ثانية بلا نافذة تراجع");
        world.Time.Advance(IQuickCaptureService.UndoWindow + TimeSpan.FromMinutes(1));

        Assert.False(await shell.QuickCapture.UndoAsync(second.UndoToken));

        var stillThere = await db.Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == second.Id);
        Assert.NotNull(stillThere);
    }

    /// <summary>Runs W02–W04 (reused from <see cref="FirstRunWalkthroughTests"/>'s own helper) and opens the session this package's scenario runs over.</summary>
    private static async Task ActivateAsync(FirstRunWorld world)
    {
        await world.ChooseAsync(world.Export());
        world.Inspection.Inspect(world.PackagePasswordText);
        Assert.True(world.Inspection.IsAcceptable);

        world.Activation.Begin();
        var result = await world.Activation.ActivateAsync(FirstRunWorld.FirstPassword);
        Assert.True(result.Succeeded);
    }
}
