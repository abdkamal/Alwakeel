using System.Text;

namespace Wakeel.Core.Services.Correspondence;

/// <summary>
/// The page a letter is printed on (AGREEMENT item 57). A4 is the default; A5 is chosen per
/// letter in the outgoing wizard, and the composer writes the choice into the generated
/// document's <c>sectPr/pgSz</c> and margins rather than expecting the template to carry it.
/// </summary>
public enum LetterPageSize
{
    /// <summary>210 × 297 mm — what the reference template is saved at.</summary>
    A4 = 0,

    /// <summary>148 × 210 mm — the half sheet used for short letters.</summary>
    A5 = 1,
}

/// <summary>Where the template that produced a letter came from.</summary>
public enum LetterTemplateOrigin
{
    /// <summary>The copy this office edited locally, which wins over everything else.</summary>
    LocalCopy = 0,

    /// <summary>The organisation's template as it arrived in the setup file.</summary>
    Setup = 1,

    /// <summary>The template built into الوكيل, used when the setup file carried none.</summary>
    BuiltIn = 2,
}

/// <summary>How one block of the letter body is laid out.</summary>
public enum LetterBlockKind
{
    /// <summary>An ordinary paragraph.</summary>
    Paragraph = 0,

    /// <summary>One item of a numbered list.</summary>
    Numbered = 1,

    /// <summary>One item of a bulleted list.</summary>
    Bulleted = 2,
}

/// <summary>One stretch of body text that is either ordinary or heavy.</summary>
/// <param name="Text">The words themselves.</param>
/// <param name="Bold">Whether they are written heavy.</param>
public sealed record LetterSpan(string Text, bool Bold = false);

/// <summary>One block of the letter body: a paragraph or one item of a list.</summary>
/// <param name="Kind">Paragraph, numbered item or bulleted item.</param>
/// <param name="Spans">Its text, cut into ordinary and heavy stretches.</param>
public sealed record LetterBlock(LetterBlockKind Kind, IReadOnlyList<LetterSpan> Spans)
{
    /// <summary>The block's plain text, with every stretch joined back together.</summary>
    public string Text => string.Concat(Spans.Select(s => s.Text));

    /// <summary>A plain paragraph of one stretch.</summary>
    public static LetterBlock Plain(string text) =>
        new(LetterBlockKind.Paragraph, [new LetterSpan(text)]);
}

/// <summary>
/// The body of a letter as the internal editor produces it: paragraphs, numbered and bulleted
/// lists, and bold — the "small rich-text model" of AGREEMENT item 57. Nothing here knows about
/// Word: the composer turns it into runs, and the HTML renderer into elements.
/// </summary>
/// <param name="Blocks">The blocks in reading order.</param>
public sealed record LetterBody(IReadOnlyList<LetterBlock> Blocks)
{
    /// <summary>A body with nothing in it.</summary>
    public static LetterBody Empty { get; } = new([]);

    /// <summary>Whether there is any text at all.</summary>
    public bool IsEmpty => Blocks.Count == 0 || Blocks.All(b => string.IsNullOrWhiteSpace(b.Text));

    /// <summary>The whole body as plain lines, for search indexing and for the HTML fallback.</summary>
    public string ToPlainText()
    {
        var builder = new StringBuilder();
        var number = 0;
        foreach (var block in Blocks)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            switch (block.Kind)
            {
                case LetterBlockKind.Numbered:
                    number++;
                    builder.Append(number).Append(". ");
                    break;
                case LetterBlockKind.Bulleted:
                    builder.Append("- ");
                    break;
                default:
                    number = 0;
                    break;
            }

            builder.Append(block.Text);
        }

        return builder.ToString();
    }

    /// <summary>A body of one paragraph per non-empty line, with no lists and no bold.</summary>
    public static LetterBody FromPlainText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Empty;
        }

        var blocks = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(LetterBlock.Plain)
            .ToList();

        return blocks.Count == 0 ? Empty : new LetterBody(blocks);
    }

    /// <summary>
    /// Reads the internal editor's plain form: a line beginning with <c>-</c> or <c>•</c> is a
    /// bulleted item, a line beginning with a number and a dot is a numbered item, and text
    /// between pairs of <c>**</c> is heavy. Deliberately small — the editor offers exactly these
    /// three things (AGREEMENT item 57), and anything else stays literal text.
    /// </summary>
    /// <param name="text">What the editor holds.</param>
    public static LetterBody Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Empty;
        }

        var blocks = new List<LetterBlock>();
        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var kind = LetterBlockKind.Paragraph;
            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("• ", StringComparison.Ordinal))
            {
                kind = LetterBlockKind.Bulleted;
                line = line[2..].Trim();
            }
            else if (TryStripNumber(line, out var rest))
            {
                kind = LetterBlockKind.Numbered;
                line = rest;
            }

            blocks.Add(new LetterBlock(kind, ParseSpans(line)));
        }

        return blocks.Count == 0 ? Empty : new LetterBody(blocks);
    }

    private static bool TryStripNumber(string line, out string rest)
    {
        rest = line;
        var digits = 0;
        while (digits < line.Length && char.IsAsciiDigit(line[digits]))
        {
            digits++;
        }

        if (digits == 0 || digits >= line.Length)
        {
            return false;
        }

        var separator = line[digits];
        if (separator is not ('.' or '-' or ')' or '،'))
        {
            return false;
        }

        rest = line[(digits + 1)..].Trim();
        return rest.Length > 0;
    }

    private static List<LetterSpan> ParseSpans(string line)
    {
        var spans = new List<LetterSpan>();
        var index = 0;
        var bold = false;
        while (index < line.Length)
        {
            var marker = line.IndexOf("**", index, StringComparison.Ordinal);
            if (marker < 0)
            {
                Add(line[index..], bold);
                break;
            }

            Add(line[index..marker], bold);
            bold = !bold;
            index = marker + 2;
        }

        if (spans.Count == 0)
        {
            spans.Add(new LetterSpan(line));
        }

        return spans;

        void Add(string text, bool heavy)
        {
            if (text.Length > 0)
            {
                spans.Add(new LetterSpan(text, heavy));
            }
        }
    }
}

/// <summary>
/// Everything the composer needs in order to fill the nine marks of AGREEMENT item 57. Every
/// field is a finished Arabic value: the composer formats nothing but the two dates and the
/// «مسودة» stand-in for a number that has not been issued yet.
/// </summary>
public sealed record LetterData
{
    /// <summary>The letter's date; both the Hijri and the Gregorian mark are read from it.</summary>
    public required DateTime Date { get; init; }

    /// <summary>
    /// The official number in the form of AGREEMENT item 5, or <c>null</c> before approval — in
    /// which case «مسودة» is written in its place and no number is ever invented.
    /// </summary>
    public string? NumberAr { get; init; }

    /// <summary>The head of the receiving office — <c>@اسم رئيس المكتب</c>.</summary>
    public string RecipientHeadNameAr { get; init; } = string.Empty;

    /// <summary>The receiving office — <c>@اسم مكتب المستقبل</c>.</summary>
    public string RecipientOfficeNameAr { get; init; } = string.Empty;

    /// <summary>The subject line — <c>@اسم الموضوع</c>.</summary>
    public string SubjectAr { get; init; } = string.Empty;

    /// <summary>The letter's own text — <c>@نص المراسلة</c>.</summary>
    public LetterBody Body { get; init; } = LetterBody.Empty;

    /// <summary>The approving employee — <c>@اسم المرسل</c>.</summary>
    public string SenderNameAr { get; init; } = string.Empty;

    /// <summary>The approving employee's office — <c>@اسم مكتب المرسل</c>.</summary>
    public string SenderOfficeNameAr { get; init; } = string.Empty;

    /// <summary>A4 unless the wizard asked for A5.</summary>
    public LetterPageSize PageSize { get; init; } = LetterPageSize.A4;
}

/// <summary>One <c>@</c> mark found in a template.</summary>
/// <param name="Name">The mark exactly as the file writes it, <c>@</c> and all.</param>
/// <param name="IsKnown">Whether it is one of the nine الوكيل fills in.</param>
/// <param name="Count">How many times it appears, across every part of the file.</param>
public sealed record LetterTemplateMark(string Name, bool IsKnown, int Count);

/// <summary>
/// What the check on upload found (AGREEMENT item 57): the known marks, the ones nobody
/// recognises — a misspelt mark would otherwise reach the printer still spelled out — and the
/// known ones this template leaves out.
/// </summary>
/// <param name="IsReadable">Whether the file could be opened as a Word document at all.</param>
/// <param name="Marks">Every mark found, in the order they first appear.</param>
public sealed record LetterTemplateCheck(bool IsReadable, IReadOnlyList<LetterTemplateMark> Marks)
{
    /// <summary>The marks الوكيل knows how to fill.</summary>
    public IReadOnlyList<LetterTemplateMark> Known => [.. Marks.Where(m => m.IsKnown)];

    /// <summary>The marks nobody recognises — the ones a person must look at.</summary>
    public IReadOnlyList<LetterTemplateMark> Unknown => [.. Marks.Where(m => !m.IsKnown)];

    /// <summary>The known marks this template does not use.</summary>
    public IReadOnlyList<string> Missing =>
        [.. LetterMarks.All.Where(known => !Marks.Any(m => m.IsKnown && m.Name == known))];

    /// <summary>
    /// Whether a letter can be produced from it at all. A template with an unknown mark still
    /// works — the mark simply stays as written, which is exactly what the warning is for — but
    /// one that carries no body mark has nowhere to put the letter's text.
    /// </summary>
    public bool CanCompose => IsReadable && Marks.Any(m => m.IsKnown && m.Name == LetterMarks.Body);

    /// <summary>A file that could not be read.</summary>
    public static LetterTemplateCheck Unreadable { get; } = new(false, []);
}

/// <summary>The nine marks of AGREEMENT item 57, and nothing else.</summary>
public static class LetterMarks
{
    /// <summary>The Hijri date, in Arabic month names.</summary>
    public const string HijriDate = "@التاريخ الهجري";

    /// <summary>The Gregorian date, in Arabic month names.</summary>
    public const string GregorianDate = "@التاريخ الميلادي";

    /// <summary>The official outgoing number, or «مسودة» before approval.</summary>
    public const string Number = "@رقم الصادر";

    /// <summary>The head of the receiving office.</summary>
    public const string RecipientHead = "@اسم رئيس المكتب";

    /// <summary>The receiving office.</summary>
    public const string RecipientOffice = "@اسم مكتب المستقبل";

    /// <summary>The subject.</summary>
    public const string Subject = "@اسم الموضوع";

    /// <summary>The letter's text; the only mark that can bring paragraphs and lists with it.</summary>
    public const string Body = "@نص المراسلة";

    /// <summary>The approving employee.</summary>
    public const string Sender = "@اسم المرسل";

    /// <summary>The approving employee's office.</summary>
    public const string SenderOffice = "@اسم مكتب المرسل";

    /// <summary>
    /// All nine, ordered longest first so that a mark which begins with another one — none do
    /// today, but a template is free to add spacing — is still matched whole.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        RecipientOffice,
        RecipientHead,
        GregorianDate,
        SenderOffice,
        HijriDate,
        Body,
        Subject,
        Number,
        Sender,
    ];

    /// <summary>The same nine, longest first, for left-to-right scanning of a joined line.</summary>
    public static IReadOnlyList<string> ByLength { get; } =
        [.. All.OrderByDescending(m => m.Length).ThenBy(m => m, StringComparer.Ordinal)];
}

/// <summary>Where one of a template's lines stands on the sheet.</summary>
/// <remarks>
/// A letter is not a stack of lines. The letterhead is printed above everything, the date and the
/// number sit in a box against one edge, the addressee against the other, the subject across a
/// band of its own, and the signature stands apart at the foot. A preview or a PDF made without
/// Word has to put them where the template puts them, so each line is read together with the place
/// it occupies.
/// </remarks>
public enum LetterTemplatePlace
{
    /// <summary>The sheet's own flow of paragraphs, from the first line to the last.</summary>
    Flow = 0,

    /// <summary>The letterhead, which Word keeps in the page header.</summary>
    Letterhead = 1,

    /// <summary>A box floating against the right edge of the writing area.</summary>
    RightBlock = 2,

    /// <summary>A box floating against the left edge of the writing area.</summary>
    LeftBlock = 3,

    /// <summary>A box spanning the writing area, such as the subject band.</summary>
    Band = 4,

    /// <summary>The page footer, printed under everything.</summary>
    Foot = 5,
}

/// <summary>How one of a template's lines is set across the width it is given.</summary>
/// <remarks>
/// Only the three a letter uses. A template's «left» or «right» is not carried over: on a
/// right-to-left sheet those two words mean the opposite of what they say half the time, and a
/// line set to the side it starts from is what every line of an Arabic letter wants.
/// </remarks>
public enum LetterTemplateAlign
{
    /// <summary>From the side the writing starts on.</summary>
    Start = 0,

    /// <summary>Centred, as the subject band and the signature are.</summary>
    Center = 1,

    /// <summary>Stretched to both edges.</summary>
    Justify = 2,
}

/// <summary>One line of a template, with the place it occupies and the box it belongs to.</summary>
/// <param name="Text">The line's text, marks still in it; a line break inside it is a newline.</param>
/// <param name="Place">Where the line stands on the sheet.</param>
/// <param name="Block">
/// Which floating box the line belongs to — consecutive lines sharing a number above zero are the
/// lines of one box, and zero means the line is not in a box at all.
/// </param>
/// <param name="Align">How the line is set across the width it is given.</param>
public sealed record LetterTemplateLine(
    string Text,
    LetterTemplatePlace Place,
    int Block,
    LetterTemplateAlign Align = LetterTemplateAlign.Start);

/// <summary>
/// Reads a letter template and says which marks are in it. Implemented in the letter package
/// (OpenXML), so Core can offer the check on upload without depending on a document library.
/// </summary>
public interface ILetterTemplateInspector
{
    /// <summary>Lists the marks in <paramref name="templateDocx"/>.</summary>
    LetterTemplateCheck Check(ReadOnlySpan<byte> templateDocx);

    /// <summary>
    /// The template's paragraphs with the marks still in them, for a preview that shows the
    /// fixed phrases as the template writes them.
    /// </summary>
    IReadOnlyList<string> ReadParagraphs(ReadOnlySpan<byte> templateDocx);

    /// <summary>
    /// The template's lines as they are laid out on the sheet: the letterhead first, then the
    /// sheet's flow with its floating boxes, each text box read once. This is what the preview and
    /// the PDF made without Word are drawn from; <see cref="ReadParagraphs"/> is the flat walk the
    /// check uses, where a box written twice is deliberately counted twice.
    /// </summary>
    /// <param name="templateDocx">The template.</param>
    IReadOnlyList<LetterTemplateLine> ReadLayout(ReadOnlySpan<byte> templateDocx);
}

/// <summary>
/// Fills a template with one letter's data. Implemented in the letter package; Core holds only
/// the contract so that the correspondence services can ask for a document without knowing how
/// one is made.
/// </summary>
public interface ILetterComposer
{
    /// <summary>
    /// Produces the letter as a Word document: marks replaced in every part, the body inserted
    /// where <c>@نص المراسلة</c> stood, and the page set to <see cref="LetterData.PageSize"/>.
    /// </summary>
    /// <param name="templateDocx">The office's template.</param>
    /// <param name="letter">The letter's data.</param>
    byte[] Compose(ReadOnlySpan<byte> templateDocx, LetterData letter);

    /// <summary>
    /// The same letter as HTML laid out like the template, for the preview and for
    /// <c>WebView2.PrintToPdf</c> when Word is not available (ARCHITECTURE §9). The result never
    /// contains a mark.
    /// </summary>
    /// <param name="templateDocx">The office's template, read for its fixed phrases.</param>
    /// <param name="letter">The letter's data.</param>
    string RenderHtml(ReadOnlySpan<byte> templateDocx, LetterData letter);
}

/// <summary>The template built into الوكيل, used when the setup file carried none.</summary>
public interface IBuiltInLetterTemplate
{
    /// <summary>A fresh copy of the built-in template's bytes.</summary>
    byte[] Read();
}

/// <summary>
/// The narrow slice of the document vault the letter package needs: read the original bytes of a
/// stored document, and store a generated one. B3-2 owns the vault itself; this contract exists
/// so the letter package can be finished and tested without it, and so an installation whose
/// vault is unavailable still records referrals (the builder is simply not registered).
/// </summary>
public interface ILetterDocumentStore
{
    /// <summary>The stored bytes, or <c>null</c> when the document is not there.</summary>
    Task<byte[]?> ReadAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>Stores generated bytes and returns the new document's id.</summary>
    /// <param name="content">The file.</param>
    /// <param name="fileName">What to call it.</param>
    /// <param name="mediaType">Its type, e.g. the Word document type.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<Guid> SaveAsync(
        byte[] content,
        string fileName,
        string mediaType,
        CancellationToken cancellationToken = default);
}

/// <summary>How a letter left Word, or why it did not.</summary>
public enum WordEditState
{
    /// <summary>The user edited it in Word and closed the window; the changes are here.</summary>
    Imported = 0,

    /// <summary>Word is not installed; the internal editor is the way in (AGREEMENT item 10).</summary>
    WordUnavailable = 1,

    /// <summary>Word is installed but the attempt did not finish; nothing was changed.</summary>
    Failed = 2,
}

/// <summary>What came back from Word.</summary>
/// <param name="State">How it went.</param>
/// <param name="Content">The edited document when it came back, otherwise <c>null</c>.</param>
public sealed record WordEditResult(WordEditState State, byte[]? Content)
{
    /// <summary>Word is not on this machine.</summary>
    public static WordEditResult Unavailable { get; } = new(WordEditState.WordUnavailable, null);

    /// <summary>Word was there but the attempt did not finish.</summary>
    public static WordEditResult Failed { get; } = new(WordEditState.Failed, null);
}

/// <summary>
/// Full editing of a generated letter in Word (AGREEMENT item 10, ARCHITECTURE §9). Implemented
/// in the desktop shell, where COM lives; Core holds only the contract, so that every screen can
/// offer the button and show «Word غير متوفر» without any layer below depending on Word.
/// </summary>
public interface IWordAutomation
{
    /// <summary>Whether a copy of Word is installed on this machine.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Writes <paramref name="docx"/> to a file of its own, opens it in Word, waits for the user
    /// to close the window, and brings back what they wrote. The document الوكيل holds is
    /// replaced only when the state says the changes came back.
    /// </summary>
    /// <param name="docx">The generated letter.</param>
    /// <param name="fileNameHint">What to call the file the user sees in Word's title bar.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    Task<WordEditResult> EditAsync(
        byte[] docx,
        string fileNameHint,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a file that is already on disk in Word and waits for the window to close, leaving
    /// whatever the user saved in place. This is what «فتح قالب المراسلة في Word» does with the
    /// path from <see cref="ITemplateService.PrepareLocalCopyAsync"/>: the office's own copy is
    /// edited where it lies, so the letterhead and the fixed phrases are changed in Word, which
    /// is what the owner asked for (AGREEMENT item 57). Show
    /// <see cref="CoreAr.Letter.LocalCopyWarning"/> beside the button.
    /// </summary>
    /// <param name="path">The file to open.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>
    /// <see cref="WordEditState.Imported"/> when the window opened and closed,
    /// <see cref="WordEditState.WordUnavailable"/> when Word is not installed.
    /// </returns>
    Task<WordEditState> EditFileAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>
/// Turns a letter into a PDF: through Word when it is installed, and otherwise by printing the
/// letter's HTML rendering through the window's own browser engine (ARCHITECTURE §9). Implemented
/// in the desktop shell.
/// </summary>
public interface ILetterPdfWriter
{
    /// <summary>Whether a PDF can be produced at all right now.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// The letter as a PDF, or <c>null</c> when neither Word nor the browser engine could produce
    /// one. <paramref name="html"/> is the fallback rendering and is used only when Word is not
    /// available.
    /// </summary>
    /// <param name="docx">The generated letter.</param>
    /// <param name="html">The same letter rendered as HTML.</param>
    /// <param name="pageSize">
    /// The paper the letter was written for. The fallback has to be told: a browser prints onto
    /// whatever the print job names and ignores the size the rendering's own style sheet declares,
    /// so without this an A5 letter comes out on a foreign page.
    /// </param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<byte[]?> ToPdfAsync(
        byte[] docx,
        string html,
        LetterPageSize pageSize = LetterPageSize.A4,
        CancellationToken cancellationToken = default);
}

/// <summary>What الوكيل will use to write the next letter, and where it came from.</summary>
/// <param name="Content">The template file.</param>
/// <param name="Origin">Which of the three copies won.</param>
/// <param name="FileName">The name to show, always <c>letter-template.docx</c> in practice.</param>
public sealed record LetterTemplateSource(byte[] Content, LetterTemplateOrigin Origin, string FileName);

/// <summary>
/// The office's letter template (AGREEMENT item 57): which copy is in force, the check run on a
/// template before it is accepted, and the local copy that «فتح قالب المراسلة في Word» edits.
/// </summary>
public interface ITemplateService
{
    /// <summary>
    /// The template in force: the office's locally edited copy if there is one, else the
    /// organisation's template from the setup file, else the one built into الوكيل.
    /// </summary>
    Task<LetterTemplateSource> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the known and unknown marks of a template that is about to be accepted, and refuses
    /// outright a file that is not a plain Word letter: one carrying stored commands, or one that
    /// asks Word to fetch something from another place the moment it opens. Every letter written
    /// on such a template would carry the same thing to every office that receives it.
    /// </summary>
    Task<LetterTemplateCheck> CheckAsync(byte[] templateDocx, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the template in force to the office's own file and returns its path, so W88's
    /// «فتح قالب المراسلة في Word» can open it. Show <see cref="CoreAr.Letter.LocalCopyWarning"/>
    /// beside the button: a new setup file replaces this copy.
    /// </summary>
    Task<string> PrepareLocalCopyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the office's local copy with <paramref name="templateDocx"/>, unless the same
    /// check <see cref="CheckAsync"/> runs refuses it — nothing is written then, and the returned
    /// check says so, so the screen can tell the user why the old template is still in force.
    /// </summary>
    Task<LetterTemplateCheck> SaveLocalCopyAsync(byte[] templateDocx, CancellationToken cancellationToken = default);

    /// <summary>Drops the office's local copy, so the setup file's template is in force again.</summary>
    Task RemoveLocalCopyAsync(CancellationToken cancellationToken = default);

    /// <summary>Where the office's local copy lives, whether or not it exists yet.</summary>
    string LocalCopyPath { get; }
}
