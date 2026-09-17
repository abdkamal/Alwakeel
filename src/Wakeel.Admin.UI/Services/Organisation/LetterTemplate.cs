using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Wakeel.Admin.UI.Services.Organisation;

/// <summary>How one paragraph of the letter template sits on the page.</summary>
public enum LetterAlignment
{
    /// <summary>Against the start of the line, which in Arabic is the right.</summary>
    Start,

    /// <summary>In the middle.</summary>
    Centre,

    /// <summary>Against the end of the line.</summary>
    End,

    /// <summary>Stretched to both margins.</summary>
    Justify,
}

/// <summary>One paragraph as the template writes it.</summary>
/// <param name="Text">Its text, with the placeholders still in it.</param>
/// <param name="Alignment">Where it sits on the line.</param>
/// <param name="Bold">Whether it is written heavy.</param>
public sealed record LetterParagraph(string Text, LetterAlignment Alignment, bool Bold);

/// <summary>One <c>@</c> mark found in a letter template.</summary>
/// <param name="Name">The mark exactly as it is written in the file, <c>@</c> and all.</param>
/// <param name="IsKnown">Whether it is one of the nine the tool fills in.</param>
/// <param name="Count">How many times it appears.</param>
public sealed record LetterPlaceholder(string Name, bool IsKnown, int Count);

/// <summary>What the tool found when it looked inside a letter template.</summary>
/// <param name="FileName">The file's name, for the screen to show.</param>
/// <param name="Paragraphs">The letter as paragraphs, for the preview.</param>
/// <param name="Placeholders">Every mark found, in the order they first appear.</param>
/// <param name="IsReadable">Whether it could be read as a Word file at all.</param>
public sealed record LetterTemplateReport(
    string FileName,
    IReadOnlyList<LetterParagraph> Paragraphs,
    IReadOnlyList<LetterPlaceholder> Placeholders,
    bool IsReadable)
{
    /// <summary>The marks the tool knows how to fill in.</summary>
    public IReadOnlyList<LetterPlaceholder> Known =>
        Placeholders.Where(p => p.IsKnown).ToList();

    /// <summary>The marks the tool does not recognise, which are the ones a person must look at.</summary>
    public IReadOnlyList<LetterPlaceholder> Unknown =>
        Placeholders.Where(p => !p.IsKnown).ToList();

    /// <summary>The known marks that this template does not use.</summary>
    public IReadOnlyList<string> Missing =>
        LetterTemplateInspector.KnownPlaceholders
            .Where(known => !Placeholders.Any(p => p.IsKnown && p.Name == known))
            .ToList();

    /// <summary>A report for a file that could not be read.</summary>
    public static LetterTemplateReport Unreadable(string fileName) =>
        new(fileName, [], [], IsReadable: false);
}

/// <summary>
/// Reads an official letter template (AGREEMENT item 57) and says which <c>@</c> marks are in it.
/// </summary>
/// <remarks>
/// <para>
/// A Word file is a zip of XML parts. Only the text matters here, so the reader walks the paragraphs
/// of the body and of every header and footer, joins the pieces of each paragraph back together —
/// Word is free to cut a single word into a dozen runs, and a mark cut in half would otherwise go
/// unseen — and then looks for the marks in the joined line.
/// </para>
/// <para>
/// Nine marks are known. Anything else beginning with <c>@</c> is reported as it stands so the
/// person can see at a glance that a mark is misspelt, rather than finding out when the first
/// letter comes out of the printer with a word still in it.
/// </para>
/// </remarks>
public static class LetterTemplateInspector
{
    private const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>
    /// The namespace Word uses to write the same thing twice: once the way a modern Word draws it
    /// and once the way an old one would. Both copies carry the same words, so only the first is
    /// read (see <see cref="ReadParagraphs"/>).
    /// </summary>
    private const string CompatibilityNamespace = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    /// <summary>The largest template file the tool will open.</summary>
    public const int MaxBytes = 4 * 1024 * 1024;

    /// <summary>
    /// The largest a single part of the package may be once unpacked. <see cref="MaxBytes"/> limits
    /// only the packed file, and a small package can hold a part that unpacks to far more than the
    /// window has room for.
    /// </summary>
    public const int MaxPartBytes = 8 * 1024 * 1024;

    /// <summary>
    /// The nine marks the tool fills in when it writes a letter (AGREEMENT item 57), longest first
    /// so that a mark which begins with another one is matched whole.
    /// </summary>
    public static IReadOnlyList<string> KnownPlaceholders { get; } =
    [
        "@التاريخ الهجري",
        "@التاريخ الميلادي",
        "@رقم الصادر",
        "@اسم رئيس المكتب",
        "@اسم مكتب المستقبل",
        "@اسم الموضوع",
        "@نص المراسلة",
        "@اسم المرسل",
        "@اسم مكتب المرسل",
    ];

    /// <summary>The most words a mark may have, which is what an unknown mark is measured against.</summary>
    private const int MaxPlaceholderWords = 3;

    private static readonly string[] ByLength =
        KnownPlaceholders.OrderByDescending(p => p.Length).ToArray();

    /// <summary>
    /// Whether a file is a Word document at all, judged by what is inside it rather than by what it
    /// is called: a Word document is a package that carries <c>word/document.xml</c>. The monthly
    /// report template is stored and shipped without ever being read, so this is the only thing
    /// standing between a renamed program and the organisation's report template.
    /// </summary>
    /// <param name="file">The whole file.</param>
    public static bool IsWordDocument(ReadOnlySpan<byte> file)
    {
        if (file.IsEmpty || file.Length > MaxBytes)
        {
            return false;
        }

        try
        {
            using var stream = new MemoryStream(file.ToArray(), writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            return archive.GetEntry("word/document.xml") is not null;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Reads a template file and reports what is in it.</summary>
    /// <param name="fileName">The file's name, kept for the screen.</param>
    /// <param name="docx">The whole file.</param>
    public static LetterTemplateReport Inspect(string fileName, ReadOnlySpan<byte> docx)
    {
        // The limit this class names is its own: a file past it is not opened at all, whoever asked
        // and whatever limit they were keeping.
        if (docx.Length > MaxBytes)
        {
            return LetterTemplateReport.Unreadable(fileName);
        }

        var bytes = docx.ToArray();
        List<LetterParagraph> paragraphs;

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            paragraphs = ReadParagraphs(archive);
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Xml.XmlException or IOException or NotSupportedException)
        {
            return LetterTemplateReport.Unreadable(fileName);
        }

        if (paragraphs.Count == 0)
        {
            return LetterTemplateReport.Unreadable(fileName);
        }

        return new LetterTemplateReport(fileName, paragraphs, FindPlaceholders(paragraphs), IsReadable: true);
    }

    /// <summary>
    /// Finds every <c>@</c> mark in a stretch of already-joined text, in the order they first
    /// appear. Split out so it can be used on a line that did not come from a file.
    /// </summary>
    public static IReadOnlyList<LetterPlaceholder> FindPlaceholders(IEnumerable<LetterParagraph> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);

        var order = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var known = new HashSet<string>(StringComparer.Ordinal);

        foreach (var paragraph in paragraphs)
        {
            var line = Collapse(paragraph.Text);
            var at = 0;

            while (at < line.Length)
            {
                var mark = line.IndexOf('@', at);
                if (mark < 0)
                {
                    break;
                }

                var (name, isKnown, length) = ReadMark(line, mark);
                if (length == 0)
                {
                    at = mark + 1;
                    continue;
                }

                if (!counts.TryGetValue(name, out var count))
                {
                    order.Add(name);
                    count = 0;
                }

                counts[name] = count + 1;
                if (isKnown)
                {
                    known.Add(name);
                }

                at = mark + length;
            }
        }

        return order.Select(name => new LetterPlaceholder(name, known.Contains(name), counts[name])).ToList();
    }

    /// <summary>
    /// Fills a template's paragraphs in with the sample values the preview shows, so a person can
    /// see a whole letter rather than a page of marks.
    /// </summary>
    public static IReadOnlyList<LetterParagraph> FillWithSample(
        IEnumerable<LetterParagraph> paragraphs,
        IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        ArgumentNullException.ThrowIfNull(values);

        return paragraphs.Select(paragraph =>
        {
            var text = paragraph.Text;
            foreach (var known in ByLength)
            {
                if (values.TryGetValue(known, out var value))
                {
                    text = text.Replace(known, value, StringComparison.Ordinal);
                }
            }

            return paragraph with { Text = text };
        }).ToList();
    }

    /// <summary>
    /// The mark starting at <paramref name="mark"/>: a known one when the text says one, otherwise
    /// whatever word or words follow the <c>@</c>, up to the length a mark is allowed to be.
    /// </summary>
    private static (string Name, bool IsKnown, int Length) ReadMark(string line, int mark)
    {
        foreach (var known in ByLength)
        {
            if (line.AsSpan(mark).StartsWith(known, StringComparison.Ordinal))
            {
                var after = mark + known.Length;

                // «@رقم الصادرات» is not «@رقم الصادر» with something after it: a mark ends where
                // the word ends, so a letter straight after it means this is a different mark.
                if (after >= line.Length || !IsWordCharacter(line[after]))
                {
                    return (known, true, known.Length);
                }
            }
        }

        var at = mark + 1;
        var end = at;
        var words = 0;

        while (words < MaxPlaceholderWords && at < line.Length && IsWordCharacter(line[at]))
        {
            while (at < line.Length && IsWordCharacter(line[at]))
            {
                at++;
            }

            end = at;
            words++;

            if (at < line.Length && line[at] == ' ')
            {
                at++;
            }
            else
            {
                break;
            }
        }

        return words == 0
            ? (string.Empty, false, 0)
            : (line[mark..end], false, end - mark);
    }

    private static bool IsWordCharacter(char character) =>
        char.IsLetterOrDigit(character) || character == '_';

    /// <summary>
    /// Every run of white space becomes one space, so a mark that Word spaced out across a tab and
    /// three spaces still reads as the mark it is.
    /// </summary>
    private static string Collapse(string text)
    {
        var builder = new StringBuilder(text.Length);
        var lastWasSpace = false;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            builder.Append(character);
            lastWasSpace = false;
        }

        return builder.ToString().TrimEnd();
    }

    private static List<LetterParagraph> ReadParagraphs(ZipArchive archive)
    {
        var paragraphs = new List<LetterParagraph>();
        XNamespace mc = CompatibilityNamespace;

        // In the order a letter is read: what is drawn at the top of every page, then the body, then
        // what is drawn at the bottom. A letterhead usually carries the date and the outgoing number,
        // and those are marks like any other, so the preview shows them where the printer puts them
        // instead of after the signature.
        foreach (var name in PartsInOrder(archive))
        {
            var entry = archive.GetEntry(name);
            if (entry is null)
            {
                continue;
            }

            var document = LoadPart(entry);
            XNamespace w = WordNamespace;

            foreach (var paragraph in document.Descendants(w + "p"))
            {
                // Word writes a drawing twice — once for itself and once as a fallback for an older
                // Word — and both copies carry the same lines. Reading both would show every line of
                // the letterhead twice and count every mark in it twice, so the fallback is skipped.
                if (paragraph.Ancestors(mc + "Fallback").Any())
                {
                    continue;
                }

                var text = JoinText(paragraph, w);
                if (text.Length == 0)
                {
                    continue;
                }

                paragraphs.Add(new LetterParagraph(text, AlignmentOf(paragraph, w), BoldnessOf(paragraph, w)));
            }
        }

        return paragraphs;
    }

    /// <summary>
    /// Reads one part out of the package, refusing anything that unpacks past
    /// <see cref="MaxPartBytes"/>. The four-megabyte limit above is on the packed file; a part
    /// inside it can claim to unpack to hundreds of megabytes, so the size the package declares is
    /// checked first and the unpacking is stopped at the same limit for a package that understates
    /// it. Either refusal is an <see cref="InvalidDataException"/>, which the caller already turns
    /// into «تعذّرت قراءة الملف».
    /// </summary>
    private static XDocument LoadPart(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxPartBytes)
        {
            throw new InvalidDataException("Template part is larger than the tool will open.");
        }

        using var part = entry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];

        int read;
        while ((read = part.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > MaxPartBytes)
            {
                throw new InvalidDataException("Template part is larger than the tool will open.");
            }

            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;
        return XDocument.Load(buffer);
    }

    private static IEnumerable<string> PartsInOrder(ZipArchive archive)
    {
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.StartsWith("word/header", StringComparison.Ordinal))
            {
                yield return entry.FullName;
            }
        }

        yield return "word/document.xml";

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.StartsWith("word/footer", StringComparison.Ordinal))
            {
                yield return entry.FullName;
            }
        }
    }

    /// <summary>
    /// One paragraph's text, with its runs joined back together. Word cuts text wherever it likes —
    /// a spell-check mark is enough — so a mark that reads whole on the page can be four runs in the
    /// file, and only the joined line can be searched.
    /// </summary>
    /// <remarks>
    /// Text that belongs to a paragraph nested inside this one — the letterhead of this very
    /// template puts its lines in text boxes, and a text box holds paragraphs of its own — is left
    /// out here and picked up when that paragraph comes round in its own turn. Taking it in would
    /// run the end of one line straight into the start of another, and a mark sitting at the end of
    /// a line would read as an unknown mark made of two lines glued together.
    /// </remarks>
    private static string JoinText(XElement paragraph, XNamespace w)
    {
        var builder = new StringBuilder();

        foreach (var node in paragraph.Descendants())
        {
            if (!ReferenceEquals(node.Ancestors(w + "p").FirstOrDefault(), paragraph))
            {
                continue;
            }

            if (node.Name == w + "t")
            {
                builder.Append(node.Value);
            }
            else if (node.Name == w + "tab")
            {
                builder.Append(' ');
            }
            else if (node.Name == w + "br")
            {
                builder.Append(' ');
            }
        }

        return Collapse(builder.ToString());
    }

    private static LetterAlignment AlignmentOf(XElement paragraph, XNamespace w)
    {
        var value = paragraph.Element(w + "pPr")?.Element(w + "jc")?.Attribute(w + "val")?.Value;
        return value switch
        {
            "center" => LetterAlignment.Centre,
            "both" or "distribute" => LetterAlignment.Justify,
            "left" => LetterAlignment.End,
            "end" => LetterAlignment.End,
            _ => LetterAlignment.Start,
        };
    }

    private static bool BoldnessOf(XElement paragraph, XNamespace w)
    {
        var bold = paragraph.Descendants(w + "b").FirstOrDefault();
        if (bold is null)
        {
            return false;
        }

        var value = bold.Attribute(w + "val")?.Value;
        return value is null or "1" or "true" or "on";
    }
}
