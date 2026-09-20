namespace Wakeel.Core.Services.Documents;

/// <summary>
/// The kinds of file الوكيل stores and what each is called on the wire (B3-2: PDF, JPG, PNG,
/// DOCX). Anything else is refused at the door rather than stored and puzzled over later.
/// </summary>
public static class DocumentMediaTypes
{
    /// <summary>A PDF.</summary>
    public const string Pdf = "application/pdf";

    /// <summary>A JPEG photograph or scan.</summary>
    public const string Jpeg = "image/jpeg";

    /// <summary>A PNG image, which is what the scanner produces.</summary>
    public const string Png = "image/png";

    /// <summary>A Word document.</summary>
    public const string Word = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <summary>The largest file that may be stored, in bytes (AGREEMENT §6, B3-2: 20 MB).</summary>
    public const long MaxSizeBytes = CoreAr.Documents.MaxFileSizeMegabytes * 1024L * 1024L;

    /// <summary>Everything that may be imported.</summary>
    public static IReadOnlyList<string> Accepted { get; } = [Pdf, Jpeg, Png, Word];

    /// <summary>Whether a media type is one الوكيل stores.</summary>
    public static bool IsAccepted(string? mediaType) =>
        mediaType is not null && Accepted.Contains(mediaType, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a stored file's text can be read at all (OCR works on pictures and PDFs).</summary>
    public static bool IsReadable(string? mediaType) =>
        string.Equals(mediaType, Pdf, StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, Jpeg, StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, Png, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The media type a file name implies, or <c>null</c> when the name says nothing الوكيل
    /// accepts. The name is all the desktop's file dialog gives; the bytes are checked separately
    /// by <see cref="Sniff"/>, and a file has to satisfy both.
    /// </summary>
    public static string? FromFileName(string? fileName) =>
        Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant() switch
        {
            ".pdf" => Pdf,
            ".jpg" or ".jpeg" => Jpeg,
            ".png" => Png,
            ".docx" => Word,
            _ => null,
        };

    /// <summary>The usual extension for a media type, so a stored file can be handed back with a name.</summary>
    public static string ExtensionFor(string? mediaType) => mediaType?.ToLowerInvariant() switch
    {
        Pdf => ".pdf",
        Jpeg => ".jpg",
        Png => ".png",
        Word => ".docx",
        _ => string.Empty,
    };

    /// <summary>
    /// What the first bytes of a file say it really is, or <c>null</c> when they say nothing الوكيل
    /// accepts. A renamed executable must not enter the vault under a name that makes the viewer
    /// try to open it as a document, so the import path trusts this over the file's name.
    /// </summary>
    /// <param name="head">The first bytes of the file; eight are enough for all four kinds.</param>
    public static string? Sniff(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 5 && head[0] == 0x25 && head[1] == 0x50 && head[2] == 0x44 && head[3] == 0x46 && head[4] == 0x2D)
        {
            // "%PDF-"
            return Pdf;
        }

        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
        {
            return Jpeg;
        }

        if (head.Length >= 8
            && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47
            && head[4] == 0x0D && head[5] == 0x0A && head[6] == 0x1A && head[7] == 0x0A)
        {
            return Png;
        }

        if (head.Length >= 4 && head[0] == 0x50 && head[1] == 0x4B && (head[2] == 0x03 || head[2] == 0x05 || head[2] == 0x07))
        {
            // A ZIP container. Only .docx arrives as one here; the name decides which.
            return Word;
        }

        return null;
    }
}

/// <summary>The entity kinds a document may be linked to (<c>document_links.entity_type</c>).</summary>
public static class DocumentLinkTypes
{
    /// <summary>A correspondence item.</summary>
    public const string Correspondence = "correspondence";

    /// <summary>A meeting.</summary>
    public const string Meeting = "meeting";

    /// <summary>A case.</summary>
    public const string Case = "case";

    /// <summary>An outside party.</summary>
    public const string Party = "party";

    /// <summary>A task.</summary>
    public const string Task = "task";

    /// <summary>Everything a document may be attached to.</summary>
    public static IReadOnlyList<string> All { get; } = [Correspondence, Meeting, Case, Party, Task];

    /// <summary>Whether a value is one of them.</summary>
    public static bool IsKnown(string? entityType) =>
        entityType is not null && All.Contains(entityType, StringComparer.Ordinal);
}

/// <summary>Where a document is to be attached the moment it is stored.</summary>
/// <param name="EntityType">One of <see cref="DocumentLinkTypes"/>.</param>
/// <param name="EntityId">The row it belongs to.</param>
public sealed record DocumentLinkTarget(string EntityType, Guid EntityId);

/// <summary>How an attempt to store a file ended.</summary>
public enum DocumentImportState
{
    /// <summary>The file was stored and a document record was created.</summary>
    Added,

    /// <summary>
    /// The same bytes were already in the vault; the existing document was used, and the new link
    /// (if one was asked for) points at it. Nothing was stored twice.
    /// </summary>
    AlreadyStored,

    /// <summary>The file is larger than <see cref="DocumentMediaTypes.MaxSizeBytes"/>.</summary>
    TooLarge,

    /// <summary>The file is not one of the four kinds الوكيل stores.</summary>
    KindNotAccepted,

    /// <summary>The file has no bytes in it.</summary>
    Empty,

    /// <summary>The vault could not be reached, so nothing was stored and nothing was recorded.</summary>
    VaultUnavailable,
}

/// <summary>What one import produced.</summary>
/// <param name="State">How it ended.</param>
/// <param name="DocumentId">The document, when one exists; <see cref="Guid.Empty"/> otherwise.</param>
/// <param name="MessageAr">What to show the person, always a finished Arabic sentence.</param>
/// <param name="Sha256Hex">The hash the bytes are filed under, when they were stored.</param>
public sealed record DocumentImportResult(
    DocumentImportState State,
    Guid DocumentId,
    string MessageAr,
    string? Sha256Hex = null)
{
    /// <summary>Whether a document exists at the end of it, however it got there.</summary>
    public bool IsStored => State is DocumentImportState.Added or DocumentImportState.AlreadyStored;
}

// ---------------------------------------------------------------------------------------------
// Scanning (W45). Core owns the contract; the Windows shell owns WIA.
// ---------------------------------------------------------------------------------------------

/// <summary>One scanner the machine can see.</summary>
/// <param name="Id">What the driver calls it; passed back in <see cref="ScanSettings.DeviceId"/>.</param>
/// <param name="NameAr">What to show in the device list.</param>
public sealed record ScannerDevice(string Id, string NameAr);

/// <summary>How a page is to be scanned.</summary>
public enum ScanColorMode
{
    /// <summary>Full colour — a stamp or a signature in ink.</summary>
    Color = 0,

    /// <summary>Shades of grey.</summary>
    Gray = 1,

    /// <summary>Two tones, the smallest file and the sharpest text.</summary>
    BlackAndWhite = 2,
}

/// <summary>What the scan dialog (W45) asked for.</summary>
/// <param name="DeviceId">The chosen scanner, or <c>null</c> for the first one found.</param>
/// <param name="Dpi">Resolution; 300 is what reading text wants.</param>
/// <param name="Color">Colour, grey or two tones.</param>
/// <param name="MultiPage">
/// Whether to keep scanning until the feeder is empty or the user stops. Several pages are stored
/// as one PDF, because a letter is one document however many sheets it was printed on.
/// </param>
public sealed record ScanSettings(
    string? DeviceId = null,
    int Dpi = 300,
    ScanColorMode Color = ScanColorMode.Color,
    bool MultiPage = false)
{
    /// <summary>The resolutions W45 offers.</summary>
    public static IReadOnlyList<int> Resolutions { get; } = [150, 200, 300, 400, 600];

    /// <summary>The resolution a preview is taken at — fast, and never stored.</summary>
    public const int PreviewDpi = 100;
}

/// <summary>How a scan ended.</summary>
public enum ScanState
{
    /// <summary>Pages came back.</summary>
    Ok = 0,

    /// <summary>Nothing answered — the «لا ماسح ضوئي» card, which always offers another way in.</summary>
    NoScanner = 1,

    /// <summary>The person stopped it; whatever had already been scanned is still in the result.</summary>
    Cancelled = 2,

    /// <summary>The scanner was there but the scan did not finish.</summary>
    Failed = 3,
}

/// <summary>One scanned sheet, as the image the driver produced.</summary>
/// <param name="Content">The image bytes.</param>
/// <param name="MediaType">Its type — <see cref="DocumentMediaTypes.Png"/> or JPEG.</param>
public sealed record ScannedPage(byte[] Content, string MediaType);

/// <summary>What a scan produced.</summary>
/// <param name="State">How it ended.</param>
/// <param name="Pages">The sheets, in the order they went through.</param>
/// <param name="MessageAr">What to show when the state is not <see cref="ScanState.Ok"/>.</param>
public sealed record ScanResult(ScanState State, IReadOnlyList<ScannedPage> Pages, string? MessageAr = null)
{
    /// <summary>Nothing answered.</summary>
    public static ScanResult NoScanner { get; } = new(ScanState.NoScanner, [], CoreAr.Documents.NoScannerMessage);

    /// <summary>The scanner was there but the scan did not finish.</summary>
    public static ScanResult Failed { get; } = new(ScanState.Failed, [], CoreAr.Documents.ScanFailedMessage);
}

/// <summary>
/// The machine's scanner (AGREEMENT item 11, B3-2). Implemented in the Windows shell over WIA
/// with late binding, so Core — and every test — can drive the whole import path without a
/// scanner, a COM interop assembly or a Windows SDK version to agree on.
/// </summary>
public interface IScanner
{
    /// <summary>
    /// Whether a scanner answered the last time the list was asked for. False is not an error: it
    /// is the «لا ماسح ضوئي» state, and the dialog goes on offering «من الجهاز» and «من الهاتف».
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>The scanners the machine can see, so W45 can offer a choice. Empty when there are none.</summary>
    Task<IReadOnlyList<ScannerDevice>> ListDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// One quick, low-resolution page for the preview pane. Never stored, so a person can look
    /// before committing a 600 dpi colour scan of the wrong side of the sheet.
    /// </summary>
    Task<ScanResult> PreviewAsync(ScanSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans. <paramref name="pageScanned"/> is raised with the running page count so W45's
    /// progress moves on a long feeder run.
    /// </summary>
    Task<ScanResult> ScanAsync(
        ScanSettings settings,
        IProgress<int>? pageScanned = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Binds scanned sheets into one PDF. B3-2 keeps this out of Core because a PDF writer means an
/// imaging library, and Core has none; the implementation lives beside the rasteriser in
/// Wakeel.Ocr, which already carries it. When no writer is registered, a multi-page scan is
/// stored as its separate images instead of failing — the office still gets its pages.
/// </summary>
public interface IScanPdfWriter
{
    /// <summary>One PDF, one page per sheet, in order.</summary>
    byte[] Combine(IReadOnlyList<ScannedPage> pages);
}

// ---------------------------------------------------------------------------------------------
// Reading a document's text (OCR). Core owns the contract; Wakeel.Ocr owns Tesseract.
// ---------------------------------------------------------------------------------------------

/// <summary>One word Tesseract found, with the box it sits in on the page.</summary>
/// <param name="Text">The word.</param>
/// <param name="Confidence">How sure the reader is, 0 to 1.</param>
/// <param name="X">Left edge of the box, in pixels of the page image.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Box width.</param>
/// <param name="Height">Box height.</param>
public sealed record OcrWordBox(string Text, double Confidence, int X, int Y, int Width, int Height);

/// <summary>What was read from one page.</summary>
/// <param name="PageNo">The page, counted from 1.</param>
/// <param name="Text">Its text in reading order.</param>
/// <param name="Confidence">The page's mean confidence, 0 to 1.</param>
/// <param name="Words">Every word with its box, so W44 can highlight a search hit on the page.</param>
public sealed record OcrPageResult(
    int PageNo,
    string Text,
    double Confidence,
    IReadOnlyList<OcrWordBox> Words);

/// <summary>How reading a document's text ended.</summary>
public enum OcrState
{
    /// <summary>Every page was read.</summary>
    Ok = 0,

    /// <summary>The reading files are not on this machine, so nothing was attempted.</summary>
    ModelMissing = 1,

    /// <summary>This kind of file has no text to read this way (a Word document carries its own).</summary>
    Unsupported = 2,

    /// <summary>Reading was attempted and did not finish.</summary>
    Failed = 3,

    /// <summary>The person or the shutting application stopped it; finished pages are kept.</summary>
    Cancelled = 4,
}

/// <summary>What reading one document produced.</summary>
/// <param name="State">How it ended.</param>
/// <param name="Pages">The pages that were read, in order.</param>
/// <param name="MessageAr">What to show when the state is not <see cref="OcrState.Ok"/>.</param>
public sealed record OcrDocumentResult(OcrState State, IReadOnlyList<OcrPageResult> Pages, string? MessageAr = null)
{
    /// <summary>Nothing to read this kind of file with.</summary>
    public static OcrDocumentResult ModelMissing { get; } =
        new(OcrState.ModelMissing, [], CoreAr.Documents.OcrModelMissingMessage);

    /// <summary>A kind of file whose text الوكيل does not read.</summary>
    public static OcrDocumentResult Unsupported { get; } = new(OcrState.Unsupported, [], CoreAr.Documents.OcrUnsupported);

    /// <summary>Reading did not finish.</summary>
    public static OcrDocumentResult Failed { get; } = new(OcrState.Failed, [], CoreAr.Documents.OcrFailed);
}

/// <summary>How far reading one document has got.</summary>
/// <param name="Page">The page being read, counted from 1.</param>
/// <param name="Pages">How many there are, or 0 while that is not yet known.</param>
public sealed record OcrPageProgress(int Page, int Pages)
{
    /// <summary>The sentence W45 shows under the bar.</summary>
    public string MessageAr => Pages > 0 ? CoreAr.Documents.OcrPageProgress(Page, Pages) : CoreAr.Documents.OcrRunning;
}

/// <summary>
/// Reads the text of one document (Tesseract <c>ara+eng</c>). Implemented in Wakeel.Ocr; Core
/// holds only the contract, so an installation without the reading files still stores documents
/// and simply shows «لا يُقرأ نصه» beside them.
/// </summary>
public interface IOcrEngine
{
    /// <summary>Whether the reading files were found. False is the «قراءة النصوص غير مهيّأة» card.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// A short, stable name for the reading files in force. It is stored on every page that was
    /// read, so that swapping the files (AGREEMENT item 30) can be detected and the affected
    /// documents read again rather than left with text nobody can reproduce.
    /// </summary>
    string? ModelFingerprint { get; }

    /// <summary>Reads <paramref name="content"/>, page by page.</summary>
    /// <param name="content">The stored file's bytes.</param>
    /// <param name="mediaType">What kind of file it is.</param>
    /// <param name="progress">Told after each page.</param>
    /// <param name="cancellationToken">Stops between pages; pages already read are returned.</param>
    Task<OcrDocumentResult> ReadAsync(
        byte[] content,
        string mediaType,
        IProgress<OcrPageProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Which documents the queue takes first.</summary>
public enum OcrPriority
{
    /// <summary>Everything the office is not looking at: scans imported in a batch, a backup restored.</summary>
    Background = 0,

    /// <summary>The document somebody just added and is waiting on — W45's progress bar.</summary>
    Interactive = 1,
}

/// <summary>How far the whole queue has got.</summary>
/// <param name="Done">Documents finished since the queue started running.</param>
/// <param name="Total">Documents it has been given.</param>
/// <param name="DocumentId">The one being read now, or <see cref="Guid.Empty"/> between documents.</param>
/// <param name="Page">The page being read, or 0.</param>
/// <param name="Pages">How many pages that document has, or 0.</param>
public sealed record OcrQueueProgress(int Done, int Total, Guid DocumentId, int Page, int Pages)
{
    /// <summary>The sentence W43 and W45 show.</summary>
    public string MessageAr => Total == 0
        ? CoreAr.Documents.OcrQueueEmpty
        : Done >= Total
            ? CoreAr.Documents.OcrQueueDone
            : CoreAr.Documents.OcrQueueProgress(Done, Total);
}

/// <summary>
/// The background reader (B3-2): documents wait their turn, the office's own work comes first,
/// and the queue survives a restart because what is waiting is a column in the database and not
/// a list in memory.
/// </summary>
public interface IOcrQueue
{
    /// <summary>Told after every page and after every document.</summary>
    event Action<OcrQueueProgress>? ProgressChanged;

    /// <summary>The last thing <see cref="ProgressChanged"/> said.</summary>
    OcrQueueProgress Progress { get; }

    /// <summary>Puts one document in line. A document already waiting is not queued twice.</summary>
    void Enqueue(Guid documentId, OcrPriority priority = OcrPriority.Background);

    /// <summary>
    /// Puts back everything the database still says is waiting — what a restart does, and what
    /// makes an interrupted run pick up where it stopped rather than lose its place.
    /// </summary>
    Task<int> ResumeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads everything in line and returns how many documents were finished. Runs at low
    /// priority: it yields between documents so the office's typing never waits on it.
    /// </summary>
    Task<int> DrainAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one document now, ahead of the queue — what W45 does while its progress bar is on
    /// screen.
    /// </summary>
    Task<OcrDocumentResult> ReadNowAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks for re-reading every document whose text was produced by different reading files
    /// from the ones in force (AGREEMENT item 30), and returns how many were marked.
    /// </summary>
    Task<int> ReprocessChangedModelAsync(CancellationToken cancellationToken = default);
}
