using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Wakeel.Core.Services;
using Wakeel.Core.Services.Correspondence;

namespace Wakeel.Reports.Letters;

/// <inheritdoc cref="ILetterComposer"/>
/// <remarks>
/// <para>
/// One letter is one pass over a copy of the office's template (AGREEMENT item 57):
/// </para>
/// <list type="number">
///   <item>the page is set to A4 or A5 and the floating shapes re-anchored to the margins;</item>
///   <item>each paragraph — of the body, of every header and footer, of every table cell and of
///     both copies of every text box — has its runs joined, then its eight simple marks
///     replaced;</item>
///   <item>the body mark is replaced last, because it is the only one that brings paragraphs and
///     lists with it and therefore changes the shape of the document around it.</item>
/// </list>
/// <para>
/// A mark the letter has no value for is removed rather than left standing: the signed letter
/// never shows a mark. An unknown mark is not touched — it is not الوكيل's to guess at, and the
/// check on upload already warned about it.
/// </para>
/// </remarks>
public sealed class LetterComposer : ILetterComposer
{
    private readonly ILetterTemplateInspector _inspector;

    /// <summary>Creates the composer.</summary>
    /// <param name="inspector">Reads the template's fixed phrases for the HTML rendering.</param>
    public LetterComposer(ILetterTemplateInspector? inspector = null) =>
        _inspector = inspector ?? new LetterTemplateInspector();

    /// <inheritdoc />
    public byte[] Compose(ReadOnlySpan<byte> templateDocx, LetterData letter)
    {
        ArgumentNullException.ThrowIfNull(letter);
        if (templateDocx.IsEmpty)
        {
            throw new ArgumentException("A letter template is required.", nameof(templateDocx));
        }

        using var stream = new MemoryStream();
        stream.Write(templateDocx);
        stream.Position = 0;

        using (var document = WordprocessingDocument.Open(stream, isEditable: true))
        {
            Fill(document, letter);
        }

        return stream.ToArray();
    }

    /// <inheritdoc />
    public string RenderHtml(ReadOnlySpan<byte> templateDocx, LetterData letter)
    {
        ArgumentNullException.ThrowIfNull(letter);
        var layout = templateDocx.IsEmpty
            ? []
            : _inspector.ReadLayout(templateDocx);
        return LetterHtmlRenderer.Render(layout, letter);
    }

    /// <summary>The values of the eight simple marks for one letter.</summary>
    /// <param name="letter">The letter.</param>
    internal static Dictionary<string, string> SimpleValues(LetterData letter) => new(StringComparer.Ordinal)
    {
        [LetterMarks.HijriDate] = LetterDates.Hijri(letter.Date),
        [LetterMarks.GregorianDate] = LetterDates.Gregorian(letter.Date),
        [LetterMarks.Number] = string.IsNullOrWhiteSpace(letter.NumberAr)
            ? CoreAr.Letter.DraftNumber
            : letter.NumberAr!.Trim(),
        [LetterMarks.RecipientHead] = letter.RecipientHeadNameAr?.Trim() ?? string.Empty,
        [LetterMarks.RecipientOffice] = letter.RecipientOfficeNameAr?.Trim() ?? string.Empty,
        [LetterMarks.Subject] = letter.SubjectAr?.Trim() ?? string.Empty,
        [LetterMarks.Sender] = letter.SenderNameAr?.Trim() ?? string.Empty,
        [LetterMarks.SenderOffice] = letter.SenderOfficeNameAr?.Trim() ?? string.Empty,
    };

    /// <summary>Fills an already-open document; shared with the derived-document builder.</summary>
    /// <param name="document">The open copy of the template.</param>
    /// <param name="letter">The letter's data.</param>
    internal static void Fill(WordprocessingDocument document, LetterData letter)
    {
        LetterPageLayout.Apply(document, letter.PageSize);

        var values = SimpleValues(letter);
        var main = document.MainDocumentPart;

        foreach (var part in LetterOpenXml.TextParts(document))
        {
            var inMainPart = ReferenceEquals(part, main);

            // Where the body goes is decided while the template still says only what its author
            // typed. A letter's own data — a subject, an office name — may well contain the words
            // of the body mark, and a paragraph that holds such a value is not a place the letter
            // was meant to be poured into.
            var bodyParagraphs = new List<Paragraph>();
            foreach (var paragraph in LetterOpenXml.Paragraphs(part).ToList())
            {
                LetterOpenXml.MergeRuns(paragraph);
                if (LetterOpenXml.TextOf(paragraph).Contains(LetterMarks.Body, StringComparison.Ordinal))
                {
                    bodyParagraphs.Add(paragraph);
                }

                LetterOpenXml.ReplaceAll(paragraph, values);
            }

            foreach (var paragraph in bodyParagraphs)
            {
                // Once per paragraph, and never again into what was just written: the letter's own
                // text may well contain the words of the mark — the head of office explaining in
                // the editor where «@نص المراسلة» goes, a subject line quoting it — and reading
                // the paragraph again afterwards would insert the whole letter inside itself. A
                // template that carries a second body mark further down is still reached, because
                // that paragraph is its own entry in the list this walk was given.
                var text = LetterOpenXml.TextOf(paragraph);
                var at = text.IndexOf(LetterMarks.Body, StringComparison.Ordinal);
                if (at >= 0)
                {
                    var last = LetterBodyWriter.Insert(document, inMainPart, paragraph, at, letter.Body);

                    // A line that writes the mark twice gets the letter once, and the marks that
                    // were not used are taken out: the signed letter never shows a mark
                    // (AGREEMENT item 57). They can only be in the tail that was carried over, so
                    // the paragraph the tail landed in is the one to clear.
                    RemoveMark(last, LetterMarks.Body);
                }
            }
        }

        Save(document);
    }

    /// <summary>
    /// Takes every remaining occurrence of one mark out of a paragraph, leaving the words around
    /// it exactly as they stood.
    /// </summary>
    /// <param name="paragraph">The paragraph to clear.</param>
    /// <param name="mark">The mark to remove.</param>
    private static void RemoveMark(Paragraph paragraph, string mark)
    {
        string[] marks = [mark];
        while (LetterOpenXml.FindFirst(paragraph, marks) is { } hit)
        {
            LetterOpenXml.Splice(paragraph, hit.Index, mark.Length, string.Empty);
        }
    }

    /// <summary>Writes every part back, so nothing is left only in memory.</summary>
    private static void Save(WordprocessingDocument document)
    {
        document.MainDocumentPart?.Document?.Save();
        if (document.MainDocumentPart is not { } main)
        {
            return;
        }

        foreach (var header in main.HeaderParts)
        {
            header.Header?.Save();
        }

        foreach (var footer in main.FooterParts)
        {
            footer.Footer?.Save();
        }

        main.FootnotesPart?.Footnotes?.Save();
        main.EndnotesPart?.Endnotes?.Save();
        main.NumberingDefinitionsPart?.Numbering?.Save();
    }

    /// <summary>
    /// The plain text of a composed letter, part by part, for the conformance test and for the
    /// search index.
    /// </summary>
    /// <param name="docx">A composed letter.</param>
    public static IReadOnlyList<string> ReadLines(ReadOnlySpan<byte> docx)
    {
        using var stream = new MemoryStream(docx.ToArray(), writable: false);
        using var document = WordprocessingDocument.Open(stream, isEditable: false);
        var lines = new List<string>();
        foreach (var part in LetterOpenXml.TextParts(document))
        {
            foreach (var paragraph in LetterOpenXml.Paragraphs(part))
            {
                lines.Add(LetterOpenXml.TextOf(paragraph));
            }
        }

        return lines;
    }

    /// <summary>The page the composed letter is set to, read back from its first section.</summary>
    /// <param name="docx">A composed letter.</param>
    public static (int Width, int Height) ReadPageSize(ReadOnlySpan<byte> docx)
    {
        using var stream = new MemoryStream(docx.ToArray(), writable: false);
        using var document = WordprocessingDocument.Open(stream, isEditable: false);
        var page = document.MainDocumentPart?.Document?.Body?.Descendants<PageSize>().FirstOrDefault();
        return page is null
            ? (0, 0)
            : ((int)(page.Width?.Value ?? 0u), (int)(page.Height?.Value ?? 0u));
    }
}
