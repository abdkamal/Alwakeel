using System.Text;
using System.Text.RegularExpressions;

namespace Wakeel.Design.Bidi;

/// <summary>
/// Helpers implementing AGREEMENT item 55 (full RTL support with mixed Arabic/Latin text): every
/// Latin or numeric token embedded in an Arabic sentence (official numbers, device ids, file names
/// and extensions, version strings, technical terms such as USB/QR/OCR/WebView2, amounts, dates)
/// must be isolated with Unicode bidi control characters so punctuation around it stays in place
/// and the base paragraph direction stays RTL regardless of the first character.
/// </summary>
public static partial class Bidi
{
    /// <summary>U+2066 LEFT-TO-RIGHT ISOLATE — opens an isolated left-to-right run.</summary>
    public const char Lri = '⁦';

    /// <summary>U+2069 POP DIRECTIONAL ISOLATE — closes an isolate opened by <see cref="Lri"/> or RLI.</summary>
    public const char Pdi = '⁩';

    /// <summary>U+200F RIGHT-TO-LEFT MARK — a zero-width strong-RTL hint.</summary>
    public const char Rlm = '‏';

    // A "Latin/numeric token" is a maximal run of ASCII letters, digits, the punctuation that
    // commonly glues such tokens together (./-_:@+,), and single internal spaces — so a multi-word
    // run like "Canon DR-C240" or "184 GB" stays inside ONE isolate instead of becoming two adjacent
    // isolates (which the UBA then reorders relative to each other under the RTL paragraph
    // direction, e.g. "184 GB" -> "GB 184"). Anchored on an alphanumeric at BOTH ends so a leading/
    // trailing space or trailing sentence punctuation (a final "."، for example) is never swallowed
    // into the isolated run and pushed to the wrong end of the RTL line — the exact failure
    // DESIGN-GUIDE.md's bidi isolation exists to prevent.
    [GeneratedRegex(@"[A-Za-z0-9](?:[A-Za-z0-9./\\_:@+,\- ]*[A-Za-z0-9])?")]
    private static partial Regex TokenPattern();

    /// <summary>
    /// Wraps every Latin/numeric token found in <paramref name="text"/> with
    /// <see cref="Lri"/>/<see cref="Pdi"/> isolates, and — when the string does not already start
    /// with a strong-RTL character — prefixes it with <see cref="Rlm"/> so the paragraph still
    /// reads right-to-left even if it happens to start with such a token. Arabic text between
    /// tokens is left untouched. Safe to call on plain Arabic text (returns it unchanged) or on an
    /// empty/null string (returns it unchanged).
    /// </summary>
    public static string Wrap(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var wrapped = TokenPattern().Replace(text, m => Lri + m.Value + Pdi);

        return NeedsLeadingMark(wrapped) ? Rlm + wrapped : wrapped;
    }

    private static bool NeedsLeadingMark(string text)
    {
        foreach (var ch in text)
        {
            if (ch is Lri or Pdi or Rlm)
            {
                continue;
            }

            // A strong-RTL character (Arabic block) means the paragraph already starts RTL.
            return !IsStrongRtl(ch);
        }

        return false;
    }

    private static bool IsStrongRtl(char ch)
        => ch is (>= '؀' and <= 'ۿ') or (>= 'ݐ' and <= 'ݿ') or (>= 'ﭐ' and <= '﷿') or (>= 'ﹰ' and <= '﻿');
}
