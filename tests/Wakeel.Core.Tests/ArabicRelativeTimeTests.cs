using System.Globalization;
using Wakeel.Core.Services;

namespace Wakeel.Core.Tests;

/// <summary>
/// Arabic relative time (AGREEMENT item 20). A fixed zone keeps the expectations true on any
/// machine; the digit assertions are the point of the mixed Arabic/Latin cases.
/// </summary>
public sealed class ArabicRelativeTimeTests
{
    /// <summary>A zone with a whole-hour offset and no daylight saving, so the arithmetic is obvious.</summary>
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("wakeel-test", TimeSpan.FromHours(3), "wakeel-test", "wakeel-test");

    private static readonly DateTime Now = new(2026, 9, 16, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void LessThanAMinuteAgo_IsNow()
    {
        Assert.Equal("الآن", ArabicRelativeTime.Describe(Now.AddSeconds(-30), Now, Zone));
        Assert.Equal("الآن", ArabicRelativeTime.Describe(Now, Now, Zone));
    }

    [Theory]
    [InlineData(1, "قبل دقيقة")]
    [InlineData(2, "قبل دقيقتين")]
    [InlineData(3, "قبل 3 دقائق")]
    [InlineData(10, "قبل 10 دقائق")]
    [InlineData(11, "قبل 11 دقيقة")]
    [InlineData(45, "قبل 45 دقيقة")]
    public void MinutesAgo_AgreeInNumber(int minutes, string expected) =>
        Assert.Equal(expected, ArabicRelativeTime.Describe(Now.AddMinutes(-minutes), Now, Zone));

    [Theory]
    [InlineData(1, "قبل ساعة")]
    [InlineData(2, "قبل ساعتين")]
    [InlineData(5, "قبل 5 ساعات")]
    public void HoursAgo_AgreeInNumber(int hours, string expected) =>
        Assert.Equal(expected, ArabicRelativeTime.Describe(Now.AddHours(-hours), Now, Zone));

    [Fact]
    public void Yesterday_IsNamedAndCarriesTheLocalTime()
    {
        // 16:40 local in a +03:00 zone is 13:40 UTC the same day.
        var yesterdayLocal1640 = new DateTime(2026, 9, 15, 13, 40, 0, DateTimeKind.Utc);

        Assert.Equal("أمس 16:40", ArabicRelativeTime.Describe(yesterdayLocal1640, Now, Zone));
    }

    [Fact]
    public void BeyondYesterday_FallsBackToTheAbsoluteDateAndTime()
    {
        var older = new DateTime(2026, 9, 11, 13, 40, 0, DateTimeKind.Utc);

        Assert.Equal("11/09/2026 16:40", ArabicRelativeTime.Describe(older, Now, Zone));
    }

    [Fact]
    public void FutureInstants_UseWithinTomorrowAndTheAbsoluteForm()
    {
        Assert.Equal("خلال 20 دقيقة", ArabicRelativeTime.Describe(Now.AddMinutes(20), Now, Zone));

        // 09:00 local tomorrow is 06:00 UTC tomorrow in a +03:00 zone.
        var tomorrow0900Local = new DateTime(2026, 9, 17, 6, 0, 0, DateTimeKind.Utc);
        Assert.Equal("غدًا 09:00", ArabicRelativeTime.Describe(tomorrow0900Local, Now, Zone));

        var later = new DateTime(2026, 9, 20, 6, 0, 0, DateTimeKind.Utc);
        Assert.Equal("20/09/2026 09:00", ArabicRelativeTime.Describe(later, Now, Zone));
    }

    [Fact]
    public void ADayBoundaryCrossingIsCountedInLocalDays_NotInElapsedHours()
    {
        // 23:30 local yesterday is only ten and a half hours before 10:00 local today, but it is
        // still YESTERDAY: the group is a calendar day in the device's zone, not an hour count.
        var now = new DateTime(2026, 9, 16, 7, 0, 0, DateTimeKind.Utc); // 10:00 local
        var lateYesterday = new DateTime(2026, 9, 15, 20, 30, 0, DateTimeKind.Utc); // 23:30 local

        Assert.Equal("أمس 23:30", ArabicRelativeTime.Describe(lateYesterday, now, Zone));
    }

    [Fact]
    public void DigitsAreAlwaysWestern_EvenUnderAnArabicCulture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            // ar-SA's native digit substitution would render "١٠" here; AGREEMENT item 20 wants
            // western digits everywhere, and the mixed Arabic/Latin run is what the bidi handling
            // of AGREEMENT item 55 isolates in the UI.
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            CultureInfo.CurrentUICulture = new CultureInfo("ar-SA");

            var relative = ArabicRelativeTime.Describe(Now.AddMinutes(-10), Now, Zone);
            var absolute = ArabicRelativeTime.DateTimeText(new DateTime(2026, 9, 11, 13, 40, 0, DateTimeKind.Utc), Zone);
            var size = CoreAr.Size(5L * 1024 * 1024);
            var days = CoreAr.DaysPhrase(13);

            Assert.Equal("قبل 10 دقائق", relative);
            Assert.Equal("11/09/2026 16:40", absolute);
            Assert.Equal("5 ميغابايت", size);
            Assert.Equal("13 يومًا", days);

            foreach (var text in new[] { relative, absolute, size, days })
            {
                Assert.DoesNotContain(text, c => c is >= '٠' and <= '٩');
                Assert.Contains(text, char.IsAsciiDigit);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Theory]
    [InlineData(0, "كل شيء سليم")]
    [InlineData(1, "هناك مشكلة واحدة تحتاج تدخلًا")]
    [InlineData(2, "هناك مشكلتان تحتاجان تدخلًا")]
    [InlineData(4, "هناك 4 مشكلات تحتاج تدخلًا")]
    [InlineData(12, "هناك 12 مشكلة تحتاج تدخلًا")]
    public void HealthSummary_AgreesInNumber(int problems, string expected) =>
        Assert.Equal(expected, CoreAr.HealthProblems(problems));
}
