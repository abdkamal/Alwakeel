using Wakeel.Core.Data;
using Wakeel.Core.Services;

namespace Wakeel.Core.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _root;
    private readonly DbSession _session;
    private readonly ISettingsService _service;

    public SettingsTests()
    {
        _session = TestHelpers.OpenNewSession(out _root, out _);
        _service = new SettingsService(_session.Db, new SystemClock(TimeProvider.System));
    }

    public void Dispose()
    {
        _session.Dispose();
        TestHelpers.DeleteRootQuietly(_root);
    }

    [Fact]
    public async Task AttentionLateDays_DefaultsToOne()
    {
        Assert.Equal(1, await _service.GetAttentionLateDaysAsync());
    }

    [Fact]
    public async Task AttentionNearDays_DefaultsToThree()
    {
        Assert.Equal(3, await _service.GetAttentionNearDaysAsync());
    }

    [Fact]
    public async Task AttentionStaleDays_DefaultsToSeven()
    {
        Assert.Equal(7, await _service.GetAttentionStaleDaysAsync());
    }

    [Fact]
    public async Task ReportReminderDays_DefaultsToThree()
    {
        Assert.Equal(3, await _service.GetReportReminderDaysAsync());
    }

    [Fact]
    public async Task MeetingReminderMinutes_DefaultsToFifteen()
    {
        Assert.Equal(15, await _service.GetMeetingReminderMinutesAsync());
    }

    [Fact]
    public async Task PhoneRemindersEnabled_DefaultsToTrue()
    {
        Assert.True(await _service.GetPhoneRemindersEnabledAsync());
    }

    [Fact]
    public async Task SoundsEnabled_DefaultsToTrue()
    {
        Assert.True(await _service.GetSoundsEnabledAsync());
    }

    [Fact]
    public async Task Theme_DefaultsToSystem()
    {
        Assert.Equal("system", await _service.GetThemeAsync());
    }

    [Fact]
    public async Task SetThenGet_RoundTripsTypedValue()
    {
        await _service.SetAsync(SettingKeys.AttentionLateDays, 5);
        Assert.Equal(5, await _service.GetAttentionLateDaysAsync());
    }

    [Fact]
    public async Task SetThenGet_RoundTripsBoolean()
    {
        await _service.SetAsync(SettingKeys.SoundsEnabled, false);
        Assert.False(await _service.GetSoundsEnabledAsync());
    }

    [Fact]
    public async Task SetTwice_OverwritesPreviousValue()
    {
        await _service.SetAsync(SettingKeys.Theme, "dark");
        await _service.SetAsync(SettingKeys.Theme, "light");
        Assert.Equal("light", await _service.GetThemeAsync());
        Assert.Single(_session.Db.Settings, s => s.Key == SettingKeys.Theme);
    }

    [Fact]
    public async Task GetAsync_UnknownKey_ReturnsSuppliedDefault()
    {
        Assert.Equal("افتراضي", await _service.GetAsync("some.unknown.key", "افتراضي"));
    }
}
