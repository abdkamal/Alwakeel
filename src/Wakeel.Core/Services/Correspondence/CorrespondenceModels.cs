using Wakeel.Core.Data;
using CorrespondenceRow = Wakeel.Core.Data.Entities.Correspondence;

namespace Wakeel.Core.Services.Correspondence;

// B3-1 — the shapes the correspondence services take in and hand back. Everything a screen
// binds to is here, already carrying its Arabic wording (AGREEMENT item 15, ARCHITECTURE §12):
// a screen never maps an enum to a word of its own.
//
// NOTE on the namespace: this namespace is itself called «Correspondence», which shadows the
// entity type of the same name. Every file in this folder therefore aliases the entity as
// CorrespondenceRow rather than fighting the lookup rules.

/// <summary>
/// A business refusal the user can act on, carrying the finished Arabic sentence to show. Kept
/// distinct from <see cref="InvalidOperationException"/> (which Core uses for programming errors
/// such as "numbering outside a transaction") so a screen can show <see cref="MessageAr"/>
/// directly instead of sending the exception through <see cref="IErrorMapper"/> and losing the
/// specific wording.
/// </summary>
public sealed class CorrespondenceRefusedException(string messageAr, IReadOnlyList<CorrespondenceValidationIssue>? issues = null)
    : Exception(messageAr)
{
    /// <summary>The Arabic sentence to show, with no technical term and no error code.</summary>
    public string MessageAr { get; } = messageAr;

    /// <summary>
    /// The failed field checks, when the refusal came from the field validation, so a form can
    /// mark each field instead of showing one sentence at the top. Empty otherwise.
    /// </summary>
    public IReadOnlyList<CorrespondenceValidationIssue> Issues { get; } = issues ?? [];
}

/// <summary>One failed field check, ready to show beside its own field.</summary>
/// <param name="Field">Stable field key (English identifier) the screen binds to.</param>
/// <param name="MessageAr">What to show the user.</param>
public sealed record CorrespondenceValidationIssue(string Field, string MessageAr);

/// <summary>Field keys used by <see cref="CorrespondenceValidationIssue"/> and by corrections.</summary>
public static class CorrespondenceFields
{
    public const string Subject = "subject";
    public const string Type = "type";
    public const string Confidentiality = "confidentiality";
    public const string Party = "party";
    public const string Unit = "unit";
    public const string ExternalNumber = "externalNumber";
    public const string ExternalDate = "externalDate";
    public const string DueAt = "dueAt";
    public const string NextStep = "nextStep";
    public const string BodyText = "bodyText";
    public const string Cc = "cc";
    public const string PartyNameSnapshot = "partyNameSnapshot";
}

/// <summary>The data a draft is created or updated from (the W13 form and the W15 wizard).</summary>
public sealed record CorrespondenceDraftInput
{
    public required InOutDirection Direction { get; init; }

    public required string Subject { get; init; }

    public string? Type { get; init; }

    public Confidentiality Confidentiality { get; init; } = Confidentiality.Public;

    /// <summary>AGREEMENT item 7 — never leaves the office except towards its recipient.</summary>
    public bool RecipientOnly { get; init; }

    public CounterpartyKind CounterpartyKind { get; init; } = CounterpartyKind.External;

    public Guid? PartyId { get; init; }

    public Guid? UnitId { get; init; }

    /// <summary>Free-text recipient name used when the counterparty is not in the directory yet.</summary>
    public string? PartyNameSnapshot { get; init; }

    /// <summary>The number the other party wrote on an incoming letter.</summary>
    public string? ExternalNumber { get; init; }

    public DateTime? ExternalDate { get; init; }

    /// <summary>JSON array of CC recipients, as stored.</summary>
    public string? Cc { get; init; }

    public string? NextStepAr { get; init; }

    public DateTime? DueAt { get; init; }

    public Guid? TemplateId { get; init; }

    public string? BodyText { get; init; }

    public Guid? LinkedCorrespondenceId { get; init; }

    public Guid? CaseId { get; init; }

    public Guid? MeetingId { get; init; }
}

/// <summary>The steps of the outgoing wizard (AGREEMENT item 18); the number is issued by the approval.</summary>
public enum OutgoingStep
{
    Data,
    Recipient,
    Template,
    Document,
    Review,
    Approval,
}

/// <summary>One row of the readiness checklist shown on the outgoing review screen.</summary>
/// <param name="Step">Which wizard step this row belongs to.</param>
/// <param name="LabelAr">The row's Arabic label.</param>
/// <param name="Done">Whether the row is satisfied.</param>
/// <param name="HintAr">What to do about it, when it is not.</param>
public sealed record ReadinessItem(OutgoingStep Step, string LabelAr, bool Done, string? HintAr);

/// <summary>The whole readiness checklist plus its one-line summary.</summary>
public sealed record ReadinessChecklist(IReadOnlyList<ReadinessItem> Items, string SummaryAr)
{
    /// <summary>True when every row is satisfied and the approval may be attempted.</summary>
    public bool IsReady => Items.All(item => item.Done);

    /// <summary>The first step still missing something — where the wizard should open.</summary>
    public OutgoingStep CurrentStep =>
        Items.FirstOrDefault(item => !item.Done)?.Step ?? OutgoingStep.Approval;
}

/// <summary>A correspondence item rendered for a screen: raw values plus their Arabic wording.</summary>
public sealed record CorrespondenceView
{
    public required Guid Id { get; init; }

    public required InOutDirection Direction { get; init; }

    /// <summary>«وارد» or «صادر».</summary>
    public required string DirectionAr { get; init; }

    public required CorrespondenceStatus Status { get; init; }

    /// <summary>«مسودة» … «مؤرشف».</summary>
    public required string StatusAr { get; init; }

    public required string Subject { get; init; }

    public string? OfficialNumber { get; init; }

    public DateTime? NumberIssuedAt { get; init; }

    public string? ExternalNumber { get; init; }

    public DateTime? ExternalDate { get; init; }

    public string? Type { get; init; }

    public required Confidentiality Confidentiality { get; init; }

    public required string ConfidentialityAr { get; init; }

    public bool RecipientOnly { get; init; }

    /// <summary>«للمستلم فقط» when <see cref="RecipientOnly"/>, otherwise <c>null</c>.</summary>
    public string? RecipientOnlyAr { get; init; }

    public required CounterpartyKind CounterpartyKind { get; init; }

    public required string CounterpartyKindAr { get; init; }

    public Guid? PartyId { get; init; }

    public Guid? UnitId { get; init; }

    public string? PartyNameAr { get; init; }

    public string? Cc { get; init; }

    public string? NextStepAr { get; init; }

    public DateTime? DueAt { get; init; }

    /// <summary>The due date as «قبل 3 أيام» / «خلال يومين»; <c>null</c> when there is no due date.</summary>
    public string? DueAr { get; init; }

    public DateTime? ApprovedAt { get; init; }

    public string? CancelReasonAr { get; init; }

    public string? CloseNoteAr { get; init; }

    public DateTime? ArchivedAt { get; init; }

    public Guid? LinkedCorrespondenceId { get; init; }

    public Guid? CaseId { get; init; }

    public Guid? MeetingId { get; init; }

    public Guid? TemplateId { get; init; }

    public string? BodyText { get; init; }

    /// <summary>
    /// True once the item carries an official number: its data may then change only through a
    /// recorded correction (AGREEMENT item 19), and its document is frozen.
    /// </summary>
    public bool IsFrozen { get; init; }

    /// <summary>For an outgoing draft: the wizard step to open on. <c>null</c> once approved.</summary>
    public OutgoingStep? CurrentStep { get; init; }

    public string? CurrentStepAr { get; init; }

    public required DateTime UpdatedAt { get; init; }

    /// <summary>«قبل 10 دقائق» — the last change, for the «آخر حفظ» indicator (AGREEMENT item 32).</summary>
    public required string UpdatedAr { get; init; }
}

/// <summary>What <c>RegisterIncomingAsync</c> produced.</summary>
/// <param name="View">The registered item.</param>
/// <param name="OfficialNumber">The incoming number issued inside the same transaction.</param>
/// <param name="NearLimitWarning">True when the yearly sequence is close to running out.</param>
/// <param name="Suspected">Suspected duplicates written as pending reviews; the screen offers «ليست مكررة».</param>
public sealed record IncomingRegistrationResult(
    CorrespondenceView View,
    string OfficialNumber,
    bool NearLimitWarning,
    IReadOnlyList<DuplicateMatch> Suspected);

/// <summary>What <c>ApproveOutgoingAsync</c> produced.</summary>
/// <param name="View">The approved item, now numbered and frozen.</param>
/// <param name="OfficialNumber">The outgoing number issued inside the same transaction.</param>
/// <param name="NearLimitWarning">True when the yearly sequence is close to running out.</param>
public sealed record ApprovalResult(CorrespondenceView View, string OfficialNumber, bool NearLimitWarning);

/// <summary>The links a correspondence item can carry (W23).</summary>
/// <param name="LinkedCorrespondenceId">Another correspondence item, or <c>null</c> to clear.</param>
/// <param name="CaseId">A case, or <c>null</c> to clear.</param>
/// <param name="MeetingId">A meeting, or <c>null</c> to clear.</param>
public sealed record CorrespondenceLinks(Guid? LinkedCorrespondenceId, Guid? CaseId, Guid? MeetingId);

/// <summary>Filters for the correspondence list (W01).</summary>
public sealed record CorrespondenceQuery
{
    public InOutDirection? Direction { get; init; }

    public IReadOnlyCollection<CorrespondenceStatus>? Statuses { get; init; }

    public Guid? PartyId { get; init; }

    public Guid? UnitId { get; init; }

    /// <summary>Free text matched, after Arabic normalisation, against subject and both numbers.</summary>
    public string? Text { get; init; }

    public bool IncludeArchived { get; init; }

    public int Limit { get; init; } = 200;
}

/// <summary>Maps the correspondence enums to their <see cref="CoreAr"/> wording.</summary>
public static class CorrespondenceAr
{
    public static string Status(CorrespondenceStatus status) => status switch
    {
        CorrespondenceStatus.Draft => CoreAr.CorrStatusDraft,
        CorrespondenceStatus.New => CoreAr.CorrStatusNew,
        CorrespondenceStatus.InProgress => CoreAr.CorrStatusInProgress,
        CorrespondenceStatus.AwaitingReply => CoreAr.CorrStatusAwaitingReply,
        CorrespondenceStatus.Done => CoreAr.CorrStatusDone,
        CorrespondenceStatus.Closed => CoreAr.CorrStatusClosed,
        CorrespondenceStatus.Cancelled => CoreAr.CorrStatusCancelled,
        CorrespondenceStatus.Archived => CoreAr.CorrStatusArchived,
        _ => CoreAr.CorrStatusDraft,
    };

    public static string Direction(InOutDirection direction) =>
        direction == InOutDirection.In ? CoreAr.KindCorrespondenceIn : CoreAr.KindCorrespondenceOut;

    public static string Confidentiality(Data.Confidentiality value) => value switch
    {
        Data.Confidentiality.Public => CoreAr.CorrConfidentialityPublic,
        Data.Confidentiality.Private => CoreAr.CorrConfidentialityPrivate,
        Data.Confidentiality.Secret => CoreAr.CorrConfidentialitySecret,
        Data.Confidentiality.TopSecret => CoreAr.CorrConfidentialityTopSecret,
        _ => CoreAr.CorrConfidentialityPublic,
    };

    public static string Counterparty(CounterpartyKind kind) =>
        kind == CounterpartyKind.Internal ? CoreAr.CorrCounterpartyInternal : CoreAr.CorrCounterpartyExternal;

    public static string Step(OutgoingStep step) => step switch
    {
        OutgoingStep.Data => CoreAr.CorrStepData,
        OutgoingStep.Recipient => CoreAr.CorrStepRecipient,
        OutgoingStep.Template => CoreAr.CorrStepTemplate,
        OutgoingStep.Document => CoreAr.CorrStepDocument,
        OutgoingStep.Review => CoreAr.CorrStepReview,
        OutgoingStep.Approval => CoreAr.CorrStepApproval,
        _ => CoreAr.CorrStepData,
    };

    /// <summary>
    /// The Arabic label of one <see cref="CorrespondenceFields"/> key, for the correction record
    /// and for the refusals that name a field. <c>null</c> for a key this build does not know, so
    /// the caller can fall back to a sentence that names no field at all rather than showing the
    /// English identifier to the user (AGREEMENT items 15 and 55).
    /// </summary>
    public static string? FieldLabel(string? field) => field switch
    {
        CorrespondenceFields.Subject => CoreAr.CorrFieldSubject,
        CorrespondenceFields.Type => CoreAr.CorrFieldType,
        CorrespondenceFields.Confidentiality => CoreAr.CorrFieldConfidentiality,
        CorrespondenceFields.Party => CoreAr.CorrFieldParty,
        CorrespondenceFields.Unit => CoreAr.CorrFieldUnit,
        CorrespondenceFields.ExternalNumber => CoreAr.CorrFieldExternalNumber,
        CorrespondenceFields.ExternalDate => CoreAr.CorrFieldExternalDate,
        CorrespondenceFields.DueAt => CoreAr.CorrFieldDueAt,
        CorrespondenceFields.NextStep => CoreAr.CorrFieldNextStep,
        CorrespondenceFields.BodyText => CoreAr.CorrFieldBodyText,
        CorrespondenceFields.Cc => CoreAr.CorrFieldCc,
        CorrespondenceFields.PartyNameSnapshot => CoreAr.CorrFieldPartyName,
        _ => null,
    };

    public static string Followup(FollowupKind kind) => kind switch
    {
        FollowupKind.Call => CoreAr.CorrFollowupCall,
        FollowupKind.Visit => CoreAr.CorrFollowupVisit,
        FollowupKind.Reply => CoreAr.CorrFollowupReply,
        FollowupKind.Note => CoreAr.CorrFollowupNote,
        FollowupKind.Status => CoreAr.CorrFollowupStatus,
        _ => CoreAr.CorrFollowupNote,
    };

    public static string Referral(ReferralStatus status) => status switch
    {
        ReferralStatus.Open => CoreAr.CorrReferralOpen,
        ReferralStatus.Answered => CoreAr.CorrReferralAnswered,
        ReferralStatus.Overdue => CoreAr.CorrReferralOverdue,
        ReferralStatus.Closed => CoreAr.CorrReferralClosed,
        _ => CoreAr.CorrReferralOpen,
    };

    public static string Verdict(DuplicateVerdict verdict) => verdict switch
    {
        DuplicateVerdict.NotDuplicate => CoreAr.CorrDuplicateNotDuplicate,
        DuplicateVerdict.Duplicate => CoreAr.CorrDuplicateConfirmed,
        _ => CoreAr.CorrDuplicateSimilarSubject,
    };
}

/// <summary>Turns a stored row into its <see cref="CorrespondenceView"/>.</summary>
internal static class CorrespondenceViewFactory
{
    public static CorrespondenceView Create(CorrespondenceRow row, DateTime now, ReadinessChecklist? readiness = null)
    {
        var frozen = row.OfficialNumber is not null;

        // The step is only ever reported when a checklist was actually built for this row: a
        // caller that skipped the checklist (the list screen) gets null rather than a guessed
        // "start at the beginning", which would be wrong for most rows.
        var step = readiness is not null && !frozen
            && row.Direction == InOutDirection.Out && row.Status == CorrespondenceStatus.Draft
            ? readiness.CurrentStep
            : (OutgoingStep?)null;

        return new CorrespondenceView
        {
            Id = row.Id,
            Direction = row.Direction,
            DirectionAr = CorrespondenceAr.Direction(row.Direction),
            Status = row.Status,
            StatusAr = CorrespondenceAr.Status(row.Status),
            Subject = row.Subject,
            OfficialNumber = row.OfficialNumber,
            NumberIssuedAt = row.NumberIssuedAt,
            ExternalNumber = row.ExternalNumber,
            ExternalDate = row.ExternalDate,
            Type = row.Type,
            Confidentiality = row.Confidentiality,
            ConfidentialityAr = CorrespondenceAr.Confidentiality(row.Confidentiality),
            RecipientOnly = row.RecipientOnly,
            RecipientOnlyAr = row.RecipientOnly ? CoreAr.CorrRecipientOnly : null,
            CounterpartyKind = row.CounterpartyKind,
            CounterpartyKindAr = CorrespondenceAr.Counterparty(row.CounterpartyKind),
            PartyId = row.PartyId,
            UnitId = row.UnitId,
            PartyNameAr = row.PartyNameSnapshot,
            Cc = row.Cc,
            NextStepAr = row.NextStepAr,
            DueAt = row.DueAt,
            DueAr = row.DueAt is null ? null : ArabicRelativeTime.Describe(row.DueAt.Value, now),
            ApprovedAt = row.ApprovedAt,
            CancelReasonAr = row.CancelReason,
            CloseNoteAr = row.CloseNote,
            ArchivedAt = row.ArchivedAt,
            LinkedCorrespondenceId = row.LinkedCorrespondenceId,
            CaseId = row.CaseId,
            MeetingId = row.MeetingId,
            TemplateId = row.TemplateId,
            BodyText = row.BodyText,
            IsFrozen = frozen,
            CurrentStep = step,
            CurrentStepAr = step is null ? null : CorrespondenceAr.Step(step.Value),
            UpdatedAt = row.UpdatedAt,
            UpdatedAr = ArabicRelativeTime.Describe(row.UpdatedAt, now),
        };
    }
}
