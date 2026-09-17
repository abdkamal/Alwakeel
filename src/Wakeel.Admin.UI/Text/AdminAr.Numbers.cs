using System.Globalization;

namespace Wakeel.Admin.UI.Text;

/// <summary>
/// The two small things every other area's strings need: putting a number into an Arabic sentence,
/// and keeping a mixed line from coming apart on screen.
/// </summary>
public static partial class AdminAr
{
    /// <summary>
    /// A number as it is written anywhere in the tool: Western digits (0–9), as the design guide
    /// rules («الأرقام غربية») and AGREEMENT item 20 requires for dates, and as الوكيل itself writes
    /// them — the two programs are read side by side and must count in one voice. Arabic-Indic
    /// figures are never produced; <see cref="Bidi"/> keeps a digit run in place inside a sentence.
    /// </summary>
    internal static string Digits(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Keeps a line that mixes Arabic with figures, Latin letters or punctuation from rearranging
    /// itself on screen (AGREEMENT item 55). The shared design system owns the rule; this is only
    /// the short name for it so the string files do not each import it.
    /// </summary>
    internal static string Bidi(string text) => global::Wakeel.Design.Bidi.Bidi.Wrap(text);
}
