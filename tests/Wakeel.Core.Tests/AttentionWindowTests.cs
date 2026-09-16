using Wakeel.Core.Services;

namespace Wakeel.Core.Tests;

/// <summary>
/// The day boundary the four indicators are measured against. «اليوم» on W08 is the office's day,
/// not UTC's: the two differ for the first hours of every morning in Palestine, and a boundary
/// that drifted with them would move records between «متأخر» and «قريب الاستحقاق» on their own
/// between one look at the screen and the next.
/// </summary>
public sealed class AttentionWindowTests
{
    private static readonly TimeZoneInfo Plus3 =
        TimeZoneInfo.CreateCustomTimeZone("wakeel-test-plus-3", TimeSpan.FromHours(3), "plus 3", "plus 3");

    private static readonly AttentionThresholds Thresholds = new(LateDays: 1, NearDays: 3, StaleDays: 14);

    [Fact]
    public void TheSameRow_IsClassifiedTheSameAtOneInTheMorningAndAtNine()
    {
        // 16/09/2026 at 01:00 and at 09:00, both in a zone three hours ahead of UTC. At 01:00 the
        // UTC date is still the 15th, which is exactly where the drift used to come from.
        var earlyMorning = new DateTime(2026, 9, 15, 22, 0, 0, DateTimeKind.Utc);
        var midMorning = new DateTime(2026, 9, 16, 6, 0, 0, DateTimeKind.Utc);

        // Due yesterday afternoon, local time: late by one day on both readings.
        var dueAt = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
        var updatedAt = dueAt;

        var early = AttentionWindow.For(earlyMorning, Thresholds, Plus3);
        var mid = AttentionWindow.For(midMorning, Thresholds, Plus3);

        Assert.Equal(AttentionBucket.Late, early.Classify(dueAt, updatedAt));
        Assert.Equal(mid.Classify(dueAt, updatedAt), early.Classify(dueAt, updatedAt));
        Assert.Equal(mid.DaysLate(dueAt), early.DaysLate(dueAt));
        Assert.Equal(1, early.DaysLate(dueAt));
    }

    [Fact]
    public void TheDayBoundary_IsLocalMidnight_NotUtcMidnight()
    {
        var window = AttentionWindow.For(new DateTime(2026, 9, 15, 22, 0, 0, DateTimeKind.Utc), Thresholds, Plus3);

        // Local 16/09 00:00 is 15/09 21:00 UTC.
        Assert.Equal(new DateTime(2026, 9, 15, 21, 0, 0, DateTimeKind.Utc), window.LateCutoff);
        Assert.Equal(new DateTime(2026, 9, 16, 0, 0, 0), window.LocalToday);
    }

    [Fact]
    public void ARecordDueLaterToday_IsNearAndNotLate_OnBothReadings()
    {
        var earlyMorning = new DateTime(2026, 9, 15, 22, 0, 0, DateTimeKind.Utc);
        var midMorning = new DateTime(2026, 9, 16, 6, 0, 0, DateTimeKind.Utc);

        // Local 16/09 at 15:00.
        var dueAt = new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);

        var early = AttentionWindow.For(earlyMorning, Thresholds, Plus3);
        var mid = AttentionWindow.For(midMorning, Thresholds, Plus3);

        Assert.Equal(AttentionBucket.Near, early.Classify(dueAt, dueAt));
        Assert.Equal(AttentionBucket.Near, mid.Classify(dueAt, dueAt));
        Assert.Equal(0, early.DaysLate(dueAt));
    }
}

/// <summary>Amounts Core hands to a screen (W08's phone-expense rows).</summary>
public sealed class MoneyTests
{
    [Fact]
    public void AnAmount_LeadsWithTheShekelSign_AsTheMockupShows()
    {
        Assert.Equal("₪ 42.50", Money.Shekels(4250));
        Assert.Equal("₪ 25.00", Money.Shekels(2500));
    }

    [Fact]
    public void ALargeAmount_KeepsTheThousandsSeparatorAndWesternDigits()
    {
        Assert.Equal("₪ 1,234.50", Money.Shekels(123450));

        // An Arabic UI culture must not turn the digits into Arabic-Indic ones: the amount is a
        // Latin run the screen isolates, and mixing the two inside one row is exactly what
        // AGREEMENT item 55 rules out.
        Assert.DoesNotContain(Money.Shekels(123450), c => c is >= '٠' and <= '٩');
    }
}
