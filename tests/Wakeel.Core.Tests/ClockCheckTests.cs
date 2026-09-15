using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using Wakeel.Core.Services;

namespace Wakeel.Core.Tests;

public sealed class ClockCheckTests : IDisposable
{
    private readonly string _root;
    private readonly byte[] _key;
    private readonly DbSession _session;
    private readonly IClockCheckService _service;

    public ClockCheckTests()
    {
        _session = TestHelpers.OpenNewSession(out _root, out _key);
        TestHelpers.SeedInstallation(
            _session.Db,
            buildDate: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            activatedAt: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        _service = new ClockCheckService(_session.Db);
    }

    public void Dispose()
    {
        _session.Dispose();
        TestHelpers.DeleteRootQuietly(_root);
    }

    [Fact]
    public async Task Check_NowAfterBuildDateAndActivation_IsOk()
    {
        var result = await _service.CheckAsync(new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(ClockVerdict.Ok, result.Verdict);
    }

    [Fact]
    public async Task Check_NowBeforeBuildDate_IsBad()
    {
        var result = await _service.CheckAsync(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(ClockVerdict.Bad, result.Verdict);
    }

    [Fact]
    public async Task Check_NowFarBeforeLastActivity_IsBad()
    {
        await _service.CheckAsync(new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc));

        var result = await _service.CheckAsync(new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(ClockVerdict.Bad, result.Verdict);
    }

    [Fact]
    public async Task Check_NowWithinFiveMinutesBeforeLastActivity_IsOk()
    {
        var first = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
        await _service.CheckAsync(first);

        var result = await _service.CheckAsync(first.AddMinutes(-2));
        Assert.Equal(ClockVerdict.Ok, result.Verdict);
    }

    [Fact]
    public async Task Check_OtherDeviceFarAhead_IsSuspect()
    {
        var now = new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc);
        var otherDeviceNow = now.AddHours(2);

        var result = await _service.CheckAsync(now, [otherDeviceNow]);
        Assert.Equal(ClockVerdict.Suspect, result.Verdict);
    }

    [Fact]
    public async Task Check_PersistsEachResultToClockChecksTable()
    {
        await _service.CheckAsync(new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc));
        await _service.CheckAsync(new DateTime(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(2, _session.Db.ClockChecks.Count());
    }
}
