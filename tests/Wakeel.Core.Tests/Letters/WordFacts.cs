using Wakeel.Reports.Letters;

namespace Wakeel.Core.Tests.Letters;

/// <summary>
/// A test that only means anything where Word is installed. Where it is not, the test is skipped
/// with its reason, so the run summary says so instead of counting a test that asserted nothing
/// as passed.
/// </summary>
public sealed class WordFactAttribute : FactAttribute
{
    /// <summary>Decides at discovery whether this machine can run the test.</summary>
    public WordFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Word يعمل على Windows فقط";
            return;
        }

        if (!WordSession.IsAvailable)
        {
            Skip = "Word غير مثبّت على هذا الجهاز";
        }
    }
}

/// <summary>
/// The other half: a test about what happens when Word is absent, which has nothing to prove on a
/// machine that has it.
/// </summary>
public sealed class NoWordFactAttribute : FactAttribute
{
    /// <summary>Decides at discovery whether this machine can run the test.</summary>
    public NoWordFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "هذا الاختبار يعمل على Windows فقط";
            return;
        }

        if (WordSession.IsAvailable)
        {
            Skip = "Word مثبّت على هذا الجهاز؛ لا محل لهذا الاختبار";
        }
    }
}
