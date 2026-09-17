using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Wakeel.Core.Tests.Letters;

/// <summary>
/// Small Word files made on the spot, for the cases the owner's own template does not cover: a
/// mark in a header, a mark Word has cut into pieces, a template with a mark nobody knows.
/// </summary>
internal static class TemplateBuilder
{
    /// <summary>A template whose header and body each carry marks.</summary>
    /// <param name="headerLine">The header's line.</param>
    /// <param name="bodyLine">The body's line.</param>
    public static byte[] WithHeader(string headerLine, string bodyLine) =>
        Build(body =>
        {
            body.AppendChild(Paragraph(bodyLine));
        },
        headerLine);

    /// <summary>A template whose only paragraph is built from the given run texts.</summary>
    /// <param name="runTexts">The pieces, in order; together they spell the line.</param>
    public static byte[] WithSplitMark(IReadOnlyList<string> runTexts) =>
        Build(body =>
        {
            var paragraph = new Paragraph();
            foreach (var piece in runTexts)
            {
                paragraph.AppendChild(new Run(new Text(piece) { Space = SpaceProcessingModeValues.Preserve }));
            }

            body.AppendChild(paragraph);
            body.AppendChild(Paragraph("@نص المراسلة"));
        });

    /// <summary>
    /// A template that already carries a numbering part with a picture bullet in it — the shape an
    /// office template takes when somebody once used a picture as a list marker. Word insists that
    /// every picture bullet precede every abstract definition in that part.
    /// </summary>
    /// <param name="firstLine">The opening line.</param>
    /// <param name="bodyLine">The line carrying the body mark.</param>
    public static byte[] WithPictureBullet(string firstLine, string bodyLine) =>
        Build(
            body =>
            {
                body.AppendChild(Paragraph(firstLine));
                body.AppendChild(Paragraph(bodyLine));
            },
            headerLine: null,
            withPictureBullet: true);

    /// <summary>A template made of the given lines, one paragraph each.</summary>
    /// <param name="lines">The lines.</param>
    public static byte[] WithLines(params string[] lines) =>
        Build(body =>
        {
            foreach (var line in lines)
            {
                body.AppendChild(Paragraph(line));
            }
        });

    /// <summary>A template of one line followed by <paramref name="blanks"/> empty paragraphs.</summary>
    /// <param name="line">The line.</param>
    /// <param name="blanks">How many blank paragraphs follow it.</param>
    /// <param name="closing">The closing line under the blanks.</param>
    public static byte[] WithBlanks(string line, int blanks, string closing) =>
        Build(body =>
        {
            body.AppendChild(Paragraph(line));
            for (var i = 0; i < blanks; i++)
            {
                body.AppendChild(new Paragraph());
            }

            body.AppendChild(Paragraph(closing));
        });

    /// <summary>A document of many identical lines, for filling a page up.</summary>
    /// <param name="line">The line to repeat.</param>
    /// <param name="times">How many times.</param>
    public static byte[] Filled(string line, int times) =>
        Build(body =>
        {
            for (var i = 0; i < times; i++)
            {
                body.AppendChild(Paragraph(line));
            }
        });

    private static Paragraph Paragraph(string text) =>
        new(
            new ParagraphProperties { BiDi = new BiDi() },
            new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));

    private static byte[] Build(Action<Body> fill, string? headerLine = null, bool withPictureBullet = false)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            var body = new Body();
            fill(body);

            var section = new SectionProperties(
                new PageSize
                {
                    Width = 11906U,
                    Height = 16838U,
                    Orient = PageOrientationValues.Portrait,
                },
                new PageMargin
                {
                    Top = 1134,
                    Right = 1134,
                    Bottom = 1134,
                    Left = 1134,
                    Header = 567,
                    Footer = 567,
                    Gutter = 0,
                });

            if (headerLine is not null)
            {
                var headerPart = main.AddNewPart<HeaderPart>();
                headerPart.Header = new Header(Paragraph(headerLine));
                headerPart.Header.Save();
                section.PrependChild(new HeaderReference
                {
                    Type = HeaderFooterValues.Default,
                    Id = main.GetIdOfPart(headerPart),
                });
            }

            if (withPictureBullet)
            {
                var numberingPart = main.AddNewPart<NumberingDefinitionsPart>();
                numberingPart.Numbering = new Numbering(
                    new NumberingPictureBullet(new Drawing()) { NumberingPictureBulletId = 0 });
                numberingPart.Numbering.Save();
            }

            body.AppendChild(section);
            main.Document = new Document(body);
            main.Document.Save();
        }

        return stream.ToArray();
    }
}
