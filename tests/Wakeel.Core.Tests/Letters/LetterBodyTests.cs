using Wakeel.Core.Services.Correspondence;
using Wakeel.Reports.Letters;

namespace Wakeel.Core.Tests.Letters;

/// <summary>
/// The small rich-text model the internal editor writes into (AGREEMENT item 57): paragraphs,
/// numbered and bulleted lists, and bold — and nothing else.
/// </summary>
public sealed class LetterBodyTests
{
    [Fact]
    public void Blank_text_is_an_empty_body()
    {
        Assert.True(LetterBody.Parse(null).IsEmpty);
        Assert.True(LetterBody.Parse("   \n\n ").IsEmpty);
        Assert.True(LetterBody.FromPlainText(null).IsEmpty);
    }

    [Fact]
    public void Each_non_empty_line_becomes_a_paragraph()
    {
        var body = LetterBody.FromPlainText("سطر أول\n\nسطر ثانٍ\r\nسطر ثالث");

        Assert.Equal(3, body.Blocks.Count);
        Assert.All(body.Blocks, b => Assert.Equal(LetterBlockKind.Paragraph, b.Kind));
        Assert.Equal("سطر ثانٍ", body.Blocks[1].Text);
    }

    [Theory]
    [InlineData("- بند", LetterBlockKind.Bulleted, "بند")]
    [InlineData("• بند", LetterBlockKind.Bulleted, "بند")]
    [InlineData("1. بند", LetterBlockKind.Numbered, "بند")]
    [InlineData("2) بند", LetterBlockKind.Numbered, "بند")]
    [InlineData("12- بند", LetterBlockKind.Numbered, "بند")]
    [InlineData("عادي", LetterBlockKind.Paragraph, "عادي")]
    public void A_line_s_opening_says_what_kind_of_block_it_is(string line, LetterBlockKind kind, string text)
    {
        var block = Assert.Single(LetterBody.Parse(line).Blocks);

        Assert.Equal(kind, block.Kind);
        Assert.Equal(text, block.Text);
    }

    [Fact]
    public void A_year_at_the_start_of_a_sentence_is_not_read_as_a_list_marker()
    {
        // «1447هـ كان عامًا…» begins with digits but no separator follows them.
        var block = Assert.Single(LetterBody.Parse("1447هـ كان عامًا حافلًا.").Blocks);

        Assert.Equal(LetterBlockKind.Paragraph, block.Kind);
        Assert.Equal("1447هـ كان عامًا حافلًا.", block.Text);
    }

    [Fact]
    public void Text_between_double_stars_is_heavy()
    {
        var block = Assert.Single(LetterBody.Parse("نأمل **الموافقة** على الطلب.").Blocks);

        Assert.Collection(
            block.Spans,
            s => { Assert.Equal("نأمل ", s.Text); Assert.False(s.Bold); },
            s => { Assert.Equal("الموافقة", s.Text); Assert.True(s.Bold); },
            s => { Assert.Equal(" على الطلب.", s.Text); Assert.False(s.Bold); });

        Assert.Equal("نأمل الموافقة على الطلب.", block.Text);
    }

    [Fact]
    public void An_unclosed_star_pair_leaves_the_rest_heavy_rather_than_showing_the_stars()
    {
        var block = Assert.Single(LetterBody.Parse("نأمل **الموافقة").Blocks);

        Assert.Equal("نأمل الموافقة", block.Text);
        Assert.Contains(block.Spans, s => s.Bold && s.Text == "الموافقة");
    }

    [Fact]
    public void The_plain_form_writes_the_list_markers_back_out()
    {
        var body = LetterBody.Parse(
            """
            مقدمة.
            1. أول.
            2. ثانٍ.
            - نقطة.
            """);

        Assert.Equal("مقدمة.\n1. أول.\n2. ثانٍ.\n- نقطة.", body.ToPlainText());
    }

    [Fact]
    public void Numbering_starts_again_after_a_paragraph_breaks_the_list()
    {
        var body = LetterBody.Parse(
            """
            1. أول.
            2. ثانٍ.
            فاصل.
            1. أول من جديد.
            """);

        Assert.Equal("1. أول.\n2. ثانٍ.\nفاصل.\n1. أول من جديد.", body.ToPlainText());
    }

    [Fact]
    public void A_body_of_only_a_list_starts_the_letter_with_the_first_item()
    {
        // The opening phrase «بالإشارة إلى الموضوع أعلاه،» keeps its own line above the list: a
        // numbered item beginning mid-sentence would read as part of it.
        var letter = LetterWorld.Sample() with
        {
            Body = LetterBody.Parse("1. البند الأول.\n2. البند الثاني."),
        };

        var lines = LetterComposer.ReadLines(new LetterComposer().Compose(LetterWorld.ReferenceTemplate(), letter));

        Assert.Contains(lines, l => l.Trim() == "بالإشارة إلى الموضوع أعلاه،");
        Assert.Contains(lines, l => l.Trim() == "البند الأول.");
        Assert.Contains(lines, l => l.Trim() == "البند الثاني.");
    }
}
