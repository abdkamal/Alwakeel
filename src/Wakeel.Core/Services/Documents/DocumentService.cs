using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;

namespace Wakeel.Core.Services.Documents;

/// <summary>
/// Everything the office does with a stored file (B3-2): adding one from the disk, scanning one,
/// attaching it to the correspondence or the meeting it belongs to, keeping the derived print
/// copies pointed at their original, tracking whether its text has been read, and marking the few
/// it wants to carry on the phone.
/// </summary>
/// <remarks>
/// The bytes and the record are two different things and are deliberately written in that order:
/// the vault first, the row second. A row pointing at bytes that were never written would be a
/// document the office can see and never open; bytes with no row are invisible and cost only
/// space, and the next import of the same file adopts them.
/// </remarks>
public interface IDocumentService
{
    /// <summary>
    /// Stores a file. Refuses what is empty, over 20 MB, or not one of the four kinds الوكيل
    /// keeps; stores nothing twice; and attaches the result to <paramref name="link"/> when one
    /// is given.
    /// </summary>
    /// <param name="content">The file's bytes.</param>
    /// <param name="fileName">The name it arrived under, kept as it is for the documents list.</param>
    /// <param name="source">Where it came from.</param>
    /// <param name="link">What to attach it to, if anything.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<DocumentImportResult> ImportAsync(
        byte[] content,
        string fileName,
        DocumentSource source = DocumentSource.Import,
        DocumentLinkTarget? link = null,
        CancellationToken cancellationToken = default);

    /// <summary>Stores a file from the disk, reading it once.</summary>
    Task<DocumentImportResult> ImportFileAsync(
        string path,
        DocumentSource source = DocumentSource.Import,
        DocumentLinkTarget? link = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans and stores the result: one image for a single sheet, one PDF for several when a
    /// <see cref="IScanPdfWriter"/> is registered. A machine with no scanner stores nothing and
    /// answers <see cref="ScanState.NoScanner"/> with the «لا ماسح ضوئي» sentence, so the
    /// dialog's other two ways in stay open.
    /// </summary>
    Task<DocumentScanImport> ScanAsync(
        ScanSettings settings,
        DocumentLinkTarget? link = null,
        IProgress<int>? pageScanned = null,
        CancellationToken cancellationToken = default);

    /// <summary>The scanners the machine can see; empty when there are none.</summary>
    Task<IReadOnlyList<ScannerDevice>> ListScannersAsync(CancellationToken cancellationToken = default);

    /// <summary>One quick page for the preview pane of W45. Never stored.</summary>
    Task<ScanResult> PreviewScanAsync(ScanSettings settings, CancellationToken cancellationToken = default);

    /// <summary>One document's record, or <c>null</c> when there is none.</summary>
    Task<Document?> GetAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>One document's stored bytes, with the vault's own answer when they cannot be had.</summary>
    Task<VaultReadResult> ReadContentAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One document's stored bytes, streamed into <paramref name="destination"/> — what exporting
    /// the original and printing it use, so a 20 MB scan never sits in memory twice.
    /// </summary>
    Task<VaultCopyResult> CopyContentToAsync(Guid documentId, Stream destination, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether one document's stored bytes are still exactly what was put in — the «السلامة»
    /// column of W43 and the health centre's document check.
    /// </summary>
    Task<VaultState> VerifyAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>Attaches a document to an entity. Attaching it twice is not an error and changes nothing.</summary>
    Task<bool> LinkAsync(Guid documentId, DocumentLinkTarget target, CancellationToken cancellationToken = default);

    /// <summary>Detaches a document from an entity, leaving the document and its bytes alone.</summary>
    Task<bool> UnlinkAsync(Guid documentId, DocumentLinkTarget target, CancellationToken cancellationToken = default);

    /// <summary>Every document attached to one entity, newest first.</summary>
    Task<IReadOnlyList<Document>> ListForAsync(DocumentLinkTarget target, CancellationToken cancellationToken = default);

    /// <summary>What a document is attached to.</summary>
    Task<IReadOnlyList<DocumentLink>> ListLinksAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a document الوكيل produced — a letter, a referral print copy, a report — pointing
    /// back at the document it was derived from when there is one (AGREEMENT item 31: the
    /// original is never touched).
    /// </summary>
    Task<DocumentImportResult> SaveDerivedAsync(
        byte[] content,
        string fileName,
        string mediaType,
        Guid? derivedFromId = null,
        DocumentLinkTarget? link = null,
        CancellationToken cancellationToken = default);

    /// <summary>The derived copies that point back at one document.</summary>
    Task<IReadOnlyList<Document>> ListDerivedAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a document to be carried on the phone, or stops carrying it (AGREEMENT item 41: the
    /// phone holds what was pinned, not the whole vault).
    /// </summary>
    Task<bool> SetPinnedOnPhoneAsync(Guid documentId, bool pinned, CancellationToken cancellationToken = default);

    /// <summary>The documents pinned for the phone.</summary>
    Task<IReadOnlyList<Document>> ListPinnedAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets a document's OCR state; what the queue writes as it starts and finishes.</summary>
    Task SetOcrStatusAsync(Guid documentId, OcrStatus status, string? ocrLang = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes what was read from one document: one <c>document_pages</c> row per page, replacing
    /// whatever was there before, and the document's page count and OCR state with it.
    /// </summary>
    Task SavePagesAsync(
        Guid documentId,
        IReadOnlyList<OcrPageResult> pages,
        string ocrLang,
        CancellationToken cancellationToken = default);

    /// <summary>One document's read pages, in order.</summary>
    Task<IReadOnlyList<DocumentPage>> ListPagesAsync(Guid documentId, CancellationToken cancellationToken = default);
}

/// <summary>What a scan-and-store produced.</summary>
/// <param name="ScanState">How the scan itself ended.</param>
/// <param name="Import">What storing it produced, or <c>null</c> when there was nothing to store.</param>
/// <param name="Pages">How many sheets came back.</param>
/// <param name="MessageAr">The finished Arabic sentence for the dialog.</param>
public sealed record DocumentScanImport(
    ScanState ScanState,
    DocumentImportResult? Import,
    int Pages,
    string MessageAr)
{
    /// <summary>Whether a document exists at the end of it.</summary>
    public bool IsStored => Import is { IsStored: true };
}

/// <inheritdoc cref="IDocumentService"/>
/// <remarks>
/// <para>
/// <b>What a file is.</b> A file is accepted only when its name and its first bytes agree on one
/// of the four kinds. A name alone would let anything into the vault under a name that makes the
/// viewer try to open it; bytes alone cannot tell a .docx from any other ZIP.
/// </para>
/// <para>
/// <b>De-duplication.</b> The vault files bytes under their own hash, so the same attachment on
/// two letters costs one copy. The document record is de-duplicated with it: an import whose hash
/// already has a record returns that record and links it, so the two letters point at one
/// document and its read text is not produced twice.
/// </para>
/// <para>
/// <b>The scanner and the PDF writer are optional.</b> Neither exists in Core, and an
/// installation without them still imports files from the disk and from the phone. A missing
/// scanner is the «لا ماسح ضوئي» state; a missing PDF writer stores a multi-page scan as its
/// separate images rather than losing the pages.
/// </para>
/// </remarks>
public sealed class DocumentService : IDocumentService
{
    /// <summary>What <c>ocr_lang</c> says when a page was read by Tesseract's two languages.</summary>
    public const string OcrLanguages = "ara+eng";

    private readonly WakeelDb _db;
    private readonly IDocumentStore _vault;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly IAuditService _audit;
    private readonly IScanner? _scanner;
    private readonly IScanPdfWriter? _pdf;
    private string? _actor;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The open database.</param>
    /// <param name="vault">The encrypted file store.</param>
    /// <param name="clock">The clock the stamps come from.</param>
    /// <param name="ids">The id generator.</param>
    /// <param name="audit">The audit log.</param>
    /// <param name="scanner">The machine's scanner, when it has one.</param>
    /// <param name="pdf">The page binder, when one is registered.</param>
    public DocumentService(
        WakeelDb db,
        IDocumentStore vault,
        IClock clock,
        IIdGenerator ids,
        IAuditService audit,
        IScanner? scanner = null,
        IScanPdfWriter? pdf = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(audit);
        _db = db;
        _vault = vault;
        _clock = clock;
        _ids = ids;
        _audit = audit;
        _scanner = scanner;
        _pdf = pdf;
    }

    /// <inheritdoc />
    public Task<DocumentImportResult> ImportAsync(
        byte[] content,
        string fileName,
        DocumentSource source = DocumentSource.Import,
        DocumentLinkTarget? link = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var name = Path.GetFileName(fileName.Trim());
        if (content.Length == 0)
        {
            return Task.FromResult(new DocumentImportResult(
                DocumentImportState.Empty, Guid.Empty, CoreAr.Documents.FileEmpty(name)));
        }

        if (content.LongLength > DocumentMediaTypes.MaxSizeBytes)
        {
            return Task.FromResult(new DocumentImportResult(
                DocumentImportState.TooLarge, Guid.Empty, CoreAr.Documents.FileTooLarge(name, content.LongLength)));
        }

        var mediaType = Classify(name, content);
        if (mediaType is null)
        {
            return Task.FromResult(new DocumentImportResult(
                DocumentImportState.KindNotAccepted, Guid.Empty, CoreAr.Documents.FileKindNotAccepted(name)));
        }

        return StoreAsync(content, name, mediaType, source, derivedFromId: null, link, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DocumentImportResult> ImportFileAsync(
        string path,
        DocumentSource source = DocumentSource.Import,
        DocumentLinkTarget? link = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var name = Path.GetFileName(path);

        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists)
            {
                return new DocumentImportResult(
                    DocumentImportState.Empty, Guid.Empty, CoreAr.Documents.FileEmpty(name));
            }

            // Checked before the file is read, so a 2 GB file is refused without first being
            // pulled through memory to find out how big it is.
            if (info.Length > DocumentMediaTypes.MaxSizeBytes)
            {
                return new DocumentImportResult(
                    DocumentImportState.TooLarge, Guid.Empty, CoreAr.Documents.FileTooLarge(name, info.Length));
            }

            var content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            return await ImportAsync(content, name, source, link, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new DocumentImportResult(
                DocumentImportState.Empty, Guid.Empty, CoreAr.Documents.FileEmpty(name));
        }
    }

    /// <inheritdoc />
    public async Task<DocumentScanImport> ScanAsync(
        ScanSettings settings,
        DocumentLinkTarget? link = null,
        IProgress<int>? pageScanned = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Only "no scanner support in this host" is decided here. Whether a machine that has the
        // support also has a device is the scanner's own answer: asking it is what looks, and a
        // cached "I have not seen one yet" must never become «لا ماسح ضوئي» before anybody looked.
        if (_scanner is null)
        {
            return new DocumentScanImport(ScanState.NoScanner, null, 0, CoreAr.Documents.NoScannerMessage);
        }

        var scan = await _scanner.ScanAsync(settings, pageScanned, cancellationToken).ConfigureAwait(false);
        if (scan.Pages.Count == 0)
        {
            var message = scan.State switch
            {
                ScanState.NoScanner => CoreAr.Documents.NoScannerMessage,
                ScanState.Failed => CoreAr.Documents.ScanFailedMessage,
                _ => CoreAr.Documents.ScanNoPages,
            };

            return new DocumentScanImport(scan.State, null, 0, message);
        }

        var stamp = _clock.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        // Bound first, stored second, so a binder that cannot make a document out of these sheets
        // — every frame unreadable — leaves the sheets themselves to be stored one by one below
        // instead of filing a PDF with nothing inside it.
        byte[]? bound = null;
        if (scan.Pages.Count > 1 && _pdf is not null)
        {
            try
            {
                bound = _pdf.Combine(scan.Pages);
            }
            catch (ArgumentException)
            {
                bound = null;
            }
        }

        // How many sheets are really in the vault at the end of this. The sentence must count
        // these and not the sheets the glass saw: a drive that went out of reach between the scan
        // and the write would otherwise be reported to the office as a finished scan.
        int stored;

        DocumentImportResult import;
        if (scan.Pages.Count == 1)
        {
            var page = scan.Pages[0];
            import = await StoreAsync(
                page.Content,
                "مسح-" + stamp + DocumentMediaTypes.ExtensionFor(page.MediaType),
                page.MediaType,
                DocumentSource.Scan,
                derivedFromId: null,
                link,
                cancellationToken).ConfigureAwait(false);
            stored = 1;
        }
        else if (bound is not null)
        {
            // Several sheets are one document: a letter printed on three pages is not three
            // documents, and the reader, the viewer and the search all want it whole.
            import = await StoreAsync(
                bound,
                "مسح-" + stamp + ".pdf",
                DocumentMediaTypes.Pdf,
                DocumentSource.Scan,
                derivedFromId: null,
                link,
                cancellationToken).ConfigureAwait(false);

            // One document was written, but it carries every sheet, so the office is told about
            // the sheets it fed.
            stored = scan.Pages.Count;
        }
        else
        {
            // No binder on this machine, or none of the sheets could be made into a page. Storing
            // them separately keeps the office's work rather than throwing it away.
            import = await StoreAsync(
                scan.Pages[0].Content,
                "مسح-" + stamp + "-1" + DocumentMediaTypes.ExtensionFor(scan.Pages[0].MediaType),
                scan.Pages[0].MediaType,
                DocumentSource.Scan,
                derivedFromId: null,
                link,
                cancellationToken).ConfigureAwait(false);

            stored = import.IsStored ? 1 : 0;

            for (var i = 1; i < scan.Pages.Count && import.IsStored; i++)
            {
                var page = scan.Pages[i];
                var sheet = await StoreAsync(
                    page.Content,
                    "مسح-" + stamp + "-" + (i + 1).ToString(CultureInfo.InvariantCulture) + DocumentMediaTypes.ExtensionFor(page.MediaType),
                    page.MediaType,
                    DocumentSource.Scan,
                    derivedFromId: null,
                    link,
                    cancellationToken).ConfigureAwait(false);

                // A sheet that could not be written stops the run and answers with its own words:
                // the sheets before it stay in the vault, and the office is told what happened
                // instead of counting paper that was never saved.
                if (!sheet.IsStored)
                {
                    return new DocumentScanImport(scan.State, sheet, scan.Pages.Count, sheet.MessageAr);
                }

                stored++;
            }
        }

        // The store's own words win: «لم نتمكّن من الوصول إلى مكان حفظ الملفات الأصلية» must reach
        // the office rather than a sentence that counts sheets nobody saved.
        if (!import.IsStored)
        {
            return new DocumentScanImport(scan.State, import, scan.Pages.Count, import.MessageAr);
        }

        // A scan the person stopped still kept its finished sheets, so the sentence counts what
        // was saved rather than reporting a failure over pages that are safely in the vault.
        return new DocumentScanImport(
            scan.State, import, scan.Pages.Count, CoreAr.Documents.ScanPagesDone(stored));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScannerDevice>> ListScannersAsync(CancellationToken cancellationToken = default) =>
        _scanner is null
            ? Task.FromResult<IReadOnlyList<ScannerDevice>>([])
            : _scanner.ListDevicesAsync(cancellationToken);

    /// <inheritdoc />
    public Task<ScanResult> PreviewScanAsync(ScanSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return _scanner is null
            ? Task.FromResult(ScanResult.NoScanner)
            : _scanner.PreviewAsync(settings, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Document?> GetAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

    /// <inheritdoc />
    public async Task<VaultReadResult> ReadContentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await GetAsync(documentId, cancellationToken).ConfigureAwait(false);
        return document is null
            ? VaultReadResult.Missing
            : await _vault.ReadAsync(document.Sha256, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<VaultCopyResult> CopyContentToAsync(Guid documentId, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var document = await GetAsync(documentId, cancellationToken).ConfigureAwait(false);
        return document is null
            ? new VaultCopyResult(VaultState.Missing, 0)
            : await _vault.CopyToAsync(document.Sha256, destination, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<VaultState> VerifyAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await GetAsync(documentId, cancellationToken).ConfigureAwait(false);
        return document is null
            ? VaultState.Missing
            : await _vault.VerifyAsync(document.Sha256, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> LinkAsync(Guid documentId, DocumentLinkTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!await _db.Documents.AnyAsync(d => d.Id == documentId, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var added = await AddLinkAsync(documentId, target, cancellationToken).ConfigureAwait(false);
        if (added)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return added;
    }

    /// <inheritdoc />
    public async Task<bool> UnlinkAsync(Guid documentId, DocumentLinkTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        var link = await _db.DocumentLinks
            .FirstOrDefaultAsync(
                l => l.DocumentId == documentId && l.EntityType == target.EntityType && l.EntityId == target.EntityId,
                cancellationToken)
            .ConfigureAwait(false);

        if (link is null)
        {
            return false;
        }

        // An official row is never physically removed (DATA-MODEL §0); soft-deleting it is what
        // makes the attachment vanish from every list and travel as a removal to the other devices.
        _db.SoftDelete(link);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Document>> ListForAsync(DocumentLinkTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        return await _db.DocumentLinks
            .AsNoTracking()
            .Where(l => l.EntityType == target.EntityType && l.EntityId == target.EntityId)
            .Join(_db.Documents.AsNoTracking(), l => l.DocumentId, d => d.Id, (_, d) => d)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentLink>> ListLinksAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        await _db.DocumentLinks
            .AsNoTracking()
            .Where(l => l.DocumentId == documentId)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<DocumentImportResult> SaveDerivedAsync(
        byte[] content,
        string fileName,
        string mediaType,
        Guid? derivedFromId = null,
        DocumentLinkTarget? link = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

        var name = Path.GetFileName(fileName.Trim());
        if (content.Length == 0)
        {
            return new DocumentImportResult(DocumentImportState.Empty, Guid.Empty, CoreAr.Documents.FileEmpty(name));
        }

        // A generated document is not held to the import limit's reason for existing (a person
        // choosing a file), but it is held to the same kinds: nothing else can be shown or printed.
        if (!DocumentMediaTypes.IsAccepted(mediaType))
        {
            return new DocumentImportResult(
                DocumentImportState.KindNotAccepted, Guid.Empty, CoreAr.Documents.FileKindNotAccepted(name));
        }

        return await StoreAsync(
            content, name, mediaType, DocumentSource.Generated, derivedFromId, link, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Document>> ListDerivedAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        await _db.Documents
            .AsNoTracking()
            .Where(d => d.DerivedFromId == documentId)
            .OrderBy(d => d.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> SetPinnedOnPhoneAsync(Guid documentId, bool pinned, CancellationToken cancellationToken = default)
    {
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken).ConfigureAwait(false);
        if (document is null || document.PinnedOnPhone == pinned)
        {
            return false;
        }

        document.PinnedOnPhone = pinned;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Document>> ListPinnedAsync(CancellationToken cancellationToken = default) =>
        await _db.Documents
            .AsNoTracking()
            .Where(d => d.PinnedOnPhone)
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task SetOcrStatusAsync(
        Guid documentId,
        OcrStatus status,
        string? ocrLang = null,
        CancellationToken cancellationToken = default)
    {
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return;
        }

        document.OcrStatus = status;
        if (ocrLang is not null)
        {
            document.OcrLang = ocrLang;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SavePagesAsync(
        Guid documentId,
        IReadOnlyList<OcrPageResult> pages,
        string ocrLang,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentException.ThrowIfNullOrWhiteSpace(ocrLang);

        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return;
        }

        // Soft-deleted pages are included deliberately: the natural key (document, page number)
        // is a unique index that counts them too, so a re-read has to revive the old row rather
        // than insert a second one beside it.
        var existing = await _db.DocumentPages
            .IgnoreQueryFilters()
            .Where(p => p.DocumentId == documentId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var page in pages)
        {
            var row = existing.Find(p => p.PageNo == page.PageNo);
            if (row is null)
            {
                row = new DocumentPage { Id = _ids.NewId(), DocumentId = documentId, PageNo = page.PageNo };
                _db.DocumentPages.Add(row);
            }

            row.Text = page.Text;
            row.Words = SerializeWords(page.Words);
            row.Confidence = page.Confidence;

            // A page that had been soft-deleted by an earlier, shorter reading of the same
            // document comes back rather than leaving a hole in the page numbers.
            row.DeletedAt = null;
        }

        // A re-read that produced fewer pages than last time — a damaged PDF repaired, say —
        // must not leave the pages nobody can reach any more in the search index.
        foreach (var row in existing.Where(p => p.DeletedAt is null && pages.All(page => page.PageNo != p.PageNo)))
        {
            _db.SoftDelete(row);
        }

        document.PageCount = pages.Count;
        document.OcrStatus = pages.Count > 0 ? OcrStatus.Done : OcrStatus.Failed;
        document.OcrLang = ocrLang;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentPage>> ListPagesAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        await _db.DocumentPages
            .AsNoTracking()
            .Where(p => p.DocumentId == documentId)
            .OrderBy(p => p.PageNo)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>The word boxes of one page as the JSON <c>document_pages.words</c> holds.</summary>
    public static string SerializeWords(IReadOnlyList<OcrWordBox> words) => JsonSerializer.Serialize(words);

    /// <summary>Reads back what <see cref="SerializeWords"/> wrote; an unreadable value is no words.</summary>
    public static IReadOnlyList<OcrWordBox> DeserializeWords(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<OcrWordBox>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// The media type a file really is, or <c>null</c> when the name and the bytes do not agree
    /// on one الوكيل accepts.
    /// </summary>
    private static string? Classify(string fileName, ReadOnlySpan<byte> content)
    {
        var byName = DocumentMediaTypes.FromFileName(fileName);
        if (byName is null)
        {
            return null;
        }

        var byBytes = DocumentMediaTypes.Sniff(content[..Math.Min(8, content.Length)]);
        return string.Equals(byName, byBytes, StringComparison.OrdinalIgnoreCase) ? byName : null;
    }

    private async Task<DocumentImportResult> StoreAsync(
        byte[] content,
        string name,
        string mediaType,
        DocumentSource source,
        Guid? derivedFromId,
        DocumentLinkTarget? link,
        CancellationToken cancellationToken)
    {
        VaultWriteResult write;
        try
        {
            write = await _vault.WriteAsync(content, cancellationToken).ConfigureAwait(false);
        }
        catch (VaultUnavailableException)
        {
            // Nothing was written and nothing is recorded: a record whose bytes are not there
            // would be a document the office can see and never open.
            return new DocumentImportResult(
                DocumentImportState.VaultUnavailable, Guid.Empty, CoreAr.Documents.VaultUnavailableMessage);
        }

        // A derived copy is a document in its own right even when its bytes happen to match an
        // existing one; everything else is looked up by hash so the same file is one document.
        var existing = derivedFromId is null
            ? await _db.Documents.FirstOrDefaultAsync(d => d.Sha256 == write.Sha256Hex, cancellationToken).ConfigureAwait(false)
            : null;

        if (existing is not null)
        {
            if (link is not null && await AddLinkAsync(existing.Id, link, cancellationToken).ConfigureAwait(false))
            {
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            return new DocumentImportResult(
                DocumentImportState.AlreadyStored,
                existing.Id,
                CoreAr.Documents.FileAlreadyStored(name),
                write.Sha256Hex);
        }

        var document = new Document
        {
            Id = _ids.NewId(),
            Sha256 = write.Sha256Hex,
            Size = write.Size,
            Mime = mediaType,
            OriginalName = name,
            Source = source,
            PageCount = 0,
            OcrStatus = DocumentMediaTypes.IsReadable(mediaType) ? OcrStatus.Pending : OcrStatus.Unsupported,
            OcrLang = null,
            PinnedOnPhone = false,
            DerivedFromId = derivedFromId,
        };

        _db.Documents.Add(document);

        // The document is saved before its link, and not with it: document_links has a foreign key
        // to documents, and nothing in the model tells EF that the two new rows depend on each
        // other, so a single save is free to write them in the order that fails. Two saves are
        // also the safe order to fail in — a stored document with no attachment is a document in
        // the list, while an attachment pointing at nothing is a row nobody can open.
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (link is not null && await AddLinkAsync(document.Id, link, cancellationToken).ConfigureAwait(false))
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        // The audit line names the file and its size and nothing else: never a path, never a hash,
        // never a byte of the document itself (ARCHITECTURE §12).
        await _audit.LogAsync(
            actor: await ActorAsync(cancellationToken).ConfigureAwait(false),
            action: "document.added",
            summaryAr: CoreAr.Documents.FileAdded(name),
            entityType: "documents",
            entityId: document.Id,
            details: new { size = write.Size, source = source.ToString() },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return new DocumentImportResult(
            DocumentImportState.Added, document.Id, CoreAr.Documents.FileAdded(name), write.Sha256Hex);
    }

    /// <summary>
    /// The one operating account of this installation (AGREEMENT item 7: no permission system),
    /// read once per service instance — a scope is one user session, so the name cannot change
    /// under it. The audit log has to name the person who attached the file, not the program.
    /// </summary>
    private async Task<string> ActorAsync(CancellationToken cancellationToken)
    {
        _actor ??= await _db.Installation.AsNoTracking().Select(i => i.EmployeeName)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
        return _actor;
    }

    /// <summary>
    /// Adds a link unless it is already there, without saving — so an import writes the document
    /// and its first link in one save and cannot leave half of itself behind.
    /// </summary>
    private async Task<bool> AddLinkAsync(Guid documentId, DocumentLinkTarget target, CancellationToken cancellationToken)
    {
        if (!DocumentLinkTypes.IsKnown(target.EntityType))
        {
            throw new ArgumentException("A document is linked to one of the known entity kinds.", nameof(target));
        }

        var existing = await _db.DocumentLinks
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                l => l.DocumentId == documentId && l.EntityType == target.EntityType && l.EntityId == target.EntityId,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            if (existing.DeletedAt is null)
            {
                return false;
            }

            // The unique index counts soft-deleted rows too, so a re-attached document brings its
            // old row back instead of colliding with it.
            existing.DeletedAt = null;
            return true;
        }

        _db.DocumentLinks.Add(new DocumentLink
        {
            Id = _ids.NewId(),
            DocumentId = documentId,
            EntityType = target.EntityType,
            EntityId = target.EntityId,
        });

        return true;
    }
}
