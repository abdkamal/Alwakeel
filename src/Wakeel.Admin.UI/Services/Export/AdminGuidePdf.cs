using System.Globalization;
using System.Text;

namespace Wakeel.Admin.UI.Services.Export;

/// <summary>
/// The provisional guide a setup file carries (AGREEMENT item 29): one A4 page, written by the tool
/// itself, that tells whoever receives the file what it is and what to do with it.
/// </summary>
/// <remarks>
/// <para>
/// The finished illustrated guide is authored later and ships as <c>docs/guide/دليل-الوكيل.pdf</c>.
/// Until that file exists the export still has to put something in the guide's place, because a
/// person who has just been handed a strange file and a password deserves a page they can read. So
/// this writes one: the organisation's name, what the file is, and the four steps of a first run.
/// </para>
/// <para>
/// It is a whole PDF written by hand — a page, a content stream, and the product's own Arabic face
/// embedded so the page reads the same on a computer with no Arabic typeface installed. Nothing is
/// generated under <c>docs/</c>; the page is written into the tool's own exports folder and put into
/// the setup file from there.
/// </para>
/// </remarks>
public static class AdminGuidePdf
{
    /// <summary>The name the guide is written under, inside the tool's exports folder.</summary>
    public const string FileName = "دليل-الوكيل-مبدئي.pdf";

    /// <summary>A4, in the points a PDF counts in.</summary>
    private const double PageWidth = 595;

    private const double PageHeight = 842;

    private const double Margin = 56;

    /// <summary>Writes the one page guide and returns its bytes.</summary>
    /// <param name="orgName">The organisation the file belongs to.</param>
    /// <param name="officeName">The office the setup file is for.</param>
    /// <param name="writtenAt">The day the page is dated.</param>
    public static byte[] Build(string orgName, string officeName, DateTimeOffset writtenAt) =>
        Write(Lines(orgName, officeName, writtenAt), PdfFontFile.Shipped());

    /// <summary>
    /// The page's own words, in the order they are drawn. Public so a test can hold every one of
    /// them against the face the tool ships: a letter or a mark the face has no glyph for is simply
    /// not drawn, and a page missing a character is a page nobody notices is wrong.
    /// </summary>
    public static IReadOnlyList<string> Text(string orgName, string officeName, DateTimeOffset writtenAt) =>
        [.. Lines(orgName, officeName, writtenAt).Select(line => line.Text)];

    private static List<(string Text, double Size, double SpaceAfter)> Lines(
        string orgName,
        string officeName,
        DateTimeOffset writtenAt)
    {
        // The day is written with dashes rather than the tool's usual slashes because the Arabic
        // face the page embeds carries no solidus; every other mark on this page is one it has.
        var day = writtenAt.ToLocalTime().ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

        return
        [
            ("دليل الوكيل «نسخة مبدئية»", 21, 10),
            (string.IsNullOrWhiteSpace(orgName) ? "الهيئة" : orgName, 13, 26),
            ("هذه الصفحة تُرافق ملف إعداد الوكيل إلى أن يصدر الدليل المصوّر الكامل.", 11.5, 22),
            ("ما هذا الملف؟", 14, 8),
            ($"ملف إعداد للحاسوب الموجود في «{officeName}». يحمل هوية الهيئة وهيكليتها ومفتاح المكتب", 11.5, 4),
            ("وشهادة هذا الحاسوب وحده، ولا يعمل على أي حاسوب آخر.", 11.5, 22),
            ("كيف يُستعمَل؟", 14, 8),
            ("1. افتح الوكيل على الحاسوب المقصود. تظهر شاشة «ملف الإعداد».", 11.5, 4),
            ("2. اختر هذا الملف، ثم اكتب كلمة مرور الملف التي سُلّمت معه.", 11.5, 4),
            ("3. راجع نتيجة الفحص، ثم اضغط «تفعيل هذه النسخة».", 11.5, 4),
            ("4. اختر كلمة مرور الحساب، واطبع ورقة الاسترداد واحفظها في مكان آمن.", 11.5, 22),
            ("تنبيهات", 14, 8),
            ("كلمة مرور الملف لا تُحفظ في أداة مدير النظام. إن ضاعت فاطلب ملفًا جديدًا.", 11.5, 4),
            ("لا تُرسل هذا الملف إلى أحد غير صاحب الحاسوب، ولا تحتفظ بنسخة منه بعد التفعيل.", 11.5, 4),
            ("ورقة الاسترداد هي الطريق الوحيد إلى الحساب عند نسيان كلمة المرور.", 11.5, 26),
            ($"حُرّرت هذه الصفحة في {day}", 10, 0),
        ];
    }

    private static byte[] Write(
        List<(string Text, double Size, double SpaceAfter)> lines,
        PdfFontFile? font)
    {
        var content = new StringBuilder();
        var glyphs = new SortedSet<ushort>();

        // A thin rule the width of the text column, under the title and the organisation's name and
        // clear of both: the two lines above it end at 730 points and the first paragraph starts at
        // 685, so the rule sits in the gap rather than through a letter.
        const double rule = PageHeight - Margin - 76;
        content.Append(CultureInfo.InvariantCulture, $"0.6 w {Margin:0.##} {rule:0.##} m ");
        content.Append(CultureInfo.InvariantCulture, $"{PageWidth - Margin:0.##} {rule:0.##} l S\n");

        var y = PageHeight - Margin - 16;
        foreach (var (text, size, spaceAfter) in lines)
        {
            if (font is not null)
            {
                var visual = ArabicShaping.ToVisualOrder(text);
                var width = font.WidthOf(visual) * size;

                // Right to left: every line starts at the right margin and runs leftwards, so the
                // drawing point is the right edge less the width of the line.
                var x = PageWidth - Margin - width;
                content.Append(CultureInfo.InvariantCulture, $"BT /F1 {size:0.##} Tf {x:0.##} {y:0.##} Td <");
                foreach (var character in visual)
                {
                    // A character the face has no glyph for is left out rather than drawn as the
                    // empty box every reader shows for a missing one.
                    var glyph = font.GlyphOf(character);
                    if (glyph == 0)
                    {
                        continue;
                    }

                    glyphs.Add(glyph);
                    content.Append(CultureInfo.InvariantCulture, $"{glyph:X4}");
                }

                content.Append("> Tj ET\n");
            }

            y -= size * 1.45 + spaceAfter;
        }

        return Assemble(content.ToString(), font, glyphs);
    }

    /// <summary>Puts the objects together, with the cross-reference table every reader starts from.</summary>
    private static byte[] Assemble(string content, PdfFontFile? font, SortedSet<ushort> glyphs)
    {
        var objects = new List<byte[]>();
        var contentBytes = Encoding.UTF8.GetBytes(content);

        var resources = font is null
            ? "<< >>"
            : "<< /Font << /F1 5 0 R >> >>";

        objects.Add(Ascii("<< /Type /Catalog /Pages 2 0 R >>"));
        objects.Add(Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"));
        objects.Add(Ascii(
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth:0} {PageHeight:0}] "
            + $"/Resources {resources} /Contents 4 0 R >>"));
        objects.Add(Stream($"<< /Length {contentBytes.Length} >>", contentBytes));

        if (font is not null)
        {
            objects.Add(Ascii(
                $"<< /Type /Font /Subtype /Type0 /BaseFont /{PdfFontFile.FontName} /Encoding /Identity-H "
                + "/DescendantFonts [6 0 R] >>"));

            objects.Add(Ascii(
                $"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{PdfFontFile.FontName} "
                + "/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> "
                + "/FontDescriptor 7 0 R /DW 600 /W [" + Widths(font, glyphs) + "] /CIDToGIDMap /Identity >>"));

            objects.Add(Ascii(
                $"<< /Type /FontDescriptor /FontName /{PdfFontFile.FontName} /Flags 4 "
                + $"/FontBBox [{font.BBox[0]} {font.BBox[1]} {font.BBox[2]} {font.BBox[3]}] /ItalicAngle 0 "
                + $"/Ascent {font.Ascent} /Descent {font.Descent} /CapHeight {font.Ascent} /StemV 80 "
                + "/FontFile2 8 0 R >>"));

            objects.Add(Stream(
                $"<< /Length {font.Bytes.Length} /Length1 {font.Bytes.Length} >>",
                font.Bytes));
        }

        using var output = new MemoryStream();
        Append(output, "%PDF-1.7\n%âãÏÓ\n");

        var offsets = new List<long>(objects.Count);
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(output.Length);
            Append(output, $"{index + 1} 0 obj\n");
            output.Write(objects[index]);
            Append(output, "\nendobj\n");
        }

        var startXref = output.Length;
        Append(output, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            Append(output, $"{offset:0000000000} 00000 n \n");
        }

        Append(
            output,
            $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{startXref}\n%%EOF\n");

        return output.ToArray();
    }

    /// <summary>The width of every glyph the page actually draws, in the shape a CID font wants.</summary>
    private static string Widths(PdfFontFile font, SortedSet<ushort> glyphs)
    {
        var builder = new StringBuilder();
        foreach (var glyph in glyphs)
        {
            builder.Append(CultureInfo.InvariantCulture, $"{glyph} [{font.WidthOf(glyph)}] ");
        }

        return builder.ToString().TrimEnd();
    }

    private static byte[] Ascii(string text) => Encoding.Latin1.GetBytes(text);

    private static byte[] Stream(string dictionary, byte[] payload)
    {
        using var buffer = new MemoryStream();
        var head = Encoding.Latin1.GetBytes(dictionary + "\nstream\n");
        buffer.Write(head);
        buffer.Write(payload);
        buffer.Write(Encoding.Latin1.GetBytes("\nendstream"));
        return buffer.ToArray();
    }

    private static void Append(Stream stream, string text)
    {
        var bytes = Encoding.Latin1.GetBytes(text);
        stream.Write(bytes);
    }
}
