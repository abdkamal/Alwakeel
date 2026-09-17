using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using CorrespondenceRow = Wakeel.Core.Data.Entities.Correspondence;

namespace Wakeel.Core.Services.Correspondence;

/// <summary>One field the user wants changed on a numbered correspondence item.</summary>
/// <param name="Field">One of the <see cref="CorrespondenceFields"/> keys.</param>
/// <param name="NewValue">The new value as text; <c>null</c> clears the field.</param>
public sealed record CorrectionChange(string Field, string? NewValue);

/// <summary>
/// Both versions of one corrected field, exactly as stored in <c>corrections.changes</c>. This is
/// the storage shape and nothing else: it is serialised to JSON, so it carries no Arabic wording
/// and it never changes. Screens bind to <see cref="CorrectionFieldView"/> instead.
/// </summary>
/// <param name="Field">The field key.</param>
/// <param name="Old">What it held before the correction.</param>
/// <param name="New">What it holds now.</param>
public sealed record CorrectionEntry(string Field, string? Old, string? New);

/// <summary>
/// One corrected field ready to be read by a human: the field's Arabic label beside both values
/// in the wording the rest of the product uses (a confidentiality level as «سري», a date as
/// «16/09/2026 00:00»), never an English enum name and never an ISO timestamp (AGREEMENT item 15,
/// ARCHITECTURE §12).
/// </summary>
/// <param name="Field">The stable field key, for a screen that needs to group or order.</param>
/// <param name="FieldLabelAr">«الموضوع», «رقم الجهة», …</param>
/// <param name="Old">The stored old value, unchanged.</param>
/// <param name="OldAr">The old value as the user reads it; null when the field was empty.</param>
/// <param name="New">The stored new value, unchanged.</param>
/// <param name="NewAr">The new value as the user reads it; null when the field was cleared.</param>
public sealed record CorrectionFieldView(
    string Field,
    string FieldLabelAr,
    string? Old,
    string? OldAr,
    string? New,
    string? NewAr);

/// <summary>One recorded correction, ready for the correspondence log tab (W25).</summary>
/// <param name="Id">The correction row.</param>
/// <param name="CorrespondenceId">The item corrected.</param>
/// <param name="Changes">Every field, with both versions kept.</param>
/// <param name="ReasonAr">Why it was corrected.</param>
/// <param name="At">When.</param>
/// <param name="AtAr">That instant as «أمس 16:40».</param>
public sealed record CorrectionView(
    Guid Id,
    Guid CorrespondenceId,
    IReadOnlyList<CorrectionFieldView> Changes,
    string ReasonAr,
    DateTime At,
    string AtAr);

/// <summary>
/// Corrections after numbering (AGREEMENT item 19): a numbered correspondence item is never
/// silently edited. Each correction stores the old value, the new value and the reason, and both
/// versions stay in <c>corrections</c> forever.
/// </summary>
public interface ICorrectionService
{
    /// <summary>
    /// Applies <paramref name="changes"/> to a numbered item and records them. Refused when the
    /// item carries no number (an unnumbered draft is simply edited), when the reason is empty,
    /// when no value actually changes, or when a field is not correctable — the official number
    /// and the approval stamp never are.
    /// </summary>
    Task<CorrectionView> ApplyAsync(
        Guid correspondenceId,
        IReadOnlyList<CorrectionChange> changes,
        string reasonAr,
        DateTime now,
        CancellationToken cancellationToken = default);

    /// <summary>Every correction of one item, newest first.</summary>
    Task<IReadOnlyList<CorrectionView>> ListAsync(Guid correspondenceId, DateTime now, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ICorrectionService"/>
public sealed class CorrectionService(WakeelDb db, IAuditService audit) : ICorrectionService
{
    public const string AuditActionCorrected = "correspondence.corrected";

    /// <summary>
    /// The fields a correction may touch. The official number, the issue date, the approval
    /// stamp, the direction and the status are absent on purpose: changing any of them would
    /// rewrite the office's record of what was sent or received, which is exactly what item 19
    /// forbids. The status moves through <see cref="ICorrespondenceService"/> instead.
    /// </summary>
    public static IReadOnlyList<string> CorrectableFields { get; } =
    [
        CorrespondenceFields.Subject,
        CorrespondenceFields.Type,
        CorrespondenceFields.Confidentiality,
        CorrespondenceFields.ExternalNumber,
        CorrespondenceFields.ExternalDate,
        CorrespondenceFields.DueAt,
        CorrespondenceFields.NextStep,
        CorrespondenceFields.BodyText,
        CorrespondenceFields.Cc,
        CorrespondenceFields.PartyNameSnapshot,
    ];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// One change that passed every check, with its new value already parsed and an action that
    /// does nothing but assign it. Nothing in here can fail, which is the whole point: the
    /// tracked row is touched only once the entire batch is known to be good.
    /// </summary>
    private sealed record PlannedChange(CorrectionEntry Entry, Action<CorrespondenceRow> Apply);

    public async Task<CorrectionView> ApplyAsync(
        Guid correspondenceId,
        IReadOnlyList<CorrectionChange> changes,
        string reasonAr,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (string.IsNullOrWhiteSpace(reasonAr))
        {
            throw new CorrespondenceRefusedException(CoreAr.CorrRefusedCorrectionReason);
        }

        var row = await db.Correspondence.FirstOrDefaultAsync(c => c.Id == correspondenceId, cancellationToken).ConfigureAwait(false)
            ?? throw new CorrespondenceRefusedException(CoreAr.CorrRefusedNotFound);

        if (row.OfficialNumber is null)
        {
            throw new CorrespondenceRefusedException(CoreAr.CorrRefusedNotNumbered);
        }

        // PASS ONE — decide everything, write nothing. WakeelDb lives as long as the session of
        // the user, so a half-applied batch left on the tracked entity would be committed by the
        // next unrelated SaveChanges anywhere in that session: a numbered letter silently edited
        // with no corrections row behind it, which is precisely what item 19 forbids. Every
        // refusal — an uncorrectable field, an unreadable date, an unknown confidentiality level,
        // a cleared subject — therefore has to be raised before the first assignment.
        var planned = new List<PlannedChange>(changes.Count);
        foreach (var change in changes)
        {
            if (!CorrectableFields.Contains(change.Field, StringComparer.Ordinal))
            {
                throw new CorrespondenceRefusedException(RefuseField(change.Field));
            }

            var old = Read(row, change.Field);
            var value = Normalize(change.NewValue);
            if (string.Equals(old, value, StringComparison.Ordinal))
            {
                // A field re-submitted unchanged (every W25 field is posted together) is not a
                // correction and must not appear in the record as one.
                continue;
            }

            planned.Add(Plan(change.Field, old, value));
        }

        if (planned.Count == 0)
        {
            throw new CorrespondenceRefusedException(CoreAr.CorrRefusedCorrectionEmpty);
        }

        // PASS TWO — nothing below this line can refuse the correction.
        foreach (var change in planned)
        {
            change.Apply(row);
        }

        var applied = planned.Select(p => p.Entry).ToList();
        var reason = reasonAr.Trim();
        var correction = new Correction
        {
            CorrespondenceId = correspondenceId,
            Changes = JsonSerializer.Serialize(applied, Json),
            Reason = reason,
            At = now,
        };
        db.Corrections.Add(correction);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var actor = await db.Installation.AsNoTracking().Select(i => i.EmployeeName)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
        await audit.LogAsync(
            actor,
            AuditActionCorrected,
            CoreAr.CorrAuditCorrected(reason),
            CorrespondenceService.AuditEntityType,
            correspondenceId,
            new { fields = applied.Select(a => a.Field).ToArray() },
            cancellationToken).ConfigureAwait(false);

        return new CorrectionView(
            correction.Id,
            correspondenceId,
            [.. applied.Select(Describe)],
            reason,
            now,
            ArabicRelativeTime.Describe(now, now));
    }

    public async Task<IReadOnlyList<CorrectionView>> ListAsync(Guid correspondenceId, DateTime now, CancellationToken cancellationToken = default)
    {
        var rows = await db.Corrections.AsNoTracking()
            .Where(c => c.CorrespondenceId == correspondenceId)
            .OrderByDescending(c => c.At)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return
        [
            .. rows.Select(c => new CorrectionView(
                c.Id,
                c.CorrespondenceId,
                [.. Parse(c.Changes).Select(Describe)],
                c.Reason,
                c.At,
                ArabicRelativeTime.Describe(c.At, now))),
        ];
    }

    /// <summary>
    /// Reads a stored <c>corrections.changes</c> payload. A row written by a newer version, or
    /// damaged, yields an empty list rather than throwing: the log tab must still open, and the
    /// row itself (its reason and its date) is the part the user is looking at.
    /// </summary>
    public static IReadOnlyList<CorrectionEntry> Parse(string? changes)
    {
        if (string.IsNullOrWhiteSpace(changes))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<CorrectionEntry>>(changes, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Turns one stored pair into what the log tab shows. The stored strings are handed back
    /// untouched beside their Arabic reading, so a row written by an older build still renders
    /// and nothing about the stored record depends on this method.
    /// </summary>
    public static CorrectionFieldView Describe(CorrectionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new CorrectionFieldView(
            entry.Field,
            CorrespondenceAr.FieldLabel(entry.Field) ?? CoreAr.CorrFieldUnnamed,
            entry.Old,
            Humanize(entry.Field, entry.Old),
            entry.New,
            Humanize(entry.Field, entry.New));
    }

    /// <summary>
    /// One stored value as the user reads it: a confidentiality level in words, a date in the
    /// format the product uses everywhere else, anything else as it was typed. A value this
    /// build cannot parse is shown as stored rather than hidden — the record is what matters.
    /// </summary>
    private static string? Humanize(string field, string? value)
    {
        if (value is null)
        {
            return null;
        }

        return field switch
        {
            CorrespondenceFields.Confidentiality =>
                Enum.TryParse<Confidentiality>(value, ignoreCase: true, out var level)
                    ? CorrespondenceAr.Confidentiality(level)
                    : value,
            CorrespondenceFields.ExternalDate or CorrespondenceFields.DueAt =>
                TryParseDate(value, out var date) ? ArabicRelativeTime.DateTimeText(date) : value,
            _ => value,
        };
    }

    /// <summary>The Arabic refusal for a field the correction may not touch.</summary>
    private static string RefuseField(string field) =>
        CorrespondenceAr.FieldLabel(field) is { } label
            ? CoreAr.CorrCorrectionFieldUnknown(label)
            : CoreAr.CorrCorrectionFieldNotCorrectable;

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Read(CorrespondenceRow row, string field) => field switch
    {
        CorrespondenceFields.Subject => row.Subject,
        CorrespondenceFields.Type => row.Type,
        CorrespondenceFields.Confidentiality => row.Confidentiality.ToString(),
        CorrespondenceFields.ExternalNumber => row.ExternalNumber,
        CorrespondenceFields.ExternalDate => Iso(row.ExternalDate),
        CorrespondenceFields.DueAt => Iso(row.DueAt),
        CorrespondenceFields.NextStep => row.NextStepAr,
        CorrespondenceFields.BodyText => row.BodyText,
        CorrespondenceFields.Cc => row.Cc,
        CorrespondenceFields.PartyNameSnapshot => row.PartyNameSnapshot,
        _ => null,
    };

    /// <summary>
    /// Validates one change and turns it into an assignment that cannot fail. Everything that
    /// may refuse the correction — an unreadable date, an unknown confidentiality level, a
    /// cleared subject — happens here, while the tracked row is still untouched.
    /// </summary>
    private static PlannedChange Plan(string field, string? old, string? value)
    {
        var entry = new CorrectionEntry(field, old, value);
        switch (field)
        {
            case CorrespondenceFields.Subject:
                // The subject is the one corrected field that cannot be cleared: it names the
                // item everywhere, and a numbered letter with no subject cannot be found again.
                var subject = value ?? throw new CorrespondenceRefusedException(CoreAr.CorrValidationSubjectRequired);
                return new PlannedChange(entry, row => row.Subject = subject);
            case CorrespondenceFields.Type:
                return new PlannedChange(entry, row => row.Type = value);
            case CorrespondenceFields.Confidentiality:
                var level = ParseConfidentiality(value);
                return new PlannedChange(entry, row => row.Confidentiality = level);
            case CorrespondenceFields.ExternalNumber:
                return new PlannedChange(entry, row => row.ExternalNumber = value);
            case CorrespondenceFields.ExternalDate:
                var externalDate = ParseDate(value);
                return new PlannedChange(entry, row => row.ExternalDate = externalDate);
            case CorrespondenceFields.DueAt:
                var dueAt = ParseDate(value);
                return new PlannedChange(entry, row => row.DueAt = dueAt);
            case CorrespondenceFields.NextStep:
                return new PlannedChange(entry, row => row.NextStepAr = value);
            case CorrespondenceFields.BodyText:
                return new PlannedChange(entry, row => row.BodyText = value);
            case CorrespondenceFields.Cc:
                return new PlannedChange(entry, row => row.Cc = value);
            case CorrespondenceFields.PartyNameSnapshot:
                return new PlannedChange(entry, row => row.PartyNameSnapshot = value);
            default:
                throw new CorrespondenceRefusedException(RefuseField(field));
        }
    }

    /// <summary>
    /// Round-trip text for a stored instant. Both versions of a corrected date are kept as text
    /// in one JSON array beside text fields, so the format has to be unambiguous and culture
    /// independent — never the display format of the user.
    /// </summary>
    private static string? Iso(DateTime? value) =>
        value?.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static bool TryParseDate(string value, out DateTime parsed)
    {
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var read))
        {
            parsed = DateTime.SpecifyKind(read, DateTimeKind.Utc);
            return true;
        }

        parsed = default;
        return false;
    }

    private static DateTime? ParseDate(string? value)
    {
        if (value is null)
        {
            return null;
        }

        // The field itself is correctable; it is the value that could not be read, and the user
        // has to be told that and not «هذا الحقل لا يقبل التصحيح».
        return TryParseDate(value, out var parsed)
            ? parsed
            : throw new CorrespondenceRefusedException(CoreAr.CorrCorrectionBadDate);
    }

    private static Confidentiality ParseConfidentiality(string? value) =>
        Enum.TryParse<Confidentiality>(value, ignoreCase: true, out var parsed)
            ? parsed
            : throw new CorrespondenceRefusedException(CoreAr.CorrCorrectionBadConfidentiality);
}
