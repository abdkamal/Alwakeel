using System.IO;
using DocumentFormat.OpenXml.Packaging;
using Wakeel.Core.Services.Correspondence;

namespace Wakeel.Reports.Letters;

/// <inheritdoc cref="ILetterTemplateInspector"/>
/// <remarks>
/// <para>
/// The check runs when a template is uploaded (AGREEMENT item 57). It reports the nine marks it
/// knows and, just as importantly, anything else beginning with <c>@</c>: a mark spelt
/// «@اسم الموضع» is invisible until the day a letter comes out of the printer with the word still
/// in it, so it is shown as it stands, beside the known ones, for a person to judge.
/// </para>
/// <para>
/// Text boxes hold the whole of this template's letterhead, so the walk covers every paragraph of
/// every part rather than the body's own paragraphs. A text box written both as a drawing and as
/// a VML fallback is therefore counted twice; the count is what a template author uses to see
/// that a mark is where they put it, not a number anyone does arithmetic on.
/// </para>
/// </remarks>
public sealed class LetterTemplateInspector : ILetterTemplateInspector
{
    /// <summary>The most words a mark may have; longer runs of words are not read as one mark.</summary>
    private const int MaxMarkWords = 3;

    /// <inheritdoc />
    public LetterTemplateCheck Check(ReadOnlySpan<byte> templateDocx)
    {
        var paragraphs = TryReadParagraphs(templateDocx);
        if (paragraphs is null)
        {
            return LetterTemplateCheck.Unreadable;
        }

        var order = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var known = new HashSet<string>(LetterMarks.All, StringComparer.Ordinal);

        foreach (var line in paragraphs)
        {
            foreach (var mark in MarksIn(line))
            {
                if (!counts.TryGetValue(mark, out var count))
                {
                    order.Add(mark);
                    count = 0;
                }

                counts[mark] = count + 1;
            }
        }

        var marks = order
            .Select(name => new LetterTemplateMark(name, known.Contains(name), counts[name]))
            .ToList();

        return new LetterTemplateCheck(true, marks);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ReadParagraphs(ReadOnlySpan<byte> templateDocx) =>
        TryReadParagraphs(templateDocx) ?? [];

    /// <inheritdoc />
    public IReadOnlyList<LetterTemplateLine> ReadLayout(ReadOnlySpan<byte> templateDocx) =>
        LetterLayoutReader.TryRead(templateDocx) ?? [];

    /// <summary>
    /// Every <c>@</c> group in one line: a known mark whole, or — when nothing known matches — the
    /// unknown mark as far as a reader would say it runs, at most three words.
    /// </summary>
    /// <param name="line">One paragraph's joined text.</param>
    internal static IEnumerable<string> MarksIn(string line)
    {
        var index = 0;
        while (index < line.Length)
        {
            var at = line.IndexOf('@', index);
            if (at < 0)
            {
                yield break;
            }

            var rest = line[at..];
            var match = LetterMarks.ByLength.FirstOrDefault(m => rest.StartsWith(m, StringComparison.Ordinal));
            if (match is not null)
            {
                yield return match;
                index = at + match.Length;
                continue;
            }

            var unknown = ReadUnknown(rest);
            if (unknown.Length > 1)
            {
                yield return unknown;
                index = at + unknown.Length;
            }
            else
            {
                index = at + 1;
            }
        }
    }

    /// <summary>
    /// Reads an unrecognised mark: the <c>@</c> and up to three words after it, stopping at
    /// punctuation or at a second <c>@</c>. Three is what the nine known marks need, and reading
    /// further would swallow the sentence the mark sits in.
    /// </summary>
    private static string ReadUnknown(string rest)
    {
        var words = 0;
        var index = 1;
        var end = 1;

        while (index < rest.Length)
        {
            while (index < rest.Length && rest[index] == ' ')
            {
                index++;
            }

            var wordStart = index;
            while (index < rest.Length && !char.IsWhiteSpace(rest[index]) && !IsStop(rest[index]))
            {
                index++;
            }

            if (index == wordStart)
            {
                break;
            }

            words++;
            end = index;
            if (words >= MaxMarkWords || index >= rest.Length || IsStop(rest[index]))
            {
                break;
            }
        }

        return rest[..end];
    }

    private static bool IsStop(char c) =>
        c is '@' or '.' or ',' or '،' or ':' or '؛' or ';' or '/' or '\\'
            or '(' or ')' or '[' or ']' or '{' or '}' or '"' or '«' or '»';

    private static List<string>? TryReadParagraphs(ReadOnlySpan<byte> templateDocx)
    {
        if (templateDocx.IsEmpty)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(templateDocx.ToArray(), writable: false);
            using var document = WordprocessingDocument.Open(stream, isEditable: false);
            if (document.MainDocumentPart is null)
            {
                return null;
            }

            var lines = new List<string>();
            foreach (var part in LetterOpenXml.TextParts(document))
            {
                foreach (var paragraph in LetterOpenXml.Paragraphs(part))
                {
                    lines.Add(LetterOpenXml.TextOf(paragraph));
                }
            }

            return lines;
        }
        catch (Exception exception) when (exception is InvalidDataException
                                              or IOException
                                              or NotSupportedException
                                              or OpenXmlPackageException
                                              or ArgumentException
                                              or FileFormatException)
        {
            return null;
        }
    }
}
