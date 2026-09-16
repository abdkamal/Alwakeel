using static Wakeel.Design.Bidi.Bidi;

namespace Wakeel.UI.Tests;

/// <summary>
/// Bidi.Wrap against the exact mixed Arabic/Latin lines catalogued in docs/design/bidi-test.html
/// (AGREEMENT item 55 / DESIGN-GUIDE.md "النص المختلط عربي/إنجليزي"). Each source line below is the
/// "بلا معالجة" (unprocessed) column from that page; every one of the page's 9 "case" entries gets
/// an exact full-string assertion matching its "مع العزل" column: a whole Latin/numeric run
/// (including internal single spaces, e.g. "184 GB", "Canon DR-C240", "Microsoft Word 365") comes
/// back as ONE LRI…PDI isolate, never as several adjacent isolates (which the UBA would then
/// reorder relative to each other under the RTL paragraph direction).
///
/// One deliberate, documented deviation from the literal HTML: in the W04 case the reference page's
/// hand-typed "مع العزل" column leaves the lone digit "1" in «الجهاز 1» outside a &lt;bdi&gt;, while
/// it isolates the equally-solitary letter "D" in the W12 disk-space case a few cases above it. That
/// is an inconsistency in the hand-authored reference (nothing in AGREEMENT item 55 / the rules list
/// at the bottom of bidi-test.html distinguishes "1" from "D"), so this suite asserts the consistent
/// behaviour — every anchored alphanumeric run isolated, "1" included — rather than reproducing that
/// one-off omission.
/// </summary>
public class BidiWrapTests
{
    private static string Isolated(string token) => Lri + token + Pdi;

    [Fact]
    public void Wrap_IsolatesFileNameVersionAndAcronym_W02FirstRun()
    {
        var result = Wrap("أحضر ملف الإعداد PLN-PC-01.wakeel-setup من مدير النظام (الإصدار v0.21) عبر USB.");

        var expected = "أحضر ملف الإعداد " + Isolated("PLN-PC-01.wakeel-setup")
            + " من مدير النظام (الإصدار " + Isolated("v0.21") + ") عبر " + Isolated("USB") + ".";
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Wrap_IsolatesAcronymAfterConjunction_W12HealthCenter()
    {
        var result = Wrap("نماذج البحث وOCR");

        Assert.Equal("نماذج البحث و" + Isolated("OCR"), result);
    }

    [Fact]
    public void Wrap_IsolatesDeviceNameAndTime_W12Scanner()
    {
        var result = Wrap("Canon DR-C240 غير متصل منذ 09:12");

        var expected = Rlm + Isolated("Canon DR-C240") + " غير متصل منذ " + Isolated("09:12");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Wrap_IsolatesSizesWithUnits_W12Disk()
    {
        var result = Wrap("184 GB متاحة من 512 GB على القرص D");

        var expected = Rlm + Isolated("184 GB") + " متاحة من " + Isolated("512 GB") + " على القرص " + Isolated("D");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Wrap_IsolatesSizeListWithMiddleDots_W12DiskList()
    {
        var result = Wrap("الوثائق 246 GB · قاعدة البيانات 2.4 GB · المؤقتات 6.1 GB");

        var expected = "الوثائق " + Isolated("246 GB") + " · قاعدة البيانات " + Isolated("2.4 GB")
            + " · المؤقتات " + Isolated("6.1 GB");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Wrap_IsolatesReferenceNumberDateAndAmount_W27()
    {
        var result = Wrap("الرقم 20260912/12046 بتاريخ 12/09/2026 والمبلغ 1,250 ₪ — عبر WebView2 وOCR");

        var expected = "الرقم " + Isolated("20260912/12046") + " بتاريخ " + Isolated("12/09/2026")
            + " والمبلغ " + Isolated("1,250") + " ₪ — عبر " + Isolated("WebView2") + " و" + Isolated("OCR");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Wrap_IsolatesLoginNameAndDeviceId_W04()
    {
        var result = Wrap("الحساب a.khatib · الموظف أحمد الخطيب · الجهاز 1 — PLN-PC-01");

        // See the class doc comment: "1" is isolated here, consistently with "D" in the disk-space
        // case, even though bidi-test.html's hand-typed column happens to leave it unwrapped.
        var expected = "الحساب " + Isolated("a.khatib") + " · الموظف أحمد الخطيب · الجهاز "
            + Isolated("1") + " — " + Isolated("PLN-PC-01");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Wrap_IsolatesAppNameAndTime_W17WordEditor()
    {
        var result = Wrap("فُتح في Microsoft Word 365 (النافذة نشطة) — 10:24");

        var expected = "فُتح في " + Isolated("Microsoft Word 365") + " (النافذة نشطة) — " + Isolated("10:24");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Wrap_IsolatesWindowsPathAndTime_W12Backup()
    {
        var result = Wrap(@"1.8 GB إلى D:\Backups\AlWakeel · النسخة التالية الليلة 23:30");

        var expected = Rlm + Isolated("1.8 GB") + " إلى " + Isolated(@"D:\Backups\AlWakeel")
            + " · النسخة التالية الليلة " + Isolated("23:30");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Wrap_PrefixesRlm_WhenLineStartsWithLatinToken()
    {
        var result = Wrap("Canon DR-C240 غير متصل منذ 09:12");

        Assert.StartsWith(Rlm.ToString(), result);
    }

    [Fact]
    public void Wrap_DoesNotPrefixRlm_WhenLineStartsWithArabic()
    {
        var result = Wrap("نماذج البحث وOCR");

        Assert.DoesNotContain(Rlm, result);
    }

    [Fact]
    public void Wrap_LeavesPlainArabicUnchanged()
    {
        const string arabic = "لا توجد بيانات بعد";

        Assert.Equal(arabic, Wrap(arabic));
    }

    [Fact]
    public void Wrap_ReturnsEmpty_ForNullOrEmptyInput()
    {
        Assert.Equal(string.Empty, Wrap(null));
        Assert.Equal(string.Empty, Wrap(string.Empty));
    }

    [Fact]
    public void Wrap_LeavesSentenceFinalPunctuation_OutsideTheIsolatedToken()
    {
        var result = Wrap("الإصدار v0.21.");

        Assert.Contains(Isolated("v0.21"), result);
        Assert.EndsWith(".", result);
    }

    [Fact]
    public void Wrap_LeavesSentenceFinalPunctuation_OutsideAnAcronymToken()
    {
        var result = Wrap("أُرسل عبر USB.");

        Assert.Contains(Isolated("USB"), result);
        Assert.EndsWith(".", result);
    }
}
