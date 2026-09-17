using DocumentFormat.OpenXml;
using System.IO;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Wakeel.Core.Services;
using Wakeel.Core.Services.Correspondence;

namespace Wakeel.Reports.Letters;

/// <inheritdoc cref="IDerivedDocumentBuilder"/>
/// <remarks>
/// <para>
/// AGREEMENT item 31: a referral is written on a copy for the file, never on the letter itself.
/// The copy is the original document with the referral added in the free area at the foot of its
/// last page — where a referral is written by hand — or, when there is not room, on a page added
/// after it. The user is told which before saving, which is what <see cref="PreviewAsync"/> is
/// for: a print that quietly grew a page is a print somebody has to explain.
/// </para>
/// <para>
/// The original document is opened from the vault, copied in memory, and the copy is what is
/// changed; the stored original is never written to. When the correspondence has no document of
/// its own — a letter registered from a telephone call, say — the copy is a referral sheet
/// standing on its own, and that always counts as an added page.
/// </para>
/// </remarks>
public sealed class DerivedDocumentBuilder : IDerivedDocumentBuilder
{
    /// <summary>The media type of a Word document.</summary>
    public const string WordMediaType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <summary>The gap left between the letter and the referral written under it, in twips.</summary>
    private const int GapTwips = 360;

    private readonly ILetterDocumentStore _documents;

    /// <summary>Creates the builder.</summary>
    /// <param name="documents">The vault the original is read from and the copy written to.</param>
    public DerivedDocumentBuilder(ILetterDocumentStore documents) =>
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));

    /// <inheritdoc />
    public async Task<DerivedDocumentResult> BuildAsync(
        DerivedDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var original = await ReadOriginalAsync(request, cancellationToken).ConfigureAwait(false);
        var (content, extraPage) = Build(original, request);
        var documentId = await _documents
            .SaveAsync(content, FileNameFor(request), WordMediaType, cancellationToken)
            .ConfigureAwait(false);

        return new DerivedDocumentResult(documentId, extraPage, Notice(extraPage));
    }

    /// <inheritdoc />
    public async Task<DerivedDocumentPreview> PreviewAsync(
        DerivedDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var original = await ReadOriginalAsync(request, cancellationToken).ConfigureAwait(false);
        var extraPage = original is null || !Fits(original, request);
        return new DerivedDocumentPreview(extraPage, Notice(extraPage));
    }

    /// <summary>The sentence shown before saving.</summary>
    /// <param name="extraPage">Whether a page had to be added.</param>
    internal static string Notice(bool extraPage) =>
        extraPage ? CoreAr.Letter.ReferralOnAddedPage : CoreAr.Letter.ReferralOnLastPage;

    /// <summary>The referral, line by line, as it is written on the copy.</summary>
    /// <param name="request">The referral.</param>
    internal static IReadOnlyList<string> Lines(DerivedDocumentRequest request)
    {
        var lines = new List<string> { CoreAr.Letter.ReferralHeading };
        if (!string.IsNullOrWhiteSpace(request.ToAr))
        {
            lines.Add(CoreAr.Letter.ReferralTo(request.ToAr!.Trim()));
        }

        if (request.DueAt is { } due)
        {
            lines.Add(CoreAr.Letter.ReferralDue(LetterDates.Both(due)));
        }

        foreach (var line in (request.ReferralTextAr ?? string.Empty)
                     .Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Replace('\r', '\n')
                     .Split('\n'))
        {
            if (line.Trim().Length > 0)
            {
                lines.Add(line.Trim());
            }
        }

        return lines;
    }

    private async Task<byte[]?> ReadOriginalAsync(
        DerivedDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SourceDocumentId is not { } id || id == Guid.Empty)
        {
            return null;
        }

        var content = await _documents.ReadAsync(id, cancellationToken).ConfigureAwait(false);
        return content is { Length: > 0 } && IsWordDocument(content) ? content : null;
    }

    private static string FileNameFor(DerivedDocumentRequest request) =>
        $"referral-{request.ReferralId:N}.docx";

    private static bool IsWordDocument(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var document = WordprocessingDocument.Open(stream, isEditable: false);
            return document.MainDocumentPart?.Document?.Body is not null;
        }
        catch (Exception exception) when (exception is InvalidDataException
                                              or IOException
                                              or NotSupportedException
                                              or OpenXmlPackageException
                                              or ArgumentException
                                              or FileFormatException)
        {
            return false;
        }
    }

    /// <summary>Whether the referral fits under the letter on its last page.</summary>
    private static bool Fits(byte[] original, DerivedDocumentRequest request)
    {
        using var stream = new MemoryStream(original, writable: false);
        using var document = WordprocessingDocument.Open(stream, isEditable: false);
        var fit = LetterPageFitter.Measure(document);
        var needed = LetterPageFitter.HeightOf(Lines(request), fit) + GapTwips;
        return needed <= fit.FreeOnLastPageTwips;
    }

    /// <summary>Produces the print copy and says whether a page had to be added.</summary>
    private static (byte[] Content, bool ExtraPage) Build(byte[]? original, DerivedDocumentRequest request)
    {
        if (original is null)
        {
            return (StandaloneSheet(request), true);
        }

        using var stream = new MemoryStream();
        stream.Write(original, 0, original.Length);
        stream.Position = 0;

        bool extraPage;
        using (var document = WordprocessingDocument.Open(stream, isEditable: true))
        {
            var body = document.MainDocumentPart!.Document!.Body!;
            var fit = LetterPageFitter.Measure(document);
            var needed = LetterPageFitter.HeightOf(Lines(request), fit) + GapTwips;
            extraPage = needed > fit.FreeOnLastPageTwips;

            var section = body.Elements<SectionProperties>().LastOrDefault();
            foreach (var paragraph in ReferralParagraphs(request, extraPage))
            {
                if (section is null)
                {
                    body.AppendChild(paragraph);
                }
                else
                {
                    body.InsertBefore(paragraph, section);
                }
            }

            document.MainDocumentPart.Document.Save();
        }

        return (stream.ToArray(), extraPage);
    }

    private static IEnumerable<Paragraph> ReferralParagraphs(DerivedDocumentRequest request, bool extraPage)
    {
        var lines = Lines(request);

        var first = new Paragraph();
        var properties = new ParagraphProperties
        {
            BiDi = new BiDi(),
            SpacingBetweenLines = new SpacingBetweenLines { Before = GapTwips.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        };
        if (extraPage)
        {
            // A page is added rather than the text spilling over a page break mid-sentence.
            first.AppendChild(new Run(new Break { Type = BreakValues.Page }));
            properties.SpacingBetweenLines = new SpacingBetweenLines { Before = "0" };
        }

        first.ParagraphProperties = properties;
        first.AppendChild(Heading(lines[0]));
        yield return first;

        foreach (var line in lines.Skip(1))
        {
            yield return Line(line);
        }
    }

    private static Run Heading(string text) =>
        new(
            new RunProperties(new Bold(), new BoldComplexScript()),
            new Text(text) { Space = SpaceProcessingModeValues.Preserve });

    private static Paragraph Line(string text) =>
        new(
            new ParagraphProperties { BiDi = new BiDi() },
            new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));

    /// <summary>
    /// A referral sheet on its own, for a correspondence that has no document to copy.
    /// </summary>
    private static byte[] StandaloneSheet(DerivedDocumentRequest request)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            var body = new Body();
            body.AppendChild(new Paragraph(
                new ParagraphProperties { BiDi = new BiDi() },
                Heading(CoreAr.Letter.ReferralHeading)));

            foreach (var line in Lines(request).Skip(1))
            {
                body.AppendChild(Line(line));
            }

            body.AppendChild(new SectionProperties(
                new PageSize
                {
                    Width = (uint)LetterPageLayout.A4WidthTwips,
                    Height = (uint)LetterPageLayout.A4HeightTwips,
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
                },
                new BiDi()));

            main.Document = new Document(body);
            main.Document.Save();
        }

        return stream.ToArray();
    }
}
