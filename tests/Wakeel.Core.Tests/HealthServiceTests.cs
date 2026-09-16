using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using Wakeel.Core.Services;

namespace Wakeel.Core.Tests;

/// <summary>
/// The health center (W12) on a crafted environment: real database and vault folders, fake
/// platform probes. Every card is a status, an Arabic sentence and an action id; the summary
/// counts the problems; the export is readable Arabic.
/// </summary>
public sealed class HealthServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 16, 9, 0, 0, DateTimeKind.Utc);

    private readonly DailyShellWorld _world = new(Now);

    public HealthServiceTests()
    {
        _world.SeedInstallation();
        _world.Db.Backups.Add(new Backup { At = Now.AddDays(-1), FilePath = "b", Size = 1024, AppVersion = "0.21.0" });
        _world.EnsureDevice(DeviceKind.Pc, lastSyncAt: Now.AddHours(-2));
        _world.Db.Models.Add(new SearchModel { Path = "m.onnx", Kind = ModelKind.Embedding, Name = "نموذج التضمين", Status = ModelStatus.Active, CheckedAt = Now });
        _world.Db.SaveChanges();
    }

    public void Dispose() => _world.Dispose();

    [Fact]
    public async Task AHealthyInstallation_ReportsEveryCardOkAndSaysSo()
    {
        var report = await _world.Health.CheckAsync(Now);

        Assert.Equal(HealthStatus.Ok, report.Overall);
        Assert.Equal(0, report.Problems);
        Assert.Equal("كل شيء سليم", report.SummaryAr);
        Assert.All(report.Cards, c => Assert.Equal(HealthStatus.Ok, c.Status));
    }

    [Fact]
    public async Task EveryComponentTheSpecificationNames_HasACard()
    {
        var report = await _world.Health.CheckAsync(Now);
        var components = report.Cards.Select(c => c.Component).ToList();

        Assert.Equal(
            new[]
            {
                HealthComponents.Database,
                HealthComponents.Vault,
                HealthComponents.Word,
                HealthComponents.Scanner,
                HealthComponents.Models,
                HealthComponents.Clock,
                HealthComponents.Space,
                HealthComponents.Backup,
                HealthComponents.Sync,
                HealthComponents.Runtime,
                HealthComponents.Version,
            },
            components);
        Assert.All(report.Cards, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.TitleAr));
            Assert.False(string.IsNullOrWhiteSpace(c.MessageAr));
        });
    }

    [Fact]
    public async Task TheDatabaseCardPassesItsIntegrityCheckAndShowsTheSize()
    {
        var report = await _world.Health.CheckAsync(Now);
        var card = Card(report, HealthComponents.Database);

        Assert.Equal(HealthStatus.Ok, card.Status);
        Assert.StartsWith("سليمة — الحجم", card.MessageAr, StringComparison.Ordinal);
        Assert.Equal(HealthActions.None, card.ActionId);
    }

    [Fact]
    public async Task MissingWord_IsAWarningWithTheFallbackExplained_NotAFault()
    {
        _world.Word.Result = WordInfo.Missing;

        var report = await _world.Health.CheckAsync(Now);
        var card = Card(report, HealthComponents.Word);

        Assert.Equal(HealthStatus.Warning, card.Status);
        Assert.Equal("غير متوفر؛ ستُطبع الكتب من البرنامج نفسه", card.MessageAr);
        Assert.Equal(HealthActions.InstallWord, card.ActionId);
        Assert.Equal(HealthStatus.Warning, report.Overall);
        Assert.Equal("هناك مشكلة واحدة تحتاج تدخلًا", report.SummaryAr);
    }

    [Fact]
    public async Task AProbeThatThrows_BecomesACardRatherThanAFailedCheck()
    {
        _world.Word.Throws = new InvalidOperationException("probe blew up");

        var report = await _world.Health.CheckAsync(Now);

        Assert.Equal(HealthStatus.Warning, Card(report, HealthComponents.Word).Status);
        Assert.DoesNotContain("probe blew up", Card(report, HealthComponents.Word).MessageAr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoScanner_IsAFault_WithAnActionToLookForItAgain()
    {
        // W12 renders a missing scanner as «عطل» with «إعادة الاكتشاف», not as a warning: paper
        // correspondence enters the office through the scanner, so one the program cannot see is
        // a component that is down.
        _world.Scanner.Result = ScannerInfo.None;

        var card = Card(await _world.Health.CheckAsync(Now), HealthComponents.Scanner);

        Assert.Equal(HealthStatus.Error, card.Status);
        Assert.Equal("لا يوجد ماسح متصل", card.MessageAr);
        Assert.Equal(HealthActions.ConnectScanner, card.ActionId);
    }

    [Fact]
    public async Task LowDiskSpace_WarnsBelowTwoGigabytes()
    {
        _world.Disk.Result = new DiskSpaceInfo(true, 1L * 1024 * 1024 * 1024, 120L * 1024 * 1024 * 1024);

        var card = Card(await _world.Health.CheckAsync(Now), HealthComponents.Space);

        Assert.Equal(HealthStatus.Warning, card.Status);
        Assert.Equal("المتاح 1 غيغابايت من 120 غيغابايت فقط؛ فرّغ مساحة", card.MessageAr);
        Assert.Equal(HealthActions.FreeSpace, card.ActionId);
    }

    [Fact]
    public async Task TheSpaceCard_MeasuresTheInstallationsOwnVolume()
    {
        await _world.Health.CheckAsync(Now);

        Assert.Equal(_world.Paths.Root, _world.Disk.LastPath);
    }

    [Fact]
    public async Task AVaultFileThatDoesNotMatchItsRecord_IsAFault()
    {
        var sha = new string('a', 64);
        _world.AddWithStamps(new Document
        {
            Sha256 = sha,
            Size = 1000,
            Mime = "application/pdf",
            OriginalName = "كتاب.pdf",
            Source = DocumentSource.Import,
            PageCount = 1,
            OcrStatus = OcrStatus.Done,
        });

        var path = _world.Paths.VaultFilePath(sha);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, new byte[10]); // the wrong size

        var report = await _world.Health.CheckAsync(Now);
        var card = Card(report, HealthComponents.Vault);

        Assert.Equal(HealthStatus.Error, card.Status);
        Assert.Equal("ملف واحد لا تطابق سجلاتها", card.MessageAr);
        Assert.Equal(HealthActions.RestoreBackup, card.ActionId);
        Assert.Equal(HealthStatus.Error, report.Overall);
    }

    [Fact]
    public async Task AVaultFileThatMatchesItsRecord_IsFine()
    {
        var sha = new string('b', 64);
        _world.AddWithStamps(new Document
        {
            Sha256 = sha,
            Size = 10,
            Mime = "application/pdf",
            OriginalName = "كتاب.pdf",
            Source = DocumentSource.Import,
            PageCount = 1,
            OcrStatus = OcrStatus.Done,
        });

        var path = _world.Paths.VaultFilePath(sha);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, new byte[10]);

        var card = Card(await _world.Health.CheckAsync(Now), HealthComponents.Vault);

        Assert.Equal(HealthStatus.Ok, card.Status);
        Assert.Equal("المجلد متاح — ملف واحد", card.MessageAr);
    }

    [Fact]
    public async Task ABadClock_ShowsAsAFaultOnTheClockCard()
    {
        await _world.ClockGuard.CheckAsync(ClockCheckTrigger.Startup, Now.AddYears(-5));

        var card = Card(await _world.Health.CheckAsync(Now), HealthComponents.Clock);

        Assert.Equal(HealthStatus.Error, card.Status);
        Assert.Equal("الساعة غير صحيحة؛ صحّحها من إعدادات ويندوز", card.MessageAr);
        Assert.Equal(HealthActions.FixClock, card.ActionId);
    }

    [Fact]
    public async Task AnOldBackup_WarnsAndOffersToTakeANewOne()
    {
        using var world = new DailyShellWorld(Now);
        world.SeedInstallation();
        world.Db.Backups.Add(new Backup { At = Now.AddDays(-30), FilePath = "b", Size = 1, AppVersion = "0.21.0" });
        await world.Db.SaveChangesAsync();

        var card = Card(await world.Health.CheckAsync(Now), HealthComponents.Backup);

        Assert.Equal(HealthStatus.Warning, card.Status);
        Assert.StartsWith("آخر نسخة احتياطية", card.MessageAr, StringComparison.Ordinal);
        Assert.EndsWith("؛ خذ نسخة جديدة", card.MessageAr, StringComparison.Ordinal);
        Assert.Equal(HealthActions.TakeBackup, card.ActionId);
    }

    [Fact]
    public async Task TheSyncCard_NamesEachKindOfDeviceAndWhenItLastSynced()
    {
        _world.EnsureDevice(DeviceKind.Phone, lastSyncAt: Now.AddDays(-1).AddHours(-3));

        var card = Card(await _world.Health.CheckAsync(Now), HealthComponents.Sync);

        Assert.Equal(HealthStatus.Ok, card.Status);
        Assert.Contains("الحاسوب: قبل ساعتين", card.MessageAr, StringComparison.Ordinal);
        Assert.Contains("الهاتف:", card.MessageAr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADeviceThatLastSyncedTenDaysAgo_WarnsInsteadOfClaimingEverythingIsFine()
    {
        // The regression this guards: the card used to answer «سليم» whenever ANY trusted device
        // had ever synced, however long ago — which is precisely the state the card exists to
        // reveal. The PC seeded by the fixture is pushed back with the phone so nothing recent
        // remains.
        using var world = new DailyShellWorld(Now);
        world.SeedInstallation();
        world.EnsureDevice(DeviceKind.Pc, lastSyncAt: Now.AddDays(-10));
        world.EnsureDevice(DeviceKind.Phone, lastSyncAt: Now.AddDays(-12));

        var card = Card(await world.Health.CheckAsync(Now), HealthComponents.Sync);

        Assert.Equal(HealthStatus.Warning, card.Status);
        Assert.Equal(HealthActions.OpenSync, card.ActionId);
        Assert.Contains("الحاسوب:", card.MessageAr, StringComparison.Ordinal);
        Assert.EndsWith("؛ آخر مزامنة قديمة", card.MessageAr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADeviceSyncedWithinTheThreshold_KeepsTheSyncCardHealthy()
    {
        using var world = new DailyShellWorld(Now);
        world.SeedInstallation();
        world.EnsureDevice(DeviceKind.Pc, lastSyncAt: Now.AddDays(-(HealthService.SyncWarningDays - 1)));

        var card = Card(await world.Health.CheckAsync(Now), HealthComponents.Sync);

        Assert.Equal(HealthStatus.Ok, card.Status);
        Assert.DoesNotContain("قديمة", card.MessageAr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSyncCard_NeverNamesAnEmployee()
    {
        // The health report is a file the user hands to whoever helps them; the card names a KIND
        // of device, never the person holding it.
        using var world = new DailyShellWorld(Now);
        world.SeedInstallation();
        world.EnsureDevice(DeviceKind.Pc, lastSyncAt: Now.AddHours(-1));
        var employee = world.Db.Devices.Select(d => d.EmployeeName).First();

        var report = await world.Health.CheckAsync(Now);

        Assert.False(string.IsNullOrWhiteSpace(employee));
        Assert.DoesNotContain(employee!, Card(report, HealthComponents.Sync).MessageAr, StringComparison.Ordinal);
        Assert.DoesNotContain(employee!, world.Health.ExportText(report), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithNothingSyncedYet_TheSyncCardSaysSo()
    {
        using var world = new DailyShellWorld(Now);
        world.SeedInstallation();

        var card = Card(await world.Health.CheckAsync(Now), HealthComponents.Sync);

        Assert.Equal(HealthStatus.Warning, card.Status);
        Assert.Equal("لم تتم مزامنة مع أي جهاز بعد", card.MessageAr);
    }

    [Fact]
    public async Task EachRunIsRecordedAsSnapshotsSoTheHistoryIsThere()
    {
        var report = await _world.Health.CheckAsync(Now);

        Assert.Equal(report.Cards.Count, _world.Db.HealthSnapshots.Count());

        await _world.Health.CheckAsync(Now.AddMinutes(5), persist: false);
        Assert.Equal(report.Cards.Count, _world.Db.HealthSnapshots.Count());
    }

    [Fact]
    public async Task ARunThatFindsNothingNew_AddsNoSnapshots()
    {
        // W12 re-checks every quarter of an hour. Storing eleven identical cards each time would
        // add a thousand rows a day to a database the office backs up and syncs, for a history
        // that says the same thing a thousand times.
        var report = await _world.Health.CheckAsync(Now);
        Assert.Equal(report.Cards.Count, _world.Db.HealthSnapshots.Count());

        await _world.Health.CheckAsync(Now.AddMinutes(15));
        await _world.Health.CheckAsync(Now.AddMinutes(30));

        Assert.Equal(report.Cards.Count, _world.Db.HealthSnapshots.Count());
    }

    [Fact]
    public async Task AChangedCard_IsRecorded_SoTheHistoryShowsWhatChanged()
    {
        await _world.Health.CheckAsync(Now);
        var before = _world.Db.HealthSnapshots.Count();

        // The scanner was ready; now it is not.
        _world.Scanner.Result = ScannerInfo.None;
        await _world.Health.CheckAsync(Now.AddMinutes(15));

        Assert.Equal(before + 1, _world.Db.HealthSnapshots.Count());
        var newest = _world.Db.HealthSnapshots
            .Where(s => s.Component == HealthComponents.Scanner)
            .OrderByDescending(s => s.CheckedAt)
            .First();
        Assert.Equal(HealthStatus.Error, newest.Status);
    }

    [Fact]
    public async Task SnapshotsOlderThanTheRetentionWindow_AreDropped()
    {
        var ancient = Now - HealthService.SnapshotRetention - TimeSpan.FromDays(1);
        _world.Db.HealthSnapshots.Add(new HealthSnapshot
        {
            Component = HealthComponents.Database,
            Status = HealthStatus.Ok,
            MessageAr = "قديم جدًا",
            CheckedAt = ancient,
        });
        await _world.Db.SaveChangesAsync();

        var report = await _world.Health.CheckAsync(Now);

        Assert.Equal(report.Cards.Count, _world.Db.HealthSnapshots.Count());
        Assert.False(_world.Db.HealthSnapshots.Any(s => s.CheckedAt == ancient));
    }

    [Fact]
    public async Task TheExportedReport_IsPlainArabicWithOneLinePerCard()
    {
        _world.Word.Result = WordInfo.Missing;
        var report = await _world.Health.CheckAsync(Now);

        var text = _world.Health.ExportText(report);
        var lines = text.Split('\n')
            .Select(l => l.Trim('\r'))
            .Where(l => l.Length > 0)
            .ToList();

        Assert.Equal("تقرير حالة الوكيل", lines[0]);
        Assert.StartsWith("أُعدَّ في ", lines[1], StringComparison.Ordinal);
        Assert.Equal("هناك مشكلة واحدة تحتاج تدخلًا", lines[2]);
        Assert.Contains(lines, l => l.Contains("[تحذير]", StringComparison.Ordinal) && l.Contains("Word", StringComparison.Ordinal));
        Assert.Equal(report.Cards.Count, lines.Count(l => l.StartsWith("- ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task NoMessageMentionsAServerAPortTheInternetOrTheDatabaseEngine()
    {
        _world.Word.Result = WordInfo.Missing;
        _world.Scanner.Result = ScannerInfo.None;
        _world.Runtime.Result = RuntimeInfo.Missing;
        _world.Disk.Result = DiskSpaceInfo.Unknown;
        await _world.ClockGuard.CheckAsync(ClockCheckTrigger.Startup, Now.AddYears(-5));

        var report = await _world.Health.CheckAsync(Now);
        var text = _world.Health.ExportText(report);

        foreach (var forbidden in new[] { "خادم", "منفذ", "إنترنت", "SQL", "SQLite", "PostgreSQL", "port", "server" })
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static HealthCard Card(HealthReport report, string component) =>
        report.Cards.Single(c => c.Component == component);
}
