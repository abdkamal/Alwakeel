using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Wakeel.Core.Data;

/// <summary>
/// Applies the embedded <c>Migrations/NNNN_*.sql</c> scripts, in order, to an open SQLCipher
/// connection, tracking which versions have run in <c>schema_versions</c>. No dotnet-ef tool
/// or EF migrations are used — the schema is plain SQL, and the EF model in
/// <see cref="WakeelDb"/> is written to match it exactly.
/// </summary>
public static class SchemaMigrator
{
    private const string EmbeddedPrefix = "Wakeel.Core.Migrations.";

    /// <summary>
    /// Applies every embedded migration script not yet recorded in <c>schema_versions</c>, in
    /// filename order. Throws <see cref="InvalidOperationException"/> without applying anything
    /// when the database's highest recorded version is already newer than the highest embedded
    /// script — this build is older than the one that created the database, and proceeding would
    /// run an outdated EF model against a schema it does not match.
    /// </summary>
    public static void Migrate(SqliteConnection connection)
    {
        EnsureVersionsTable(connection);
        var applied = GetAppliedVersions(connection);
        var scripts = GetEmbeddedScripts().ToList();

        var maxApplied = applied.Count == 0 ? 0 : applied.Max();
        var maxEmbedded = scripts.Count == 0 ? 0 : scripts.Max(s => s.Version);
        if (maxApplied > maxEmbedded)
        {
            throw new InvalidOperationException(
                $"database schema version {maxApplied} is newer than the highest migration this build knows ({maxEmbedded}); this build is older than the one that created the database.");
        }

        foreach (var (version, name, sql) in scripts)
        {
            if (applied.Contains(version))
            {
                continue;
            }

            using var transaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }

            using (var record = connection.CreateCommand())
            {
                record.Transaction = transaction;
                record.CommandText = "INSERT INTO schema_versions(version, name, applied_at) VALUES ($version, $name, $appliedAt);";
                record.Parameters.AddWithValue("$version", version);
                record.Parameters.AddWithValue("$name", name);
                record.Parameters.AddWithValue("$appliedAt", DateTime.UtcNow.ToString("O"));
                record.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    /// <summary>
    /// The highest applied schema version, or 0 if <c>schema_versions</c> does not exist yet (a
    /// database <see cref="Migrate"/> has never run against) or is empty. Pure read: unlike
    /// <see cref="Migrate"/>, this never creates the table, so a read-only inspection tool cannot
    /// have the side effect of marking an untouched database as migrated.
    /// </summary>
    public static int GetCurrentVersion(SqliteConnection connection)
    {
        if (!VersionsTableExists(connection))
        {
            return 0;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_versions;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static bool VersionsTableExists(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'schema_versions';";
        return command.ExecuteScalar() is not null;
    }

    private static void EnsureVersionsTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_versions (
                version INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                applied_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static HashSet<int> GetAppliedVersions(SqliteConnection connection)
    {
        var result = new HashSet<int>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_versions;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(reader.GetInt32(0));
        }

        return result;
    }

    private static IEnumerable<(int Version, string Name, string Sql)> GetEmbeddedScripts()
    {
        var assembly = typeof(SchemaMigrator).Assembly;
        var names = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(EmbeddedPrefix, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal);

        foreach (var resourceName in names)
        {
            var fileName = resourceName[EmbeddedPrefix.Length..];
            var versionText = fileName.Split('_', 2)[0];
            if (!int.TryParse(versionText, out var version))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Missing embedded migration resource: {resourceName}");
            using var reader = new StreamReader(stream);
            yield return (version, fileName, reader.ReadToEnd());
        }
    }
}
