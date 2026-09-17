using Wakeel.Core.Services.Correspondence;
using Wakeel.Reports.Letters;

namespace Wakeel.Core.Tests.Letters;

/// <summary>
/// The HTML rendering that stands in for Word (ARCHITECTURE §9): the same letter, laid out like
/// the template, on a page of the same size, with no mark anywhere in it.
/// </summary>
public sealed class LetterHtmlRendererTests
{
    private readonly LetterComposer _composer = new();

    [Fact]
    public void The_rendering_contains_no_mark()
    {
        var html = _composer.RenderHtml(LetterWorld.ReferenceTemplate(), LetterWorld.Sample());

        foreach (var mark in LetterMarks.All)
        {
            Assert.DoesNotContain(mark, html, StringComparison.Ordinal);
        }

        // Not one stray '@' either, anywhere a reader would see it: an unfilled mark on a printed
        // letter is the failure this is guarding against. Only the page it is printed on is
        // searched, since the stylesheet above it legitimately writes @page and @media.
        Assert.DoesNotContain('@', Printed(html));
    }

    /// <summary>The part of the rendering that ends up on paper.</summary>
    private static string Printed(string html)
    {
        var body = html.IndexOf("<body>", StringComparison.Ordinal);
        Assert.True(body >= 0, "The rendering is expected to be a whole HTML page.");
        return html[body..];
    }

    [Fact]
    public void The_rendering_carries_the_letter_s_values_and_the_template_s_fixed_phrases()
    {
        var letter = LetterWorld.Sample();

        var html = _composer.RenderHtml(LetterWorld.ReferenceTemplate(), letter);

        Assert.Contains(letter.SubjectAr, html, StringComparison.Ordinal);
        Assert.Contains(letter.RecipientHeadNameAr, html, StringComparison.Ordinal);
        Assert.Contains(letter.SenderOfficeNameAr, html, StringComparison.Ordinal);
        Assert.Contains("19 صفر 1448", html, StringComparison.Ordinal);
        Assert.Contains("2 أغسطس 2026", html, StringComparison.Ordinal);
        Assert.Contains("الموقر", html, StringComparison.Ordinal);
        Assert.Contains("وتفضلوا بقبول فائق الاحترام والتقدير", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_rendering_is_right_to_left_and_isolates_every_value()
    {
        var html = _composer.RenderHtml(LetterWorld.ReferenceTemplate(), LetterWorld.Sample());

        Assert.Contains("dir=\"rtl\"", html, StringComparison.Ordinal);
        Assert.Contains("lang=\"ar\"", html, StringComparison.Ordinal);

        // A number like و/م/1448/214 beside Arabic words reads in the order it was typed only
        // because THAT VALUE is isolated on its own, not because the finished line around it is
        // (AGREEMENT item 11): the label «الرقم /» stays outside the isolate.
        Assert.Contains("<bdi>و/م/1448/214</bdi>", html, StringComparison.Ordinal);
        Assert.Contains("<bdi>ترشيح موظف لدورة تدريبية</bdi>", html, StringComparison.Ordinal);
        Assert.Contains("<bdi>19 صفر 1448</bdi>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<bdi>الرقم", html, StringComparison.Ordinal);
        Assert.Contains("unicode-bidi: isolate", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LetterPageSize.A4, "210mm 297mm")]
    [InlineData(LetterPageSize.A5, "148mm 210mm")]
    public void The_page_matches_the_one_the_wizard_asked_for(LetterPageSize size, string expected)
    {
        var html = _composer.RenderHtml(LetterWorld.ReferenceTemplate(), LetterWorld.Sample(size));

        Assert.Contains("size: " + expected, html, StringComparison.Ordinal);
    }

    [Fact]
    public void Lists_and_bold_survive_into_the_rendering()
    {
        var letter = LetterWorld.Sample() with
        {
            Body = LetterBody.Parse(
                """
                نأمل الموافقة على **الترشيح**.
                1. البند الأول.
                2. البند الثاني.
                - ملاحظة إضافية.
                """),
        };

        var html = _composer.RenderHtml(LetterWorld.ReferenceTemplate(), letter);

        Assert.Contains("<strong><bdi>الترشيح</bdi></strong>", html, StringComparison.Ordinal);
        Assert.Contains("<ol class=\"body-list\">", html, StringComparison.Ordinal);
        Assert.Contains("<ul class=\"body-list\">", html, StringComparison.Ordinal);
        Assert.Contains("<li><bdi>البند الأول.</bdi></li>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_opening_phrase_keeps_the_first_paragraph_company()
    {
        var letter = LetterWorld.Sample() with
        {
            Body = LetterBody.FromPlainText("نأمل الموافقة."),
        };

        var html = _composer.RenderHtml(LetterWorld.ReferenceTemplate(), letter);

        // The template's own opening words are written as the template wrote them; only the
        // letter's own text is isolated.
        Assert.Contains(
            "بالإشارة إلى الموضوع أعلاه، <bdi>نأمل الموافقة.</bdi>",
            html,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_that_looks_like_markup_is_escaped()
    {
        var letter = LetterWorld.Sample() with { SubjectAr = "<script>alert(1)</script>" };

        var html = _composer.RenderHtml(LetterWorld.ReferenceTemplate(), letter);

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_the_template_says_once_is_printed_twice()
    {
        // A modern Word stores every text box twice — a drawing and a VML fallback — and the
        // letterhead lives in the page header. The composer must fill all of those; the printed
        // sheet must show each of them once.
        var letter = LetterWorld.Sample();
        var printed = Printed(_composer.RenderHtml(LetterWorld.ReferenceTemplate(), letter));

        Assert.Equal(1, Occurrences(printed, letter.NumberAr!));
        Assert.Equal(1, Occurrences(printed, letter.SubjectAr));
        Assert.Equal(1, Occurrences(printed, letter.SenderNameAr));
        Assert.Equal(1, Occurrences(printed, letter.SenderOfficeNameAr));
        Assert.Equal(1, Occurrences(printed, letter.RecipientHeadNameAr));
        Assert.Equal(1, Occurrences(printed, "الترويسة"));
        Assert.Equal(1, Occurrences(printed, "وتفضلوا بقبول فائق الاحترام والتقدير"));
    }

    [Fact]
    public void The_letterhead_is_printed_above_the_letter_and_not_under_the_signature()
    {
        var letter = LetterWorld.Sample();
        var printed = Printed(_composer.RenderHtml(LetterWorld.ReferenceTemplate(), letter));

        var letterhead = printed.IndexOf("الترويسة", StringComparison.Ordinal);
        var addressee = printed.IndexOf(letter.RecipientHeadNameAr, StringComparison.Ordinal);
        var signature = printed.IndexOf(letter.SenderNameAr, StringComparison.Ordinal);

        Assert.Contains("<header class=\"letterhead\">", printed, StringComparison.Ordinal);
        Assert.True(letterhead >= 0 && letterhead < addressee, "The letterhead comes first.");
        Assert.True(addressee < signature, "The signature comes last.");
    }

    [Fact]
    public void The_floating_blocks_stand_where_the_template_anchors_them()
    {
        // The template floats the date and number block against one edge and lays the subject
        // across a band of its own; printing them in the flow instead would be a different letter.
        var letter = LetterWorld.Sample();
        var printed = Printed(_composer.RenderHtml(LetterWorld.ReferenceTemplate(), letter));

        Assert.Contains(letter.NumberAr!, Block(printed, "block left"), StringComparison.Ordinal);
        Assert.Contains(letter.SubjectAr, Block(printed, "block band"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_line_break_inside_the_template_s_date_block_starts_a_new_line()
    {
        // The two dates and the number are one paragraph with line breaks between them: printed as
        // one run-together line, «1448» and «2» would meet and read as a single number.
        var printed = Printed(_composer.RenderHtml(LetterWorld.ReferenceTemplate(), LetterWorld.Sample()));

        var hijri = printed.IndexOf("19 صفر 1448", StringComparison.Ordinal);
        var gregorian = printed.IndexOf("2 أغسطس 2026", StringComparison.Ordinal);

        Assert.True(hijri >= 0 && gregorian > hijri, "Both dates are printed, the Hijri one first.");
        Assert.Contains("</p>", printed[hijri..gregorian], StringComparison.Ordinal);
    }

    [Fact]
    public void The_template_s_padding_does_not_push_the_closing_phrase_onto_a_second_page()
    {
        // The reference template keeps a long run of empty paragraphs between the body and the
        // closing phrase, so that a letter typed in Word by hand lands low on the sheet. A printed
        // page cannot reflow around the letter's own length, so that run becomes one blank line.
        var printed = Printed(_composer.RenderHtml(LetterWorld.ReferenceTemplate(), LetterWorld.Sample()));

        Assert.True(
            Occurrences(printed, "<p class=\"blank\"></p>") <= 3,
            "A run of the template's blank paragraphs is expected to become one blank line.");
    }

    [Fact]
    public void A_template_line_that_writes_the_body_mark_twice_leaves_no_mark_on_the_sheet()
    {
        var template = TemplateBuilder.WithLines(
            "بالإشارة إلى الموضوع، @نص المراسلة وأيضاً @نص المراسلة",
            "وتفضلوا بقبول فائق الاحترام");
        var letter = LetterWorld.Sample() with { Body = LetterBody.FromPlainText("نأمل الموافقة.") };

        var printed = Printed(_composer.RenderHtml(template, letter));

        Assert.DoesNotContain('@', printed);
        Assert.Contains("وأيضاً", printed, StringComparison.Ordinal);
    }

    /// <summary>How many times <paramref name="what"/> appears in <paramref name="text"/>.</summary>
    private static int Occurrences(string text, string what)
    {
        var count = 0;
        var at = 0;
        while ((at = text.IndexOf(what, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += what.Length;
        }

        return count;
    }

    /// <summary>The contents of the first block carrying <paramref name="className"/>.</summary>
    private static string Block(string printed, string className)
    {
        var open = printed.IndexOf("<aside class=\"" + className + "\">", StringComparison.Ordinal);
        Assert.True(open >= 0, "The rendering is expected to carry a «" + className + "» block.");
        var close = printed.IndexOf("</aside>", open, StringComparison.Ordinal);
        Assert.True(close > open, "The block is expected to be closed.");
        return printed[open..close];
    }

    [Fact]
    public void A_template_that_cannot_be_read_still_prints_the_letter_s_text()
    {
        var letter = LetterWorld.Sample() with { Body = LetterBody.FromPlainText("نص الكتاب.") };

        var html = _composer.RenderHtml([], letter);

        Assert.Contains("نص الكتاب.", html, StringComparison.Ordinal);
        Assert.DoesNotContain('@', Printed(html));
    }
}
