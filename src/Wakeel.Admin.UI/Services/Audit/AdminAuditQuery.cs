using System.Globalization;
using System.Text;
using Wakeel.Admin.UI.Data;
using Wakeel.Admin.UI.Text;

namespace Wakeel.Admin.UI.Services.Audit;

/// <summary>
/// A11 «سجل العمليات»: reading the log back — who did what and when — searching it on this computer,
/// and writing it out as a table anybody can open.
/// </summary>
/// <remarks>
/// <para>
/// The log is written by <see cref="AdminAuditService"/> and never by this. Reading is all that
/// happens here, and the reading deliberately leaves the <c>details</c> column behind: it is the
/// technical shoulder of a row — file names, versions, codes — and the screen and the exported table
/// are meant to be read out loud. Nothing secret is written into a row in the first place, and not
/// carrying the column here means a change of mind upstream cannot leak one downstream either.
/// </para>
/// <para>
/// The search is a plain contains, case and diacritic insensitive over what the person can actually
/// see: the sentence, the actor and the day.
/// </para>
/// </remarks>
public sealed class AdminAuditQuery
{
    /// <summary>The most rows the screen will hold at once.</summary>
    public const int MaxRows = 1000;

    private readonly AdminDb _db;
    private readonly AdminAuditService _audit;

    public AdminAuditQuery(AdminDb db, AdminAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <summary>The log, newest first, narrowed by what was typed into the search field.</summary>
    /// <param name="search">What to look for, or null for everything.</param>
    /// <param name="limit">How many rows at most.</param>
    public IReadOnlyList<AdminAuditEntry> Read(string? search = null, int limit = MaxRows)
    {
        if (!_db.IsOpen)
        {
            return [];
        }

        var rows = _audit.Recent(Math.Clamp(limit, 1, MaxRows));
        if (string.IsNullOrWhiteSpace(search))
        {
            return rows;
        }

        var needle = Normalize(search);
        return [.. rows.Where(row =>
            Normalize(row.SummaryAr).Contains(needle, StringComparison.Ordinal)
            || Normalize(row.Actor).Contains(needle, StringComparison.Ordinal)
            || Normalize(AdminAr.Audit.ActionName(row.Action)).Contains(needle, StringComparison.Ordinal)
            || AdminAr.Audit.Day(row.At).Contains(needle, StringComparison.Ordinal))];
    }

    /// <summary>How many rows the log holds altogether.</summary>
    public int Count => _db.IsOpen ? (int)_db.Scalar("SELECT COUNT(*) FROM audit_log;") : 0;

    /// <summary>
    /// The rows as a table file: four columns, the same four the screen shows, with the marker that
    /// makes a spreadsheet read it as Arabic rather than as a row of question marks.
    /// </summary>
    public static string ToCsv(IEnumerable<AdminAuditEntry> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var builder = new StringBuilder();
        builder.Append('﻿');
        builder.AppendLine(string.Join(',', new[]
        {
            AdminAr.Audit.ColumnWhen,
            AdminAr.Audit.ColumnWho,
            AdminAr.Audit.ColumnWhat,
            AdminAr.Audit.ColumnDetail,
        }.Select(Quote)));

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(',', new[]
            {
                AdminAr.Audit.Moment(row.At),
                row.Actor,
                AdminAr.Audit.ActionName(row.Action),
                row.SummaryAr,
            }.Select(Quote)));
        }

        return builder.ToString();
    }

    /// <summary>
    /// A field as a table file writes it: always quoted, with any quotation mark inside it doubled,
    /// and with the four characters that turn a cell into a formula in some spreadsheets pushed one
    /// place along so a log line can never be executed by the program that opens it.
    /// </summary>
    private static string Quote(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@')
        {
            text = "'" + text;
        }

        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>
    /// The form both sides of a search are compared in: lower case, without the Arabic marks that a
    /// person never types, and with the three shapes of alef and the two of yaa folded together, so
    /// «إلغاء» is found by typing «الغاء».
    /// </summary>
    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text.Trim().ToLowerInvariant())
        {
            if (character is >= 'ً' and <= 'ْ' or 'ـ')
            {
                continue;
            }

            builder.Append(character switch
            {
                'آ' or 'أ' or 'إ' => 'ا',
                'ى' => 'ي',
                'ة' => 'ه',
                _ => character,
            });
        }

        return builder.ToString();
    }

    /// <summary>The day part of a moment, as the search compares it.</summary>
    internal static string DayOf(DateTimeOffset at) =>
        at.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
