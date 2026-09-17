using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using Wakeel.Core.Services;
using Wakeel.Core.Services.Correspondence;

namespace Wakeel.Core.Tests;

/// <summary>
/// A temporary installation with every B2 daily-shell service wired over it: one database, one
/// controllable clock, and fake platform probes, so a test can craft rows and then ask the real
/// services what they make of them.
/// </summary>
internal sealed class DailyShellWorld : IDisposable
{
    private readonly string _root;

    public DailyShellWorld(DateTime? now = null)
    {
        Clock = new TestClock { UtcNow = now ?? new DateTime(2026, 9, 16, 9, 0, 0, DateTimeKind.Utc) };
        Session = TestHelpers.OpenNewSession(out _root, out _, Clock);
        Paths = WakeelPaths.ForRoot(_root);
        Paths.EnsureDirectories();

        Settings = new SettingsService(Db, Clock);
        Ids = new IdGenerator();
        Attention = new AttentionService(Db, Settings);
        Badges = new BadgeService(Db, Attention);
        Notifications = new NotificationService(Db, Settings, Ids, Clock);
        ClockCheck = new ClockCheckService(Db);
        ClockGuard = new ClockGuard(ClockCheck, TimeProvider.System);
        Audit = new AuditService(Db, Clock);
        FollowUps = new FollowUpService(Db, Audit, Notifications);
        Reminders = new ReminderScheduler(Db, Notifications, Settings, Badges, FollowUps);
        Cycles = new FinancialCycleService(Db);
        QuickCapture = new QuickCaptureService(Db, Clock, Ids, Cycles, Badges);
        Word = new FakeWordProbe();
        Scanner = new FakeScannerProbe();
        Disk = new FakeDiskSpaceProbe();
        Runtime = new FakeRuntimeProbe();
        Health = new HealthService(Db, Paths, ClockGuard, Word, Scanner, Disk, Runtime);
    }

    public TestClock Clock { get; }

    public DbSession Session { get; }

    public WakeelDb Db => Session.Db;

    public WakeelPaths Paths { get; }

    public ISettingsService Settings { get; }

    public IIdGenerator Ids { get; }

    public IAttentionService Attention { get; }

    public IBadgeService Badges { get; }

    public INotificationService Notifications { get; }

    public IClockCheckService ClockCheck { get; }

    public ClockGuard ClockGuard { get; }

    public IAuditService Audit { get; }

    /// <summary>The correspondence follow-up pass the reminder scheduler runs alongside its own.</summary>
    public IFollowUpService FollowUps { get; }

    public IReminderScheduler Reminders { get; }

    public IFinancialCycleService Cycles { get; }

    public IQuickCaptureService QuickCapture { get; }

    public FakeWordProbe Word { get; }

    public FakeScannerProbe Scanner { get; }

    public FakeDiskSpaceProbe Disk { get; }

    public FakeRuntimeProbe Runtime { get; }

    public IHealthService Health { get; }

    public DateTime Now => Clock.UtcNow;

    /// <summary>Adds the single installation row most checks need.</summary>
    public Installation SeedInstallation(int cycleStartDay = 1) =>
        TestHelpers.SeedInstallation(Db, cycleStartDay, activatedAt: Now.AddYears(-1), buildDate: Now.AddYears(-1));

    /// <summary>
    /// Adds a device row and returns its id. <c>phone_expenses.phone_device_id</c> is a NOT NULL
    /// foreign key into <c>devices</c>, so a phone expense cannot be crafted without one; this is
    /// created lazily and reused, so a test that seeds many expenses still has one paired phone.
    /// </summary>
    public Guid EnsureDevice(DeviceKind kind = DeviceKind.Phone, DateTime? lastSyncAt = null)
    {
        var existing = Db.Devices.FirstOrDefault(d => d.Kind == kind);
        if (existing is not null)
        {
            if (lastSyncAt is not null)
            {
                existing.LastSyncAt = lastSyncAt;
                Db.SaveChanges();
            }

            return existing.Id;
        }

        var device = new Device
        {
            DeviceNo = kind == DeviceKind.Pc ? 1 : 2,
            EmployeeNo = 2,
            EmployeeName = "موظف تجريبي",
            Role = InstallationRole.Director,
            Kind = kind,
            IssuedAt = Now.AddYears(-1),
            PairedAt = Now.AddMonths(-1),
            LastSyncAt = lastSyncAt,
            SyncScope = SyncScope.Full,
        };
        Db.Devices.Add(device);
        Db.SaveChanges();
        return device.Id;
    }

    /// <summary>
    /// Writes <paramref name="entities"/> with their <c>CreatedAt</c>/<c>UpdatedAt</c> exactly as
    /// set, instead of being re-stamped to "now" on save. Crafting a stale record — one that has
    /// not been touched for a fortnight — is impossible otherwise.
    /// </summary>
    public void AddWithStamps(params SyncedEntity[] entities) => AddWithStamps((IEnumerable<SyncedEntity>)entities);

    /// <inheritdoc cref="AddWithStamps(SyncedEntity[])"/>
    public void AddWithStamps(IEnumerable<SyncedEntity> entities)
    {
        using (Db.SuppressAuditStamps())
        {
            foreach (var entity in entities)
            {
                if (entity.CreatedAt == default)
                {
                    entity.CreatedAt = entity.UpdatedAt == default ? Now : entity.UpdatedAt;
                }

                if (entity.UpdatedAt == default)
                {
                    entity.UpdatedAt = entity.CreatedAt;
                }

                if (entity.RowVersion == 0)
                {
                    entity.RowVersion = 1;
                }

                if (string.IsNullOrEmpty(entity.OriginDevice))
                {
                    entity.OriginDevice = "test-device";
                }

                Db.Add(entity);
            }

            Db.SaveChanges();
        }
    }

    public void Dispose()
    {
        ClockGuard.Dispose();
        Session.Dispose();
        TestHelpers.DeleteRootQuietly(_root);
    }
}

/// <summary>A <see cref="IWordProbe"/> whose answer the test sets.</summary>
internal sealed class FakeWordProbe : IWordProbe
{
    public WordInfo Result { get; set; } = new(true, "Office 2016 أو أحدث");

    public Exception? Throws { get; set; }

    public Task<WordInfo> DetectAsync(CancellationToken cancellationToken = default) =>
        Throws is null ? Task.FromResult(Result) : Task.FromException<WordInfo>(Throws);
}

/// <summary>A <see cref="IScannerProbe"/> whose answer the test sets.</summary>
internal sealed class FakeScannerProbe : IScannerProbe
{
    public ScannerInfo Result { get; set; } = new(true, 1);

    public Task<ScannerInfo> DetectAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result);
}

/// <summary>A <see cref="IDiskSpaceProbe"/> whose answer the test sets.</summary>
internal sealed class FakeDiskSpaceProbe : IDiskSpaceProbe
{
    public DiskSpaceInfo Result { get; set; } = new(true, 40L * 1024 * 1024 * 1024, 120L * 1024 * 1024 * 1024);

    /// <summary>The last path the health service asked about.</summary>
    public string? LastPath { get; private set; }

    public Task<DiskSpaceInfo> MeasureAsync(string path, CancellationToken cancellationToken = default)
    {
        LastPath = path;
        return Task.FromResult(Result);
    }
}

/// <summary>A <see cref="IRuntimeProbe"/> whose answer the test sets.</summary>
internal sealed class FakeRuntimeProbe : IRuntimeProbe
{
    public RuntimeInfo Result { get; set; } = new(true, "130.0.0.0");

    public Task<RuntimeInfo> DetectAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result);
}

/// <summary>An <see cref="IMinuteTicker"/> a test fires by hand.</summary>
internal sealed class ManualMinuteTicker : IMinuteTicker
{
    private Func<DateTime, CancellationToken, Task>? _onTick;

    /// <summary>How many times <see cref="Stop"/> has been called.</summary>
    public int StopCount { get; private set; }

    /// <summary>Whether a callback is currently registered.</summary>
    public bool Started => _onTick is not null;

    public void Start(Func<DateTime, CancellationToken, Task> onTick) => _onTick = onTick;

    public void Stop()
    {
        StopCount++;
        _onTick = null;
    }

    /// <summary>Fires one tick at <paramref name="now"/>.</summary>
    public Task TickAsync(DateTime now) => _onTick?.Invoke(now, CancellationToken.None) ?? Task.CompletedTask;

    public void Dispose() => Stop();
}
