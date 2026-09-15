using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;

namespace Wakeel.Core.Services;

/// <summary>
/// Clock-sanity check (ARCHITECTURE.md §9, AGREEMENT item 20): run on every startup and before
/// issuing an official number. The clock must be no earlier than the last recorded activity
/// minus five minutes, and no earlier than the build date; a mismatch against another device's
/// timestamp (seen only during a sync import) additionally downgrades the verdict.
/// </summary>
public interface IClockCheckService
{
    /// <summary>Runs the check for <paramref name="now"/>, records the result, and returns it.</summary>
    Task<ClockCheck> CheckAsync(DateTime now, IReadOnlyCollection<DateTime>? otherDeviceTimestamps = null, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IClockCheckService"/>
public sealed class ClockCheckService(WakeelDb db) : IClockCheckService
{
    /// <summary>The clock may lag the last recorded activity by at most this much before it is considered "bad".</summary>
    public static readonly TimeSpan BackwardTolerance = TimeSpan.FromMinutes(5);

    public async Task<ClockCheck> CheckAsync(DateTime now, IReadOnlyCollection<DateTime>? otherDeviceTimestamps = null, CancellationToken cancellationToken = default)
    {
        var utcNow = now.Kind switch
        {
            DateTimeKind.Utc => now,
            DateTimeKind.Local => now.ToUniversalTime(),
            _ => DateTime.SpecifyKind(now, DateTimeKind.Utc),
        };
        var installation = await db.Installation.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        var lastCheck = await db.ClockChecks.AsNoTracking()
            .OrderByDescending(c => c.At)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var lastActivity = lastCheck?.At ?? installation?.ActivatedAt;

        var verdict = ClockVerdict.Ok;
        var reasons = new List<string>();

        if (installation is not null && utcNow < installation.BuildDate)
        {
            verdict = ClockVerdict.Bad;
            reasons.Add("clock_before_build_date");
        }

        if (lastActivity is not null && utcNow < lastActivity.Value - BackwardTolerance)
        {
            verdict = ClockVerdict.Bad;
            reasons.Add("clock_before_last_activity");
        }

        if (verdict == ClockVerdict.Ok && otherDeviceTimestamps is { Count: > 0 })
        {
            var maxOther = otherDeviceTimestamps.Max();
            if (utcNow < maxOther - BackwardTolerance)
            {
                verdict = ClockVerdict.Suspect;
                reasons.Add("clock_behind_other_device");
            }
        }

        var check = new ClockCheck
        {
            At = utcNow,
            Verdict = verdict,
            Details = reasons.Count == 0 ? null : string.Join(",", reasons),
        };

        db.ClockChecks.Add(check);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return check;
    }
}
