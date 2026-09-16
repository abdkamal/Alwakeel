using System.Globalization;
using Wakeel.Admin.UI.Data;
using Wakeel.Admin.UI.Services.Account;
using Wakeel.Admin.UI.Text;

namespace Wakeel.Admin.UI.Services.Organisation;

/// <summary>The organisation's identity as A04 edits it.</summary>
/// <param name="Name">The full official name.</param>
/// <param name="CycleStartDay">The day of the month the financial cycle starts on, 1 to 28.</param>
/// <param name="NumberingFormat">The official-number format in force.</param>
/// <param name="Logo">The logo, or null when none has been chosen.</param>
/// <param name="ReportTemplateName">The monthly report template's file name, or null.</param>
/// <param name="LetterTemplateName">The letter template's file name, or null while the built-in one is in use.</param>
/// <param name="UpdatedAt">When any of this was last changed.</param>
public sealed record AdminOrgIdentity(
    string Name,
    int CycleStartDay,
    string NumberingFormat,
    AdminLogoImage? Logo,
    string? ReportTemplateName,
    string? LetterTemplateName,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Whether the letter template is the one that came with the tool.</summary>
    public bool UsesBuiltInLetterTemplate => string.IsNullOrEmpty(LetterTemplateName);
}

/// <summary>Why a change to the organisation's identity was refused.</summary>
public enum AdminOrgRefusal
{
    /// <summary>It was not refused.</summary>
    None,

    /// <summary>The organisation has no name.</summary>
    NameRequired,

    /// <summary>The cycle start day is outside the first twenty eight days of the month.</summary>
    CycleDayOutOfRange,

    /// <summary>The numbering format is empty or says nothing about where the number goes.</summary>
    NumberingFormatInvalid,

    /// <summary>There is no organisation to change yet.</summary>
    NoOrganisation,
}

/// <summary>
/// A04 «الهيئة والهوية»: the organisation's name, its logo, the day its financial cycle starts,
/// the shape of its official numbers, and the two Word templates it writes with.
/// </summary>
/// <remarks>
/// <para>
/// Everything here reaches every computer in the organisation through a setup file or an update, so
/// every change writes a waiting-to-be-distributed row: the dashboard then says how many changes
/// are waiting and A10 sends them out. Nothing here is a secret, so nothing here is sealed; the
/// organisation keys stay where A01 put them and this never touches them.
/// </para>
/// <para>
/// The numbering format is the one change that can break the record of what has already gone out
/// (AGREEMENT item 5), so the screen puts a danger dialog in front of it; this service records that
/// it happened and what the format was before, so the operations log can answer the question later.
/// </para>
/// </remarks>
public sealed class AdminOrgService
{
    /// <summary>The largest report template the tool will keep.</summary>
    public const int MaxTemplateBytes = 4 * 1024 * 1024;

    /// <summary>The first day of the month a cycle may start on.</summary>
    public const int MinCycleDay = 1;

    /// <summary>The last: past the twenty eighth a month would sometimes have no such day at all.</summary>
    public const int MaxCycleDay = 28;

    private readonly AdminDb _db;
    private readonly AdminAuditService _audit;
    private readonly AdminSession _session;
    private readonly AdminPendingChanges _pending;
    private readonly TimeProvider _time;

    public AdminOrgService(
        AdminDb db,
        AdminAuditService audit,
        AdminSession session,
        AdminPendingChanges pending,
        TimeProvider time)
    {
        _db = db;
        _audit = audit;
        _session = session;
        _pending = pending;
        _time = time;
    }

    /// <summary>The identity as it stands, or null before there is an organisation at all.</summary>
    public AdminOrgIdentity? Read()
    {
        if (!_db.IsOpen)
        {
            return null;
        }

        using var command = _db.Command(
            """
            SELECT name, cycle_start_day, numbering_format, logo, logo_mime,
                   report_template_name, letter_template_name, updated_at
            FROM org LIMIT 1;
            """);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        AdminLogoImage? logo = null;
        if (!reader.IsDBNull(3) && reader.GetValue(3) is byte[] bytes && bytes.Length > 0)
        {
            var mime = reader.IsDBNull(4) ? "image/png" : reader.GetString(4);
            AdminLogoReader.TryMeasure(bytes, out var width, out var height);
            logo = new AdminLogoImage(bytes, mime, width, height, WasCropped: width == height && width > 0);
        }

        return new AdminOrgIdentity(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetString(2),
            logo,
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    /// <summary>
    /// Saves the name, the cycle day and the numbering format together, which is how the screen
    /// saves them: one form, one button.
    /// </summary>
    /// <param name="name">The full official name.</param>
    /// <param name="cycleStartDay">The day the financial cycle starts on.</param>
    /// <param name="numberingFormat">The official-number format.</param>
    public AdminOrgRefusal Save(string name, int cycleStartDay, string numberingFormat)
    {
        var current = Read();
        if (current is null)
        {
            return AdminOrgRefusal.NoOrganisation;
        }

        var trimmedName = (name ?? string.Empty).Trim();
        if (trimmedName.Length == 0)
        {
            return AdminOrgRefusal.NameRequired;
        }

        if (cycleStartDay is < MinCycleDay or > MaxCycleDay)
        {
            return AdminOrgRefusal.CycleDayOutOfRange;
        }

        var format = (numberingFormat ?? string.Empty).Trim();
        if (!IsNumberingFormatValid(format))
        {
            return AdminOrgRefusal.NumberingFormatInvalid;
        }

        var now = _time.GetUtcNow();
        _db.Execute(
            """
            UPDATE org SET name = $name, cycle_start_day = $day, numbering_format = $format, updated_at = $at;
            """,
            ("$name", trimmedName),
            ("$day", cycleStartDay),
            ("$format", format),
            ("$at", now.ToString("O", CultureInfo.InvariantCulture)));

        if (!string.Equals(current.Name, trimmedName, StringComparison.Ordinal))
        {
            Record("org_renamed", AdminAr.Organisation.Log.Renamed(trimmedName));
        }

        if (current.CycleStartDay != cycleStartDay)
        {
            Record(
                "org_cycle_changed",
                AdminAr.Organisation.Log.CycleChanged(cycleStartDay),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["cycle_start_day"] = cycleStartDay.ToString(CultureInfo.InvariantCulture),
                });
        }

        if (!string.Equals(current.NumberingFormat, format, StringComparison.Ordinal))
        {
            // The one change that can make yesterday's numbers unreadable (AGREEMENT item 5): what
            // it was before is written down so the question can be answered later.
            Record(
                "org_numbering_changed",
                AdminAr.Organisation.Log.NumberingChanged(current.NumberingFormat, format),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["numbering_format_was"] = current.NumberingFormat,
                    ["numbering_format_now"] = format,
                });
        }

        return AdminOrgRefusal.None;
    }

    /// <summary>
    /// A numbering format has to say where the running number goes, or a letter could never be
    /// numbered at all. Everything else about it is the organisation's own business.
    /// </summary>
    public static bool IsNumberingFormatValid(string format) =>
        !string.IsNullOrWhiteSpace(format)
        && format.Length <= 64
        && format.Contains('S', StringComparison.Ordinal);

    /// <summary>Stores an accepted logo.</summary>
    public void SaveLogo(AdminLogoImage logo)
    {
        ArgumentNullException.ThrowIfNull(logo);
        if (!_db.IsOpen)
        {
            return;
        }

        _db.Execute(
            "UPDATE org SET logo = $logo, logo_mime = $mime, updated_at = $at;",
            ("$logo", logo.Bytes),
            ("$mime", logo.Mime),
            ("$at", Now()));

        Record("org_logo_set", AdminAr.Organisation.Log.LogoSet);
    }

    /// <summary>Takes the logo away again.</summary>
    public void ClearLogo()
    {
        if (!_db.IsOpen)
        {
            return;
        }

        _db.Execute("UPDATE org SET logo = NULL, logo_mime = NULL, updated_at = $at;", ("$at", Now()));
        Record("org_logo_cleared", AdminAr.Organisation.Log.LogoCleared);
    }

    /// <summary>Stores the monthly report template (AGREEMENT item 53).</summary>
    public void SaveReportTemplate(string fileName, ReadOnlySpan<byte> docx)
    {
        if (!_db.IsOpen)
        {
            return;
        }

        _db.Execute(
            "UPDATE org SET report_template = $file, report_template_name = $name, updated_at = $at;",
            ("$file", docx.ToArray()),
            ("$name", fileName),
            ("$at", Now()));

        Record("org_report_template_set", AdminAr.Organisation.Log.ReportTemplateSet(fileName));
    }

    /// <summary>Removes the monthly report template.</summary>
    public void ClearReportTemplate()
    {
        if (!_db.IsOpen)
        {
            return;
        }

        _db.Execute(
            "UPDATE org SET report_template = NULL, report_template_name = NULL, updated_at = $at;",
            ("$at", Now()));

        Record("org_report_template_cleared", AdminAr.Organisation.Log.ReportTemplateCleared);
    }

    /// <summary>Stores the official letter template (AGREEMENT item 57).</summary>
    public void SaveLetterTemplate(string fileName, ReadOnlySpan<byte> docx)
    {
        if (!_db.IsOpen)
        {
            return;
        }

        _db.Execute(
            "UPDATE org SET letter_template = $file, letter_template_name = $name, updated_at = $at;",
            ("$file", docx.ToArray()),
            ("$name", fileName),
            ("$at", Now()));

        Record("org_letter_template_set", AdminAr.Organisation.Log.LetterTemplateSet(fileName));
    }

    /// <summary>Goes back to the template the tool came with.</summary>
    public void ClearLetterTemplate()
    {
        if (!_db.IsOpen)
        {
            return;
        }

        _db.Execute(
            "UPDATE org SET letter_template = NULL, letter_template_name = NULL, updated_at = $at;",
            ("$at", Now()));

        Record("org_letter_template_cleared", AdminAr.Organisation.Log.LetterTemplateCleared);
    }

    /// <summary>
    /// The letter template in force: the organisation's own when one was uploaded, and otherwise
    /// the one carried inside the tool, so a letter can always be written.
    /// </summary>
    public (string FileName, byte[] Bytes) ReadLetterTemplate()
    {
        if (_db.IsOpen)
        {
            using var command = _db.Command("SELECT letter_template_name, letter_template FROM org LIMIT 1;");
            using var reader = command.ExecuteReader();
            if (reader.Read()
                && !reader.IsDBNull(0)
                && !reader.IsDBNull(1)
                && reader.GetValue(1) is byte[] bytes
                && bytes.Length > 0)
            {
                return (reader.GetString(0), bytes);
            }
        }

        return (DefaultLetterTemplate.FileName, DefaultLetterTemplate.Bytes());
    }

    /// <summary>The report template as stored, or null when none was uploaded.</summary>
    public (string FileName, byte[] Bytes)? ReadReportTemplate()
    {
        if (!_db.IsOpen)
        {
            return null;
        }

        using var command = _db.Command("SELECT report_template_name, report_template FROM org LIMIT 1;");
        using var reader = command.ExecuteReader();
        return reader.Read()
               && !reader.IsDBNull(0)
               && !reader.IsDBNull(1)
               && reader.GetValue(1) is byte[] bytes
               && bytes.Length > 0
            ? (reader.GetString(0), bytes)
            : null;
    }

    private string Now() => _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);

    private void Record(string action, string summary, IReadOnlyDictionary<string, string>? details = null)
    {
        _audit.Write(_session.AdminName, action, summary, entityType: "org", details: details);
        _pending.Add("org", "org", summary);
    }
}
