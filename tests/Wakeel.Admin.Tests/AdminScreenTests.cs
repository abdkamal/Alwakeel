using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Wakeel.Admin.UI.Components;
using Wakeel.Admin.UI.Layout;
using Wakeel.Admin.UI.Pages;
using Wakeel.Admin.UI.Services;
using Wakeel.Admin.UI.Services.Account;
using Wakeel.Admin.UI.Text;

namespace Wakeel.Admin.Tests;

/// <summary>
/// The screens themselves: A01, A02 and A03 rendered against a real tool on a temporary folder, plus
/// the shell and the two shared pieces of A12 those screens lean on.
/// </summary>
public class AdminScreenTests : AdminTestContext
{
    private readonly BunitContext _bunit = new();
    private readonly TestAdminWindow _window = new();

    public AdminScreenTests()
    {
        // Loose mode lets the interop calls the design system makes (applying the theme, trapping
        // focus in a dialog) go through with default answers instead of throwing.
        _bunit.JSInterop.Mode = JSRuntimeMode.Loose;

        // A window that can be closed, registered before the tool's own defaults so the screens draw
        // «الخروج» exactly as the Windows host makes them draw it — and closing it is counted here
        // instead of ending the test run.
        _bunit.Services.AddSingleton<IAdminWindow>(_window);
        RegisterInto(_bunit);
    }

    [Fact]
    public void A01_ShowsBothStepsAndKeepsTheSheetBackUntilTheAccountExists()
    {
        var cut = _bunit.Render<A01CreateAccount>();

        Assert.Contains(AdminAr.Account.Create.PasswordCardTitle, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Account.Create.SheetCardTitle, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Account.Create.CreateAccount, cut.Markup, StringComparison.Ordinal);

        // Nothing of the sheet is on screen before there is one.
        Assert.Empty(cut.FindAll(".a01-code"));
        Assert.DoesNotContain(AdminAr.Account.Create.Start, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A01_PrintsTheFourRulesAndTheStrengthMeter()
    {
        var cut = _bunit.Render<A01CreateAccount>();

        Assert.Equal(4, cut.FindAll(".a01-rule").Count);
        Assert.Equal(AdminPasswordStrengthSegments, cut.FindAll(".a01-meter-seg").Count);
        Assert.Contains(AdminAr.Account.Strength.RuleLength, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Account.Strength.RuleSymbol, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A01_ShowsTheSheetOnceTheAccountIsMade_AndGatesStartOnTheTickBox()
    {
        var cut = _bunit.Render<A01CreateAccount>();

        FillAndSubmit(cut);

        // The code, its picture and the warning that this is the only time it is shown.
        Assert.Contains(AdminAr.Account.Create.OnceWarningTitle, cut.Markup, StringComparison.Ordinal);
        Assert.Single(cut.FindAll(".a01-code"));
        Assert.Single(cut.FindAll(".a01-sheet-qr img"));
        Assert.Contains(AdminAr.Account.Create.PrintedCheckbox, cut.Markup, StringComparison.Ordinal);

        // «ابدأ» is there but refuses to be pressed until the tick box is ticked.
        var start = cut.FindAll("button").Single(button =>
            button.TextContent.Contains(AdminAr.Account.Create.Start, StringComparison.Ordinal));
        Assert.True(start.HasAttribute("disabled"));

        cut.Find(".a01-printed input[type=checkbox]").Change(true);

        start = cut.FindAll("button").Single(button =>
            button.TextContent.Contains(AdminAr.Account.Create.Start, StringComparison.Ordinal));
        Assert.False(start.HasAttribute("disabled"));
    }

    [Fact]
    public void A01_AsksBeforeClosingWhileTheSheetHasNotBeenSaved()
    {
        var cut = _bunit.Render<A01CreateAccount>();

        FillAndSubmit(cut);

        // The sheet is on screen and nobody has said yet that they printed it. One press of
        // «الخروج» would take the only copy of the organisation's recovery code with it, so it is
        // asked first, in red.
        ClickLabelled(cut, AdminAr.Account.Create.Exit);

        Assert.Equal(0, _window.Closes);
        Assert.Contains(AdminAr.Account.Create.ExitUnsavedTitle, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Account.Create.ExitUnsavedDesc, cut.Markup, StringComparison.Ordinal);

        // Backing out leaves the sheet where it was.
        ClickLabelled(cut, AdminAr.Account.Create.ExitUnsavedCancel);
        Assert.Equal(0, _window.Closes);
        Assert.Single(cut.FindAll(".a01-code"));

        // Saying it out loud is what closes the tool.
        ClickLabelled(cut, AdminAr.Account.Create.Exit);
        ClickLabelled(cut, AdminAr.Account.Create.ExitUnsavedConfirm);
        Assert.Equal(1, _window.Closes);
    }

    [Fact]
    public void A01_ClosesWithoutAskingOnceTheSheetIsSaid()
    {
        var cut = _bunit.Render<A01CreateAccount>();

        FillAndSubmit(cut);
        cut.Find(".a01-printed input[type=checkbox]").Change(true);

        ClickLabelled(cut, AdminAr.Account.Create.Exit);

        Assert.Equal(1, _window.Closes);
        Assert.DoesNotContain(AdminAr.Account.Create.ExitUnsavedTitle, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A01_SaysNothingAboutTheStrengthOfAPasswordNobodyHasTypedYet()
    {
        var cut = _bunit.Render<A01CreateAccount>();

        // An untouched field is not a weak password: judging it «قصيرة جدًا» says something about
        // something that is not there yet.
        Assert.DoesNotContain(
            AdminPasswordStrength.Describe(string.Empty),
            cut.Find(".a01-meter").TextContent,
            StringComparison.Ordinal);

        Type(cut, AdminAr.Account.Create.PasswordLabel, "abc");

        Assert.Contains(
            AdminPasswordStrength.Describe("abc"),
            cut.Find(".a01-meter").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A01_NamesTheComputerTheToolWasToldAbout()
    {
        var cut = _bunit.Render<A01CreateAccount>();

        // The same source A02 reads, so a computer that will not name itself is handled in one
        // place rather than two.
        Assert.Contains(Options.MachineName, cut.Find(".a01-bar-machine").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A01_SaysWhyWhenThePasswordIsTooWeak()
    {
        var cut = _bunit.Render<A01CreateAccount>();

        FillAndSubmit(cut, password: "short1!A");

        Assert.Contains(AdminAr.Account.Create.PasswordTooWeak, cut.Markup, StringComparison.Ordinal);
        Assert.False(Accounts.IsCreated);
    }

    [Fact]
    public void A02_AsksForThePasswordAndSaysHowManyTriesAreLeft()
    {
        CreateAccount();
        Accounts.SignOut();

        var cut = _bunit.Render<A02Login>();

        Assert.Contains(AdminAr.Account.SignIn.Title, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Account.SignIn.ForgotLink, cut.Markup, StringComparison.Ordinal);

        // The organisation is named above the field even though the database is still shut — the
        // key file carries its name for exactly this moment.
        Assert.Contains("هيئة تنمية المناطق الريفية", cut.Find(".a02-org").TextContent, StringComparison.Ordinal);

        Type(cut, AdminAr.Account.SignIn.PasswordLabel, OtherPassword);
        ClickLabelled(cut, AdminAr.Account.SignIn.Submit);

        Assert.Contains(
            AdminAr.Account.SignIn.AttemptsRemaining(Options.MaxAttempts - 1),
            cut.Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A02_ShowsTheTemporaryLockOutAndDisablesTheField()
    {
        CreateAccount();
        Accounts.SignOut();
        for (var attempt = 0; attempt < Options.MaxAttempts; attempt++)
        {
            Accounts.SignIn(OtherPassword);
        }

        var cut = _bunit.Render<A02Login>();

        Assert.Contains(AdminAr.Account.SignIn.LockedTitle, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Account.SignIn.LockedBody, cut.Markup, StringComparison.Ordinal);
        Assert.Single(cut.FindAll(".a02-locked"));
    }

    [Fact]
    public void A02_OffersTheRecoverySheetPathWithItsOwnFields()
    {
        CreateAccount();
        Accounts.SignOut();

        var cut = _bunit.Render<A02Login>();
        cut.Find(".a02-link").Click();

        Assert.Contains(AdminAr.Account.Recovery.Title, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Account.Recovery.CodeLabel, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Account.Recovery.NewPasswordLabel, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Account.Recovery.PickImage, cut.Markup, StringComparison.Ordinal);

        // This host cannot read pictures, and the screen says so rather than offering a dead button.
        Assert.Contains(AdminAr.Account.Recovery.ImageUnavailable, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A02_SaysWhenTheTypedCodeIsNotACodeAtAll()
    {
        CreateAccount();
        Accounts.SignOut();

        var cut = _bunit.Render<A02Login>();
        cut.Find(".a02-link").Click();

        Type(cut, AdminAr.Account.Recovery.CodeLabel, "ليس رمزًا");
        Type(cut, AdminAr.Account.Recovery.NewPasswordLabel, OtherPassword);
        Type(cut, AdminAr.Account.Recovery.NewPasswordConfirmLabel, OtherPassword);
        ClickLabelled(cut, AdminAr.Account.Recovery.Submit);

        Assert.Contains(AdminAr.Account.Recovery.CodeInvalid, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A03_ShowsTheFourFiguresAndAnInvitationWhileTheStructureIsEmpty()
    {
        CreateAccount();

        var cut = _bunit.Render<A03Dashboard>();

        Assert.Equal(4, cut.FindAll(".a03-kpis > *").Count);
        Assert.Contains(AdminAr.Dashboard.StructureCard, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Dashboard.DevicesCard, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Dashboard.StructureEmpty, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Dashboard.AlertsNone, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A03_SaysWhatTheAlertsAreRatherThanAssumingTheyAreAboutSetupFiles()
    {
        CreateAccount();

        var at = Time.GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        Db.Execute(
            """
            INSERT INTO org_units(id, parent_id, level, name, sort_order, created_at, updated_at)
            VALUES ('u-plan', NULL, 'department', 'دائرة التخطيط', 0, $at, $at);
            """,
            ("$at", at));
        Db.Execute(
            """
            INSERT INTO offices(id, unit_id, name, office_code, activated_at, created_at, updated_at)
            VALUES ('o-plan', 'u-plan', 'مكتب دائرة التخطيط', 'PLN', $at, $at, $at);
            """,
            ("$at", at));

        // The only thing wrong with this organisation is a device somebody revoked and never took
        // out of its office; nothing anywhere is waiting for a setup file.
        Db.Execute(
            """
            INSERT INTO devices(id, office_id, device_no, ed25519_pub, x25519_pub, issued_at, revoked_at, created_at, updated_at)
            VALUES ('d-2', 'o-plan', 2, 'pub-2', 'x-2', $at, $at, $at, $at);
            """,
            ("$at", at));

        var cut = _bunit.Render<A03Dashboard>();
        var head = cut.Find("[aria-labelledby=a03-alerts-title] .a03-panel-head").TextContent;

        Assert.Contains("جهاز مُلغى", head, StringComparison.Ordinal);
        Assert.DoesNotContain("ملف الإعداد", head, StringComparison.Ordinal);

        // And the way through to the whole list is on the panel, as the design draws it.
        Assert.Contains(AdminAr.Dashboard.AlertsAll, head, StringComparison.Ordinal);
    }

    [Fact]
    public void A03_OpensTheWaysOutNowThatTheScreensBehindThemExist()
    {
        CreateAccount();

        var at = Time.GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        Db.Execute(
            """
            INSERT INTO pending_changes(id, entity_type, entity_id, summary_ar, created_at)
            VALUES ('p-1', 'org_unit', 'u-1', 'أُضيف قسم جديد', $at);
            """,
            ("$at", at));

        var cut = _bunit.Render<A03Dashboard>();

        // Until admin-3 both ways out were drawn switched off, with the reason standing beside them:
        // a control that looks live and does nothing when pressed is indistinguishable from a broken
        // tool. A11 and A08 exist now, so both are live and the note is gone.
        var showAll = cut.FindAll("button")
            .Single(button => button.TextContent.Contains(AdminAr.Dashboard.AlertsAll, StringComparison.Ordinal));

        Assert.False(showAll.HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("[aria-labelledby=a03-alerts-title] .a03-off-note"));

        // The same for «تصدير ملف الإعداد», which the shell draws from the page's own header actions.
        var header = (AdminPageHeaderState)_bunit.Services.GetService(typeof(AdminPageHeaderState))!;
        var actions = _bunit.Render(header.Actions!);
        var export = actions.FindAll("button")
            .Single(button => button.TextContent.Contains(AdminAr.Dashboard.ExportSetup, StringComparison.Ordinal));

        Assert.False(export.HasAttribute("disabled"));
        Assert.DoesNotContain(AdminAr.Dashboard.NotReadyYetTooltip, actions.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Dashboard.ExportSetupTooltip, actions.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A03_PutsItsTitleAndItsTwoButtonsInTheShellHeader()
    {
        CreateAccount();

        _bunit.Render<A03Dashboard>();
        var header = _bunit.Services.GetService(typeof(AdminPageHeaderState)) as AdminPageHeaderState;

        Assert.NotNull(header);
        Assert.Equal(AdminAr.Dashboard.Title, header.Title);
        Assert.Equal(AdminAr.Dashboard.Sub, header.Sub);
        Assert.NotNull(header.Actions);
    }

    [Fact]
    public void TheShell_DrawsTheDarkBarAndAllSevenTabsAndNoSidebar()
    {
        CreateAccount();

        var cut = _bunit.Render<AdminLayout>(parameters => parameters.Add(
            layout => layout.Body,
            (RenderFragment)(builder => builder.AddContent(0, "محتوى"))));

        Assert.Single(cut.FindAll(".a-topbar"));
        Assert.Equal(AdminAr.Tabs.All.Count, cut.FindAll(".a-tab").Count);
        Assert.Contains(AdminAr.Tabs.Organisation, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Tabs.AuditLog, cut.Markup, StringComparison.Ordinal);
        Assert.Contains("محتوى", cut.Markup, StringComparison.Ordinal);

        // No sidebar anywhere in the administration tool.
        Assert.Empty(cut.FindAll(".w-sidebar"));
    }

    [Fact]
    public void TheShell_NamesTheOrganisationAndTheAdministratorInTheBar()
    {
        CreateAccount();

        var cut = _bunit.Render<AdminLayout>(parameters => parameters.Add(
            layout => layout.Body,
            (RenderFragment)(builder => builder.AddContent(0, "محتوى"))));

        Assert.Contains("هيئة تنمية المناطق الريفية", cut.Find(".a-topbar-sub").TextContent, StringComparison.Ordinal);
        Assert.Contains("سامي الحاج", cut.Find(".a-topbar-user-name").TextContent, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Shell.AdminRole, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDangerDialog_WillNotGoAheadUntilTheWordIsTypedOut()
    {
        var confirmed = false;

        var cut = _bunit.Render<AdminConfirmDialog>(parameters => parameters
            .Add(dialog => dialog.Open, true)
            .Add(dialog => dialog.Danger, true)
            .Add(dialog => dialog.Title, "إلغاء جهاز")
            .Add(dialog => dialog.OnConfirm, EventCallback.Factory.Create(this, () => confirmed = true)));

        Assert.Contains(AdminAr.Confirm.TypeToConfirm, cut.Markup, StringComparison.Ordinal);
        Assert.False(cut.Instance.CanConfirm);

        cut.Find(".a-confirm-typed input").Input(AdminAr.Confirm.ConfirmWord);

        Assert.True(cut.Instance.CanConfirm);
        Assert.False(confirmed);
    }

    [Fact]
    public void TheErrorState_SaysWhatHappenedInWordsAndOffersToTryAgain()
    {
        var retried = false;

        var cut = _bunit.Render<AdminErrorState>(parameters => parameters
            .Add(state => state.Message, AdminAr.Errors.CannotWriteFolder)
            .Add(state => state.OnRetry, EventCallback.Factory.Create(this, () => retried = true)));

        Assert.Contains(AdminAr.Errors.CannotWriteFolder, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Errors.Retry, cut.Markup, StringComparison.Ordinal);

        // Nothing a person can see carries a number or a word from the machine's own vocabulary.
        Assert.DoesNotContain("Exception", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SQL", cut.Markup, StringComparison.OrdinalIgnoreCase);

        cut.FindAll("button").First(button =>
            button.TextContent.Contains(AdminAr.Errors.Retry, StringComparison.Ordinal)).Click();

        Assert.True(retried);
    }

    private const int AdminPasswordStrengthSegments = 4;

    private void FillAndSubmit(IRenderedComponent<A01CreateAccount> cut, string password = GoodPassword)
    {
        Type(cut, AdminAr.Account.Create.PasswordLabel, password);
        Type(cut, AdminAr.Account.Create.ConfirmLabel, password);
        Type(cut, AdminAr.Account.Create.AdminNameLabel, "سامي الحاج");
        Type(cut, AdminAr.Account.Create.OrgNameLabel, "هيئة تنمية المناطق الريفية");
        ClickLabelled(cut, AdminAr.Account.Create.CreateAccount);
    }

    /// <summary>Types into the field whose printed label says <paramref name="label"/>.</summary>
    private static void Type<T>(IRenderedComponent<T> cut, string label, string value)
        where T : IComponent
    {
        var field = cut.FindAll("label")
            .First(element => element.TextContent.Contains(label, StringComparison.Ordinal))
            .ParentElement!;

        field.QuerySelector("input, textarea")!.Input(value);
    }

    /// <summary>Presses the button whose label says <paramref name="label"/>.</summary>
    private static void ClickLabelled<T>(IRenderedComponent<T> cut, string label)
        where T : IComponent =>
        cut.FindAll("button")
            .First(button => button.TextContent.Contains(label, StringComparison.Ordinal))
            .Click();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bunit.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>A window that can be closed and counts how often it was, instead of closing anything.</summary>
public sealed class TestAdminWindow : IAdminWindow
{
    /// <summary>How many times the tool asked to be closed.</summary>
    public int Closes { get; private set; }

    /// <inheritdoc />
    public bool CanClose => true;

    /// <summary>The guard the screen on show registered, so a test can ask exactly what the title
    /// bar's close button asks.</summary>
    public Func<bool>? CloseGuard { get; private set; }

    /// <inheritdoc />
    public void Close() => Closes++;

    /// <inheritdoc />
    public void SetCloseGuard(Func<bool>? guard) => CloseGuard = guard;
}
