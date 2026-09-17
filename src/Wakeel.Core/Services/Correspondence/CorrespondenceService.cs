using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using CorrespondenceRow = Wakeel.Core.Data.Entities.Correspondence;

namespace Wakeel.Core.Services.Correspondence;

/// <summary>
/// The correspondence lifecycle (B3-1): drafts, field checks, registering an incoming letter with
/// its official number, the outgoing wizard with its readiness checklist and approval, the status
/// transitions, deletion of an unnumbered draft, cancellation with the number kept, manual
/// closing, archiving, «للمستلم فقط», and the links to another correspondence, a case or a
/// meeting. Every transition writes an <c>audit_log</c> row.
/// </summary>
public interface ICorrespondenceService
{
    /// <summary>Checks the fields of <paramref name="input"/> without touching the database.</summary>
    /// <param name="today">
    /// The day the check is made against, for the "not in the future" and "not in the past"
    /// rules. Only its date part is used.
    /// </param>
    IReadOnlyList<CorrespondenceValidationIssue> Validate(CorrespondenceDraftInput input, DateTime today);

    /// <summary>Creates a draft (status «مسودة», no number).</summary>
    Task<CorrespondenceView> CreateDraftAsync(CorrespondenceDraftInput input, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rewrites a draft from <paramref name="input"/> — the three-second autosave of AGREEMENT
    /// item 32. Refused once the item carries a number: from then on only
    /// <see cref="ICorrectionService"/> may change its fields.
    /// </summary>
    Task<CorrespondenceView> UpdateDraftAsync(Guid id, CorrespondenceDraftInput input, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>One item, or <c>null</c> when it does not exist (or was deleted).</summary>
    Task<CorrespondenceView?> GetAsync(Guid id, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>The correspondence list (W01), newest first.</summary>
    Task<IReadOnlyList<CorrespondenceView>> ListAsync(CorrespondenceQuery query, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks for duplicates of a draft without registering it — what the register screen runs
    /// while the user types (AGREEMENT item 14).
    /// </summary>
    Task<DuplicateScan> ScanForDuplicatesAsync(Guid draftId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers an incoming draft: one transaction issues the incoming official number, stamps
    /// the party name snapshot, moves the item to «جديد» and records any suspected duplicates as
    /// pending reviews. An exact duplicate (same party number and same party) is refused before
    /// the transaction opens. Any failure after the number was issued rolls the number back with
    /// everything else.
    /// </summary>
    Task<IncomingRegistrationResult> RegisterIncomingAsync(Guid draftId, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>The readiness checklist of an outgoing draft (AGREEMENT item 18).</summary>
    Task<ReadinessChecklist> GetReadinessAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves an outgoing draft: one transaction issues the outgoing official number, freezes
    /// the document and moves the item to «جديد». Refused unless every readiness row is
    /// satisfied.
    /// </summary>
    Task<ApprovalResult> ApproveOutgoingAsync(Guid id, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an unnumbered draft (AGREEMENT item 19). Refused for anything that carries a
    /// number — that is a cancellation. Returns false when there was nothing to delete.
    /// </summary>
    Task<bool> DeleteDraftAsync(Guid id, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Cancels a numbered item with a reason; the number stays consumed (AGREEMENT item 19).</summary>
    Task<CorrespondenceView> CancelAsync(Guid id, string reasonAr, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Closes an item manually with a note.</summary>
    Task<CorrespondenceView> CloseAsync(Guid id, string noteAr, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Archives an item.</summary>
    Task<CorrespondenceView> ArchiveAsync(Guid id, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Moves an item to another status, if the move is allowed.</summary>
    Task<CorrespondenceView> ChangeStatusAsync(
        Guid id,
        CorrespondenceStatus to,
        DateTime now,
        string? noteAr = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sets or clears «للمستلم فقط» (AGREEMENT item 7).</summary>
    Task<CorrespondenceView> SetRecipientOnlyAsync(Guid id, bool recipientOnly, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Sets the links to another correspondence, a case and a meeting (W23).</summary>
    Task<CorrespondenceView> SetLinksAsync(Guid id, CorrespondenceLinks links, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Links a stored document to the item. Once the item is approved only
    /// <see cref="CorrespondenceDocumentKind.Attachment"/> may still be added — the original and
    /// its print copy are frozen with the number.
    /// </summary>
    Task LinkDocumentAsync(
        Guid id,
        Guid documentId,
        CorrespondenceDocumentKind kind,
        int sort = 0,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ICorrespondenceService"/>
public sealed class CorrespondenceService(
    WakeelDb db,
    IOfficialNumberService numbers,
    IClockCheckService clockChecks,
    IDuplicateDetector duplicates,
    IAuditService audit) : ICorrespondenceService
{
    public const string AuditActionDraftCreated = "correspondence.draft_created";
    public const string AuditActionDraftDeleted = "correspondence.draft_deleted";
    public const string AuditActionRegistered = "correspondence.registered";
    public const string AuditActionApproved = "correspondence.approved";
    public const string AuditActionTransition = "correspondence.status_changed";
    public const string AuditActionCancelled = "correspondence.cancelled";
    public const string AuditActionClosed = "correspondence.closed";
    public const string AuditActionArchived = "correspondence.archived";
    public const string AuditActionRecipientOnly = "correspondence.recipient_only";
    public const string AuditActionLinksChanged = "correspondence.links_changed";

    /// <summary><c>audit_log.entity_type</c> for every row this package writes.</summary>
    public const string AuditEntityType = "correspondence";

    /// <summary>
    /// How many rows the local search box of W01 normalises in memory before it gives up. Wide
    /// enough that the office's live work is always covered, bounded so a ten-year archive never
    /// turns one keystroke into a full table read; the hybrid index of B3-3 is what searches the
    /// whole archive.
    /// </summary>
    public const int TextScanLimit = 2000;

    public IReadOnlyList<CorrespondenceValidationIssue> Validate(CorrespondenceDraftInput input, DateTime today)
    {
        ArgumentNullException.ThrowIfNull(input);
        var issues = new List<CorrespondenceValidationIssue>();
        var day = ArabicRelativeTime.ToUtc(today).Date;

        if (string.IsNullOrWhiteSpace(input.Subject))
        {
            issues.Add(new CorrespondenceValidationIssue(CorrespondenceFields.Subject, CoreAr.CorrValidationSubjectRequired));
        }
        else if (input.Subject.Trim().Length > CoreAr.CorrSubjectMaxLength)
        {
            issues.Add(new CorrespondenceValidationIssue(CorrespondenceFields.Subject, CoreAr.CorrValidationSubjectTooLong));
        }

        // The recipient may be a directory party, an org unit, or — for a letter from a body not
        // in the directory yet — a typed name. What is refused is no recipient at all.
        var hasRecipient = input.PartyId is not null || input.UnitId is not null || !string.IsNullOrWhiteSpace(input.PartyNameSnapshot);
        if (!hasRecipient)
        {
            issues.Add(new CorrespondenceValidationIssue(
                input.CounterpartyKind == CounterpartyKind.Internal ? CorrespondenceFields.Unit : CorrespondenceFields.Party,
                CoreAr.CorrValidationRecipientRequired));
        }
        else if (input.CounterpartyKind == CounterpartyKind.Internal && input.UnitId is null && input.PartyId is not null)
        {
            issues.Add(new CorrespondenceValidationIssue(CorrespondenceFields.Unit, CoreAr.CorrValidationUnitRequired));
        }
        else if (input.CounterpartyKind == CounterpartyKind.External && input.PartyId is null && input.UnitId is not null)
        {
            issues.Add(new CorrespondenceValidationIssue(CorrespondenceFields.Party, CoreAr.CorrValidationPartyRequired));
        }

        if (input.Direction == InOutDirection.In)
        {
            if (string.IsNullOrWhiteSpace(input.ExternalNumber))
            {
                issues.Add(new CorrespondenceValidationIssue(CorrespondenceFields.ExternalNumber, CoreAr.CorrValidationExternalNumberRequired));
            }

            if (input.ExternalDate is null)
            {
                issues.Add(new CorrespondenceValidationIssue(CorrespondenceFields.ExternalDate, CoreAr.CorrValidationExternalDateRequired));
            }
            else if (ArabicRelativeTime.ToUtc(input.ExternalDate.Value).Date > day)
            {
                issues.Add(new CorrespondenceValidationIssue(CorrespondenceFields.ExternalDate, CoreAr.CorrValidationExternalDateFuture));
            }
        }

        if (input.DueAt is { } due && ArabicRelativeTime.ToUtc(due).Date < day)
        {
            issues.Add(new CorrespondenceValidationIssue(CorrespondenceFields.DueAt, CoreAr.CorrValidationDueBeforeToday));
        }

        return issues;
    }

    public async Task<CorrespondenceView> CreateDraftAsync(CorrespondenceDraftInput input, DateTime now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Subject))
        {
            // The one field a draft cannot do without: the list and the autosave indicator both
            // name the item by it. The rest of the field checks only gate registration/approval,
            // so a half-typed draft still saves (AGREEMENT item 32).
            throw new CorrespondenceRefusedException(
                CoreAr.CorrValidationSubjectRequired,
                [new CorrespondenceValidationIssue(CorrespondenceFields.Subject, CoreAr.CorrValidationSubjectRequired)]);
        }

        var row = new CorrespondenceRow { Status = CorrespondenceStatus.Draft };
        Apply(row, input);
        db.Correspondence.Add(row);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await LogAsync(AuditActionDraftCreated, CoreAr.CorrAuditDraftCreated(row.Subject), row.Id, cancellationToken).ConfigureAwait(false);
        return CorrespondenceViewFactory.Create(row, now, await BuildReadinessAsync(row, cancellationToken).ConfigureAwait(false));
    }

    public async Task<CorrespondenceView> UpdateDraftAsync(Guid id, CorrespondenceDraftInput input, DateTime now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var row = await LoadAsync(id, cancellationToken).ConfigureAwait(false);
        EnsureNotFrozen(row);
        if (input.LinkedCorrespondenceId == row.Id)
        {
            throw new CorrespondenceRefusedException(CoreAr.CorrRefusedLinkSelf);
        }

        Apply(row, input);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return CorrespondenceViewFactory.Create(row, now, await BuildReadinessAsync(row, cancellationToken).ConfigureAwait(false));
    }

    public async Task<CorrespondenceView?> GetAsync(Guid id, DateTime now, CancellationToken cancellationToken = default)
    {
        var row = await db.Correspondence.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken).ConfigureAwait(false);
        return row is null
            ? null
            : CorrespondenceViewFactory.Create(row, now, await BuildReadinessAsync(row, cancellationToken).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<CorrespondenceView>> ListAsync(CorrespondenceQuery query, DateTime now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var rows = db.Correspondence.AsNoTracking();

        if (query.Direction is { } direction)
        {
            rows = rows.Where(c => c.Direction == direction);
        }

        if (query.Statuses is { Count: > 0 } statuses)
        {
            var wanted = statuses.ToList();
            rows = rows.Where(c => wanted.Contains(c.Status));
        }
        else if (!query.IncludeArchived)
        {
            rows = rows.Where(c => c.Status != CorrespondenceStatus.Archived);
        }

        if (query.PartyId is { } partyId)
        {
            rows = rows.Where(c => c.PartyId == partyId);
        }

        if (query.UnitId is { } unitId)
        {
            rows = rows.Where(c => c.UnitId == unitId);
        }

        var limit = Math.Clamp(query.Limit, 1, 5000);
        var searching = !string.IsNullOrWhiteSpace(query.Text);

        // With a search term the window read has to be wider than the page returned: filtering
        // after Take(limit) would search only the newest `limit` rows and answer «لا توجد نتائج»
        // for a subject that is sitting a few hundred rows further down. The window is bounded
        // the same way the duplicate scan bounds its own subject sweep.
        var window = searching ? Math.Max(limit, TextScanLimit) : limit;
        var loaded = await rows.OrderByDescending(c => c.CreatedAt).Take(window).ToListAsync(cancellationToken).ConfigureAwait(false);

        // The local search box of W01. Matching happens in memory on the window just read,
        // because the Arabic normalisation (alef/ya/ta-marbuta, tashkeel, tatweel) has no SQLite
        // equivalent here — the hybrid index of B3-3 is what searches the whole archive.
        if (searching)
        {
            var needle = ArabicText.Normalize(query.Text);
            loaded =
            [
                .. loaded.Where(c =>
                    ArabicText.Normalize(c.Subject).Contains(needle, StringComparison.Ordinal)
                    || ArabicText.Normalize(c.OfficialNumber).Contains(needle, StringComparison.Ordinal)
                    || ArabicText.Normalize(c.ExternalNumber).Contains(needle, StringComparison.Ordinal)
                    || ArabicText.Normalize(c.PartyNameSnapshot).Contains(needle, StringComparison.Ordinal))
                    .Take(limit),
            ];
        }

        // The list deliberately does not build a readiness checklist per row: that would be one
        // extra read per outgoing draft on a screen showing two hundred of them, and the
        // checklist belongs to the wizard, not to the table. CurrentStep is therefore null here
        // and filled by GetAsync on the detail screen.
        return [.. loaded.Select(row => CorrespondenceViewFactory.Create(row, now))];
    }

    public async Task<DuplicateScan> ScanForDuplicatesAsync(Guid draftId, CancellationToken cancellationToken = default)
    {
        var row = await LoadAsync(draftId, cancellationToken).ConfigureAwait(false);
        return await duplicates.ScanAsync(Candidate(row), cancellationToken).ConfigureAwait(false);
    }

    public async Task<IncomingRegistrationResult> RegisterIncomingAsync(Guid draftId, DateTime now, CancellationToken cancellationToken = default)
    {
        var row = await LoadAsync(draftId, cancellationToken).ConfigureAwait(false);
        if (row.Direction != InOutDirection.In)
        {
            throw new CorrespondenceRefusedException(CoreAr.CorrRefusedDirectionIn);
        }

        EnsureUnnumberedDraft(row);
        EnsureValid(row, now);

        var scan = await duplicates.ScanAsync(Candidate(row), cancellationToken).ConfigureAwait(false);
        if (scan.Exact is { } blocking)
        {
            throw new CorrespondenceRefusedException(blocking.ReasonAr);
        }

        var verdict = await EnsureClockAsync(now, cancellationToken).ConfigureAwait(false);
        await SnapshotPartyNameAsync(row, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<DuplicateMatch> suspected = [];
        OfficialNumberResult issued;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            issued = await numbers.IssueAsync(InOutDirection.In, now, verdict, cancellationToken).ConfigureAwait(false);
            row.OfficialNumber = issued.Number;
            row.NumberIssuedAt = now;
            row.Status = CorrespondenceStatus.New;
            suspected = await duplicates.RecordSuspectedAsync(row.Id, scan.Suspected, cancellationToken).ConfigureAwait(false);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await LogAsync(AuditActionRegistered, CoreAr.CorrAuditRegistered(issued.Number), row.Id, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await RollbackAsync(transaction, row, cancellationToken).ConfigureAwait(false);
            throw;
        }

        return new IncomingRegistrationResult(
            CorrespondenceViewFactory.Create(row, now),
            issued.Number,
            issued.NearLimitWarning,
            suspected);
    }

    public async Task<ReadinessChecklist> GetReadinessAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await LoadAsync(id, cancellationToken).ConfigureAwait(false);
        return await BuildReadinessAsync(row, cancellationToken).ConfigureAwait(false)
            ?? new ReadinessChecklist([], CoreAr.CorrReadyRemaining(0));
    }

    public async Task<ApprovalResult> ApproveOutgoingAsync(Guid id, DateTime now, CancellationToken cancellationToken = default)
    {
        var row = await LoadAsync(id, cancellationToken).ConfigureAwait(false);
        if (row.Direction != InOutDirection.Out)
        {
            throw new CorrespondenceRefusedException(CoreAr.CorrRefusedDirectionOut);
        }

        EnsureUnnumberedDraft(row);

        // The checklist speaks first: it is the screen the user is standing on (AGREEMENT item
        // 18), and «أكمل بنود الجاهزية قبل الاعتماد» points at the rows that are still empty.
        // The field checks below then catch what a complete checklist still cannot accept, such
        // as a due date already in the past.
        var readiness = await BuildReadinessAsync(row, cancellationToken).ConfigureAwait(false);
        if (readiness is null || !readiness.IsReady)
        {
            throw new CorrespondenceRefusedException(CoreAr.CorrRefusedNotReady);
        }

        EnsureValid(row, now);

        var verdict = await EnsureClockAsync(now, cancellationToken).ConfigureAwait(false);
        await SnapshotPartyNameAsync(row, cancellationToken).ConfigureAwait(false);

        OfficialNumberResult issued;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            issued = await numbers.IssueAsync(InOutDirection.Out, now, verdict, cancellationToken).ConfigureAwait(false);
            row.OfficialNumber = issued.Number;
            row.NumberIssuedAt = now;
            row.ApprovedAt = now;
            row.Status = CorrespondenceStatus.New;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await LogAsync(AuditActionApproved, CoreAr.CorrAuditApproved(issued.Number), row.Id, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await RollbackAsync(transaction, row, cancellationToken).ConfigureAwait(false);
            throw;
        }

        return new ApprovalResult(CorrespondenceViewFactory.Create(row, now), issued.Number, issued.NearLimitWarning);
    }

    public async Task<bool> DeleteDraftAsync(Guid id, DateTime now, CancellationToken cancellationToken = default)
    {
        var row = await db.Correspondence.FirstOrDefaultAsync(c => c.Id == id, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }

        if (row.OfficialNumber is not null)
        {
            throw new CorrespondenceRefusedException(CoreAr.CorrRefusedDeleteNumbered);
        }

        if (row.Status != CorrespondenceStatus.Draft)
        {
            throw new CorrespondenceRefusedException(CoreAr.CorrRefusedNotDraft);
        }

        var subject = row.Subject;

        // DATA-MODEL §0 allows no physical delete of a synced row, and there is no AFTER DELETE
        // trigger, so a DELETE would never reach the other devices and the draft would reappear
        // on the next sync. The soft delete is what "permanently" means to the user: the global
        // query filter hides it from every screen and every service, no command brings it back,
        // and the UPDATE travels to the other devices like any other change.
        db.SoftDelete(row);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await LogAsync(AuditActionDraftDeleted, CoreAr.CorrAuditDraftDeleted(subject), id, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<CorrespondenceView> CancelAsync(Guid id, string reasonAr, DateTime now, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reasonAr))
        {
            throw new CorrespondenceRefusedException(CoreAr.CorrRefusedCancelReason);
        }

        var row = await LoadAsync(id, cancellationToken).ConfigureAwait(false);
        if (row.OfficialNumber is null)
        {
            // AGREEMENT item 19: an unnumbered draft is deleted, not cancelled — offering both
            // would leave numberless "cancelled" rows nobody can act on. The refusal has to say
            // that and point at the command that does apply.
            throw new CorrespondenceRefusedException(CoreAr.CorrRefusedCancelDraft);
        }

        var from = row.Status;
        CorrespondenceStateMachine.EnsureTransition(from, CorrespondenceStatus.Cancelled);

        var number = row.OfficialNumber;
        var reason = reasonAr.Trim();
        row.Status = CorrespondenceStatus.Cancelled;
        row.CancelReason = reason;
        db.Followups.Add(StatusFollowup(row.Id, from, CorrespondenceStatus.Cancelled, reason));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The number stays exactly as it was: it is consumed forever, and the item keeps showing
        // it beside its reason.
        await LogAsync(AuditActionCancelled, CoreAr.CorrAuditCancelled(number, reason), row.Id, cancellationToken).ConfigureAwait(false);
        return CorrespondenceViewFactory.Create(row, now);
    }

    public async Task<CorrespondenceView> CloseAsync(Guid id, string noteAr, DateTime now, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(noteAr))
        {
            throw new CorrespondenceRefusedException(CoreAr.CorrRefusedCloseNote);
        }

        var row = await LoadAsync(id, cancellationToken).ConfigureAwait(false);
        var from = row.Status;
        CorrespondenceStateMachine.EnsureTransition(from, CorrespondenceStatus.Closed);

        var note = noteAr.Trim();
        row.Status = CorrespondenceStatus.Closed;
        row.CloseNote = note;
        db.Followups.Add(StatusFollowup(row.Id, from, CorrespondenceStatus.Closed, note));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await LogAsync(AuditActionClosed, CoreAr.CorrAuditClosed(note), row.Id, cancellationToken).ConfigureAwait(false);
        return CorrespondenceViewFactory.Create(row, now);
    }

    public async Task<CorrespondenceView> ArchiveAsync(Guid id, DateTime now, CancellationToken cancellationToken = default)
    {
        var row = await LoadAsync(id, cancellationToken).ConfigureAwait(false);
        var from = row.Status;
        CorrespondenceStateMachine.EnsureTransition(from, CorrespondenceStatus.Archived);

        row.Status = CorrespondenceStatus.Archived;
        row.ArchivedAt = now;
        db.Followups.Add(StatusFollowup(row.Id, from, CorrespondenceStatus.Archived, null));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await LogAsync(AuditActionArchived, CoreAr.CorrAuditArchived, row.Id, cancellationToken).ConfigureAwait(false);
        return CorrespondenceViewFactory.Create(row, now);
    }

    public async Task<CorrespondenceView> ChangeStatusAsync(
        Guid id,
        CorrespondenceStatus to,
        DateTime now,
        string? noteAr = null,
        CancellationToken cancellationToken = default)
    {
        // The three commands that carry their own stored field route through their own method, so
        // a caller can never reach «ملغى» without a reason or «مغلق» without a note.
        switch (to)
        {
            case CorrespondenceStatus.Cancelled:
                return await CancelAsync(id, noteAr ?? string.Empty, now, cancellationToken).ConfigureAwait(false);
            case CorrespondenceStatus.Closed:
                return await CloseAsync(id, noteAr ?? string.Empty, now, cancellationToken).ConfigureAwait(false);
            case CorrespondenceStatus.Archived:
                return await ArchiveAsync(id, now, cancellationToken).ConfigureAwait(false);
            default:
                break;
        }

        var row = await LoadAsync(id, cancellationToken).ConfigureAwait(false);
        var from = row.Status;

        // A draft leaves «مسودة» only through registration (incoming) or approval (outgoing) —
        // the two operations that issue the official number inside their own transaction. Letting
        // a plain status change do it would leave an open numberless row that can no longer be
        // deleted, cancelled or corrected while still being counted by the attention centre.
        if (from == CorrespondenceStatus.Draft)
        {
            throw new CorrespondenceRefusedException(CoreAr.CorrRefusedDraftNeedsNumbering);
        }

        CorrespondenceStateMachine.EnsureTransition(from, to);

        row.Status = to;
        db.Followups.Add(StatusFollowup(row.Id, from, to, noteAr));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await LogAsync(
            AuditActionTransition,
            CoreAr.CorrAuditTransition(CorrespondenceAr.Status(from), CorrespondenceAr.Status(to)),
            row.Id,
            cancellationToken).ConfigureAwait(false);
        return CorrespondenceViewFactory.Create(row, now);
    }

    public async Task<CorrespondenceView> SetRecipientOnlyAsync(Guid id, bool recipientOnly, DateTime now, CancellationToken cancellationToken = default)
    {
        var row = await LoadAsync(id, cancellationToken).ConfigureAwait(false);

        // AGREEMENT item 7 is about who may receive the row in a sync package, not about the
        // letter's own data, so it stays changeable after the number is issued — a letter can be
        // reclassified at any time and the sync export reads the flag as it stands.
        var changed = row.RecipientOnly != recipientOnly;
        row.RecipientOnly = recipientOnly;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // «للمستلم فقط» decides whether the letter ever leaves this installation in a sync
        // package, so lifting it is a security-relevant act and must leave a trace of who did it
        // and when. Re-submitting the flag unchanged is not an act and writes nothing.
        if (changed)
        {
            await LogAsync(
                AuditActionRecipientOnly,
                recipientOnly ? CoreAr.CorrAuditRecipientOnlySet : CoreAr.CorrAuditRecipientOnlyCleared,
                row.Id,
                cancellationToken).ConfigureAwait(false);
        }

        return CorrespondenceViewFactory.Create(row, now);
    }

    public async Task<CorrespondenceView> SetLinksAsync(Guid id, CorrespondenceLinks links, DateTime now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(links);
        var row = await LoadAsync(id, cancellationToken).ConfigureAwait(false);
        if (links.LinkedCorrespondenceId == row.Id)
        {
            throw new CorrespondenceRefusedException(CoreAr.CorrRefusedLinkSelf);
        }

        var changed = row.LinkedCorrespondenceId != links.LinkedCorrespondenceId
            || row.CaseId != links.CaseId
            || row.MeetingId != links.MeetingId;
        row.LinkedCorrespondenceId = links.LinkedCorrespondenceId;
        row.CaseId = links.CaseId;
        row.MeetingId = links.MeetingId;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (changed)
        {
            await LogAsync(AuditActionLinksChanged, CoreAr.CorrAuditLinksChanged, row.Id, cancellationToken).ConfigureAwait(false);
        }

        return CorrespondenceViewFactory.Create(row, now);
    }

    public async Task LinkDocumentAsync(
        Guid id,
        Guid documentId,
        CorrespondenceDocumentKind kind,
        int sort = 0,
        CancellationToken cancellationToken = default)
    {
        var row = await LoadAsync(id, cancellationToken).ConfigureAwait(false);
        if (row.OfficialNumber is not null && kind != CorrespondenceDocumentKind.Attachment)
        {
            // The approval froze the letter itself; only later attachments (a reply, a scan of
            // the signed copy) may still join it.
            throw new CorrespondenceRefusedException(CoreAr.CorrRefusedAlreadyNumbered);
        }

        var existing = await db.CorrespondenceDocuments
            .FirstOrDefaultAsync(d => d.CorrespondenceId == id && d.DocumentId == documentId && d.Kind == kind, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            existing.Sort = sort;
        }
        else
        {
            db.CorrespondenceDocuments.Add(new CorrespondenceDocument
            {
                CorrespondenceId = id,
                DocumentId = documentId,
                Kind = kind,
                Sort = sort,
            });
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads a tracked row, or refuses in Arabic. Every command takes this path, so a deleted or
    /// unknown id never surfaces as a null-reference somewhere deeper.
    /// </summary>
    private async Task<CorrespondenceRow> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Correspondence.FirstOrDefaultAsync(c => c.Id == id, cancellationToken).ConfigureAwait(false)
        ?? throw new CorrespondenceRefusedException(CoreAr.CorrRefusedNotFound);

    private static void EnsureNotFrozen(CorrespondenceRow row)
    {
        if (row.OfficialNumber is not null)
        {
            throw new CorrespondenceRefusedException(CoreAr.CorrRefusedAlreadyNumbered);
        }
    }

    private static void EnsureUnnumberedDraft(CorrespondenceRow row)
    {
        EnsureNotFrozen(row);
        if (row.Status != CorrespondenceStatus.Draft)
        {
            throw new CorrespondenceRefusedException(CoreAr.CorrRefusedNotDraft);
        }
    }

    private void EnsureValid(CorrespondenceRow row, DateTime now)
    {
        var issues = Validate(ToInput(row), now);
        if (issues.Count > 0)
        {
            throw new CorrespondenceRefusedException(issues[0].MessageAr, issues);
        }
    }

    /// <summary>
    /// Runs the ARCHITECTURE §9 clock check once and turns a bad verdict into an Arabic refusal,
    /// so the user reads «لن تُصدر أرقام رسمية حتى التصحيح» instead of the numbering service's own
    /// English contract message. The verdict is then handed to the numbering service so the check
    /// is not repeated inside the transaction.
    /// </summary>
    private async Task<ClockVerdict> EnsureClockAsync(DateTime now, CancellationToken cancellationToken)
    {
        var check = await clockChecks.CheckAsync(now, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (check.Verdict != ClockVerdict.Ok)
        {
            throw new CorrespondenceRefusedException(CoreAr.ClockBannerNumberingBlocked);
        }

        return check.Verdict;
    }

    /// <summary>
    /// Stamps <c>party_name_snapshot</c> from the directory (or the structure) at the moment the
    /// number is issued: the letter must keep the name the body carried that day even after the
    /// directory entry is renamed. A name typed by hand is kept as it is.
    /// </summary>
    private async Task SnapshotPartyNameAsync(CorrespondenceRow row, CancellationToken cancellationToken)
    {
        if (row.PartyId is { } partyId)
        {
            var name = await db.Parties.AsNoTracking().Where(p => p.Id == partyId).Select(p => p.Name)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(name))
            {
                row.PartyNameSnapshot = name;
                return;
            }
        }

        if (row.UnitId is { } unitId)
        {
            var name = await db.OrgUnits.AsNoTracking().Where(u => u.Id == unitId).Select(u => u.Name)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(name))
            {
                row.PartyNameSnapshot = name;
            }
        }
    }

    /// <summary>
    /// Rolls the transaction back and brings the context back in step with the database. Without
    /// the reload the tracked <c>official_numbers</c> row would keep the sequence value the
    /// rolled-back attempt gave it and the next number would silently skip one — the very gap the
    /// AGREEMENT item 37 integrity check exists to find.
    /// </summary>
    /// <remarks>
    /// Only the entities this operation touched are swept: <paramref name="row"/> itself, the
    /// number sequence, the suspected-duplicate reviews it may have written, and the audit and
    /// change-log rows that followed them. A <see cref="WakeelDb"/> is scoped to one user session
    /// and other screens share it, so detaching or reloading everything the tracker holds would
    /// throw away unsaved edits that have nothing to do with this registration.
    /// </remarks>
    private async Task RollbackAsync(IDbContextTransaction transaction, CorrespondenceRow row, CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        foreach (var entry in db.ChangeTracker.Entries().ToList())
        {
            var touched = ReferenceEquals(entry.Entity, row)
                || entry.Entity is OfficialNumber or DuplicateReview or AuditLogEntry or ChangeLogEntry;
            if (!touched)
            {
                continue;
            }

            if (entry.State == EntityState.Added)
            {
                entry.State = EntityState.Detached;
                continue;
            }

            await entry.ReloadAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task LogAsync(string action, string summaryAr, Guid entityId, CancellationToken cancellationToken)
    {
        var actor = await ActorAsync(cancellationToken).ConfigureAwait(false);
        await audit.LogAsync(actor, action, summaryAr, AuditEntityType, entityId, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The one operating account of this installation (AGREEMENT item 7: no permission system),
    /// read once per service instance — a scope is one user session, so the name cannot change
    /// under it.
    /// </summary>
    private async Task<string> ActorAsync(CancellationToken cancellationToken)
    {
        _actor ??= await db.Installation.AsNoTracking().Select(i => i.EmployeeName)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
        return _actor;
    }

    private string? _actor;

    private static Followup StatusFollowup(Guid correspondenceId, CorrespondenceStatus from, CorrespondenceStatus to, string? noteAr) =>
        new()
        {
            CorrespondenceId = correspondenceId,
            Kind = FollowupKind.Status,
            Note = string.IsNullOrWhiteSpace(noteAr) ? null : noteAr.Trim(),
            StatusFrom = from,
            StatusTo = to,
        };

    private static DuplicateCandidate Candidate(CorrespondenceRow row) =>
        new(row.Direction, row.Subject, row.ExternalNumber, row.PartyId, row.UnitId, row.PartyNameSnapshot, row.Id);

    private static CorrespondenceDraftInput ToInput(CorrespondenceRow row) => new()
    {
        Direction = row.Direction,
        Subject = row.Subject,
        Type = row.Type,
        Confidentiality = row.Confidentiality,
        RecipientOnly = row.RecipientOnly,
        CounterpartyKind = row.CounterpartyKind,
        PartyId = row.PartyId,
        UnitId = row.UnitId,
        PartyNameSnapshot = row.PartyNameSnapshot,
        ExternalNumber = row.ExternalNumber,
        ExternalDate = row.ExternalDate,
        Cc = row.Cc,
        NextStepAr = row.NextStepAr,
        DueAt = row.DueAt,
        TemplateId = row.TemplateId,
        BodyText = row.BodyText,
        LinkedCorrespondenceId = row.LinkedCorrespondenceId,
        CaseId = row.CaseId,
        MeetingId = row.MeetingId,
    };

    private static void Apply(CorrespondenceRow row, CorrespondenceDraftInput input)
    {
        row.Direction = input.Direction;
        row.Subject = input.Subject.Trim();
        row.Type = Trimmed(input.Type);
        row.Confidentiality = input.Confidentiality;
        row.RecipientOnly = input.RecipientOnly;
        row.CounterpartyKind = input.CounterpartyKind;
        row.PartyId = input.PartyId;
        row.UnitId = input.UnitId;
        row.PartyNameSnapshot = Trimmed(input.PartyNameSnapshot);
        row.ExternalNumber = Trimmed(input.ExternalNumber);
        row.ExternalDate = input.ExternalDate;
        row.Cc = Trimmed(input.Cc);
        row.NextStepAr = Trimmed(input.NextStepAr);
        row.DueAt = input.DueAt;
        row.TemplateId = input.TemplateId;
        row.BodyText = input.BodyText;
        row.LinkedCorrespondenceId = input.LinkedCorrespondenceId;
        row.CaseId = input.CaseId;
        row.MeetingId = input.MeetingId;
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// The readiness checklist, or <c>null</c> for an incoming item (which has no wizard). Built
    /// from the row plus one indexed read of its document links.
    /// </summary>
    private async Task<ReadinessChecklist?> BuildReadinessAsync(CorrespondenceRow row, CancellationToken cancellationToken)
    {
        if (row.Direction != InOutDirection.Out)
        {
            return null;
        }

        var hasOriginal = !string.IsNullOrWhiteSpace(row.BodyText)
            || await db.CorrespondenceDocuments.AsNoTracking()
                .AnyAsync(
                    d => d.CorrespondenceId == row.Id && d.Kind == CorrespondenceDocumentKind.Original,
                    cancellationToken)
                .ConfigureAwait(false);

        var hasRecipient = row.PartyId is not null || row.UnitId is not null || !string.IsNullOrWhiteSpace(row.PartyNameSnapshot);

        var items = new List<ReadinessItem>
        {
            new(
                OutgoingStep.Data,
                CoreAr.CorrReadySubject,
                !string.IsNullOrWhiteSpace(row.Subject),
                CoreAr.CorrReadySubjectHint),
            new(
                OutgoingStep.Recipient,
                CoreAr.CorrReadyRecipient,
                hasRecipient,
                CoreAr.CorrReadyRecipientHint),
            new(
                OutgoingStep.Template,
                CoreAr.CorrReadyTemplate,
                row.TemplateId is not null,
                CoreAr.CorrReadyTemplateHint),
            new(
                OutgoingStep.Document,
                CoreAr.CorrReadyDocument,
                hasOriginal,
                CoreAr.CorrReadyDocumentHint),
            new(
                OutgoingStep.Review,
                CoreAr.CorrReadyNotApproved,
                row.OfficialNumber is null,
                CoreAr.CorrReadyNotApprovedHint),
        };

        return new ReadinessChecklist(items, CoreAr.CorrReadyRemaining(items.Count(i => !i.Done)));
    }
}
