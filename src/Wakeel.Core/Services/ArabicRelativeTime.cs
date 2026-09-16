using System.Globalization;

namespace Wakeel.Core.Services;

/// <summary>
/// Arabic relative-time phrasing for the bell, the notification panel and the health center
/// (AGREEMENT item 20: Gregorian dates as <c>dd/MM/yyyy</c> with western digits, the device's
/// local time as <c>HH:mm</c>, and relative expressions).
/// </summary>
/// <remarks>
/// <para>
/// Instants are stored UTC (ARCHITECTURE.md §12) and displayed in the device's local zone, so
/// every method takes the zone explicitly (defaulting to <see cref="TimeZoneInfo.Local"/>) —
/// tests pass a fixed zone and get a deterministic answer on any machine.
/// </para>
/// <para>
/// Every number is formatted with <see cref="CultureInfo.InvariantCulture"/>, so the digits are
/// always ASCII 0-9 even when the thread's culture is Arabic (whose default digit substitution
/// would otherwise produce Arabic-Indic digits and break both AGREEMENT item 20 and the mixed
/// Arabic/Latin bidi handling of AGREEMENT item 55). Callers rendering these strings in the UI
/// still isolate the numeric run — the text is bidi-mixed by construction.
/// </para>
/// </remarks>
public static class ArabicRelativeTime
{
    /// <summary>Below this the phrase is «الآن» rather than a count of minutes.</summary>
    public static readonly TimeSpan JustNowThreshold = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Renders <paramref name="instant"/> relative to <paramref name="now"/>: «الآن»,
    /// «قبل 10 دقائق», «قبل 3 ساعات», «أمس 16:40», «خلال 20 دقيقة», «غدًا 09:00», or —
    /// beyond yesterday/tomorrow — the absolute «dd/MM/yyyy HH:mm».
    /// </summary>
    /// <param name="instant">The instant to describe (UTC, or an unspecified-kind value taken as UTC).</param>
    /// <param name="now">"Now" (UTC, or an unspecified-kind value taken as UTC).</param>
    /// <param name="zone">Display zone; defaults to the device's local zone.</param>
    public static string Describe(DateTime instant, DateTime now, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var instantUtc = ToUtc(instant);
        var nowUtc = ToUtc(now);
        var delta = nowUtc - instantUtc;

        if (delta.Duration() < JustNowThreshold)
        {
            return CoreAr.JustNow;
        }

        var localInstant = TimeZoneInfo.ConvertTimeFromUtc(instantUtc, zone);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        var dayOffset = (localInstant.Date - localNow.Date).Days;

        if (delta > TimeSpan.Zero)
        {
            // Past.
            if (dayOffset == 0)
            {
                return $"{CoreAr.AgoPrefix} {Elapsed(delta)}";
            }

            if (dayOffset == -1)
            {
                return $"{CoreAr.Yesterday} {Time(localInstant)}";
            }

            return $"{Date(localInstant)} {Time(localInstant)}";
        }

        // Future.
        var ahead = delta.Negate();
        if (dayOffset == 0)
        {
            return $"{CoreAr.WithinPrefix} {Elapsed(ahead)}";
        }

        if (dayOffset == 1)
        {
            return $"{CoreAr.Tomorrow} {Time(localInstant)}";
        }

        return $"{Date(localInstant)} {Time(localInstant)}";
    }

    /// <summary>The absolute «dd/MM/yyyy» form, western digits (AGREEMENT item 20).</summary>
    public static string Date(DateTime localValue) => localValue.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>The absolute «HH:mm» form, western digits (AGREEMENT item 20).</summary>
    public static string Time(DateTime localValue) => localValue.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>«dd/MM/yyyy HH:mm» in <paramref name="zone"/> for a stored UTC instant.</summary>
    public static string DateTimeText(DateTime instant, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var local = TimeZoneInfo.ConvertTimeFromUtc(ToUtc(instant), zone);
        return $"{Date(local)} {Time(local)}";
    }

    /// <summary>«HH:mm» in <paramref name="zone"/> for a stored UTC instant.</summary>
    public static string TimeText(DateTime instant, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        return Time(TimeZoneInfo.ConvertTimeFromUtc(ToUtc(instant), zone));
    }

    /// <summary>
    /// The bare duration phrase — «دقيقة», «دقيقتين», «10 دقائق», «3 ساعات», «12 ساعة» — with
    /// Arabic number agreement (ARCHITECTURE.md §12). Used with the «قبل»/«خلال» prefixes.
    /// </summary>
    public static string Elapsed(TimeSpan span)
    {
        var total = span.Duration();
        if (total < TimeSpan.FromHours(1))
        {
            return Count((int)total.TotalMinutes, "دقيقة", "دقيقتين", "دقائق", "دقيقة");
        }

        return Count((int)total.TotalHours, "ساعة", "ساعتين", "ساعات", "ساعة");
    }

    /// <summary>
    /// «singular» for 1 and «dual» for 2 with NO number (Arabic states the count in the word
    /// itself), «N plural» for 3-10, and «N pluralOver10» from 11 up — the same buckets
    /// <see cref="ArabicText.Plural"/> defines, rendered with the number where Arabic wants one.
    /// </summary>
    private static string Count(int value, string singular, string dual, string plural, string pluralOver10)
    {
        var word = ArabicText.Plural(value, singular, dual, plural, pluralOver10);
        return value is 1 or 2 ? word : $"{CoreAr.N(value)} {word}";
    }

    /// <summary>Same Kind normalization the rest of Core uses: Local converts, Unspecified is taken as UTC.</summary>
    internal static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
