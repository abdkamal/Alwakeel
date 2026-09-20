using Wakeel.Admin.UI.Services.Devices;
using Wakeel.Admin.UI.Services.Export;
using Wakeel.Admin.UI.Services.Keys;
using Wakeel.Admin.UI.Services.Organisation;
using Wakeel.Admin.UI.Services.Structure;
using Wakeel.Crypto;

namespace Wakeel.Admin.Tests;

/// <summary>
/// A08 «تصدير ملف الإعداد»: the file the tool makes has to be exactly the file the other machine
/// opens, so every one of these reads the result back through <see cref="SetupPackageReader"/> — the
/// very reader the first run uses — instead of trusting what the writer said it wrote.
/// </summary>
public class AdminExportTests : AdminTestContext
{
    [Fact]
    public void Export_ProducesAFileTheFirstRunReaderOpensWithEveryCheckHeld()
    {
        var world = World();

        var refusal = Exports.Export(world.DeviceId, SetupExportOptions.All, Password, out var result);

        Assert.Equal(ExportRefusal.None, refusal);
        Assert.NotNull(result);
        Assert.True(File.Exists(result.FilePath));

        using var package = Open(result.FilePath);
        Assert.True(package.IsAcceptable);
        Assert.All(package.Checks.Checks, check => Assert.NotEqual(SetupCheckStatus.Failed, check.Status));

        var content = package.Content;
        Assert.Equal("هيئة تنمية المناطق الريفية", content.Org.Name);
        Assert.Equal(world.OfficeUnitId, content.Office.UnitId);
        Assert.Equal("OF-001", content.Office.OfficeCode);
        Assert.Equal(2, content.Device.DeviceNo);
        Assert.Equal(DeviceRoles.Secretary, content.Device.Role);
        Assert.Equal("منى العلي", content.Employee.Name);
        Assert.Equal(1, content.ExportSeq);
    }

    [Fact]
    public void Export_CarriesTheLetterTemplateAndTheGuideAndTheLogoWhenTheyAreAskedFor()
    {
        var world = World();
        Org.SaveLogo(new AdminLogoImage(Png(), "image/png", 64, 64, WasCropped: true));

        Assert.Equal(
            ExportRefusal.None,
            Exports.Export(world.DeviceId, SetupExportOptions.All, Password, out var result));

        using var package = Open(result!.FilePath);
        Assert.True(package.Content.Includes.LetterTemplate);
        Assert.True(package.Content.Includes.Guide);
        Assert.True(package.Content.Includes.Logo);
        Assert.False(package.Content.Includes.ReportTemplate);

        // The letter template that travels is the one the tool would write a letter with, and the
        // guide is a whole PDF rather than a note about one.
        var letter = package.Read(SetupEntryNames.LetterTemplate);
        Assert.Equal("PK", System.Text.Encoding.ASCII.GetString(letter, 0, 2));

        var guide = package.Read(SetupEntryNames.Guide);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(guide, 0, 4));
        Assert.EndsWith("%%EOF\n", System.Text.Encoding.Latin1.GetString(guide), StringComparison.Ordinal);
    }

    [Fact]
    public void Export_WithoutTheAttachmentsStillOpensWithEveryCheckHeld()
    {
        var world = World();

        Assert.Equal(
            ExportRefusal.None,
            Exports.Export(
                world.DeviceId,
                new SetupExportOptions(Logo: false, Guide: false, ReportTemplate: false, LetterTemplate: false),
                Password,
                out var result));

        using var package = Open(result!.FilePath);
        Assert.True(package.IsAcceptable);
        Assert.Equal(new SetupIncludes(false, false, false, false), package.Content.Includes);
        Assert.False(package.Has(SetupEntryNames.LetterTemplate));
        Assert.False(package.Has(SetupEntryNames.Guide));
    }

    [Fact]
    public void Export_TakesTheDeviceSeedOutOfTheToolWithTheFirstFile()
    {
        var world = World();
        Assert.True(DeviceKeys.SeedsStillHeld(world.DeviceId));

        Assert.Equal(ExportRefusal.None, Exports.Export(world.DeviceId, SetupExportOptions.All, Password, out _));

        Assert.False(DeviceKeys.SeedsStillHeld(world.DeviceId));
        Assert.NotNull(DeviceRegistry.FindDevice(world.DeviceId)!.ExportedAt);
    }

    [Fact]
    public void Export_GivesTheDeviceFreshKeysWhenItsSeedHasAlreadyLeftInAnEarlierFile()
    {
        var world = World();
        Assert.Equal(ExportRefusal.None, Exports.Export(world.DeviceId, SetupExportOptions.All, Password, out var first));
        var firstCertificate = DeviceKeys.ReadCertificate(world.DeviceId);

        Assert.Equal(ExportRefusal.None, Exports.Export(world.DeviceId, SetupExportOptions.All, Password, out var second));

        Assert.NotNull(second);
        Assert.True(second.KeysRenewed);
        Assert.False(first!.KeysRenewed);
        Assert.Equal(2, second.Sequence);

        // Both files open, and the second names keys the first knew nothing about — which is what
        // makes the first one useless from here on.
        using var package = Open(second.FilePath);
        Assert.True(package.IsAcceptable);
        Assert.NotEqual(firstCertificate!.Body.Ed25519Pub, package.Content.Device.Certificate.Body.Ed25519Pub);
    }

    [Fact]
    public void Export_IsWrittenIntoTheSetupExportsTableWithWhatItCarried()
    {
        var world = World();
        Assert.Equal(ExportRefusal.None, Exports.Export(world.DeviceId, SetupExportOptions.All, Password, out var result));

        var history = Exports.HistoryOf(world.DeviceId);
        var row = Assert.Single(history);
        Assert.Equal(1, row.Sequence);
        Assert.Equal(result!.FileName, row.FileName);
        Assert.Equal(Time.GetUtcNow(), row.At);

        var logged = Audit.Recent().First(entry => entry.Action == "setup_exported");
        Assert.Contains(result.FileName, logged.SummaryAr, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, logged.Details ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_NamesTheFileAfterTheOfficeTheDeviceAndTheDay()
    {
        var world = World();
        var plan = Exports.Plan(world.DeviceId);

        Assert.NotNull(plan);
        Assert.Equal("OF-001-2-20260916.wakeel-setup", Exports.FileNameFor(plan));
    }

    [Fact]
    public void Export_RefusesADeviceThatHasBeenShutOutAndOneWithNoOfficeKey()
    {
        var world = World();
        Assert.Equal(KeyRefusal.None, DeviceKeys.Revoke(world.DeviceId, out _));

        Assert.Equal(
            ExportRefusal.DeviceRevoked,
            Exports.Export(world.DeviceId, SetupExportOptions.All, Password, out _));

        var other = SecondOffice();
        var device = Register(other, 1, "خالد سعيد", 1, DeviceRoles.Manager);
        Assert.Equal(
            ExportRefusal.NoOfficeKey,
            Exports.Export(device, SetupExportOptions.All, Password, out _));
    }

    [Fact]
    public void Export_RefusesAPasswordThisProductDidNotIssue()
    {
        var world = World();

        Assert.Equal(
            ExportRefusal.PasswordInvalid,
            Exports.Export(world.DeviceId, SetupExportOptions.All, "short", out _));
        Assert.True(DeviceKeys.SeedsStillHeld(world.DeviceId));
    }

    [Fact]
    public void Export_CarriesTheSignedRevocationListOnceADeviceHasBeenShutOut()
    {
        var world = World();
        var second = Register(world.OfficeId, 3, "خالد سعيد", 3, DeviceRoles.Custodian);
        Assert.Equal(KeyRefusal.None, DeviceKeys.Revoke(second, out _));

        Assert.Equal(ExportRefusal.None, Exports.Export(world.DeviceId, SetupExportOptions.All, Password, out var result));

        using var package = Open(result!.FilePath);
        Assert.NotNull(package.Content.Revocation);
        Assert.Contains(package.Content.Revocation.Body.Entries, entry => entry.DeviceId == second);
        Assert.True(package.IsAcceptable);
    }

    [Fact]
    public void Guide_IsAWholePageWithTheOrganisationOnIt()
    {
        var bytes = Wakeel.Admin.UI.Services.Export.AdminGuidePdf.Build(
            "هيئة تنمية المناطق الريفية", "وحدة الرواتب", Time.GetUtcNow());
        var text = System.Text.Encoding.Latin1.GetString(bytes);

        Assert.StartsWith("%PDF-1.7", text, StringComparison.Ordinal);
        Assert.Contains("/Type /Page", text, StringComparison.Ordinal);
        Assert.Contains("/FontFile2", text, StringComparison.Ordinal);
        Assert.Contains("startxref", text, StringComparison.Ordinal);

        // The face is really in the file, and the page really draws with it.
        Assert.True(bytes.Length > 100_000);
        Assert.Contains("Tj", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Guide_ShapesArabicAndLaysItOutRightToLeft()
    {
        // «الوكيل» comes back with its last letter first, which is how a page draws it, and every
        // letter in the shape its neighbours give it: a final lam at the front of the visual line, a
        // lone alef at the end of it, and not one plain letter left anywhere.
        var visual = ArabicShaping.ToVisualOrder("الوكيل");

        Assert.Equal(6, visual.Length);
        Assert.Equal('ﻞ', visual[0]);
        Assert.Equal('ﺍ', visual[^1]);
        Assert.DoesNotContain("ل", visual, StringComparison.Ordinal);

        // And where a lam really does precede an alef, the pair is one glyph rather than two.
        Assert.Equal("ﻻ", ArabicShaping.ToVisualOrder("لا"));

        // A run of figures inside an Arabic line keeps its own order.
        Assert.Contains("2026", ArabicShaping.ToVisualOrder("حُرّرت في 2026"), StringComparison.Ordinal);
    }

    [Fact]
    public void Guide_FindsEveryShapedLetterInTheFaceTheToolShips()
    {
        var font = PdfFontFile.Shipped();
        Assert.NotNull(font);

        // Every character the page actually draws, mark by mark: a letter the face has no glyph for
        // is silently left out, so the page would come out short and nobody would know why.
        foreach (var line in AdminGuidePdf.Text("هيئة تنمية المناطق الريفية", "وحدة الرواتب", Time.GetUtcNow()))
        {
            foreach (var character in ArabicShaping.ToVisualOrder(line))
            {
                Assert.True(
                    character == ' ' || font.GlyphOf(character) != 0,
                    $"no glyph for U+{(int)character:X4} in «{line}»");
            }
        }
    }

    /// <summary>A password of the sixteen characters this product issues, fixed so a test can read it.</summary>
    private const string Password = "K7M2-P9QR-3TVW-X4YZ";

    private SetupPackage Open(string path) =>
        SetupPackageReader.Open(
            path,
            Password,
            SetupExpectations.FirstRun(Path.Combine(Paths.StagingFolder, "read")),
            Time);

    private sealed record Built(string OfficeId, string OfficeUnitId, string DeviceId);

    /// <summary>An organisation with one office, one office key and one registered computer in it.</summary>
    private Built World()
    {
        CreateAccount();
        var root = Structure.EnsureRoot();
        Assert.NotNull(root);
        Structure.Add(root.Id, "دائرة المالية", null, null, out var department);
        Structure.Add(department!, "قسم الحسابات", "رئيس القسم", "سامي الحاج", out var section);
        Structure.Add(section!, "وحدة الرواتب", null, null, out var unit);
        Assert.Equal(StructureRefusal.None, Structure.SetOffice(unit!, "OF-001"));

        var office = DeviceRegistry.ListOffices().Single(candidate => candidate.OfficeCode == "OF-001");
        Assert.Equal(KeyRefusal.None, DeviceKeys.IssueOrRotateOfficeKey(office.Id, out _));

        var device = Register(office.Id, 2, "منى العلي", 4, DeviceRoles.Secretary);
        return new Built(office.Id, office.UnitId, device);
    }

    private string SecondOffice()
    {
        var root = Structure.EnsureRoot();
        Assert.NotNull(root);
        Structure.Add(root.Id, "دائرة الهندسة", null, null, out var department);
        Structure.Add(department!, "قسم التصميم", null, null, out var section);
        Structure.Add(section!, "وحدة الرسم", null, null, out var unit);
        Assert.Equal(StructureRefusal.None, Structure.SetOffice(unit!, "OF-002"));
        return DeviceRegistry.ListOffices().Single(candidate => candidate.OfficeCode == "OF-002").Id;
    }

    private string Register(string officeId, int deviceNo, string employee, int employeeNo, string role)
    {
        Assert.Equal(
            DeviceRefusal.None,
            DeviceRegistry.AddDevice(officeId, deviceNo, role, employee, employeeNo, SyncScopes.Full, out var device));
        Assert.NotNull(device);
        return device;
    }

    /// <summary>The smallest picture that is really a PNG, for the logo.</summary>
    private static byte[] Png() =>
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAAIElEQVRoge3BAQ0AAADCoPdPbQ43"
            + "oAAAAAAAAAAAAAAAAAB4NxUAAAHy9y2TAAAAAElFTkSuQmCC");
}
