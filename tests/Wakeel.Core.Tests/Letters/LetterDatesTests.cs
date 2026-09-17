using System.Globalization;
using Wakeel.Reports.Letters;

namespace Wakeel.Core.Tests.Letters;

/// <summary>
/// The two dates of AGREEMENT item 57, against dates whose answer is known independently: the
/// owner's own example, and the first day of a Hijri year.
/// </summary>
public sealed class LetterDatesTests
{
    [Theory]
    [InlineData(2026, 8, 2, "19 صفر 1448")]
    [InlineData(2026, 6, 16, "1 محرم 1448")]
    [InlineData(2026, 2, 17, "29 شعبان 1447")]
    public void Hijri_is_written_with_arabic_month_names(int year, int month, int day, string expected) =>
        Assert.Equal(expected, LetterDates.Hijri(new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Unspecified)));

    [Theory]
    [InlineData(2026, 8, 2, "2 أغسطس 2026")]
    [InlineData(2026, 1, 31, "31 يناير 2026")]
    [InlineData(2026, 12, 9, "9 ديسمبر 2026")]
    public void Gregorian_is_written_with_arabic_month_names(int year, int month, int day, string expected) =>
        Assert.Equal(expected, LetterDates.Gregorian(new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Unspecified)));

    [Fact]
    public void Dates_use_western_digits_whatever_the_thread_s_culture_is()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            // An Arabic culture would otherwise render the digits Arabic-Indic, which AGREEMENT
            // item 20 does not allow.
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            var date = new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Unspecified);

            Assert.Equal("19 صفر 1448", LetterDates.Hijri(date));
            Assert.Equal("2 أغسطس 2026", LetterDates.Gregorian(date));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void A_date_outside_the_um_al_qura_tables_leaves_the_hijri_mark_empty() =>
        Assert.Equal(string.Empty, LetterDates.Hijri(new DateTime(1850, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)));

    [Fact]
    public void Both_dates_fit_on_one_line_for_a_caption() =>
        Assert.Equal(
            "19 صفر 1448 — 2 أغسطس 2026",
            LetterDates.Both(new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Unspecified)));

    [Fact]
    public void There_are_twelve_months_in_each_calendar()
    {
        Assert.Equal(12, LetterDates.HijriMonths.Count);
        Assert.Equal(12, LetterDates.GregorianMonths.Count);
        Assert.DoesNotContain(LetterDates.HijriMonths, m => string.IsNullOrWhiteSpace(m));
        Assert.DoesNotContain(LetterDates.GregorianMonths, m => string.IsNullOrWhiteSpace(m));
    }
}
