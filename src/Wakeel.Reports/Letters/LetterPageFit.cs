using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Wakeel.Reports.Letters;

/// <summary>How much of the last page a document has left empty.</summary>
/// <param name="ContentWidthTwips">The writing area's width.</param>
/// <param name="ContentHeightTwips">The writing area's height on one page.</param>
/// <param name="UsedOnLastPageTwips">How far down the last page the text reaches.</param>
/// <param name="DefaultFontHalfPoints">The document's body size, for measuring what is added.</param>
internal sealed record LetterPageFit(
    int ContentWidthTwips,
    int ContentHeightTwips,
    int UsedOnLastPageTwips,
    int DefaultFontHalfPoints)
{
    /// <summary>What is left at the foot of the last page.</summary>
    public int FreeOnLastPageTwips => Math.Max(0, ContentHeightTwips - UsedOnLastPageTwips);
}

/// <summary>
/// Answers "will this still fit at the bottom of the last page?" — the question AGREEMENT item 31
/// makes the referral's print copy ask before it is saved.
/// </summary>
/// <remarks>
/// <para>
/// Word itself does not lay a document out until it is opened, and الوكيل must answer before the
/// user presses save and without Word being installed at all. So the answer is an estimate, made
/// the way a typist would make it: the writing area is so many lines tall, each paragraph is as
/// many lines as its characters need at this size, and what is left over on the last page is the
/// free area. It reads a little pessimistically on purpose — a referral pushed onto its own page
/// when it would just have fitted costs a sheet of paper, while one that overflows the page it
/// was promised costs a reprint of an official document.
/// </para>
/// <para>
/// Only the flow of the body is counted. Text boxes are what a template's letterhead is made of:
/// they float over the page and do not push the text down, so counting them would add the
/// letterhead's height to every letter.
/// </para>
/// </remarks>
internal static class LetterPageFitter
{
    /// <summary>Twips in one point.</summary>
    private const int TwipsPerPoint = 20;

    /// <summary>Line height as a multiple of the font size, matching Word's single spacing.</summary>
    private const double LineFactor = 1.3;

    /// <summary>
    /// The average width of a character as a fraction of the font size. Arabic script at a given
    /// point size is narrower than Latin; 0.5 is measured against the reference template's body
    /// text, where a full line of Arabic at 13 pt holds roughly 80 characters across 17 cm.
    /// </summary>
    private const double CharWidthFactor = 0.5;

    /// <summary>Space Word leaves under a paragraph when the style says nothing, in twips.</summary>
    private const int DefaultSpacingAfterTwips = 120;

    /// <summary>Measures a document.</summary>
    /// <param name="document">The open document.</param>
    public static LetterPageFit Measure(WordprocessingDocument document)
    {
        var body = document.MainDocumentPart?.Document?.Body;
        var section = body?.Descendants<SectionProperties>().LastOrDefault();
        var page = section?.GetFirstChild<PageSize>();
        var margin = section?.GetFirstChild<PageMargin>();

        var pageWidth = page?.Width?.Value is { } w ? (int)w : LetterPageLayout.A4WidthTwips;
        var pageHeight = page?.Height?.Value is { } h ? (int)h : LetterPageLayout.A4HeightTwips;
        var left = margin?.Left?.Value is { } l ? (int)l : 1134;
        var right = margin?.Right?.Value is { } r ? (int)r : 1134;
        var top = margin?.Top?.Value ?? 1134;
        var bottom = margin?.Bottom?.Value ?? 1134;

        var contentWidth = Math.Max(1000, pageWidth - left - right);
        var contentHeight = Math.Max(1000, pageHeight - Math.Max(0, top) - Math.Max(0, bottom));

        var fontHalfPoints = DefaultFontHalfPoints(document);
        var used = 0;

        if (body is not null)
        {
            foreach (var paragraph in body.Elements<Paragraph>())
            {
                used += Height(LetterOpenXml.TextOf(paragraph), FontOf(paragraph, fontHalfPoints), contentWidth);
            }

            foreach (var table in body.Elements<Table>())
            {
                foreach (var paragraph in table.Descendants<Paragraph>())
                {
                    used += Height(LetterOpenXml.TextOf(paragraph), FontOf(paragraph, fontHalfPoints), contentWidth);
                }
            }
        }

        // Explicit page breaks start a new page, so whatever came before them is not on the last.
        var breaks = body?.Descendants<Break>().Count(b => b.Type?.Value == BreakValues.Page) ?? 0;
        var usedOnLast = used <= contentHeight && breaks == 0
            ? used
            : used % contentHeight;

        return new LetterPageFit(contentWidth, contentHeight, usedOnLast, fontHalfPoints);
    }

    /// <summary>
    /// The height a block of text would take in the same writing area, for deciding whether the
    /// referral fits under the letter.
    /// </summary>
    /// <param name="lines">The lines to be added.</param>
    /// <param name="fit">The measured document.</param>
    public static int HeightOf(IEnumerable<string> lines, LetterPageFit fit) =>
        lines.Sum(line => Height(line, fit.DefaultFontHalfPoints, fit.ContentWidthTwips));

    private static int Height(string text, int fontHalfPoints, int contentWidth)
    {
        var fontPoints = fontHalfPoints / 2.0;
        var lineHeight = (int)Math.Ceiling(fontPoints * TwipsPerPoint * LineFactor);
        var charWidth = Math.Max(1.0, fontPoints * TwipsPerPoint * CharWidthFactor);
        var perLine = Math.Max(1, (int)(contentWidth / charWidth));
        var lines = Math.Max(1, (int)Math.Ceiling(text.Trim().Length / (double)perLine));
        return (lines * lineHeight) + DefaultSpacingAfterTwips;
    }

    private static int FontOf(Paragraph paragraph, int fallback)
    {
        var size = paragraph.Elements<Run>()
            .Select(r => r.RunProperties?.FontSize?.Val?.Value)
            .FirstOrDefault(v => v is not null);
        return size is not null && int.TryParse(size, out var halfPoints) && halfPoints > 0
            ? halfPoints
            : fallback;
    }

    private static int DefaultFontHalfPoints(WordprocessingDocument document)
    {
        var value = document.MainDocumentPart?.StyleDefinitionsPart?.Styles?
            .GetFirstChild<DocDefaults>()?
            .RunPropertiesDefault?
            .RunPropertiesBaseStyle?
            .FontSize?.Val?.Value;
        return value is not null && int.TryParse(value, out var halfPoints) && halfPoints > 0
            ? halfPoints
            : 24;
    }
}
