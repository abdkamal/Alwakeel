using Wakeel.Core.Services;
using Wakeel.Core.Services.Correspondence;
using Wakeel.Reports.Letters;

namespace Wakeel.Core.Tests.Letters;

/// <summary>
/// The print copy that carries the referral (AGREEMENT item 31): under the letter when there is
/// room, on a page of its own when there is not, and the user told which before saving.
/// </summary>
public sealed class DerivedDocumentBuilderTests
{
    private readonly MemoryDocumentStore _documents = new();

    private DerivedDocumentBuilder Builder() => new(_documents);

    private DerivedDocumentRequest Request(Guid? source, string text) => new(
        CorrespondenceId: Guid.NewGuid(),
        ReferralId: Guid.NewGuid(),
        SourceDocumentId: source,
        ReferralTextAr: text,
        ToAr: "مكتب الشؤون المالية",
        DueAt: new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Unspecified));

    [Fact]
    public async Task A_short_referral_goes_under_the_letter_on_its_last_page()
    {
        var letter = new LetterComposer().Compose(LetterWorld.ReferenceTemplate(), LetterWorld.Sample());
        var request = Request(_documents.Add(letter), "للاطلاع والإفادة.");

        var result = await Builder().BuildAsync(request, CancellationToken.None);

        Assert.False(result.ExtraPageAdded);
        Assert.Equal(CoreAr.Letter.ReferralOnLastPage, result.NoticeAr);
        Assert.False(LetterProbe.HasPageBreak(_documents.Content(result.DocumentId)));
    }

    [Fact]
    public async Task A_long_referral_gets_a_page_of_its_own()
    {
        var letter = new LetterComposer().Compose(LetterWorld.ReferenceTemplate(), LetterWorld.Sample());
        var request = Request(
            _documents.Add(letter),
            string.Join(
                "\n",
                Enumerable.Repeat(
                    "يُحال الموضوع إلى مكتب الشؤون المالية لدراسة الأثر المالي المترتب على الترشيح وبيان مدى توفر المخصص في بند التدريب.",
                    30)));

        var result = await Builder().BuildAsync(request, CancellationToken.None);

        Assert.True(result.ExtraPageAdded);
        Assert.Equal(CoreAr.Letter.ReferralOnAddedPage, result.NoticeAr);
        Assert.True(LetterProbe.HasPageBreak(_documents.Content(result.DocumentId)));
    }

    [Fact]
    public async Task The_preview_gives_the_same_answer_without_writing_anything()
    {
        var letter = new LetterComposer().Compose(LetterWorld.ReferenceTemplate(), LetterWorld.Sample());
        var source = _documents.Add(letter);
        var builder = Builder();

        var shortOne = Request(source, "للاطلاع.");
        var longOne = Request(
            source,
            string.Join("\n", Enumerable.Repeat("سطر إحالة طويل يستهلك المساحة المتبقية أسفل الصفحة الأخيرة من الكتاب.", 30)));

        var shortPreview = await builder.PreviewAsync(shortOne, CancellationToken.None);
        var longPreview = await builder.PreviewAsync(longOne, CancellationToken.None);

        Assert.False(shortPreview.ExtraPageAdded);
        Assert.True(longPreview.ExtraPageAdded);
        Assert.Equal(CoreAr.Letter.ReferralOnAddedPage, longPreview.NoticeAr);

        // Nothing was produced: the preview answers before the user presses save.
        Assert.Empty(_documents.Saved);
    }

    [Fact]
    public async Task A_letter_that_already_fills_its_page_pushes_the_referral_over()
    {
        // Thirty-two lines is about as much as an A4 page holds at this size: the letter ends
        // near the foot of its only page, and even a one-line referral no longer fits under it.
        var full = TemplateBuilder.Filled("سطر من نص الكتاب يملأ عرض الصفحة كاملًا ويستهلك سطرًا من ارتفاعها.", 32);
        var request = Request(_documents.Add(full), "للاطلاع والإفادة.");

        var result = await Builder().BuildAsync(request, CancellationToken.None);

        Assert.True(result.ExtraPageAdded);
    }

    [Fact]
    public async Task The_referral_carries_who_it_went_to_and_the_deadline()
    {
        var letter = new LetterComposer().Compose(LetterWorld.ReferenceTemplate(), LetterWorld.Sample());
        var request = Request(_documents.Add(letter), "للدراسة والرفع بالتوصية.");

        var result = await Builder().BuildAsync(request, CancellationToken.None);
        var lines = LetterProbe.Lines(_documents.Content(result.DocumentId));

        Assert.Contains(lines, l => l.Trim() == CoreAr.Letter.ReferralHeading);
        Assert.Contains(lines, l => l.Contains("مكتب الشؤون المالية", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("20 أغسطس 2026", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Trim() == "للدراسة والرفع بالتوصية.");
    }

    [Fact]
    public async Task The_original_document_is_never_touched()
    {
        var letter = new LetterComposer().Compose(LetterWorld.ReferenceTemplate(), LetterWorld.Sample());
        var source = _documents.Add(letter);
        var before = _documents.Content(source).ToArray();

        var result = await Builder().BuildAsync(Request(source, "للاطلاع."), CancellationToken.None);

        Assert.Equal(before, _documents.Content(source));
        Assert.NotEqual(source, result.DocumentId);
        Assert.Equal(DerivedDocumentBuilder.WordMediaType, Assert.Single(_documents.Saved).MediaType);
    }

    [Fact]
    public async Task A_correspondence_with_no_document_gets_a_referral_sheet_of_its_own()
    {
        var result = await Builder().BuildAsync(Request(null, "للاطلاع والإفادة."), CancellationToken.None);

        Assert.True(result.ExtraPageAdded);
        var lines = LetterProbe.Lines(_documents.Content(result.DocumentId));
        Assert.Contains(lines, l => l.Trim() == CoreAr.Letter.ReferralHeading);
        Assert.Contains(lines, l => l.Trim() == "للاطلاع والإفادة.");
    }

    [Fact]
    public async Task A_stored_document_that_is_not_a_word_file_falls_back_to_a_sheet_of_its_own()
    {
        var scan = _documents.Add("ليست وثيقة وورد"u8.ToArray());

        var result = await Builder().BuildAsync(Request(scan, "للاطلاع."), CancellationToken.None);

        Assert.True(result.ExtraPageAdded);
        Assert.Contains(LetterProbe.Lines(_documents.Content(result.DocumentId)), l => l.Trim() == CoreAr.Letter.ReferralHeading);
    }
}
