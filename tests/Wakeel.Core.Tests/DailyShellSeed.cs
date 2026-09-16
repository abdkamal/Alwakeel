using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;

namespace Wakeel.Core.Tests;

/// <summary>
/// How many rows of each table <see cref="DailyShellSeed"/> writes. The defaults add up to the
/// 10,000 mixed rows the B2 specification measures the attention center and the badges against.
/// </summary>
/// <param name="Correspondence">Correspondence rows.</param>
/// <param name="Tasks">Task rows.</param>
/// <param name="Commitments">Commitment rows.</param>
/// <param name="Cases">Case rows.</param>
/// <param name="Decisions">Decision rows.</param>
/// <param name="PhoneExpenses">Phone-expense rows.</param>
internal sealed record SeedShape(
    int Correspondence = 3000,
    int Tasks = 3000,
    int Commitments = 1500,
    int Cases = 1000,
    int Decisions = 1000,
    int PhoneExpenses = 500)
{
    /// <summary>Total rows written.</summary>
    public int Total => Correspondence + Tasks + Commitments + Cases + Decisions + PhoneExpenses;

    /// <summary>Rows in the five due-date-carrying tables (everything except phone expenses).</summary>
    public int Dated => Correspondence + Tasks + Commitments + Cases + Decisions;
}

/// <summary>What a seeding run produced, so a test can assert the services agree with it.</summary>
/// <param name="Shape">The shape that was asked for.</param>
/// <param name="Late">Rows deliberately made overdue.</param>
/// <param name="Near">Rows deliberately made due soon.</param>
/// <param name="Stale">Rows deliberately left untouched.</param>
/// <param name="Pending">Phone expenses deliberately left awaiting confirmation.</param>
internal sealed record SeedResult(SeedShape Shape, int Late, int Near, int Stale, int Pending)
{
    /// <summary>Rows expected in the attention center, in total.</summary>
    public int AttentionTotal => Late + Near + Stale + Pending;
}

/// <summary>
/// Fills a <see cref="DailyShellWorld"/> with a realistic mix of records: overdue, due soon,
/// untouched, awaiting confirmation, and plenty of settled rows that must NOT surface. Used both
/// by the threshold tests (as a small deterministic set) and by the performance measurement (as
/// the 10,000-row set the specification names).
/// </summary>
/// <remarks>
/// <para>
/// The mix is deterministic, not random: every tenth row is overdue, the next is due tomorrow,
/// the next has been untouched for a month, and the remaining seven are settled or comfortably in
/// the future. A test can therefore predict the exact counts, and a failure points at the service
/// rather than at an unlucky seed.
/// </para>
/// <para>
/// Rows are written with <c>SuppressAuditStamps</c> so their <c>updated_at</c> is what this
/// helper set it to; without that every row would be stamped "now" on save and nothing would ever
/// look stale.
/// </para>
/// </remarks>
internal static class DailyShellSeed
{
    /// <summary>Rows in every group of ten that are made overdue / due soon / untouched.</summary>
    private const int Period = 10;

    public static SeedResult Fill(DailyShellWorld world, SeedShape? shape = null)
    {
        shape ??= new SeedShape();
        var now = world.Now;
        var db = world.Db;

        // 10k EF inserts with change detection on every Add is quadratic; the seeder writes one
        // batch per table with detection off and re-enables it afterwards.
        var previousAutoDetect = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            var late = 0;
            var near = 0;
            var stale = 0;

            var correspondence = new List<SyncedEntity>(shape.Correspondence);
            for (var i = 0; i < shape.Correspondence; i++)
            {
                var slot = Slot(i, ref late, ref near, ref stale);
                correspondence.Add(new Correspondence
                {
                    Direction = i % 2 == 0 ? InOutDirection.In : InOutDirection.Out,
                    Subject = $"معاملة {i}",
                    OfficialNumber = i % 3 == 0 ? $"2026090{i % 10}/12{i % 1000:000}" : null,
                    PartyNameSnapshot = $"جهة {i % 50}",
                    Status = slot == Slot_.Settled && i % 4 == 0 ? CorrespondenceStatus.Closed : CorrespondenceStatus.InProgress,
                    NextStepAr = "متابعة الرد",
                    DueAt = DueFor(slot, now, i),
                    CreatedAt = now.AddDays(-60),
                    UpdatedAt = UpdatedFor(slot, now, i),
                });
            }

            Write(world, correspondence);

            var tasks = new List<SyncedEntity>(shape.Tasks);
            for (var i = 0; i < shape.Tasks; i++)
            {
                var slot = Slot(i, ref late, ref near, ref stale);
                tasks.Add(new TaskItem
                {
                    Title = $"مهمة {i}",
                    AssigneeName = $"موظف {i % 20}",
                    Priority = (TaskPriority)(i % 3),
                    Status = slot == Slot_.Settled && i % 4 == 0 ? WorkTaskStatus.Done : WorkTaskStatus.Open,
                    DueAt = DueFor(slot, now, i),
                    CreatedAt = now.AddDays(-60),
                    UpdatedAt = UpdatedFor(slot, now, i),
                });
            }

            Write(world, tasks);

            var commitments = new List<SyncedEntity>(shape.Commitments);
            for (var i = 0; i < shape.Commitments; i++)
            {
                var slot = Slot(i, ref late, ref near, ref stale);
                commitments.Add(new Commitment
                {
                    Title = $"التزام {i}",
                    Amount = 1000 + (i * 37 % 90000),
                    Status = slot == Slot_.Settled && i % 4 == 0 ? CommitmentStatus.Paid : CommitmentStatus.Open,
                    DueAt = DueFor(slot, now, i),
                    CreatedAt = now.AddDays(-60),
                    UpdatedAt = UpdatedFor(slot, now, i),
                });
            }

            Write(world, commitments);

            var cases = new List<SyncedEntity>(shape.Cases);
            for (var i = 0; i < shape.Cases; i++)
            {
                var slot = Slot(i, ref late, ref near, ref stale);
                cases.Add(new Case
                {
                    CaseNumber = $"ق/{i:0000}",
                    Title = $"قضية {i}",
                    ResponsibleName = $"مسؤول {i % 10}",
                    Stage = "التحقيق",
                    Status = slot == Slot_.Settled && i % 4 == 0 ? CaseStatus.Closed : CaseStatus.Open,
                    NextHearingAt = DueFor(slot, now, i),
                    CreatedAt = now.AddDays(-60),
                    UpdatedAt = UpdatedFor(slot, now, i),
                });
            }

            Write(world, cases);

            var decisions = new List<SyncedEntity>(shape.Decisions);
            for (var i = 0; i < shape.Decisions; i++)
            {
                var slot = Slot(i, ref late, ref near, ref stale);
                decisions.Add(new Decision
                {
                    Text = $"قرار {i}",
                    OwnerName = $"مسؤول {i % 10}",
                    SourceType = DecisionSourceType.Meeting,
                    DecidedAt = now.AddDays(-30),
                    Status = slot == Slot_.Settled && i % 4 == 0 ? DecisionStatus.Done : DecisionStatus.InProgress,
                    DueAt = DueFor(slot, now, i),
                    CreatedAt = now.AddDays(-60),
                    UpdatedAt = UpdatedFor(slot, now, i),
                });
            }

            Write(world, decisions);

            var pending = 0;
            var phoneDeviceId = world.EnsureDevice();
            var expenses = new List<SyncedEntity>(shape.PhoneExpenses);
            for (var i = 0; i < shape.PhoneExpenses; i++)
            {
                // Three in every five are still awaiting confirmation (AGREEMENT item 50).
                var status = (i % 5) switch
                {
                    0 or 1 or 2 => PhoneExpenseStatus.Pending,
                    3 => PhoneExpenseStatus.Confirmed,
                    _ => PhoneExpenseStatus.Rejected,
                };
                if (status == PhoneExpenseStatus.Pending)
                {
                    pending++;
                }

                expenses.Add(new PhoneExpense
                {
                    PhoneDeviceId = phoneDeviceId,
                    Amount = 500 + (i * 13 % 20000),
                    Purpose = $"مصروف {i}",
                    At = now.AddDays(-(i % 20)),
                    Status = status,
                    CreatedAt = now.AddDays(-(i % 20)),
                    UpdatedAt = now.AddDays(-(i % 20)),
                });
            }

            Write(world, expenses);

            return new SeedResult(shape, late, near, stale, pending);
        }
        finally
        {
            db.ChangeTracker.AutoDetectChangesEnabled = previousAutoDetect;
        }
    }

    private static void Write(DailyShellWorld world, List<SyncedEntity> rows)
    {
        world.AddWithStamps(rows);

        // The batch is committed; drop it from the tracker so the next batch does not re-scan it.
        world.Db.ChangeTracker.Clear();
    }

    private enum Slot_
    {
        Late,
        Near,
        Stale,
        Settled,
    }

    private static Slot_ Slot(int index, ref int late, ref int near, ref int stale)
    {
        switch (index % Period)
        {
            case 0:
                late++;
                return Slot_.Late;
            case 1:
                near++;
                return Slot_.Near;
            case 2:
                stale++;
                return Slot_.Stale;
            default:
                return Slot_.Settled;
        }
    }

    /// <summary>
    /// A due date that puts the row squarely in its slot: well past for overdue, tomorrow for due
    /// soon, none at all for untouched (so only staleness can claim it), and two months out for a
    /// settled row (so no threshold change accidentally drags it in).
    /// </summary>
    private static DateTime? DueFor(Slot_ slot, DateTime now, int index) => slot switch
    {
        Slot_.Late => now.Date.AddDays(-(2 + (index % 15))),
        Slot_.Near => now.Date.AddDays(1),
        Slot_.Stale => null,
        _ => now.Date.AddDays(60),
    };

    /// <summary>A last-touched instant: a month ago for the untouched rows, an hour ago for the rest.</summary>
    private static DateTime UpdatedFor(Slot_ slot, DateTime now, int index) =>
        slot == Slot_.Stale ? now.AddDays(-30 - (index % 10)) : now.AddHours(-1);
}
