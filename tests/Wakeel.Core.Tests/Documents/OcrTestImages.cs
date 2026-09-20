using SkiaSharp;
using Wakeel.Ocr;

namespace Wakeel.Core.Tests.Documents;

/// <summary>
/// Draws the pages the OCR tests then read back. Nothing is checked in: a picture of Arabic text
/// is made here, at the moment of the test, so the test proves the whole path — a page of paper
/// comes in as pixels and words come out — rather than proving that a stored fixture still
/// decodes.
/// </summary>
internal static class OcrTestImages
{
    /// <summary>The font size words are drawn at; large enough that 300 dpi is not needed to read them.</summary>
    private const float FontSize = 48f;

    /// <summary>Whether this machine has the reading files at all.</summary>
    public static bool HasTessdata => TessdataLocator.Find() is not null;

    /// <summary>
    /// The sentence a test skipping for want of the reading files prints, so a person reading the
    /// run knows what to do rather than wondering what silently did not happen.
    /// </summary>
    public const string SkipReason =
        "The Tesseract model files are not on this machine. Run build/get-models.ps1 to fetch tools/tessdata.";

    /// <summary>Whether a font that can draw Arabic was found; without one there is nothing to read.</summary>
    public static bool HasArabicFont => ArabicTypeface() is not null;

    /// <summary>The sentence a test skips with when no Arabic font is installed.</summary>
    public const string NoFontReason = "No font on this machine can draw Arabic, so there is nothing to read back.";

    /// <summary>
    /// One page as PNG bytes, with each line drawn under the last. Black on white and nothing
    /// else: a reader is being tested, not a renderer.
    /// </summary>
    /// <param name="lines">The lines to draw.</param>
    /// <param name="width">Page width in pixels.</param>
    /// <param name="height">Page height in pixels.</param>
    public static byte[] Page(IEnumerable<string> lines, int width = 1000, int height = 500)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            Draw(canvas, lines, width, height);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>A PDF of one page per set of lines — what the two-page rasterisation test reads.</summary>
    /// <param name="pages">The lines of each page.</param>
    /// <param name="width">Page width in points.</param>
    /// <param name="height">Page height in points.</param>
    public static byte[] Pdf(IEnumerable<IEnumerable<string>> pages, int width = 600, int height = 400)
    {
        using var output = new MemoryStream();
        using (var document = SKDocument.CreatePdf(output))
        {
            foreach (var lines in pages)
            {
                using var canvas = document.BeginPage(width, height);
                Draw(canvas, lines, width, height);
                document.EndPage();
            }

            document.Close();
        }

        return output.ToArray();
    }

    private static void Draw(SKCanvas canvas, IEnumerable<string> lines, int width, int height)
    {
        canvas.Clear(SKColors.White);

        using var typeface = ArabicTypeface() ?? SKTypeface.Default;
        using var font = new SKFont(typeface, FontSize);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };

        var y = FontSize + 20f;
        foreach (var line in lines)
        {
            var textWidth = font.MeasureText(line);
            canvas.DrawText(line, Math.Max(20f, width - 40f - textWidth), y, SKTextAlign.Left, font, paint);
            y += FontSize * 1.6f;
            if (y > height - 10)
            {
                break;
            }
        }
    }

    /// <summary>
    /// What the reader gives back for a line these pages were drawn with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Skia has no bidirectional pass of its own: it paints the characters it is handed from left
    /// to right, so a line of Arabic is drawn with its first letter on the left — the opposite of
    /// where an Arabic page puts it. Tesseract then reads that line the way Arabic is read, from
    /// the right, and returns it back to front. Reversing the Arabic runs of the line that was
    /// drawn — and leaving Latin and digits alone — is therefore what the page really says, and
    /// what a test can hold the reader to.
    /// </para>
    /// <para>
    /// This is a property of the drawing in these tests and of nothing in الوكيل: a real scan is
    /// a photograph of a page that was typeset properly, and the reader returns its text the
    /// right way round. The product path is not affected; only the fixture is.
    /// </para>
    /// </remarks>
    /// <param name="line">The line as it was handed to the page.</param>
    public static string AsRead(string line)
    {
        var result = new List<char>(line.Length);
        var run = new List<char>();

        foreach (var c in line)
        {
            if (IsArabic(c) || (run.Count > 0 && c == ' '))
            {
                run.Add(c);
                continue;
            }

            Flush();
            result.Add(c);
        }

        Flush();
        return new string([.. result]);

        void Flush()
        {
            if (run.Count == 0)
            {
                return;
            }

            // A trailing space belongs between the runs, not inside the reversed one.
            var trailing = 0;
            while (run.Count > 0 && run[^1] == ' ')
            {
                run.RemoveAt(run.Count - 1);
                trailing++;
            }

            run.Reverse();
            result.AddRange(run);
            result.AddRange(Enumerable.Repeat(' ', trailing));
            run.Clear();
        }
    }

    /// <summary>Whether a character belongs to an Arabic run — letters, marks and Arabic-Indic digits.</summary>
    private static bool IsArabic(char c) => c is >= '؀' and <= 'ۿ' or >= 'ﭐ' and <= 'ﻼ';

    /// <summary>
    /// A font on this machine that has Arabic letters in it. Windows always has at least one; the
    /// search is by well-known family first so the page looks like a page, then by asking Skia
    /// for anything that can draw an Arabic character at all.
    /// </summary>
    private static SKTypeface? ArabicTypeface()
    {
        foreach (var family in new[] { "Arial", "Tahoma", "Segoe UI", "Times New Roman", "Simplified Arabic" })
        {
            var typeface = SKTypeface.FromFamilyName(family);
            if (typeface is not null && CanDrawArabic(typeface))
            {
                return typeface;
            }

            typeface?.Dispose();
        }

        using var manager = SKFontManager.Default;
        var fallback = manager.MatchCharacter('م');
        return fallback is not null && CanDrawArabic(fallback) ? fallback : null;
    }

    private static bool CanDrawArabic(SKTypeface typeface)
    {
        // U+0645 ARABIC LETTER MEEM: a font with no glyph for it cannot draw an Arabic word.
        var glyphs = typeface.GetGlyphs("م");
        return glyphs.Length > 0 && glyphs[0] != 0;
    }
}
