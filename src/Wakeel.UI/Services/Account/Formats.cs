using System.Globalization;

namespace Wakeel.UI.Services.Account;

/// <summary>
/// The display forms ARCHITECTURE.md §12 fixes for the whole product: dates as
/// <c>dd/MM/yyyy</c> with Western digits, times as <c>HH:mm</c> in the machine's own time zone, and
/// file sizes rounded the way a person reads them. Every value produced here is a Latin/numeric
/// token and is isolated by the caller with <c>&lt;bdi&gt;</c>.
/// </summary>
public static class Formats
{
    /// <summary>The culture every number and date is rendered in: Western digits, unchanging separators.</summary>
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static readonly string[] ArabicWeekdays =
    [
        "الأحد", "الإثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت",
    ];

    /// <summary>«12/09/2026».</summary>
    public static string Date(DateTimeOffset value) => value.ToLocalTime().ToString("dd/MM/yyyy", Invariant);

    /// <summary>«12/09/2026».</summary>
    public static string Date(DateTime value) => Date(ToOffset(value));

    /// <summary>«10:24».</summary>
    public static string Time(DateTimeOffset value) => value.ToLocalTime().ToString("HH:mm", Invariant);

    /// <summary>«10:24».</summary>
    public static string Time(DateTime value) => Time(ToOffset(value));

    /// <summary>«السبت».</summary>
    public static string Weekday(DateTimeOffset value) => ArabicWeekdays[(int)value.ToLocalTime().DayOfWeek];

    /// <summary>«السبت».</summary>
    public static string Weekday(DateTime value) => Weekday(ToOffset(value));

    /// <summary>«1.8 ميغابايت», «640 كيلوبايت», «412 بايت».</summary>
    public static string FileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} بايت";
        }

        var kilobytes = bytes / 1024d;
        if (kilobytes < 1024)
        {
            return $"{Math.Round(kilobytes):0} كيلوبايت";
        }

        var megabytes = kilobytes / 1024d;
        return string.Create(Invariant, $"{megabytes:0.#} ميغابايت");
    }

    private static DateTimeOffset ToOffset(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => new DateTimeOffset(value, TimeSpan.Zero),
        DateTimeKind.Local => new DateTimeOffset(value),
        _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero),
    };
}
