using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using Wakeel.Core.Services;
using Wakeel.Core.Services.Correspondence;

namespace Wakeel.Core.Tests;

/// <summary>
/// B3-1 «CorrespondenceService»: drafts and their field checks, registering an incoming letter,
/// the outgoing readiness checklist and approval, the rule that a number is issued only at
/// registration or approval and only inside the transaction, deletion of an unnumbered draft,
/// cancellation keeping the number, manual closing, archiving and «للمستلم فقط».
/// </summary>
public sealed class CorrespondenceServiceTests
{
    [Fact]
    public async Task A_draft_is_saved_unnumbered_and_reports_its_first_missing_step()
    {
        using var world = new CorrespondenceWorld();

        var draft = await world.Correspondence.CreateDraftAsync(
            new CorrespondenceDraftInput { Direction = InOutDirection.Out, Subject = "بشأن الصيانة" },
            world.Now);

        Assert.Equal(CorrespondenceStatus.Draft, draft.Status);
        Assert.Equal(CoreAr.CorrStatusDraft, draft.StatusAr);
        Assert.Null(draft.OfficialNumber);
        Assert.False(draft.IsFrozen);

        // The subject is written, so the wizard opens on the first thing that is not.
        Assert.Equal(OutgoingStep.Recipient, draft.CurrentStep);
        Assert.Equal(CoreAr.CorrStepRecipient, draft.CurrentStepAr);
    }

    [Fact]
    public async Task A_draft_without_a_subject_is_refused_in_arabic()
    {
        using var world = new CorrespondenceWorld();

        var error = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Correspondence.CreateDraftAsync(
                new CorrespondenceDraftInput { Direction = InOutDirection.In, Subject = "   " },
                world.Now));

        Assert.Equal(CoreAr.CorrValidationSubjectRequired, error.MessageAr);
        Assert.Equal(CorrespondenceFields.Subject, Assert.Single(error.Issues).Field);
    }

    [Fact]
    public void The_field_check_names_every_missing_incoming_field()
    {
        using var world = new CorrespondenceWorld();

        var issues = world.Correspondence.Validate(
            new CorrespondenceDraftInput { Direction = InOutDirection.In, Subject = "موضوع" },
            world.Now);

        Assert.Contains(issues, i => i.Field == CorrespondenceFields.ExternalNumber);
        Assert.Contains(issues, i => i.Field == CorrespondenceFields.ExternalDate);
        Assert.Contains(issues, i => i.MessageAr == CoreAr.CorrValidationRecipientRequired);
    }

    [Fact]
    public void A_party_date_in_the_future_and_a_due_date_in_the_past_are_both_refused()
    {
        using var world = new CorrespondenceWorld();

        var issues = world.Correspondence.Validate(
            world.IncomingInput() with
            {
                ExternalDate = world.Now.AddDays(3),
                DueAt = world.Now.AddDays(-3),
            },
            world.Now);

        Assert.Contains(issues, i => i.MessageAr == CoreAr.CorrValidationExternalDateFuture);
        Assert.Contains(issues, i => i.MessageAr == CoreAr.CorrValidationDueBeforeToday);
    }

    [Fact]
    public async Task Registering_an_incoming_letter_issues_the_incoming_number_and_snapshots_the_party_name()
    {
        using var world = new CorrespondenceWorld();
        var partyId = world.AddParty("وزارة المالية");

        var draft = await world.Correspondence.CreateDraftAsync(
            world.IncomingInput(partyId: partyId, partyName: null),
            world.Now);
        Assert.Null(draft.OfficialNumber);

        var result = await world.Correspondence.RegisterIncomingAsync(draft.Id, world.Now);

        Assert.Equal(CorrespondenceStatus.New, result.View.Status);
        Assert.Equal("20260916/12001", result.OfficialNumber);
        Assert.Equal(result.OfficialNumber, result.View.OfficialNumber);
        Assert.True(result.View.IsFrozen);
        Assert.False(result.NearLimitWarning);

        // The name is copied at the instant the number is issued, so renaming the directory entry
        // afterwards never rewrites what the letter said that day.
        Assert.Equal("وزارة المالية", world.Row(draft.Id).PartyNameSnapshot);

        var party = world.Db.Parties.Single(p => p.Id == partyId);
        party.Name = "وزارة المالية والتخطيط";
        world.Db.SaveChanges();
        Assert.Equal("وزارة المالية", world.Row(draft.Id).PartyNameSnapshot);
    }

    [Fact]
    public async Task Incoming_and_outgoing_numbers_run_on_separate_sequences()
    {
        using var world = new CorrespondenceWorld();

        var incoming = await world.RegisterIncomingAsync();
        var outgoing = await world.ApproveOutgoingAsync();

        Assert.Equal("20260916/12001", incoming.OfficialNumber);
        Assert.Equal("20260916/12001", outgoing.OfficialNumber);
        Assert.Equal(1, world.Sequence(InOutDirection.In));
        Assert.Equal(1, world.Sequence(InOutDirection.Out));
    }

    [Fact]
    public async Task An_outgoing_letter_is_not_numbered_before_its_approval()
    {
        using var world = new CorrespondenceWorld();

        var draft = await world.Correspondence.CreateDraftAsync(world.OutgoingInput(), world.Now);
        Assert.Null(world.Row(draft.Id).OfficialNumber);
        Assert.Equal(0, world.Sequence(InOutDirection.Out));

        var approved = await world.Correspondence.ApproveOutgoingAsync(draft.Id, world.Now);

        Assert.Equal(CorrespondenceStatus.New, approved.View.Status);
        Assert.NotNull(approved.View.OfficialNumber);
        Assert.Equal(world.Now, world.Row(draft.Id).ApprovedAt);
        Assert.Equal(1, world.Sequence(InOutDirection.Out));
    }

    [Fact]
    public async Task The_readiness_checklist_lists_what_is_missing_and_the_approval_waits_for_it()
    {
        using var world = new CorrespondenceWorld();

        var draft = await world.Correspondence.CreateDraftAsync(
            new CorrespondenceDraftInput { Direction = InOutDirection.Out, Subject = "بشأن التزويد" },
            world.Now);

        var checklist = await world.Correspondence.GetReadinessAsync(draft.Id);
        Assert.False(checklist.IsReady);
        Assert.Contains(checklist.Items, i => i.Step == OutgoingStep.Recipient && !i.Done);
        Assert.Contains(checklist.Items, i => i.Step == OutgoingStep.Template && !i.Done);
        Assert.Contains(checklist.Items, i => i.Step == OutgoingStep.Document && !i.Done);
        Assert.Equal(CoreAr.CorrReadyRemaining(3), checklist.SummaryAr);

        var refusal = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Correspondence.ApproveOutgoingAsync(draft.Id, world.Now));
        Assert.Equal(CoreAr.CorrRefusedNotReady, refusal.MessageAr);
        Assert.Equal(0, world.Sequence(InOutDirection.Out));

        // Completing the three missing rows is enough; nothing else is asked for.
        await world.Correspondence.UpdateDraftAsync(draft.Id, world.OutgoingInput(subject: "بشأن التزويد"), world.Now);
        Assert.True((await world.Correspondence.GetReadinessAsync(draft.Id)).IsReady);

        var approved = await world.Correspondence.ApproveOutgoingAsync(draft.Id, world.Now);
        Assert.NotNull(approved.View.OfficialNumber);
    }

    [Fact]
    public async Task An_approved_letter_is_frozen_against_editing_and_against_a_second_approval()
    {
        using var world = new CorrespondenceWorld();
        var approved = await world.ApproveOutgoingAsync();

        var edit = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Correspondence.UpdateDraftAsync(approved.Id, world.OutgoingInput(subject: "موضوع آخر"), world.Now));
        Assert.Equal(CoreAr.CorrRefusedAlreadyNumbered, edit.MessageAr);

        // AGREEMENT item 32: a second click must not consume a second number.
        await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Correspondence.ApproveOutgoingAsync(approved.Id, world.Now));
        Assert.Equal(1, world.Sequence(InOutDirection.Out));

        // The original document is frozen with it; a later attachment is still allowed.
        await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Correspondence.LinkDocumentAsync(approved.Id, Guid.CreateVersion7(), CorrespondenceDocumentKind.Original));
        await world.Correspondence.LinkDocumentAsync(approved.Id, Guid.CreateVersion7(), CorrespondenceDocumentKind.Attachment);
    }

    [Fact]
    public async Task A_failure_after_the_number_was_issued_rolls_the_number_back_with_everything_else()
    {
        // The duplicate detector's RecordSuspectedAsync runs inside the registration transaction,
        // right after the number is issued: a throwing one is exactly "a failure after numbering".
        var detector = new ThrowAfterNumberingDetector();
        using var world = new CorrespondenceWorld(duplicateDetector: detector);

        var draft = await world.Correspondence.CreateDraftAsync(world.IncomingInput(), world.Now);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            world.Correspondence.RegisterIncomingAsync(draft.Id, world.Now));

        // Nothing survived: no number on the row, and the yearly sequence did not move.
        var row = world.Row(draft.Id);
        Assert.Null(row.OfficialNumber);
        Assert.Equal(CorrespondenceStatus.Draft, row.Status);
        Assert.Equal(0, world.Sequence(InOutDirection.In));

        // And the next registration gets 001, not 002 — the rolled-back attempt burned nothing.
        detector.Fail = false;
        var registered = await world.Correspondence.RegisterIncomingAsync(draft.Id, world.Now);
        Assert.Equal("20260916/12001", registered.OfficialNumber);
        Assert.Equal(1, world.Sequence(InOutDirection.In));
    }

    [Fact]
    public async Task An_unnumbered_draft_is_deleted_and_a_numbered_item_is_not()
    {
        using var world = new CorrespondenceWorld();

        var draft = await world.Correspondence.CreateDraftAsync(world.OutgoingInput(), world.Now);
        Assert.True(await world.Correspondence.DeleteDraftAsync(draft.Id, world.Now));

        // Gone from every screen and every service: the soft-delete filter hides it everywhere.
        Assert.Null(await world.Correspondence.GetAsync(draft.Id, world.Now));
        Assert.Empty(await world.Correspondence.ListAsync(new CorrespondenceQuery(), world.Now));
        Assert.False(await world.Correspondence.DeleteDraftAsync(draft.Id, world.Now));

        var approved = await world.ApproveOutgoingAsync();
        var refusal = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Correspondence.DeleteDraftAsync(approved.Id, world.Now));
        Assert.Equal(CoreAr.CorrRefusedDeleteNumbered, refusal.MessageAr);
        Assert.NotNull(await world.Correspondence.GetAsync(approved.Id, world.Now));
    }

    [Fact]
    public async Task Cancelling_a_numbered_item_keeps_its_number_and_records_the_reason()
    {
        using var world = new CorrespondenceWorld();
        var approved = await world.ApproveOutgoingAsync();
        var number = approved.OfficialNumber;

        var cancelled = await world.Correspondence.CancelAsync(approved.Id, "صدر بدلًا عنه كتاب آخر", world.Now);

        Assert.Equal(CorrespondenceStatus.Cancelled, cancelled.Status);
        Assert.Equal(CoreAr.CorrStatusCancelled, cancelled.StatusAr);
        Assert.Equal(number, cancelled.OfficialNumber);
        Assert.Equal("صدر بدلًا عنه كتاب آخر", cancelled.CancelReasonAr);

        // The number stays consumed: the next letter takes the following one, never this one.
        var next = await world.ApproveOutgoingAsync(world.OutgoingInput(subject: "كتاب لاحق"));
        Assert.Equal("20260916/12002", next.OfficialNumber);
        Assert.Equal(number, world.Row(approved.Id).OfficialNumber);
    }

    [Fact]
    public async Task Cancelling_without_a_reason_is_refused()
    {
        using var world = new CorrespondenceWorld();
        var approved = await world.ApproveOutgoingAsync();

        var error = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Correspondence.CancelAsync(approved.Id, "  ", world.Now));
        Assert.Equal(CoreAr.CorrRefusedCancelReason, error.MessageAr);
        Assert.Equal(CorrespondenceStatus.New, world.Row(approved.Id).Status);
    }

    [Fact]
    public async Task Cancelling_an_unnumbered_draft_points_at_deletion_instead()
    {
        using var world = new CorrespondenceWorld();
        var draft = await world.Correspondence.CreateDraftAsync(world.OutgoingInput(), world.Now);

        var error = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Correspondence.CancelAsync(draft.Id, "لم يعد مطلوبًا", world.Now));

        // The refusal must not tell the user the opposite of the truth («لم تعد مسودة») when the
        // item is a draft precisely; it names the command that does apply.
        Assert.Equal(CoreAr.CorrRefusedCancelDraft, error.MessageAr);
        Assert.Equal(CorrespondenceStatus.Draft, world.Row(draft.Id).Status);
        Assert.True(await world.Correspondence.DeleteDraftAsync(draft.Id, world.Now));
    }

    [Fact]
    public async Task Closing_needs_a_note_and_archiving_stamps_the_date()
    {
        using var world = new CorrespondenceWorld();
        var registered = await world.RegisterIncomingAsync();

        var missingNote = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Correspondence.CloseAsync(registered.Id, string.Empty, world.Now));
        Assert.Equal(CoreAr.CorrRefusedCloseNote, missingNote.MessageAr);

        var closed = await world.Correspondence.CloseAsync(registered.Id, "أُنجز المطلوب وأُبلغت الجهة", world.Now);
        Assert.Equal(CorrespondenceStatus.Closed, closed.Status);
        Assert.Equal("أُنجز المطلوب وأُبلغت الجهة", closed.CloseNoteAr);

        var archived = await world.Correspondence.ArchiveAsync(registered.Id, world.Now);
        Assert.Equal(CorrespondenceStatus.Archived, archived.Status);
        Assert.Equal(world.Now, archived.ArchivedAt);
    }

    [Fact]
    public async Task Every_transition_writes_one_audit_row()
    {
        using var world = new CorrespondenceWorld();
        var registered = await world.RegisterIncomingAsync();

        await world.Correspondence.ChangeStatusAsync(registered.Id, CorrespondenceStatus.InProgress, world.Now);
        await world.Correspondence.ChangeStatusAsync(registered.Id, CorrespondenceStatus.AwaitingReply, world.Now);
        await world.Correspondence.CloseAsync(registered.Id, "وصل الرد", world.Now);
        await world.Correspondence.ArchiveAsync(registered.Id, world.Now);

        var log = await world.Db.AuditLog.AsNoTracking()
            .Where(a => a.EntityType == CorrespondenceService.AuditEntityType && a.EntityId == registered.Id)
            .ToListAsync();

        // Six actions, one per operation — the two status moves, the close and the archive, plus
        // the draft and the registration that preceded them. Counted rather than ordered: every
        // row carries the same test clock, so there is no order to assert on.
        Assert.Equal(6, log.Count);
        Assert.Equal(1, log.Count(a => a.Action == CorrespondenceService.AuditActionDraftCreated));
        Assert.Equal(1, log.Count(a => a.Action == CorrespondenceService.AuditActionRegistered));
        Assert.Equal(2, log.Count(a => a.Action == CorrespondenceService.AuditActionTransition));
        Assert.Equal(1, log.Count(a => a.Action == CorrespondenceService.AuditActionClosed));
        Assert.Equal(1, log.Count(a => a.Action == CorrespondenceService.AuditActionArchived));

        // The audit summary is a finished Arabic sentence, not a code.
        Assert.All(log, entry => Assert.False(string.IsNullOrWhiteSpace(entry.SummaryAr)));
        Assert.All(log, entry => Assert.Equal(world.Installation.EmployeeName, entry.Actor));
    }

    [Fact]
    public async Task A_refused_transition_changes_nothing()
    {
        using var world = new CorrespondenceWorld();
        var registered = await world.RegisterIncomingAsync();
        await world.Correspondence.ArchiveAsync(registered.Id, world.Now);

        await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Correspondence.ChangeStatusAsync(registered.Id, CorrespondenceStatus.InProgress, world.Now));

        Assert.Equal(CorrespondenceStatus.Archived, world.Row(registered.Id).Status);
    }

    [Fact]
    public async Task Recipient_only_can_be_set_and_cleared_even_after_numbering()
    {
        using var world = new CorrespondenceWorld();
        var approved = await world.ApproveOutgoingAsync();
        Assert.False(approved.RecipientOnly);
        Assert.Null(approved.RecipientOnlyAr);

        var marked = await world.Correspondence.SetRecipientOnlyAsync(approved.Id, true, world.Now);
        Assert.True(marked.RecipientOnly);
        Assert.Equal(CoreAr.CorrRecipientOnly, marked.RecipientOnlyAr);
        Assert.True(world.Row(approved.Id).RecipientOnly);

        var cleared = await world.Correspondence.SetRecipientOnlyAsync(approved.Id, false, world.Now);
        Assert.False(cleared.RecipientOnly);
    }

    [Fact]
    public async Task Setting_and_lifting_recipient_only_is_written_to_the_audit_log()
    {
        using var world = new CorrespondenceWorld();
        var approved = await world.ApproveOutgoingAsync();

        await world.Correspondence.SetRecipientOnlyAsync(approved.Id, true, world.Now);
        await world.Correspondence.SetRecipientOnlyAsync(approved.Id, true, world.Now);
        await world.Correspondence.SetRecipientOnlyAsync(approved.Id, false, world.Now);

        var log = await world.Db.AuditLog.AsNoTracking()
            .Where(a => a.EntityId == approved.Id && a.Action == CorrespondenceService.AuditActionRecipientOnly)
            .ToListAsync();

        // «للمستلم فقط» decides whether the letter leaves this installation at all (AGREEMENT
        // item 7), so both the setting and the lifting leave a trace — and re-sending the same
        // value, which the form does on every save, is not an act and writes nothing.
        Assert.Equal(2, log.Count);
        Assert.Equal(1, log.Count(a => a.SummaryAr == CoreAr.CorrAuditRecipientOnlySet));
        Assert.Equal(1, log.Count(a => a.SummaryAr == CoreAr.CorrAuditRecipientOnlyCleared));
    }

    [Fact]
    public async Task Changing_the_links_is_written_to_the_audit_log()
    {
        using var world = new CorrespondenceWorld();
        var first = await world.RegisterIncomingAsync();
        var second = await world.ApproveOutgoingAsync();

        await world.Correspondence.SetLinksAsync(second.Id, new CorrespondenceLinks(first.Id, null, null), world.Now);
        await world.Correspondence.SetLinksAsync(second.Id, new CorrespondenceLinks(first.Id, null, null), world.Now);

        var log = await world.Db.AuditLog.AsNoTracking()
            .Where(a => a.EntityId == second.Id && a.Action == CorrespondenceService.AuditActionLinksChanged)
            .ToListAsync();

        var entry = Assert.Single(log);
        Assert.Equal(CoreAr.CorrAuditLinksChanged, entry.SummaryAr);
    }

    [Fact]
    public async Task Links_to_another_correspondence_a_case_and_a_meeting_are_stored_and_self_links_refused()
    {
        using var world = new CorrespondenceWorld();
        var first = await world.RegisterIncomingAsync();
        var second = await world.ApproveOutgoingAsync();
        var caseId = Guid.CreateVersion7();
        var meetingId = Guid.CreateVersion7();

        var linked = await world.Correspondence.SetLinksAsync(
            second.Id,
            new CorrespondenceLinks(first.Id, caseId, meetingId),
            world.Now);

        Assert.Equal(first.Id, linked.LinkedCorrespondenceId);
        Assert.Equal(caseId, linked.CaseId);
        Assert.Equal(meetingId, linked.MeetingId);

        var error = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Correspondence.SetLinksAsync(second.Id, new CorrespondenceLinks(second.Id, null, null), world.Now));
        Assert.Equal(CoreAr.CorrRefusedLinkSelf, error.MessageAr);
    }

    [Fact]
    public async Task The_list_filters_by_direction_and_finds_an_arabic_subject_however_it_is_spelled()
    {
        using var world = new CorrespondenceWorld();
        await world.RegisterIncomingAsync(world.IncomingInput(subject: "بشأن المُوازنَة العامّة", externalNumber: "1/1"));
        await world.ApproveOutgoingAsync(world.OutgoingInput(subject: "بشأن الصيانة"));

        var incoming = await world.Correspondence.ListAsync(new CorrespondenceQuery { Direction = InOutDirection.In }, world.Now);
        Assert.Equal(CoreAr.KindCorrespondenceIn, Assert.Single(incoming).DirectionAr);

        // Typed without the diacritics and with the plain «ه» the letter was written with «ة».
        var found = await world.Correspondence.ListAsync(new CorrespondenceQuery { Text = "الموازنه" }, world.Now);
        Assert.Single(found);
    }

    [Fact]
    public async Task The_search_box_reaches_past_the_page_it_returns()
    {
        using var world = new CorrespondenceWorld();
        var query = new CorrespondenceQuery();

        // The letter being looked for is the OLDEST one, so the newest page of the list cannot
        // contain it: it is written first, with the clock a month back.
        var later = world.Clock.UtcNow;
        world.Clock.UtcNow = later.AddDays(-30);
        world.Db.Correspondence.Add(new Wakeel.Core.Data.Entities.Correspondence
        {
            Direction = InOutDirection.In,
            Subject = "بشأن صيانة مبنى الدائرة",
            Status = CorrespondenceStatus.New,
        });
        world.Db.SaveChanges();

        world.Clock.UtcNow = later;
        for (var i = 0; i < query.Limit + 50; i++)
        {
            world.Db.Correspondence.Add(new Wakeel.Core.Data.Entities.Correspondence
            {
                Direction = InOutDirection.In,
                Subject = $"بشأن متابعة أخرى {i}",
                Status = CorrespondenceStatus.New,
            });
        }

        world.Db.SaveChanges();

        // Without a term the list is one page of the newest rows, and the wanted letter is not in
        // it.
        var page = await world.Correspondence.ListAsync(query, world.Now);
        Assert.Equal(query.Limit, page.Count);
        Assert.DoesNotContain(page, v => v.Subject == "بشأن صيانة مبنى الدائرة");

        // Typed with «ه» for «ة» and «ي» for «ى», it is still found.
        var found = await world.Correspondence.ListAsync(query with { Text = "صيانه مبني" }, world.Now);
        Assert.Equal("بشأن صيانة مبنى الدائرة", Assert.Single(found).Subject);
    }

    [Fact]
    public async Task An_outgoing_item_cannot_be_registered_as_incoming_and_the_other_way_round()
    {
        using var world = new CorrespondenceWorld();

        var outgoing = await world.Correspondence.CreateDraftAsync(world.OutgoingInput(), world.Now);
        var asIncoming = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Correspondence.RegisterIncomingAsync(outgoing.Id, world.Now));
        Assert.Equal(CoreAr.CorrRefusedDirectionIn, asIncoming.MessageAr);

        var incoming = await world.Correspondence.CreateDraftAsync(world.IncomingInput(), world.Now);
        var asOutgoing = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Correspondence.ApproveOutgoingAsync(incoming.Id, world.Now));
        Assert.Equal(CoreAr.CorrRefusedDirectionOut, asOutgoing.MessageAr);

        Assert.Equal(0, world.Sequence(InOutDirection.In));
        Assert.Equal(0, world.Sequence(InOutDirection.Out));
    }

    /// <summary>
    /// A <c>WakeelDb</c> is scoped to one user session and every screen of that session shares it,
    /// so a failed registration must put back only what it touched. Work another form is holding
    /// in memory — a renamed party, a party it has only just added — is none of its business.
    /// </summary>
    [Fact]
    public async Task A_failed_registration_leaves_another_screens_unsaved_work_alone()
    {
        var detector = new ThrowAfterNumberingDetector();
        using var world = new CorrespondenceWorld(duplicateDetector: detector);

        var partyId = world.AddParty("وزارة المالية");
        var draft = await world.Correspondence.CreateDraftAsync(world.IncomingInput(partyId: partyId), world.Now);

        // Another screen of the same session: one party renamed but not saved, one newly added.
        var tracked = world.Db.Parties.Single(p => p.Id == partyId);
        tracked.Name = "وزارة المالية — الدائرة القانونية";
        var pending = new Party { Name = "بلدية المدينة", Kind = PartyKind.Municipality };
        world.Db.Parties.Add(pending);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            world.Correspondence.RegisterIncomingAsync(draft.Id, world.Now));

        // The registration rolled itself back …
        Assert.Null(world.Row(draft.Id).OfficialNumber);
        Assert.Equal(0, world.Sequence(InOutDirection.In));

        // … and the other screen still holds exactly what it had typed, instead of being reloaded
        // back to the stored name or having its new row thrown out of the tracker altogether.
        Assert.Equal("وزارة المالية — الدائرة القانونية", tracked.Name);
        Assert.NotEqual(EntityState.Detached, world.Db.Entry(pending).State);
        Assert.Equal("بلدية المدينة", pending.Name);
    }

    /// <summary>
    /// The owner's right-to-left requirement, applied where the sentence is COMPOSED AND STORED:
    /// an official number or a file name sitting inside an Arabic audit line has to carry its own
    /// direction with it, because no screen can wrap it in a <c>bdi</c> element after the fact.
    /// </summary>
    [Fact]
    public async Task Stored_audit_lines_isolate_the_number_and_the_file_name_they_embed()
    {
        const char FirstStrongIsolate = '⁨';
        const char PopDirectionalIsolate = '⁩';

        using var world = new CorrespondenceWorld();
        var registered = await world.RegisterIncomingAsync();
        await world.Correspondence.CancelAsync(registered.Id, "صدر بدلًا عنه كتاب آخر", world.Now);

        var summaries = world.Db.AuditLog
            .Where(a => a.EntityId == registered.Id)
            .Select(a => a.SummaryAr)
            .ToList();

        var number = registered.OfficialNumber!;
        var registeredLine = Assert.Single(summaries, s => s == CoreAr.CorrAuditRegistered(number));
        Assert.Contains($"{FirstStrongIsolate}{number}{PopDirectionalIsolate}", registeredLine, StringComparison.Ordinal);

        var cancelledLine = Assert.Single(summaries, s => s.StartsWith("أُلغيت المراسلة", StringComparison.Ordinal));
        Assert.Contains($"{FirstStrongIsolate}{number}{PopDirectionalIsolate}", cancelledLine, StringComparison.Ordinal);

        // The pair is invisible: stripping it gives back the plain sentence the reader sees.
        Assert.Equal(
            "سُجّل وارد برقم " + number,
            registeredLine
                .Replace(FirstStrongIsolate.ToString(), string.Empty, StringComparison.Ordinal)
                .Replace(PopDirectionalIsolate.ToString(), string.Empty, StringComparison.Ordinal));

        // Every composed audit sentence of this package isolates what it embeds …
        Assert.Contains(FirstStrongIsolate, CoreAr.CorrAuditApproved("20260916/22001"));
        Assert.Contains(FirstStrongIsolate, CoreAr.CorrAuditExported("20260916-12001.wakeel-msg"));
        Assert.Contains(FirstStrongIsolate, CoreAr.CorrAuditImported("20260916/12001"));
        Assert.Contains(PopDirectionalIsolate, CoreAr.CorrAuditImported("20260916/12001"));

        // … and so do the health sentences that embed a size, a version or a date.
        Assert.Contains(FirstStrongIsolate, CoreAr.HealthVersionOk("0.21.0", "2026-09-16"));
        Assert.Contains(FirstStrongIsolate, CoreAr.HealthDatabaseOk(CoreAr.Size(1024 * 1024)));
    }

    /// <summary>
    /// A duplicate detector that finds a suspected match and then fails while recording it —
    /// i.e. inside the registration transaction, after the official number has been issued.
    /// </summary>
    private sealed class ThrowAfterNumberingDetector : IDuplicateDetector
    {
        public bool Fail { get; set; } = true;

        public Task<DuplicateScan> ScanAsync(DuplicateCandidate candidate, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DuplicateScan(
                null,
                [new DuplicateMatch(Guid.CreateVersion7(), DuplicateMatchKind.SimilarSubject, 0.9, "مشابه", "موضوع", null, null, null, null)]));

        public Task<IReadOnlyList<DuplicateMatch>> RecordSuspectedAsync(
            Guid correspondenceId,
            IReadOnlyList<DuplicateMatch> suspected,
            CancellationToken cancellationToken = default) =>
            Fail
                ? throw new InvalidOperationException("the review could not be written")
                : Task.FromResult<IReadOnlyList<DuplicateMatch>>([]);

        public Task<IReadOnlyList<DuplicateReviewView>> GetReviewsAsync(Guid correspondenceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DuplicateReviewView>>([]);

        public Task<bool> MarkNotDuplicateAsync(Guid reviewId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> MarkDuplicateAsync(Guid reviewId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
