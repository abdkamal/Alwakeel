using Wakeel.Core.Services.Correspondence;
using Wakeel.Reports.Letters;

namespace Wakeel.Core.Tests.Letters;

/// <summary>
/// The page the generated letter is set to, and the re-anchoring that keeps the template's text
/// boxes inside the writing area when the page changes (AGREEMENT item 57).
/// </summary>
public sealed class LetterPageLayoutTests
{
    [Theory]
    [InlineData(LetterPageSize.A4, 11906, 16838)]
    [InlineData(LetterPageSize.A5, 8419, 11906)]
    public void The_two_pages_are_the_paper_sizes_they_are_named_after(LetterPageSize size, int width, int height) =>
        Assert.Equal((width, height), LetterPageLayout.Size(size));

    [Fact]
    public void A5_is_half_of_A4_the_long_way()
    {
        // A5's long side is A4's short side; getting this the wrong way round would rotate the page.
        Assert.Equal(LetterPageLayout.A4WidthTwips, LetterPageLayout.A5HeightTwips);
    }

    [Theory]
    // The distance loses exactly the margin it used to include, so the box stays where the
    // template put it: 56.7pt from the paper, a half-inch margin, 20.7pt from the margin.
    [InlineData(
        "position:absolute;left:56.7pt;top:56.7pt;mso-position-horizontal-relative:page;mso-position-vertical-relative:page",
        "position:absolute;left:20.7pt;top:20.7pt;mso-position-horizontal-relative:margin;mso-position-vertical-relative:margin")]
    // Word's other spelling of the same two declarations, and its other units.
    [InlineData(
        "margin-left:2cm;margin-top:1in;mso-position-horizontal-relative:page;mso-position-vertical-relative:page",
        "margin-left:0.73cm;margin-top:0.5in;mso-position-horizontal-relative:margin;mso-position-vertical-relative:margin")]
    // A shape against the paper's edge is written out as one margin before the margin.
    [InlineData(
        "position:absolute;mso-position-horizontal-relative:page;mso-position-vertical-relative:page",
        "position:absolute;mso-position-horizontal-relative:margin;mso-position-vertical-relative:margin;left:-36pt;top:-36pt")]
    // "column" already means "from where the text starts": renamed, never moved.
    [InlineData(
        "left:10pt;mso-position-horizontal-relative:column",
        "left:10pt;mso-position-horizontal-relative:margin")]
    // A distance written in something unreadable keeps the paper rather than being moved blindly.
    [InlineData(
        "left:calc(2em);mso-position-horizontal-relative:page",
        "left:calc(2em);mso-position-horizontal-relative:page")]
    [InlineData(
        "mso-position-vertical-relative:margin;width:10pt",
        "mso-position-vertical-relative:margin;width:10pt")]
    [InlineData("width:100pt;height:20pt", "width:100pt;height:20pt")]
    public void A_shape_s_style_is_re_anchored_to_the_margin_and_moved_by_it(
        string before,
        string expected) =>
        Assert.Equal(expected, LetterPageLayout.ReanchorVmlStyle(before, 720, 720)?.Value);

    [Theory]
    [InlineData(LetterPageSize.A4, 8.27, 11.69)]
    [InlineData(LetterPageSize.A5, 5.85, 8.27)]
    public void The_page_is_also_known_in_the_unit_a_print_job_speaks(
        LetterPageSize size,
        double width,
        double height)
    {
        // The letter printed without Word goes through the browser engine, which puts it on the
        // paper the print job names and ignores the size the rendering's own style sheet
        // declares. Without this an A5 letter would come out on a foreign page.
        var (actualWidth, actualHeight) = LetterPageLayout.SizeInInches(size);

        Assert.Equal(width, actualWidth, 2);
        Assert.Equal(height, actualHeight, 2);
    }

    [Fact]
    public void An_empty_or_missing_style_is_left_alone()
    {
        Assert.Null(LetterPageLayout.ReanchorVmlStyle(null, 720, 720));
        Assert.Equal(string.Empty, LetterPageLayout.ReanchorVmlStyle(string.Empty, 720, 720)?.Value);
    }

    [Fact]
    public void A4_keeps_the_margins_the_template_set()
    {
        var a4 = new LetterComposer().Compose(LetterWorld.ReferenceTemplate(), LetterWorld.Sample());
        var a5 = new LetterComposer().Compose(LetterWorld.ReferenceTemplate(), LetterWorld.Sample(LetterPageSize.A5));

        var onA4 = LetterProbe.Margins(a4);
        var onA5 = LetterProbe.Margins(a5);

        // The half sheet gets proportionally smaller margins, so the writing area keeps its shape
        // instead of the text being squeezed into the middle of a narrower page.
        Assert.True(onA5.Left < onA4.Left, "A5's side margins should be narrower than A4's.");
        Assert.True(onA5.Top < onA4.Top, "A5's top margin should be shallower than A4's.");
        Assert.True(onA5.Left > 0 && onA5.Top > 0, "A5 must still have margins.");
    }
}
