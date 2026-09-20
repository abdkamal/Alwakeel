namespace Wakeel.Core.Tests.Documents;

/// <summary>
/// A test that only means anything where the reading files are present. They are fetched by
/// <c>build/get-models.ps1</c> and are deliberately not in the repository, so on a machine that
/// has not fetched them the test is skipped with its reason — the run summary then says what is
/// missing instead of counting a test that asserted nothing as passed.
/// </summary>
/// <remarks>
/// The same attribute also checks for a font that can draw Arabic, because every one of these
/// tests draws its own page before reading it back: with no Arabic glyphs there is nothing on the
/// page to find, and the failure would say something untrue about the reader.
/// </remarks>
public sealed class OcrFactAttribute : FactAttribute
{
    /// <summary>Decides at discovery whether this machine can run the test.</summary>
    public OcrFactAttribute()
    {
        if (!OcrTestImages.HasTessdata)
        {
            Skip = OcrTestImages.SkipReason;
            return;
        }

        if (!OcrTestImages.HasArabicFont)
        {
            Skip = OcrTestImages.NoFontReason;
        }
    }
}
