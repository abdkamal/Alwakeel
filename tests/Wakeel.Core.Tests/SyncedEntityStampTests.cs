using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;

namespace Wakeel.Core.Tests;

/// <summary>
/// The DATA-MODEL.md §0 rules <see cref="WakeelDb"/> enforces on every synced ("official") row:
/// automatic audit stamps, the sync-import exemption from them
/// (<see cref="WakeelDb.SuppressAuditStamps"/>), and the ban on physical deletion.
/// </summary>
public sealed class SyncedEntityStampTests : IDisposable
{
    private static readonly DateTime ImportedCreatedAt = new(2026, 5, 1, 7, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ImportedUpdatedAt = new(2026, 6, 2, 8, 30, 0, DateTimeKind.Utc);
    private const string ImportedOriginDevice = "9F1C2A70-0000-7000-8000-00000000AAAA";

    private readonly string _root;
    private readonly byte[] _key;
    private readonly TestClock _clock = new() { UtcNow = new DateTime(2026, 9, 16, 10, 0, 0, DateTimeKind.Utc) };
    private readonly DbSession _session;

    public SyncedEntityStampTests()
    {
        _session = TestHelpers.OpenNewSession(out _root, out _key, _clock);
    }

    public void Dispose()
    {
        _session.Dispose();
        TestHelpers.DeleteRootQuietly(_root);
    }

    // ---- SuppressAuditStamps: the sync-import path (ARCHITECTURE.md §6) --------------------

    [Fact]
    public void SuppressAuditStamps_OnInsert_KeepsEveryCallerSetAuditColumn()
    {
        var db = _session.Db;
        TestHelpers.SeedInstallation(db);

        var imported = new Party
        {
            Name = "جهة واردة من جهاز آخر",
            Kind = PartyKind.Other,
            CreatedAt = ImportedCreatedAt,
            UpdatedAt = ImportedUpdatedAt,
            OriginDevice = ImportedOriginDevice,
            RowVersion = 9,
            BaseVersion = 8,
        };

        using (db.SuppressAuditStamps())
        {
            db.Parties.Add(imported);
            db.SaveChanges();
        }

        var stored = db.Parties.AsNoTracking().Single(p => p.Id == imported.Id);
        Assert.Equal(ImportedCreatedAt, stored.CreatedAt);
        Assert.Equal(ImportedUpdatedAt, stored.UpdatedAt);
        Assert.Equal(ImportedOriginDevice, stored.OriginDevice);
        Assert.Equal(9, stored.RowVersion);
        Assert.Equal(8, stored.BaseVersion);
    }

    [Fact]
    public void SuppressAuditStamps_OnUpdate_KeepsTheIncomingRowVersionAndTimestamp()
    {
        var db = _session.Db;
        TestHelpers.SeedInstallation(db);

        var party = new Party { Name = "جهة", Kind = PartyKind.Other };
        db.Parties.Add(party);
        db.SaveChanges();

        // A newer copy of the same row arrives from the originating device.
        using (db.SuppressAuditStamps())
        {
            party.Name = "جهة بعد تعديل على الجهاز الآخر";
            party.UpdatedAt = ImportedUpdatedAt;
            party.RowVersion = 12;
            party.BaseVersion = 11;
            db.SaveChanges();
        }

        var stored = db.Parties.AsNoTracking().Single(p => p.Id == party.Id);
        Assert.Equal(ImportedUpdatedAt, stored.UpdatedAt);
        Assert.Equal(12, stored.RowVersion);
        Assert.Equal(11, stored.BaseVersion);
    }

    [Fact]
    public void OutsideTheSuppressionScope_TheAutomaticStampsStillApply()
    {
        var db = _session.Db;
        var installation = TestHelpers.SeedInstallation(db);

        using (db.SuppressAuditStamps())
        {
            db.Parties.Add(new Party
            {
                Name = "جهة مستوردة",
                Kind = PartyKind.Other,
                CreatedAt = ImportedCreatedAt,
                UpdatedAt = ImportedUpdatedAt,
                OriginDevice = ImportedOriginDevice,
                RowVersion = 9,
            });
            db.SaveChanges();
        }

        var local = new Party { Name = "جهة أُنشئت هنا", Kind = PartyKind.Other };
        db.Parties.Add(local);
        db.SaveChanges();

        Assert.Equal(_clock.UtcNow, local.CreatedAt);
        Assert.Equal(_clock.UtcNow, local.UpdatedAt);
        Assert.Equal(1, local.RowVersion);
        Assert.Equal(installation.DeviceId.ToString(), local.OriginDevice, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuppressAuditStamps_AppliesToSaveChangesAsyncToo_AndNestsSafely()
    {
        var db = _session.Db;
        TestHelpers.SeedInstallation(db);

        var imported = new Party
        {
            Name = "جهة واردة",
            Kind = PartyKind.Other,
            CreatedAt = ImportedCreatedAt,
            UpdatedAt = ImportedUpdatedAt,
            OriginDevice = ImportedOriginDevice,
            RowVersion = 5,
        };

        using (db.SuppressAuditStamps())
        {
            using (db.SuppressAuditStamps())
            {
                db.Parties.Add(imported);
                await db.SaveChangesAsync();
            }

            // Still inside the outer scope: the stamps must stay suppressed.
            imported.Name = "جهة واردة معدّلة";
            imported.UpdatedAt = ImportedUpdatedAt;
            imported.RowVersion = 6;
            await db.SaveChangesAsync();
        }

        var stored = db.Parties.AsNoTracking().Single(p => p.Id == imported.Id);
        Assert.Equal(ImportedCreatedAt, stored.CreatedAt);
        Assert.Equal(ImportedUpdatedAt, stored.UpdatedAt);
        Assert.Equal(6, stored.RowVersion);

        // The outermost scope is closed now, so an ordinary edit is stamped again.
        _clock.UtcNow = _clock.UtcNow.AddHours(1);
        imported.Name = "جهة واردة عُدّلت محليًا";
        await db.SaveChangesAsync();

        var restamped = db.Parties.AsNoTracking().Single(p => p.Id == imported.Id);
        Assert.Equal(_clock.UtcNow, restamped.UpdatedAt);
        Assert.Equal(7, restamped.RowVersion);
    }

    // ---- Remove() is always a soft delete (DATA-MODEL.md §0, ARCHITECTURE.md §12) -----------

    [Fact]
    public void Remove_OnSyncedEntity_BecomesASoftDelete_AndWritesAnUpdateChangeLogEntry()
    {
        var db = _session.Db;
        TestHelpers.SeedInstallation(db);

        var party = new Party { Name = "جهة ستُحذف", Kind = PartyKind.Other };
        db.Parties.Add(party);
        db.SaveChanges();
        var rowVersionBeforeDelete = party.RowVersion;

        _clock.UtcNow = _clock.UtcNow.AddMinutes(30);
        db.Parties.Remove(party);
        db.SaveChanges();

        // The row is still there physically — read past the global filter and past EF entirely.
        var hidden = db.Parties.IgnoreDeleted().AsNoTracking().Single(p => p.Id == party.Id);
        Assert.Equal(_clock.UtcNow, hidden.DeletedAt);
        Assert.Equal(_clock.UtcNow, hidden.UpdatedAt);
        Assert.Equal(rowVersionBeforeDelete + 1, hidden.RowVersion);
        Assert.Equal(1, CountRawRows("parties", party.Id));

        // Hidden from the ordinary queries every list uses ...
        Assert.False(db.Parties.Any(p => p.Id == party.Id));

        // ... and visible to sync as an update, not as a disappearance.
        var entries = db.ChangeLog.Where(c => c.TableName == "parties" && c.RowId == party.Id)
            .OrderBy(c => c.Seq).ToList();
        Assert.Equal(2, entries.Count);
        Assert.Equal(ChangeOperation.Insert, entries[0].Op);
        Assert.Equal(ChangeOperation.Update, entries[1].Op);
    }

    [Fact]
    public async Task RemoveRange_OnSaveChangesAsync_AlsoBecomesSoftDeletes()
    {
        var db = _session.Db;
        TestHelpers.SeedInstallation(db);

        var first = new Party { Name = "أ", Kind = PartyKind.Other };
        var second = new Party { Name = "ب", Kind = PartyKind.Other };
        db.Parties.AddRange(first, second);
        await db.SaveChangesAsync();

        db.Parties.RemoveRange(first, second);
        await db.SaveChangesAsync();

        Assert.Equal(1, CountRawRows("parties", first.Id));
        Assert.Equal(1, CountRawRows("parties", second.Id));
        Assert.Equal(2, db.Parties.IgnoreDeleted().Count(p => p.DeletedAt != null));
    }

    [Fact]
    public void SoftDelete_Helper_HidesTheRowWithoutRemovingIt()
    {
        var db = _session.Db;
        TestHelpers.SeedInstallation(db);

        var task = new TaskItem { Title = "مهمة", Priority = TaskPriority.Normal, Status = WorkTaskStatus.Open };
        db.Tasks.Add(task);
        db.SaveChanges();

        _clock.UtcNow = _clock.UtcNow.AddMinutes(10);
        db.SoftDelete(task);
        db.SaveChanges();

        Assert.NotNull(task.DeletedAt);
        Assert.Equal(_clock.UtcNow, task.DeletedAt);
        Assert.False(db.Tasks.Any(t => t.Id == task.Id));
        Assert.Equal(1, CountRawRows("tasks", task.Id));
    }

    [Fact]
    public void SoftDelete_OnANotYetInsertedEntity_JustCancelsTheInsert()
    {
        var db = _session.Db;
        TestHelpers.SeedInstallation(db);

        var note = new Note { Text = "ملاحظة لم تُحفظ بعد" };
        db.Notes.Add(note);
        db.SoftDelete(note);
        db.SaveChanges();

        Assert.Equal(0, CountRawRows("notes", note.Id));
    }

    // ---- The local device id must never be cached as empty -----------------------------------

    [Fact]
    public void RowsWrittenAfterSetup_AreAttributed_EvenWhenARowWasWrittenBeforeIt()
    {
        // A synced row written before .wakeel-setup has been consumed cannot be attributed; the
        // empty answer must NOT be cached, or every row written later on the same long-lived
        // context would silently carry an empty origin_device too.
        var db = _session.Db;

        var beforeSetup = new Party { Name = "جهة قبل التفعيل", Kind = PartyKind.Other };
        db.Parties.Add(beforeSetup);
        db.SaveChanges();
        Assert.Equal(string.Empty, beforeSetup.OriginDevice);

        var installation = TestHelpers.SeedInstallation(db);

        var afterSetup = new Party { Name = "جهة بعد التفعيل", Kind = PartyKind.Other };
        db.Parties.Add(afterSetup);
        db.SaveChanges();

        Assert.False(string.IsNullOrEmpty(afterSetup.OriginDevice));
        Assert.Equal(installation.DeviceId.ToString(), afterSetup.OriginDevice, StringComparer.OrdinalIgnoreCase);
    }

    private int CountRawRows(string table, Guid id)
    {
        var paths = WakeelPaths.ForRoot(_root);
        using var connection = DbConnectionFactory.Open(paths.DbPath, _key);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString().ToUpperInvariant());
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
