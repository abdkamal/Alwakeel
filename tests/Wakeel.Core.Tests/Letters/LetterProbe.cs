using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Wp = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace Wakeel.Core.Tests.Letters;

/// <summary>
/// Looks inside a composed letter at the things a reader would only see on paper: the margins,
/// how a shape is anchored, whether a word is heavy, how much blank space was left.
/// </summary>
internal static class LetterProbe
{
    /// <summary>The page margins of the first section, in twips.</summary>
    /// <param name="docx">A Word document.</param>
    public static (int Left, int Right, int Top, int Bottom) Margins(byte[] docx)
    {
        using var document = Open(docx);
        var margin = document.MainDocumentPart?.Document?.Body?
            .Descendants<PageMargin>().FirstOrDefault();
        return margin is null
            ? (0, 0, 0, 0)
            : ((int)(margin.Left?.Value ?? 0u),
               (int)(margin.Right?.Value ?? 0u),
               margin.Top?.Value ?? 0,
               margin.Bottom?.Value ?? 0);
    }

    /// <summary>
    /// The shapes still tied to the paper rather than to the margins — which on a page الوكيل
    /// chose the size of would drift out of the writing area.
    /// </summary>
    /// <param name="docx">A Word document.</param>
    public static IReadOnlyList<string> PageAnchoredShapes(byte[] docx)
    {
        using var document = Open(docx);
        var offenders = new List<string>();

        foreach (var part in Parts(document))
        {
            if (part.RootElement is not { } root)
            {
                continue;
            }

            foreach (var anchor in root.Descendants<Wp.Anchor>())
            {
                if (anchor.HorizontalPosition?.RelativeFrom?.Value == Wp.HorizontalRelativePositionValues.Page)
                {
                    offenders.Add("horizontal:page");
                }

                if (anchor.VerticalPosition?.RelativeFrom?.Value == Wp.VerticalRelativePositionValues.Page)
                {
                    offenders.Add("vertical:page");
                }
            }

            foreach (var shape in root.Descendants<DocumentFormat.OpenXml.Vml.Shape>())
            {
                var style = shape.Style?.Value ?? string.Empty;
                if (style.Contains("mso-position-horizontal-relative:page", StringComparison.Ordinal)
                    || style.Contains("mso-position-vertical-relative:page", StringComparison.Ordinal))
                {
                    offenders.Add("vml:page");
                }
            }
        }

        return offenders;
    }

    /// <summary>Whether the document carries numbering definitions of its own.</summary>
    /// <param name="docx">A Word document.</param>
    public static bool HasNumbering(byte[] docx)
    {
        using var document = Open(docx);
        var numbering = document.MainDocumentPart?.NumberingDefinitionsPart?.Numbering;
        if (numbering is null)
        {
            return false;
        }

        var ids = numbering.Elements<NumberingInstance>()
            .Select(n => n.NumberID?.Value)
            .Where(v => v is not null)
            .ToHashSet();

        return document.MainDocumentPart!.Document!.Body!
            .Descendants<NumberingId>()
            .Any(n => n.Val?.Value is { } value && ids.Contains(value));
    }

    /// <summary>
    /// How the first body paragraph whose text contains <paramref name="marker"/> is laid out
    /// across the line, or <c>null</c> when it says nothing about it.
    /// </summary>
    /// <param name="docx">A Word document.</param>
    /// <param name="marker">Text that identifies the paragraph.</param>
    public static string? JustificationOf(byte[] docx, string marker)
    {
        using var document = Open(docx);
        var paragraph = document.MainDocumentPart?.Document?.Body?
            .Descendants<Paragraph>()
            .FirstOrDefault(p => string.Concat(p.Descendants<Text>().Select(t => t.Text))
                .Contains(marker, StringComparison.Ordinal));

        return paragraph?.ParagraphProperties?.Justification?.Val?.Value.ToString();
    }

    /// <summary>
    /// The numbering part's children in the order Word will read them, so a test can say that a
    /// picture bullet still comes before every abstract definition.
    /// </summary>
    /// <param name="docx">A Word document.</param>
    public static IReadOnlyList<string> NumberingPartOrder(byte[] docx)
    {
        using var document = Open(docx);
        var numbering = document.MainDocumentPart?.NumberingDefinitionsPart?.Numbering;
        return numbering is null ? [] : [.. numbering.ChildElements.Select(c => c.LocalName)];
    }

    /// <summary>The bullet marker the document's own definitions print.</summary>
    /// <param name="docx">A Word document.</param>
    public static string? BulletLevelText(byte[] docx)
    {
        using var document = Open(docx);
        var numbering = document.MainDocumentPart?.NumberingDefinitionsPart?.Numbering;
        return numbering?
            .Elements<AbstractNum>()
            .SelectMany(a => a.Elements<Level>())
            .FirstOrDefault(l => l.NumberingFormat?.Val?.Value == NumberFormatValues.Bullet)?
            .LevelText?.Val?.Value;
    }

    /// <summary>Whether some run written heavy carries <paramref name="word"/>.</summary>
    /// <param name="docx">A Word document.</param>
    /// <param name="word">The word to look for.</param>
    public static bool HasBoldRunWith(byte[] docx, string word)
    {
        using var document = Open(docx);
        return Parts(document)
            .Select(p => p.RootElement)
            .OfType<OpenXmlPartRootElement>()
            .SelectMany(root => root.Descendants<Run>())
            .Any(run =>
                run.RunProperties?.Bold is not null
                && string.Concat(run.Elements<Text>().Select(t => t.Text)).Contains(word, StringComparison.Ordinal));
    }

    /// <summary>
    /// How many blank paragraphs follow the first body paragraph whose text contains
    /// <paramref name="marker"/>.
    /// </summary>
    /// <param name="docx">A Word document.</param>
    /// <param name="marker">Text that identifies the paragraph to start counting after.</param>
    public static int BlankParagraphsAfter(byte[] docx, string marker)
    {
        using var document = Open(docx);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return 0;
        }

        var paragraphs = body.Elements<Paragraph>().ToList();
        var index = paragraphs.FindIndex(p => TextOf(p).Contains(marker, StringComparison.Ordinal));
        if (index < 0)
        {
            return 0;
        }

        var blanks = 0;
        for (var i = index + 1; i < paragraphs.Count; i++)
        {
            if (TextOf(paragraphs[i]).Trim().Length > 0)
            {
                break;
            }

            blanks++;
        }

        return blanks;
    }

    /// <summary>How many paragraphs of the body come after a page break.</summary>
    /// <param name="docx">A Word document.</param>
    public static bool HasPageBreak(byte[] docx)
    {
        using var document = Open(docx);
        return document.MainDocumentPart?.Document?.Body?
            .Descendants<Break>()
            .Any(b => b.Type?.Value == BreakValues.Page) == true;
    }

    /// <summary>Every paragraph's text, body and headers alike.</summary>
    /// <param name="docx">A Word document.</param>
    public static IReadOnlyList<string> Lines(byte[] docx)
    {
        using var document = Open(docx);
        var lines = new List<string>();
        foreach (var part in Parts(document))
        {
            if (part.RootElement is { } root)
            {
                lines.AddRange(root.Descendants<Paragraph>().Select(TextOf));
            }
        }

        return lines;
    }

    private static string TextOf(Paragraph paragraph) =>
        string.Concat(paragraph.Descendants<Text>()
            .Where(t => t.Ancestors<Paragraph>().FirstOrDefault() == paragraph)
            .Select(t => t.Text));

    private static IEnumerable<OpenXmlPart> Parts(WordprocessingDocument document)
    {
        var main = document.MainDocumentPart;
        if (main is null)
        {
            yield break;
        }

        yield return main;
        foreach (var header in main.HeaderParts)
        {
            yield return header;
        }

        foreach (var footer in main.FooterParts)
        {
            yield return footer;
        }
    }

    private static WordprocessingDocument Open(byte[] docx) =>
        WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
}
