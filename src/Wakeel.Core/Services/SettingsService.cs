using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;

namespace Wakeel.Core.Services;

/// <summary>Well-known setting keys (DATA-MODEL.md §1 <c>settings</c>).</summary>
public static class SettingKeys
{
    /// <summary>Days overdue before a correspondence/task counts as "late" in the attention center.</summary>
    public const string AttentionLateDays = "attention.late_days";

    /// <summary>Days before due date to count as "near due" in the attention center.</summary>
    public const string AttentionNearDays = "attention.near_days";

    /// <summary>Days without activity before an item counts as "stale" in the attention center.</summary>
    public const string AttentionStaleDays = "attention.stale_days";

    /// <summary>Days before a financial cycle's end to start reminding about the monthly report.</summary>
    public const string ReportReminderDays = "report.reminder_days";

    /// <summary>Default minutes before a meeting's start to remind (AGREEMENT item 56).</summary>
    public const string MeetingReminderMinutes = "meeting.reminder_minutes";

    /// <summary>Whether meeting/task reminders are also sent to the paired phone (AGREEMENT item 56).</summary>
    public const string PhoneRemindersEnabled = "notifications.phone_reminders_enabled";

    /// <summary>Whether notification sounds are enabled.</summary>
    public const string SoundsEnabled = "notifications.sounds_enabled";

    /// <summary>"system" | "light" | "dark".</summary>
    public const string Theme = "appearance.theme";
}

/// <summary>Typed get/set access to the local <c>settings</c> table, with built-in defaults.</summary>
public interface ISettingsService
{
    Task<T> GetAsync<T>(string key, T defaultValue, CancellationToken cancellationToken = default);

    Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default);

    Task<int> GetAttentionLateDaysAsync(CancellationToken cancellationToken = default);

    Task<int> GetAttentionNearDaysAsync(CancellationToken cancellationToken = default);

    Task<int> GetAttentionStaleDaysAsync(CancellationToken cancellationToken = default);

    Task<int> GetReportReminderDaysAsync(CancellationToken cancellationToken = default);

    Task<int> GetMeetingReminderMinutesAsync(CancellationToken cancellationToken = default);

    Task<bool> GetPhoneRemindersEnabledAsync(CancellationToken cancellationToken = default);

    Task<bool> GetSoundsEnabledAsync(CancellationToken cancellationToken = default);

    Task<string> GetThemeAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ISettingsService"/>
public sealed class SettingsService(WakeelDb db, IClock clock) : ISettingsService
{
    // AGREEMENT item 23 / DATA-MODEL.md §1 defaults.
    public const int DefaultAttentionLateDays = 1;
    public const int DefaultAttentionNearDays = 3;
    public const int DefaultAttentionStaleDays = 7;
    public const int DefaultReportReminderDays = 3;
    public const int DefaultMeetingReminderMinutes = 15;
    public const bool DefaultPhoneRemindersEnabled = true;
    public const bool DefaultSoundsEnabled = true;
    public const string DefaultTheme = "system";

    public async Task<T> GetAsync<T>(string key, T defaultValue, CancellationToken cancellationToken = default)
    {
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return defaultValue;
        }

        return JsonSerializer.Deserialize<T>(row.Value) ?? defaultValue;
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken).ConfigureAwait(false);
        var json = JsonSerializer.Serialize(value);
        if (row is null)
        {
            db.Settings.Add(new Setting { Key = key, Value = json, UpdatedAt = clock.UtcNow });
        }
        else
        {
            row.Value = json;
            row.UpdatedAt = clock.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<int> GetAttentionLateDaysAsync(CancellationToken cancellationToken = default) =>
        GetAsync(SettingKeys.AttentionLateDays, DefaultAttentionLateDays, cancellationToken);

    public Task<int> GetAttentionNearDaysAsync(CancellationToken cancellationToken = default) =>
        GetAsync(SettingKeys.AttentionNearDays, DefaultAttentionNearDays, cancellationToken);

    public Task<int> GetAttentionStaleDaysAsync(CancellationToken cancellationToken = default) =>
        GetAsync(SettingKeys.AttentionStaleDays, DefaultAttentionStaleDays, cancellationToken);

    public Task<int> GetReportReminderDaysAsync(CancellationToken cancellationToken = default) =>
        GetAsync(SettingKeys.ReportReminderDays, DefaultReportReminderDays, cancellationToken);

    public Task<int> GetMeetingReminderMinutesAsync(CancellationToken cancellationToken = default) =>
        GetAsync(SettingKeys.MeetingReminderMinutes, DefaultMeetingReminderMinutes, cancellationToken);

    public Task<bool> GetPhoneRemindersEnabledAsync(CancellationToken cancellationToken = default) =>
        GetAsync(SettingKeys.PhoneRemindersEnabled, DefaultPhoneRemindersEnabled, cancellationToken);

    public Task<bool> GetSoundsEnabledAsync(CancellationToken cancellationToken = default) =>
        GetAsync(SettingKeys.SoundsEnabled, DefaultSoundsEnabled, cancellationToken);

    public Task<string> GetThemeAsync(CancellationToken cancellationToken = default) =>
        GetAsync(SettingKeys.Theme, DefaultTheme, cancellationToken);
}
