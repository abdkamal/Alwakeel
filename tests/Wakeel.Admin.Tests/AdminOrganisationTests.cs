using System.IO.Compression;
using System.Text;
using Wakeel.Admin.UI.Services;
using Wakeel.Admin.UI.Services.Organisation;
using Wakeel.Admin.UI.Text;

namespace Wakeel.Admin.Tests;

/// <summary>
/// A04's rules: what the organisation's identity accepts, what a logo file has to be, and what the
/// letter template says about itself (AGREEMENT items 5, 45, 52, 53 and 57).
/// </summary>
public class AdminOrganisationTests : AdminTestContext
{
    [Fact]
    public void Identity_StartsFromWhatTheFirstRunWroteAndSavesWhatA04Changes()
    {
        CreateAccount();

        var before = Org.Read();
        Assert.NotNull(before);
        Assert.Equal("هيئة تنمية المناطق الريفية", before.Name);
        Assert.Equal(1, before.CycleStartDay);
        Assert.Equal(AdminKeyService.DefaultNumberingFormat, before.NumberingFormat);

        Assert.Equal(AdminOrgRefusal.None, Org.Save("هيئة تنمية الريف", 15, "YYYYMMDD/DESSS"));

        var after = Org.Read();
        Assert.NotNull(after);
        Assert.Equal("هيئة تنمية الريف", after.Name);
        Assert.Equal(15, after.CycleStartDay);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    [InlineData(31)]
    public void Identity_RefusesACycleDayOutsideTheFirstTwentyEight(int day)
    {
        CreateAccount();

        Assert.Equal(AdminOrgRefusal.CycleDayOutOfRange, Org.Save("هيئة", day, AdminKeyService.DefaultNumberingFormat));
        Assert.Equal(1, Org.Read()!.CycleStartDay);
    }

    [Fact]
    public void TheCycleExample_EndsTheCycleOnTheDayBeforeItStarts_IncludingTheDefaultDay()
    {
        // Day 1 is its own case: a cycle that opens on the first of a month closes on the LAST day
        // of that same month, not on a day of the month after it. It used to be folded onto 28 of
        // the next month, which contradicted the rule printed directly above it and stretched the
        // example — the period expenses, custody and reports are counted over — to nearly two
        // months. This is the value an administrator sees the first time A04 opens.
        // The sentence isolates each digit run for the screen (item 55); the facts read the words.
        static string Plain(string text) => new(text.Where(c => c is not ('⁦' or '⁧' or '⁨' or '⁩' or '‏')).ToArray());

        var first = Plain(AdminAr.Organisation.Cycle.Example(1));
        Assert.Contains("في 1/3", first, StringComparison.Ordinal);
        Assert.Contains("31/3", first, StringComparison.Ordinal);
        Assert.DoesNotContain("/4", first, StringComparison.Ordinal);

        var second = Plain(AdminAr.Organisation.Cycle.Example(2));
        Assert.Contains("في 2/3", second, StringComparison.Ordinal);
        Assert.Contains("1/4", second, StringComparison.Ordinal);

        var last = Plain(AdminAr.Organisation.Cycle.Example(28));
        Assert.Contains("في 28/3", last, StringComparison.Ordinal);
        Assert.Contains("27/4", last, StringComparison.Ordinal);
    }

    [Fact]
    public void Identity_RefusesAnEmptyNameAndAFormatThatSaysNothingAboutTheNumber()
    {
        CreateAccount();

        Assert.Equal(AdminOrgRefusal.NameRequired, Org.Save("   ", 1, AdminKeyService.DefaultNumberingFormat));
        Assert.Equal(AdminOrgRefusal.NumberingFormatInvalid, Org.Save("هيئة", 1, "بلا أرقام"));
    }

    [Fact]
    public void Identity_EveryChangeQueuesOneThingWaitingToBeDistributed()
    {
        CreateAccount();

        Org.Save("هيئة تنمية الريف", 7, AdminKeyService.DefaultNumberingFormat);
        Org.Save("هيئة تنمية الريف الأعلى", 7, AdminKeyService.DefaultNumberingFormat);

        // The newest wording wins rather than piling a second row on the first: what waits to go out
        // is the organisation as it now stands, not the history of how it got there.
        Assert.Equal(1, (int)Db.Scalar("SELECT COUNT(*) FROM pending_changes WHERE distributed_at IS NULL;"));
    }

    // The logo.

    [Fact]
    public void Logo_RefusesAFileThatIsNotAPictureAtAll()
    {
        var text = Encoding.UTF8.GetBytes("هذا ملف نصّي، لا صورة.");

        Assert.Equal(
            AdminLogoRefusal.WrongKind,
            AdminLogoReader.Accept(text, new NoAdminImageSquareCrop(), 0.5, out var image));
        Assert.Null(image);
    }

    [Fact]
    public void Logo_RefusesAPictureBiggerThanTwoMegabytes()
    {
        var huge = new byte[AdminLogoReader.MaxBytes + 1];
        Png(64, 64).CopyTo(huge.AsSpan());

        Assert.Equal(
            AdminLogoRefusal.TooLarge,
            AdminLogoReader.Accept(huge, new NoAdminImageSquareCrop(), 0.5, out var image));
        Assert.Null(image);
    }

    [Fact]
    public void Logo_RefusesAPictureTooSmallToStandBesideTheOrganisationName()
    {
        Assert.Equal(
            AdminLogoRefusal.TooSmall,
            AdminLogoReader.Accept(Png(16, 16), new NoAdminImageSquareCrop(), 0.5, out var image));
        Assert.Null(image);
    }

    [Fact]
    public void Logo_AcceptsAPngAndMeasuresItEvenWhenThisComputerCannotCutItSquare()
    {
        Assert.Equal(
            AdminLogoRefusal.None,
            AdminLogoReader.Accept(Png(300, 200), new NoAdminImageSquareCrop(), 0.5, out var image));

        Assert.NotNull(image);
        Assert.Equal("image/png", image.Mime);
        Assert.Equal(300, image.Width);
        Assert.Equal(200, image.Height);
        Assert.False(image.WasCropped);
    }

    [Fact]
    public void Logo_IsStoredAndClearedThroughTheOrganisationRow()
    {
        CreateAccount();

        Assert.Equal(AdminLogoRefusal.None, AdminLogoReader.Accept(Png(300, 300), new NoAdminImageSquareCrop(), 0.5, out var image));
        Org.SaveLogo(image!);

        var stored = Org.Read()!.Logo;
        Assert.NotNull(stored);
        Assert.Equal(300, stored.Width);
        Assert.StartsWith("data:image/png;base64,", stored.ToDataUrl(), StringComparison.Ordinal);

        Org.ClearLogo();
        Assert.Null(Org.Read()!.Logo);
    }

    // The letter template (AGREEMENT item 57).

    [Fact]
    public void LetterTemplate_TheBuiltInOneCarriesTheNineMarksAndNothingUnknown()
    {
        var report = LetterTemplateInspector.Inspect(DefaultLetterTemplate.FileName, DefaultLetterTemplate.Bytes());

        Assert.True(report.IsReadable);
        Assert.Equal(9, report.Known.Count);
        Assert.Empty(report.Unknown);
        Assert.Empty(report.Missing);

        foreach (var known in LetterTemplateInspector.KnownPlaceholders)
        {
            Assert.Contains(report.Known, mark => string.Equals(mark.Name, known, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void LetterTemplate_TheLetterheadIsReadOnceEvenThoughWordWritesItTwice()
    {
        // Word stores a drawing twice — once for itself and once as a fallback for an older Word —
        // and the owner's form puts its letterhead in three of them. Reading both copies showed every
        // letterhead line twice in the preview and counted its marks twice on the screen.
        var report = LetterTemplateInspector.Inspect(DefaultLetterTemplate.FileName, DefaultLetterTemplate.Bytes());

        Assert.All(report.Placeholders, mark => Assert.Equal(1, mark.Count));
        Assert.Equal(
            report.Paragraphs.Select(paragraph => paragraph.Text).Distinct(StringComparer.Ordinal).Count(),
            report.Paragraphs.Count);
    }

    [Fact]
    public void LetterTemplate_AMisspeltMarkIsReportedAsUnknownAndTheRealOneAsMissing()
    {
        // «@رقم الصادرة» instead of «@رقم الصادر»: the kind of slip a person makes editing the Word
        // file by hand, and the whole reason the screen lists what it found against what it knows.
        var docx = Docx(
            "@اسم مكتب المرسل",
            "@رقم الصادرة",
            "@التاريخ الهجري",
            "@التاريخ الميلادي",
            "@اسم مكتب المستقبل",
            "@اسم رئيس المكتب",
            "@اسم الموضوع",
            "@نص المراسلة",
            "@اسم المرسل");

        var report = LetterTemplateInspector.Inspect("قالب-فيه-خطأ.docx", docx);

        Assert.True(report.IsReadable);
        Assert.Contains(report.Unknown, mark => string.Equals(mark.Name, "@رقم الصادرة", StringComparison.Ordinal));
        Assert.Contains(report.Missing, missing => string.Equals(missing, "@رقم الصادر", StringComparison.Ordinal));
        Assert.Equal(8, report.Known.Count);
    }

    [Fact]
    public void LetterTemplate_AFileThatIsNotAWordDocumentIsSaidToBeUnreadable()
    {
        var report = LetterTemplateInspector.Inspect("ليس-قالبًا.docx", Encoding.UTF8.GetBytes("ليس ملف وورد"));

        Assert.False(report.IsReadable);
        Assert.Empty(report.Placeholders);
    }

    [Fact]
    public void ReportTemplate_IsJudgedByWhatIsInsideItNotByWhatItIsCalled()
    {
        // The monthly report template is stored and shipped without ever being opened again, so this
        // is the only thing standing between a renamed program and the organisation's template.
        Assert.True(LetterTemplateInspector.IsWordDocument(Docx("تقرير الشهر")));
        Assert.False(LetterTemplateInspector.IsWordDocument(Encoding.UTF8.GetBytes("ليس ملف وورد")));
        Assert.False(LetterTemplateInspector.IsWordDocument(ReadOnlySpan<byte>.Empty));
        Assert.False(LetterTemplateInspector.IsWordDocument(ZipWithoutADocument()));
    }

    [Fact]
    public void LetterTemplate_IsNotOpenedAtAllPastTheLimitTheInspectorKeeps()
    {
        var tooBig = new byte[LetterTemplateInspector.MaxBytes + 1];

        Assert.False(LetterTemplateInspector.Inspect("ضخم.docx", tooBig).IsReadable);
        Assert.False(LetterTemplateInspector.IsWordDocument(tooBig));
    }

    [Fact]
    public void LetterTemplate_WithoutAnUploadTheToolWritesOnItsOwnTemplate()
    {
        CreateAccount();

        var (fileName, bytes) = Org.ReadLetterTemplate();
        Assert.Equal(DefaultLetterTemplate.FileName, fileName);
        Assert.True(Org.Read()!.UsesBuiltInLetterTemplate);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void LetterTemplate_AnUploadedOneReplacesItAndRemovingItPutsTheBuiltInOneBack()
    {
        CreateAccount();

        var mine = Docx("@رقم الصادر", "@نص المراسلة");
        Org.SaveLetterTemplate("قالبي.docx", mine);

        Assert.False(Org.Read()!.UsesBuiltInLetterTemplate);
        Assert.Equal("قالبي.docx", Org.ReadLetterTemplate().FileName);

        Org.ClearLetterTemplate();
        Assert.True(Org.Read()!.UsesBuiltInLetterTemplate);
        Assert.Equal(DefaultLetterTemplate.FileName, Org.ReadLetterTemplate().FileName);
    }

    /// <summary>A Word file whose body is one paragraph per line given.</summary>
    private static byte[] Docx(params string[] lines)
    {
        var body = new StringBuilder();
        foreach (var line in lines)
        {
            body.Append("<w:p><w:r><w:t xml:space=\"preserve\">")
                .Append(System.Security.SecurityElement.Escape(line))
                .Append("</w:t></w:r></w:p>");
        }

        var document =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">"
            + "<w:body>" + body + "</w:body></w:document>";

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("word/document.xml");
            using var stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes(document));
        }

        return buffer.ToArray();
    }

    /// <summary>A well-formed package that simply is not a Word document: no word/document.xml.</summary>
    private static byte[] ZipWithoutADocument()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("readme.txt");
            using var stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes("لا شيء هنا"));
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// The smallest PNG that measures as the size asked for: a signature and one IHDR chunk, which
    /// is all the reader looks at.
    /// </summary>
    private static byte[] Png(int width, int height)
    {
        var bytes = new byte[33];
        ReadOnlySpan<byte> signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes);

        // The IHDR chunk: its length, its name, then width and height as big-endian four-byte
        // numbers, which is exactly where a PNG states its size.
        bytes[8] = 0;
        bytes[9] = 0;
        bytes[10] = 0;
        bytes[11] = 13;
        bytes[12] = (byte)'I';
        bytes[13] = (byte)'H';
        bytes[14] = (byte)'D';
        bytes[15] = (byte)'R';
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }
}
