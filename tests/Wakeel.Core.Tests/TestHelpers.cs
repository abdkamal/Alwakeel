using Microsoft.Extensions.Logging;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using Wakeel.Core.Services;

namespace Wakeel.Core.Tests;

internal static class TestHelpers
{
    public static byte[] NewKey()
    {
        var key = new byte[32];
        Random.Shared.NextBytes(key);
        return key;
    }

    public static string NewTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "wakeel-core-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    public static DbSession OpenNewSession(out string root, out byte[] key, IClock? clock = null)
    {
        root = NewTempRoot();
        key = NewKey();
        var paths = WakeelPaths.ForRoot(root);
        return DbSession.Open(paths, key, clock);
    }

    public static void DeleteRootQuietly(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
            // best effort cleanup; SQLite may still hold a WAL file briefly on some runners.
        }
    }

    public static Installation SeedInstallation(
        WakeelDb db,
        int cycleStartDay = 1,
        int deviceNo = 1,
        int employeeNo = 2,
        DateTime? buildDate = null,
        DateTime? activatedAt = null,
        string? numberingFormat = null)
    {
        var installation = new Installation
        {
            OrgId = Guid.CreateVersion7(),
            OrgName = "هيئة تجريبية",
            OfficeId = Guid.CreateVersion7(),
            OfficeName = "مكتب تجريبي",
            OfficeUnitId = Guid.CreateVersion7(),
            OfficeCode = "T1",
            DeviceId = Guid.CreateVersion7(),
            DeviceNo = deviceNo,
            EmployeeNo = employeeNo,
            EmployeeName = "موظف تجريبي",
            Role = InstallationRole.Director,
            SyncScope = SyncScope.Full,
            CycleStartDay = cycleStartDay,
            NumberingFormat = numberingFormat ?? OfficialNumberService.DefaultFormat,
            SetupVersion = "1",
            ActivatedAt = activatedAt ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            AppVersion = "0.21.0",
            BuildDate = buildDate ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            OrgX25519Pub = new byte[32],
            OrgEd25519Pub = new byte[32],
        };
        db.Installation.Add(installation);
        db.SaveChanges();
        return installation;
    }
}

/// <summary>A controllable <see cref="IClock"/> for tests that need to observe two distinct instants deterministically.</summary>
internal sealed class TestClock : IClock
{
    public DateTime UtcNow { get; set; } = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}

/// <summary>Collects the formatted warning messages a <see cref="WakeelDb"/> writes, for assertions.</summary>
internal sealed class RecordingLogger : ILogger<WakeelDb>
{
    private readonly List<string> _warnings = [];

    /// <summary>Every warning-level message written so far, formatted.</summary>
    public IReadOnlyList<string> Warnings
    {
        get
        {
            lock (_warnings)
            {
                return [.. _warnings];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel != LogLevel.Warning)
        {
            return;
        }

        lock (_warnings)
        {
            _warnings.Add(formatter(state, exception));
        }
    }
}
