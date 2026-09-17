using Wakeel.Core.Data;
using Wakeel.Core.Services;
using Wakeel.Core.Services.Correspondence;

namespace Wakeel.Core.Tests;

/// <summary>
/// AGREEMENT item 14 / B3-1 «DuplicateDetector»: an exact match (party number + party) is refused,
/// a suspected one (same number with another party, or a normalised subject at or above the 0.8
/// Jaccard threshold) becomes a review the user settles with «ليست مكررة». The Arabic subjects
/// here are written the way an office actually writes them — with and without diacritics, with
/// ة/ه and ى/ي interchanged, and with tatweel.
/// </summary>
public sealed class DuplicateDetectorTests
{
    [Fact]
    public async Task The_same_number_from_the_same_party_is_refused_and_nothing_is_numbered()
    {
        using var world = new CorrespondenceWorld();
        var partyId = world.AddParty("وزارة المالية");

        await world.RegisterIncomingAsync(world.IncomingInput(externalNumber: "2026/145", partyId: partyId, partyName: null));

        var again = await world.Correspondence.CreateDraftAsync(
            world.IncomingInput(subject: "موضوع مختلف تمامًا", externalNumber: "2026/145", partyId: partyId, partyName: null),
            world.Now);

        var error = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Correspondence.RegisterIncomingAsync(again.Id, world.Now));

        Assert.Equal(CoreAr.CorrDuplicateExact, error.MessageAr);
        Assert.Null(world.Row(again.Id).OfficialNumber);

        // The refused attempt must not have consumed a number.
        Assert.Equal(1, world.Sequence(InOutDirection.In));
    }

    [Fact]
    public async Task The_same_number_from_another_party_is_only_suspected_and_still_registers()
    {
        using var world = new CorrespondenceWorld();
        var finance = world.AddParty("وزارة المالية");
        var municipality = world.AddParty("بلدية المدينة");

        var first = await world.RegisterIncomingAsync(
            world.IncomingInput(subject: "بشأن الموازنة", externalNumber: "2026/145", partyId: finance, partyName: null));

        var second = await world.Correspondence.CreateDraftAsync(
            world.IncomingInput(subject: "بشأن ترخيص البناء", externalNumber: "2026/145", partyId: municipality, partyName: null),
            world.Now);

        var registered = await world.Correspondence.RegisterIncomingAsync(second.Id, world.Now);

        // Two different bodies numbering their own letters 2026/145 is ordinary, not an error.
        Assert.NotNull(registered.OfficialNumber);
        var match = Assert.Single(registered.Suspected);
        Assert.Equal(DuplicateMatchKind.SameNumberOtherParty, match.Kind);
        Assert.Equal(first.Id, match.CorrespondenceId);
        Assert.Equal(CoreAr.CorrDuplicateSameNumberOtherParty, match.ReasonAr);
        Assert.NotNull(match.ReviewId);
    }

    [Fact]
    public async Task A_subject_written_with_diacritics_tatweel_and_the_other_spellings_still_scores_as_a_duplicate()
    {
        using var world = new CorrespondenceWorld();
        var finance = world.AddParty("وزارة المالية");
        var municipality = world.AddParty("بلدية المدينة");

        await world.RegisterIncomingAsync(
            world.IncomingInput(subject: "بشأن صرف مستحقات المقاولين", externalNumber: "1/2026", partyId: finance, partyName: null));

        // Same words, written with tashkeel, a tatweel, «ه» for «ة» and «ى» for «ي».
        var restyled = await world.Correspondence.CreateDraftAsync(
            world.IncomingInput(subject: "بشأن صَرف مستحقـات المقاولىن", externalNumber: "9/2026", partyId: municipality, partyName: null),
            world.Now);

        var registered = await world.Correspondence.RegisterIncomingAsync(restyled.Id, world.Now);

        var match = Assert.Single(registered.Suspected);
        Assert.Equal(DuplicateMatchKind.SimilarSubject, match.Kind);
        Assert.True(match.Score >= DuplicateDetector.SubjectThreshold);
        Assert.Equal(CoreAr.CorrDuplicateSimilarSubject, match.ReasonAr);
    }

    [Fact]
    public async Task A_subject_about_something_else_is_not_suspected()
    {
        using var world = new CorrespondenceWorld();
        var finance = world.AddParty("وزارة المالية");
        var municipality = world.AddParty("بلدية المدينة");

        await world.RegisterIncomingAsync(
            world.IncomingInput(subject: "بشأن صرف مستحقات المقاولين", externalNumber: "1/2026", partyId: finance, partyName: null));

        var other = await world.Correspondence.CreateDraftAsync(
            world.IncomingInput(subject: "دعوة لحضور اجتماع اللجنة الفنية", externalNumber: "9/2026", partyId: municipality, partyName: null),
            world.Now);

        var registered = await world.Correspondence.RegisterIncomingAsync(other.Id, world.Now);
        Assert.Empty(registered.Suspected);
    }

    [Theory]
    // Identical after normalisation: the whole word set matches.
    [InlineData("بشأن الموازنة العامة", "بشأن المُوازَنَةِ العامّة", 1.0)]
    [InlineData("تزويدنا بالبيانات", "تزويــدنا بالبيانات", 1.0)]
    [InlineData("مستحقات المقاولين", "مستحقات المقاولىن", 1.0)]
    // Four shared words out of a five-word union: 4/5 = 0.8, exactly at the threshold.
    [InlineData("بشأن صرف مستحقات المقاولين", "بشأن صرف مستحقات المقاولين اليوم", 0.8)]
    public void The_jaccard_score_is_measured_on_normalised_words(string left, string right, double expected)
    {
        var score = DuplicateDetector.SubjectSimilarity(left, right);
        Assert.Equal(expected, score, 3);
        Assert.True(score >= DuplicateDetector.SubjectThreshold);
    }

    [Theory]
    // Half the words shared is far below the threshold.
    [InlineData("بشأن صرف مستحقات المقاولين", "بشأن اجتماع اللجنة الفنية")]
    // Two empty subjects are unknown, not identical: a blank draft must not suspect every other one.
    [InlineData("", "")]
    [InlineData("   ", "بشأن الموازنة")]
    public void Unrelated_or_empty_subjects_score_below_the_threshold(string left, string right)
    {
        Assert.True(DuplicateDetector.SubjectSimilarity(left, right) < DuplicateDetector.SubjectThreshold);
    }

    [Fact]
    public void Punctuation_does_not_change_the_word_set()
    {
        Assert.Equal(
            DuplicateDetector.Words("بشأن: الموازنة العامة").OrderBy(w => w, StringComparer.Ordinal),
            DuplicateDetector.Words("بشأن الموازنة، العامة").OrderBy(w => w, StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_suspected_match_becomes_a_pending_review_the_user_settles_with_not_duplicate()
    {
        using var world = new CorrespondenceWorld();
        var finance = world.AddParty("وزارة المالية");
        var municipality = world.AddParty("بلدية المدينة");

        await world.RegisterIncomingAsync(
            world.IncomingInput(subject: "بشأن صرف مستحقات المقاولين", externalNumber: "1/2026", partyId: finance, partyName: null));
        var second = await world.Correspondence.CreateDraftAsync(
            world.IncomingInput(subject: "بشأن صرف مستحقات المقاولين", externalNumber: "9/2026", partyId: municipality, partyName: null),
            world.Now);
        var registered = await world.Correspondence.RegisterIncomingAsync(second.Id, world.Now);

        var reviews = await world.Duplicates.GetReviewsAsync(registered.View.Id);
        var review = Assert.Single(reviews);
        Assert.Equal(DuplicateVerdict.Pending, review.Verdict);
        Assert.Equal("بشأن صرف مستحقات المقاولين", review.SimilarSubject);

        Assert.True(await world.Duplicates.MarkNotDuplicateAsync(review.ReviewId));

        var settled = Assert.Single(await world.Duplicates.GetReviewsAsync(registered.View.Id));
        Assert.Equal(DuplicateVerdict.NotDuplicate, settled.Verdict);
        Assert.Equal(CoreAr.CorrDuplicateNotDuplicate, settled.VerdictAr);

        // Settling the review never touches either letter: both keep their number and status.
        Assert.Equal(CorrespondenceStatus.New, world.Row(registered.View.Id).Status);
        Assert.NotNull(world.Row(registered.View.Id).OfficialNumber);
    }

    [Fact]
    public async Task A_cancelled_letter_does_not_block_registering_the_same_one_again()
    {
        using var world = new CorrespondenceWorld();
        var partyId = world.AddParty("وزارة المالية");

        var first = await world.RegisterIncomingAsync(
            world.IncomingInput(externalNumber: "2026/145", partyId: partyId, partyName: null));
        await world.Correspondence.CancelAsync(first.Id, "سُجّل بالخطأ", world.Now);

        var again = await world.Correspondence.CreateDraftAsync(
            world.IncomingInput(externalNumber: "2026/145", partyId: partyId, partyName: null),
            world.Now);

        // The withdrawn letter keeps its number, but it no longer stands in the way of the real
        // registration of the same incoming.
        var registered = await world.Correspondence.RegisterIncomingAsync(again.Id, world.Now);
        Assert.NotNull(registered.OfficialNumber);
        Assert.NotEqual(first.OfficialNumber, registered.OfficialNumber);
    }

    [Fact]
    public async Task An_outgoing_letter_is_never_compared_with_an_incoming_one()
    {
        using var world = new CorrespondenceWorld();

        await world.RegisterIncomingAsync(world.IncomingInput(subject: "بشأن الموازنة العامة"));

        var outgoing = await world.Correspondence.CreateDraftAsync(
            world.OutgoingInput(subject: "بشأن الموازنة العامة"),
            world.Now);
        var scan = await world.Correspondence.ScanForDuplicatesAsync(outgoing.Id);

        Assert.False(scan.IsBlocked);
        Assert.Empty(scan.Suspected);
    }

    [Fact]
    public async Task The_scan_can_be_run_while_typing_without_writing_anything()
    {
        using var world = new CorrespondenceWorld();
        var partyId = world.AddParty("وزارة المالية");
        await world.RegisterIncomingAsync(world.IncomingInput(externalNumber: "2026/145", partyId: partyId, partyName: null));

        var draft = await world.Correspondence.CreateDraftAsync(
            world.IncomingInput(externalNumber: "2026/145", partyId: partyId, partyName: null),
            world.Now);

        var scan = await world.Correspondence.ScanForDuplicatesAsync(draft.Id);

        Assert.True(scan.IsBlocked);
        Assert.Equal(DuplicateMatchKind.Exact, scan.Exact!.Kind);
        Assert.Empty(world.Db.DuplicateReviews);
    }

    [Fact]
    public async Task The_same_letter_from_a_party_typed_by_hand_is_refused_as_an_exact_duplicate()
    {
        using var world = new CorrespondenceWorld();

        // The ordinary data-entry path for a body that is not in the directory yet: no party id,
        // only the name as the clerk typed it.
        await world.RegisterIncomingAsync(world.IncomingInput(externalNumber: "2026/145", partyName: "وزارة المالية"));

        var again = await world.Correspondence.CreateDraftAsync(
            world.IncomingInput(externalNumber: "2026/145", partyName: "وزارة المالية"),
            world.Now);

        var error = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Correspondence.RegisterIncomingAsync(again.Id, world.Now));

        // The reason has to be the truthful one: it is the same party, not another one.
        Assert.Equal(CoreAr.CorrDuplicateExact, error.MessageAr);
        Assert.NotEqual(CoreAr.CorrDuplicateSameNumberOtherParty, error.MessageAr);

        // And one letter has consumed one incoming number, not two.
        Assert.Equal(1, world.Sequence(InOutDirection.In));
    }

    [Fact]
    public async Task A_typed_party_name_spelled_differently_is_still_the_same_party()
    {
        using var world = new CorrespondenceWorld();
        await world.RegisterIncomingAsync(world.IncomingInput(externalNumber: "2026/145", partyName: "وزارة الماليّة"));

        // Same body, written with ه for ة and with tatweel — the normalisation that decides the
        // subject comparison decides the party name too.
        var again = await world.Correspondence.CreateDraftAsync(
            world.IncomingInput(externalNumber: "2026/145", partyName: "وزارة الماليه"),
            world.Now);

        var error = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Correspondence.RegisterIncomingAsync(again.Id, world.Now));

        Assert.Equal(CoreAr.CorrDuplicateExact, error.MessageAr);
        Assert.Equal(1, world.Sequence(InOutDirection.In));
    }

    [Fact]
    public async Task A_typed_party_name_of_another_body_with_the_same_number_stays_a_review()
    {
        using var world = new CorrespondenceWorld();
        var first = await world.RegisterIncomingAsync(world.IncomingInput(externalNumber: "2026/145", partyName: "وزارة المالية"));

        var other = await world.Correspondence.CreateDraftAsync(
            world.IncomingInput(subject: "بشأن صرف المستحقات", externalNumber: "2026/145", partyName: "بلدية المدينة"),
            world.Now);
        var result = await world.Correspondence.RegisterIncomingAsync(other.Id, world.Now);

        // Two different bodies may well write the same number on their own letters: suspected,
        // never blocked.
        var match = Assert.Single(result.Suspected);
        Assert.Equal(DuplicateMatchKind.SameNumberOtherParty, match.Kind);
        Assert.Equal(CoreAr.CorrDuplicateSameNumberOtherParty, match.ReasonAr);
        Assert.Equal(first.Id, match.CorrespondenceId);
        Assert.Equal(2, world.Sequence(InOutDirection.In));
    }
}
