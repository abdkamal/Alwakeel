using PDFtoImage;
using SkiaSharp;

namespace Wakeel.Ocr;

/// <summary>
/// Turns the pages of a PDF into pictures a reader can look at. الوكيل's own letters carry their
/// text and never come here; what comes here is a scan saved as a PDF, whose pages are pictures
/// of paper and have no text in them at all.
/// </summary>
/// <remarks>
/// <para>
/// Pages are produced one at a time and handed on as PNG bytes rather than all at once as
/// bitmaps. A twenty-page colour scan at 300 dpi is over a gigabyte of pixels if it is all held
/// together, and the reader only ever looks at one page.
/// </para>
/// <para>
/// 300 dpi is the resolution: it is what Tesseract's own guidance asks for, and reading Arabic at
/// 150 turns ب ت ث into each other.
/// </para>
/// </remarks>
public static class PdfRasterizer
{
    /// <summary>The resolution pages are rendered at for reading.</summary>
    public const int ReadingDpi = 300;

    /// <summary>How many pages a PDF has, or 0 when it cannot be read as one.</summary>
    public static int PageCount(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        try
        {
            // PDFium is supported on every platform الوكيل runs on; the analyzer objects only
            // because this assembly declares no platform of its own, and one that named Windows
            // would spread the annotation through the reader, the queue and Core's contracts for
            // no gain. Both call sites are suppressed here, where the reason is written down.
#pragma warning disable CA1416 // Validate platform compatibility
            return Conversion.GetPageCount(pdf);
#pragma warning restore CA1416
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A damaged or encrypted PDF is a document whose text cannot be read; it is not a
            // crash, and the document itself is still stored and still shown.
            return 0;
        }
    }

    /// <summary>
    /// Renders one page as PNG bytes, or <c>null</c> when that page cannot be rendered.
    /// </summary>
    /// <param name="pdf">The file.</param>
    /// <param name="pageIndex">The page, counted from 0.</param>
    /// <param name="dpi">The resolution.</param>
    public static byte[]? RenderPage(byte[] pdf, int pageIndex, int dpi = ReadingDpi)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);

        try
        {
#pragma warning disable CA1416 // Validate platform compatibility — see PageCount.
            using var bitmap = Conversion.ToImage(pdf, pageIndex, password: null, new RenderOptions { Dpi = dpi });
#pragma warning restore CA1416
            if (bitmap is null)
            {
                return null;
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data?.ToArray();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    /// Every page as PNG bytes, produced lazily so only one page is in memory at a time. A page
    /// that cannot be rendered is skipped rather than ending the walk: one damaged page in the
    /// middle of a long scan must not cost the office the other nineteen.
    /// </summary>
    /// <param name="pdf">The file.</param>
    /// <param name="dpi">The resolution.</param>
    /// <param name="cancellationToken">Stops between pages.</param>
    public static IEnumerable<(int PageNo, byte[] Png)> RenderPages(
        byte[] pdf,
        int dpi = ReadingDpi,
        CancellationToken cancellationToken = default)
    {
        var count = PageCount(pdf);
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var png = RenderPage(pdf, i, dpi);
            if (png is not null)
            {
                yield return (i + 1, png);
            }
        }
    }
}
