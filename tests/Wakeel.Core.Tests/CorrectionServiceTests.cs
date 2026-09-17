using Wakeel.Core.Data;
using Wakeel.Core.Services;
using Wakeel.Core.Services.Correspondence;

namespace Wakeel.Core.Tests;

/// <summary>
/// AGREEMENT item 19 / B3-1 «CorrectionService»: after numbering, a change is a recorded
/// correction — the old value, the new value and the reason — and both versions stay.
/// </summary>
public sealed class CorrectionServiceTests
{
    [Fact]
    public async Task A_correction_applies_the_new_value_and_keeps_both_versions_with_the_reason()
    {
        using var world = new CorrespondenceWorld();
        var approved = await world.ApproveOutgoingAsync(world.OutgoingInput(subject: "بشأن تزويدنا بالبيانات"));

        var correction = await world.Corrections.ApplyAsync(
            approved.Id,
            [new CorrectionChange(CorrespondenceFields.Subject, "بشأن تزويدنا بالبيانات المالية")],
            "سقط وصف البيانات من الموضوع",
            world.Now);

        Assert.Equal("سقط وصف البيانات من الموضوع", correction.ReasonAr);
        var entry = Assert.Single(correction.Changes);
        Assert.Equal(CorrespondenceFields.Subject, entry.Field);
        Assert.Equal("بشأن تزويدنا بالبيانات", entry.Old);
        Assert.Equal("بشأن تزويدنا بالبيانات المالية", entry.New);

        // The letter now carries the new value; the old one survives in the correction.
        Assert.Equal("بشأن تزويدنا بالبيانات المالية", world.Row(approved.Id).Subject);

        var stored = Assert.Single(await world.Corrections.ListAsync(approved.Id, world.Now));
        Assert.Equal("بشأن تزويدنا بالبيانات", Assert.Single(stored.Changes).Old);
        Assert.Equal("بشأن تزويدنا بالبيانات المالية", Assert.Single(stored.Changes).New);
    }

    [Fact]
    public async Task The_official_number_is_never_touched_by_a_correction()
    {
        using var world = new CorrespondenceWorld();
        var approved = await world.ApproveOutgoingAsync();
        var number = approved.OfficialNumber;

        await world.Corrections.ApplyAsync(
            approved.Id,
            [new CorrectionChange(CorrespondenceFields.Type, "كتاب رسمي")],
            "تصحيح نوع المراسلة",
            world.Now);

        Assert.Equal(number, world.Row(approved.Id).OfficialNumber);
        Assert.Equal(1, world.Sequence(InOutDirection.Out));
    }

    [Fact]
    public async Task Several_fields_are_corrected_together_in_one_record()
    {
        using var world = new CorrespondenceWorld();
        var registered = await world.RegisterIncomingAsync(world.IncomingInput(externalNumber: "2026/145"));

        var correction = await world.Corrections.ApplyAsync(
            registered.Id,
            [
                new CorrectionChange(CorrespondenceFields.ExternalNumber, "2026/154"),
                new CorrectionChange(CorrespondenceFields.Confidentiality, nameof(Confidentiality.Secret)),
                new CorrectionChange(CorrespondenceFields.NextStep, "عرض على المدير"),
            ],
            "أُدخل رقم الجهة مقلوبًا وصُنّف الكتاب خطأً",
            world.Now);

        Assert.Equal(3, correction.Changes.Count);

        var row = world.Row(registered.Id);
        Assert.Equal("2026/154", row.ExternalNumber);
        Assert.Equal(Confidentiality.Secret, row.Confidentiality);
        Assert.Equal("عرض على المدير", row.NextStepAr);

        Assert.Equal("2026/145", correction.Changes.Single(c => c.Field == CorrespondenceFields.ExternalNumber).Old);
        Assert.Equal(nameof(Confidentiality.Public), correction.Changes.Single(c => c.Field == CorrespondenceFields.Confidentiality).Old);
    }

    [Fact]
    public async Task An_unnumbered_draft_is_edited_not_corrected()
    {
        using var world = new CorrespondenceWorld();
        var draft = await world.Correspondence.CreateDraftAsync(world.OutgoingInput(), world.Now);

        var error = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Corrections.ApplyAsync(
                draft.Id,
                [new CorrectionChange(CorrespondenceFields.Subject, "موضوع آخر")],
                "سبب",
                world.Now));

        Assert.Equal(CoreAr.CorrRefusedNotNumbered, error.MessageAr);
        Assert.Empty(await world.Corrections.ListAsync(draft.Id, world.Now));
    }

    [Fact]
    public async Task A_correction_without_a_reason_or_without_a_real_change_is_refused()
    {
        using var world = new CorrespondenceWorld();
        var approved = await world.ApproveOutgoingAsync(world.OutgoingInput(subject: "بشأن الصيانة"));

        var noReason = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Corrections.ApplyAsync(approved.Id, [new CorrectionChange(CorrespondenceFields.Subject, "آخر")], "  ", world.Now));
        Assert.Equal(CoreAr.CorrRefusedCorrectionReason, noReason.MessageAr);

        // Every field of the correction form is posted together; the ones that did not change
        // must not be recorded as corrections.
        var noChange = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Corrections.ApplyAsync(approved.Id, [new CorrectionChange(CorrespondenceFields.Subject, "بشأن الصيانة")], "سبب", world.Now));
        Assert.Equal(CoreAr.CorrRefusedCorrectionEmpty, noChange.MessageAr);

        Assert.Empty(await world.Corrections.ListAsync(approved.Id, world.Now));
        Assert.Equal("بشأن الصيانة", world.Row(approved.Id).Subject);
    }

    [Fact]
    public async Task A_field_that_may_not_be_corrected_is_refused_and_nothing_is_written()
    {
        using var world = new CorrespondenceWorld();
        var approved = await world.ApproveOutgoingAsync();

        var error = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Corrections.ApplyAsync(
                approved.Id,
                [new CorrectionChange("officialNumber", "20260916/12999")],
                "محاولة تغيير الرقم",
                world.Now));

        // The refusal never shows the English field key to the user (AGREEMENT items 15 and 55);
        // an unrecognised key has no Arabic label, so the sentence names no field at all.
        Assert.Equal(CoreAr.CorrCorrectionFieldNotCorrectable, error.MessageAr);
        Assert.DoesNotContain("officialNumber", error.MessageAr, StringComparison.Ordinal);
        Assert.Empty(await world.Corrections.ListAsync(approved.Id, world.Now));
        Assert.DoesNotContain("officialNumber", CorrectionService.CorrectableFields);
    }

    [Fact]
    public async Task A_known_field_that_may_not_be_corrected_is_refused_by_its_arabic_label()
    {
        using var world = new CorrespondenceWorld();
        var approved = await world.ApproveOutgoingAsync();

        var error = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Corrections.ApplyAsync(
                approved.Id,
                [new CorrectionChange(CorrespondenceFields.Party, Guid.NewGuid().ToString())],
                "محاولة تغيير الجهة",
                world.Now));

        Assert.Equal(CoreAr.CorrCorrectionFieldUnknown(CoreAr.CorrFieldParty), error.MessageAr);
        Assert.Contains(CoreAr.CorrFieldParty, error.MessageAr, StringComparison.Ordinal);
        Assert.Empty(await world.Corrections.ListAsync(approved.Id, world.Now));
    }

    [Fact]
    public async Task An_unreadable_value_is_refused_with_its_own_sentence_not_the_field_one()
    {
        using var world = new CorrespondenceWorld();
        var approved = await world.ApproveOutgoingAsync();

        var date = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Corrections.ApplyAsync(
                approved.Id,
                [new CorrectionChange(CorrespondenceFields.ExternalDate, "ليس تاريخًا")],
                "تصحيح تاريخ الجهة",
                world.Now));
        Assert.Equal(CoreAr.CorrCorrectionBadDate, date.MessageAr);

        var level = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Corrections.ApplyAsync(
                approved.Id,
                [new CorrectionChange(CorrespondenceFields.Confidentiality, "درجة غير معروفة")],
                "تصحيح درجة السرية",
                world.Now));
        Assert.Equal(CoreAr.CorrCorrectionBadConfidentiality, level.MessageAr);
    }

    [Fact]
    public async Task A_batch_that_fails_part_way_writes_nothing_even_after_a_later_save()
    {
        using var world = new CorrespondenceWorld();
        var approved = await world.ApproveOutgoingAsync(world.OutgoingInput(subject: "بشأن تزويدنا بالبيانات"));

        // W25 posts every field together, so a user who fixes the subject and mistypes the date
        // sends both in one batch. The good field must not reach the tracked row: WakeelDb lives
        // as long as the session, and the next unrelated save would then commit an unrecorded
        // edit to a numbered letter (AGREEMENT item 19).
        await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Corrections.ApplyAsync(
                approved.Id,
                [
                    new CorrectionChange(CorrespondenceFields.Subject, "موضوع جديد"),
                    new CorrectionChange(CorrespondenceFields.ExternalDate, "ليس تاريخًا"),
                ],
                "تصحيح الموضوع والتاريخ",
                world.Now));

        // Something else in the same session saves — a follow-up, a note, anything.
        await world.FollowUps.AddAsync(approved.Id, FollowupKind.Note, "ملاحظة غير ذات صلة", world.Now);

        Assert.Equal("بشأن تزويدنا بالبيانات", world.Row(approved.Id).Subject);
        Assert.Empty(await world.Corrections.ListAsync(approved.Id, world.Now));
    }

    [Fact]
    public async Task The_correction_record_is_read_in_arabic_not_in_enum_names_or_iso_dates()
    {
        using var world = new CorrespondenceWorld();
        var registered = await world.RegisterIncomingAsync(world.IncomingInput());

        var correction = await world.Corrections.ApplyAsync(
            registered.Id,
            [
                new CorrectionChange(CorrespondenceFields.Confidentiality, nameof(Confidentiality.Secret)),
                new CorrectionChange(CorrespondenceFields.DueAt, "2026-09-20T00:00:00Z"),
            ],
            "تصحيح التصنيف والاستحقاق",
            world.Now);

        var level = correction.Changes.Single(c => c.Field == CorrespondenceFields.Confidentiality);
        Assert.Equal(CoreAr.CorrFieldConfidentiality, level.FieldLabelAr);
        Assert.Equal(CoreAr.CorrConfidentialityPublic, level.OldAr);
        Assert.Equal(CoreAr.CorrConfidentialitySecret, level.NewAr);

        // The stored pair is untouched: the record keeps what it always kept.
        Assert.Equal(nameof(Confidentiality.Public), level.Old);
        Assert.Equal(nameof(Confidentiality.Secret), level.New);

        var due = correction.Changes.Single(c => c.Field == CorrespondenceFields.DueAt);
        Assert.Equal(CoreAr.CorrFieldDueAt, due.FieldLabelAr);
        Assert.Equal(ArabicRelativeTime.DateTimeText(new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc)), due.NewAr);
        Assert.DoesNotContain("T", due.NewAr!, StringComparison.Ordinal);
        Assert.Null(due.OldAr);

        // And it reads the same way when it comes back from the database.
        var stored = Assert.Single(await world.Corrections.ListAsync(registered.Id, world.Now));
        Assert.Equal(CoreAr.CorrConfidentialitySecret, stored.Changes.Single(c => c.Field == CorrespondenceFields.Confidentiality).NewAr);
    }

    [Fact]
    public async Task Two_corrections_of_the_same_field_keep_every_version_in_order()
    {
        using var world = new CorrespondenceWorld();
        var approved = await world.ApproveOutgoingAsync(world.OutgoingInput(subject: "الأول"));

        await world.Corrections.ApplyAsync(approved.Id, [new CorrectionChange(CorrespondenceFields.Subject, "الثاني")], "أول تصحيح", world.Now);
        await world.Corrections.ApplyAsync(approved.Id, [new CorrectionChange(CorrespondenceFields.Subject, "الثالث")], "ثاني تصحيح", world.Now.AddMinutes(1));

        var corrections = await world.Corrections.ListAsync(approved.Id, world.Now.AddMinutes(2));
        Assert.Equal(2, corrections.Count);

        // Newest first: the chain reads الأول → الثاني → الثالث.
        Assert.Equal("الثاني", corrections[0].Changes.Single().Old);
        Assert.Equal("الثالث", corrections[0].Changes.Single().New);
        Assert.Equal("الأول", corrections[1].Changes.Single().Old);
        Assert.Equal("الثاني", corrections[1].Changes.Single().New);
        Assert.Equal("الثالث", world.Row(approved.Id).Subject);
    }

    [Fact]
    public async Task The_correction_is_written_to_the_audit_log_with_its_reason()
    {
        using var world = new CorrespondenceWorld();
        var approved = await world.ApproveOutgoingAsync();

        await world.Corrections.ApplyAsync(
            approved.Id,
            [new CorrectionChange(CorrespondenceFields.NextStep, "متابعة بعد أسبوع")],
            "إضافة الخطوة التالية",
            world.Now);

        var entry = Assert.Single(
            world.Db.AuditLog.Where(a => a.Action == CorrectionService.AuditActionCorrected && a.EntityId == approved.Id));
        Assert.Equal(CoreAr.CorrAuditCorrected("إضافة الخطوة التالية"), entry.SummaryAr);
    }
}
