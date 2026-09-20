using Wakeel.Core.Services.Documents;
using Wakeel.Ocr;

namespace Wakeel.Core.Tests.Documents;

/// <summary>
/// Reading real pixels with Tesseract (B3-2). Every test here draws its own page, so what is
/// proved is the whole path: paper-shaped pixels in, Arabic and English words with boxes out.
/// </summary>
/// <remarks>
/// The reading files are fetched by <c>build/get-models.ps1</c> and are not in the repository, so
/// every test that needs them skips with a sentence saying so rather than failing on a machine
/// that has not fetched them.
/// </remarks>
public sealed class OcrServiceTests
{
    /// <summary>Whether a character is an Arabic letter, which is what «the Arabic was read» means here.</summary>
    private static bool IsArabicLetter(char c) => c is >= 'ؠ' and <= 'ي' or >= 'ٱ' and <= 'ۓ';

    [Fact]
    public void Without_model_files_the_reader_says_so_instead_of_throwing()
    {
        using var ocr = new OcrService(tessdataFolder: Path.Combine(Path.GetTempPath(), "wakeel-no-tessdata"));

        Assert.False(ocr.IsAvailable);
        Assert.Null(ocr.ModelFingerprint);
    }

    [Fact]
    public async Task Without_model_files_a_page_comes_back_as_not_configured()
    {
        using var ocr = new OcrService(tessdataFolder: null);

        var result = await ocr.ReadAsync([1, 2, 3], DocumentMediaTypes.Png);

        Assert.Equal(OcrState.ModelMissing, result.State);
        Assert.Empty(result.Pages);
    }

    [Fact]
    public async Task A_word_document_is_never_read_this_way()
    {
        using var ocr = new OcrService(TessdataLocator.Find());

        var result = await ocr.ReadAsync([1, 2, 3], DocumentMediaTypes.Word);

        Assert.Equal(OcrState.Unsupported, result.State);
    }

    [OcrFact]
    public async Task Arabic_and_english_words_are_read_off_a_generated_page_with_their_boxes()
    {
        var page = OcrTestImages.Page(["وزارة الداخلية", "Ministry Report"]);
        using var ocr = new OcrService(TessdataLocator.Find());

        var result = await ocr.ReadAsync(page, DocumentMediaTypes.Png);

        Assert.Equal(OcrState.Ok, result.State);
        var read = Assert.Single(result.Pages);
        Assert.Equal(1, read.PageNo);

        // The Latin line comes back exactly; a reader that found nothing would fail here first.
        Assert.Contains("Ministry", read.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Report", read.Text, StringComparison.OrdinalIgnoreCase);

        // And the Arabic line comes back as Arabic letters. Exactly which letters a bare synthetic
        // page yields is not something to hold the Arabic model to — the page is drawn without a
        // shaper (see OcrTestImages.AsRead) and is nothing like a scan of typeset paper — but a
        // reader that had not loaded the Arabic language at all would return none of them, and
        // that is what this proves.
        var arabicLetters = read.Text.Count(IsArabicLetter);
        Assert.True(arabicLetters >= 5, $"expected Arabic letters in what was read: «{read.Text}»");

        // Two words were drawn in Arabic and two in English; the reader may split or join a few,
        // so the assertion is that it found several and boxed every one of them.
        Assert.InRange(read.Words.Count, 3, 24);
        Assert.All(read.Words, word =>
        {
            Assert.False(string.IsNullOrWhiteSpace(word.Text));
            Assert.True(word.Width > 0, "a word's box has a width");
            Assert.True(word.Height > 0, "a word's box has a height");
            Assert.InRange(word.Confidence, 0d, 1d);
        });

        Assert.InRange(read.Confidence, 0d, 1d);
        Assert.True(read.Confidence > 0, "a page that was read has some confidence in it");

        // Every box sits on the page it was read from.
        Assert.All(read.Words, word =>
        {
            Assert.InRange(word.X, 0, 1000);
            Assert.InRange(word.Y, 0, 500);
        });
    }

    [OcrFact]
    public async Task A_reading_asked_for_with_the_stop_already_pressed_answers_stopped()
    {
        // A session that closes between «اقرأ النص» and the thread pool picking the work up: the
        // reading must come back as a stopped run, which is what the queue is written to put back
        // in line. A task that faulted instead would leave the document in «جارٍ قراءة النص» until
        // the next time somebody resumed the queue.
        var page = OcrTestImages.Page(["وزارة الداخلية"]);
        using var ocr = new OcrService(TessdataLocator.Find());
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        var result = await ocr.ReadAsync(page, DocumentMediaTypes.Png, progress: null, stop.Token);

        Assert.Equal(OcrState.Cancelled, result.State);
    }

    [OcrFact]
    public async Task Arabic_indic_digits_on_the_page_come_back_as_western_digits()
    {
        // AGREEMENT item 20: a number read off a scan must be the same number the rest of الوكيل
        // writes, so it is folded to western digits before anything stores it.
        var page = OcrTestImages.Page(["رقم ١٢٣٤"]);
        using var ocr = new OcrService(TessdataLocator.Find());

        var result = await ocr.ReadAsync(page, DocumentMediaTypes.Png);

        Assert.Equal(OcrState.Ok, result.State);
        Assert.False(ArabicOcrText.HasArabicIndicDigits(result.Pages[0].Text));
        Assert.All(result.Pages[0].Words, word => Assert.False(ArabicOcrText.HasArabicIndicDigits(word.Text)));
    }

    [OcrFact]
    public async Task A_generated_two_page_pdf_is_rasterised_and_read_page_by_page()
    {
        var pdf = OcrTestImages.Pdf(
        [
            ["الصفحة الأولى", "Page One"],
            ["الصفحة الثانية", "Page Two"],
        ]);

        Assert.Equal(2, PdfRasterizer.PageCount(pdf));

        var progress = new List<OcrPageProgress>();
        using var ocr = new OcrService(TessdataLocator.Find());
        var result = await ocr.ReadAsync(pdf, DocumentMediaTypes.Pdf, new Progress<OcrPageProgress>(progress.Add));

        Assert.Equal(OcrState.Ok, result.State);
        Assert.Equal(2, result.Pages.Count);
        Assert.Equal([1, 2], result.Pages.Select(p => p.PageNo));

        Assert.Contains("One", result.Pages[0].Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Two", result.Pages[1].Text, StringComparison.OrdinalIgnoreCase);
        Assert.All(result.Pages, page => Assert.NotEmpty(page.Words));

        // Progress was reported for each page, and it said how many there were.
        Assert.Equal(2, progress.Count);
        Assert.All(progress, p => Assert.Equal(2, p.Pages));
        Assert.Contains("1", progress[0].MessageAr, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pdf_that_is_not_a_pdf_has_no_pages_rather_than_throwing()
    {
        Assert.Equal(0, PdfRasterizer.PageCount([1, 2, 3, 4]));
        Assert.Null(PdfRasterizer.RenderPage([1, 2, 3, 4], 0));
    }

    [Fact]
    public void The_model_fingerprint_changes_when_the_files_change()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wakeel-tessdata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "ara.traineddata"), new byte[100]);
            File.WriteAllBytes(Path.Combine(folder, "eng.traineddata"), new byte[100]);
            var before = TessdataLocator.Fingerprint(folder);
            Assert.NotNull(before);

            File.WriteAllBytes(Path.Combine(folder, "ara.traineddata"), new byte[101]);
            var after = TessdataLocator.Fingerprint(folder);

            Assert.NotEqual(before, after);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_replacement_of_the_same_size_is_still_noticed()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wakeel-tessdata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var arabic = Path.Combine(folder, "ara.traineddata");
            File.WriteAllBytes(arabic, new byte[100]);
            File.WriteAllBytes(Path.Combine(folder, "eng.traineddata"), new byte[100]);
            var before = TessdataLocator.Fingerprint(folder);

            // The office swapped one build of Arabic for another. Two builds of the same language
            // being the same number of bytes is not far-fetched, and going by the size alone
            // would leave every document carrying text nobody can reproduce (AGREEMENT item 30).
            var replacement = new byte[100];
            replacement[0] = 7;
            File.WriteAllBytes(arabic, replacement);
            File.SetLastWriteTimeUtc(arabic, DateTime.UtcNow.AddMinutes(5));

            Assert.NotEqual(before, TessdataLocator.Fingerprint(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_folder_missing_one_language_is_not_usable()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wakeel-tessdata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "ara.traineddata"), new byte[10]);

            Assert.False(TessdataLocator.IsUsable(folder));
            Assert.Null(TessdataLocator.Fingerprint(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
