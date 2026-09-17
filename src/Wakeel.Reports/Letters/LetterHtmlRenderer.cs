using System.Globalization;
using System.Net;
using System.Text;
using Wakeel.Core.Services;
using Wakeel.Core.Services.Correspondence;

namespace Wakeel.Reports.Letters;

/// <summary>
/// The letter as HTML, laid out like the template, for the preview and for
/// <c>WebView2.PrintToPdf</c> when Word is not available (ARCHITECTURE §9, AGREEMENT item 10).
/// </summary>
/// <remarks>
/// <para>
/// This is not a Word renderer and does not try to be one. It takes the template's own lines in
/// the places the template puts them — the letterhead above the sheet, the date and number block
/// floating against one edge, the addressee against the other, the subject across its own band,
/// the fixed phrases in the flow, the signature apart at the foot — fills the same nine marks in
/// them, and lays them out on a page of the same size. A person comparing the printed PDF with a
/// letter that went through Word sees the same words in the same order and the same places; the
/// typeface metrics are the printer's business.
/// </para>
/// <para>
/// Every value is escaped, and each one is wrapped on its own — not the finished line around it —
/// so that Arabic and Latin text on one line, an office name with an abbreviation in it, a number
/// like <c>و/م/1447/12</c> beside its Arabic label, stays in the order it was typed (AGREEMENT
/// item 11). The result never contains a mark: that is the whole point of it, and a test asserts
/// it.
/// </para>
/// </remarks>
public static class LetterHtmlRenderer
{
    /// <summary>Renders the letter from the template's layout.</summary>
    /// <param name="layout">The template's lines, marks still in them, each in its place.</param>
    /// <param name="letter">The letter's data.</param>
    public static string Render(IReadOnlyList<LetterTemplateLine> layout, LetterData letter)
    {
        ArgumentNullException.ThrowIfNull(letter);
        var values = LetterComposer.SimpleValues(letter);
        var (width, height) = PageMillimetres(letter.PageSize);
        var lines = Expand(layout ?? []);

        var html = new StringBuilder(4096);
        html.Append("<!DOCTYPE html>\n<html dir=\"rtl\" lang=\"ar\">\n<head>\n");
        html.Append("<meta charset=\"utf-8\">\n");
        html.Append("<title>").Append(WebUtility.HtmlEncode(Title(letter))).Append("</title>\n");
        html.Append("<style>\n").Append(Style(width, height)).Append("</style>\n");
        html.Append("</head>\n<body>\n<div class=\"sheet\">\n");

        AppendArea(html, lines, LetterTemplatePlace.Letterhead, "header", "letterhead", values);

        html.Append("<div class=\"flow\">\n");
        var wrote = AppendFlow(html, lines, values, letter);
        if (!wrote)
        {
            // A template with no body mark still prints the letter's text rather than nothing:
            // the missing mark is reported by the check on upload, not by a blank page.
            AppendBody(html, string.Empty, string.Empty, string.Empty, values, letter.Body);
        }

        html.Append("</div>\n");

        AppendArea(html, lines, LetterTemplatePlace.Foot, "footer", "sheet-foot", values);

        html.Append("</div>\n</body>\n</html>\n");
        return html.ToString();
    }

    /// <summary>
    /// One template line can be several printed lines: the reference template's date block is a
    /// single paragraph holding the Hijri date, a line break, the Gregorian date, a line break and
    /// the number. Each break starts a line of its own, so the two dates never run into one.
    /// </summary>
    /// <param name="layout">The template's lines.</param>
    private static List<LetterTemplateLine> Expand(IReadOnlyList<LetterTemplateLine> layout)
    {
        var lines = new List<LetterTemplateLine>(layout.Count + 8);
        foreach (var line in layout)
        {
            var text = line.Text ?? string.Empty;
            if (text.IndexOf('\n', StringComparison.Ordinal) < 0)
            {
                lines.Add(line with { Text = text });
                continue;
            }

            foreach (var segment in text.Split('\n'))
            {
                lines.Add(line with { Text = segment });
            }
        }

        return lines;
    }

    /// <summary>Writes the letterhead or the foot, when the template has one.</summary>
    private static void AppendArea(
        StringBuilder html,
        List<LetterTemplateLine> lines,
        LetterTemplatePlace place,
        string element,
        string className,
        IReadOnlyDictionary<string, string> values)
    {
        var area = lines.Where(line => line.Place == place).ToList();
        if (area.All(line => Fill(line.Text, values).Trim().Length == 0))
        {
            return;
        }

        html.Append('<').Append(element).Append(" class=\"").Append(className).Append("\">\n");
        foreach (var line in area)
        {
            if (Fill(line.Text, values).Trim().Length == 0)
            {
                continue;
            }

            AppendLine(html, line, values);
        }

        html.Append("</").Append(element).Append(">\n");
    }

    /// <summary>
    /// Writes the sheet's flow: its paragraphs, its floating boxes where the template anchors them,
    /// and the letter's own text where the body mark stands.
    /// </summary>
    /// <returns>Whether the letter's text was written.</returns>
    private static bool AppendFlow(
        StringBuilder html,
        List<LetterTemplateLine> lines,
        IReadOnlyDictionary<string, string> values,
        LetterData letter)
    {
        var flow = lines
            .Where(line => line.Place is LetterTemplatePlace.Flow
                or LetterTemplatePlace.LeftBlock
                or LetterTemplatePlace.RightBlock
                or LetterTemplatePlace.Band)
            .ToList();

        var wroteBody = false;
        var wroteAnything = false;
        var blanks = 0;
        var index = 0;

        while (index < flow.Count)
        {
            var line = flow[index];

            if (line.Block > 0)
            {
                var end = index;
                while (end < flow.Count && flow[end].Block == line.Block)
                {
                    end++;
                }

                html.Append("<aside class=\"block ").Append(SideClass(line.Place)).Append("\">\n");
                for (var inside = index; inside < end; inside++)
                {
                    AppendLine(html, flow[inside], values);
                }

                html.Append("</aside>\n");
                index = end;
                continue;
            }

            if (Fill(line.Text, values).Trim().Length == 0)
            {
                // Templates pad the sheet with empty paragraphs to push the closing phrase down.
                // Word reflows around the letter's own length; a printed page cannot, so a run of
                // them becomes one blank line and the signature stays on the first page.
                blanks++;
                index++;
                continue;
            }

            if (wroteAnything && blanks > 0)
            {
                html.Append("<p class=\"blank\"></p>\n");
            }

            blanks = 0;

            if (line.Text.Contains(LetterMarks.Body, StringComparison.Ordinal))
            {
                var at = line.Text.IndexOf(LetterMarks.Body, StringComparison.Ordinal);
                var prefix = line.Text[..at];
                var suffix = line.Text[(at + LetterMarks.Body.Length)..];
                AppendBody(html, prefix, suffix, AlignClass(line.Align), values, letter.Body);
                wroteBody = true;
            }
            else
            {
                AppendLine(html, line, values);
            }

            wroteAnything = true;
            index++;
        }

        return wroteBody;
    }

    private static void AppendLine(
        StringBuilder html,
        LetterTemplateLine line,
        IReadOnlyDictionary<string, string> values)
    {
        html.Append("<p class=\"line").Append(AlignClass(line.Align)).Append("\">")
            .Append(FillHtml(line.Text, values))
            .Append("</p>\n");
    }

    private static string AlignClass(LetterTemplateAlign align) => align switch
    {
        LetterTemplateAlign.Center => " center",
        LetterTemplateAlign.Justify => " justify",
        _ => string.Empty,
    };

    private static string SideClass(LetterTemplatePlace place) => place switch
    {
        LetterTemplatePlace.LeftBlock => "left",
        LetterTemplatePlace.RightBlock => "right",
        _ => "band",
    };

    private static string Title(LetterData letter) =>
        string.IsNullOrWhiteSpace(letter.SubjectAr)
            ? (string.IsNullOrWhiteSpace(letter.NumberAr) ? CoreAr.Letter.DraftNumber : letter.NumberAr!)
            : letter.SubjectAr;

    private static void AppendBody(
        StringBuilder html,
        string prefix,
        string suffix,
        string alignClass,
        IReadOnlyDictionary<string, string> values,
        LetterBody body)
    {
        // A template line that writes the body mark twice gets the letter once — pouring it in
        // again would put the letter inside itself — and the mark it did not use is taken out, so
        // that no '@' ever reaches the printed sheet (AGREEMENT item 57).
        prefix = Without(prefix, LetterMarks.Body);
        suffix = Without(suffix, LetterMarks.Body);

        // The template's own words and the letter's data are kept apart: the first are written as
        // they stand, the second are each isolated, because it is a value — a number, an office
        // name with a Latin word in it — that would otherwise be reordered by the line around it.
        var prefixText = Fill(prefix, values);
        var suffixText = Fill(suffix, values);
        var prefixHtml = FillHtml(prefix, values);
        var suffixHtml = FillHtml(suffix, values);
        var paragraphClass = "line body" + alignClass;

        var blocks = body.Blocks.Where(b => !string.IsNullOrWhiteSpace(b.Text)).ToList();
        if (blocks.Count == 0)
        {
            if (prefixText.Trim().Length > 0 || suffixText.Trim().Length > 0)
            {
                html.Append("<p class=\"").Append(paragraphClass).Append("\">")
                    .Append(prefixHtml).Append(suffixHtml).Append("</p>\n");
            }

            return;
        }

        var index = 0;
        string? openList = null;

        // The opening phrase never becomes a list item: it keeps the line above the list, which
        // is where the template put it.
        var prefixPending = prefixText.Length > 0;
        if (prefixPending && blocks[0].Kind != LetterBlockKind.Paragraph && prefixText.Trim().Length > 0)
        {
            html.Append("<p class=\"").Append(paragraphClass).Append("\">")
                .Append(prefixHtml).Append("</p>\n");
            prefixPending = false;
        }

        while (index < blocks.Count)
        {
            var block = blocks[index];
            var wanted = block.Kind switch
            {
                LetterBlockKind.Numbered => "ol",
                LetterBlockKind.Bulleted => "ul",
                _ => null,
            };

            if (openList != wanted)
            {
                if (openList is not null)
                {
                    html.Append("</").Append(openList).Append(">\n");
                }

                if (wanted is not null)
                {
                    html.Append('<').Append(wanted).Append(" class=\"body-list\">\n");
                }

                openList = wanted;
            }

            var isLast = index == blocks.Count - 1;
            var tail = isLast && suffixText.Trim().Length > 0 ? suffixHtml : string.Empty;

            if (wanted is null)
            {
                html.Append("<p class=\"").Append(paragraphClass).Append("\">");
                if (prefixPending)
                {
                    html.Append(prefixHtml);
                    prefixPending = false;
                }

                AppendSpans(html, block);
                if (tail.Length > 0)
                {
                    html.Append(tail);
                }

                html.Append("</p>\n");
            }
            else
            {
                html.Append("<li>");
                AppendSpans(html, block);
                if (tail.Length > 0)
                {
                    html.Append(tail);
                }

                html.Append("</li>\n");
            }

            index++;
        }

        if (openList is not null)
        {
            html.Append("</").Append(openList).Append(">\n");
        }
    }

    private static string Without(string text, string mark) =>
        text.Contains(mark, StringComparison.Ordinal)
            ? text.Replace(mark, string.Empty, StringComparison.Ordinal)
            : text;

    private static void AppendSpans(StringBuilder html, LetterBlock block)
    {
        foreach (var span in block.Spans)
        {
            if (span.Text.Length == 0)
            {
                continue;
            }

            if (span.Bold)
            {
                html.Append("<strong>").Append(Bidi(span.Text)).Append("</strong>");
            }
            else
            {
                html.Append(Bidi(span.Text));
            }
        }
    }

    /// <summary>Replaces the eight simple marks in one of the template's lines.</summary>
    private static string Fill(string line, IReadOnlyDictionary<string, string> values)
    {
        if (line.IndexOf('@', StringComparison.Ordinal) < 0)
        {
            return line;
        }

        var text = line;
        foreach (var mark in LetterMarks.ByLength)
        {
            if (values.TryGetValue(mark, out var value))
            {
                text = text.Replace(mark, value, StringComparison.Ordinal);
            }
        }

        return text;
    }

    /// <summary>
    /// Writes one of the template's lines as HTML: its own words as it wrote them, and each value
    /// الوكيل puts into it isolated on its own, so a line that mixes Arabic with Latin letters or
    /// digits reads in the order it was written (AGREEMENT item 11). Isolating the whole finished
    /// line instead would isolate it from its neighbours and leave the value inside it exposed to
    /// the label beside it — the very place a number or an office name goes wrong.
    /// </summary>
    /// <param name="line">The template's line, marks still in it.</param>
    /// <param name="values">Mark to value.</param>
    private static string FillHtml(string line, IReadOnlyDictionary<string, string> values)
    {
        if (line.IndexOf('@', StringComparison.Ordinal) < 0)
        {
            return WebUtility.HtmlEncode(line);
        }

        var html = new StringBuilder(line.Length + 32);
        var at = 0;
        while (at < line.Length)
        {
            var next = line.IndexOf('@', at);
            if (next < 0)
            {
                break;
            }

            string? hit = null;
            foreach (var mark in LetterMarks.ByLength)
            {
                if (values.ContainsKey(mark)
                    && string.CompareOrdinal(line, next, mark, 0, mark.Length) == 0)
                {
                    hit = mark;
                    break;
                }
            }

            if (hit is null)
            {
                // An unknown mark stays exactly as the template wrote it — the check on upload is
                // where a person is told about it, not the printed letter.
                html.Append(WebUtility.HtmlEncode(line[at..(next + 1)]));
                at = next + 1;
                continue;
            }

            html.Append(WebUtility.HtmlEncode(line[at..next]));
            html.Append(Bidi(values[hit]));
            at = next + hit.Length;
        }

        if (at < line.Length)
        {
            html.Append(WebUtility.HtmlEncode(line[at..]));
        }

        return html.ToString();
    }

    /// <summary>
    /// Escapes a value and isolates it, so a line that mixes Arabic with Latin letters or digits
    /// reads in the order it was written (AGREEMENT item 11). A value the letter has nothing for
    /// leaves nothing behind.
    /// </summary>
    private static string Bidi(string text) =>
        text.Length == 0 ? string.Empty : $"<bdi>{WebUtility.HtmlEncode(text)}</bdi>";

    private static (int Width, int Height) PageMillimetres(LetterPageSize size) =>
        size == LetterPageSize.A5 ? (148, 210) : (210, 297);

    private static string Style(int width, int height) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""
        @page { size: {{width}}mm {{height}}mm; margin: 0; }
        html, body { margin: 0; padding: 0; background: #fff; }
        body {
          font-family: "Noto Sans Arabic", "Segoe UI", Tahoma, sans-serif;
          color: #111;
          -webkit-print-color-adjust: exact;
          print-color-adjust: exact;
        }
        .sheet {
          box-sizing: border-box;
          width: {{width}}mm;
          min-height: {{height}}mm;
          margin: 0 auto;
          padding-block: 12mm 18mm;
          padding-inline: 20mm;
          direction: rtl;
          text-align: start;
          font-size: {{(width <= 148 ? "11pt" : "13pt")}};
          line-height: 1.9;
        }
        .letterhead { display: block; margin: 0 0 8mm; text-align: center; }
        .letterhead .line { margin: 0; }
        .flow { display: block; }
        .flow::after { content: ""; display: block; clear: both; }
        .sheet-foot { display: block; clear: both; margin: 8mm 0 0; text-align: center; font-size: 0.85em; }
        .line { margin: 0 0 2mm; unicode-bidi: plaintext; }
        .blank { margin: 0 0 4mm; min-height: 1em; }
        .body { text-align: justify; }
        .center { text-align: center; }
        .justify { text-align: justify; }
        .block { box-sizing: border-box; max-width: 45%; margin: 0 0 3mm; text-align: center; }
        .block.left { float: left; }
        .block.right { float: right; }
        .block.band { float: none; clear: both; max-width: 100%; }
        .block .line { margin: 0; }
        .body-list { margin: 0 0 3mm; padding-inline-start: 8mm; }
        .body-list li { margin: 0 0 1.5mm; unicode-bidi: plaintext; text-align: justify; }
        strong { font-weight: 700; }
        bdi { unicode-bidi: isolate; }
        @media print { .sheet { margin: 0; } }
        """);
}
