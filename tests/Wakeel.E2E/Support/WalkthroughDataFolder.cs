using Microsoft.Extensions.DependencyInjection;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using Wakeel.Core.Services;
using Wakeel.Crypto;
using Wakeel.UI.Services.Account;
using Wakeel.UI.Services.Shell;

namespace Wakeel.E2E.Support;

/// <summary>
/// Builds a fully activated, seeded installation on disk for Wakeel.Desktop.exe to open with
/// <c>--data-folder=&lt;path&gt;</c> — "the walkthrough helpers" this package's spec asks the
/// screenshot harness to seed the host through (B2-daily-shell.md's «القبول»), adapted for a
/// single automated test process: the org/device identity, the exported <c>.wakeel-setup</c> file,
/// the activation and the daily-shell records are the same shape
/// <c>Wakeel.Walkthrough.Tests.FirstRunWorld</c> and
/// <c>tests/Wakeel.UI.Tests/Shell/HostDataFolderSeed.cs</c> build, written fresh here because
/// neither of those two lives where this package can reference them as a library: the first
/// deletes its own folder on disposal (its very reason for existing — an isolated fixture per
/// in-process test), and the second is an xunit fixture in another package's allowed paths, meant
/// to be run as its own separate <c>dotnet test</c> pass, not called from code.
/// </summary>
/// <remarks>
/// <b>Why the database connection is fully closed before the host ever sees the folder.</b> This
/// runs in the SAME .NET process that goes on to launch Wakeel.Desktop.exe as a child OS process
/// against the very same SQLite file. <see cref="ActivateAsync"/> disposes its
/// <see cref="ServiceProvider"/> (closing the <c>DbSession</c> it built) before returning, and then
/// clears Microsoft.Data.Sqlite's connection pool — SQLite's own multi-process locking is designed
/// for concurrent access from separate processes, but a pooled, merely-"disposed" connection sitting
/// idle in THIS process is an avoidable, gratuitous risk of a file lock the host's own connection
/// would have to wait out. HostDataFolderSeed.cs never needed this because it always ran as a
/// separate process from the host it seeded for.
/// </remarks>
internal static class WalkthroughDataFolder
{
    private const string OrgId = "ORG-1";
    private const string OrgName = "هيئة الشؤون الإدارية";
    private const string OfficeUnitId = "U-UNIT";
    private const string OfficeCode = "OF-01";
    private const string DeviceId = "PC-1";
    private const string EmployeeName = "أحمد الخطيب";
    private const string JobTitle = "سكرتير";
    private const string OfficeName = "وحدة السكرتارية";

    /// <summary>The account password the seeded installation is signed in with over CDP.</summary>
    internal const string Password = "كلمة-المرور-للقطات-E2E-2026";

    private static readonly IReadOnlyList<SetupUnit> Structure =
    [
        new SetupUnit("U-ORG", null, 1, OrgName, "رئيس الهيئة", "سالم العامري", null),
        new SetupUnit("U-DEPT", "U-ORG", 2, "دائرة الشؤون الإدارية", "مدير الدائرة", "خالد الهاشمي", null),
        new SetupUnit("U-SEC", "U-DEPT", 3, "قسم المتابعة", "رئيس القسم", "ليلى المنصوري", null),
        new SetupUnit(OfficeUnitId, "U-SEC", 4, OfficeName, "رئيس الوحدة", "نور السالمي", OfficeCode),
    ];

    private static readonly byte[] Logo =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
        0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41,
        0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
        0x00, 0x03, 0x01, 0x01, 0x00, 0x18, 0xDD, 0x8D,
        0xB0, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
        0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    /// <summary>
    /// Activates a fresh installation at <paramref name="dataFolder"/> and seeds a day's daily-shell
    /// work: one overdue, one near-due and one stale task, two pending phone expenses, two of
    /// today's meetings and four bell notifications — the same picture b2-screens' own host runs
    /// photographed the screens against. When <paramref name="skewClock"/> is set, an extra
    /// ClockCheck row dated two days ahead is left behind, which is exactly what a clock turned back
    /// looks like to the clock check the next time it runs (W11's banner, mirroring
    /// HostDataFolderSeed's own <c>WAKEEL_HOST_SKEW_CLOCK</c>).
    /// </summary>
    internal static async Task ActivateAsync(string dataFolder, bool skewClock = false)
    {
        var paths = WakeelPaths.ForRoot(dataFolder);
        paths.EnsureDirectories();

        using var org = DeviceIdentity.Generate();
        using var device = DeviceIdentity.Generate();
        var packagePassword = PackagePassword.New();
        var exportedAt = new DateTimeOffset(2026, 9, 16, 9, 0, 0, TimeSpan.Zero);

        var certificate = DeviceCertificate.Issue(
            new DeviceCertificateBody(
                OrgId,
                OfficeUnitId,
                DeviceId,
                1,
                2,
                SetupRoles.Secretary,
                Wakeel.Crypto.DeviceKind.Pc,
                device.SigningPublicKeyText,
                device.AgreementPublicKeyText,
                exportedAt,
                OrgId),
            org);

        var content = new SetupContent(
            SetupContent.CurrentFormatVersion,
            exportedAt,
            1,
            new SetupOrg(OrgId, OrgName, org.SigningPublicKeyText, org.AgreementPublicKeyText, 1, "YYYYMMDD/DESSS"),
            Structure,
            new SetupOffice(OfficeUnitId, OfficeCode),
            new SetupDevice(DeviceId, 1, SetupRoles.Secretary, SetupSyncScopes.Full, certificate),
            device.Export(),
            new SetupEmployee(EmployeeName, 2, JobTitle),
            Base64Url.Encode(RandomBytes.Next(SetupContent.OfficeKeySize)),
            null,
            new SetupIncludes(true, true, true, true));

        var stagingRoot = Path.Combine(Path.GetTempPath(), "wakeel-e2e-staging", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(stagingRoot);
        var exportedPath = Path.Combine(stagingRoot, "office-device" + ContainerKinds.Extension(ContainerKind.Setup));

        SetupPackageWriter.Write(exportedPath, new SetupWriteRequest
        {
            Content = content,
            PackagePassword = packagePassword,
            OrgIdentity = org,
            Logo = () => new MemoryStream(Logo, writable: false),
            Guide = () => new MemoryStream("دليل الاستخدام"u8.ToArray(), writable: false),
            ReportTemplate = () => new MemoryStream("قالب التقرير الشهري"u8.ToArray(), writable: false),
            LetterTemplate = () => new MemoryStream("قالب الكتاب الرسمي"u8.ToArray(), writable: false),
            StagingDirectory = Path.Combine(stagingRoot, "admin-staging"),
            Time = TimeProvider.System,
        });

        var services = new ServiceCollection();
        services.AddWakeelCore(o => o.Paths = paths);
        services.AddWakeelAccount();
        await using (var provider = services.BuildServiceProvider())
        {
            var inspection = provider.GetRequiredService<SetupInspectionService>();
            await using (var stream = File.OpenRead(exportedPath))
            {
                await inspection.StageAsync(stream, Path.GetFileName(exportedPath), exportedAt);
            }

            inspection.Inspect(packagePassword);

            var activation = provider.GetRequiredService<ActivationService>();
            activation.Begin();
            var activated = await activation.ActivateAsync(Password);
            if (!activated.Succeeded)
            {
                throw new InvalidOperationException("WalkthroughDataFolder could not activate the seeded installation: " + activated.Message);
            }

            var profiles = provider.GetRequiredService<SignInProfileStore>();
            profiles.Save(new SignInProfile
            {
                OrgName = OrgName,
                OfficeName = OfficeName,
                OfficeCode = OfficeCode,
                EmployeeName = EmployeeName,
                JobTitle = JobTitle,
                EmployeeNo = 2,
                DeviceNo = 1,
                Logo = Logo,
            });

            var session = provider.GetRequiredService<AccountSession>();
            var shell = provider.GetRequiredService<ShellServices>();

            await SeedAsync(session.Db, shell);
            if (skewClock)
            {
                SkewClock(session.Db);
            }

            session.SignOut();
        }

        // Belt and braces (see the remarks on this class): the pooled native SQLite handle the
        // provider's DbSession opened must be gone, not merely returned to the pool, before the
        // host's own process opens the same file.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(stagingRoot, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort; a leftover temp staging folder is not this helper's data folder.
        }
    }

    private static async Task SeedAsync(WakeelDb db, ShellServices shell)
    {
        var now = DateTime.UtcNow;
        var localToday = DateTime.Now.Date;

        var phone = new Device
        {
            DeviceNo = 2,
            EmployeeNo = 2,
            EmployeeName = "محمد عوض",
            Role = InstallationRole.Secretary,
            Kind = Wakeel.Core.Data.DeviceKind.Phone,
            IssuedAt = now.AddYears(-1),
        };
        db.Devices.Add(phone);
        db.SaveChanges();

        Meeting firstMeeting;
        using (db.SuppressAuditStamps())
        {
            firstMeeting = new Meeting
            {
                Title = "لجنة المشتريات — عطاء صيانة المركبات",
                StartsAt = localToday.AddHours(11).ToUniversalTime(),
                DurationMin = 60,
                Location = "قاعة الاجتماعات",
                Status = MeetingStatus.Planned,
                CreatedAt = now.AddDays(-3),
                UpdatedAt = now.AddDays(-3),
            };

            db.AddRange(
                new TaskItem
                {
                    Title = "طلب تزويد بيانات مشروع الطريق الدائري",
                    DueAt = now.AddDays(-2),
                    AssigneeName = "سامر أبو غزالة",
                    Status = WorkTaskStatus.Open,
                    Priority = TaskPriority.Normal,
                    CreatedAt = now.AddDays(-3),
                    UpdatedAt = now.AddDays(-2),
                },
                new TaskItem
                {
                    Title = "الرد على استفسار وزارة الحكم المحلي",
                    DueAt = now.AddHours(6),
                    AssigneeName = "ليلى الشريف",
                    Status = WorkTaskStatus.Open,
                    Priority = TaskPriority.Normal,
                    CreatedAt = now.AddDays(-2),
                    UpdatedAt = now.AddDays(-1),
                },
                new TaskItem
                {
                    Title = "متابعة اتفاقية الصرف الصحي مع بلدية رام الله",
                    DueAt = null,
                    AssigneeName = "نور السالمي",
                    Status = WorkTaskStatus.Open,
                    Priority = TaskPriority.Normal,
                    CreatedAt = now.AddDays(-33),
                    UpdatedAt = now.AddDays(-32),
                },
                firstMeeting,
                new Meeting
                {
                    Title = "متابعة اتفاقية الصرف الصحي",
                    StartsAt = localToday.AddHours(14).AddMinutes(30).ToUniversalTime(),
                    DurationMin = 45,
                    Location = "بلدية رام الله",
                    Status = MeetingStatus.Planned,
                    CreatedAt = now.AddDays(-4),
                    UpdatedAt = now.AddDays(-4),
                },
                new PhoneExpense
                {
                    PhoneDeviceId = phone.Id,
                    Amount = 4250,
                    Purpose = "اتصالات دولية — مناقصة الطريق",
                    Note = "فاتورة جوال 0599-482-117",
                    At = now.AddDays(-1),
                    Status = PhoneExpenseStatus.Pending,
                    CreatedAt = now.AddDays(-1),
                    UpdatedAt = now.AddDays(-1),
                },
                new PhoneExpense
                {
                    PhoneDeviceId = phone.Id,
                    Amount = 2500,
                    Purpose = "رصيد شحن — هاتف المكتب الأرضي",
                    Note = "إيصال شحن 09-2381-440",
                    At = now.AddDays(-1),
                    Status = PhoneExpenseStatus.Pending,
                    CreatedAt = now.AddDays(-1),
                    UpdatedAt = now.AddDays(-1),
                });

            db.SaveChanges();
        }

        await shell.Notifications.CreateAsync(
            NotificationKinds.Meeting,
            "اجتماع قريب: " + firstMeeting.Title,
            "قاعة الاجتماعات · تبدأ الساعة 11:00",
            "meetings",
            firstMeeting.Id,
            createdAt: now.AddMinutes(-25));

        await shell.Notifications.CreateAsync(
            NotificationKinds.FinancialCycle,
            "بقي 3 أيام على نهاية الدورة المالية",
            "أنجز بنود التقرير الشهري قبل الإغلاق",
            createdAt: now.AddHours(-3));

        await shell.Notifications.CreateAsync(
            NotificationKinds.TaskDue,
            "مهمة متأخرة: طلب تزويد بيانات مشروع الطريق الدائري",
            "المكلّف: سامر أبو غزالة",
            createdAt: now.AddHours(-1));

        var backup = await shell.Notifications.CreateAsync(
            NotificationKinds.Backup,
            "لم تُؤخذ نسخة احتياطية منذ يومين",
            createdAt: now.AddDays(-1));

        // One read row, so the panel's «الكل» and «غير المقروء» tabs are not the same number.
        await shell.Notifications.MarkReadAsync(backup.Id, now);
    }

    /// <summary>
    /// Leaves behind a recorded check dated in the future — exactly what a machine whose clock has
    /// been turned back looks like to the next check the shell runs on sign-in — so W11's banner is
    /// reachable without touching the machine's own clock.
    /// </summary>
    private static void SkewClock(WakeelDb db)
    {
        using (db.SuppressAuditStamps())
        {
            db.ClockChecks.Add(new ClockCheck { At = DateTime.UtcNow.AddDays(2), Verdict = ClockVerdict.Ok });
            db.SaveChanges();
        }
    }
}
