using Wakeel.Core.Services.Documents;

namespace Wakeel.Core.Tests.Documents;

/// <summary>
/// The cleanup every read page goes through before it is stored or searched, and which the
/// search package (B3-3) has to put its queries through as well.
/// </summary>
public sealed class ArabicOcrTextTests
{
    [Theory]
    [InlineData("١٢٣٤٥٦٧٨٩٠", "1234567890")]
    [InlineData("رقم ٢٠٢٦/١١٤", "رقم 2026/114")]
    [InlineData("۱۲۳", "123")]
    [InlineData("already 2026", "already 2026")]
    public void Arabic_indic_digits_become_western_digits(string input, string expected)
    {
        // AGREEMENT item 20: every numeral الوكيل shows is a western digit, including one read
        // off a page that was printed the other way.
        Assert.Equal(expected, ArabicOcrText.Clean(input));
        Assert.False(ArabicOcrText.HasArabicIndicDigits(ArabicOcrText.Clean(input)));
    }

    [Fact]
    public void Invisible_marks_a_reader_leaves_behind_are_dropped()
    {
        var withMarks = "وزارة‏ـ​الداخلية﻿";

        var cleaned = ArabicOcrText.Clean(withMarks);

        Assert.DoesNotContain('‏', cleaned);
        Assert.DoesNotContain('​', cleaned);
        Assert.DoesNotContain('﻿', cleaned);
    }

    [Fact]
    public void The_page_layout_is_kept_but_not_its_empty_margins()
    {
        var text = "السطر الأول   \r\n\r\n\r\n\r\nالسطر الثاني\r\n";

        var cleaned = ArabicOcrText.Clean(text);

        Assert.Equal("السطر الأول\n\nالسطر الثاني", cleaned);
    }

    [Fact]
    public void A_space_a_reader_puts_before_a_comma_is_taken_out()
    {
        Assert.Equal("الموضوع، والمرفقات.", ArabicOcrText.Clean("الموضوع ، والمرفقات ."));
    }

    [Fact]
    public void The_searchable_form_folds_the_same_way_every_other_field_does()
    {
        var normalized = ArabicOcrText.NormalizeForSearch("وِزَارَةُ الدَّاخِلِيَّة\nإلى المكتب");

        // Alef variants unified, ta marbuta folded, diacritics gone, lines collapsed to spaces.
        Assert.Equal("وزاره الداخليه الي المكتب", normalized);
    }

    [Fact]
    public void Nothing_in_becomes_nothing_out_rather_than_a_null()
    {
        Assert.Equal(string.Empty, ArabicOcrText.Clean(null));
        Assert.Equal(string.Empty, ArabicOcrText.NormalizeForSearch(null));
        Assert.Empty(ArabicOcrText.Words(null));
        Assert.Empty(ArabicOcrText.Words("   "));
    }

    [Fact]
    public void The_words_of_a_page_are_counted_without_its_blank_space()
    {
        var words = ArabicOcrText.Words("وزارة   الداخلية\n\nMinistry  2026");

        Assert.Equal(4, words.Count);
        Assert.Equal("وزارة", words[0]);
        Assert.Equal("2026", words[3]);
    }
}
