using System.Globalization;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Wakeel.Core.Services.Correspondence;
using Vml = DocumentFormat.OpenXml.Vml;
using Wp = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace Wakeel.Reports.Letters;

/// <summary>
/// Reads a template the way a person looking at the printed sheet reads it, for the preview and
/// for the PDF a machine without Word produces (ARCHITECTURE §9, AGREEMENT item 10).
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately not the walk <see cref="LetterTemplateInspector.ReadParagraphs"/> does.
/// That one visits every paragraph of every part, because the composer must fill both copies of a
/// text box — the drawing under <c>mc:Choice</c> and the VML under <c>mc:Fallback</c> — and the
/// check must count what is really in the file. Printing what that walk returns puts every text box
/// on the sheet twice and puts the letterhead under the signature. So this reader:
/// </para>
/// <list type="bullet">
///   <item>skips anything under an <c>mc:Fallback</c>, so each text box is read once;</item>
///   <item>puts the header parts' lines first and the footer parts' last, whatever order the
///     package stores them in;</item>
///   <item>reads a text box where the paragraph that anchors it stands, not after it, because that
///     is where the template draws it;</item>
///   <item>works out from the anchor whether the box sits against the left edge, the right edge or
///     across the writing area, so the date block and the addressee end up on opposite sides of
///     the sheet as they do in Word.</item>
/// </list>
/// </remarks>
internal static class LetterLayoutReader
{
    /// <summary>A box at least this much of the writing area is a band, not a side block.</summary>
    private const double BandShareOfWidth = 0.7;

    /// <summary>A4 in twips, used when a template's section says nothing.</summary>
    private const int DefaultPageWidth = 11906;

    /// <summary>One inch, used when a template's section says nothing about its margins.</summary>
    private const int DefaultMargin = 1440;

    /// <summary>Reads the template, or answers <c>null</c> when it cannot be opened.</summary>
    /// <param name="templateDocx">The template.</param>
    public static List<LetterTemplateLine>? TryRead(ReadOnlySpan<byte> templateDocx)
    {
        if (templateDocx.IsEmpty)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(templateDocx.ToArray(), writable: false);
            using var document = WordprocessingDocument.Open(stream, isEditable: false);
            if (document.MainDocumentPart is not { } main)
            {
                return null;
            }

            var sheet = GeometryOf(main);
            var lines = new List<LetterTemplateLine>();
            var boxes = new Dictionary<OpenXmlElement, int>();

            foreach (var header in main.HeaderParts)
            {
                Read(header, LetterTemplatePlace.Letterhead, sheet, boxes, lines);
            }

            Read(main, LetterTemplatePlace.Flow, sheet, boxes, lines);

            if (main.FootnotesPart is { } footnotes)
            {
                Read(footnotes, LetterTemplatePlace.Flow, sheet, boxes, lines);
            }

            if (main.EndnotesPart is { } endnotes)
            {
                Read(endnotes, LetterTemplatePlace.Flow, sheet, boxes, lines);
            }

            foreach (var footer in main.FooterParts)
            {
                Read(footer, LetterTemplatePlace.Foot, sheet, boxes, lines);
            }

            return lines;
        }
        catch (Exception exception) when (exception is InvalidDataException
                                              or IOException
                                              or NotSupportedException
                                              or OpenXmlPackageException
                                              or ArgumentException
                                              or FileFormatException)
        {
            return null;
        }
    }

    /// <summary>The sheet the template was drawn on, in twips.</summary>
    /// <param name="Width">The paper's width.</param>
    /// <param name="Left">The left margin.</param>
    /// <param name="Right">The right margin.</param>
    private readonly record struct Sheet(int Width, int Left, int Right)
    {
        /// <summary>The width a line of text is allowed.</summary>
        public int TextWidth => Math.Max(1, Width - Left - Right);
    }

    private static Sheet GeometryOf(MainDocumentPart main)
    {
        var section = main.Document?.Body?.Descendants<SectionProperties>().FirstOrDefault();
        var page = section?.GetFirstChild<PageSize>();
        var margin = section?.GetFirstChild<PageMargin>();

        var width = page?.Width?.Value is { } w and > 0 ? (int)w : DefaultPageWidth;
        var left = margin?.Left?.Value is { } l ? (int)l : DefaultMargin;
        var right = margin?.Right?.Value is { } r ? (int)r : DefaultMargin;

        // A margin wider than the paper is not a sheet anyone can write on; fall back rather than
        // divide by a width of nothing further down.
        if (left < 0 || right < 0 || left + right >= width)
        {
            return new Sheet(width, DefaultMargin, DefaultMargin);
        }

        return new Sheet(width, left, right);
    }

    private static void Read(
        OpenXmlPart part,
        LetterTemplatePlace place,
        Sheet sheet,
        Dictionary<OpenXmlElement, int> boxes,
        List<LetterTemplateLine> lines)
    {
        if (part.RootElement is not { } root)
        {
            return;
        }

        foreach (var paragraph in root.Descendants<Paragraph>())
        {
            if (IsInFallback(paragraph) || paragraph.Ancestors<TextBoxContent>().Any())
            {
                // The VML copy of a box says the same words as the drawing copy, and a paragraph
                // inside a box is read with its box, where the box is anchored.
                continue;
            }

            foreach (var box in paragraph.Descendants<TextBoxContent>())
            {
                if (!IsInFallback(box))
                {
                    ReadBox(box, place, sheet, boxes, lines);
                }
            }

            lines.Add(new LetterTemplateLine(
                LetterOpenXml.ReadableTextOf(paragraph),
                place,
                0,
                AlignOf(paragraph)));
        }
    }

    private static void ReadBox(
        TextBoxContent box,
        LetterTemplatePlace place,
        Sheet sheet,
        Dictionary<OpenXmlElement, int> boxes,
        List<LetterTemplateLine> lines)
    {
        var side = place == LetterTemplatePlace.Flow ? SideOf(box, sheet) : place;

        foreach (var paragraph in box.Descendants<Paragraph>())
        {
            if (paragraph.Ancestors<TextBoxContent>().FirstOrDefault() != box)
            {
                continue;
            }

            var text = LetterOpenXml.ReadableTextOf(paragraph);
            if (text.Trim().Length == 0)
            {
                // A box's own empty line is the space inside the box, not a line of the sheet.
                continue;
            }

            var number = 0;
            if (place == LetterTemplatePlace.Flow && !boxes.TryGetValue(box, out number))
            {
                number = boxes.Count + 1;
                boxes[box] = number;
            }

            lines.Add(new LetterTemplateLine(text, side, number, AlignOf(paragraph)));
        }
    }

    /// <summary>Whether this element is the copy Word keeps for a reader too old for drawings.</summary>
    private static bool IsInFallback(OpenXmlElement element) =>
        element.Ancestors<AlternateContentFallback>().Any();

    private static LetterTemplateAlign AlignOf(Paragraph paragraph) =>
        paragraph.ParagraphProperties?.Justification?.Val?.InnerText switch
        {
            "center" => LetterTemplateAlign.Center,
            "both" or "distribute" => LetterTemplateAlign.Justify,
            _ => LetterTemplateAlign.Start,
        };

    /// <summary>Which side of the sheet a floating box sits on.</summary>
    /// <param name="box">The box's content.</param>
    /// <param name="sheet">The sheet it is drawn on.</param>
    private static LetterTemplatePlace SideOf(TextBoxContent box, Sheet sheet)
    {
        if (box.Ancestors<Wp.Anchor>().FirstOrDefault() is { } anchor)
        {
            var width = Twips(anchor.Extent?.Cx?.Value);
            return Classify(LeftOf(anchor, sheet, width), width, sheet);
        }

        if (box.Ancestors<Vml.Shape>().FirstOrDefault()?.Style?.Value is { } style
            && TryReadVml(style, sheet, out var vmlLeft, out var vmlWidth))
        {
            return Classify(vmlLeft, vmlWidth, sheet);
        }

        // A box whose place cannot be read is given the whole width rather than guessed onto a
        // side it may not be on.
        return LetterTemplatePlace.Band;
    }

    private static LetterTemplatePlace Classify(double left, double width, Sheet sheet)
    {
        if (width <= 0 || width >= sheet.TextWidth * BandShareOfWidth)
        {
            return LetterTemplatePlace.Band;
        }

        return left + (width / 2) <= sheet.Width / 2.0
            ? LetterTemplatePlace.LeftBlock
            : LetterTemplatePlace.RightBlock;
    }

    /// <summary>Where a drawing anchor puts the box's left edge, measured from the paper's edge.</summary>
    private static double LeftOf(Wp.Anchor anchor, Sheet sheet, double width)
    {
        var position = anchor.HorizontalPosition;
        var relative = position?.RelativeFrom?.InnerText ?? "column";

        var (areaStart, areaEnd) = relative switch
        {
            "page" or "leftMargin" => (0.0, (double)sheet.Width),
            "rightMargin" or "outsideMargin" => (sheet.Width - (double)sheet.Right, (double)sheet.Width),
            _ => ((double)sheet.Left, sheet.Width - (double)sheet.Right),
        };

        if (position?.HorizontalAlignment is { } alignment)
        {
            return alignment.Text.Trim() switch
            {
                "right" or "outside" => areaEnd - width,
                "center" => (areaStart + areaEnd - width) / 2,
                _ => areaStart,
            };
        }

        if (position?.PositionOffset is { } offset
            && long.TryParse(offset.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var emu))
        {
            return areaStart + ((double)emu / LetterPageLayout.EmuPerTwip);
        }

        return areaStart;
    }

    private static double Twips(long? emu) =>
        emu is { } value ? (double)value / LetterPageLayout.EmuPerTwip : 0;

    /// <summary>
    /// Where the VML copy of a shape puts itself. Only a template that carries no drawing copy at
    /// all is read this way — an older Word, or a file written by something that is not Word.
    /// </summary>
    /// <param name="style">The shape's style string.</param>
    /// <param name="sheet">The sheet it is drawn on.</param>
    /// <param name="left">The box's left edge in twips, measured from the paper's edge.</param>
    /// <param name="width">The box's width in twips.</param>
    private static bool TryReadVml(string style, Sheet sheet, out double left, out double width)
    {
        left = 0;
        width = 0;

        double? distance = null;
        double? size = null;
        var relative = "column";

        foreach (var declaration in style.Split(';'))
        {
            var colon = declaration.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0)
            {
                continue;
            }

            var name = declaration[..colon].Trim();
            var value = declaration[(colon + 1)..].Trim();

            switch (name)
            {
                case "mso-position-horizontal-relative":
                    relative = value;
                    break;
                case "left":
                case "margin-left":
                    if (TryLength(value, out var parsedLeft))
                    {
                        distance = parsedLeft;
                    }

                    break;
                case "width":
                    if (TryLength(value, out var parsedWidth))
                    {
                        size = parsedWidth;
                    }

                    break;
                default:
                    break;
            }
        }

        if (distance is not { } from || size is not { } across)
        {
            return false;
        }

        left = (relative is "page" or "left-margin" ? 0 : sheet.Left) + from;
        width = across;
        return true;
    }

    /// <summary>Reads a VML distance such as "-68.5pt" as twips.</summary>
    private static bool TryLength(string written, out double twips)
    {
        twips = 0;

        var end = written.Length;
        while (end > 0 && !char.IsAsciiDigit(written[end - 1]) && written[end - 1] != '.')
        {
            end--;
        }

        var unit = written[end..].Trim();
        if (!double.TryParse(
                written[..end].Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var amount))
        {
            return false;
        }

        var perUnit = unit switch
        {
            "pt" or "" => 20.0,
            "in" => LetterPageLayout.TwipsPerInch,
            "cm" => LetterPageLayout.TwipsPerInch / 2.54,
            "mm" => LetterPageLayout.TwipsPerInch / 25.4,
            "pc" => 240.0,
            "px" => LetterPageLayout.TwipsPerInch / 96.0,
            _ => 0.0,
        };

        if (perUnit <= 0)
        {
            return false;
        }

        twips = amount * perUnit;
        return true;
    }
}
