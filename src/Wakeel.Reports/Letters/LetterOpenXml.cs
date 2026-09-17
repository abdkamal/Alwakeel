using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Wakeel.Reports.Letters;

/// <summary>
/// The small amount of OpenXML knowledge the letter package needs: where a Word file keeps text,
/// how to see a mark that Word has cut into pieces, and how to put something else in its place.
/// </summary>
/// <remarks>
/// <para>
/// Word is free to split a single word across any number of runs — a spell-check pass, a stray
/// keystroke, a language tag — so <c>@اسم الموضوع</c> may well be stored as <c>@اسم ال</c>,
/// <c>مو</c>, <c>ضوع</c>. Nothing here looks at one run at a time. Instead each paragraph's text
/// nodes are read as one string with an index back to the node that holds each character, the
/// mark is found in that string, and the replacement is spliced across however many nodes the
/// mark happened to span. The runs' own formatting is left exactly as the template set it.
/// </para>
/// <para>
/// A mark is looked for in every part that can carry text — the body, every header and footer,
/// footnotes and endnotes — and inside every paragraph of those parts, which reaches text boxes
/// as well: a modern Word writes a text box twice, once as a drawing under <c>mc:Choice</c> and
/// once as VML under <c>mc:Fallback</c>, and both copies carry the same words. Walking the
/// paragraphs of the whole part visits both, so the two never disagree (ARCHITECTURE §9).
/// </para>
/// </remarks>
internal static class LetterOpenXml
{
    /// <summary>Every part of a Word file that can hold a paragraph of text.</summary>
    /// <param name="document">The open document.</param>
    public static IEnumerable<OpenXmlPart> TextParts(WordprocessingDocument document)
    {
        var main = document.MainDocumentPart;
        if (main is null)
        {
            yield break;
        }

        yield return main;

        foreach (var header in main.HeaderParts)
        {
            yield return header;
        }

        foreach (var footer in main.FooterParts)
        {
            yield return footer;
        }

        if (main.FootnotesPart is { } footnotes)
        {
            yield return footnotes;
        }

        if (main.EndnotesPart is { } endnotes)
        {
            yield return endnotes;
        }
    }

    /// <summary>Every paragraph of a part, text boxes and tables included.</summary>
    /// <param name="part">The part to walk.</param>
    public static IEnumerable<Paragraph> Paragraphs(OpenXmlPart part) =>
        part.RootElement is { } root ? root.Descendants<Paragraph>() : [];

    /// <summary>
    /// The text nodes that belong to this paragraph itself — not to a paragraph nested inside one
    /// of its text boxes, which is walked in its own right.
    /// </summary>
    /// <param name="paragraph">The paragraph.</param>
    public static List<Text> OwnTexts(Paragraph paragraph) =>
        [.. paragraph.Descendants<Text>().Where(t => t.Ancestors<Paragraph>().FirstOrDefault() == paragraph)];

    /// <summary>The paragraph's whole text, as a reader sees it.</summary>
    /// <param name="paragraph">The paragraph.</param>
    public static string TextOf(Paragraph paragraph) =>
        string.Concat(OwnTexts(paragraph).Select(t => t.Text));

    /// <summary>
    /// The paragraph as it would be read aloud: its text nodes, and the places where the template
    /// pressed Enter-inside-the-line or Tab.
    /// </summary>
    /// <remarks>
    /// <see cref="TextOf"/> deliberately reads the text nodes and nothing else, because every
    /// index the composer splices at is an index into that string. This is the other reading, for
    /// whoever has to lay the template out rather than fill it: the reference template's date box
    /// is one paragraph holding «@التاريخ الهجري», a line break, «@التاريخ الميلادي», a line break
    /// and the number, and printing those as one run-together line puts the two dates in the same
    /// word.
    /// </remarks>
    /// <param name="paragraph">The paragraph.</param>
    /// <returns>The text, with a line break written as a newline and a tab as a space.</returns>
    public static string ReadableTextOf(Paragraph paragraph)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var element in paragraph.Descendants())
        {
            if (element.Ancestors<Paragraph>().FirstOrDefault() != paragraph)
            {
                // Inside one of this paragraph's text boxes; that box is read in its own right.
                continue;
            }

            switch (element)
            {
                case Text text:
                    builder.Append(text.Text);
                    break;
                case Break:
                    builder.Append('\n');
                    break;
                case TabChar:
                    builder.Append(' ');
                    break;
                case NoBreakHyphen:
                    builder.Append('‑');
                    break;
                default:
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Joins neighbouring runs that carry exactly the same formatting into one run holding one
    /// text node. This is not what makes replacement work — the splice below already spans runs —
    /// but it keeps the produced file tidy and makes a template that was typed letter by letter
    /// read like one that was typed in one go.
    /// </summary>
    /// <remarks>
    /// The walk is over the paragraph's children in document order, not over its runs alone,
    /// because text can also sit inside a hyperlink, a field, a content control or a tracked
    /// insertion — and <see cref="TextOf"/> reads those too. Two runs are joined only when nothing
    /// that can carry text stands between them; otherwise the joined text the mark search indexes
    /// against would be reordered behind its back.
    /// </remarks>
    /// <param name="paragraph">The paragraph to tidy.</param>
    public static void MergeRuns(Paragraph paragraph)
    {
        var children = paragraph.ChildElements.ToList();
        Run? previous = null;
        string? previousProperties = null;

        foreach (var child in children)
        {
            if (child is not Run run)
            {
                // A marker carries no text of its own and cannot come between two runs' words.
                if (!CarriesNoText(child))
                {
                    previous = null;
                    previousProperties = null;
                }

                continue;
            }

            // Only runs that are nothing but text may be joined: one carrying a break, a picture
            // or a footnote reference keeps its own identity.
            if (!IsPlainText(run))
            {
                previous = null;
                previousProperties = null;
                continue;
            }

            var properties = run.RunProperties?.OuterXml ?? string.Empty;
            if (previous is not null && previousProperties == properties)
            {
                var target = previous.Elements<Text>().Last();
                target.Text += string.Concat(run.Elements<Text>().Select(t => t.Text));
                target.Space = SpaceProcessingModeValues.Preserve;
                foreach (var extra in previous.Elements<Text>().Skip(1).ToList())
                {
                    if (extra != target)
                    {
                        extra.Remove();
                    }
                }

                run.Remove();
                continue;
            }

            // Collapse this run's own text nodes into one, so the merged run stays simple.
            var texts = run.Elements<Text>().ToList();
            if (texts.Count > 1)
            {
                texts[0].Text = string.Concat(texts.Select(t => t.Text));
                texts[0].Space = SpaceProcessingModeValues.Preserve;
                foreach (var extra in texts.Skip(1))
                {
                    extra.Remove();
                }
            }

            previous = run;
            previousProperties = properties;
        }
    }

    /// <summary>
    /// Whether a paragraph child is one of the invisible markers Word scatters between runs —
    /// a bookmark, a proofing marker, a comment anchor, the paragraph's own properties. None of
    /// them can hold a word, so two runs on either side of one are still neighbours.
    /// </summary>
    /// <param name="child">The child element.</param>
    private static bool CarriesNoText(OpenXmlElement child) => child
        is ParagraphProperties
        or BookmarkStart
        or BookmarkEnd
        or ProofError
        or CommentRangeStart
        or CommentRangeEnd;

    private static bool IsPlainText(Run run) =>
        run.ChildElements.All(child => child is RunProperties or Text) && run.Elements<Text>().Any();

    /// <summary>
    /// Finds the first place in <paramref name="paragraph"/> where any of <paramref name="marks"/>
    /// appears, preferring the longest mark when two start at the same character.
    /// </summary>
    /// <param name="paragraph">The paragraph to search.</param>
    /// <param name="marks">The marks to look for, longest first.</param>
    /// <returns>The mark and where it starts, or <c>null</c>.</returns>
    public static (string Mark, int Index)? FindFirst(Paragraph paragraph, IReadOnlyList<string> marks)
    {
        var text = TextOf(paragraph);
        return FindFirst(text, marks);
    }

    /// <summary>Finds the first of <paramref name="marks"/> in <paramref name="text"/>.</summary>
    /// <param name="text">The joined text.</param>
    /// <param name="marks">The marks to look for, longest first.</param>
    public static (string Mark, int Index)? FindFirst(string text, IReadOnlyList<string> marks)
    {
        var best = -1;
        string? found = null;
        foreach (var mark in marks)
        {
            var index = text.IndexOf(mark, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            if (best < 0 || index < best || (index == best && mark.Length > found!.Length))
            {
                best = index;
                found = mark;
            }
        }

        return found is null ? null : (found, best);
    }

    /// <summary>
    /// Puts <paramref name="replacement"/> in place of the <paramref name="length"/> characters
    /// starting at <paramref name="start"/> of the paragraph's joined text, however many runs
    /// they are spread across.
    /// </summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <param name="start">Where the replaced stretch begins.</param>
    /// <param name="length">How long it is.</param>
    /// <param name="replacement">What goes there; may be empty.</param>
    /// <returns>
    /// The text node that now holds the replacement and the index inside it where the replacement
    /// begins, for a caller that must cut the paragraph open at that point.
    /// </returns>
    public static (Text Node, int At)? Splice(Paragraph paragraph, int start, int length, string replacement)
    {
        var texts = OwnTexts(paragraph);
        var offset = 0;
        Text? first = null;
        var firstStart = 0;
        var end = start + length;

        foreach (var node in texts)
        {
            var nodeStart = offset;
            var nodeEnd = offset + node.Text.Length;
            offset = nodeEnd;

            if (nodeEnd <= start || nodeStart >= end)
            {
                continue;
            }

            var from = Math.Max(0, start - nodeStart);
            var to = Math.Min(node.Text.Length, end - nodeStart);

            if (first is null)
            {
                first = node;
                firstStart = from;
                node.Text = node.Text[..from] + replacement + node.Text[to..];
            }
            else
            {
                node.Text = node.Text[..from] + node.Text[to..];
            }

            node.Space = SpaceProcessingModeValues.Preserve;
        }

        return first is null ? null : (first, firstStart);
    }

    /// <summary>
    /// Replaces every occurrence of every mark in <paramref name="values"/> throughout a
    /// paragraph. A value that is empty simply removes the mark, which is what a template that
    /// carries a mark the letter has no data for should do — a printed letter never shows one
    /// (AGREEMENT item 57).
    /// </summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <param name="values">Mark to value.</param>
    /// <returns>How many marks were replaced.</returns>
    public static int ReplaceAll(Paragraph paragraph, IReadOnlyDictionary<string, string> values)
    {
        var order = values.Keys
            .OrderByDescending(k => k.Length)
            .ThenBy(k => k, StringComparer.Ordinal)
            .ToList();
        var replaced = 0;

        // A mark's replacement can itself never contain a mark (the values are a letter's data,
        // and a subject reading "@رقم الصادر" is text, not an instruction), so the scan resumes
        // after the inserted value rather than from the start — no value can be re-read as a mark.
        var from = 0;
        while (true)
        {
            var text = TextOf(paragraph);
            if (from >= text.Length)
            {
                break;
            }

            var hit = FindFirst(text[from..], order);
            if (hit is not { } found)
            {
                break;
            }

            var value = values[found.Mark];

            var at = from + found.Index;
            Splice(paragraph, at, found.Mark.Length, value);
            from = at + value.Length;
            replaced++;
        }

        return replaced;
    }
}
