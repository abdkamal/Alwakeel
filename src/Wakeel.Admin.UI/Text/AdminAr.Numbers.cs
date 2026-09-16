using System.Globalization;

namespace Wakeel.Admin.UI.Text;

/// <summary>
/// The two small things every other area's strings need: putting a number into an Arabic sentence,
/// and keeping a mixed line from coming apart on screen.
/// </summary>
public static partial class AdminAr
{
    /// <summary>
    /// A number as it is written inside an Arabic sentence. The tool's prose already counts in
    /// Arabic-Indic figures — «١٢ حرفًا فأكثر» on A01, «من ٦» on A03 — and a sentence that mixed the
    /// two would read as two voices. Figures standing on their own in a table or on a card keep the
    /// plain shape, which is how a code or a serial number is read back to somebody.
    /// </summary>
    internal static string Digits(int value)
    {
        var plain = value.ToString(CultureInfo.InvariantCulture);
        return string.Create(plain.Length, plain, static (span, source) =>
        {
            for (var at = 0; at < source.Length; at++)
            {
                var character = source[at];
                span[at] = character is >= '0' and <= '9'
                    ? (char)('٠' + (character - '0'))
                    : character;
            }
        });
    }

    /// <summary>
    /// Keeps a line that mixes Arabic with figures, Latin letters or punctuation from rearranging
    /// itself on screen (AGREEMENT item 55). The shared design system owns the rule; this is only
    /// the short name for it so the string files do not each import it.
    /// </summary>
    internal static string Bidi(string text) => global::Wakeel.Design.Bidi.Bidi.Wrap(text);
}
