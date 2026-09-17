using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Wakeel.Core.Services.Correspondence;
using Wp = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace Wakeel.Reports.Letters;

/// <summary>
/// Sets the generated letter's page (AGREEMENT item 57): A4 by default, A5 when the outgoing
/// wizard asked for the half sheet, written into the section's <c>pgSz</c> and <c>pgMar</c>
/// rather than expected from the template.
/// </summary>
/// <remarks>
/// Because الوكيل — not the template — decides the page, anything the template positions must be
/// tied to the margins and not to the paper: a text box anchored 3 cm from the right edge of A4
/// would sit 3 cm from the right edge of A5 too, which on the smaller sheet is halfway across the
/// writing area. Every floating shape is therefore re-anchored to the margin, its offset shifted
/// by exactly the margin it used to include so that nothing moves on A4, and the A5 margins are
/// scaled with the paper so the same shape lands in the same place proportionally.
/// </remarks>
public static class LetterPageLayout
{
    /// <summary>A4 width in twentieths of a point (210 mm).</summary>
    public const int A4WidthTwips = 11906;

    /// <summary>A4 height in twentieths of a point (297 mm).</summary>
    public const int A4HeightTwips = 16838;

    /// <summary>A5 width in twentieths of a point (148 mm).</summary>
    public const int A5WidthTwips = 8419;

    /// <summary>A5 height in twentieths of a point (210 mm).</summary>
    public const int A5HeightTwips = 11906;

    /// <summary>English Metric Units per twip, the unit a drawing anchor is written in.</summary>
    public const int EmuPerTwip = 635;

    /// <summary>The smallest margin the scaler will leave, one centimetre.</summary>
    private const int MinimumMarginTwips = 567;

    /// <summary>The page in twips.</summary>
    /// <param name="size">A4 or A5.</param>
    public static (int Width, int Height) Size(LetterPageSize size) =>
        size == LetterPageSize.A5 ? (A5WidthTwips, A5HeightTwips) : (A4WidthTwips, A4HeightTwips);

    /// <summary>Twips in an inch — the unit a printer's page setup is written in.</summary>
    public const int TwipsPerInch = 1440;

    /// <summary>
    /// The same page in inches, which is how a print job is told what paper to use. The letter
    /// printed without Word goes through the browser engine, and a browser ignores the page size
    /// written in the document's own style sheet unless the print settings carry it too — so the
    /// PDF a Word-less machine produces is put on this paper explicitly (ARCHITECTURE §9).
    /// </summary>
    /// <param name="size">A4 or A5.</param>
    public static (double Width, double Height) SizeInInches(LetterPageSize size)
    {
        var (width, height) = Size(size);
        return ((double)width / TwipsPerInch, (double)height / TwipsPerInch);
    }

    /// <summary>
    /// Applies <paramref name="size"/> to every section of the document and re-anchors the
    /// floating shapes to the margins.
    /// </summary>
    /// <param name="document">The open letter.</param>
    /// <param name="size">The page asked for.</param>
    public static void Apply(WordprocessingDocument document, LetterPageSize size)
    {
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return;
        }

        var sections = body.Descendants<SectionProperties>().ToList();
        if (sections.Count == 0)
        {
            var created = new SectionProperties();
            body.AppendChild(created);
            sections.Add(created);
        }

        var (width, height) = Size(size);

        // Once for the whole file, against the first section's margins: shifting an offset twice
        // would move the shape twice.
        var reference = sections[0].GetFirstChild<PageMargin>();
        ReanchorShapes(
            document,
            reference is null ? 1134 : LeftOf(reference),
            (int)(reference?.Top?.Value ?? 1134));

        foreach (var section in sections)
        {
            var page = section.GetFirstChild<PageSize>();
            if (page is null)
            {
                page = new PageSize();
                section.PrependChild(page);
            }

            var oldWidth = page.Width?.Value is { } w ? (int)w : A4WidthTwips;
            var oldHeight = page.Height?.Value is { } h ? (int)h : A4HeightTwips;

            var margin = section.GetFirstChild<PageMargin>();
            if (margin is null)
            {
                margin = new PageMargin
                {
                    Top = 1134,
                    Right = 1134,
                    Bottom = 1134,
                    Left = 1134,
                    Header = 567,
                    Footer = 567,
                    Gutter = 0,
                };
                section.InsertAfter(margin, page);
            }

            page.Width = (uint)width;
            page.Height = (uint)height;
            page.Orient = PageOrientationValues.Portrait;

            ScaleMargins(margin, oldWidth, oldHeight, width, height);
        }
    }

    private static int LeftOf(PageMargin margin) => (int)(margin.Left?.Value ?? 1134u);

    private static void ScaleMargins(PageMargin margin, int oldWidth, int oldHeight, int newWidth, int newHeight)
    {
        if (oldWidth <= 0 || oldHeight <= 0 || (oldWidth == newWidth && oldHeight == newHeight))
        {
            return;
        }

        margin.Left = Scale(margin.Left?.Value, oldWidth, newWidth);
        margin.Right = Scale(margin.Right?.Value, oldWidth, newWidth);
        margin.Gutter = Scale(margin.Gutter?.Value, oldWidth, newWidth);
        margin.Top = ScaleSigned(margin.Top?.Value, oldHeight, newHeight);
        margin.Bottom = ScaleSigned(margin.Bottom?.Value, oldHeight, newHeight);
        margin.Header = Scale(margin.Header?.Value, oldHeight, newHeight);
        margin.Footer = Scale(margin.Footer?.Value, oldHeight, newHeight);
    }

    private static uint Scale(uint? value, int from, int to)
    {
        if (value is not { } original || from <= 0)
        {
            return value ?? 0u;
        }

        var scaled = (int)Math.Round(original * (double)to / from, MidpointRounding.AwayFromZero);
        return (uint)Math.Max(scaled, original == 0 ? 0 : Math.Min((int)original, MinimumMarginTwips));
    }

    private static int ScaleSigned(int? value, int from, int to)
    {
        if (value is not { } original || from <= 0)
        {
            return value ?? 0;
        }

        var scaled = (int)Math.Round(original * (double)to / from, MidpointRounding.AwayFromZero);
        if (original <= 0)
        {
            return scaled;
        }

        return Math.Max(scaled, Math.Min(original, MinimumMarginTwips));
    }

    /// <summary>
    /// Moves every floating shape from "so far from the paper's edge" to "so far from the
    /// margin", keeping it where it is: the offset loses exactly the margin it used to include.
    /// </summary>
    private static void ReanchorShapes(WordprocessingDocument document, int leftMarginTwips, int topMarginTwips)
    {
        foreach (var part in LetterOpenXml.TextParts(document))
        {
            if (part.RootElement is not { } root)
            {
                continue;
            }

            foreach (var anchor in root.Descendants<Wp.Anchor>())
            {
                ReanchorHorizontal(anchor, leftMarginTwips);
                ReanchorVertical(anchor, topMarginTwips);
            }

            foreach (var shape in root.Descendants<DocumentFormat.OpenXml.Vml.Shape>())
            {
                shape.Style = ReanchorVmlStyle(shape.Style?.Value, leftMarginTwips, topMarginTwips);
            }
        }
    }

    private static void ReanchorHorizontal(Wp.Anchor anchor, int leftMarginTwips)
    {
        var position = anchor.HorizontalPosition;
        if (position is null || position.RelativeFrom is null)
        {
            return;
        }

        var relative = position.RelativeFrom.Value;
        if (relative != Wp.HorizontalRelativePositionValues.Page)
        {
            return;
        }

        if (position.PositionOffset is { } offset
            && long.TryParse(offset.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var emu))
        {
            offset.Text = (emu - ((long)leftMarginTwips * EmuPerTwip))
                .ToString(CultureInfo.InvariantCulture);
        }

        position.RelativeFrom = Wp.HorizontalRelativePositionValues.Margin;
    }

    private static void ReanchorVertical(Wp.Anchor anchor, int topMarginTwips)
    {
        var position = anchor.VerticalPosition;
        if (position is null || position.RelativeFrom is null)
        {
            return;
        }

        var relative = position.RelativeFrom.Value;
        if (relative != Wp.VerticalRelativePositionValues.Page)
        {
            return;
        }

        if (position.PositionOffset is { } offset
            && long.TryParse(offset.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var emu))
        {
            offset.Text = (emu - ((long)topMarginTwips * EmuPerTwip))
                .ToString(CultureInfo.InvariantCulture);
        }

        position.RelativeFrom = Wp.VerticalRelativePositionValues.Margin;
    }

    /// <summary>
    /// The VML copy of the same shape says where it sits in a CSS-like style string. An axis
    /// measured from the paper's edge is moved to the margin exactly as the modern copy's anchor
    /// is: the "relative" declaration changes AND the distance loses the margin it used to
    /// include, otherwise a Word old enough to read the fallback draws the letterhead a whole
    /// margin out of place. An axis whose distance cannot be read — an unfamiliar unit, a value
    /// that is not a number — is left measured from the paper rather than moved blindly.
    /// </summary>
    /// <param name="style">The style string, or <c>null</c>.</param>
    /// <param name="leftMarginTwips">The new left margin.</param>
    /// <param name="topMarginTwips">The new top margin.</param>
    internal static StringValue? ReanchorVmlStyle(string? style, int leftMarginTwips, int topMarginTwips)
    {
        if (string.IsNullOrEmpty(style))
        {
            return style is null ? null : new StringValue(style);
        }

        var parts = style.Split(';');

        var horizontalRelative = -1;
        var verticalRelative = -1;
        var left = -1;
        var top = -1;

        for (var i = 0; i < parts.Length; i++)
        {
            var colon = parts[i].IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            switch (parts[i][..colon].Trim())
            {
                case "mso-position-horizontal-relative":
                    horizontalRelative = i;
                    break;
                case "mso-position-vertical-relative":
                    verticalRelative = i;
                    break;
                case "left":
                case "margin-left":
                    left = i;
                    break;
                case "top":
                case "margin-top":
                    top = i;
                    break;
                default:
                    break;
            }
        }

        var moved = new List<string>();
        MoveAxis(parts, moved, horizontalRelative, left, leftMarginTwips, "mso-position-horizontal-relative", "left");
        MoveAxis(parts, moved, verticalRelative, top, topMarginTwips, "mso-position-vertical-relative", "top");

        return new StringValue(string.Join(';', parts.Concat(moved)));
    }

    /// <summary>Moves one axis of a VML shape from the paper's edge to the margin.</summary>
    /// <param name="parts">The style's declarations.</param>
    /// <param name="added">Declarations to append, for a shape that named no distance at all.</param>
    /// <param name="relativeAt">Where the "relative" declaration sits, or -1.</param>
    /// <param name="distanceAt">Where "left"/"margin-left" or "top"/"margin-top" sits, or -1.</param>
    /// <param name="marginTwips">The margin to take off.</param>
    /// <param name="name">The name of the "relative" declaration.</param>
    /// <param name="distanceName">What to call the distance when the shape named none.</param>
    private static void MoveAxis(
        string[] parts,
        List<string> added,
        int relativeAt,
        int distanceAt,
        int marginTwips,
        string name,
        string distanceName)
    {
        if (relativeAt < 0)
        {
            return;
        }

        var value = parts[relativeAt][(parts[relativeAt].IndexOf(':') + 1)..].Trim();

        // "column" already means "from where the text starts", which is the margin: the
        // declaration is renamed and the distance stays as it is.
        if (value == "column")
        {
            parts[relativeAt] = name + ":margin";
            return;
        }

        if (value != "page")
        {
            return;
        }

        // A shape that names no distance on this axis sits against the paper's edge, so measured
        // from the margin it sits one whole margin before it — which is written out, rather than
        // left for Word to read as zero and draw the letterhead a margin out of place.
        if (distanceAt < 0)
        {
            added.Add(distanceName + ":" + Points(-marginTwips));
            parts[relativeAt] = name + ":margin";
            return;
        }

        var declaration = parts[distanceAt];
        var colon = declaration.IndexOf(':');
        var written = declaration[(colon + 1)..].Trim();
        if (!TryShift(written, marginTwips, out var shifted))
        {
            // The distance is written in something this cannot read; the shape keeps the paper it
            // was measured from rather than being moved blindly.
            return;
        }

        parts[distanceAt] = declaration[..colon] + ":" + shifted;
        parts[relativeAt] = name + ":margin";
    }

    /// <summary>Twips as VML's usual unit.</summary>
    /// <param name="twips">The distance.</param>
    private static string Points(int twips) =>
        (twips / 20.0).ToString("0.####", CultureInfo.InvariantCulture) + "pt";

    /// <summary>
    /// Takes <paramref name="marginTwips"/> off a VML distance, keeping the unit it was written
    /// in. Answers <c>false</c> for anything it does not recognise.
    /// </summary>
    /// <param name="written">The distance as the template wrote it, such as "56.7pt".</param>
    /// <param name="marginTwips">The margin to take off.</param>
    /// <param name="moved">The distance rewritten in the same unit.</param>
    private static bool TryShift(string written, int marginTwips, out string moved)
    {
        moved = written;

        var end = written.Length;
        while (end > 0 && !char.IsDigit(written[end - 1]) && written[end - 1] != '.')
        {
            end--;
        }

        var unit = written[end..].Trim();
        var number = written[..end].Trim();
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
        {
            return false;
        }

        // Twips in one of each unit VML is allowed to use.
        var perUnit = unit switch
        {
            "pt" => 20.0,
            "in" => TwipsPerInch,
            "cm" => TwipsPerInch / 2.54,
            "mm" => TwipsPerInch / 25.4,
            "pc" => 240.0,
            "px" => TwipsPerInch / 96.0,
            _ => 0.0,
        };

        if (perUnit <= 0)
        {
            return false;
        }

        var shifted = amount - (marginTwips / perUnit);
        moved = shifted.ToString("0.####", CultureInfo.InvariantCulture) + unit;
        return true;
    }
}
