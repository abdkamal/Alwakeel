using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wakeel.Core.Services;

namespace Wakeel.Core.Data;

/// <summary>
/// Owns one open database connection + <see cref="WakeelDb"/> for the lifetime of a run of
/// الوكيل: <see cref="Open"/> opens the keyed connection, migrates the schema, and hands back a
/// ready session; <see cref="Lock"/>/<see cref="Unlock"/> back the auto-lock feature (AGREEMENT
/// item 7) by closing and reopening the database handle.
/// </summary>
public sealed class DbSession : IDisposable
{
    private readonly WakeelPaths _paths;
    private readonly IClock _clock;
    private readonly ILogger<WakeelDb>? _logger;
    private SqliteConnection? _connection;
    private WakeelDb? _db;

    private DbSession(WakeelPaths paths, IClock clock, ILogger<WakeelDb>? logger)
    {
        _paths = paths;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>True after <see cref="Lock"/> and before the next successful <see cref="Unlock"/>.</summary>
    public bool IsLocked { get; private set; }

    /// <summary>The active context. Throws while the session is locked or not yet opened.</summary>
    public WakeelDb Db => _db ?? throw new InvalidOperationException(
        IsLocked ? "قاعدة البيانات مقفلة." : "قاعدة البيانات غير مفتوحة.");

    /// <summary>
    /// Opens (creating on first run) and migrates the database at
    /// <paramref name="paths"/>.<see cref="WakeelPaths.DbPath"/>. <paramref name="clock"/> feeds
    /// <see cref="WakeelDb"/>'s automatic <c>SyncedEntity</c> timestamping; defaults to
    /// <see cref="SystemClock"/> over <see cref="TimeProvider.System"/> when omitted.
    /// <paramref name="logger"/>, when supplied, receives the <see cref="DataIntegrityLog"/>
    /// warnings the context raises for unreadable stored values.
    /// </summary>
    public static DbSession Open(WakeelPaths paths, byte[] keyBytes, IClock? clock = null, ILogger<WakeelDb>? logger = null)
    {
        var session = new DbSession(paths, clock ?? new SystemClock(TimeProvider.System), logger);
        session.OpenCore(keyBytes);
        return session;
    }

    /// <summary>Closes the database handle without touching any files; call <see cref="Unlock"/> to resume.</summary>
    public void Lock()
    {
        if (IsLocked)
        {
            return;
        }

        _db?.Dispose();
        _db = null;
        _connection?.Dispose();
        _connection = null;
        IsLocked = true;
    }

    /// <summary>Reopens the database after a successful re-authentication (schema is already migrated).</summary>
    public void Unlock(byte[] keyBytes)
    {
        if (!IsLocked)
        {
            throw new InvalidOperationException("قاعدة البيانات غير مقفلة.");
        }

        OpenCore(keyBytes);
    }

    public void Dispose()
    {
        _db?.Dispose();
        _connection?.Dispose();
    }

    private void OpenCore(byte[] keyBytes)
    {
        _paths.EnsureDirectories();
        var connection = DbConnectionFactory.Open(_paths.DbPath, keyBytes);
        try
        {
            SchemaMigrator.Migrate(connection);
            var options = new DbContextOptionsBuilder<WakeelDb>().UseSqlite(connection).Options;
            _db = new WakeelDb(options, _clock, _logger);
            _connection = connection;
            IsLocked = false;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}
