using System.Globalization;

namespace Wakeel.Reports.Letters;

/// <summary>
/// The two dates every official letter carries (AGREEMENT item 57): the Hijri one in Arabic month
/// names — «19 صفر 1448» — and the Gregorian one likewise — «2 أغسطس 2026».
/// </summary>
/// <remarks>
/// <para>
/// The Hijri date comes from <see cref="UmAlQuraCalendar"/>, the calendar the Kingdom's official
/// correspondence uses, rather than from the arithmetical <c>HijriCalendar</c>: the two disagree
/// by a day often enough that a letter dated by the wrong one would be noticed.
/// </para>
/// <para>
/// Digits are always western (AGREEMENT item 20), written through
/// <see cref="CultureInfo.InvariantCulture"/> so the thread's culture cannot turn them into
/// Arabic-Indic ones, and the month names are spelled out so that no reader has to decide whether
/// «2/8» means February or August.
/// </para>
/// </remarks>
public static class LetterDates
{
    /// <summary>The Hijri months, in order, as official correspondence writes them.</summary>
    public static IReadOnlyList<string> HijriMonths { get; } =
    [
        "محرم",
        "صفر",
        "ربيع الأول",
        "ربيع الآخر",
        "جمادى الأولى",
        "جمادى الآخرة",
        "رجب",
        "شعبان",
        "رمضان",
        "شوال",
        "ذو القعدة",
        "ذو الحجة",
    ];

    /// <summary>The Gregorian months, in order, as they are written in Arabic here.</summary>
    public static IReadOnlyList<string> GregorianMonths { get; } =
    [
        "يناير",
        "فبراير",
        "مارس",
        "أبريل",
        "مايو",
        "يونيو",
        "يوليو",
        "أغسطس",
        "سبتمبر",
        "أكتوبر",
        "نوفمبر",
        "ديسمبر",
    ];

    private static readonly UmAlQuraCalendar Calendar = new();

    /// <summary>
    /// The Hijri date, e.g. «19 صفر 1448». Outside the Um al-Qura tables — which run from 1900 to
    /// 2077 — the mark is left empty rather than filled with a date nobody can vouch for.
    /// </summary>
    /// <param name="date">The letter's date.</param>
    public static string Hijri(DateTime date)
    {
        if (date < Calendar.MinSupportedDateTime || date > Calendar.MaxSupportedDateTime)
        {
            return string.Empty;
        }

        var day = Calendar.GetDayOfMonth(date);
        var month = Calendar.GetMonth(date);
        var year = Calendar.GetYear(date);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{day} {HijriMonths[month - 1]} {year}");
    }

    /// <summary>The Gregorian date, e.g. «2 أغسطس 2026».</summary>
    /// <param name="date">The letter's date.</param>
    public static string Gregorian(DateTime date) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{date.Day} {GregorianMonths[date.Month - 1]} {date.Year}");

    /// <summary>Both dates on one line, for a preview caption.</summary>
    /// <param name="date">The letter's date.</param>
    public static string Both(DateTime date)
    {
        var hijri = Hijri(date);
        var gregorian = Gregorian(date);
        return hijri.Length == 0 ? gregorian : $"{hijri} — {gregorian}";
    }
}
