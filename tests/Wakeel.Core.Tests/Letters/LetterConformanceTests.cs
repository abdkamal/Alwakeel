using System.Text.RegularExpressions;
using Wakeel.Core.Services.Correspondence;
using Wakeel.Reports.Letters;
using Xunit.Abstractions;

namespace Wakeel.Core.Tests.Letters;

/// <summary>
/// The conformance test the B3 specification asks for: a letter generated from the approved
/// reference template is compared, word for word, with the letter the owner filled in by hand in
/// Word and handed over as <c>مثال-مراسلة-المالك.docx</c>.
/// </summary>
/// <remarks>
/// This is the one test that can tell whether الوكيل writes the same letter the office writes.
/// It compares the text of every part — body, text boxes, header — after collapsing runs of
/// whitespace, because where Word puts a line break inside a paragraph is Word's business and not
/// something a reader of the printed page can see.
/// </remarks>
public sealed partial class LetterConformanceTests(ITestOutputHelper output)
{
    /// <summary>The values the owner typed into his example, mark by mark.</summary>
    private static LetterData OwnerExample() => new()
    {
        Date = new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Unspecified),
        NumberAr = "20260916/12003",
        RecipientHeadNameAr = "أحمد جمعة",
        RecipientOfficeNameAr = "بلدية نابلس",
        SubjectAr = "نقل ممتلكات بين المكاتب",
        SenderNameAr = "حسن داوود",
        SenderOfficeNameAr = "مكتب المعدات",
        PageSize = LetterPageSize.A4,
        Body = LetterBody.Parse(
            """
            أود منكم الموافقة على نقل ممتلكات من مكتب مصلحة المياه إلى مكتب التوجيه والتي لا حاجة لها في مكتب مصلحة المياه وهي
            - حاسوب مكتبي كامل عدد 3
            - مكتب خشبي عدد 3
            """),
    };

    [Fact]
    public void Generated_letter_reads_the_same_as_the_owner_s_filled_example()
    {
        var composed = new LetterComposer().Compose(LetterWorld.ReferenceTemplate(), OwnerExample());

        var mine = Meaningful(LetterComposer.ReadLines(composed));
        var his = Meaningful(LetterComposer.ReadLines(File.ReadAllBytes(LetterWorld.OwnerExamplePath)));

        output.WriteLine("الوكيل:");
        foreach (var line in mine)
        {
            output.WriteLine("  " + line);
        }

        output.WriteLine("المالك:");
        foreach (var line in his)
        {
            output.WriteLine("  " + line);
        }

        Assert.Equal(his, mine);
    }

    [Fact]
    public void Generated_letter_carries_the_dates_the_owner_wrote_by_hand()
    {
        var composed = new LetterComposer().Compose(LetterWorld.ReferenceTemplate(), OwnerExample());
        var text = string.Join("\n", LetterComposer.ReadLines(composed));

        // The owner wrote these two himself in his example; they are the ground truth for the
        // Hijri conversion and for both sets of Arabic month names.
        Assert.Contains("19 صفر 1448", text, StringComparison.Ordinal);
        Assert.Contains("2 أغسطس 2026", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The lines a reader would see: no blanks, and every run of whitespace collapsed to one
    /// space so that a line broken inside a paragraph compares equal to the same line unbroken.
    /// </summary>
    private static List<string> Meaningful(IEnumerable<string> lines) =>
        [.. lines
            .Select(l => Whitespace().Replace(l, " ").Trim())
            .Where(l => l.Length > 0)];

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
