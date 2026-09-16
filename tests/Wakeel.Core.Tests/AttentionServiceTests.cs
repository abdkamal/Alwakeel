using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using Wakeel.Core.Services;

namespace Wakeel.Core.Tests;

/// <summary>
/// The four attention indicators on crafted data (AGREEMENT item 23): the thresholds come from
/// settings, the buckets never overlap, and a settled record never surfaces.
/// </summary>
public sealed class AttentionServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 16, 9, 0, 0, DateTimeKind.Utc);

    private readonly DailyShellWorld _world = new(Now);

    public void Dispose() => _world.Dispose();

    [Fact]
    public async Task Thresholds_ComeFromSettings_WithTheAgreementDefaults()
    {
        var defaults = await _world.Attention.GetThresholdsAsync();
        Assert.Equal(SettingsService.DefaultAttentionLateDays, defaults.LateDays);
        Assert.Equal(SettingsService.DefaultAttentionNearDays, defaults.NearDays);
        Assert.Equal(SettingsService.DefaultAttentionStaleDays, defaults.StaleDays);

        await _world.Settings.SetAsync(SettingKeys.AttentionNearDays, 10);
        var changed = await _world.Attention.GetThresholdsAsync();
        Assert.Equal(10, changed.NearDays);
    }

    [Fact]
    public async Task Counts_ClassifyCraftedRowsIntoTheFourBuckets()
    {
        _world.AddWithStamps(
            OpenTask("مهمة متأخرة", due: Now.AddDays(-3)),
            OpenTask("مهمة قريبة", due: Now.AddDays(2)),
            OpenTask("مهمة راكدة", due: null, updated: Now.AddDays(-20)),
            OpenTask("مهمة مستقرة", due: Now.AddDays(30)),
            PendingExpense("مصروف بانتظار التأكيد"));

        var counts = await _world.Attention.GetCountsAsync(Now);

        Assert.Equal(1, counts.Late);
        Assert.Equal(1, counts.Near);
        Assert.Equal(1, counts.Stale);
        Assert.Equal(1, counts.PendingConfirmation);
        Assert.Equal(4, counts.Total);
    }

    [Fact]
    public async Task ARecordThatIsBothOverdueAndUntouched_IsCountedOnceAsLate()
    {
        // Without mutually-exclusive buckets this row would be both "متأخر" and "راكد", the four
        // KPI numbers would not add up to the list beneath them, and it would appear on two W09 tabs.
        _world.AddWithStamps(OpenTask("متأخرة ومهملة", due: Now.AddDays(-9), updated: Now.AddDays(-40)));

        var counts = await _world.Attention.GetCountsAsync(Now);
        Assert.Equal(1, counts.Late);
        Assert.Equal(0, counts.Stale);
        Assert.Equal(1, counts.Total);

        var late = await _world.Attention.GetBucketAsync(AttentionBucket.Late, Now);
        var stale = await _world.Attention.GetBucketAsync(AttentionBucket.Stale, Now);
        Assert.Single(late);
        Assert.Empty(stale);
    }

    [Fact]
    public async Task RaisingTheNearThreshold_MovesARowFromNothingIntoNear()
    {
        _world.AddWithStamps(OpenTask("مهمة بعد أسبوع", due: Now.AddDays(6)));

        Assert.Equal(0, (await _world.Attention.GetCountsAsync(Now)).Near);

        await _world.Settings.SetAsync(SettingKeys.AttentionNearDays, 7);

        Assert.Equal(1, (await _world.Attention.GetCountsAsync(Now)).Near);
    }

    [Fact]
    public async Task RaisingTheStaleThreshold_TakesARowOutOfStale()
    {
        _world.AddWithStamps(OpenTask("بلا حراك", due: null, updated: Now.AddDays(-10)));

        Assert.Equal(1, (await _world.Attention.GetCountsAsync(Now)).Stale);

        await _world.Settings.SetAsync(SettingKeys.AttentionStaleDays, 30);

        Assert.Equal(0, (await _world.Attention.GetCountsAsync(Now)).Stale);
    }

    [Fact]
    public async Task ADueDateToday_IsNearNotLate_WithTheDefaultOneDayLateThreshold()
    {
        _world.AddWithStamps(OpenTask("مستحقة اليوم", due: Now.Date.AddHours(8)));

        var counts = await _world.Attention.GetCountsAsync(Now);
        Assert.Equal(0, counts.Late);
        Assert.Equal(1, counts.Near);
    }

    [Fact]
    public async Task SoftDeletedAndSettledRecords_NeverSurface()
    {
        var deleted = OpenTask("مهمة محذوفة", due: Now.AddDays(-5));
        deleted.DeletedAt = Now.AddDays(-1);
        var done = OpenTask("مهمة منجزة", due: Now.AddDays(-5));
        done.Status = WorkTaskStatus.Done;
        var paid = new Commitment
        {
            Title = "التزام مسدد",
            Amount = 5000,
            Status = CommitmentStatus.Paid,
            DueAt = Now.AddDays(-5),
            UpdatedAt = Now.AddHours(-1),
        };
        _world.AddWithStamps(deleted, done, paid);

        var counts = await _world.Attention.GetCountsAsync(Now);
        Assert.Equal(0, counts.Total);
    }

    [Fact]
    public async Task EveryTrackedTable_ContributesToTheLateBucket()
    {
        _world.AddWithStamps(
            new Correspondence { Subject = "وارد متأخر", Direction = InOutDirection.In, Status = CorrespondenceStatus.InProgress, DueAt = Now.AddDays(-2), UpdatedAt = Now.AddHours(-1) },
            OpenTask("مهمة متأخرة", due: Now.AddDays(-2)),
            new Commitment { Title = "التزام متأخر", Amount = 1000, Status = CommitmentStatus.Open, DueAt = Now.AddDays(-2), UpdatedAt = Now.AddHours(-1) },
            new Case { CaseNumber = "ق/1", Title = "قضية متأخرة", Status = CaseStatus.Open, NextHearingAt = Now.AddDays(-2), UpdatedAt = Now.AddHours(-1) },
            new Decision { Text = "قرار متأخر", Status = DecisionStatus.InProgress, DueAt = Now.AddDays(-2), UpdatedAt = Now.AddHours(-1), DecidedAt = Now.AddDays(-10) });

        var late = await _world.Attention.GetBucketAsync(AttentionBucket.Late, Now);

        Assert.Equal(5, late.Count);
        Assert.Equal(
            new[]
            {
                AttentionEntityKind.Case,
                AttentionEntityKind.Commitment,
                AttentionEntityKind.CorrespondenceIn,
                AttentionEntityKind.Decision,
                AttentionEntityKind.Task,
            },
            late.Select(i => i.Kind).OrderBy(k => k.ToString(), StringComparer.Ordinal));
    }

    [Fact]
    public async Task ACommitmentAndACase_CarryTheirPartysName_SoTheGihaColumnIsNotBlank()
    {
        // «الجهة» is a column of both the W08 worklist and the W09 tabs. Commitments and cases
        // reference the party by id rather than carrying a name snapshot, so the column was
        // permanently empty for every one of their rows until the id was resolved.
        var party = new Party { Name = "وزارة الحكم المحلي", Kind = PartyKind.Ministry };
        _world.AddWithStamps(party);

        _world.AddWithStamps(
            new Commitment { Title = "التزام متأخر", Amount = 1000, Status = CommitmentStatus.Open, PartyId = party.Id, DueAt = Now.AddDays(-2), UpdatedAt = Now.AddHours(-1) },
            new Case { CaseNumber = "ق/1", Title = "قضية متأخرة", Status = CaseStatus.Open, PartyId = party.Id, NextHearingAt = Now.AddDays(-2), UpdatedAt = Now.AddHours(-1) },
            new Commitment { Title = "التزام بلا جهة", Amount = 500, Status = CommitmentStatus.Open, DueAt = Now.AddDays(-2), UpdatedAt = Now.AddHours(-1) });

        var late = await _world.Attention.GetBucketAsync(AttentionBucket.Late, Now);

        Assert.Equal("وزارة الحكم المحلي", late.Single(i => i.Kind == AttentionEntityKind.Commitment && i.TitleAr == "التزام متأخر").PartyAr);
        Assert.Equal("وزارة الحكم المحلي", late.Single(i => i.Kind == AttentionEntityKind.Case).PartyAr);

        // A row with no party keeps an empty column rather than an invented name.
        Assert.Null(late.Single(i => i.TitleAr == "التزام بلا جهة").PartyAr);
    }

    [Fact]
    public async Task TheBackupAge_IsTheSameEarlyInTheLocalMorningAsItIsLaterTheSameDay()
    {
        // The regression this guards: the age was counted on UTC dates while the KPI cards count
        // on local ones, so in a zone ahead of UTC «مضى 7 أيام» and the overdue flag flipped a few
        // hours later than «متأخر» did on the very same screen.
        var zone = TimeZoneInfo.Local;
        var localToday = TimeZoneInfo.ConvertTimeFromUtc(Now, zone).Date;
        var atOne = ToUtc(localToday.AddHours(1), zone);
        var atNine = ToUtc(localToday.AddHours(9), zone);

        _world.Db.Backups.Add(new Backup
        {
            At = ToUtc(localToday.AddDays(-7).AddHours(20), zone),
            FilePath = "b.wakeel-backup",
            Size = 10,
            AppVersion = "0.21.0",
        });
        await _world.Db.SaveChangesAsync();

        var early = (await _world.Attention.GetSnapshotAsync(atOne)).Backup;
        var later = (await _world.Attention.GetSnapshotAsync(atNine)).Backup;

        Assert.Equal(7, early.DaysSince);
        Assert.Equal(later.DaysSince, early.DaysSince);
        Assert.True(early.Overdue);
        Assert.Equal(later.Overdue, early.Overdue);
    }

    /// <summary>A local wall-clock instant as the UTC value the database stores, skipping a DST gap.</summary>
    private static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddMinutes(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
    }

    [Fact]
    public async Task NeedsActionToday_PutsTheMostOverdueFirstAndTheStaleRowsLast()
    {
        _world.AddWithStamps(
            OpenTask("متأخرة يومين", due: Now.AddDays(-2)),
            OpenTask("متأخرة عشرة أيام", due: Now.AddDays(-10)),
            OpenTask("قريبة", due: Now.AddDays(1)),
            OpenTask("راكدة", due: null, updated: Now.AddDays(-30)),
            PendingExpense("بانتظار التأكيد"));

        var snapshot = await _world.Attention.GetSnapshotAsync(Now);
        var titles = snapshot.NeedsActionToday.Select(i => i.TitleAr).ToList();

        Assert.Equal(
            new[] { "متأخرة عشرة أيام", "متأخرة يومين", "بانتظار التأكيد", "قريبة", "راكدة" },
            titles);
        Assert.Equal(10, snapshot.NeedsActionToday[0].DaysLate);
    }

    [Fact]
    public async Task Snapshot_ReportsTodaysMeetingsLastSyncAndBackupState()
    {
        var localToday = TimeZoneInfo.ConvertTimeFromUtc(Now, TimeZoneInfo.Local).Date;
        var startsLocal = localToday.AddHours(14);
        var startsUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(startsLocal, DateTimeKind.Unspecified), TimeZoneInfo.Local);

        _world.AddWithStamps(
            new Meeting { Title = "اجتماع اليوم", StartsAt = startsUtc, DurationMin = 60, Location = "قاعة الاجتماعات", Status = MeetingStatus.Planned },
            new Meeting { Title = "اجتماع الأسبوع القادم", StartsAt = startsUtc.AddDays(7), DurationMin = 60, Status = MeetingStatus.Planned });

        _world.Db.Devices.Add(new Device { DeviceNo = 1, EmployeeNo = 2, EmployeeName = "أ", Kind = DeviceKind.Pc, LastSyncAt = Now.AddHours(-3) });
        _world.Db.Backups.Add(new Backup { At = Now.AddDays(-2), FilePath = "b.wakeel-backup", Size = 10, AppVersion = "0.21.0" });
        await _world.Db.SaveChangesAsync();

        var snapshot = await _world.Attention.GetSnapshotAsync(Now);

        Assert.Single(snapshot.TodayMeetings);
        Assert.Equal("اجتماع اليوم", snapshot.TodayMeetings[0].TitleAr);

        Assert.Equal(Now.AddHours(-3), snapshot.Sync.LastAt);
        Assert.Contains("آخر مزامنة", snapshot.Sync.MessageAr, StringComparison.Ordinal);

        Assert.False(snapshot.Backup.Overdue);
        Assert.Equal(2, snapshot.Backup.DaysSince);
        Assert.Contains("آخر نسخة احتياطية", snapshot.Backup.MessageAr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Snapshot_IgnoresARevokedDevicesLastSync_LikeTheHealthCentreDoes()
    {
        // W08's «آخر مزامنة» and W12's sync card must not disagree: a laptop the office has
        // revoked is no longer a device the user should be told they are in sync with.
        _world.Db.Devices.Add(new Device
        {
            DeviceNo = 1,
            EmployeeNo = 2,
            EmployeeName = "أ",
            Kind = DeviceKind.Pc,
            LastSyncAt = Now.AddMinutes(-5),
            RevokedAt = Now.AddMinutes(-1),
        });
        _world.Db.Devices.Add(new Device
        {
            DeviceNo = 2,
            EmployeeNo = 3,
            EmployeeName = "ب",
            Kind = DeviceKind.Phone,
            LastSyncAt = Now.AddHours(-6),
        });
        await _world.Db.SaveChangesAsync();

        var snapshot = await _world.Attention.GetSnapshotAsync(Now);

        Assert.Equal(Now.AddHours(-6), snapshot.Sync.LastAt);
    }

    [Fact]
    public async Task Snapshot_OnAnEmptyDatabase_SaysNothingHasSyncedAndNoBackupExists()
    {
        var snapshot = await _world.Attention.GetSnapshotAsync(Now);

        Assert.Equal(0, snapshot.Counts.Total);
        Assert.Empty(snapshot.NeedsActionToday);
        Assert.Empty(snapshot.TodayMeetings);
        Assert.Null(snapshot.Sync.LastAt);
        Assert.Equal("لم تتم مزامنة بعد", snapshot.Sync.MessageAr);
        Assert.True(snapshot.Backup.Overdue);
        Assert.Equal("لم تُؤخذ نسخة احتياطية بعد", snapshot.Backup.MessageAr);
    }

    [Fact]
    public async Task NeedsActionToday_IsCappedSoTheListStaysAWorklist()
    {
        var many = Enumerable.Range(0, AttentionService.NeedsActionLimit + 25)
            .Select(i => (SyncedEntity)OpenTask($"مهمة {i}", due: Now.AddDays(-1 - (i % 5))))
            .ToList();
        _world.AddWithStamps(many);

        var snapshot = await _world.Attention.GetSnapshotAsync(Now);

        Assert.Equal(AttentionService.NeedsActionLimit, snapshot.NeedsActionToday.Count);
        Assert.Equal(many.Count, snapshot.Counts.Total);
    }

    private TaskItem OpenTask(string title, DateTime? due, DateTime? updated = null) => new()
    {
        Title = title,
        Status = WorkTaskStatus.Open,
        DueAt = due,
        UpdatedAt = updated ?? Now.AddHours(-1),
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
