using Tesseract;
using Wakeel.Core.Data;
using Wakeel.Core.Services;
using Wakeel.Core.Services.Documents;

namespace Wakeel.Ocr;

/// <summary>
/// Reads the text of a scanned document with Tesseract (<c>ara+eng</c>), page by page
/// (<see cref="IOcrEngine"/>, B3-2).
/// </summary>
/// <remarks>
/// <para>
/// <b>What it reads.</b> A picture is one page. A PDF is rasterised page by page by
/// <see cref="PdfRasterizer"/> and each page is handed to Tesseract as PNG bytes through
/// <c>Pix.LoadFromMemory</c>, so nothing is ever written to a temporary file — a scan of a
/// confidential letter must not appear on the disk outside the vault, even for a moment. A Word
/// document carries its own text and is never read this way.
/// </para>
/// <para>
/// <b>Both languages at once.</b> Office letters are Arabic with English names, numbers and
/// abbreviations in them (AGREEMENT's bidi requirement), so the two languages are loaded together
/// rather than the page being guessed at first.
/// </para>
/// <para>
/// <b>One engine, one thread.</b> Tesseract's engine is not safe to use from two threads, and
/// building one costs about a second — too much per page, and far too much per document in a long
/// queue. One engine is therefore built lazily and every read takes a lock on it. The queue is a
/// background worker reading one document at a time, so nothing waits on that lock in practice.
/// </para>
/// <para>
/// <b>It never throws for a document.</b> A missing model, a damaged PDF, a page Tesseract
/// refuses: each of them is a state the documents list shows in words, not an exception that
/// stops an import the office has already been told succeeded.
/// </para>
/// </remarks>
public sealed class OcrService : IOcrEngine, IDisposable
{
    private readonly string? _tessdata;
    private readonly Lock _gate = new();
    private TesseractEngine? _engine;
    private bool _disposed;

    /// <summary>Creates the reader, finding the model files for this machine.</summary>
    /// <param name="paths">The installation, whose models folder is one of the places looked in.</param>
    public OcrService(WakeelPaths? paths = null)
        : this(TessdataLocator.Find(paths))
    {
    }

    /// <summary>Creates the reader over a known folder — what the tests use.</summary>
    /// <param name="tessdataFolder">The folder holding <c>ara.traineddata</c> and <c>eng.traineddata</c>.</param>
    public OcrService(string? tessdataFolder)
    {
        _tessdata = TessdataLocator.IsUsable(tessdataFolder) ? tessdataFolder : null;
        ModelFingerprint = TessdataLocator.Fingerprint(_tessdata);
    }

    /// <inheritdoc />
    public bool IsAvailable => _tessdata is not null;

    /// <inheritdoc />
    public string? ModelFingerprint { get; }

    /// <summary>The folder the model files were found in, for the health centre's card.</summary>
    public string? TessdataFolder => _tessdata;

    /// <inheritdoc />
    public Task<OcrDocumentResult> ReadAsync(
        byte[] content,
        string mediaType,
        IProgress<OcrPageProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

        if (!DocumentMediaTypes.IsReadable(mediaType))
        {
            return Task.FromResult(OcrDocumentResult.Unsupported);
        }

        if (!IsAvailable)
        {
            return Task.FromResult(OcrDocumentResult.ModelMissing);
        }

        // Reading is CPU work measured in seconds per page; it never runs on the caller's thread.
        // The token is deliberately not given to Task.Run: a token that is already cancelled when
        // the work is scheduled would fault the task, while Read turns cancellation into
        // OcrState.Cancelled with the pages it finished — which is what the queue is written to
        // handle, and what keeps a stopped document out of «جارٍ قراءة النص» for ever.
        return Task.Run(() => Read(content, mediaType, progress, cancellationToken));
    }

    /// <summary>Reads one picture and returns its page — the smallest unit, and what the tests drive.</summary>
    /// <param name="png">The picture, as PNG or JPEG bytes.</param>
    /// <param name="pageNo">The number to give the page.</param>
    public OcrPageResult? ReadImage(byte[] png, int pageNo = 1)
    {
        ArgumentNullException.ThrowIfNull(png);
        return IsAvailable ? ReadPage(png, pageNo) : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _engine?.Dispose();
            _engine = null;
        }
    }

    private OcrDocumentResult Read(
        byte[] content,
        string mediaType,
        IProgress<OcrPageProgress>? progress,
        CancellationToken cancellationToken)
    {
        var pages = new List<OcrPageResult>();
        try
        {
            // A run that was already stopped before the thread pool picked it up reads nothing and
            // answers «stopped», the same as one stopped halfway: the queue then puts the document
            // back in line instead of leaving it in «جارٍ قراءة النص».
            cancellationToken.ThrowIfCancellationRequested();

            if (string.Equals(mediaType, DocumentMediaTypes.Pdf, StringComparison.OrdinalIgnoreCase))
            {
                var total = PdfRasterizer.PageCount(content);
                if (total == 0)
                {
                    return OcrDocumentResult.Failed;
                }

                foreach (var (pageNo, png) in PdfRasterizer.RenderPages(content, PdfRasterizer.ReadingDpi, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(new OcrPageProgress(pageNo, total));
                    var page = ReadPage(png, pageNo);
                    if (page is not null)
                    {
                        pages.Add(page);
                    }
                }
            }
            else
            {
                progress?.Report(new OcrPageProgress(1, 1));
                var page = ReadPage(content, 1);
                if (page is not null)
                {
                    pages.Add(page);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The pages already read are kept: a queue stopped by a sign-out resumes from where
            // it was rather than reading the first half of a twenty-page scan twice.
            return new OcrDocumentResult(OcrState.Cancelled, pages, CoreAr.Documents.OcrPending);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return pages.Count > 0
                ? new OcrDocumentResult(OcrState.Ok, pages)
                : OcrDocumentResult.Failed;
        }

        return pages.Count > 0 ? new OcrDocumentResult(OcrState.Ok, pages) : OcrDocumentResult.Failed;
    }

    private OcrPageResult? ReadPage(byte[] imageBytes, int pageNo)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var engine = _engine ??= new TesseractEngine(_tessdata, TessdataLocator.Languages, EngineMode.Default);

            try
            {
                using var pix = Pix.LoadFromMemory(imageBytes);
                using var page = engine.Process(pix, PageSegMode.Auto);

                var text = ArabicOcrText.Clean(page.GetText());
                var confidence = Math.Clamp(page.GetMeanConfidence(), 0f, 1f);
                var words = ReadWords(page);
                return new OcrPageResult(pageNo, text, confidence, words);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // One page the reader could not take. The document is still stored and its other
                // pages are still read.
                return null;
            }
        }
    }

    /// <summary>
    /// Every word on the page with the box it sits in — what lets W44 highlight a search hit on
    /// the picture instead of only in the text beside it.
    /// </summary>
    private static List<OcrWordBox> ReadWords(Page page)
    {
        var words = new List<OcrWordBox>();
        using var iterator = page.GetIterator();
        iterator.Begin();

        do
        {
            var text = iterator.GetText(PageIteratorLevel.Word);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (!iterator.TryGetBoundingBox(PageIteratorLevel.Word, out var box))
            {
                continue;
            }

            // Tesseract's word confidence is a percentage; everything in الوكيل stores 0 to 1.
            var confidence = Math.Clamp(iterator.GetConfidence(PageIteratorLevel.Word) / 100f, 0f, 1f);
            words.Add(new OcrWordBox(
                ArabicOcrText.Clean(text).Trim(),
                confidence,
                box.X1,
                box.Y1,
                box.Width,
                box.Height));
        }
        while (iterator.Next(PageIteratorLevel.Word));

        return words;
    }
}
