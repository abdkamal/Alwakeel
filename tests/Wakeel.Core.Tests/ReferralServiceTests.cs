using Wakeel.Core.Data;
using Wakeel.Core.Services;
using Wakeel.Core.Services.Correspondence;

namespace Wakeel.Core.Tests;

/// <summary>
/// AGREEMENT item 31 / B3-1 «ReferralService»: a detailed referral to a unit or a person with a
/// deadline, the derived print copy handed to the letter package's
/// <see cref="IDerivedDocumentBuilder"/>, and the rule that the original never changes.
/// </summary>
public sealed class ReferralServiceTests
{
    [Fact]
    public async Task A_referral_records_its_text_target_and_deadline()
    {
        using var world = new CorrespondenceWorld();
        var registered = await world.RegisterIncomingAsync();
        var unitId = world.AddUnit("دائرة الشؤون المالية", "F1");

        var referral = await world.Referrals.CreateAsync(
            registered.Id,
            "يُحال إلى دائرة الشؤون المالية لدراسة الطلب وإبداء الرأي خلال أسبوع.",
            unitId,
            toNameAr: null,
            dueAt: world.Now.AddDays(7),
            world.Now);

        Assert.Equal("يُحال إلى دائرة الشؤون المالية لدراسة الطلب وإبداء الرأي خلال أسبوع.", referral.TextAr);
        Assert.Equal(unitId, referral.ToUnitId);

        // The unit's name is snapshotted, so the tab still reads correctly after a rename.
        Assert.Equal("دائرة الشؤون المالية", referral.ToAr);
        Assert.Equal(ReferralStatus.Open, referral.Status);
        Assert.Equal(CoreAr.CorrReferralOpen, referral.StatusAr);
        Assert.Equal(world.Now.AddDays(7), referral.DueAt);
    }

    [Fact]
    public async Task A_referral_without_a_text_or_without_a_target_is_refused()
    {
        using var world = new CorrespondenceWorld();
        var registered = await world.RegisterIncomingAsync();

        var noText = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Referrals.CreateAsync(registered.Id, "   ", null, "مدير المكتب", null, world.Now));
        Assert.Equal(CoreAr.CorrRefusedReferralText, noText.MessageAr);

        var noTarget = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Referrals.CreateAsync(registered.Id, "نص الإحالة", null, null, null, world.Now));
        Assert.Equal(CoreAr.CorrRefusedReferralTarget, noTarget.MessageAr);

        Assert.Empty(await world.Referrals.ListAsync(registered.Id, world.Now));
    }

    [Fact]
    public async Task An_open_referral_past_its_deadline_reads_as_overdue_without_being_stored_that_way()
    {
        using var world = new CorrespondenceWorld();
        var registered = await world.RegisterIncomingAsync();

        await world.Referrals.CreateAsync(registered.Id, "للدراسة", null, "مدير الدائرة", world.Now.AddDays(2), world.Now);

        Assert.Equal(ReferralStatus.Open, Assert.Single(await world.Referrals.ListAsync(registered.Id, world.Now)).Status);

        var later = await world.Referrals.ListAsync(registered.Id, world.Now.AddDays(3));
        Assert.Equal(ReferralStatus.Overdue, Assert.Single(later).Status);
        Assert.Equal(CoreAr.CorrReferralOverdue, Assert.Single(later).StatusAr);

        // Nothing wrote "overdue" anywhere; it is what the row IS at that instant.
        Assert.Equal(ReferralStatus.Open, world.Db.Referrals.Single(r => r.CorrespondenceId == registered.Id).Status);
    }

    [Fact]
    public async Task An_answered_referral_stops_being_overdue()
    {
        using var world = new CorrespondenceWorld();
        var registered = await world.RegisterIncomingAsync();
        var referral = await world.Referrals.CreateAsync(registered.Id, "للدراسة", null, "مدير الدائرة", world.Now.AddDays(1), world.Now);

        var answered = await world.Referrals.MarkAnsweredAsync(referral.Id, world.Now.AddDays(5));
        Assert.Equal(ReferralStatus.Answered, answered.Status);
        Assert.Equal(CoreAr.CorrReferralAnswered, answered.StatusAr);

        var closed = await world.Referrals.CloseAsync(referral.Id, world.Now.AddDays(6));
        Assert.Equal(ReferralStatus.Closed, closed.Status);
    }

    [Fact]
    public async Task The_derived_print_copy_is_built_and_linked_while_the_original_is_left_alone()
    {
        var builder = new FakeDerivedDocumentBuilder { ExtraPageAdded = true, NoticeAr = CoreAr.CorrReferralExtraPage };
        using var world = new CorrespondenceWorld(derivedDocuments: builder);
        builder.DocumentId = world.AddDocument("نسخة-الطباعة.pdf");

        // The scanned original joins the letter while it is still a draft; the approval freezes it.
        var draft = await world.Correspondence.CreateDraftAsync(world.IncomingInput(), world.Now);
        var originalDocumentId = world.AddDocument();
        await world.Correspondence.LinkDocumentAsync(draft.Id, originalDocumentId, CorrespondenceDocumentKind.Original);
        var registered = (await world.Correspondence.RegisterIncomingAsync(draft.Id, world.Now)).View;
        var before = world.Row(registered.Id);

        var referral = await world.Referrals.CreateAsync(
            registered.Id,
            "يُحال للدراسة مع نص طويل جدًا يحتاج صفحة إضافية.",
            null,
            "دائرة الشؤون القانونية",
            world.Now.AddDays(5),
            world.Now);

        // The builder was asked for the print copy, told which original to start from.
        var request = Assert.Single(builder.Requests);
        Assert.Equal(registered.Id, request.CorrespondenceId);
        Assert.Equal(originalDocumentId, request.SourceDocumentId);
        Assert.Equal("دائرة الشؤون القانونية", request.ToAr);

        Assert.Equal(builder.DocumentId, referral.DerivedDocumentId);
        Assert.True(referral.ExtraPageAdded);

        // The print copy joins the correspondence as an extra document; the original link is
        // untouched and so is every field of the letter itself.
        var links = world.Db.CorrespondenceDocuments.Where(d => d.CorrespondenceId == registered.Id).ToList();
        Assert.Equal(2, links.Count);
        Assert.Contains(links, l => l.DocumentId == originalDocumentId && l.Kind == CorrespondenceDocumentKind.Original);
        Assert.Contains(links, l => l.DocumentId == builder.DocumentId && l.Kind == CorrespondenceDocumentKind.DerivedPrint);

        var after = world.Row(registered.Id);
        Assert.Equal(before.Subject, after.Subject);
        Assert.Equal(before.BodyText, after.BodyText);
        Assert.Equal(before.OfficialNumber, after.OfficialNumber);
        Assert.Equal(before.Status, after.Status);
    }

    [Fact]
    public async Task The_extra_page_warning_can_be_asked_for_before_saving()
    {
        var builder = new FakeDerivedDocumentBuilder { ExtraPageAdded = true, NoticeAr = CoreAr.CorrReferralExtraPage };
        using var world = new CorrespondenceWorld(derivedDocuments: builder);
        var registered = await world.RegisterIncomingAsync();

        var preview = await world.Referrals.PreviewAsync(registered.Id, "نص طويل", "دائرة الشؤون القانونية", null);

        Assert.True(preview.ExtraPageAdded);
        Assert.Equal(CoreAr.CorrReferralExtraPage, preview.NoticeAr);

        // Nothing was written by the preview.
        Assert.Empty(world.Db.Referrals);
    }

    [Fact]
    public async Task Without_a_document_builder_the_referral_is_still_recorded()
    {
        // The letter package is what registers a builder; an installation without Word, or a
        // build before that package lands, must still be able to refer a letter.
        using var world = new CorrespondenceWorld();
        var registered = await world.RegisterIncomingAsync();

        var referral = await world.Referrals.CreateAsync(registered.Id, "للدراسة", null, "مدير الدائرة", null, world.Now);

        Assert.Null(referral.DerivedDocumentId);
        Assert.False(referral.ExtraPageAdded);
        Assert.Single(await world.Referrals.ListAsync(registered.Id, world.Now));

        var preview = await world.Referrals.PreviewAsync(registered.Id, "للدراسة", "مدير الدائرة", null);
        Assert.False(preview.ExtraPageAdded);
        Assert.Null(preview.NoticeAr);
    }

    [Fact]
    public async Task Referring_writes_one_audit_row_naming_the_target()
    {
        using var world = new CorrespondenceWorld();
        var registered = await world.RegisterIncomingAsync();

        await world.Referrals.CreateAsync(registered.Id, "للدراسة", null, "دائرة الشؤون المالية", null, world.Now);

        var entry = Assert.Single(
            world.Db.AuditLog.Where(a => a.Action == ReferralService.AuditActionReferred && a.EntityId == registered.Id));
        Assert.Equal(CoreAr.CorrAuditReferred("دائرة الشؤون المالية"), entry.SummaryAr);
    }

    /// <summary>
    /// Answering and closing a referral are transitions too, and B3-1 asks for an audit row per
    /// transition, so the log tab shows the whole life of the referral and not only its creation.
    /// </summary>
    [Fact]
    public async Task Answering_and_closing_a_referral_each_leave_their_own_audit_row()
    {
        using var world = new CorrespondenceWorld();
        var registered = await world.RegisterIncomingAsync();

        var referral = await world.Referrals.CreateAsync(registered.Id, "للدراسة", null, "دائرة الشؤون المالية", null, world.Now);

        await world.Referrals.MarkAnsweredAsync(referral.Id, world.Now);
        await world.Referrals.CloseAsync(referral.Id, world.Now);

        var summaries = world.Db.AuditLog
            .Where(a => a.Action == ReferralService.AuditActionReferred && a.EntityId == registered.Id)
            .Select(a => a.SummaryAr)
            .ToList();

        Assert.Equal(3, summaries.Count);
        Assert.Contains(CoreAr.CorrAuditReferralStatus("دائرة الشؤون المالية", CoreAr.CorrReferralAnswered), summaries);
        Assert.Contains(CoreAr.CorrAuditReferralStatus("دائرة الشؤون المالية", CoreAr.CorrReferralClosed), summaries);
    }

    /// <summary>
    /// A withdrawn letter (AGREEMENT item 19) and a filed one are out of the office's hands, so a
    /// unit cannot be asked to act on either: the deadline would be one nobody could meet.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_cancelled_or_archived_letter_cannot_be_referred(bool cancelled)
    {
        using var world = new CorrespondenceWorld();
        var registered = await world.RegisterIncomingAsync();

        if (cancelled)
        {
            await world.Correspondence.CancelAsync(registered.Id, "صدر بدلًا عنه كتاب آخر", world.Now);
        }
        else
        {
            await world.Correspondence.ArchiveAsync(registered.Id, world.Now);
        }

        var error = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Referrals.CreateAsync(registered.Id, "للدراسة", null, "دائرة الشؤون المالية", null, world.Now));

        Assert.Equal(CoreAr.CorrRefusedReferralStatus, error.MessageAr);
        Assert.DoesNotContain(error.MessageAr, c => char.IsAsciiLetter(c));
        Assert.Empty(await world.Referrals.ListAsync(registered.Id, world.Now));
    }

    [Theory]
    [InlineData(-60, ReferralStatus.Open)]
    [InlineData(60, ReferralStatus.Overdue)]
    public async Task The_deadline_is_read_in_utc_whatever_clock_the_screen_hands_over(int minutesFromDeadline, ReferralStatus expected)
    {
        using var world = new CorrespondenceWorld();
        var registered = await world.RegisterIncomingAsync();

        var deadline = world.Now.AddDays(2);
        await world.Referrals.CreateAsync(registered.Id, "للدراسة", null, "مدير الدائرة", deadline, world.Now);

        // The screens hand over the local wall clock while the deadline is stored in UTC:
        // comparing the two raw would move the «متأخرة» boundary by the machine's offset.
        var localNow = deadline.AddMinutes(minutesFromDeadline).ToLocalTime();
        Assert.Equal(DateTimeKind.Local, localNow.Kind);

        var view = Assert.Single(await world.Referrals.ListAsync(registered.Id, localNow));
        Assert.Equal(expected, view.Status);
    }

    /// <summary>Stands in for the letter package's OpenXML builder.</summary>
    private sealed class FakeDerivedDocumentBuilder : IDerivedDocumentBuilder
    {
        public Guid DocumentId { get; set; } = Guid.CreateVersion7();

        public bool ExtraPageAdded { get; init; }

        public string? NoticeAr { get; init; }

        public List<DerivedDocumentRequest> Requests { get; } = [];

        public Task<DerivedDocumentResult> BuildAsync(DerivedDocumentRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new DerivedDocumentResult(DocumentId, ExtraPageAdded, NoticeAr));
        }

        public Task<DerivedDocumentPreview> PreviewAsync(DerivedDocumentRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new DerivedDocumentPreview(ExtraPageAdded, NoticeAr));
        }
    }
}
