using System.Text;

namespace Wakeel.Core.Services;

/// <summary>
/// Arabic text normalization (for search indexing) and deterministic plural agreement
/// (ARCHITECTURE.md §8, §12; AGREEMENT item 53(c): "جمل عربية قالبية حتمية تراعي المفرد
/// والمثنى والجمع، بلا نموذج لغوي").
/// </summary>
public static class ArabicText
{
    private const char Tatweel = 'ـ';

    /// <summary>Tashkeel (diacritics) code points stripped by <see cref="Normalize"/>.</summary>
    private static readonly HashSet<char> Tashkeel =
    [
        'ؐ', 'ؑ', 'ؒ', 'ؓ', 'ؔ', 'ؕ', 'ؖ', 'ؗ', 'ؘ', 'ؙ', 'ؚ',
        'ً', 'ٌ', 'ٍ', 'َ', 'ُ', 'ِ', 'ّ', 'ْ', 'ٓ', 'ٔ', 'ٕ',
        'ٖ', 'ٗ', '٘', 'ٙ', 'ٚ', 'ٛ', 'ٜ', 'ٝ', 'ٞ', 'ٟ', 'ٰ',
        'ۖ', 'ۗ', 'ۘ', 'ۙ', 'ۚ', 'ۛ', 'ۜ', '۟', '۠', 'ۡ', 'ۢ',
        'ۣ', 'ۤ', 'ۧ', 'ۨ', '۪', '۫', '۬', 'ۭ',
    ];

    /// <summary>
    /// Normalizes Arabic text for search: unifies alef variants (أ/إ/آ/ٱ → ا) and ى → ي and
    /// ة → ه, strips tashkeel and tatweel, and collapses surrounding whitespace. Non-Arabic
    /// text (including mixed Arabic/English) passes through unchanged aside from that stripping.
    /// </summary>
    /// <remarks>
    /// Index-destructive: collapsing whitespace and dropping characters shifts every later
    /// character's offset relative to the original text, so the result must never be used to
    /// directly locate OCR word bounding boxes (ARCHITECTURE.md §8) against the original text —
    /// a caller that needs offset alignment (e.g. search-result highlighting on a scanned page)
    /// must keep its own original-to-normalized offset map rather than reuse string positions.
    /// </remarks>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c == Tatweel || Tashkeel.Contains(c))
            {
                continue;
            }

            builder.Append(c switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا', // أ إ آ ٱ → ا
                'ى' => 'ي', // ى → ي
                'ة' => 'ه', // ة → ه
                _ => c,
            });
        }

        return CollapseWhitespace(builder.ToString());
    }

    private static string CollapseWhitespace(string value)
    {
        var trimmed = value.Trim();
        var builder = new StringBuilder(trimmed.Length);
        var lastWasSpace = false;
        foreach (var c in trimmed)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
            }
            else
            {
                builder.Append(c);
                lastWasSpace = false;
            }
        }

        return builder.ToString();
    }

    /// <summary>Prefix Arabic puts before a plural to say "no ...", as in «لا مهام».</summary>
    private const string NonePrefix = "لا ";

    /// <summary>
    /// Chooses the grammatically-agreeing form for <paramref name="count"/> (ARCHITECTURE.md §12,
    /// ruling of 2026-09-16): 1 uses <paramref name="singular"/>, 2 uses <paramref name="dual"/>,
    /// 3-10 use <paramref name="plural"/> (broken plural, e.g. "مهام"), 11+ uses
    /// <paramref name="pluralOver10"/> (the singular-tamyiz form Arabic uses from 11 upward, e.g.
    /// "11 مهمة"), and 0 renders as «لا » followed by <paramref name="plural"/> with no number at
    /// all («لا مهام») — which is how a count of nothing is said in a sentence — unless the caller
    /// supplies its own <paramref name="zero"/> text, which is then used verbatim.
    /// </summary>
    /// <param name="count">The count to agree with; a negative count agrees by its magnitude.</param>
    /// <param name="singular">Form for exactly one, e.g. "مهمة".</param>
    /// <param name="dual">Form for exactly two, e.g. "مهمتان".</param>
    /// <param name="plural">Broken plural for 3-10, also used to build the zero phrase, e.g. "مهام".</param>
    /// <param name="pluralOver10">Singular-tamyiz form for 11 and above, e.g. "مهمة".</param>
    /// <param name="zero">Optional replacement for the whole zero phrase, e.g. "لا مهام بعد".</param>
    public static string Plural(int count, string singular, string dual, string plural, string pluralOver10, string? zero = null)
    {
        // Math.Abs(int.MinValue) overflows (int.MinValue has no positive counterpart); treat it
        // as the largest magnitude instead of throwing, since it only ever lands in the 11+ bucket.
        var n = count == int.MinValue ? int.MaxValue : Math.Abs(count);
        return n switch
        {
            0 => zero ?? NonePrefix + plural,
            1 => singular,
            2 => dual,
            >= 3 and <= 10 => plural,
            _ => pluralOver10,
        };
    }
}
