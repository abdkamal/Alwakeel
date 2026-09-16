using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;

namespace Wakeel.Core.Tests;

/// <summary>
/// Exercises the ISO-8601 UTC <see cref="DateTime"/> value converter's read side against raw
/// text a foreign writer (raw SQL, a different build, sync-import) could have left in the
/// column, by writing that text directly through SQL and then reading it back through EF.
/// </summary>
public sealed class DateTimeConverterTests : IDisposable
{
    private readonly string _root;
    private readonly byte[] _key;
    private readonly DbSession _session;

    public DateTimeConverterTests()
    {
        _session = TestHelpers.OpenNewSession(out _root, out _key);
    }

    public void Dispose()
    {
        _session.Dispose();
        TestHelpers.DeleteRootQuietly(_root);
    }

    [Theory]
    [InlineData("2026-09-15T12:30:45.1234567Z", 1234567)] // this converter's own 7-digit-fraction writer
    [InlineData("2026-09-15T12:30:45.123Z", 1230000)] // 3-digit fraction, e.g. SQLite strftime('...%fZ')
    [InlineData("2026-09-15T12:30:45Z", 0)] // no fraction
    [InlineData("2026-09-15 12:30:45", 0)] // space-separated, e.g. SQLite datetime('now')
    public void Read_TolerantOfForeignlyWrittenTimestampShapes(string rawText, int fractionTicks)
    {
        var party = SeedPartyWithRawCreatedAt(rawText);

        var fetched = _session.Db.Parties.AsNoTracking().Single(p => p.Id == party);
        Assert.Equal(new DateTime(2026, 9, 15, 12, 30, 45, DateTimeKind.Utc).AddTicks(fractionTicks), fetched.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, fetched.CreatedAt.Kind);
    }

    [Fact]
    public void Read_EmptyString_DoesNotThrow_AndYieldsUtcKind()
    {
        var party = SeedPartyWithRawCreatedAt(string.Empty);

        var fetched = _session.Db.Parties.AsNoTracking().Single(p => p.Id == party);
        Assert.Equal(DateTimeKind.Utc, fetched.CreatedAt.Kind);
    }

    [Fact]
    public void Read_UnparseableText_DoesNotThrow_ButIsReportedOnce_WithItsTableAndColumn()
    {
        // A corrupted NOT NULL timestamp must not take a whole list down through an exception
        // raised inside EF materialization, and must not vanish silently as year 1 either: the
        // read stays tolerant and reports the value so the health center (W12) can show it.
        const string corrupt = "not-a-timestamp-at-all";
        var party = SeedPartyWithRawCreatedAt(corrupt);

        var reported = Capture(corrupt, () =>
        {
            var fetched = _session.Db.Parties.AsNoTracking().Single(p => p.Id == party);
            Assert.Equal(DateTimeKind.Utc, fetched.CreatedAt.Kind);
        });

        var single = Assert.Single(reported);
        Assert.Equal("parties", single.Table);
        Assert.Equal("created_at", single.Column);
        Assert.Equal(corrupt, single.Text);
    }

    [Fact]
    public void Read_ValidText_IsNeverReported()
    {
        const string valid = "2026-09-15T12:30:45.1234567Z";
        var party = SeedPartyWithRawCreatedAt(valid);

        var reported = Capture(valid, () =>
        {
            var fetched = _session.Db.Parties.AsNoTracking().Single(p => p.Id == party);
            Assert.Equal(new DateTime(2026, 9, 15, 12, 30, 45, DateTimeKind.Utc).AddTicks(1234567), fetched.CreatedAt);
        });

        Assert.Empty(reported);
    }

    [Fact]
    public void Read_UnparseableText_IsAlsoKeptInTheSinkForTheHealthCenter()
    {
        const string corrupt = "2026-13-45T99:99:99Z";
        var party = SeedPartyWithRawCreatedAt(corrupt);

        _ = _session.Db.Parties.AsNoTracking().Single(p => p.Id == party);

        Assert.Contains(DataIntegrityLog.Recent, e => e.Text == corrupt && e.Table == "parties" && e.Column == "created_at");
    }

    [Fact]
    public void Read_UnparseableText_IsForwardedToTheContextLoggerAsAWarning()
    {
        // The observable hook is wired end-to-end: the value converter reports, WakeelDb forwards
        // to its ILogger as a warning, and the message carries the table, the column and the raw
        // text so the row can be found and repaired.
        const string corrupt = "1445-13-99 corrupted by a bad restore";
        var logger = new RecordingLogger();
        var root = TestHelpers.NewTempRoot();
        try
        {
            var key = TestHelpers.NewKey();
            var paths = WakeelPaths.ForRoot(root);
            using var session = DbSession.Open(paths, key, clock: null, logger: logger);

            var party = new Party { Name = "جهة", Kind = PartyKind.Other, OriginDevice = "d" };
            session.Db.Parties.Add(party);
            session.Db.SaveChanges();
            WriteRawCreatedAt(paths, key, party.Id, corrupt);

            // Not Assert.Empty(logger.Warnings): DataIntegrityLog.Reported is a static,
            // process-wide event and WakeelDb subscribes to it for as long as this session is
            // open, so a concurrently running xunit test class that reports an unrelated
            // unreadable timestamp would also reach this logger and make an unfiltered
            // precondition flake. Filter by the unique corrupt text instead, matching every other
            // assertion in this file.
            Assert.DoesNotContain(logger.Warnings, w => w.Contains(corrupt, StringComparison.Ordinal));
            _ = session.Db.Parties.AsNoTracking().Single(p => p.Id == party.Id);

            var warning = Assert.Single(logger.Warnings, w => w.Contains(corrupt, StringComparison.Ordinal));
            Assert.Contains("parties", warning, StringComparison.Ordinal);
            Assert.Contains("created_at", warning, StringComparison.Ordinal);
        }
        finally
        {
            TestHelpers.DeleteRootQuietly(root);
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> while collecting the <see cref="DataIntegrityLog"/> events
    /// whose raw text is <paramref name="text"/>. Filtering by the text keeps the assertion exact
    /// even though the sink is process-wide and xunit runs test classes in parallel.
    /// </summary>
    private static List<DataIntegrityEvent> Capture(string text, Action action)
    {
        var captured = new List<DataIntegrityEvent>();
        void Handler(DataIntegrityEvent e)
        {
            if (string.Equals(e.Text, text, StringComparison.Ordinal))
            {
                lock (captured)
                {
                    captured.Add(e);
                }
            }
        }

        DataIntegrityLog.Reported += Handler;
        try
        {
            action();
        }
        finally
        {
            DataIntegrityLog.Reported -= Handler;
        }

        return captured;
    }

    private Guid SeedPartyWithRawCreatedAt(string rawText)
    {
        var db = _session.Db;
        var party = new Party { Name = "جهة", Kind = PartyKind.Other, OriginDevice = "d" };
        db.Parties.Add(party);
        db.SaveChanges();

        WriteRawCreatedAt(WakeelPaths.ForRoot(_root), _key, party.Id, rawText);
        return party.Id;
    }

    private static void WriteRawCreatedAt(WakeelPaths paths, byte[] key, Guid partyId, string rawText)
    {
        using var connection = DbConnectionFactory.Open(paths.DbPath, key);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE parties SET created_at = $raw WHERE id = $id;";
        command.Parameters.AddWithValue("$raw", rawText);
        command.Parameters.AddWithValue("$id", partyId.ToString().ToUpperInvariant());
        _ = command.ExecuteNonQuery();
    }
}
