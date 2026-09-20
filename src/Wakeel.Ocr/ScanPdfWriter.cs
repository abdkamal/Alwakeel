using SkiaSharp;
using Wakeel.Core.Services.Documents;

namespace Wakeel.Ocr;

/// <summary>
/// Binds the sheets of one scan into a single PDF (<see cref="IScanPdfWriter"/>). A letter
/// printed on three pages is one document: three separate images would be three rows in W43,
/// three OCR runs, and three things to attach to a correspondence that has one attachment.
/// </summary>
/// <remarks>
/// It lives here rather than in Core because writing a PDF means an imaging library and Core has
/// none; this package already carries SkiaSharp for the rasteriser. Each sheet is drawn at its
/// own pixel size mapped to points at <see cref="ScanDpi"/>, so an A4 sheet scanned at 300 dpi
/// comes out an A4 page rather than a page the size of its pixel count.
/// </remarks>
public sealed class ScanPdfWriter : IScanPdfWriter
{
    /// <summary>The resolution a scanned sheet's pixels are read as when they become a page.</summary>
    public const int ScanDpi = 300;

    /// <summary>Points per inch, which is the unit a PDF page is measured in.</summary>
    private const float PointsPerInch = 72f;

    private readonly int _dpi;

    /// <summary>Creates the writer.</summary>
    /// <param name="dpi">The resolution the sheets were scanned at.</param>
    public ScanPdfWriter(int dpi = ScanDpi)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dpi, 1);
        _dpi = dpi;
    }

    /// <inheritdoc />
    public byte[] Combine(IReadOnlyList<ScannedPage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count == 0)
        {
            throw new ArgumentException("A PDF is bound from at least one sheet.", nameof(pages));
        }

        var written = 0;
        using var output = new MemoryStream();
        using (var document = SKDocument.CreatePdf(output))
        {
            foreach (var page in pages)
            {
                // A sheet the driver gave us that is not a picture at all: skipping it keeps the
                // rest of the scan rather than losing the whole run to one bad frame. Skia
                // answers a null bitmap for some of those and throws for others, so both are
                // treated as the same thing.
                SKBitmap? decoded;
                try
                {
                    decoded = SKBitmap.Decode(page.Content);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    decoded = null;
                }

                if (decoded is null)
                {
                    continue;
                }

                using var bitmap = decoded;

                var width = bitmap.Width * PointsPerInch / _dpi;
                var height = bitmap.Height * PointsPerInch / _dpi;
                using var canvas = document.BeginPage(width, height);
                canvas.DrawBitmap(bitmap, new SKRect(0, 0, width, height), SKSamplingOptions.Default);
                document.EndPage();
                written++;
            }

            document.Close();
        }

        // Every sheet was skipped. A PDF with no pages inside it is worse than no PDF at all: it
        // would be stored, listed as «مسح-…» and open on nothing, so the caller is told the same
        // way it is told about an empty scan and falls back to storing the sheets as they are.
        if (written == 0)
        {
            throw new ArgumentException("A PDF is bound from at least one sheet.", nameof(pages));
        }

        return output.ToArray();
    }
}
