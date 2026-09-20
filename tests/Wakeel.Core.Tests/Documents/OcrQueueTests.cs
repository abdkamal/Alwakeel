using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Services;
using Wakeel.Core.Services.Documents;
using Wakeel.Ocr;

namespace Wakeel.Core.Tests.Documents;

/// <summary>
/// The background reader's queue (B3-2): what order documents are taken in, what the office is
/// told while it runs, and what happens after a restart cuts a run off in the middle.
/// </summary>
/// <remarks>
/// These drive a stand-in reader rather than Tesseract, so they assert the queue's own behaviour
/// — order, progress, resume, the model change — without needing the model files and without
/// spending a second per page. <see cref="OcrServiceTests"/> is where the real reader is proved.
/// </remarks>
public sealed class OcrQueueTests
{
    private static byte[] Png(int payload)
    {
        var bytes = new byte[8 + 32];
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes);
        for (var i = 8; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(payload + i);
        }

        return bytes;
    }

    /// <summary>
    /// Stores one picture and tells the stand-in reader which document its bytes belong to, so a
    /// test can assert the order documents were taken in — the queue itself only ever hands the
    /// reader bytes.
    /// </summary>
    private static async Task<Guid> ImportAsync(DocumentsWorld world, FakeEngine engine, string name, int payload)
    {
        var content = Png(payload);
        var id = (await world.Documents.ImportAsync(content, name)).DocumentId;
        engine.Expect(id, content);
        return id;
    }

    [Fact]
    public async Task What_the_office_is_waiting_on_is_read_before_the_background_batch()
    {
        using var world = new DocumentsWorld();
        var engine = new FakeEngine();
        var queue = new OcrQueue(world.Db, world.Documents, engine);

        var first = await ImportAsync(world, engine, "أ.png", 10);
        var second = await ImportAsync(world, engine, "ب.png", 20);
        var third = await ImportAsync(world, engine, "ج.png", 30);

        queue.Enqueue(first);
        queue.Enqueue(second);
        queue.Enqueue(third, OcrPriority.Interactive);

        // The one somebody is looking at goes first; the other two keep the order they arrived in.
        Assert.Equal(new[] { third, first, second }, queue.Pending);

        await queue.DrainAsync();
        Assert.Equal(new[] { third, first, second }, engine.Read);
    }

    [Fact]
    public async Task A_document_already_in_line_is_not_queued_twice_but_can_be_moved_up()
    {
        using var world = new DocumentsWorld();
        var engine = new FakeEngine();
        var queue = new OcrQueue(world.Db, world.Documents, engine);

        var first = await ImportAsync(world, engine, "أ.png", 10);
        var second = await ImportAsync(world, engine, "ب.png", 20);

        queue.Enqueue(first);
        queue.Enqueue(second);
        queue.Enqueue(second);
        Assert.Equal(new[] { first, second }, queue.Pending);

        // Asking again from a screen the office is looking at moves it up rather than adding it
        // a second time.
        queue.Enqueue(second, OcrPriority.Interactive);
        Assert.Equal(new[] { second, first }, queue.Pending);
    }

    [Fact]
    public async Task Progress_counts_documents_and_names_the_page_being_read()
    {
        using var world = new DocumentsWorld();
        var engine = new FakeEngine { PagesPerDocument = 3 };
        var queue = new OcrQueue(world.Db, world.Documents, engine);

        var seen = new List<OcrQueueProgress>();
        queue.ProgressChanged += seen.Add;

        var first = await ImportAsync(world, engine, "أ.png", 10);
        var second = await ImportAsync(world, engine, "ب.png", 20);
        queue.Enqueue(first);
        queue.Enqueue(second);

        Assert.Equal(2, await queue.DrainAsync());

        Assert.Equal(2, queue.Progress.Done);
        Assert.Equal(2, queue.Progress.Total);
        Assert.Equal(CoreAr.Documents.OcrQueueDone, queue.Progress.MessageAr);

        // Each page of each document was reported while it was being read.
        var pages = seen.Where(p => p.DocumentId != Guid.Empty).ToList();
        Assert.Equal(6, pages.Count);
        Assert.All(pages, p => Assert.Equal(3, p.Pages));
        Assert.Contains(pages, p => p.DocumentId == first && p.Page == 3);
        Assert.Contains(pages, p => p.DocumentId == second && p.Page == 1);

        // And the sentence the screens show is western-digit Arabic with no code in it.
        var midway = new OcrQueueProgress(1, 2, first, 1, 3);
        Assert.Contains("1", midway.MessageAr, StringComparison.Ordinal);
        Assert.Contains("2", midway.MessageAr, StringComparison.Ordinal);
        Assert.False(ArabicOcrText.HasArabicIndicDigits(midway.MessageAr));
    }

    [Fact]
    public async Task A_read_document_carries_its_pages_its_count_and_the_files_that_read_it()
    {
        using var world = new DocumentsWorld();
        var engine = new FakeEngine { PagesPerDocument = 2, Fingerprint = "abc123" };
        var queue = new OcrQueue(world.Db, world.Documents, engine);

        var id = await ImportAsync(world, engine, "وارد.png", 5);
        queue.Enqueue(id);
        await queue.DrainAsync();

        var document = await world.Documents.GetAsync(id);
        Assert.Equal(OcrStatus.Done, document!.OcrStatus);
        Assert.Equal(2, document.PageCount);
        Assert.Equal("ara+eng/abc123", document.OcrLang);
        Assert.Equal(2, (await world.Documents.ListPagesAsync(id)).Count);
    }

    [Fact]
    public async Task A_run_cut_off_in_the_middle_is_picked_up_again_after_a_restart()
    {
        using var world = new DocumentsWorld();
        var engine = new FakeEngine();

        var finished = await ImportAsync(world, engine, "منتهٍ.png", 10);
        var interrupted = await ImportAsync(world, engine, "مقطوع.png", 20);
        var waiting = await ImportAsync(world, engine, "منتظر.png", 30);

        // The state the database is left in by a power cut: one document read, one marked as
        // being read by a session that no longer exists, one still waiting.
        var first = new OcrQueue(world.Db, world.Documents, engine);
        first.Enqueue(finished);
        await first.DrainAsync();
        await world.Documents.SetOcrStatusAsync(interrupted, OcrStatus.Running);

        // A fresh queue, as a fresh session builds.
        var resumed = new OcrQueue(world.Db, world.Documents, engine);
        var count = await resumed.ResumeAsync();

        Assert.Equal(2, count);
        Assert.Contains(interrupted, resumed.Pending);
        Assert.Contains(waiting, resumed.Pending);
        Assert.DoesNotContain(finished, resumed.Pending);

        // The one that was being read is waiting again rather than stuck saying «جارٍ».
        Assert.Equal(OcrStatus.Pending, (await world.Documents.GetAsync(interrupted))!.OcrStatus);

        await resumed.DrainAsync();
        Assert.Equal(OcrStatus.Done, (await world.Documents.GetAsync(interrupted))!.OcrStatus);
        Assert.Equal(OcrStatus.Done, (await world.Documents.GetAsync(waiting))!.OcrStatus);
    }

    [Fact]
    public async Task A_document_stopped_halfway_keeps_its_pages_and_stays_in_line()
    {
        using var world = new DocumentsWorld();
        using var stop = new CancellationTokenSource();
        var engine = new FakeEngine { PagesPerDocument = 20, StopAfterPage = 3, StopSource = stop };
        var queue = new OcrQueue(world.Db, world.Documents, engine);

        var id = await ImportAsync(world, engine, "مسح-طويل.png", 11);
        queue.Enqueue(id);

        // Signing out or shutting down in the middle of a twenty-page scan.
        await queue.DrainAsync(stop.Token);

        // The three pages that were read are kept — the work is not thrown away — but the
        // document is not filed as read, or the remaining seventeen pages would never be looked
        // at again and the document would be half of itself in the search for ever.
        var document = await world.Documents.GetAsync(id);
        Assert.Equal(OcrStatus.Pending, document!.OcrStatus);
        Assert.Equal(3, (await world.Documents.ListPagesAsync(id)).Count);

        // And the next session picks it up exactly because it is still waiting.
        engine.StopAfterPage = null;
        engine.StopSource = null;
        var resumed = new OcrQueue(world.Db, world.Documents, engine);
        Assert.Equal(1, await resumed.ResumeAsync());
        Assert.Contains(id, resumed.Pending);

        await resumed.DrainAsync();
        var finished = await world.Documents.GetAsync(id);
        Assert.Equal(OcrStatus.Done, finished!.OcrStatus);
        Assert.Equal(20, finished.PageCount);
        Assert.Equal(20, (await world.Documents.ListPagesAsync(id)).Count);
    }

    [Fact]
    public async Task Changing_the_reading_files_puts_the_documents_they_read_back_in_line()
    {
        using var world = new DocumentsWorld();
        var engine = new FakeEngine { Fingerprint = "old" };
        var id = await ImportAsync(world, engine, "قديم.png", 7);

        var before = new OcrQueue(world.Db, world.Documents, engine);
        before.Enqueue(id);
        await before.DrainAsync();
        Assert.Equal("ara+eng/old", (await world.Documents.GetAsync(id))!.OcrLang);

        // The office dropped new files into the models folder (AGREEMENT item 30).
        engine.Fingerprint = "new";
        var after = new OcrQueue(world.Db, world.Documents, engine);
        Assert.Equal(1, await after.ReprocessChangedModelAsync());
        Assert.Contains(id, after.Pending);
        Assert.Equal(OcrStatus.Pending, (await world.Documents.GetAsync(id))!.OcrStatus);

        await after.DrainAsync();
        Assert.Equal("ara+eng/new", (await world.Documents.GetAsync(id))!.OcrLang);

        // Nothing is stale any more, so a second pass marks nothing.
        Assert.Equal(0, await after.ReprocessChangedModelAsync());
    }

    [Fact]
    public async Task Without_reading_files_nothing_is_marked_and_nothing_is_lost()
    {
        using var world = new DocumentsWorld();
        var engine = new FakeEngine { Available = false };
        var queue = new OcrQueue(world.Db, world.Documents, engine);

        var id = await ImportAsync(world, engine, "وارد.png", 3);
        queue.Enqueue(id);
        await queue.DrainAsync();

        // Left waiting on purpose: the files may arrive later and the document is read then,
        // without anybody having to ask again.
        Assert.Equal(OcrStatus.Pending, (await world.Documents.GetAsync(id))!.OcrStatus);
        Assert.Equal(0, await queue.ReprocessChangedModelAsync());
    }

    [Fact]
    public async Task A_word_document_is_settled_as_unreadable_rather_than_waiting_for_ever()
    {
        using var world = new DocumentsWorld();
        var engine = new FakeEngine();
        var queue = new OcrQueue(world.Db, world.Documents, engine);

        var docx = new byte[] { 0x50, 0x4B, 0x03, 0x04, 9, 8, 7, 6 };
        var id = (await world.Documents.ImportAsync(docx, "خطاب.docx")).DocumentId;

        queue.Enqueue(id);
        await queue.DrainAsync();

        Assert.Equal(OcrStatus.Unsupported, (await world.Documents.GetAsync(id))!.OcrStatus);
        Assert.Empty(engine.Read);
    }

    [Fact]
    public async Task A_damaged_file_leaves_the_record_alone_and_says_the_reading_failed()
    {
        using var world = new DocumentsWorld();
        var engine = new FakeEngine();
        var queue = new OcrQueue(world.Db, world.Documents, engine);

        var import = await world.Documents.ImportAsync(Png(64), "تالف.png");
        var path = world.VaultFile(import.Sha256Hex!);
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(path, bytes);

        queue.Enqueue(import.DocumentId);
        await queue.DrainAsync();

        Assert.Equal(OcrStatus.Failed, (await world.Documents.GetAsync(import.DocumentId))!.OcrStatus);
        Assert.NotNull(await world.Documents.GetAsync(import.DocumentId));
        Assert.Empty(engine.Read);
    }

    [Fact]
    public async Task Reading_one_document_now_takes_it_out_of_the_line()
    {
        using var world = new DocumentsWorld();
        var engine = new FakeEngine();
        var queue = new OcrQueue(world.Db, world.Documents, engine);

        var first = await ImportAsync(world, engine, "أ.png", 10);
        var second = await ImportAsync(world, engine, "ب.png", 20);
        queue.Enqueue(first);
        queue.Enqueue(second);

        var result = await queue.ReadNowAsync(second);

        Assert.Equal(OcrState.Ok, result.State);
        Assert.Equal(new[] { first }, queue.Pending);
        Assert.Equal(new[] { second }, engine.Read);
    }

    [Fact]
    public async Task An_import_leaves_the_document_waiting_so_a_later_resume_finds_it()
    {
        using var world = new DocumentsWorld();
        var engine = new FakeEngine();
        await ImportAsync(world, engine, "أ.png", 10);
        await ImportAsync(world, engine, "ب.png", 20);

        Assert.Equal(2, await world.Db.Documents.CountAsync(d => d.OcrStatus == OcrStatus.Pending));

        var queue = new OcrQueue(world.Db, world.Documents, engine);
        Assert.Equal(2, await queue.ResumeAsync());
    }

    /// <summary>
    /// A reader that answers instantly with made-up pages, so the queue's own behaviour can be
    /// asserted without the model files and without a second per page. It remembers which
    /// document each set of bytes belongs to, because the queue hands it bytes and nothing else.
    /// </summary>
    private sealed class FakeEngine : IOcrEngine
    {
        private readonly Dictionary<string, Guid> _known = new(StringComparer.Ordinal);

        /// <summary>The documents that were read, in the order they were taken.</summary>
        public List<Guid> Read { get; } = [];

        public bool Available { get; set; } = true;

        public int PagesPerDocument { get; set; } = 1;

        public string? Fingerprint { get; set; } = "test";

        /// <summary>
        /// The page at which the run is stopped — what signing out or shutting down does to a
        /// twenty-page scan halfway through. The pages already read come back with the run, the
        /// way the real reader hands back whatever it had when the token went.
        /// </summary>
        public int? StopAfterPage { get; set; }

        /// <summary>What <see cref="StopAfterPage"/> cancels, so the token really is cancelled.</summary>
        public CancellationTokenSource? StopSource { get; set; }

        public bool IsAvailable => Available;

        public string? ModelFingerprint => Fingerprint;

        /// <summary>Tells the reader which document a set of bytes belongs to.</summary>
        public void Expect(Guid documentId, byte[] content) => _known[Key(content)] = documentId;

        public Task<OcrDocumentResult> ReadAsync(
            byte[] content,
            string mediaType,
            IProgress<OcrPageProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (_known.TryGetValue(Key(content), out var id))
            {
                Read.Add(id);
            }

            var pages = new List<OcrPageResult>();
            for (var page = 1; page <= PagesPerDocument; page++)
            {
                progress?.Report(new OcrPageProgress(page, PagesPerDocument));
                pages.Add(new OcrPageResult(
                    page,
                    "نص الصفحة " + page.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    0.9,
                    [new OcrWordBox("نص", 0.9, 1, 2, 3, 4)]));

                if (StopAfterPage == page)
                {
                    StopSource?.Cancel();
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    // What OcrService does with the pages it already has: they come back with the
                    // run rather than being thrown away, and the state says it did not finish.
                    return Task.FromResult(new OcrDocumentResult(OcrState.Cancelled, pages));
                }
            }

            return Task.FromResult(new OcrDocumentResult(OcrState.Ok, pages));
        }

        private static string Key(byte[] content) => Convert.ToHexString(SHA256.HashData(content));
    }
}
