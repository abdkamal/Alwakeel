using Microsoft.Extensions.DependencyInjection;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using Wakeel.Core.Services;
using Wakeel.UI.Services.Account;
using Wakeel.UI.Tests.FirstRun;

namespace Wakeel.UI.Tests.Shell;

/// <summary>
/// Prepares a data folder the desktop host can be launched on, so the eight daily screens can be
/// LOOKED at in the real window instead of only in a renderer. It is not a test of anything: it runs
/// only when <c>WAKEEL_HOST_ROOT</c> names a folder, and is skipped in every ordinary run of the
/// suite.
/// </summary>
/// <remarks>
/// Run it with:
/// <code>
/// set WAKEEL_HOST_ROOT=&lt;folder&gt;
/// dotnet test tests/Wakeel.UI.Tests --no-build --filter FullyQualifiedName~HostDataFolderSeed
/// </code>
/// then start the host with <c>--data-folder=&lt;folder&gt;</c> and sign in with
/// <see cref="Password"/>. The derivation cost is left at its default, because the host reads the
/// cost back out of the key file the activation wrote and a cheapened one would be a different
/// installation from the shipped shape.
/// </remarks>
public sealed class HostDataFolderSeed
{
    /// <summary>The account password the seeded installation is opened with.</summary>
    public const string Password = "كلمة-المرور-للعرض-2026";

    /// <summary>The instant the seeded records are arranged around.</summary>
    private static readonly DateTime Now = DateTime.UtcNow;

    /// <summary>Today as the screens read it, so a seeded meeting really is one of today's.</summary>
    private static readonly DateTime LocalToday = DateTime.Now.Date;

    [Fact]
    public async Task Activate_a_data_folder_for_the_host()
    {
        var root = Environment.GetEnvironmentVariable("WAKEEL_HOST_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            // Not a host run. Rather than pass on nothing, it checks the one precondition a host run
            // needs and cannot recover from: the shared setup package this folder is activated from.
            Assert.True(
                File.Exists(SharedSetupFile.Path),
                "The shared setup package a host data folder is activated from is missing.");
            return;
        }

        var paths = WakeelPaths.ForRoot(root!);
        paths.EnsureDirectories();

        var services = new ServiceCollection();
        services.AddWakeelCore(options => options.Paths = paths);
        services.AddWakeelAccount();
        await using var provider = services.BuildServiceProvider();

        var inspection = provider.GetRequiredService<SetupInspectionService>();
        await using (var stream = File.OpenRead(SharedSetupFile.Path))
        {
            await inspection.StageAsync(stream, Path.GetFileName(SharedSetupFile.Path), SharedSetupFile.ExportedAt);
        }

        inspection.Inspect(SharedSetupFile.Password);

        var activation = provider.GetRequiredService<ActivationService>();
        activation.Begin();
        await activation.ActivateAsync(Password);

        var profiles = provider.GetRequiredService<SignInProfileStore>();
        profiles.Save(new SignInProfile
        {
            OrgName = SharedSetupFile.OrgName,
            OfficeName = SharedSetupFile.OfficeName,
            OfficeCode = SharedSetupFile.OfficeCode,
            EmployeeName = SharedSetupFile.EmployeeName,
            JobTitle = SharedSetupFile.JobTitle,
            EmployeeNo = 2,
            DeviceNo = 1,
            Logo = SharedSetupFile.OnePixelPng,
        });

        var session = provider.GetRequiredService<AccountSession>();
        Assert.True(session.IsOpen);

        Seed(session.Db);
        await SeedNotificationsAsync(provider, session.Db);

        if (Environment.GetEnvironmentVariable("WAKEEL_HOST_SKEW_CLOCK") is { Length: > 0 })
        {
            SkewClock(session.Db);
        }

        session.SignOut();
    }

    /// <summary>
    /// Leaves behind a recorded activity dated in the future, which is exactly what a machine whose
    /// clock has been turned back looks like to the clock check. It is the only way to photograph
    /// the clock banner in the running host without touching the machine's own clock, so it is
    /// opt-in through <c>WAKEEL_HOST_SKEW_CLOCK</c> and never part of an ordinary seed.
    /// </summary>
    private static void SkewClock(WakeelDb db)
    {
        using (db.SuppressAuditStamps())
        {
            db.ClockChecks.Add(new ClockCheck { At = Now.AddDays(2), Verdict = ClockVerdict.Ok });
            db.SaveChanges();
        }
    }

    /// <summary>
    /// A day's worth of work: two overdue records, one due tomorrow, one untouched for a month,
    /// two meetings later today and two phone expenses waiting to be confirmed.
    /// </summary>
    private static void Seed(WakeelDb db)
    {
        var phone = new Device
        {
            DeviceNo = 2,
            EmployeeNo = 2,
            EmployeeName = "محمد عوض",
            Role = InstallationRole.Secretary,
            Kind = DeviceKind.Phone,
            IssuedAt = Now.AddYears(-1),
            PairedAt = Now.AddMonths(-1),
            LastSyncAt = Now.AddMinutes(-8),
            SyncScope = SyncScope.Full,
        };
        db.Devices.Add(phone);
        db.SaveChanges();

        using (db.SuppressAuditStamps())
        {
            db.AddRange(
                Task("طلب تزويد بيانات مشروع الطريق الدائري", Now.AddDays(-2), Now.AddDays(-2), "سامر أبو غزالة"),
                Task("الرد على استفسار وزارة الحكم المحلي", Now.AddHours(6), Now.AddDays(-1), "ليلى الشريف"),
                Task("تسليم كشف مصروفات أغسطس لدائرة المالية", Now.AddDays(1), Now, "أحمد الخطيب"),
                Task("متابعة اتفاقية الصرف الصحي مع بلدية رام الله", null, Now.AddDays(-32), "نور السالمي"),
                Task("إعداد مذكرة التوصية لعطاء صيانة المركبات", Now.AddDays(20), Now, "خالد الهاشمي"),
                new Meeting
                {
                    Title = "لجنة المشتريات — عطاء صيانة المركبات",
                    StartsAt = LocalToday.AddHours(11).ToUniversalTime(),
                    DurationMin = 60,
                    Location = "قاعة الاجتماعات",
                    Status = MeetingStatus.Planned,
                    CreatedAt = Now.AddDays(-3),
                    UpdatedAt = Now.AddDays(-3),
                },
                new Meeting
                {
                    Title = "متابعة اتفاقية الصرف الصحي",
                    StartsAt = LocalToday.AddHours(14).AddMinutes(30).ToUniversalTime(),
                    DurationMin = 45,
                    Location = "بلدية رام الله",
                    Status = MeetingStatus.Planned,
                    CreatedAt = Now.AddDays(-4),
                    UpdatedAt = Now.AddDays(-4),
                },
                Expense(phone.Id, "اتصالات دولية — مناقصة الطريق", 4250, "فاتورة جوال 0599-482-117"),
                Expense(phone.Id, "رصيد شحن — هاتف المكتب الأرضي", 2500, "إيصال شحن 09-2381-440"));

            db.SaveChanges();
        }
    }

    private static async Task SeedNotificationsAsync(IServiceProvider provider, WakeelDb db)
    {
        var notifications = new NotificationService(
            db,
            new SettingsService(db, provider.GetRequiredService<IClock>()),
            provider.GetRequiredService<IIdGenerator>(),
            provider.GetRequiredService<IClock>());

        var meeting = db.Meetings.OrderBy(m => m.StartsAt).First();
        await notifications.CreateAsync(
            NotificationKinds.Meeting,
            "اجتماع قريب: " + meeting.Title,
            "قاعة الاجتماعات · تبدأ الساعة 11:00",
            "meetings",
            meeting.Id,
            createdAt: Now.AddMinutes(-25));

        await notifications.CreateAsync(
            NotificationKinds.FinancialCycle,
            "بقي 3 أيام على نهاية الدورة المالية",
            "أنجز بنود التقرير الشهري قبل الإغلاق",
            createdAt: Now.AddHours(-3));

        var late = db.Tasks.First(t => t.DueAt < Now);
        await notifications.CreateAsync(
            NotificationKinds.TaskDue,
            "مهمة متأخرة: " + late.Title,
            "المكلّف: سامر أبو غزالة",
            "tasks",
            late.Id,
            createdAt: Now.AddHours(-1));

        var backup = await notifications.CreateAsync(
            NotificationKinds.Backup,
            "لم تُؤخذ نسخة احتياطية منذ يومين",
            createdAt: Now.AddDays(-1));

        // One read row, so the panel's «الكل» and «غير المقروء» tabs cannot be the same number.
        await notifications.MarkReadAsync(backup.Id, Now);
    }

    private static TaskItem Task(string title, DateTime? due, DateTime updated, string assignee) => new()
    {
        Title = title,
        DueAt = due,
        AssigneeName = assignee,
        Status = WorkTaskStatus.Open,
        Priority = TaskPriority.Normal,
        CreatedAt = updated.AddDays(-1),
        UpdatedAt = updated,
    };

    private static PhoneExpense Expense(Guid deviceId, string purpose, long amount, string note) => new()
    {
        PhoneDeviceId = deviceId,
        Amount = amount,
        Purpose = purpose,
        Note = note,
        At = Now.AddDays(-1),
        Status = PhoneExpenseStatus.Pending,
        CreatedAt = Now.AddDays(-1),
        UpdatedAt = Now.AddDays(-1),
    };
}
