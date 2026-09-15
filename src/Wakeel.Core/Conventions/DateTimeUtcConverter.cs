using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Wakeel.Core.Data;

namespace Wakeel.Core.Conventions;

/// <summary>Stores a <see cref="DateTime"/> as ISO-8601 UTC text (round-trip "O" format, always <c>Z</c>-suffixed).</summary>
internal sealed class DateTimeUtcConverter : ValueConverter<DateTime, string>
{
    /// <summary>Format used to write the value (includes the trailing literal <c>Z</c>).</summary>
    public const string Format = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

    /// <summary>
    /// Formats tried to parse the value, WITHOUT a trailing <c>Z</c> — .NET's date parser treats an
    /// unescaped <c>Z</c> in a custom format string as a UTC marker and has been observed to
    /// silently shift the parsed VALUE toward local time (not just mislabel its Kind) even under
    /// <see cref="DateTimeStyles.None"/>; a later <see cref="DateTime.SpecifyKind"/> would then
    /// only relabel an already-wrong value. <see cref="Parse"/> strips a trailing <c>Z</c> before
    /// matching against these and always finishes with <see cref="DateTime.SpecifyKind"/> itself,
    /// so that risk never applies. Covers every shape this text column has been observed to hold:
    /// our own 7-digit-fraction writer, a 3-digit fraction (e.g. SQLite <c>strftime('%fZ')</c>),
    /// no fraction, and the space-separated form SQLite's own <c>datetime('now')</c> writes.
    /// </summary>
    private static readonly string[] ParseFormats =
    [
        "yyyy-MM-ddTHH:mm:ss.fffffff",
        "yyyy-MM-ddTHH:mm:ss.fff",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-dd HH:mm:ss",
    ];

    /// <param name="table">Table this converter is attached to, reported with any unreadable value.</param>
    /// <param name="column">Column this converter is attached to, reported with any unreadable value.</param>
    public DateTimeUtcConverter(string table, string column)
        : base(
            v => ToUtc(v).ToString(Format, CultureInfo.InvariantCulture),
            v => Parse(v, table, column))
    {
    }

    /// <summary>
    /// Parses any ISO-8601-ish UTC timestamp text this column has been observed to hold: what
    /// this converter itself writes, but also raw SQL writers (<c>datetime('now')</c>,
    /// <c>strftime('...%fZ')</c>), a backup restored from a different build, or rows applied by
    /// the sync-import layer (B6) — none of which necessarily match <see cref="Format"/> exactly.
    /// Never throws: a single corrupted value must not take down a whole list through an
    /// exception raised inside EF materialization. Text that no parser understands (and text that
    /// is empty, which a NOT NULL timestamp column should never hold) therefore yields the
    /// earliest representable UTC instant — but is reported to <see cref="DataIntegrityLog"/>
    /// with its table, column and raw text first, so the value is visible to the log and to the
    /// health center (W12) instead of silently sliding outside every date range.
    /// </summary>
    internal static DateTime Parse(string v, string table, string column)
    {
        if (string.IsNullOrEmpty(v))
        {
            DataIntegrityLog.Report(table, column, v ?? string.Empty);
            return DateTime.SpecifyKind(default, DateTimeKind.Utc);
        }

        var withoutZ = v.EndsWith('Z') ? v.AsSpan(0, v.Length - 1) : v.AsSpan();
        if (DateTime.TryParseExact(withoutZ, ParseFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return DateTime.SpecifyKind(exact, DateTimeKind.Utc);
        }

        // The general-purpose parser understands a trailing Z, unlike ParseExact with a custom
        // format string, so it is worth one more try before the value is declared unreadable.
        if (DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var general))
        {
            return DateTime.SpecifyKind(general, DateTimeKind.Utc);
        }

        DataIntegrityLog.Report(table, column, v);
        return DateTime.SpecifyKind(default, DateTimeKind.Utc);
    }

    internal static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}

/// <summary>Stores a nullable <see cref="DateTime"/> as ISO-8601 UTC text, or <c>NULL</c>.</summary>
internal sealed class NullableDateTimeUtcConverter : ValueConverter<DateTime?, string?>
{
    /// <param name="table">Table this converter is attached to, reported with any unreadable value.</param>
    /// <param name="column">Column this converter is attached to, reported with any unreadable value.</param>
    public NullableDateTimeUtcConverter(string table, string column)
        : base(
            v => v == null ? null : DateTimeUtcConverter.ToUtc(v.Value).ToString(DateTimeUtcConverter.Format, CultureInfo.InvariantCulture),
            v => v == null ? null : DateTimeUtcConverter.Parse(v, table, column))
    {
    }
}
