using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Wakeel.Core.Services.Correspondence;

namespace Wakeel.Reports.Letters;

/// <summary>
/// Puts the letter's own text where <c>@نص المراسلة</c> stood (AGREEMENT item 57).
/// </summary>
/// <remarks>
/// <para>
/// The mark almost never stands alone. The reference template writes «بالإشارة إلى الموضوع
/// أعلاه، @نص المراسلة», and the opening words must stay on the same line as the first words of
/// the letter — so the first paragraph of the body is appended to the paragraph the mark was in,
/// and only the rest become paragraphs of their own, each one carrying the template paragraph's
/// own alignment, spacing and direction. A list item never joins that line: a numbered item
/// beginning mid-sentence would read as part of the opening phrase.
/// </para>
/// <para>
/// Templates leave blank paragraphs under the mark to keep the closing phrase down the page. A
/// body of eight paragraphs plus those blanks would push the signature onto a second page for no
/// reason, so once the body is in, the run of blank paragraphs that follows is trimmed to one.
/// </para>
/// </remarks>
internal static class LetterBodyWriter
{
    /// <summary>Indent of a list item from the start of the line, in twips.</summary>
    private const int ListIndentTwips = 720;

    /// <summary>How far the marker hangs back from the item's text, in twips.</summary>
    private const int ListHangingTwips = 360;

    /// <summary>
    /// Replaces the body mark in <paramref name="paragraph"/> with <paramref name="body"/>.
    /// </summary>
    /// <param name="document">The open letter, for the numbering definitions.</param>
    /// <param name="inMainPart">Whether this paragraph is in the body of the document.</param>
    /// <param name="paragraph">The paragraph holding the mark.</param>
    /// <param name="markIndex">Where the mark starts in that paragraph's joined text.</param>
    /// <param name="body">The letter's text.</param>
    /// <returns>
    /// The last paragraph the body occupies, so the caller can go on from there instead of
    /// reading the paragraph it just wrote into all over again.
    /// </returns>
    public static Paragraph Insert(
        WordprocessingDocument document,
        bool inMainPart,
        Paragraph paragraph,
        int markIndex,
        LetterBody body)
    {
        var mark = LetterMarks.Body;
        var whole = LetterOpenXml.TextOf(paragraph);
        var prefix = whole[..markIndex];
        var suffix = whole[(markIndex + mark.Length)..];

        var modelRun = RunAt(paragraph, markIndex);
        var runProperties = modelRun?.RunProperties?.CloneNode(true) as RunProperties;
        var paragraphProperties = paragraph.ParagraphProperties?.CloneNode(true) as ParagraphProperties;

        // Take the mark out, then whatever followed it on the same line: the letter's text stands
        // between them, and the tail is put back after the last block.
        LetterOpenXml.Splice(paragraph, markIndex, mark.Length, string.Empty);
        if (suffix.Length > 0)
        {
            LetterOpenXml.Splice(paragraph, markIndex, suffix.Length, string.Empty);
        }

        var blocks = body.Blocks.Where(b => !string.IsNullOrWhiteSpace(b.Text)).ToList();
        if (blocks.Count == 0)
        {
            if (suffix.Length > 0)
            {
                paragraph.AppendChild(MakeRun(suffix, false, runProperties));
            }

            return paragraph;
        }

        var numbering = inMainPart ? LetterNumbering.Ensure(document) : null;
        var placed = new List<Paragraph>();
        var prefixIsEmpty = prefix.Trim().Length == 0;
        var start = 0;

        if (prefixIsEmpty)
        {
            // Nothing worth keeping on the line: the paragraph becomes the first block itself.
            ClearRuns(paragraph);
            Fill(paragraph, blocks[0], runProperties, paragraphProperties, numbering, 1);
            placed.Add(paragraph);
            start = 1;
        }
        else if (blocks[0].Kind == LetterBlockKind.Paragraph)
        {
            foreach (var run in RunsFor(blocks[0], runProperties))
            {
                paragraph.AppendChild(run);
            }

            placed.Add(paragraph);
            start = 1;
        }
        else
        {
            placed.Add(paragraph);
        }

        var previous = paragraph;
        var itemNumber = 0;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Kind == LetterBlockKind.Numbered)
            {
                itemNumber++;
            }
            else
            {
                itemNumber = 0;
            }

            if (i < start)
            {
                continue;
            }

            var next = new Paragraph();
            if (paragraphProperties?.CloneNode(true) is ParagraphProperties cloned)
            {
                next.ParagraphProperties = cloned;
            }

            Fill(next, blocks[i], runProperties, paragraphProperties, numbering, itemNumber);
            previous.InsertAfterSelf(next);
            previous = next;
            placed.Add(next);
        }

        if (suffix.Length > 0)
        {
            placed[^1].AppendChild(MakeRun(suffix, false, runProperties));
        }

        TrimBlankParagraphsAfter(placed[^1]);
        return placed[^1];
    }

    /// <summary>
    /// Leaves at most one blank paragraph after the body, so the template's breathing space is
    /// kept but its padding is not.
    /// </summary>
    /// <param name="last">The last paragraph of the inserted body.</param>
    internal static void TrimBlankParagraphsAfter(Paragraph last)
    {
        var blanks = new List<Paragraph>();
        var sibling = last.NextSibling();
        while (sibling is Paragraph candidate && IsBlank(candidate))
        {
            blanks.Add(candidate);
            sibling = candidate.NextSibling();
        }

        foreach (var extra in blanks.Skip(1))
        {
            extra.Remove();
        }
    }

    /// <summary>
    /// Whether a paragraph carries nothing a reader would see. A paragraph holding a picture, a
    /// drawing or a page break is never blank, however little text it has.
    /// </summary>
    /// <param name="paragraph">The paragraph.</param>
    internal static bool IsBlank(Paragraph paragraph)
    {
        if (LetterOpenXml.TextOf(paragraph).Trim().Length > 0)
        {
            return false;
        }

        return !paragraph.Descendants<Drawing>().Any()
            && !paragraph.Descendants<Picture>().Any()
            && !paragraph.Descendants<Break>().Any()
            && paragraph.GetFirstChild<ParagraphProperties>()?.SectionProperties is null;
    }

    private static void Fill(
        Paragraph target,
        LetterBlock block,
        RunProperties? runProperties,
        ParagraphProperties? modelProperties,
        LetterNumbering.Ids? numbering,
        int itemNumber)
    {
        if (target.ParagraphProperties is null && modelProperties?.CloneNode(true) is ParagraphProperties cloned)
        {
            target.ParagraphProperties = cloned;
        }

        if (block.Kind != LetterBlockKind.Paragraph)
        {
            ApplyListLook(target, block.Kind, numbering);
        }

        var spans = block.Spans;
        if (block.Kind != LetterBlockKind.Paragraph && numbering is null)
        {
            // No numbering definitions to lean on (the mark sat in a header): write the marker as
            // text so the list still reads as a list.
            var marker = block.Kind == LetterBlockKind.Numbered
                ? itemNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) + ". "
                : "• ";
            target.AppendChild(MakeRun(marker, false, runProperties));
        }

        foreach (var run in RunsFor(new LetterBlock(block.Kind, spans), runProperties))
        {
            target.AppendChild(run);
        }
    }

    private static void ApplyListLook(Paragraph target, LetterBlockKind kind, LetterNumbering.Ids? numbering)
    {
        var properties = target.ParagraphProperties ??= new ParagraphProperties();

        // A list item is never justified: stretching an item to both margins pulls its words away
        // from its marker. It starts at the margin the template's own text starts at.
        properties.Justification = new Justification { Val = JustificationValues.Start };
        properties.Indentation = new Indentation
        {
            Start = ListIndentTwips.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Hanging = ListHangingTwips.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        if (numbering is null)
        {
            return;
        }

        var numberingId = kind == LetterBlockKind.Numbered ? numbering.Decimal : numbering.Bullet;
        properties.NumberingProperties = new NumberingProperties(
            new NumberingLevelReference { Val = 0 },
            new NumberingId { Val = numberingId });
    }

    private static IEnumerable<Run> RunsFor(LetterBlock block, RunProperties? model)
    {
        foreach (var span in block.Spans)
        {
            if (span.Text.Length == 0)
            {
                continue;
            }

            yield return MakeRun(span.Text, span.Bold, model);
        }
    }

    private static Run MakeRun(string text, bool bold, RunProperties? model)
    {
        var run = new Run();
        var properties = model?.CloneNode(true) as RunProperties ?? new RunProperties();
        if (bold)
        {
            properties.Bold = new Bold();
            properties.BoldComplexScript = new BoldComplexScript();
        }
        else
        {
            properties.Bold = null;
            properties.BoldComplexScript = null;
        }

        if (properties.HasChildren)
        {
            run.RunProperties = properties;
        }

        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        return run;
    }

    private static void ClearRuns(Paragraph paragraph)
    {
        foreach (var run in paragraph.Elements<Run>().ToList())
        {
            run.Remove();
        }
    }

    /// <summary>The run holding the character at <paramref name="index"/> of the joined text.</summary>
    private static Run? RunAt(Paragraph paragraph, int index)
    {
        var offset = 0;
        foreach (var text in LetterOpenXml.OwnTexts(paragraph))
        {
            var end = offset + text.Text.Length;
            if (index < end)
            {
                return text.Ancestors<Run>().FirstOrDefault();
            }

            offset = end;
        }

        return paragraph.Elements<Run>().LastOrDefault();
    }
}
