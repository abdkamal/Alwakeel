using System.Globalization;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Wakeel.Admin.UI.Data;

/// <summary>
/// Applies the admin tool's own embedded <c>Migrations/NNNN_*.sql</c> scripts, in order, to an open
/// SQLCipher connection, recording what has run in <c>schema_versions</c>.
/// </summary>
/// <remarks>
/// Deliberately a separate migrator from <c>Wakeel.Core.Data.SchemaMigrator</c> rather than a reuse
/// of it: that one is bound to the migration set embedded in Wakeel.Core, which is the schema of a
/// الوكيل installation. <c>admin.db</c> is a different database with a different schema
/// (DATA-MODEL.md §13), so it carries its own set here, and the two can never be applied to each
/// other's file by accident. The bookkeeping table and its rules are the same, so a person who
/// knows one knows the other.
/// </remarks>
public static class AdminSchemaMigrator
{
    private const string EmbeddedPrefix = "Wakeel.Admin.UI.Migrations.";

    /// <summary>
    /// Applies every embedded script not yet recorded in <c>schema_versions</c>. Throws
    /// <see cref="InvalidOperationException"/> without applying anything when the file's highest
    /// recorded version is already newer than the highest script this build carries — this build is
    /// older than the one that created the file, and going on would run outdated code against a
    /// schema it does not match.
    /// </summary>
    public static void Migrate(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        EnsureVersionsTable(connection);
        var applied = GetAppliedVersions(connection);
        var scripts = GetEmbeddedScripts().ToList();

        var maxApplied = applied.Count == 0 ? 0 : applied.Max();
        var maxEmbedded = scripts.Count == 0 ? 0 : scripts.Max(script => script.Version);
        if (maxApplied > maxEmbedded)
        {
            throw new InvalidOperationException(
                $"admin database schema version {maxApplied} is newer than the highest migration this build knows ({maxEmbedded}).");
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
                record.CommandText =
                    "INSERT INTO schema_versions(version, name, applied_at) VALUES ($version, $name, $appliedAt);";
                record.Parameters.AddWithValue("$version", version);
                record.Parameters.AddWithValue("$name", name);
                record.Parameters.AddWithValue("$appliedAt", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                record.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    /// <summary>The highest applied version, or zero for a file this migrator has never touched.</summary>
    public static int GetCurrentVersion(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!VersionsTableExists(connection))
        {
            return 0;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_versions;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static bool VersionsTableExists(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'schema_versions';";
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
        var assembly = typeof(AdminSchemaMigrator).Assembly;
        var names = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(EmbeddedPrefix, StringComparison.Ordinal)
                           && name.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal);

        foreach (var resource in names)
        {
            var fileName = resource[EmbeddedPrefix.Length..];
            var separator = fileName.IndexOf('_', StringComparison.Ordinal);
            if (separator <= 0
                || !int.TryParse(fileName[..separator], NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
            {
                continue;
            }

            yield return (version, fileName, ReadResource(assembly, resource));
        }
    }

    private static string ReadResource(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
                           ?? throw new InvalidOperationException($"embedded migration {resource} could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
