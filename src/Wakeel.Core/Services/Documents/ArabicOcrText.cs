using System.Text;

namespace Wakeel.Core.Services.Documents;

/// <summary>
/// Cleans up what a reader hands back before it is stored or indexed (B3-2). It is deliberately
/// in Core and not in the OCR package: the search indexer (B3-3) has to put a query through
/// exactly the same steps as the page text it is searching, and a query never passes through
/// Tesseract.
/// </summary>
/// <remarks>
/// <para>
/// Two stages, and callers need both for different things. <see cref="Clean"/> repairs the text
/// while keeping it readable — it is what goes into <c>document_pages.text</c>, what W44 shows
/// beside the page, and what a person copies out. <see cref="NormalizeForSearch"/> then folds it
/// the way <see cref="ArabicText.Normalize"/> folds every other searchable field, so a document's
/// text and a correspondence's subject match a query by the same rules.
/// </para>
/// <para>
/// Digits are the reason this exists at all. An Arabic page printed with Arabic-Indic numerals
/// comes back with them, and AGREEMENT item 20 says الوكيل shows western digits 0–9 — a number
/// read off a scan must be searchable and displayable as the same number the rest of the product
/// writes, so the digits are folded here, once, before anything stores them.
/// </para>
/// </remarks>
public static class ArabicOcrText
{
    /// <summary>Marks a reader emits that carry no meaning: joiners, marks, the byte-order mark.</summary>
    private static bool IsInvisible(char c) =>
        c is '​' or '‌' or '‍' or '‎' or '‏'
            or '‪' or '‫' or '‬' or '‭' or '‮'
            or '⁦' or '⁧' or '⁨' or '⁩'
            or '﻿' or '­';

    /// <summary>
    /// Turns Arabic-Indic and Persian digits into western ones (AGREEMENT item 20). Anything that
    /// is not a digit is returned unchanged.
    /// </summary>
    public static char WesternDigit(char c) => c switch
    {
        >= '٠' and <= '٩' => (char)('0' + (c - '٠')), // ٠..٩
        >= '۰' and <= '۹' => (char)('0' + (c - '۰')), // ۰..۹
        _ => c,
    };

    /// <summary>Whether a string holds any Arabic-Indic digit.</summary>
    public static bool HasArabicIndicDigits(string? text) =>
        text is not null && text.Any(c => c is (>= '٠' and <= '٩') or (>= '۰' and <= '۹'));

    /// <summary>
    /// The readable form of what a page reader produced: western digits, no invisible marks, no
    /// trailing spaces on a line, no run of more than one blank line, and no stray spaces before
    /// Arabic punctuation. Line breaks are kept — they are the page's own layout, and W44 puts
    /// the text beside the picture it came from.
    /// </summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'))
        {
            if (IsInvisible(c))
            {
                continue;
            }

            builder.Append(WesternDigit(c));
        }

        var lines = builder.ToString().Split('\n');
        var result = new StringBuilder(builder.Length);
        var blanks = 0;
        foreach (var raw in lines)
        {
            var line = TrimBeforePunctuation(raw).TrimEnd();
            if (line.Length == 0)
            {
                // One blank line separates paragraphs; a scanner's idea of a margin does not need
                // six of them.
                blanks++;
                if (blanks > 1)
                {
                    continue;
                }
            }
            else
            {
                blanks = 0;
            }

            if (result.Length > 0)
            {
                result.Append('\n');
            }

            result.Append(line);
        }

        return result.ToString().Trim('\n');
    }

    /// <summary>
    /// The searchable form: <see cref="Clean"/> then <see cref="ArabicText.Normalize"/>, so a
    /// page's text is folded exactly as every other searchable field in الوكيل is.
    /// </summary>
    /// <remarks>
    /// Index-destructive, for the reason <see cref="ArabicText.Normalize"/> gives: character
    /// positions no longer line up with the original, so a caller highlighting a hit on the page
    /// must use the word boxes stored beside the text rather than a string offset.
    /// </remarks>
    public static string NormalizeForSearch(string? text) => ArabicText.Normalize(Clean(text));

    /// <summary>
    /// The words of a page in reading order, with the empty strings a reader leaves behind
    /// dropped. Used to count what a page produced without re-splitting its text everywhere.
    /// </summary>
    public static IReadOnlyList<string> Words(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : [.. Clean(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)];

    /// <summary>Drops the space a reader often puts before a comma, a full stop or a question mark.</summary>
    private static string TrimBeforePunctuation(string line)
    {
        if (line.Length < 2)
        {
            return line;
        }

        var builder = new StringBuilder(line.Length);
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == ' ' && i + 1 < line.Length && line[i + 1] is '،' or '؛' or '؟' or '.' or ',' or ':' or '!')
            {
                continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
