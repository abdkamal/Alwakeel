using System.Diagnostics;
using Wakeel.Core.Services;

namespace Wakeel.Core.Tests;

/// <summary>
/// The B2 specification's performance requirement: the attention center and the badges answer in
/// under 200 ms on 10,000 mixed rows.
/// </summary>
/// <remarks>
/// <para>
/// A timing assertion on a shared build agent is a blunt instrument, so this test is written to
/// fail only on a real regression: the budget it asserts is <see cref="BudgetMs"/>, several times
/// the measured cost, and every run prints its own numbers so a reviewer reading the test output
/// can see how much head-room there actually was rather than inferring it from a pass.
/// </para>
/// <para>
/// The first call of each service is excluded from the measurement. It pays for EF Core's model
/// compilation and query-plan caching, which happen once per process and are not what the
/// specification is about; a screen refreshing the attention center is always paying the warm cost.
/// </para>
/// </remarks>
public sealed class DailyShellPerformanceTests : IDisposable
{
    /// <summary>The budget this test fails at. The specification's limit is 200 ms.</summary>
    private const int BudgetMs = 200;

    /// <summary>How many timed runs are made; the median is compared against the budget.</summary>
    private const int Runs = 5;

    private static readonly DateTime Now = new(2026, 9, 16, 9, 0, 0, DateTimeKind.Utc);

    private readonly DailyShellWorld _world = new(Now);

    public void Dispose() => _world.Dispose();

    [Fact]
    public async Task TheAttentionCenterAndTheBadgesAnswerWithinTheBudgetOn10kRows()
    {
        var seedWatch = Stopwatch.StartNew();
        var seed = DailyShellSeed.Fill(_world);
        seedWatch.Stop();

        Assert.Equal(10_000, seed.Shape.Total);

        // Warm-up: EF Core compiles the model and caches query plans on first use.
        await _world.Attention.GetSnapshotAsync(Now);
        await _world.Badges.RefreshAsync(Now);

        var attentionMs = await MedianAsync(async () => await _world.Attention.GetSnapshotAsync(Now));
        var badgeMs = await MedianAsync(async () => await _world.Badges.RefreshAsync(Now));
        var countsMs = await MedianAsync(async () => await _world.Attention.GetCountsAsync(Now));

        // The numbers are part of the test's output, not only its verdict: a reviewer should be
        // able to read the actual cost, and the head-room, off a passing run.
        Console.WriteLine(
            $"[b2-services] rows={seed.Shape.Total} seed={seedWatch.ElapsedMilliseconds}ms " +
            $"attention-snapshot={attentionMs}ms badges-refresh={badgeMs}ms attention-counts={countsMs}ms budget={BudgetMs}ms");

        Assert.True(attentionMs < BudgetMs, $"attention snapshot took {attentionMs}ms, budget {BudgetMs}ms");
        Assert.True(badgeMs < BudgetMs, $"badge refresh took {badgeMs}ms, budget {BudgetMs}ms");
        Assert.True(countsMs < BudgetMs, $"attention counts took {countsMs}ms, budget {BudgetMs}ms");
    }

    [Fact]
    public async Task On10kRows_TheServicesStillAgreeWithWhatWasSeeded()
    {
        // Speed is worthless if the answer is wrong, so the same 10,000-row set is checked for
        // correctness: the seeder knows exactly how many rows it made late, near, stale and
        // pending, and the services must find precisely those.
        var seed = DailyShellSeed.Fill(_world);

        var counts = await _world.Attention.GetCountsAsync(Now);

        Assert.Equal(seed.Late, counts.Late);
        Assert.Equal(seed.Near, counts.Near);
        Assert.Equal(seed.Stale, counts.Stale);
        Assert.Equal(seed.Pending, counts.PendingConfirmation);
        Assert.Equal(seed.AttentionTotal, counts.Total);

        // Roughly a third of the seeded rows need attention; the rest are settled and must not
        // surface at all.
        Assert.True(counts.Total < seed.Shape.Total / 2, $"{counts.Total} of {seed.Shape.Total} rows surfaced");

        var badges = await _world.Badges.RefreshAsync(Now);
        Assert.Equal(counts.Total, badges.For(BadgeKeys.Attention));

        // The group header still counts each record once, at this scale too.
        var dailyWorkSum = BadgeKeys.Groups[BadgeKeys.GroupDailyWork].Sum(badges.For);
        Assert.True(badges.For(BadgeKeys.GroupDailyWork) < dailyWorkSum);
        Assert.Equal(counts.Total, badges.For(BadgeKeys.GroupDailyWork));
    }

    private static async Task<long> MedianAsync(Func<Task> action)
    {
        var timings = new List<long>(Runs);
        for (var i = 0; i < Runs; i++)
        {
            var watch = Stopwatch.StartNew();
            await action();
            watch.Stop();
            timings.Add(watch.ElapsedMilliseconds);
        }

        timings.Sort();
        return timings[timings.Count / 2];
    }
}
