using System.Text;

namespace Wakeel.Admin.UI.Services.Export;

/// <summary>
/// Turns an Arabic sentence into the exact glyphs a page has to draw, in the order it has to draw
/// them: each letter in the shape its neighbours give it, and the whole line laid out right to left.
/// </summary>
/// <remarks>
/// <para>
/// A browser does this for every screen of the tool; a PDF does not. The provisional guide
/// (<see cref="AdminGuidePdf"/>) is written by this tool itself, so the joining and the direction
/// have to be settled here or the file would come out as a line of disconnected letters running the
/// wrong way — AGREEMENT item 55, on paper.
/// </para>
/// <para>
/// This is deliberately the small version of the problem: the four contextual shapes from the Arabic
/// Presentation Forms-B block, the four lam-alef ligatures, and a single pass that puts the runs of
/// Western digits and Latin back the right way round inside a right-to-left line. It is enough for a
/// one page notice and nothing more; the real guide (B8) is authored in a publishing program.
/// </para>
/// </remarks>
public static class ArabicShaping
{
    /// <summary>The four shapes a letter can take, in the order the presentation block lists them.</summary>
    private enum Form
    {
        Isolated = 0,
        Final = 1,
        Initial = 2,
        Medial = 3,
    }

    /// <summary>
    /// Every letter that has contextual shapes: its four forms, and zero where the letter has none
    /// (the right-joining letters have an isolated and a final shape only).
    /// </summary>
    private static readonly Dictionary<char, char[]> Forms = new()
    {
        ['ء'] = ['ﺀ', '\0', '\0', '\0'],
        ['آ'] = ['ﺁ', 'ﺂ', '\0', '\0'],
        ['أ'] = ['ﺃ', 'ﺄ', '\0', '\0'],
        ['ؤ'] = ['ﺅ', 'ﺆ', '\0', '\0'],
        ['إ'] = ['ﺇ', 'ﺈ', '\0', '\0'],
        ['ئ'] = ['ﺉ', 'ﺊ', 'ﺋ', 'ﺌ'],
        ['ا'] = ['ﺍ', 'ﺎ', '\0', '\0'],
        ['ب'] = ['ﺏ', 'ﺐ', 'ﺑ', 'ﺒ'],
        ['ة'] = ['ﺓ', 'ﺔ', '\0', '\0'],
        ['ت'] = ['ﺕ', 'ﺖ', 'ﺗ', 'ﺘ'],
        ['ث'] = ['ﺙ', 'ﺚ', 'ﺛ', 'ﺜ'],
        ['ج'] = ['ﺝ', 'ﺞ', 'ﺟ', 'ﺠ'],
        ['ح'] = ['ﺡ', 'ﺢ', 'ﺣ', 'ﺤ'],
        ['خ'] = ['ﺥ', 'ﺦ', 'ﺧ', 'ﺨ'],
        ['د'] = ['ﺩ', 'ﺪ', '\0', '\0'],
        ['ذ'] = ['ﺫ', 'ﺬ', '\0', '\0'],
        ['ر'] = ['ﺭ', 'ﺮ', '\0', '\0'],
        ['ز'] = ['ﺯ', 'ﺰ', '\0', '\0'],
        ['س'] = ['ﺱ', 'ﺲ', 'ﺳ', 'ﺴ'],
        ['ش'] = ['ﺵ', 'ﺶ', 'ﺷ', 'ﺸ'],
        ['ص'] = ['ﺹ', 'ﺺ', 'ﺻ', 'ﺼ'],
        ['ض'] = ['ﺽ', 'ﺾ', 'ﺿ', 'ﻀ'],
        ['ط'] = ['ﻁ', 'ﻂ', 'ﻃ', 'ﻄ'],
        ['ظ'] = ['ﻅ', 'ﻆ', 'ﻇ', 'ﻈ'],
        ['ع'] = ['ﻉ', 'ﻊ', 'ﻋ', 'ﻌ'],
        ['غ'] = ['ﻍ', 'ﻎ', 'ﻏ', 'ﻐ'],
        ['ف'] = ['ﻑ', 'ﻒ', 'ﻓ', 'ﻔ'],
        ['ق'] = ['ﻕ', 'ﻖ', 'ﻗ', 'ﻘ'],
        ['ك'] = ['ﻙ', 'ﻚ', 'ﻛ', 'ﻜ'],
        ['ل'] = ['ﻝ', 'ﻞ', 'ﻟ', 'ﻠ'],
        ['م'] = ['ﻡ', 'ﻢ', 'ﻣ', 'ﻤ'],
        ['ن'] = ['ﻥ', 'ﻦ', 'ﻧ', 'ﻨ'],
        ['ه'] = ['ﻩ', 'ﻪ', 'ﻫ', 'ﻬ'],
        ['و'] = ['ﻭ', 'ﻮ', '\0', '\0'],
        ['ى'] = ['ﻯ', 'ﻰ', '\0', '\0'],
        ['ي'] = ['ﻱ', 'ﻲ', 'ﻳ', 'ﻴ'],
    };

    /// <summary>The four alefs that fuse with a preceding lam, and the pair of shapes each fusion has.</summary>
    private static readonly Dictionary<char, char[]> LamAlef = new()
    {
        ['آ'] = ['ﻵ', 'ﻶ'],
        ['أ'] = ['ﻷ', 'ﻸ'],
        ['إ'] = ['ﻹ', 'ﻺ'],
        ['ا'] = ['ﻻ', 'ﻼ'],
    };

    /// <summary>Pairs that have to swap when a line is laid out right to left.</summary>
    private static readonly Dictionary<char, char> Mirrored = new()
    {
        ['('] = ')',
        [')'] = '(',
        ['['] = ']',
        [']'] = '[',
        ['{'] = '}',
        ['}'] = '{',
        ['<'] = '>',
        ['>'] = '<',

        // The quotation marks Arabic actually writes with: they point the other way in a line that
        // runs the other way, so a quoted name opens on the right and closes on the left.
        ['«'] = '»',
        ['»'] = '«',
    };

    /// <summary>
    /// The glyphs of one line, in the order a right-to-left page draws them: leftmost first, so the
    /// caller can hand the result straight to a drawing routine that only moves forwards.
    /// </summary>
    public static string ToVisualOrder(string? text)
    {
        var joined = Join(text);
        if (joined.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(joined.Length);

        // Walk the logical line backwards, which is the right-to-left order; a run of digits, Latin
        // letters or the punctuation between them is put back the way round it is read.
        var index = joined.Length - 1;
        while (index >= 0)
        {
            if (!IsLeftToRight(joined[index]))
            {
                builder.Append(Mirrored.TryGetValue(joined[index], out var mirrored) ? mirrored : joined[index]);
                index--;
                continue;
            }

            var end = index;
            var start = index;
            while (start >= 0 && IsLeftToRight(joined[start]))
            {
                start--;
            }

            // Trailing spaces belong to the Arabic around the run, not inside it.
            var runStart = start + 1;
            builder.Append(joined.AsSpan(runStart, end - runStart + 1));
            index = start;
        }

        return builder.ToString();
    }

    /// <summary>Each letter in the shape its neighbours give it, still in reading order.</summary>
    public static string Join(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];

            // لا and its three cousins are one glyph, never two, and the pair is consumed together.
            if (current == 'ل'
                && index + 1 < text.Length
                && LamAlef.TryGetValue(text[index + 1], out var ligature))
            {
                builder.Append(JoinsForward(Previous(text, index)) ? ligature[1] : ligature[0]);
                index++;
                continue;
            }

            if (!Forms.TryGetValue(current, out var shapes))
            {
                builder.Append(current);
                continue;
            }

            var joinsRight = JoinsForward(Previous(text, index));
            var joinsLeft = JoinsBackward(Next(text, index));

            var form = (joinsRight, joinsLeft) switch
            {
                (true, true) => Form.Medial,
                (true, false) => Form.Final,
                (false, true) => Form.Initial,
                _ => Form.Isolated,
            };

            // A right-joining letter has no initial or medial shape; it falls back to the pair it
            // does have, which is exactly how it is written by hand.
            var glyph = shapes[(int)form];
            if (glyph == '\0')
            {
                glyph = joinsRight ? shapes[(int)Form.Final] : shapes[(int)Form.Isolated];
            }

            builder.Append(glyph);
        }

        return builder.ToString();
    }

    /// <summary>The letter before this one, ignoring the marks that sit above and below.</summary>
    private static char Previous(string text, int index)
    {
        for (var at = index - 1; at >= 0; at--)
        {
            if (!IsMark(text[at]))
            {
                return text[at];
            }
        }

        return '\0';
    }

    private static char Next(string text, int index)
    {
        for (var at = index + 1; at < text.Length; at++)
        {
            if (!IsMark(text[at]))
            {
                return text[at];
            }
        }

        return '\0';
    }

    /// <summary>Whether the letter before can carry a join towards the letter after it.</summary>
    private static bool JoinsForward(char character) =>
        character == 'ـ'
        || (Forms.TryGetValue(character, out var shapes) && shapes[(int)Form.Initial] != '\0');

    /// <summary>Whether the letter after can accept a join from the letter before it.</summary>
    private static bool JoinsBackward(char character) =>
        character == 'ـ' || Forms.ContainsKey(character);

    private static bool IsMark(char character) => character is >= 'ً' and <= 'ْ' or 'ٰ';

    /// <summary>Whether this character belongs to a run that is read from the left.</summary>
    private static bool IsLeftToRight(char character) =>
        character is >= '0' and <= '9'
            or >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or '/' or '.' or '-' or '_' or ':' or '@';
}
