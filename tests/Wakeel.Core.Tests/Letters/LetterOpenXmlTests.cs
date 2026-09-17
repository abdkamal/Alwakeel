using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using Wakeel.Reports.Letters;

namespace Wakeel.Core.Tests.Letters;

/// <summary>
/// Tidying a paragraph's runs must never change what it says (AGREEMENT item 57): the mark search
/// and the splice both index against the paragraph's joined text, so a merge that moved a word
/// would put the letter's own values in the wrong place.
/// </summary>
public sealed class LetterOpenXmlTests
{
    [Fact]
    public void Two_runs_with_the_same_look_become_one()
    {
        var paragraph = new Paragraph(
            Run("الرقم "),
            Run("/ "),
            Run("الصادر"));

        LetterOpenXml.MergeRuns(paragraph);

        Assert.Equal("الرقم / الصادر", LetterOpenXml.TextOf(paragraph));
        Assert.Single(paragraph.Elements<Run>());
    }

    [Fact]
    public void A_hyperlink_between_two_runs_keeps_them_apart()
    {
        // A letterhead line shaped [البريد ][hyperlink][ هاتف 011]. The two outer runs look
        // identical, but joining them would carry the third run's words over the address and the
        // line would read in an order nobody typed.
        var paragraph = new Paragraph(
            Run("البريد "),
            new Hyperlink(Run("info@example.gov")),
            Run(" هاتف 011"));

        var before = LetterOpenXml.TextOf(paragraph);
        LetterOpenXml.MergeRuns(paragraph);

        Assert.Equal("البريد info@example.gov هاتف 011", before);
        Assert.Equal(before, LetterOpenXml.TextOf(paragraph));
    }

    [Fact]
    public void A_bookmark_between_two_runs_does_not()
    {
        // A bookmark holds no words of its own, so the runs on either side of one are still
        // neighbours and the paragraph is tidied as it would have been without it.
        var paragraph = new Paragraph(
            Run("التاريخ "),
            new BookmarkStart { Id = "1", Name = "date" },
            new BookmarkEnd { Id = "1" },
            Run("الهجري"));

        LetterOpenXml.MergeRuns(paragraph);

        Assert.Equal("التاريخ الهجري", LetterOpenXml.TextOf(paragraph));
        Assert.Single(paragraph.Elements<Run>());
    }

    private static Run Run(string text) =>
        new(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
}
