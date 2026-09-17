using Wakeel.Core.Services;
using Wakeel.Core.Services.Correspondence;
using Wakeel.Reports.Letters;

namespace Wakeel.Core.Tests.Letters;

/// <summary>
/// The composer against the owner's own template (AGREEMENT item 57): every mark filled, in the
/// text boxes the letterhead is made of as well as in the flowing text, and the letter that comes
/// out carrying no mark at all.
/// </summary>
public sealed class LetterComposerTests
{
    private readonly LetterComposer _composer = new();

    [Fact]
    public void Compose_fills_every_mark_and_leaves_none_behind()
    {
        var letter = LetterWorld.Sample();

        var composed = _composer.Compose(LetterWorld.ReferenceTemplate(), letter);
        var lines = LetterComposer.ReadLines(composed);
        var text = string.Join("\n", lines);

        foreach (var mark in LetterMarks.All)
        {
            Assert.DoesNotContain(mark, text, StringComparison.Ordinal);
        }

        Assert.Contains(letter.SubjectAr, text, StringComparison.Ordinal);
        Assert.Contains(letter.RecipientHeadNameAr, text, StringComparison.Ordinal);
        Assert.Contains(letter.RecipientOfficeNameAr, text, StringComparison.Ordinal);
        Assert.Contains(letter.SenderNameAr, text, StringComparison.Ordinal);
        Assert.Contains(letter.SenderOfficeNameAr, text, StringComparison.Ordinal);
        Assert.Contains(letter.NumberAr!, text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_line_that_writes_the_body_mark_twice_still_leaves_no_mark_behind()
    {
        // The letter goes in once — pouring it in again would put the letter inside itself — and
        // the mark that was not used is taken out rather than printed (AGREEMENT item 57).
        var template = TemplateBuilder.WithLines(
            "بالإشارة إلى الموضوع، @نص المراسلة وأيضاً @نص المراسلة",
            "وتفضلوا بقبول فائق الاحترام");
        var letter = LetterWorld.Sample() with { Body = LetterBody.FromPlainText("نأمل الموافقة.") };

        var text = string.Join("\n", LetterComposer.ReadLines(_composer.Compose(template, letter)));

        Assert.DoesNotContain(LetterMarks.Body, text, StringComparison.Ordinal);
        Assert.DoesNotContain('@', text);
        Assert.Contains("بالإشارة إلى الموضوع، نأمل الموافقة.", text, StringComparison.Ordinal);
        Assert.Contains("وأيضاً", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_keeps_the_fixed_phrases_the_template_carries()
    {
        var composed = _composer.Compose(LetterWorld.ReferenceTemplate(), LetterWorld.Sample());
        var text = string.Join("\n", LetterComposer.ReadLines(composed));

        // The letterhead and the set phrases belong to the template, not to الوكيل; the composer
        // must not touch them.
        Assert.Contains("الموقر", text, StringComparison.Ordinal);
        Assert.Contains("نهديكم أطيب التحيات", text, StringComparison.Ordinal);
        Assert.Contains("وتفضلوا بقبول فائق الاحترام والتقدير", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_replaces_marks_inside_text_boxes_in_both_copies()
    {
        // The reference template writes the date, the number and the signature block inside text
        // boxes, and a modern Word stores each one twice — a drawing and a VML fallback. Both
        // copies carry the value, so the two never disagree on paper.
        var composed = _composer.Compose(LetterWorld.ReferenceTemplate(), LetterWorld.Sample());
        var lines = LetterComposer.ReadLines(composed);

        var numberLines = lines.Count(l => l.Contains("و/م/1448/214", StringComparison.Ordinal));
        var senderLines = lines.Count(l => l.Contains("سعد بن ناصر", StringComparison.Ordinal));

        Assert.True(numberLines >= 2, $"The number was written {numberLines} time(s); both copies of the text box should carry it.");
        Assert.True(senderLines >= 2, $"The sender was written {senderLines} time(s); both copies of the text box should carry it.");
    }

    [Fact]
    public void Compose_replaces_marks_in_the_header()
    {
        var template = TemplateBuilder.WithHeader(
            headerLine: "هيئة @اسم مكتب المرسل — @رقم الصادر",
            bodyLine: "بالإشارة إلى الموضوع أعلاه، @نص المراسلة");

        var composed = _composer.Compose(template, LetterWorld.Sample());
        var text = string.Join("\n", LetterComposer.ReadLines(composed));

        Assert.Contains("هيئة مكتب المدير التنفيذي — و/م/1448/214", text, StringComparison.Ordinal);
        Assert.DoesNotContain("@", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_sees_a_mark_that_Word_cut_into_pieces()
    {
        // Word is free to split a mark across runs; nothing in the composer looks at one run.
        var template = TemplateBuilder.WithSplitMark(["الموضوع / @اسم ", "الم", "وضوع"]);

        var composed = _composer.Compose(template, LetterWorld.Sample());
        var text = string.Join("\n", LetterComposer.ReadLines(composed));

        Assert.Contains("الموضوع / ترشيح موظف لدورة تدريبية", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_writes_draft_in_the_number_s_place_before_approval()
    {
        var letter = LetterWorld.Sample() with { NumberAr = null };

        var composed = _composer.Compose(LetterWorld.ReferenceTemplate(), letter);
        var text = string.Join("\n", LetterComposer.ReadLines(composed));

        Assert.Contains(CoreAr.Letter.DraftNumber, text, StringComparison.Ordinal);
        Assert.DoesNotContain(LetterMarks.Number, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_keeps_the_words_before_the_body_mark_on_the_same_line()
    {
        var letter = LetterWorld.Sample() with
        {
            Body = LetterBody.FromPlainText("نأمل الموافقة على الترشيح."),
        };

        var composed = _composer.Compose(LetterWorld.ReferenceTemplate(), letter);
        var lines = LetterComposer.ReadLines(composed);

        Assert.Contains(
            lines,
            l => l.Contains("بالإشارة إلى الموضوع أعلاه، نأمل الموافقة على الترشيح.", StringComparison.Ordinal));
    }

    [Fact]
    public void Compose_writes_paragraphs_lists_and_bold()
    {
        var letter = LetterWorld.Sample() with
        {
            Body = LetterBody.Parse(
                """
                نأمل الموافقة على **ترشيح** الموظف.
                1. اسم الموظف: خالد.
                2. مدة الدورة: أسبوعان.
                - تُصرف بدلات الانتداب.
                وشكرًا لتعاونكم.
                """),
        };

        var composed = _composer.Compose(LetterWorld.ReferenceTemplate(), letter);
        var lines = LetterComposer.ReadLines(composed);
        var text = string.Join("\n", lines);

        Assert.Contains(
            lines,
            l => l.Contains("بالإشارة إلى الموضوع أعلاه، نأمل الموافقة على ترشيح الموظف.", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Trim() == "اسم الموظف: خالد.");
        Assert.Contains(lines, l => l.Trim() == "مدة الدورة: أسبوعان.");
        Assert.Contains(lines, l => l.Trim() == "تُصرف بدلات الانتداب.");
        Assert.Contains(lines, l => l.Trim() == "وشكرًا لتعاونكم.");

        // The markers come from the document's numbering definitions, not from the text itself.
        Assert.DoesNotContain("1. اسم الموظف", text, StringComparison.Ordinal);
        Assert.True(LetterProbe.HasNumbering(composed));
        Assert.True(LetterProbe.HasBoldRunWith(composed, "ترشيح"));

        // A list item is never stretched to both margins: that pulls a short item's words away
        // from its marker and leaves a gap between «1.» and the text it belongs to.
        Assert.NotEqual("both", LetterProbe.JustificationOf(composed, "اسم الموظف: خالد."));

        // Word's own way of asking for a round bullet: the character at F0B7 of the Symbol font,
        // rather than the ordinary bullet character looked up in a symbol-encoded font.
        Assert.Equal("", LetterProbe.BulletLevelText(composed));
    }

    [Fact]
    public void Compose_leaves_a_template_s_picture_bullets_in_front_of_its_own_definitions()
    {
        // Word reads the numbering part in a fixed order and calls the letter damaged if a
        // picture bullet turns up after an abstract definition — so an office template that uses
        // one must still open without a repair prompt.
        var template = TemplateBuilder.WithPictureBullet("ترويسة", "@نص المراسلة");
        var letter = LetterWorld.Sample() with { Body = LetterBody.Parse("- بند مُنقّط.") };

        var order = LetterProbe.NumberingPartOrder(_composer.Compose(template, letter)).ToList();

        var lastPicture = order.FindLastIndex(name => name == "numPicBullet");
        var firstAbstract = order.FindIndex(name => name == "abstractNum");
        Assert.True(lastPicture >= 0, "The template is expected to carry a picture bullet.");
        Assert.True(
            firstAbstract > lastPicture,
            $"A definition was written before the picture bullet: {string.Join(", ", order)}");
    }

    [Fact]
    public void Compose_never_pours_the_letter_into_its_own_words()
    {
        // The head of office explains, in the internal editor, where the mark goes — and a
        // subject line may quote it too. Neither is an instruction to insert the letter again.
        var letter = LetterWorld.Sample() with
        {
            SubjectAr = "شرح موضع @نص المراسلة في القالب",
            Body = LetterBody.FromPlainText("تُكتب العبارة @نص المراسلة في القالب مرة واحدة."),
        };

        var composed = _composer.Compose(LetterWorld.ReferenceTemplate(), letter);
        var text = string.Join("\n", LetterComposer.ReadLines(composed));

        Assert.Equal(1, text.Split("تُكتب العبارة").Length - 1);
        Assert.Contains("شرح موضع @نص المراسلة في القالب", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_trims_the_blank_paragraphs_the_template_left_under_the_body()
    {
        var before = LetterProbe.BlankParagraphsAfter(LetterWorld.ReferenceTemplate(), LetterMarks.Body);

        var letter = LetterWorld.Sample() with
        {
            Body = LetterBody.FromPlainText("سطر أول.\nسطر ثانٍ.\nسطر ثالث."),
        };
        var composed = _composer.Compose(LetterWorld.ReferenceTemplate(), letter);
        var after = LetterProbe.BlankParagraphsAfter(composed, "سطر ثالث.");

        Assert.True(before >= 1, "The reference template is expected to pad under the body mark.");
        Assert.True(after <= 1, $"{after} blank paragraphs were left under the body; at most one should remain.");
    }

    [Theory]
    [InlineData(LetterPageSize.A4, LetterPageLayout.A4WidthTwips, LetterPageLayout.A4HeightTwips)]
    [InlineData(LetterPageSize.A5, LetterPageLayout.A5WidthTwips, LetterPageLayout.A5HeightTwips)]
    public void Compose_sets_the_page_the_wizard_asked_for(LetterPageSize size, int width, int height)
    {
        var composed = _composer.Compose(LetterWorld.ReferenceTemplate(), LetterWorld.Sample(size));

        Assert.Equal((width, height), LetterComposer.ReadPageSize(composed));
    }

    [Fact]
    public void Compose_on_A5_shrinks_the_margins_and_anchors_the_shapes_to_them()
    {
        var composed = _composer.Compose(LetterWorld.ReferenceTemplate(), LetterWorld.Sample(LetterPageSize.A5));

        var margins = LetterProbe.Margins(composed);
        var page = LetterComposer.ReadPageSize(composed);

        Assert.True(margins.Left + margins.Right < page.Width, "The margins must leave a writing area on A5.");
        Assert.True(margins.Top + margins.Bottom < page.Height, "The margins must leave a writing area on A5.");

        // Every floating shape is tied to the margin, so the letterhead keeps its place on the
        // smaller sheet instead of drifting across the writing area.
        Assert.Empty(LetterProbe.PageAnchoredShapes(composed));
    }

    [Fact]
    public void Compose_never_changes_the_template_it_was_given()
    {
        var template = LetterWorld.ReferenceTemplate();
        var before = template.ToArray();

        _composer.Compose(template, LetterWorld.Sample(LetterPageSize.A5));

        Assert.Equal(before, template);
    }
}
