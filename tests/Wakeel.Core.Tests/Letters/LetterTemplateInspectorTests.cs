using Wakeel.Core.Services.Correspondence;
using Wakeel.Reports.Letters;

namespace Wakeel.Core.Tests.Letters;

/// <summary>
/// The check run on a template when it is uploaded (AGREEMENT item 57): what الوكيل will fill,
/// what it does not recognise, and what the template leaves out.
/// </summary>
public sealed class LetterTemplateInspectorTests
{
    private readonly LetterTemplateInspector _inspector = new();

    [Fact]
    public void The_reference_template_uses_all_nine_marks_and_nothing_else()
    {
        var check = _inspector.Check(LetterWorld.ReferenceTemplate());

        Assert.True(check.IsReadable);
        Assert.Empty(check.Unknown);
        Assert.Empty(check.Missing);
        Assert.True(check.CanCompose);
        Assert.Equal(
            LetterMarks.All.OrderBy(m => m, StringComparer.Ordinal),
            check.Known.Select(k => k.Name).OrderBy(m => m, StringComparer.Ordinal));
    }

    [Fact]
    public void A_misspelt_mark_is_reported_as_it_stands()
    {
        // «@اسم الموضع» is one letter away from «@اسم الموضوع»; without this warning it would be
        // found on the first printed letter.
        var template = TemplateBuilder.WithLines(
            "الموضوع / @اسم الموضع",
            "بالإشارة إلى الموضوع أعلاه، @نص المراسلة",
            "@اسم المرسل");

        var check = _inspector.Check(template);

        var unknown = Assert.Single(check.Unknown);
        Assert.Equal("@اسم الموضع", unknown.Name);
        Assert.Equal(1, unknown.Count);
        Assert.Contains(check.Known, k => k.Name == LetterMarks.Body);
        Assert.Contains(check.Known, k => k.Name == LetterMarks.Sender);
    }

    [Fact]
    public void The_known_marks_a_template_leaves_out_are_listed()
    {
        var template = TemplateBuilder.WithLines("بالإشارة إلى الموضوع أعلاه، @نص المراسلة");

        var check = _inspector.Check(template);

        Assert.Equal(8, check.Missing.Count);
        Assert.Contains(LetterMarks.HijriDate, check.Missing);
        Assert.Contains(LetterMarks.Number, check.Missing);
        Assert.DoesNotContain(LetterMarks.Body, check.Missing);
    }

    [Fact]
    public void A_template_with_no_body_mark_cannot_be_used_to_write_a_letter()
    {
        var check = _inspector.Check(TemplateBuilder.WithLines("الموضوع / @اسم الموضوع"));

        Assert.True(check.IsReadable);
        Assert.False(check.CanCompose);
        Assert.Contains(LetterMarks.Body, check.Missing);
    }

    [Fact]
    public void A_mark_repeated_in_both_copies_of_a_text_box_is_counted_each_time()
    {
        // The reference template's date and number live in text boxes, which Word writes twice.
        var check = _inspector.Check(LetterWorld.ReferenceTemplate());

        var number = Assert.Single(check.Known, k => k.Name == LetterMarks.Number);
        Assert.True(number.Count >= 2, $"The number mark was counted {number.Count} time(s).");
    }

    [Fact]
    public void A_file_that_is_not_a_word_document_is_reported_as_unreadable()
    {
        var check = _inspector.Check("ليس ملف وورد"u8);

        Assert.False(check.IsReadable);
        Assert.False(check.CanCompose);
        Assert.Empty(check.Known);
    }

    [Theory]
    [InlineData("الموضوع / @اسم الموضوع", "@اسم الموضوع")]
    [InlineData("رقم الصادر: @رقم الصادر.", "@رقم الصادر")]
    [InlineData("السيد / @اسم رئيس المكتب الموقر", "@اسم رئيس المكتب")]
    public void A_known_mark_is_read_whole_even_when_words_follow_it(string line, string expected) =>
        Assert.Equal([expected], LetterTemplateInspector.MarksIn(line));

    [Theory]
    [InlineData("@اسم المستلم غير معروف", "@اسم المستلم غير")]
    [InlineData("راسلنا @الجهة، وشكرًا", "@الجهة")]
    [InlineData("عنوان@مثال", "@مثال")]
    public void An_unknown_mark_stops_at_punctuation_or_after_three_words(string line, string expected) =>
        Assert.Equal([expected], LetterTemplateInspector.MarksIn(line));
}
