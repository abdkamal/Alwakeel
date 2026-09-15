using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;

namespace Wakeel.Core.Tests;

public sealed class ChangeLogTriggerTests : IDisposable
{
    private readonly string _root;
    private readonly byte[] _key;
    private readonly DbSession _session;

    public ChangeLogTriggerTests()
    {
        _session = TestHelpers.OpenNewSession(out _root, out _key);
    }

    public void Dispose()
    {
        _session.Dispose();
        TestHelpers.DeleteRootQuietly(_root);
    }

    [Fact]
    public void Insert_OnSyncedTable_WritesChangeLogRow()
    {
        var db = _session.Db;
        var party = new Party { Name = "جهة", Kind = PartyKind.Other, OriginDevice = "device-1" };
        db.Parties.Add(party);
        db.SaveChanges();

        var entries = db.ChangeLog.Where(c => c.TableName == "parties" && c.RowId == party.Id).ToList();
        var insertEntry = Assert.Single(entries);
        Assert.Equal(ChangeOperation.Insert, insertEntry.Op);
        Assert.Equal("device-1", insertEntry.Device);
    }

    [Fact]
    public void Update_OnSyncedTable_WritesSecondChangeLogRow()
    {
        var db = _session.Db;
        var party = new Party { Name = "جهة", Kind = PartyKind.Other, OriginDevice = "device-1" };
        db.Parties.Add(party);
        db.SaveChanges();

        party.Name = "جهة معدّلة";
        party.OriginDevice = "device-1";
        db.SaveChanges();

        var entries = db.ChangeLog.Where(c => c.TableName == "parties" && c.RowId == party.Id)
            .OrderBy(c => c.Seq).ToList();

        Assert.Equal(2, entries.Count);
        Assert.Equal(ChangeOperation.Insert, entries[0].Op);
        Assert.Equal(ChangeOperation.Update, entries[1].Op);
    }

    [Fact]
    public void MultipleUpdates_EachWriteASeparateChangeLogRow()
    {
        var db = _session.Db;
        var task = new TaskItem { Title = "مهمة", Priority = TaskPriority.Normal, Status = WorkTaskStatus.Open, OriginDevice = "d" };
        db.Tasks.Add(task);
        db.SaveChanges();

        for (var i = 0; i < 3; i++)
        {
            task.Progress = (i + 1) * 10;
            db.SaveChanges();
        }

        var updateCount = db.ChangeLog.Count(c => c.TableName == "tasks" && c.RowId == task.Id && c.Op == ChangeOperation.Update);
        Assert.Equal(3, updateCount);
    }

    [Fact]
    public void Insert_OnLocalTable_DoesNotWriteChangeLogRow()
    {
        var db = _session.Db;
        var before = db.ChangeLog.Count();

        db.Settings.Add(new Setting { Key = "x", Value = "1", UpdatedAt = DateTime.UtcNow });
        db.SaveChanges();

        Assert.Equal(before, db.ChangeLog.Count());
    }

    [Theory]
    [InlineData("correspondence")]
    [InlineData("tasks")]
    [InlineData("decisions")]
    [InlineData("commitments")]
    [InlineData("meetings")]
    [InlineData("cases")]
    [InlineData("employees")]
    [InlineData("assets")]
    [InlineData("transactions")]
    [InlineData("monthly_reports")]
    public void SyncedTable_HasWorkingInsertTrigger(string tableName)
    {
        var paths = WakeelPaths.ForRoot(_root);
        using var connection = DbConnectionFactory.Open(paths.DbPath, _key);
        using var check = connection.CreateCommand();
        check.CommandText = "SELECT name FROM sqlite_master WHERE type='trigger' AND name = $name;";
        check.Parameters.AddWithValue("$name", $"trg_{tableName}_ai");
        Assert.NotNull(check.ExecuteScalar());
    }

    [Fact]
    public void Insert_ThenUpdate_SetsAuditColumnsAutomatically_AndChangeLogAtMatchesUpdatedAt()
    {
        var clock = new TestClock { UtcNow = new DateTime(2026, 9, 15, 8, 0, 0, DateTimeKind.Utc) };
        using var session = TestHelpers.OpenNewSession(out var root, out _, clock);
        try
        {
            var db = session.Db;
            TestHelpers.SeedInstallation(db);

            var party = new Party { Name = "جهة", Kind = PartyKind.Other };
            db.Parties.Add(party);
            db.SaveChanges();

            var createdAt = party.CreatedAt;
            Assert.Equal(clock.UtcNow, createdAt);
            Assert.Equal(clock.UtcNow, party.UpdatedAt);
            Assert.Equal(1, party.RowVersion);
            Assert.False(string.IsNullOrEmpty(party.OriginDevice));

            clock.UtcNow = clock.UtcNow.AddMinutes(5);
            party.Name = "جهة معدّلة";
            db.SaveChanges();

            Assert.Equal(createdAt, party.CreatedAt);
            Assert.NotEqual(createdAt, party.UpdatedAt);
            Assert.Equal(clock.UtcNow, party.UpdatedAt);
            Assert.Equal(2, party.RowVersion);

            var updateEntry = db.ChangeLog.Single(c => c.TableName == "parties" && c.RowId == party.Id && c.Op == ChangeOperation.Update);
            Assert.Equal(party.UpdatedAt, updateEntry.At);
        }
        finally
        {
            session.Dispose();
            TestHelpers.DeleteRootQuietly(root);
        }
    }

    [Fact]
    public void Update_ByLocalDevice_ChangeLogRecordsLocalDevice_NotTheOriginatingDevice()
    {
        var db = _session.Db;
        var installation = TestHelpers.SeedInstallation(db);
        var localDeviceId = installation.DeviceId.ToString();

        var party = new Party { Name = "جهة", Kind = PartyKind.Other, OriginDevice = "device-from-another-pc" };
        db.Parties.Add(party);
        db.SaveChanges();

        party.Name = "جهة معدّلة من هذا الجهاز";
        db.SaveChanges();

        var insertEntry = db.ChangeLog.Single(c => c.TableName == "parties" && c.RowId == party.Id && c.Op == ChangeOperation.Insert);
        Assert.Equal("device-from-another-pc", insertEntry.Device);

        var updateEntry = db.ChangeLog.Single(c => c.TableName == "parties" && c.RowId == party.Id && c.Op == ChangeOperation.Update);
        Assert.Equal(localDeviceId, updateEntry.Device, StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual("device-from-another-pc", updateEntry.Device);
    }
}
