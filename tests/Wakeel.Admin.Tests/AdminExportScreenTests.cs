using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Wakeel.Admin.UI;
using Wakeel.Admin.UI.Pages;
using Wakeel.Admin.UI.Services;
using Wakeel.Admin.UI.Services.Devices;
using Wakeel.Admin.UI.Services.Export;
using Wakeel.Admin.UI.Services.Keys;
using Wakeel.Admin.UI.Services.Structure;
using Wakeel.Admin.UI.Text;

namespace Wakeel.Admin.Tests;

/// <summary>
/// The five screens of admin-3 — A08 «تصدير ملف الإعداد», A09 «الصيانة», A10 «توزيع التحديثات»,
/// A11 «سجل العمليات» and A12 «حالات وحوارات المدير» — rendered against a real tool on a temporary
/// folder.
/// </summary>
public class AdminExportScreenTests : AdminTestContext
{
    private readonly BunitContext _bunit = new();

    public AdminExportScreenTests()
    {
        _bunit.JSInterop.Mode = JSRuntimeMode.Loose;
        _bunit.Services.AddSingleton<IAdminWindow>(new TestAdminWindow());
        RegisterInto(_bunit);
    }

    // ---- A08 «تصدير ملف الإعداد» ------------------------------------------------------------

    [Fact]
    public void A08_OpensOnTheFirstStepWithTheOfficeTheDeviceAndTheSummaryBesideThem()
    {
        World();

        var cut = _bunit.Render<A08SetupExport>();

        Assert.Contains(AdminAr.Export.StepDeviceHeading, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Export.SummaryHeading, cut.Markup, StringComparison.Ordinal);
        Assert.Contains("OF-001", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("منى العلي", cut.Markup, StringComparison.Ordinal);

        // All four steps are named from the start, and the first is the one being drawn.
        foreach (var step in AdminAr.Export.Steps)
        {
            Assert.Contains(step, cut.Markup, StringComparison.Ordinal);
        }

        Assert.Contains("OF-001-2-20260916.wakeel-setup", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A08_SendsAnOrganisationWithNoOfficeToTheStructureInsteadOfAskingItToChoose()
    {
        CreateAccount();

        var cut = _bunit.Render<A08SetupExport>();

        Assert.Contains(AdminAr.Export.NoOffices, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Export.OpenStructure, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A08_WillNotGoOnFromAnOfficeThatHasNoKeyAndSaysWhereToIssueIt()
    {
        CreateAccount();
        var office = OfficeWithoutKey();
        Register(office, 1, "خالد سعيد", 1, DeviceRoles.Manager);

        var cut = _bunit.Render<A08SetupExport>();

        Assert.Contains(AdminAr.Export.NoOfficeKey, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Export.OpenKeys, cut.Markup, StringComparison.Ordinal);

        var next = cut.FindAll("button").Single(button =>
            button.TextContent.Contains(AdminAr.Export.Next, StringComparison.Ordinal));
        Assert.True(next.HasAttribute("disabled"));
    }

    [Fact]
    public void A08_ListsWhatTravelsAlwaysAndWhatIsOptionalOnTheSecondStep()
    {
        World();

        var cut = _bunit.Render<A08SetupExport>();
        GoOn(cut);

        Assert.Contains(AdminAr.Export.ItemStructure, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Export.ItemOfficeKey, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Export.ItemCertificate, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Export.ItemLogo, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Export.ItemGuide, cut.Markup, StringComparison.Ordinal);

        // The organisation has no logo and no report template yet, so those two say so instead of
        // offering a switch that would put nothing in the file.
        Assert.Contains(AdminAr.Export.LogoMissing, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Export.ReportTemplateMissing, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Export.LetterTemplateBuiltIn, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A08_ShowsThePasswordOnceAndKeepsTheWayOnShutUntilItHasBeenKept()
    {
        World();

        var cut = _bunit.Render<A08SetupExport>();
        GoOn(cut);
        GoOn(cut);

        Assert.Contains(AdminAr.Export.StepPasswordHeading, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Export.PasswordWarning, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Export.PasswordKept, cut.Markup, StringComparison.Ordinal);

        var next = cut.FindAll("button").Single(button =>
            button.TextContent.Contains(AdminAr.Export.Next, StringComparison.Ordinal));
        Assert.True(next.HasAttribute("disabled"));

        // The password really is there, in the grouped shape a person copies off the screen.
        var shown = cut.Find(".a08-password-value").TextContent.Trim();
        Assert.Equal(19, shown.Length);
        Assert.Equal(3, shown.Count(character => character == '-'));
    }

    [Fact]
    public void A08_WritesTheFileOnTheLastStepAndSaysWhereItWent()
    {
        var world = World();

        var cut = _bunit.Render<A08SetupExport>();
        GoOn(cut);
        GoOn(cut);
        cut.Find(".a08-main input[type=checkbox]").Change(true);
        GoOn(cut);

        cut.FindAll("button")
            .Single(button => button.TextContent.Contains(AdminAr.Export.CreateFile, StringComparison.Ordinal))
            .Click();

        Assert.Contains(AdminAr.Export.SuccessTitle, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(".wakeel-setup", cut.Markup, StringComparison.Ordinal);
        Assert.Single(Exports.HistoryOf(world.DeviceId));
        Assert.False(DeviceKeys.SeedsStillHeld(world.DeviceId));
    }

    // ---- A09 «الصيانة» -----------------------------------------------------------------------

    [Fact]
    public void A09_OffersTheThreeWaysInAndSaysNothingHasBeenOpenedYet()
    {
        World();

        var cut = _bunit.Render<A09Maintenance>();

        Assert.Contains(AdminAr.Maintenance.ChooseFolder, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Maintenance.ChooseBackup, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Maintenance.ChooseMessage, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Maintenance.NothingOpenYet, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Maintenance.RecentEmpty, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A09_KeepsTheRecoveryQuestionShutUntilItIsAskedFor()
    {
        World();

        var cut = _bunit.Render<A09Maintenance>();

        Assert.Contains(AdminAr.Maintenance.RecoverHeading, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Maintenance.RecoverWarning, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(AdminAr.Maintenance.RecoverTitle, cut.Markup, StringComparison.Ordinal);

        cut.FindAll("button")
            .Single(button => button.TextContent.Contains(AdminAr.Maintenance.RecoverButton, StringComparison.Ordinal)
                              && !button.HasAttribute("disabled"))
            .Click();

        Assert.Contains(AdminAr.Maintenance.RecoverTitle, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Confirm.TypeToConfirm, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A09_SaysWhyTheThreeButtonsAreOffOnAHostWithNoFileWindow()
    {
        World();
        FileDialog.Answers = false;

        var cut = _bunit.Render<A09Maintenance>();

        // The chooser is there but answers nothing, so the buttons stay live and the screen simply
        // draws no result; a host with no chooser at all says so in words instead.
        Assert.Contains(AdminAr.Maintenance.NothingOpenYet, cut.Markup, StringComparison.Ordinal);
    }

    // ---- A10 «توزيع التحديثات» ---------------------------------------------------------------

    [Fact]
    public void A10_ListsWhatIsWaitingAndTheOfficesItReaches()
    {
        World();

        var cut = _bunit.Render<A10Distribution>();

        Assert.Contains(AdminAr.Distribution.WaitingHeading, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Distribution.TargetsHeading, cut.Markup, StringComparison.Ordinal);
        Assert.Contains("OF-001", cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Distribution.ReadyChip, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Distribution.NeverExported, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(AdminAr.Distribution.NothingWaiting, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A10_MakesTheFilesOnlyAfterTheTypedConfirmationAndShowsAPasswordForEachOfThem()
    {
        World();

        var cut = _bunit.Render<A10Distribution>();
        cut.FindAll("button")
            .Single(button => button.TextContent.Contains(AdminAr.Distribution.Distribute, StringComparison.Ordinal)
                              && !button.HasAttribute("disabled"))
            .Click();

        Assert.Contains(AdminAr.Distribution.DistributeTitle, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Confirm.TypeToConfirm, cut.Markup, StringComparison.Ordinal);

        // The word is written by hand, and only then does the main button do anything.
        cut.Find(".a-confirm-typed input").Input(AdminAr.Confirm.ConfirmWord);
        cut.FindAll(".w-dialog-actions button")
            .First(button => button.TextContent.Contains(AdminAr.Distribution.Distribute, StringComparison.Ordinal))
            .Click();

        Assert.Contains(AdminAr.Distribution.ResultHeading, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Distribution.ColumnPassword, cut.Markup, StringComparison.Ordinal);
        Assert.Single(cut.FindAll(".a10-password"));
        Assert.Contains(AdminAr.Distribution.NothingWaiting, cut.Markup, StringComparison.Ordinal);
    }

    // ---- A11 «سجل العمليات» -------------------------------------------------------------------

    [Fact]
    public void A11_ShowsTheLogNewestFirstWithTheFourColumnsAndACount()
    {
        World();

        var cut = _bunit.Render<A11AuditLog>();

        Assert.Contains(AdminAr.Audit.ColumnWhen, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Audit.ColumnWho, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Audit.ColumnWhat, cut.Markup, StringComparison.Ordinal);
        Assert.Contains("سامي الحاج", cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Audit.ActionName("device_added"), cut.Markup, StringComparison.Ordinal);

        // Not one technical token reaches the screen (AGREEMENT item 15).
        Assert.DoesNotContain("device_added", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A11_NarrowsTheLogToWhatWasTypedAndSaysSoWhenNothingMatches()
    {
        World();

        var cut = _bunit.Render<A11AuditLog>();
        cut.Find(".w-search input").Input("لا شيء من هذا القبيل");

        Assert.Contains(AdminAr.Audit.NoneFound, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(AdminAr.Audit.Empty, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A11_WritesTheTableWhereTheChooserSaid()
    {
        World();

        var cut = _bunit.Render<A11AuditLog>();
        cut.FindAll("button")
            .Single(button => button.TextContent.Contains(AdminAr.Audit.ExportCsv, StringComparison.Ordinal)
                              && !button.HasAttribute("disabled"))
            .Click();

        Assert.NotNull(FileDialog.LastChosenPath);
        Assert.True(File.Exists(FileDialog.LastChosenPath));
        Assert.EndsWith(".csv", FileDialog.LastSuggestedName, StringComparison.Ordinal);

        var csv = File.ReadAllText(FileDialog.LastChosenPath!);
        Assert.Contains(AdminAr.Audit.ColumnWho, csv, StringComparison.Ordinal);
        Assert.DoesNotContain("sealed", csv, StringComparison.OrdinalIgnoreCase);
    }

    // ---- A12 «حالات وحوارات المدير» -----------------------------------------------------------

    [Fact]
    public void A12_DrawsTheFiveDialogsInThePageWithTheWordingTheirOwnScreensUse()
    {
        CreateAccount();

        var cut = _bunit.Render<A12States>();

        Assert.Equal(5, cut.FindAll(".w-dialog--inline").Count);
        Assert.Contains(AdminAr.Keys.RevokeTitle, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Organisation.Numbering.ChangeTitle, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.States.FileErrorTitle, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Export.SuccessTitle, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Organisation.Logo.TooLarge, cut.Markup, StringComparison.Ordinal);

        // The two dangerous ones ask for the word to be written, exactly as they do on their screens.
        Assert.Equal(2, cut.FindAll(".a-confirm-typed").Count);
        Assert.Equal(2, cut.FindAll(".w-dialog--confirm-locked").Count);
    }

    [Fact]
    public void A12_HasNoTabOfItsOwnAndIsStillARouteTheToolKnows()
    {
        Assert.Null(AdminRoutes.ForTab(AdminAr.States.Title));
        Assert.Equal(AdminRoutes.SetupExport, AdminRoutes.ForTab(AdminAr.Tabs.SetupExport));
        Assert.Equal(AdminRoutes.Maintenance, AdminRoutes.ForTab(AdminAr.Tabs.Maintenance));
        Assert.Equal(AdminRoutes.AuditLog, AdminRoutes.ForTab(AdminAr.Tabs.AuditLog));
    }

    /// <summary>Presses «التالي» once.</summary>
    private static void GoOn(IRenderedComponent<A08SetupExport> cut) =>
        cut.FindAll("button")
            .Single(button => button.TextContent.Contains(AdminAr.Export.Next, StringComparison.Ordinal))
            .Click();

    private sealed record Built(string OfficeId, string DeviceId);

    private Built World()
    {
        CreateAccount(orgName: "هيئة تنمية المناطق الريفية");
        var office = OfficeWithoutKey();
        Assert.Equal(KeyRefusal.None, DeviceKeys.IssueOrRotateOfficeKey(office, out _));
        var device = Register(office, 2, "منى العلي", 4, DeviceRoles.Secretary);
        return new Built(office, device);
    }

    private string OfficeWithoutKey()
    {
        var root = Structure.EnsureRoot();
        Assert.NotNull(root);
        Structure.Add(root.Id, "دائرة المالية", null, null, out var department);
        Structure.Add(department!, "قسم الحسابات", null, null, out var section);
        Structure.Add(section!, "وحدة الرواتب", null, null, out var unit);
        Assert.Equal(StructureRefusal.None, Structure.SetOffice(unit!, "OF-001"));
        return DeviceRegistry.ListOffices().Single(office => office.OfficeCode == "OF-001").Id;
    }

    private string Register(string officeId, int deviceNo, string employee, int employeeNo, string role)
    {
        Assert.Equal(
            DeviceRefusal.None,
            DeviceRegistry.AddDevice(officeId, deviceNo, role, employee, employeeNo, SyncScopes.Full, out var device));
        Assert.NotNull(device);
        return device;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bunit.Dispose();
        }

        base.Dispose(disposing);
    }
}
