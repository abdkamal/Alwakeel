using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Wakeel.Admin.UI.Components;
using Wakeel.Admin.UI.Pages;
using Wakeel.Admin.UI.Services;
using Wakeel.Admin.UI.Services.Devices;
using Wakeel.Admin.UI.Services.Keys;
using Wakeel.Admin.UI.Services.Organisation;
using Wakeel.Admin.UI.Services.Structure;
using Wakeel.Admin.UI.Text;

namespace Wakeel.Admin.Tests;

/// <summary>
/// The four screens of admin-2 — A04 «الهيئة والهوية», A05 «الهيكلية», A06 «المكاتب والأجهزة» and
/// A07 «الحسابات والمفاتيح» — rendered against a real tool on a temporary folder.
/// </summary>
public class AdminAreaScreenTests : AdminTestContext
{
    private readonly BunitContext _bunit = new();

    public AdminAreaScreenTests()
    {
        // Loose mode lets the design system's interop (theme, focus trapping, file pickers) answer
        // with defaults rather than throwing, which is all these tests need from the browser.
        _bunit.JSInterop.Mode = JSRuntimeMode.Loose;
        _bunit.Services.AddSingleton<IAdminWindow>(new TestAdminWindow());
        RegisterInto(_bunit);
    }

    // ---- A04 «الهيئة والهوية» -------------------------------------------------------------

    [Fact]
    public void A04_ShowsTheIdentityTheLogoTheCycleAndTheNumberingFormat()
    {
        CreateAccount(orgName: "هيئة تنمية المناطق الريفية");

        var cut = _bunit.Render<A04Organisation>();

        Assert.Contains(AdminAr.Organisation.Identity.Heading, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Organisation.Logo.Heading, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Organisation.Cycle.Heading, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Organisation.Numbering.Heading, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Organisation.Cycle.Explain, cut.Markup, StringComparison.Ordinal);
        Assert.Contains("هيئة تنمية المناطق الريفية", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A04_OffersOnlyTheTwentyEightDaysACycleMayStartOn()
    {
        CreateAccount();

        var cut = _bunit.Render<A04Organisation>();
        var days = cut.FindAll("select")
            .Select(select => select.QuerySelectorAll("option").Length)
            .ToArray();

        Assert.Contains(AdminOrgService.MaxCycleDay, days);
    }

    [Fact]
    public void A04_KeepsTheDangerWarningBackUntilTheNumberingFormatIsActuallyChanged()
    {
        CreateAccount();

        var cut = _bunit.Render<A04Organisation>();

        // The dialog exists on the page from the start, closed; its warning is not on screen.
        Assert.DoesNotContain(AdminAr.Organisation.Numbering.ChangeWarning, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminKeyService.DefaultNumberingFormat, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A04_ListsTheNineKnownMarksOfTheBuiltInLetterTemplateAndFlagsNothingAsUnknown()
    {
        CreateAccount();

        var cut = _bunit.Render<A04Organisation>();

        Assert.Contains(AdminAr.Organisation.LetterTemplate.Heading, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Organisation.LetterTemplate.KnownHeading, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Organisation.LetterTemplate.AllKnown, cut.Markup, StringComparison.Ordinal);

        // One line per known mark, and the nine of AGREEMENT item 57 are all there.
        var marks = cut.FindAll(".a04-mark-list li");
        Assert.Equal(LetterTemplateInspector.KnownPlaceholders.Count, marks.Count);
        foreach (var placeholder in LetterTemplateInspector.KnownPlaceholders)
        {
            Assert.Contains(placeholder, cut.Markup, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A04_DrawsTheLetterOnASheetAndCanSwitchItFromA4ToA5()
    {
        CreateAccount();

        var cut = _bunit.Render<A04Organisation>();

        Assert.Contains(AdminAr.Organisation.LetterTemplate.PreviewHeading, cut.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(cut.FindAll(".a04-letter-sheet--a4"));
        Assert.NotEmpty(cut.FindAll(".a04-letter-line"));
        Assert.Empty(cut.FindAll(".a04-letter-sheet--a5"));
    }

    [Fact]
    public void A04_SaysWhyWhenThereIsNoOrganisationToShow()
    {
        var cut = _bunit.Render<A04Organisation>();

        Assert.Contains(AdminAr.Errors.DataUnreadable, cut.Markup, StringComparison.Ordinal);
    }

    // ---- A05 «الهيكلية» -------------------------------------------------------------------

    [Fact]
    public void A05_DrawsTheTreeBesideTheDetailPaneAndAsksForAChoiceFirst()
    {
        CreateAccount();

        var cut = _bunit.Render<A05Structure>();

        Assert.NotEmpty(cut.FindAll(".a05-tree"));
        Assert.NotEmpty(cut.FindAll(".a05-detail"));
        Assert.Contains(AdminAr.Structure.ChooseHeading, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Structure.SearchPlaceholder, cut.Markup, StringComparison.Ordinal);

        // The organisation itself is always the one node there is.
        Assert.Single(cut.FindAll(".a05-row"));
    }

    [Fact]
    public void A05_DrawsOneRowPerNodeOfTheFourLayers()
    {
        BuildStructure();

        var cut = _bunit.Render<A05Structure>();

        // Organisation, department, section, unit.
        Assert.Equal(4, cut.FindAll(".a05-row").Count);
        Assert.Contains("دائرة المالية", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("وحدة الرواتب", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A05_OpensTheDetailPaneOnTheNodeThatWasClicked()
    {
        BuildStructure();

        var cut = _bunit.Render<A05Structure>();
        cut.FindAll(".a05-name").First(row => row.TextContent.Contains("وحدة الرواتب", StringComparison.Ordinal)).Click();

        Assert.Contains(AdminAr.Structure.NameLabel, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Structure.HeadNameLabel, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Structure.OfficeHeading, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(AdminAr.Structure.ChooseHeading, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A05_MarksAnOfficeWithItsChipAndSaysWhenABranchIsSwitchedOff()
    {
        var unit = BuildStructure();
        Assert.Equal(StructureRefusal.None, Structure.SetOffice(unit, "OF-001"));
        Assert.Equal(StructureRefusal.None, Structure.Disable(unit));

        var cut = _bunit.Render<A05Structure>();

        // In the tree a switched-off node carries the chip and the greyed row; being an office is
        // drawn there as a small building rather than a word, so the chip for it belongs to the
        // detail pane, where there is room to say it.
        Assert.Contains(AdminAr.Structure.DisabledChip, cut.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(cut.FindAll(".a05-row--off"));

        cut.FindAll(".a05-name").First(row => row.TextContent.Contains("وحدة الرواتب", StringComparison.Ordinal)).Click();

        Assert.Contains(AdminAr.Structure.OfficeChip, cut.Markup, StringComparison.Ordinal);
        Assert.Contains("OF-001", cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Structure.Enable, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A05_SaysNothingWasFoundRatherThanDrawingAnEmptyTree()
    {
        BuildStructure();

        var cut = _bunit.Render<A05Structure>();
        cut.Find(".a05-tree input").Input("لا شيء بهذا الاسم أبدًا");

        Assert.Contains(AdminAr.Structure.SearchNothing, cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".a05-row"));
    }

    // ---- A06 «المكاتب والأجهزة» ------------------------------------------------------------

    [Fact]
    public void A06_SendsThePersonToTheStructureWhileThereIsNoOfficeAtAll()
    {
        CreateAccount();

        var cut = _bunit.Render<A06OfficesAndDevices>();

        Assert.Contains(AdminAr.Devices.OfficesEmpty, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Devices.OpenStructure, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A06_ListsTheOfficesAndOpensTheFirstOneRatherThanAskingForNothing()
    {
        Office();

        var cut = _bunit.Render<A06OfficesAndDevices>();

        Assert.Single(cut.FindAll(".a06-offices .w-tree-row"));
        // With one office there is nothing to choose between, so the screen opens on it and the
        // «اختر مكتبًا» invitation has no reason to be there.
        Assert.Single(cut.FindAll(".a06-offices .w-tree-row--selected"));
        Assert.DoesNotContain(AdminAr.Devices.ChooseOffice, cut.Markup, StringComparison.Ordinal);
        Assert.Contains("وحدة الرواتب", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A06_ShowsTheChosenOfficeItsCodeAndTheInvitationToAddTheFirstDevice()
    {
        Office();

        var cut = _bunit.Render<A06OfficesAndDevices>();
        cut.Find(".a06-offices .w-tree-row").Click();

        Assert.Contains(AdminAr.Devices.OfficeCodeLabel, cut.Markup, StringComparison.Ordinal);
        Assert.Contains("OF-001", cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Devices.DevicesEmpty, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Devices.Add, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A06_DrawsOneRowPerDeviceWithItsNumberRoleAndScope()
    {
        var office = Office();
        AddDevice(office, 1, DeviceRoles.Manager, "سامي الحاج", 1, SyncScopes.Full);
        AddDevice(office, 2, DeviceRoles.Custodian, "منى العلي", 2, SyncScopes.Custody);

        var cut = _bunit.Render<A06OfficesAndDevices>();
        cut.Find(".a06-offices .w-tree-row").Click();

        Assert.Equal(2, cut.FindAll("tbody tr").Count);
        Assert.Contains("سامي الحاج", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("منى العلي", cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Devices.RoleName(DeviceRoles.Custodian), cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Devices.ScopeName(SyncScopes.Custody), cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A06_SaysTheNineNumbersAreAllTakenInsteadOfOfferingAnotherDevice()
    {
        var office = Office();
        for (var i = 1; i <= 9; i++)
        {
            AddDevice(office, i, DeviceRoles.Secretary, $"موظف {i}", i, SyncScopes.Full);
        }

        var cut = _bunit.Render<A06OfficesAndDevices>();
        cut.Find(".a06-offices .w-tree-row").Click();

        Assert.Contains(AdminAr.Devices.NumbersFull, cut.Markup, StringComparison.Ordinal);
        Assert.Equal(9, cut.FindAll("tbody tr").Count);
    }

    // ---- A07 «الحسابات والمفاتيح» ----------------------------------------------------------

    [Fact]
    public void A07_SendsThePersonToTheDeviceRegisterWhileThereIsNoAccountAtAll()
    {
        CreateAccount();

        var cut = _bunit.Render<A07AccountsAndKeys>();

        Assert.Contains(AdminAr.Keys.AccountsEmpty, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Keys.OpenDevices, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A07_ListsTheAccountsWithTheirCertificatesAndTheOfficeKeyBesideThem()
    {
        var office = Office();
        AddDevice(office, 1, DeviceRoles.Manager, "سامي الحاج", 1, SyncScopes.Full);

        var cut = _bunit.Render<A07AccountsAndKeys>();

        Assert.Contains(AdminAr.Keys.AccountsHeading, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Keys.OfficeKeyHeading, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Keys.OfficeKeyNone, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Keys.OfficeKeyIssue, cut.Markup, StringComparison.Ordinal);
        Assert.Contains("سامي الحاج", cut.Markup, StringComparison.Ordinal);
        Assert.Single(cut.FindAll("tbody tr"));
    }

    [Fact]
    public void A07_OffersRotatingTheKeyOnceTheOfficeHasOneAndWarnsWhatThatCosts()
    {
        var office = Office();
        AddDevice(office, 1, DeviceRoles.Manager, "سامي الحاج", 1, SyncScopes.Full);
        Assert.Equal(KeyRefusal.None, DeviceKeys.IssueOrRotateOfficeKey(office, out _));

        var cut = _bunit.Render<A07AccountsAndKeys>();

        Assert.Contains(AdminAr.Keys.OfficeKeyRotate, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(AdminAr.Keys.OfficeKeyNone, cut.Markup, StringComparison.Ordinal);

        // The warning itself belongs to the dialog, which is closed until the person asks.
        Assert.DoesNotContain(AdminAr.Keys.OfficeKeyRotateWarning, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A07_ShowsARevokedDeviceAsRevokedAndListsItUnderTheRevocationList()
    {
        var office = Office();
        var device = AddDevice(office, 1, DeviceRoles.Manager, "سامي الحاج", 1, SyncScopes.Full);
        Assert.Equal(KeyRefusal.None, DeviceKeys.Revoke(device, out _));

        var cut = _bunit.Render<A07AccountsAndKeys>();

        Assert.NotEmpty(cut.FindAll(".a07-row--revoked"));

        // The revocation list draws one key/value row per shut-out device with the day it happened
        // in a red chip, so two of each are on the screen: the office key's own row and this one,
        // and the table's revoked chip beside the list's.
        Assert.True(cut.FindAll(".w-kv-row").Count >= 2, "the revocation list draws a row per device");
        Assert.True(cut.FindAll(".w-chip--danger").Count >= 2, "revoked in the table and in the list");
        Assert.DoesNotContain(AdminAr.Keys.Revoke + "\"", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A07_SaysTheSeedWasDestroyedWhenADeviceIsRevokedBeforeAnySetupFile()
    {
        var office = Office();
        var device = AddDevice(office, 1, DeviceRoles.Manager, "سامي الحاج", 1, SyncScopes.Full);
        Assert.Equal(KeyRefusal.None, DeviceKeys.Revoke(device, out _));

        var cut = _bunit.Render<A07AccountsAndKeys>();

        // No setup file was ever made, so the seed did not leave with one: it was wiped here.
        Assert.Contains(AdminAr.Keys.SeedsDestroyed, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(AdminAr.Keys.SeedsGone, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A07_SwitchedOffRowActionsSayWhyRatherThanWhatTheyWouldHaveDone()
    {
        var office = Office();
        var device = AddDevice(office, 1, DeviceRoles.Manager, "سامي الحاج", 1, SyncScopes.Full);
        Assert.Equal(KeyRefusal.None, DeviceKeys.Revoke(device, out _));

        var cut = _bunit.Render<A07AccountsAndKeys>();

        Assert.Contains(AdminAr.Keys.RevokedNoAction, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(AdminAr.Keys.RevokeTooltip, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(AdminAr.Keys.RecoverTooltip, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A07_KeepsTheDangerDialogShutUntilRevokingIsAskedFor()
    {
        var office = Office();
        AddDevice(office, 1, DeviceRoles.Manager, "سامي الحاج", 1, SyncScopes.Full);

        var cut = _bunit.Render<A07AccountsAndKeys>();

        Assert.DoesNotContain(AdminAr.Keys.RevokeWarning, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(AdminAr.Keys.RecoverWarning, cut.Markup, StringComparison.Ordinal);
    }

    // ---- A12's shared confirmation ------------------------------------------------------------

    [Fact]
    public void DangerDialog_KeepsItsMainButtonSwitchedOffUntilTheWordMatches()
    {
        var cut = _bunit.Render<AdminConfirmDialog>(parameters => parameters
            .Add(p => p.Open, true)
            .Add(p => p.Danger, true)
            .Add(p => p.Title, AdminAr.Keys.RevokeTitle));

        // Nothing typed yet: the surface is marked locked and the reason is said in words.
        Assert.NotEmpty(cut.FindAll(".w-dialog--confirm-locked"));
        Assert.Contains(AdminAr.Confirm.LockedReason, cut.Markup, StringComparison.Ordinal);

        cut.Find(".a-confirm-typed input").Input(AdminAr.Confirm.ConfirmWord);

        Assert.Empty(cut.FindAll(".w-dialog--confirm-locked"));
        Assert.Contains(AdminAr.Confirm.Unlocked, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DangerDialog_SwitchesOffTheMainButtonOnlyAndLeavesTheWayBackAlive()
    {
        var backedOut = false;

        var cut = _bunit.Render<AdminConfirmDialog>(parameters => parameters
            .Add(p => p.Open, true)
            .Add(p => p.Danger, true)
            .Add(p => p.Title, AdminAr.Keys.RevokeTitle)
            .Add(p => p.ConfirmLabel, AdminAr.Keys.Revoke)
            .Add(p => p.OnCancel, () => backedOut = true));

        // The stylesheet that dims the locked button reaches it as «the first child of the actions
        // row», because every button there sits inside its own tooltip wrapper. This asserts the
        // shape that rule depends on: the first child carries the main button and nothing else, and
        // the way back is a separate child the rule can never reach.
        var actions = cut.Find(".w-dialog--confirm-locked .w-dialog-actions");

        Assert.True(actions.Children.Length > 1);
        Assert.Contains(AdminAr.Keys.Revoke, actions.Children[0].TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(AdminAr.Confirm.Back, actions.Children[0].TextContent, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Confirm.Back, actions.Children[1].TextContent, StringComparison.Ordinal);

        // And the way back really works while the dialog is locked.
        var back = cut.FindAll(".w-dialog-actions button")
            .Single(button => button.TextContent.Contains(AdminAr.Confirm.Back, StringComparison.Ordinal));
        back.Click();

        Assert.True(backedOut);
    }

    // ---- shared scaffolding ----------------------------------------------------------------

    private string BuildStructure()
    {
        CreateAccount();
        var root = Structure.EnsureRoot();
        Assert.NotNull(root);
        Structure.Add(root.Id, "دائرة المالية", null, null, out var department);
        Structure.Add(department!, "قسم الحسابات", null, null, out var section);
        Structure.Add(section!, "وحدة الرواتب", "منى العلي", "رئيس الوحدة", out var unit);
        Assert.NotNull(unit);
        return unit;
    }

    private string Office()
    {
        var unit = BuildStructure();
        Assert.Equal(StructureRefusal.None, Structure.SetOffice(unit, "OF-001"));
        return DeviceRegistry.ListOffices().Single().Id;
    }

    private string AddDevice(string officeId, int no, string role, string name, int employeeNo, string scope)
    {
        Assert.Equal(
            DeviceRefusal.None,
            DeviceRegistry.AddDevice(officeId, no, role, name, employeeNo, scope, out var id));
        Assert.NotNull(id);
        return id;
    }
}
