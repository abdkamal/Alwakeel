using System.Diagnostics;
using System.Runtime.Versioning;
using Wakeel.Reports.Letters;

namespace Wakeel.Core.Tests.Letters;

/// <summary>
/// The one test that actually talks to Word (AGREEMENT item 10, ARCHITECTURE §9).
/// </summary>
/// <remarks>
/// It runs only where Word is installed, and is SKIPPED with its reason — not quietly passed —
/// everywhere else, so a run that could not try says so in its own summary. "Word غير متوفر" must
/// never be what makes a build red: it is a state الوكيل is built to work in, and every other
/// test in this folder proves the letter is composed, previewed and rendered without Word at all.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WordSessionTests
{
    [WordFact]
    public void Word_turns_a_composed_letter_into_a_pdf_and_then_exits()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wakeel-word-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var documentPath = Path.Combine(folder, "letter.docx");
        var pdfPath = Path.Combine(folder, "letter.pdf");

        try
        {
            var composed = new LetterComposer().Compose(LetterWorld.ReferenceTemplate(), LetterWorld.Sample());
            File.WriteAllBytes(documentPath, composed);

            var before = WordProcessIds();
            var outcome = WordSession.ExportPdf(documentPath, pdfPath);

            Assert.Equal(WordOutcome.Done, outcome);
            Assert.True(File.Exists(pdfPath), "Word was asked for a PDF and none appeared.");
            Assert.True(new FileInfo(pdfPath).Length > 0, "The PDF Word produced is empty.");

            // A copy of Word left running would hold the office's file open and make the next
            // attempt fail for a reason nobody can see, so the session must take its own away.
            // Word acknowledges Quit before its process has finished unwinding, so the check
            // waits for it rather than asking the instant the call returns.
            Assert.Empty(WordProcessesLeftBehind(before, TimeSpan.FromSeconds(20)));
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // A temporary folder left behind is the operating system's to clean up.
            }
        }
    }

    [NoWordFact]
    public void A_machine_without_word_is_told_so_rather_than_shown_a_failure()
    {
        var outcome = WordSession.ExportPdf(
            Path.Combine(Path.GetTempPath(), "missing.docx"),
            Path.Combine(Path.GetTempPath(), "missing.pdf"));

        // Not «Failed»: nothing went wrong, Word is simply not here, and the screens turn that
        // into «Word غير متوفر» beside the internal editor rather than into an error.
        Assert.Equal(WordOutcome.Unavailable, outcome);
    }

    /// <summary>
    /// The copies of Word that appeared during the test and are still running once
    /// <paramref name="grace"/> has passed.
    /// </summary>
    /// <param name="before">The copies that were already running.</param>
    /// <param name="grace">How long Word is given to finish exiting.</param>
    private static List<int> WordProcessesLeftBehind(HashSet<int> before, TimeSpan grace)
    {
        var deadline = DateTime.UtcNow + grace;
        List<int> extra;
        do
        {
            extra = [.. WordProcessIds().Except(before)];
            if (extra.Count == 0)
            {
                return extra;
            }

            Thread.Sleep(250);
        }
        while (DateTime.UtcNow < deadline);

        return extra;
    }

    private static HashSet<int> WordProcessIds()
    {
        try
        {
            return [.. Process.GetProcessesByName("WINWORD").Select(p => p.Id)];
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }
}
