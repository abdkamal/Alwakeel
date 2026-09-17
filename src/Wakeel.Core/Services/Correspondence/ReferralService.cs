using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;

namespace Wakeel.Core.Services.Correspondence;

/// <summary>
/// What the letter package needs in order to produce the print copy that carries the referral
/// text (AGREEMENT item 31). Defined here, beside the service that asks for it, and implemented
/// in the letter/composer package (OpenXML on the template, or a composed PDF) — so Core never
/// depends on Word, OpenXML or a rendering engine, and an installation without them still
/// records referrals.
/// </summary>
public interface IDerivedDocumentBuilder
{
    /// <summary>
    /// Builds the print copy of <paramref name="request"/>'s correspondence with the referral
    /// text placed in the free space at the foot of the last page, or on an added page when it
    /// does not fit. The original document is never modified: the result is a new document that
    /// points back at it.
    /// </summary>
    Task<DerivedDocumentResult> BuildAsync(DerivedDocumentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers the same question without producing anything, so the screen can warn «ستُضاف صفحة»
    /// before the user saves (AGREEMENT items 31 and 34).
    /// </summary>
    Task<DerivedDocumentPreview> PreviewAsync(DerivedDocumentRequest request, CancellationToken cancellationToken = default);
}

/// <summary>The print copy asked for.</summary>
/// <param name="CorrespondenceId">The correspondence whose print copy is wanted.</param>
/// <param name="ReferralId">The referral whose text goes on it.</param>
/// <param name="SourceDocumentId">The original document, when the correspondence has one.</param>
/// <param name="ReferralTextAr">The referral text, exactly as the user wrote it.</param>
/// <param name="ToAr">Who it is referred to, for the line above the text.</param>
/// <param name="DueAt">The deadline, when one was set.</param>
public sealed record DerivedDocumentRequest(
    Guid CorrespondenceId,
    Guid ReferralId,
    Guid? SourceDocumentId,
    string ReferralTextAr,
    string? ToAr,
    DateTime? DueAt);

/// <summary>What the builder produced.</summary>
/// <param name="DocumentId">The new document in the vault.</param>
/// <param name="ExtraPageAdded">True when the text did not fit and a page was added.</param>
/// <param name="NoticeAr">What to tell the user before saving, or <c>null</c> when there is nothing to say.</param>
public sealed record DerivedDocumentResult(Guid DocumentId, bool ExtraPageAdded, string? NoticeAr);

/// <summary>The answer to "will this need an extra page?", before anything is written.</summary>
/// <param name="ExtraPageAdded">True when the referral text will not fit on the last page.</param>
/// <param name="NoticeAr">What to show the user, or <c>null</c>.</param>
public sealed record DerivedDocumentPreview(bool ExtraPageAdded, string? NoticeAr);

/// <summary>One referral, ready for the referrals tab (W22).</summary>
/// <param name="Id">The referral row.</param>
/// <param name="CorrespondenceId">The correspondence it belongs to.</param>
/// <param name="ToUnitId">The unit it was referred to, when it was a unit.</param>
/// <param name="ToAr">Who it was referred to, as shown.</param>
/// <param name="TextAr">The referral text.</param>
/// <param name="DueAt">The deadline.</param>
/// <param name="DueAr">The deadline as «خلال يومين» / «قبل 3 أيام».</param>
/// <param name="Status">Stored status; <see cref="ReferralStatus.Overdue"/> is derived, not stored.</param>
/// <param name="StatusAr">The status's Arabic wording.</param>
/// <param name="DerivedDocumentId">The print copy, when one was produced.</param>
/// <param name="ExtraPageAdded">Whether that print copy needed an added page.</param>
/// <param name="CreatedAt">When the referral was written.</param>
/// <param name="CreatedAr">That instant as «قبل ساعتين».</param>
public sealed record ReferralView(
    Guid Id,
    Guid CorrespondenceId,
    Guid? ToUnitId,
    string? ToAr,
    string TextAr,
    DateTime? DueAt,
    string? DueAr,
    ReferralStatus Status,
    string StatusAr,
    Guid? DerivedDocumentId,
    bool ExtraPageAdded,
    DateTime CreatedAt,
    string CreatedAr);

/// <summary>
/// Referrals of a correspondence item to a unit or a person, with a deadline, and the derived
/// print copy that carries the referral text (AGREEMENT item 31). The original correspondence and
/// its original document are never touched.
/// </summary>
public interface IReferralService
{
    /// <summary>
    /// Records a referral. When a <see cref="IDerivedDocumentBuilder"/> is registered, the print
    /// copy is built too and linked to the correspondence as
    /// <see cref="CorrespondenceDocumentKind.DerivedPrint"/>; without one (no Word, no composer
    /// yet) the referral is still recorded and the print copy can be produced later.
    /// </summary>
    Task<ReferralView> CreateAsync(
        Guid correspondenceId,
        string textAr,
        Guid? toUnitId,
        string? toNameAr,
        DateTime? dueAt,
        DateTime now,
        CancellationToken cancellationToken = default);

    /// <summary>Asks the builder whether the referral text will need an added page, before saving.</summary>
    Task<DerivedDocumentPreview> PreviewAsync(
        Guid correspondenceId,
        string textAr,
        string? toNameAr,
        DateTime? dueAt,
        CancellationToken cancellationToken = default);

    /// <summary>The referrals of one correspondence item, newest first.</summary>
    Task<IReadOnlyList<ReferralView>> ListAsync(Guid correspondenceId, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Marks a referral answered.</summary>
    Task<ReferralView> MarkAnsweredAsync(Guid referralId, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Closes a referral.</summary>
    Task<ReferralView> CloseAsync(Guid referralId, DateTime now, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IReferralService"/>
public sealed class ReferralService(
    WakeelDb db,
    IAuditService audit,
    IDerivedDocumentBuilder? derivedDocuments = null) : IReferralService
{
    public const string AuditActionReferred = "correspondence.referred";

    public async Task<ReferralView> CreateAsync(
        Guid correspondenceId,
        string textAr,
        Guid? toUnitId,
        string? toNameAr,
        DateTime? dueAt,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(textAr))
        {
            throw new CorrespondenceRefusedException(CoreAr.CorrRefusedReferralText);
        }

        if (toUnitId is null && string.IsNullOrWhiteSpace(toNameAr))
        {
            throw new CorrespondenceRefusedException(CoreAr.CorrRefusedReferralTarget);
        }

        var correspondence = await db.Correspondence.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == correspondenceId, cancellationToken).ConfigureAwait(false)
            ?? throw new CorrespondenceRefusedException(CoreAr.CorrRefusedNotFound);

        // A withdrawn letter (AGREEMENT item 19) and a filed one are both out of the office's
        // hands: asking a unit to act on either would produce a deadline nobody can meet and a
        // referral that the correspondence's own screen no longer shows as live work.
        if (correspondence.Status is CorrespondenceStatus.Cancelled or CorrespondenceStatus.Archived)
        {
            throw new CorrespondenceRefusedException(CoreAr.CorrRefusedReferralStatus);
        }

        var toName = await ResolveTargetNameAsync(toUnitId, toNameAr, cancellationToken).ConfigureAwait(false);

        var referral = new Referral
        {
            CorrespondenceId = correspondenceId,
            ToUnitId = toUnitId,
            ToName = toName,
            Text = textAr.Trim(),
            DueAt = dueAt,
            Status = ReferralStatus.Open,
        };
        db.Referrals.Add(referral);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (derivedDocuments is not null)
        {
            var sourceDocumentId = await OriginalDocumentIdAsync(correspondenceId, cancellationToken).ConfigureAwait(false);
            var built = await derivedDocuments.BuildAsync(
                new DerivedDocumentRequest(correspondenceId, referral.Id, sourceDocumentId, referral.Text, toName, dueAt),
                cancellationToken).ConfigureAwait(false);

            referral.DerivedDocumentId = built.DocumentId;
            referral.ExtraPageAdded = built.ExtraPageAdded;

            // The print copy is an extra document of the correspondence, never a replacement: the
            // original row in correspondence_documents keeps its Original kind and its own id.
            db.CorrespondenceDocuments.Add(new CorrespondenceDocument
            {
                CorrespondenceId = correspondenceId,
                DocumentId = built.DocumentId,
                Kind = CorrespondenceDocumentKind.DerivedPrint,
                Sort = 0,
            });
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await LogAsync(CoreAr.CorrAuditReferred(toName ?? string.Empty), correspondence.Id, cancellationToken).ConfigureAwait(false);
        return ToView(referral, now);
    }

    public async Task<DerivedDocumentPreview> PreviewAsync(
        Guid correspondenceId,
        string textAr,
        string? toNameAr,
        DateTime? dueAt,
        CancellationToken cancellationToken = default)
    {
        if (derivedDocuments is null)
        {
            // Nothing can be said about a page that will not be rendered; the referral itself is
            // still allowed, so this is not a refusal.
            return new DerivedDocumentPreview(false, null);
        }

        var sourceDocumentId = await OriginalDocumentIdAsync(correspondenceId, cancellationToken).ConfigureAwait(false);
        return await derivedDocuments.PreviewAsync(
            new DerivedDocumentRequest(correspondenceId, Guid.Empty, sourceDocumentId, textAr?.Trim() ?? string.Empty, toNameAr, dueAt),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ReferralView>> ListAsync(Guid correspondenceId, DateTime now, CancellationToken cancellationToken = default)
    {
        var rows = await db.Referrals.AsNoTracking()
            .Where(r => r.CorrespondenceId == correspondenceId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(r => ToView(r, now))];
    }

    public Task<ReferralView> MarkAnsweredAsync(Guid referralId, DateTime now, CancellationToken cancellationToken = default) =>
        SetStatusAsync(referralId, ReferralStatus.Answered, now, cancellationToken);

    public Task<ReferralView> CloseAsync(Guid referralId, DateTime now, CancellationToken cancellationToken = default) =>
        SetStatusAsync(referralId, ReferralStatus.Closed, now, cancellationToken);

    private async Task<ReferralView> SetStatusAsync(Guid referralId, ReferralStatus status, DateTime now, CancellationToken cancellationToken)
    {
        var referral = await db.Referrals.FirstOrDefaultAsync(r => r.Id == referralId, cancellationToken).ConfigureAwait(false)
            ?? throw new CorrespondenceRefusedException(CoreAr.CorrRefusedNotFound);
        referral.Status = status;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Answering or closing a referral is a transition like any other, so the log tab of the
        // correspondence shows it beside the referral itself (the B3 specification asks for an
        // audit row per transition).
        await LogAsync(
            CoreAr.CorrAuditReferralStatus(referral.ToName ?? string.Empty, CorrespondenceAr.Referral(status)),
            referral.CorrespondenceId,
            cancellationToken).ConfigureAwait(false);
        return ToView(referral, now);
    }

    private async Task<Guid?> OriginalDocumentIdAsync(Guid correspondenceId, CancellationToken cancellationToken) =>
        await db.CorrespondenceDocuments.AsNoTracking()
            .Where(d => d.CorrespondenceId == correspondenceId && d.Kind == CorrespondenceDocumentKind.Original)
            .OrderBy(d => d.Sort)
            .Select(d => (Guid?)d.DocumentId)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// A referral to a unit is stored with the unit's name as well as its id, so the referrals
    /// tab still reads correctly after the structure is renamed — the same reason correspondence
    /// keeps a party name snapshot.
    /// </summary>
    private async Task<string?> ResolveTargetNameAsync(Guid? toUnitId, string? toNameAr, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(toNameAr))
        {
            return toNameAr.Trim();
        }

        if (toUnitId is not { } unitId)
        {
            return null;
        }

        return await db.OrgUnits.AsNoTracking().Where(u => u.Id == unitId).Select(u => u.Name)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task LogAsync(string summaryAr, Guid correspondenceId, CancellationToken cancellationToken)
    {
        var actor = await db.Installation.AsNoTracking().Select(i => i.EmployeeName)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
        await audit.LogAsync(
            actor,
            AuditActionReferred,
            summaryAr,
            CorrespondenceService.AuditEntityType,
            correspondenceId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// <see cref="ReferralStatus.Overdue"/> is never stored: it is what an open referral past its
    /// deadline IS, and storing it would need a background pass to keep it true. It is derived on
    /// every read instead, so the referrals tab is right the moment the deadline passes.
    /// </summary>
    private static ReferralView ToView(Referral referral, DateTime now)
    {
        // Both sides go through ToUtc: the screens pass a local-kind «now», the stored deadline
        // is UTC, and comparing them raw would move the overdue boundary by the clock's offset.
        var status = referral.Status == ReferralStatus.Open
            && referral.DueAt is { } due
            && ArabicRelativeTime.ToUtc(due) < ArabicRelativeTime.ToUtc(now)
            ? ReferralStatus.Overdue
            : referral.Status;

        return new ReferralView(
            referral.Id,
            referral.CorrespondenceId,
            referral.ToUnitId,
            referral.ToName,
            referral.Text,
            referral.DueAt,
            referral.DueAt is null ? null : ArabicRelativeTime.Describe(referral.DueAt.Value, now),
            status,
            CorrespondenceAr.Referral(status),
            referral.DerivedDocumentId,
            referral.ExtraPageAdded,
            referral.CreatedAt,
            ArabicRelativeTime.Describe(referral.CreatedAt, now));
    }
}
