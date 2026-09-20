using Wakeel.Core.Services.Documents;
using Wakeel.Ocr;

namespace Wakeel.Core.Tests.Documents;

/// <summary>
/// Binding a multi-page scan back into one document (B3-2): three sheets in, one PDF of three
/// pages out, which the rasteriser can then take apart again for the reader.
/// </summary>
public sealed class ScanPdfWriterTests
{
    [Fact]
    public void Three_sheets_become_one_pdf_of_three_pages()
    {
        var writer = new ScanPdfWriter();
        var pages = new List<ScannedPage>
        {
            new(OcrTestImages.Page(["الصفحة الأولى"], 600, 300), DocumentMediaTypes.Png),
            new(OcrTestImages.Page(["الصفحة الثانية"], 600, 300), DocumentMediaTypes.Png),
            new(OcrTestImages.Page(["الصفحة الثالثة"], 600, 300), DocumentMediaTypes.Png),
        };

        var pdf = writer.Combine(pages);

        Assert.Equal(DocumentMediaTypes.Pdf, DocumentMediaTypes.Sniff(pdf.AsSpan(0, 8)));
        Assert.Equal(3, PdfRasterizer.PageCount(pdf));

        // And every page comes back out as a picture the reader could look at.
        var rendered = PdfRasterizer.RenderPages(pdf, 100).ToList();
        Assert.Equal(3, rendered.Count);
        Assert.All(rendered, page => Assert.Equal(DocumentMediaTypes.Png, DocumentMediaTypes.Sniff(page.Png.AsSpan(0, 8))));
    }

    [Fact]
    public void A_sheet_that_is_not_a_picture_is_skipped_rather_than_losing_the_run()
    {
        var writer = new ScanPdfWriter();
        var pages = new List<ScannedPage>
        {
            new(OcrTestImages.Page(["صفحة"], 400, 200), DocumentMediaTypes.Png),
            new([1, 2, 3, 4], DocumentMediaTypes.Png),
        };

        var pdf = writer.Combine(pages);

        Assert.Equal(1, PdfRasterizer.PageCount(pdf));
    }

    [Fact]
    public void Binding_nothing_is_a_defect_and_not_an_empty_pdf()
    {
        var writer = new ScanPdfWriter();

        Assert.Throws<ArgumentException>(() => writer.Combine([]));
    }

    [Fact]
    public void Sheets_that_are_none_of_them_pictures_are_refused_rather_than_bound_into_nothing()
    {
        var writer = new ScanPdfWriter();
        var pages = new List<ScannedPage>
        {
            new([1, 2, 3, 4], DocumentMediaTypes.Png),
            new([5, 6, 7, 8], DocumentMediaTypes.Png),
        };

        // A PDF of no pages would be stored, listed as «مسح-…» and open on nothing. Refusing it
        // is what sends the scan down the «keep the sheets as they are» road instead.
        Assert.Throws<ArgumentException>(() => writer.Combine(pages));
    }
}
