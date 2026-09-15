using Wakeel.Core.Services;

namespace Wakeel.Core.Tests;

public sealed class ArabicTextTests
{
    [Theory]
    [InlineData("أحمد", "احمد")]
    [InlineData("إحسان", "احسان")]
    [InlineData("آمال", "امال")]
    [InlineData("ٱلرحمن", "الرحمن")]
    public void Normalize_UnifiesAlefVariants(string input, string expected)
    {
        Assert.Equal(expected, ArabicText.Normalize(input));
    }

    [Fact]
    public void Normalize_MapsAlefMaksuraToYeh()
    {
        Assert.Equal("علي", ArabicText.Normalize("على"));
        Assert.Equal("مستشفي", ArabicText.Normalize("مستشفى"));
    }

    [Fact]
    public void Normalize_MapsTehMarbutaToHeh()
    {
        Assert.Equal("مدرسه", ArabicText.Normalize("مدرسة"));
    }

    [Fact]
    public void Normalize_StripsTashkeel()
    {
        Assert.Equal("محمد", ArabicText.Normalize("مُحَمَّد"));
    }

    [Fact]
    public void Normalize_StripsTatweel()
    {
        Assert.Equal("ممتاز", ArabicText.Normalize("مـــمـــتـــاز"));
    }

    [Fact]
    public void Normalize_CollapsesWhitespaceAndTrims()
    {
        Assert.Equal("مكتب مدير", ArabicText.Normalize("  مكتب   مدير  "));
    }

    [Fact]
    public void Normalize_EmptyOrNull_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ArabicText.Normalize(null));
        Assert.Equal(string.Empty, ArabicText.Normalize(string.Empty));
    }

    [Fact]
    public void Normalize_PreservesMixedArabicLatinContent()
    {
        Assert.Equal("جهاز PLN-PC-01 متصل", ArabicText.Normalize("جهاز PLN-PC-01 متصل"));
    }

    [Theory]
    [InlineData(0, "لا مهام")] // ARCHITECTURE.md §12 (2026-09-16): «لا » + plural, with no number.
    [InlineData(1, "مهمة")]
    [InlineData(2, "مهمتان")]
    [InlineData(3, "مهام")]
    [InlineData(10, "مهام")]
    [InlineData(11, "مهمة (11+)")]
    [InlineData(100, "مهمة (11+)")]
    public void Plural_ChoosesAgreeingForm(int count, string expected)
    {
        var actual = ArabicText.Plural(count, singular: "مهمة", dual: "مهمتان", plural: "مهام", pluralOver10: "مهمة (11+)");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Plural_Zero_UsesTheCallerSuppliedZeroText_WhenGiven()
    {
        var actual = ArabicText.Plural(
            0,
            singular: "مهمة",
            dual: "مهمتان",
            plural: "مهام",
            pluralOver10: "مهمة (11+)",
            zero: "لا مهام متأخرة");
        Assert.Equal("لا مهام متأخرة", actual);
    }

    [Fact]
    public void Plural_Zero_DoesNotRenderTheNumberItself()
    {
        // "0 مهام" is not how the count of nothing is said in a report sentence; the zero bucket
        // deliberately produces a phrase with no digits in it at all.
        var actual = ArabicText.Plural(0, singular: "مهمة", dual: "مهمتان", plural: "مهام", pluralOver10: "مهمة (11+)");
        Assert.DoesNotContain(actual, char.IsDigit);
        Assert.StartsWith("لا ", actual, StringComparison.Ordinal);
        Assert.EndsWith("مهام", actual, StringComparison.Ordinal);
    }

    [Fact]
    public void Plural_NegativeCount_UsesAbsoluteValue()
    {
        var actual = ArabicText.Plural(-2, singular: "مهمة", dual: "مهمتان", plural: "مهام", pluralOver10: "مهمة (11+)");
        Assert.Equal("مهمتان", actual);
    }

    [Fact]
    public void Plural_IntMinValue_DoesNotOverflow_AndFallsIntoThe11PlusBucket()
    {
        // Math.Abs(int.MinValue) alone throws OverflowException; int.MinValue has no positive
        // counterpart representable as int, so it must be handled as a special case.
        var actual = ArabicText.Plural(int.MinValue, singular: "مهمة", dual: "مهمتان", plural: "مهام", pluralOver10: "مهمة (11+)");
        Assert.Equal("مهمة (11+)", actual);
    }
}
