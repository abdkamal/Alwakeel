using Microsoft.Data.Sqlite;

namespace Wakeel.Core.Data;

/// <summary>Opens SQLCipher-encrypted SQLite connections with the database key and required pragmas.</summary>
public static class DbConnectionFactory
{
    private static int _batteriesInitialized;

    /// <summary>
    /// Opens <paramref name="dbPath"/> (creating it if missing) keyed with <paramref name="key"/>
    /// (exactly 32 bytes), sets <c>journal_mode=WAL</c>, <c>foreign_keys=ON</c> and
    /// <c>busy_timeout=5000</c>, then reads <c>sqlite_master</c> once so a wrong key fails here —
    /// inside this call — rather than on the caller's first unrelated query.
    /// </summary>
    public static SqliteConnection Open(string dbPath, byte[] key)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException("Database key must be exactly 32 bytes.", nameof(key));
        }

        EnsureBatteriesInitialized();

        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Pooling is off: Microsoft.Data.Sqlite pools native connection handles by data source,
        // and a pooled handle keeps whatever key unlocked it — reusing the pool across a
        // lock/re-key or a wrong-key attempt would silently reuse an already-unlocked handle
        // instead of actually validating the given key.
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            ApplyKey(connection, key);

            // Verifies the key immediately: SQLCipher does not validate the key on "PRAGMA key"
            // itself, only on the first actual page read. Reading sqlite_master here makes a
            // wrong key fail inside Open() instead of on some later, unrelated query.
            using (var probe = connection.CreateCommand())
            {
                probe.CommandText = "SELECT count(*) FROM sqlite_master;";
                probe.ExecuteScalar();
            }

            ApplyPragmas(connection);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void EnsureBatteriesInitialized()
    {
        if (Interlocked.Exchange(ref _batteriesInitialized, 1) == 0)
        {
            SQLitePCL.Batteries_V2.Init();
        }
    }

    /// <summary>
    /// Sets the SQLCipher key via <c>PRAGMA key</c>. The hex-encoded key text is built into a
    /// managed <see cref="string"/> (both the local <c>hex</c>/<c>CommandText</c> here and
    /// whatever Microsoft.Data.Sqlite copies internally) — .NET strings cannot be zeroed, so this
    /// value is unavoidably heap-resident for the process's lifetime; Microsoft.Data.Sqlite
    /// offers no byte-oriented key API to avoid it. The command is disposed immediately after use
    /// to at least drop that particular reference promptly. Because of this, the process must
    /// never write a full memory dump to disk while a database is unlocked, and callers should
    /// zero their own <c>keyBytes</c> array (the one true byte-oriented copy) after
    /// <see cref="Open"/> — or, for re-authentication, after <c>DbSession.Unlock</c> — returns.
    /// </summary>
    private static void ApplyKey(SqliteConnection connection, byte[] key)
    {
        var hex = Convert.ToHexString(key);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA key = \"x'{hex}'\";";
        command.ExecuteNonQuery();
    }

    private static void ApplyPragmas(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            """;
        command.ExecuteNonQuery();
    }
}
