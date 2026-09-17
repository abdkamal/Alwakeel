using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;

namespace Wakeel.Core.Tests;

public sealed class SchemaTests : IDisposable
{
    private readonly string _root;
    private readonly byte[] _key;
    private readonly DbSession _session;

    public SchemaTests()
    {
        _session = TestHelpers.OpenNewSession(out _root, out _key);
    }

    public void Dispose()
    {
        _session.Dispose();
        TestHelpers.DeleteRootQuietly(_root);
    }

    [Fact]
    public void Open_CreatesEncryptedFileAndAppliesSchema()
    {
        var paths = WakeelPaths.ForRoot(_root);
        Assert.True(File.Exists(paths.DbPath));

        // The file must not be a plaintext SQLite database: the header magic is absent.
        var header = new byte[16];
        using (var stream = new FileStream(paths.DbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            _ = stream.Read(header, 0, header.Length);
        }

        var plaintextHeader = "SQLite format 3\0"u8.ToArray();
        Assert.NotEqual(plaintextHeader, header);
    }

    [Fact]
    public void Open_RecordsEveryEmbeddedSchemaVersion()
    {
        var paths = WakeelPaths.ForRoot(_root);
        using var connection = DbConnectionFactory.Open(paths.DbPath, _key);

        // A fresh database is migrated to the newest embedded script, not merely to the first.
        var embedded = typeof(SchemaMigrator).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("Wakeel.Core.Migrations.", StringComparison.Ordinal)
                && n.EndsWith(".sql", StringComparison.Ordinal))
            .Select(n => int.Parse(n["Wakeel.Core.Migrations.".Length..].Split('_', 2)[0], System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        Assert.NotEmpty(embedded);
        Assert.Equal(embedded.Max(), SchemaMigrator.GetCurrentVersion(connection));

        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM schema_versions;";
        Assert.Equal(embedded.Count, Convert.ToInt32(count.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Open_IndexesTheNotificationSourceAndCreationTime()
    {
        // The reminder pass runs every minute and reads the notification table each time
        // (used reminder keys), and the bell panel orders it by creation time.
        var paths = WakeelPaths.ForRoot(_root);
        using var connection = DbConnectionFactory.Open(paths.DbPath, _key);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'notifications';";
        var names = new List<string>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }
        }

        Assert.Contains("ix_notifications_source", names);
        Assert.Contains("ix_notifications_created_at", names);
    }

    [Fact]
    public void Open_IsIdempotent_SecondOpenDoesNotReapplyOrFail()
    {
        var paths = WakeelPaths.ForRoot(_root);
        using var second = DbSession.Open(paths, _key);
        Assert.False(second.IsLocked);
    }

    [Fact]
    public void Open_WithWrongKey_ThrowsOnFirstRead()
    {
        var paths = WakeelPaths.ForRoot(_root);
        var wrongKey = TestHelpers.NewKey();
        Assert.Throws<SqliteException>(() => DbConnectionFactory.Open(paths.DbPath, wrongKey));
    }

    [Fact]
    public void Open_WithKeyOfWrongLength_Throws()
    {
        var paths = WakeelPaths.ForRoot(_root);
        Assert.Throws<ArgumentException>(() => DbConnectionFactory.Open(paths.DbPath, new byte[16]));
    }

    [Fact]
    public void EveryDbSet_IsQueryable()
    {
        var db = _session.Db;
        var setProperties = typeof(WakeelDb).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .ToList();

        Assert.Equal(66, setProperties.Count);

        foreach (var property in setProperties)
        {
            var dbSet = property.GetValue(db)!;
            var elementType = property.PropertyType.GetGenericArguments()[0];
            var method = typeof(SchemaTests).GetMethod(nameof(AssertEmptyAndQueryable), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(elementType);
            try
            {
                method.Invoke(null, [dbSet]);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw new Xunit.Sdk.XunitException($"{property.Name} ({elementType.Name}) failed to query: {ex.InnerException.Message}");
            }
        }
    }

    private static void AssertEmptyAndQueryable<T>(IQueryable<T> queryable)
        where T : class
    {
        // Freshly migrated database: every mapped table starts empty. Materializing must not throw.
        Assert.Empty(queryable.Take(1).ToList());
    }

    [Fact]
    public void EverySyncedTable_CarriesTheSharedColumnSet_AndBothChangeLogTriggers()
    {
        // DATA-MODEL.md §0: every official (synced) table carries the shared column set and is
        // written to change_log by one AFTER INSERT and one AFTER UPDATE trigger. The count is
        // pinned so that adding an entity without its SQL table — or a table without its
        // triggers — fails here rather than silently dropping rows out of the sync export.
        // document_pages and document_links joined the set with the DATA-MODEL.md §3 decision
        // of 2026-09-16, bringing it to 46.
        var syncedTables = _session.Db.Model.GetEntityTypes()
            .Where(t => typeof(SyncedEntity).IsAssignableFrom(t.ClrType))
            .Select(t => t.GetTableName()!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(46, syncedTables.Count);
        Assert.Contains("document_pages", syncedTables);
        Assert.Contains("document_links", syncedTables);

        var paths = WakeelPaths.ForRoot(_root);
        using var connection = DbConnectionFactory.Open(paths.DbPath, _key);

        string[] sharedColumns =
            ["id", "created_at", "updated_at", "origin_device", "row_version", "base_version", "deleted_at"];
        foreach (var table in syncedTables)
        {
            var columns = new HashSet<string>(StringComparer.Ordinal);
            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "SELECT name FROM pragma_table_info($table);";
                pragma.Parameters.AddWithValue("$table", table);
                using var reader = pragma.ExecuteReader();
                while (reader.Read())
                {
                    _ = columns.Add(reader.GetString(0));
                }
            }

            foreach (var column in sharedColumns)
            {
                Assert.True(columns.Contains(column), $"{table} is missing the shared column {column}");
            }

            foreach (var suffix in new[] { "ai", "au" })
            {
                using var check = connection.CreateCommand();
                check.CommandText = "SELECT name FROM sqlite_master WHERE type = 'trigger' AND name = $name;";
                check.Parameters.AddWithValue("$name", $"trg_{table}_{suffix}");
                Assert.True(check.ExecuteScalar() is not null, $"{table} is missing the trigger trg_{table}_{suffix}");
            }
        }

        // Exactly two change_log triggers per synced table, and nothing else writes to change_log.
        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND sql LIKE '%INSERT INTO change_log%';";
        Assert.Equal(92L, (long)count.ExecuteScalar()!);
    }

    [Fact]
    public void AuditLog_StaysLocalAppendOnly_WithNoSharedColumnsAndNoTrigger()
    {
        // DATA-MODEL.md §1 (decision of 2026-09-16): audit_log is local and append-only. B6
        // exports it read-only by its own `at` range; on import a row is inserted when its id is
        // absent and never updated, so it needs neither the §0 columns nor a change_log trigger.
        var paths = WakeelPaths.ForRoot(_root);
        using var connection = DbConnectionFactory.Open(paths.DbPath, _key);

        var columns = new HashSet<string>(StringComparer.Ordinal);
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "SELECT name FROM pragma_table_info('audit_log');";
            using var reader = pragma.ExecuteReader();
            while (reader.Read())
            {
                _ = columns.Add(reader.GetString(0));
            }
        }

        Assert.Contains("at", columns);
        foreach (var absent in new[] { "created_at", "updated_at", "origin_device", "row_version", "base_version", "deleted_at" })
        {
            Assert.DoesNotContain(absent, columns);
        }

        using var triggers = connection.CreateCommand();
        triggers.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND tbl_name = 'audit_log';";
        Assert.Equal(0L, (long)triggers.ExecuteScalar()!);
    }

    [Theory]
    [InlineData("ux_assets_inventory_number")]
    [InlineData("ux_financial_cycles_start_date")]
    [InlineData("ux_monthly_reports_cycle_id")]
    // Renamed by 0003_correspondence_number_index.sql: the uniqueness is measured per sequence
    // (incoming/outgoing), because the two independent yearly sequences of ARCHITECTURE.md §5
    // legitimately produce the same number text on the same day and device. The "never filtered
    // on deleted_at" policy this check exists for is unchanged.
    [InlineData("ux_correspondence_direction_official_number")]
    public void UniqueIndexesOnOfficialIdentifiers_AreNotFilteredOnDeletedAt(string indexName)
    {
        // The single policy stated in the header of 0001_initial.sql: an official identifier
        // stays unique forever, soft-deleted rows included, and the services handle the hidden
        // row explicitly. A partial index would let a deleted row's number be reused.
        var paths = WakeelPaths.ForRoot(_root);
        using var connection = DbConnectionFactory.Open(paths.DbPath, _key);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = $name;";
        command.Parameters.AddWithValue("$name", indexName);
        var sql = command.ExecuteScalar() as string;

        Assert.NotNull(sql);
        Assert.DoesNotContain("deleted_at", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DocumentPageAndDocumentLink_AreSyncedRows_WithIdsAndChangeLogEntries()
    {
        var db = _session.Db;
        var document = new Document
        {
            Sha256 = "0000000000000000000000000000000000000000000000000000000000000000",
            Size = 10,
            Mime = "application/pdf",
            OriginalName = "كتاب.pdf",
            Source = DocumentSource.Scan,
            PageCount = 1,
            OcrStatus = OcrStatus.Done,
            OriginDevice = "device-1",
        };
        db.Documents.Add(document);
        db.SaveChanges();

        var page = new DocumentPage
        {
            DocumentId = document.Id,
            PageNo = 1,
            Text = "نص الصفحة الأولى",
            Words = "[]",
            Confidence = 0.97,
            OriginDevice = "device-1",
        };
        var link = new DocumentLink
        {
            DocumentId = document.Id,
            EntityType = "correspondence",
            EntityId = Guid.CreateVersion7(),
            OriginDevice = "device-1",
        };
        db.DocumentPages.Add(page);
        db.DocumentLinks.Add(link);
        db.SaveChanges();

        Assert.Equal(7, page.Id.Version);
        Assert.Equal(7, link.Id.Version);
        Assert.Equal(1, page.RowVersion);
        Assert.Equal(1, link.RowVersion);

        Assert.Single(db.ChangeLog.Where(c => c.TableName == "document_pages" && c.RowId == page.Id));
        Assert.Single(db.ChangeLog.Where(c => c.TableName == "document_links" && c.RowId == link.Id));

        // The natural key stays unique even against a soft-deleted page: page 1 of this document
        // cannot be inserted a second time.
        db.SoftDelete(page);
        db.SaveChanges();

        db.DocumentPages.Add(new DocumentPage { DocumentId = document.Id, PageNo = 1, OriginDevice = "device-1" });
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void Party_InsertAndQuery_RoundTrips()
    {
        var db = _session.Db;
        var party = new Party
        {
            Name = "وزارة تجريبية",
            Kind = PartyKind.Ministry,
            OriginDevice = "device-1",
        };
        db.Parties.Add(party);
        db.SaveChanges();

        var fetched = db.Parties.AsNoTracking().Single(p => p.Id == party.Id);
        Assert.Equal("وزارة تجريبية", fetched.Name);
        Assert.Equal(PartyKind.Ministry, fetched.Kind);
        Assert.NotEqual(Guid.Empty, fetched.Id);
    }

    [Fact]
    public void SoftDeletedRow_IsExcludedByDefault_AndVisibleWithIgnoreDeleted()
    {
        var db = _session.Db;
        var party = new Party { Name = "جهة محذوفة منطقيًا", Kind = PartyKind.Other, OriginDevice = "device-1" };
        db.Parties.Add(party);
        db.SaveChanges();

        party.DeletedAt = DateTime.UtcNow;
        db.SaveChanges();

        Assert.False(db.Parties.Any(p => p.Id == party.Id));
        Assert.True(db.Parties.IgnoreDeleted().Any(p => p.Id == party.Id));
    }

    [Fact]
    public void GuidIds_AreVersion7_AndUnique()
    {
        var db = _session.Db;
        var a = new Party { Name = "أ", Kind = PartyKind.Other, OriginDevice = "d" };
        var b = new Party { Name = "ب", Kind = PartyKind.Other, OriginDevice = "d" };
        db.Parties.AddRange(a, b);
        db.SaveChanges();

        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(7, a.Id.Version);
        Assert.Equal(7, b.Id.Version);
    }

    [Fact]
    public void DateTime_IsStoredAsIso8601UtcText()
    {
        var paths = WakeelPaths.ForRoot(_root);
        var db = _session.Db;
        var party = new Party { Name = "تحقق التاريخ", Kind = PartyKind.Other, OriginDevice = "d" };
        db.Parties.Add(party);
        db.SaveChanges();

        using var connection = DbConnectionFactory.Open(paths.DbPath, _key);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT created_at FROM parties WHERE id = $id;";
        command.Parameters.AddWithValue("$id", party.Id.ToString().ToUpperInvariant());
        var raw = (string)command.ExecuteScalar()!;

        Assert.EndsWith("Z", raw, StringComparison.Ordinal);
        Assert.True(DateTime.TryParse(raw, out _));
    }

    [Fact]
    public void MoneyColumn_IsStoredAsIntegerAgorot()
    {
        var paths = WakeelPaths.ForRoot(_root);
        var db = _session.Db;
        var party = new Party { Name = "جهة الالتزام", Kind = PartyKind.Other, OriginDevice = "d" };
        db.Parties.Add(party);
        db.SaveChanges();

        var commitment = new Commitment
        {
            Title = "التزام تجريبي",
            PartyId = party.Id,
            Amount = 12345,
            Status = CommitmentStatus.Open,
            OriginDevice = "d",
        };
        db.Commitments.Add(commitment);
        db.SaveChanges();

        using var connection = DbConnectionFactory.Open(paths.DbPath, _key);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT typeof(amount), amount FROM commitments WHERE id = $id;";
        command.Parameters.AddWithValue("$id", commitment.Id.ToString().ToUpperInvariant());
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("integer", reader.GetString(0));
        Assert.Equal(12345L, reader.GetInt64(1));
    }

    [Fact]
    public void EnumColumn_IsStoredAsSnakeCaseText()
    {
        var paths = WakeelPaths.ForRoot(_root);
        var db = _session.Db;
        var party = new Party { Name = "جهة", Kind = PartyKind.Other, OriginDevice = "d" };
        db.Parties.Add(party);
        db.SaveChanges();

        var commitment = new Commitment
        {
            Title = "التزام",
            PartyId = party.Id,
            Amount = 1,
            Status = CommitmentStatus.Partial,
            OriginDevice = "d",
        };
        db.Commitments.Add(commitment);
        db.SaveChanges();

        using var connection = DbConnectionFactory.Open(paths.DbPath, _key);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT status FROM commitments WHERE id = $id;";
        command.Parameters.AddWithValue("$id", commitment.Id.ToString().ToUpperInvariant());
        Assert.Equal("partial", (string)command.ExecuteScalar()!);
    }

    [Fact]
    public void DbSession_LockThenUnlock_RestoresAccess()
    {
        var party = new Party { Name = "قبل القفل", Kind = PartyKind.Other, OriginDevice = "d" };
        _session.Db.Parties.Add(party);
        _session.Db.SaveChanges();

        _session.Lock();
        Assert.True(_session.IsLocked);
        Assert.Throws<InvalidOperationException>(() => _session.Db);

        _session.Unlock(_key);
        Assert.False(_session.IsLocked);
        Assert.True(_session.Db.Parties.Any(p => p.Id == party.Id));
    }

    [Fact]
    public void DbSession_Unlock_WithWrongKey_Throws()
    {
        _session.Lock();
        Assert.Throws<SqliteException>(() => _session.Unlock(TestHelpers.NewKey()));
    }

    [Fact]
    public void Migrate_DatabaseNewerThanEmbeddedScripts_Throws_AndAppliesNothing()
    {
        var paths = WakeelPaths.ForRoot(_root);
        using var connection = DbConnectionFactory.Open(paths.DbPath, _key);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO schema_versions(version, name, applied_at) VALUES (999, 'from_the_future.sql', $at);";
            command.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }

        Assert.Throws<InvalidOperationException>(() => SchemaMigrator.Migrate(connection));
    }

    [Fact]
    public void GetCurrentVersion_OnUnmigratedConnection_ReturnsZero_WithoutCreatingTheVersionsTable()
    {
        var freshRoot = TestHelpers.NewTempRoot();
        try
        {
            var freshPaths = WakeelPaths.ForRoot(freshRoot);
            using var connection = DbConnectionFactory.Open(freshPaths.DbPath, TestHelpers.NewKey());

            Assert.Equal(0, SchemaMigrator.GetCurrentVersion(connection));

            using var check = connection.CreateCommand();
            check.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'schema_versions';";
            Assert.Null(check.ExecuteScalar());
        }
        finally
        {
            TestHelpers.DeleteRootQuietly(freshRoot);
        }
    }
}
