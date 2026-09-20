using System.Text;
using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Services;
using Wakeel.Core.Services.Documents;

namespace Wakeel.Core.Tests.Documents;

/// <summary>
/// Importing, linking and scanning (B3-2): what may be stored, what is refused and in what words,
/// and what the office sees when it adds the same file twice.
/// </summary>
public sealed class DocumentServiceTests
{
    /// <summary>The eight bytes every PNG begins with; the import path checks them.</summary>
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The smallest thing that is really a PNG: the signature plus a little payload.</summary>
    private static byte[] Png(int payload = 64) => PngOfSize(8 + payload);

    /// <summary>A PNG of exactly this many bytes, for the tests about the size limit.</summary>
    private static byte[] PngOfSize(int size)
    {
        var bytes = new byte[size];
        PngSignature.CopyTo(bytes, 0);
        Random.Shared.NextBytes(bytes.AsSpan(PngSignature.Length));
        return bytes;
    }

    private static byte[] Pdf(string body = "one page")
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.4\n" + body);
        return bytes;
    }

    [Fact]
    public async Task An_imported_picture_becomes_a_document_waiting_to_be_read()
    {
        using var world = new DocumentsWorld();

        var result = await world.Documents.ImportAsync(Png(), "مسح-الوارد.png");

        Assert.Equal(DocumentImportState.Added, result.State);
        Assert.NotEqual(Guid.Empty, result.DocumentId);
        Assert.Contains("مسح-الوارد.png", result.MessageAr, StringComparison.Ordinal);

        var document = await world.Documents.GetAsync(result.DocumentId);
        Assert.NotNull(document);
        Assert.Equal(DocumentMediaTypes.Png, document.Mime);
        Assert.Equal(DocumentSource.Import, document.Source);
        Assert.Equal(OcrStatus.Pending, document.OcrStatus);
        Assert.False(document.PinnedOnPhone);
        Assert.Equal(result.Sha256Hex, document.Sha256);
    }

    [Fact]
    public async Task A_word_document_is_stored_but_never_queued_for_reading()
    {
        using var world = new DocumentsWorld();

        // A .docx is a ZIP; the name is what says which kind of ZIP it is.
        var docx = new byte[] { 0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4, 5, 6 };
        var result = await world.Documents.ImportAsync(docx, "خطاب.docx");

        Assert.Equal(DocumentImportState.Added, result.State);
        var document = await world.Documents.GetAsync(result.DocumentId);
        Assert.Equal(OcrStatus.Unsupported, document!.OcrStatus);
    }

    [Fact]
    public async Task An_empty_file_is_refused_in_words()
    {
        using var world = new DocumentsWorld();

        var result = await world.Documents.ImportAsync([], "فارغ.pdf");

        Assert.Equal(DocumentImportState.Empty, result.State);
        Assert.Equal(Guid.Empty, result.DocumentId);
        Assert.Contains("فارغ", result.MessageAr, StringComparison.Ordinal);
        Assert.Empty(await world.Db.Documents.ToListAsync());
    }

    [Fact]
    public async Task A_file_over_twenty_megabytes_is_refused_and_its_size_is_said_in_western_digits()
    {
        using var world = new DocumentsWorld();
        var tooBig = PngOfSize((int)DocumentMediaTypes.MaxSizeBytes + 1);

        var result = await world.Documents.ImportAsync(tooBig, "ضخم.png");

        Assert.Equal(DocumentImportState.TooLarge, result.State);
        Assert.Contains("20", result.MessageAr, StringComparison.Ordinal);

        // AGREEMENT item 20: every numeral الوكيل writes is a western digit.
        Assert.False(ArabicOcrText.HasArabicIndicDigits(result.MessageAr));
        Assert.Empty(await world.Db.Documents.ToListAsync());

        // Exactly the limit is allowed; it is a limit, not a boundary to be shy of.
        var atLimit = PngOfSize((int)DocumentMediaTypes.MaxSizeBytes);
        Assert.Equal(DocumentImportState.Added, (await world.Documents.ImportAsync(atLimit, "على-الحد.png")).State);
    }

    [Theory]
    [InlineData("برنامج.exe")]
    [InlineData("جدول.xlsx")]
    [InlineData("بلا-امتداد")]
    public async Task A_kind_the_office_does_not_keep_is_refused(string fileName)
    {
        using var world = new DocumentsWorld();

        var result = await world.Documents.ImportAsync(Png(), fileName);

        Assert.Equal(DocumentImportState.KindNotAccepted, result.State);
        Assert.Empty(await world.Db.Documents.ToListAsync());
    }

    [Fact]
    public async Task A_file_whose_name_disagrees_with_its_bytes_is_refused()
    {
        using var world = new DocumentsWorld();

        // Anything at all renamed to .pdf would otherwise reach the viewer as a document.
        var result = await world.Documents.ImportAsync(Png(), "متنكّر.pdf");

        Assert.Equal(DocumentImportState.KindNotAccepted, result.State);
    }

    [Fact]
    public async Task The_same_file_added_twice_is_one_document_and_one_stored_copy()
    {
        using var world = new DocumentsWorld();
        var content = Png(200);

        var first = await world.Documents.ImportAsync(content, "مرفق.png");
        var second = await world.Documents.ImportAsync(content, "نسخة-أخرى-من-المرفق.png");

        Assert.Equal(DocumentImportState.Added, first.State);
        Assert.Equal(DocumentImportState.AlreadyStored, second.State);
        Assert.Equal(first.DocumentId, second.DocumentId);
        Assert.Single(await world.Db.Documents.ToListAsync());
        Assert.Single(Directory.GetFiles(world.Paths.VaultDir, "*.bin", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task The_same_file_on_two_letters_is_one_document_with_two_links()
    {
        using var world = new DocumentsWorld();
        var content = Png(300);
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        var a = await world.Documents.ImportAsync(
            content, "مرفق.png", DocumentSource.Import, new DocumentLinkTarget(DocumentLinkTypes.Correspondence, first));
        var b = await world.Documents.ImportAsync(
            content, "مرفق.png", DocumentSource.Import, new DocumentLinkTarget(DocumentLinkTypes.Correspondence, second));

        Assert.Equal(a.DocumentId, b.DocumentId);
        var links = await world.Documents.ListLinksAsync(a.DocumentId);
        Assert.Equal(2, links.Count);

        Assert.Single(await world.Documents.ListForAsync(new DocumentLinkTarget(DocumentLinkTypes.Correspondence, first)));
        Assert.Single(await world.Documents.ListForAsync(new DocumentLinkTarget(DocumentLinkTypes.Correspondence, second)));
    }

    [Fact]
    public async Task Linking_twice_changes_nothing_and_unlinking_leaves_the_document_alone()
    {
        using var world = new DocumentsWorld();
        var meeting = new DocumentLinkTarget(DocumentLinkTypes.Meeting, Guid.CreateVersion7());
        var document = await world.Documents.ImportAsync(Pdf(), "محضر.pdf", DocumentSource.Import, meeting);

        Assert.False(await world.Documents.LinkAsync(document.DocumentId, meeting));
        Assert.True(await world.Documents.UnlinkAsync(document.DocumentId, meeting));
        Assert.Empty(await world.Documents.ListForAsync(meeting));

        // The document itself is still there and still readable.
        Assert.NotNull(await world.Documents.GetAsync(document.DocumentId));
        Assert.Equal(VaultState.Ok, await world.Documents.VerifyAsync(document.DocumentId));

        // And it can be attached again, which must not collide with the row that was hidden.
        Assert.True(await world.Documents.LinkAsync(document.DocumentId, meeting));
        Assert.Single(await world.Documents.ListForAsync(meeting));
    }

    [Fact]
    public async Task A_link_to_something_that_is_not_a_known_kind_is_a_defect()
    {
        using var world = new DocumentsWorld();
        var document = await world.Documents.ImportAsync(Pdf(), "ملف.pdf");

        await Assert.ThrowsAsync<ArgumentException>(
            () => world.Documents.LinkAsync(document.DocumentId, new DocumentLinkTarget("invoices", Guid.CreateVersion7())));
    }

    [Fact]
    public async Task A_derived_copy_points_back_at_its_original_and_never_replaces_it()
    {
        using var world = new DocumentsWorld();
        var original = await world.Documents.ImportAsync(Pdf("original"), "الوارد.pdf");

        var derived = await world.Documents.SaveDerivedAsync(
            Pdf("with referral"), "الوارد-للإحالة.pdf", DocumentMediaTypes.Pdf, original.DocumentId);

        Assert.Equal(DocumentImportState.Added, derived.State);
        var copies = await world.Documents.ListDerivedAsync(original.DocumentId);
        Assert.Single(copies);
        Assert.Equal(derived.DocumentId, copies[0].Id);
        Assert.Equal(DocumentSource.Generated, copies[0].Source);

        var readOriginal = await world.Documents.ReadContentAsync(original.DocumentId);
        Assert.Equal(Pdf("original"), readOriginal.Content);
    }

    [Fact]
    public async Task A_document_can_be_pinned_for_the_phone_and_unpinned_again()
    {
        using var world = new DocumentsWorld();
        var document = await world.Documents.ImportAsync(Pdf(), "قرار.pdf");

        Assert.True(await world.Documents.SetPinnedOnPhoneAsync(document.DocumentId, true));
        Assert.False(await world.Documents.SetPinnedOnPhoneAsync(document.DocumentId, true));
        Assert.Single(await world.Documents.ListPinnedAsync());

        Assert.True(await world.Documents.SetPinnedOnPhoneAsync(document.DocumentId, false));
        Assert.Empty(await world.Documents.ListPinnedAsync());
    }

    [Fact]
    public async Task A_damaged_document_still_has_its_record_and_says_so()
    {
        using var world = new DocumentsWorld();
        var document = await world.Documents.ImportAsync(Pdf("سليم"), "خطاب.pdf");

        var path = world.VaultFile(document.Sha256Hex!);
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[^2] ^= 0xFF;
        await File.WriteAllBytesAsync(path, bytes);

        Assert.Equal(VaultState.Corrupt, await world.Documents.VerifyAsync(document.DocumentId));
        Assert.Equal(VaultState.Corrupt, (await world.Documents.ReadContentAsync(document.DocumentId)).State);
        Assert.NotNull(await world.Documents.GetAsync(document.DocumentId));
    }

    [Fact]
    public async Task A_file_read_from_the_disk_is_stored_the_same_way()
    {
        using var world = new DocumentsWorld();
        var path = Path.Combine(world.Root, "من-القرص.png");
        await File.WriteAllBytesAsync(path, Png(120));

        var result = await world.Documents.ImportFileAsync(path);

        Assert.Equal(DocumentImportState.Added, result.State);
        var document = await world.Documents.GetAsync(result.DocumentId);
        Assert.Equal("من-القرص.png", document!.OriginalName);
    }

    [Fact]
    public async Task A_file_that_is_not_there_is_refused_without_throwing()
    {
        using var world = new DocumentsWorld();

        var result = await world.Documents.ImportFileAsync(Path.Combine(world.Root, "غير-موجود.pdf"));

        Assert.Equal(DocumentImportState.Empty, result.State);
    }

    [Fact]
    public async Task Without_a_scanner_the_answer_is_words_and_not_a_failure()
    {
        using var world = new DocumentsWorld();

        var result = await world.Documents.ScanAsync(new ScanSettings());

        Assert.Equal(ScanState.NoScanner, result.ScanState);
        Assert.False(result.IsStored);
        Assert.Equal(CoreAr.Documents.NoScannerMessage, result.MessageAr);
        Assert.Empty(await world.Documents.ListScannersAsync());
    }

    [Fact]
    public async Task A_machine_that_has_a_scanner_answers_the_words_only_when_it_really_has_none()
    {
        var scanner = new FakeScanner(Png(700)) { HasDevice = false };
        using var world = new DocumentsWorld(scanner: scanner);

        var none = await world.Documents.ScanAsync(new ScanSettings());
        Assert.Equal(ScanState.NoScanner, none.ScanState);
        Assert.Equal(CoreAr.Documents.NoScannerMessage, none.MessageAr);
    }

    [Fact]
    public async Task Scanning_before_the_device_list_was_ever_asked_for_still_scans()
    {
        var scanner = new FakeScanner(Png(701));
        using var world = new DocumentsWorld(scanner: scanner);

        // Nothing has looked at the machine yet, so the scanner's own cached answer is still
        // «I have not seen one». That must not become «لا ماسح ضوئي»: asking it to scan is
        // what looks, and a person who opens the dialog and presses scan has asked.
        Assert.False(scanner.IsAvailable);

        var result = await world.Documents.ScanAsync(new ScanSettings());

        Assert.Equal(ScanState.Ok, result.ScanState);
        Assert.True(result.IsStored);
        Assert.Equal(1, result.Pages);
    }

    [Fact]
    public async Task The_audit_line_names_the_person_who_added_the_file()
    {
        using var world = new DocumentsWorld();

        await world.Documents.ImportAsync(Png(702), "وارد.png");

        var entry = Assert.Single(await world.Db.AuditLog.Where(a => a.Action == "document.added").ToListAsync());
        Assert.Equal(world.Installation.EmployeeName, entry.Actor);
        Assert.False(string.IsNullOrWhiteSpace(entry.SummaryAr));
    }

    [Fact]
    public async Task One_scanned_sheet_becomes_one_picture_document()
    {
        var scanner = new FakeScanner(Png(500));
        using var world = new DocumentsWorld(scanner: scanner);

        var seen = new List<int>();
        var result = await world.Documents.ScanAsync(
            new ScanSettings(DeviceId: "test-1", Dpi: 400, Color: ScanColorMode.Gray),
            pageScanned: new Progress<int>(seen.Add));

        Assert.Equal(ScanState.Ok, result.ScanState);
        Assert.True(result.IsStored);
        Assert.Equal(1, result.Pages);

        var document = await world.Documents.GetAsync(result.Import!.DocumentId);
        Assert.Equal(DocumentSource.Scan, document!.Source);
        Assert.Equal(DocumentMediaTypes.Png, document.Mime);

        // The settings reach the driver unchanged.
        Assert.Equal(400, scanner.LastSettings!.Dpi);
        Assert.Equal(ScanColorMode.Gray, scanner.LastSettings.Color);
    }

    [Fact]
    public async Task Several_sheets_become_one_pdf_when_a_binder_is_registered()
    {
        var scanner = new FakeScanner(Png(100), Png(110), Png(120));
        using var world = new DocumentsWorld(scanner: scanner, pdf: new StubPdfWriter());

        var result = await world.Documents.ScanAsync(new ScanSettings(MultiPage: true));

        Assert.Equal(3, result.Pages);
        Assert.True(result.IsStored);
        Assert.Single(await world.Db.Documents.ToListAsync());

        var document = await world.Documents.GetAsync(result.Import!.DocumentId);
        Assert.Equal(DocumentMediaTypes.Pdf, document!.Mime);
        Assert.Contains("3", result.MessageAr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_binder_several_sheets_are_kept_as_separate_pictures()
    {
        var scanner = new FakeScanner(Png(100), Png(110));
        using var world = new DocumentsWorld(scanner: scanner);

        var result = await world.Documents.ScanAsync(new ScanSettings(MultiPage: true));

        Assert.Equal(2, result.Pages);
        Assert.Equal(2, (await world.Db.Documents.ToListAsync()).Count);
    }

    [Fact]
    public async Task Sheets_the_binder_cannot_make_a_document_of_are_kept_as_separate_pictures()
    {
        var scanner = new FakeScanner(Png(130), Png(140));
        using var world = new DocumentsWorld(scanner: scanner, pdf: new RefusingPdfWriter());

        var result = await world.Documents.ScanAsync(new ScanSettings(MultiPage: true));

        // Rather than storing a PDF with nothing inside it, the sheets are kept as they came.
        Assert.Equal(2, result.Pages);
        Assert.True(result.IsStored);
        var documents = await world.Db.Documents.ToListAsync();
        Assert.Equal(2, documents.Count);
        Assert.All(documents, document => Assert.Equal(DocumentMediaTypes.Png, document.Mime));
    }

    [Fact]
    public async Task A_preview_never_reaches_the_vault()
    {
        var scanner = new FakeScanner(Png(90));
        using var world = new DocumentsWorld(scanner: scanner);

        var preview = await world.Documents.PreviewScanAsync(new ScanSettings(Dpi: 600));

        Assert.Equal(ScanState.Ok, preview.State);
        Assert.Single(preview.Pages);
        Assert.Equal(ScanSettings.PreviewDpi, scanner.LastSettings!.Dpi);
        Assert.Empty(await world.Db.Documents.ToListAsync());
    }

    [Fact]
    public async Task Read_pages_are_written_once_per_page_and_replaced_on_a_re_read()
    {
        using var world = new DocumentsWorld();
        var document = await world.Documents.ImportAsync(Pdf(), "ملف.pdf");

        await world.Documents.SavePagesAsync(
            document.DocumentId,
            [
                new OcrPageResult(1, "الصفحة الأولى", 0.9, [new OcrWordBox("الصفحة", 0.9, 10, 20, 30, 40)]),
                new OcrPageResult(2, "الصفحة الثانية", 0.8, []),
            ],
            "ara+eng/abc");

        var stored = await world.Documents.GetAsync(document.DocumentId);
        Assert.Equal(OcrStatus.Done, stored!.OcrStatus);
        Assert.Equal(2, stored.PageCount);
        Assert.Equal("ara+eng/abc", stored.OcrLang);

        var pages = await world.Documents.ListPagesAsync(document.DocumentId);
        Assert.Equal(2, pages.Count);
        var words = DocumentService.DeserializeWords(pages[0].Words);
        Assert.Single(words);
        Assert.Equal("الصفحة", words[0].Text);
        Assert.Equal(30, words[0].Width);

        // A shorter re-read hides the pages nobody can reach any more and does not duplicate the rest.
        await world.Documents.SavePagesAsync(
            document.DocumentId,
            [new OcrPageResult(1, "أعيدت القراءة", 0.95, [])],
            "ara+eng/def");

        pages = await world.Documents.ListPagesAsync(document.DocumentId);
        Assert.Single(pages);
        Assert.Equal("أعيدت القراءة", pages[0].Text);
        Assert.Equal(1, (await world.Documents.GetAsync(document.DocumentId))!.PageCount);
    }

    [Fact]
    public async Task A_scan_the_vault_could_not_take_says_so_instead_of_counting_sheets()
    {
        var scanner = new FakeScanner(Png(120), Png(130));
        using var world = new DocumentsWorld(scanner: scanner);

        // The place the originals live is out of reach — a file stands where the vault folder
        // should be, so nothing can be created underneath it. This is «الخزنة غير متاحة».
        var blocked = Path.Combine(world.Root, "blocked-vault");
        await File.WriteAllTextAsync(blocked, "not a folder");
        var vault = new VaultStore(WakeelPaths.ForRoot(blocked), world.Keys);
        var documents = new DocumentService(
            world.Db, vault, world.Clock, world.Ids, world.Audit, scanner, pdf: null);

        var result = await documents.ScanAsync(new ScanSettings(MultiPage: true));

        // Nothing was saved, so the office must not be told that the sheets were scanned and filed.
        Assert.False(result.IsStored);
        Assert.Equal(CoreAr.Documents.VaultUnavailableMessage, result.MessageAr);
        Assert.Equal(DocumentImportState.VaultUnavailable, result.Import!.State);
        Assert.Empty(await world.Db.Documents.ToListAsync());
    }

    [Fact]
    public async Task A_vault_that_goes_out_of_reach_halfway_never_counts_the_sheets_it_lost()
    {
        var scanner = new FakeScanner(Png(140), Png(150), Png(160));
        using var world = new DocumentsWorld(scanner: scanner);

        // No binder, so the three sheets are stored one by one; the drive goes out of reach after
        // the first of them.
        var vault = new FailingAfterStore(world.Vault, 1);
        var documents = new DocumentService(
            world.Db, vault, world.Clock, world.Ids, world.Audit, scanner, pdf: null);

        var result = await documents.ScanAsync(new ScanSettings(MultiPage: true));

        Assert.False(result.IsStored);
        Assert.Equal(CoreAr.Documents.VaultUnavailableMessage, result.MessageAr);
        Assert.Single(await world.Db.Documents.ToListAsync());
    }

    /// <summary>
    /// The vault for as long as the drive is there, and «الخزنة غير متاحة» from every write after
    /// that — an external disk unplugged in the middle of a stack of sheets.
    /// </summary>
    private sealed class FailingAfterStore(IDocumentStore inner, int writes) : IDocumentStore
    {
        private int _left = writes;

        public bool IsAvailable => _left > 0 && inner.IsAvailable;

        public bool Exists(string sha256Hex) => _left > 0 && inner.Exists(sha256Hex);

        public Task<VaultWriteResult> WriteAsync(Stream content, CancellationToken cancellationToken = default) =>
            Take() ? inner.WriteAsync(content, cancellationToken) : throw Gone();

        public Task<VaultWriteResult> WriteAsync(ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default) =>
            Take() ? inner.WriteAsync(content, cancellationToken) : throw Gone();

        public Task<VaultCopyResult> CopyToAsync(string sha256Hex, Stream destination, CancellationToken cancellationToken = default) =>
            _left > 0
                ? inner.CopyToAsync(sha256Hex, destination, cancellationToken)
                : Task.FromResult(new VaultCopyResult(VaultState.Unavailable, 0));

        public Task<VaultReadResult> ReadAsync(string sha256Hex, CancellationToken cancellationToken = default) =>
            _left > 0 ? inner.ReadAsync(sha256Hex, cancellationToken) : Task.FromResult(VaultReadResult.Unavailable);

        public Task<VaultState> VerifyAsync(string sha256Hex, CancellationToken cancellationToken = default) =>
            _left > 0 ? inner.VerifyAsync(sha256Hex, cancellationToken) : Task.FromResult(VaultState.Unavailable);

        public void Delete(string sha256Hex)
        {
            if (_left > 0)
            {
                inner.Delete(sha256Hex);
            }
        }

        private bool Take()
        {
            if (_left <= 0)
            {
                return false;
            }

            _left--;
            return true;
        }

        private static VaultUnavailableException Gone() =>
            new("The drive holding the vault was unplugged.");
    }

    /// <summary>Stands in for the real binder: it only has to produce something that is a PDF.</summary>
    private sealed class StubPdfWriter : IScanPdfWriter
    {
        public byte[] Combine(IReadOnlyList<ScannedPage> pages) =>
            Encoding.ASCII.GetBytes("%PDF-1.4\n" + pages.Count);
    }

    /// <summary>
    /// A binder that cannot make a document of these sheets — what the real one does when none of
    /// the frames the driver handed over is a picture it can read.
    /// </summary>
    private sealed class RefusingPdfWriter : IScanPdfWriter
    {
        public byte[] Combine(IReadOnlyList<ScannedPage> pages) =>
            throw new ArgumentException("A PDF is bound from at least one sheet.", nameof(pages));
    }
}
