using Bunit;
using Wakeel.Crypto;
using Wakeel.Design.Components;
using Wakeel.Design.Text;
using Wakeel.UI.Pages.Account;
using Wakeel.UI.Pages.FirstRun;
using Wakeel.UI.Services.Account;

namespace Wakeel.UI.Tests.FirstRun;

/// <summary>
/// W02 «ملف الإعداد»: the welcoming half, the drop zone that doubles as a file picker, the package
/// password, «فحص الحزمة» and the «أين أجد الملف؟» tooltip.
/// </summary>
public class W02SetupFileTests : FirstRunScreenContext
{
    [Fact]
    public void Shows_the_welcome_the_three_steps_and_both_ways_of_giving_it_a_file()
    {
        var cut = Render<W02SetupFile>();

        Assert.Contains(Ar.FirstRun.Setup.WelcomeTitle, cut.Markup);
        Assert.Contains(Ar.FirstRun.Setup.DropZoneChoose, cut.Markup);
        Assert.Contains(Ar.FirstRun.Setup.DropZoneOrDrag, cut.Markup);
        Assert.Single(cut.FindAll(".w02-drop input[type=file]"));
        Assert.Equal(3, cut.FindAll(".w02-step").Count);
    }

    [Fact]
    public void Asks_for_the_package_password_and_offers_the_check_button()
    {
        var cut = Render<W02SetupFile>();

        var password = cut.FindComponents<WInput>()
            .Single(input => input.Instance.Label == Ar.FirstRun.Setup.PasswordLabel);
        Assert.Equal(WInputType.Password, password.Instance.Type);

        Assert.Contains(cut.FindComponents<WButton>(), b => b.Instance.Label == Ar.FirstRun.Setup.Inspect);
    }

    [Fact]
    public void Explains_where_the_file_comes_from_in_a_tooltip_rather_than_in_the_page()
    {
        var cut = Render<W02SetupFile>();

        var tooltip = cut.FindComponents<WTooltip>()
            .Single(t => t.Instance.Text == Ar.FirstRun.Setup.WhereIsTheFileTooltip);

        Assert.Contains(Ar.FirstRun.Setup.WhereIsTheFile, cut.Find(".w02-where").TextContent);
        Assert.NotNull(tooltip);
    }

    [Fact]
    public async Task Names_the_chosen_file_with_its_size_and_date_isolated_from_the_Arabic()
    {
        await StageAsync();

        var cut = Render<W02SetupFile>();

        var row = cut.Find(".w02-file");
        Assert.Contains(SetupInspectionService.Extension, row.TextContent);

        // The file name and the «٢٤ ك.ب — ١٦/٠٩/٢٠٢٦» line are Latin and digit runs inside an Arabic
        // sentence; both have to sit in their own isolate or the punctuation walks to the wrong end.
        Assert.NotEmpty(row.QuerySelectorAll("bdi"));
        Assert.Contains(Ar.FirstRun.Setup.FileReady, cut.Markup);
    }

    [Fact]
    public void Every_icon_only_button_carries_a_tooltip()
    {
        var cut = Render<W02SetupFile>();

        foreach (var button in cut.FindComponents<WButton>())
        {
            if (button.Instance.Variant == WButtonVariant.Icon)
            {
                Assert.False(string.IsNullOrWhiteSpace(button.Instance.Tooltip));
            }
        }
    }

    [Fact]
    public void The_footer_dates_the_screen_from_the_clock_the_host_supplies()
    {
        var cut = Render<W02SetupFile>();

        var footer = cut.Find(".w02-footer");
        Assert.Contains(Ar.AppName, footer.TextContent);
        Assert.NotNull(footer.QuerySelector("bdi"));
    }
}

/// <summary>
/// W03 «فحص الحزمة والتفعيل»: the live check list, the organisation card, and every refusal the
/// spec lists — each of which still has to name whose file this is.
/// </summary>
public class W03SetupCheckTests : FirstRunScreenContext
{
    [Fact]
    public async Task Lists_every_check_as_passed_and_offers_to_activate()
    {
        await InspectAsync();

        var cut = Render<W03SetupCheck>();

        var lines = cut.FindAll(".w03-list li");
        Assert.Equal(Inspection.Lines.Count, lines.Count);
        Assert.NotEmpty(lines);
        Assert.Contains(Ar.FirstRun.Check.ReadyChip, cut.Markup);

        var activate = cut.FindComponents<WButton>()
            .Single(b => b.Instance.Label == Ar.FirstRun.Check.Activate);
        Assert.False(activate.Instance.Disabled);
    }

    [Fact]
    public async Task Shows_the_organisation_card_with_its_logo_and_the_device_it_binds_to()
    {
        await InspectAsync();

        var cut = Render<W03SetupCheck>();

        var card = cut.Find(".w03-org-card");
        Assert.Contains(SharedSetupFile.OrgName, card.TextContent);
        Assert.Contains(SharedSetupFile.OfficeName, card.TextContent);
        Assert.Contains(SharedSetupFile.EmployeeName, card.TextContent);
        Assert.StartsWith("data:image/png;base64,", card.QuerySelector("img.w03-org-logo")!.GetAttribute("src"));
    }

    [Fact]
    public async Task A_file_meant_for_another_machine_is_refused_but_still_named()
    {
        await InspectAsync(new SetupExpectations { InstalledDeviceId = "PC-9", InstalledExportSeq = 1 });

        var cut = Render<W03SetupCheck>();

        Assert.False(Inspection.IsAcceptable);
        Assert.Contains(Ar.FirstRun.Check.NotReadyChip, cut.Markup);
        Assert.Contains(SharedSetupFile.OrgName, cut.Find(".w03-org-card").TextContent);

        var activate = cut.FindComponents<WButton>()
            .Single(b => b.Instance.Label == Ar.FirstRun.Check.Activate);
        Assert.True(activate.Instance.Disabled);

        // Every refused line says why in an Arabic sentence, never a code.
        var failed = Inspection.Lines.Where(l => l.Status == SetupCheckStatus.Failed).ToList();
        Assert.NotEmpty(failed);
        foreach (var line in failed)
        {
            AssertPlainArabic(line.Value);
        }
    }

    [Fact]
    public async Task A_file_signed_by_someone_else_is_refused_in_plain_words()
    {
        using var stranger = DeviceIdentity.Generate();
        await InspectAsync(new SetupExpectations { PinnedOrgSigningPub = stranger.SigningPublicKey });

        var cut = Render<W03SetupCheck>();

        Assert.False(Inspection.IsAcceptable);
        AssertPlainArabic(Assert.IsType<string>(Inspection.RefusalMessage));
        Assert.Contains(Ar.FirstRun.Check.ChooseAnother, cut.Markup);
    }

    [Fact]
    public async Task A_file_older_than_the_installed_one_is_refused()
    {
        await InspectAsync(new SetupExpectations
        {
            InstalledDeviceId = SharedSetupFile.DeviceId,
            InstalledExportSeq = 9,
        });

        Render<W03SetupCheck>();

        Assert.False(Inspection.IsAcceptable);
        AssertPlainArabic(Assert.IsType<string>(Inspection.RefusalMessage));
    }

    [Fact]
    public async Task Nothing_on_the_screen_is_written_in_Latin_letters()
    {
        await InspectAsync();

        var cut = Render<W03SetupCheck>();

        foreach (var line in cut.FindAll(".w03-line-label"))
        {
            AssertPlainArabic(line.TextContent);
        }
    }

    private static void AssertPlainArabic(string text)
    {
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.DoesNotContain(text, c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
    }
}

/// <summary>
/// W04 «كلمة المرور وورقة الاسترداد»: the strength meter, the confirmation, and the sheet that is
/// shown once and gates «ابدأ العمل» behind «طبعتها وحفظتها».
/// </summary>
public class W04AccountTests : FirstRunScreenContext
{
    [Fact]
    public async Task Shows_the_recovery_sheet_once_with_its_code_and_its_square_picture()
    {
        await InspectAsync();

        var cut = Render<W04Account>();

        var sheet = cut.Find(".w04-sheet");
        Assert.Contains(Ar.FirstRun.Account.SheetCodeLabel, sheet.TextContent);
        Assert.StartsWith("data:image/png;base64,", sheet.QuerySelector("img.w04-sheet-qr")!.GetAttribute("src"));

        // The code itself is Latin letters and digits inside an Arabic sheet: it has to be isolated.
        var code = sheet.QuerySelector("bdi.w04-code");
        Assert.NotNull(code);
        Assert.True(RecoveryCode.TryParse(code!.TextContent, out _));
    }

    [Fact]
    public async Task The_sheet_can_be_printed_or_saved_and_says_so_when_this_host_cannot()
    {
        await InspectAsync();

        var cut = Render<W04Account>();

        // The bUnit host has no printer at all, which is exactly the state the two buttons must
        // show honestly rather than pretend away.
        var print = cut.FindComponents<WButton>().Single(b => b.Instance.Label == Ar.FirstRun.Account.Print);
        var save = cut.FindComponents<WButton>().Single(b => b.Instance.Label == Ar.FirstRun.Account.SavePdf);
        Assert.True(print.Instance.Disabled);
        Assert.True(save.Instance.Disabled);
    }

    [Fact]
    public async Task The_strength_meter_and_the_four_rules_follow_what_is_typed()
    {
        await InspectAsync();

        var cut = Render<W04Account>();
        Assert.Equal(4, cut.FindAll(".w04-rules li").Count);
        Assert.Contains(PasswordStrength.Describe(null), cut.Find(".w04-strength-label").TextContent);

        var password = cut.FindComponents<WInput>()
            .First(input => input.Instance.Label == Ar.FirstRun.Account.PasswordLabel);
        await cut.InvokeAsync(() => password.Instance.ValueChanged.InvokeAsync("كلمة-مرور-قوية-2026"));

        Assert.Contains(
            PasswordStrength.Describe("كلمة-مرور-قوية-2026"),
            cut.Find(".w04-strength-label").TextContent);
    }

    [Fact]
    public async Task Start_work_refuses_until_the_sheet_has_been_confirmed_as_printed()
    {
        await InspectAsync();

        var cut = Render<W04Account>();
        var inputs = cut.FindComponents<WInput>();
        var password = inputs.First(i => i.Instance.Label == Ar.FirstRun.Account.PasswordLabel);
        var confirm = inputs.First(i => i.Instance.Label == Ar.FirstRun.Account.ConfirmLabel);

        const string chosen = "كلمة-المرور-الأولى-2026";
        await cut.InvokeAsync(() => password.Instance.ValueChanged.InvokeAsync(chosen));
        await cut.InvokeAsync(() => confirm.Instance.ValueChanged.InvokeAsync(chosen));

        var start = cut.FindComponents<WButton>().Single(b => b.Instance.Label == Ar.FirstRun.Account.Start);
        await cut.InvokeAsync(() => start.Instance.OnClick.InvokeAsync());

        Assert.Contains(Ar.FirstRun.Account.ConfirmRequired, cut.Markup);
        Assert.False(Login.IsActivated);
    }

    [Fact]
    public async Task A_mistyped_confirmation_is_said_in_words_the_moment_it_differs()
    {
        await InspectAsync();

        var cut = Render<W04Account>();
        var inputs = cut.FindComponents<WInput>();
        var password = inputs.First(i => i.Instance.Label == Ar.FirstRun.Account.PasswordLabel);
        var confirm = inputs.First(i => i.Instance.Label == Ar.FirstRun.Account.ConfirmLabel);

        await cut.InvokeAsync(() => password.Instance.ValueChanged.InvokeAsync("كلمة-المرور-الأولى-2026"));
        await cut.InvokeAsync(() => confirm.Instance.ValueChanged.InvokeAsync("شيء-آخر-تمامًا-2026"));

        Assert.Contains(Ar.FirstRun.Account.ConfirmMismatch, cut.Markup);
    }
}

/// <summary>
/// W05 «الدخول»: the organisation's own logo and name, the one employee this installation belongs
/// to, the password, the way back through the recovery sheet, and the clock line.
/// </summary>
public class W05LoginTests : FirstRunScreenContext
{
    [Fact]
    public async Task Shows_the_organisation_the_office_and_the_employee_it_belongs_to()
    {
        await SignedOutAsync();

        var cut = Render<W05Login>();

        Assert.Contains(SharedSetupFile.OrgName, cut.Markup);
        Assert.Contains(SharedSetupFile.OfficeName, cut.Markup);
        Assert.Contains(SharedSetupFile.EmployeeName, cut.Find(".w05-name").TextContent);
        Assert.StartsWith("data:image/png;base64,", cut.Find("img.w05-logo").GetAttribute("src"));
    }

    [Fact]
    public async Task Sends_the_person_back_to_the_first_run_when_nothing_was_ever_activated()
    {
        var nav = (Microsoft.AspNetCore.Components.NavigationManager)
            Services.GetService(typeof(Microsoft.AspNetCore.Components.NavigationManager))!;

        Render<W05Login>();

        await Task.CompletedTask;
        Assert.EndsWith("/first-run", nav.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_identity_line_isolates_the_two_numbers_inside_the_Arabic()
    {
        await SignedOutAsync();

        var cut = Render<W05Login>();

        var line = cut.Find(".w05-identity bdi");
        Assert.Contains(SharedSetupFile.JobTitle, line.TextContent);
        Assert.Contains("2", line.TextContent);
        Assert.Contains("1", line.TextContent);
    }

    [Fact]
    public async Task Offers_the_password_with_a_way_to_reveal_it_and_the_way_back_through_the_sheet()
    {
        await SignedOutAsync();

        var cut = Render<W05Login>();

        var password = cut.FindComponents<WInput>()
            .Single(i => i.Instance.Label == Ar.FirstRun.Login.PasswordLabel);
        Assert.Equal(WInputType.Password, password.Instance.Type);

        // A password field carries its own show/hide control, and that control is icon-only, so it
        // has to name itself for anyone who cannot see the icon.
        var reveal = password.Find("button.w-field-adornment");
        Assert.Equal(Ar.Fields.ShowPassword, reveal.GetAttribute("aria-label"));
        Assert.Equal(Ar.Fields.ShowPassword, reveal.GetAttribute("title"));

        Assert.Contains(Ar.FirstRun.Login.ForgotPassword, cut.Find(".w05-forgot").TextContent);
    }

    [Fact]
    public async Task States_how_the_machine_clock_looks_underneath_everything_else()
    {
        await SignedOutAsync();

        var cut = Render<W05Login>();

        var clock = cut.Find(".w05-clock");
        Assert.Contains(Ar.FirstRun.Login.ClockOk, clock.TextContent);
    }

    [Fact]
    public async Task A_wrong_password_is_refused_in_words_and_counted()
    {
        await SignedOutAsync();

        var cut = Render<W05Login>();
        var password = cut.FindComponents<WInput>()
            .Single(i => i.Instance.Label == Ar.FirstRun.Login.PasswordLabel);
        await cut.InvokeAsync(() => password.Instance.ValueChanged.InvokeAsync("ليست كلمة المرور"));

        var signIn = cut.FindComponents<WButton>().Single(b => b.Instance.Label == Ar.FirstRun.Login.SignIn);
        await cut.InvokeAsync(() => signIn.Instance.OnClick.InvokeAsync());

        // The attempt counter appears, and what it says is a sentence in Arabic — never a code.
        var attempts = cut.Find(".w05-attempts-text").TextContent;
        Assert.DoesNotContain(attempts, c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
        Assert.False(Session.IsOpen);
    }

    [Fact]
    public async Task The_right_password_opens_the_session()
    {
        await SignedOutAsync();

        var cut = Render<W05Login>();
        var password = cut.FindComponents<WInput>()
            .Single(i => i.Instance.Label == Ar.FirstRun.Login.PasswordLabel);
        await cut.InvokeAsync(() => password.Instance.ValueChanged.InvokeAsync(AccountPassword));

        var signIn = cut.FindComponents<WButton>().Single(b => b.Instance.Label == Ar.FirstRun.Login.SignIn);
        await cut.InvokeAsync(() => signIn.Instance.OnClick.InvokeAsync());

        Assert.True(Session.IsOpen);
    }

    [Fact]
    public async Task An_empty_password_is_asked_for_rather_than_sent()
    {
        await SignedOutAsync();

        var cut = Render<W05Login>();

        Assert.Empty(cut.FindAll(".w05-attempts"));

        var signIn = cut.FindComponents<WButton>().Single(b => b.Instance.Label == Ar.FirstRun.Login.SignIn);
        await cut.InvokeAsync(() => signIn.Instance.OnClick.InvokeAsync());

        Assert.Contains(Ar.FirstRun.Login.PasswordRequired, cut.Markup);
        Assert.False(Session.IsOpen);
    }

    /// <summary>An activated machine nobody is signed in on — exactly what W05 is drawn for.</summary>
    private async Task SignedOutAsync()
    {
        await ActivateAsync();
        Session.SignOut();
    }
}

/// <summary>
/// W06 «القفل التلقائي»: nothing at all while the session is open, and a layer over whatever is on
/// screen once it has locked itself.
/// </summary>
public class W06LockOverlayTests : FirstRunScreenContext
{
    [Fact]
    public void Draws_nothing_while_the_session_is_open()
    {
        var cut = Render<W06LockOverlay>();

        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public async Task Covers_the_screen_with_the_name_the_password_and_the_drafts_note_once_locked()
    {
        await ActivateAsync();
        Session.Lock();

        var cut = Render<W06LockOverlay>();

        Assert.Single(cut.FindAll(".w06-scrim"));
        var dialog = cut.Find(".w06-card");
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        Assert.Contains(SharedSetupFile.EmployeeName, cut.Find(".w06-identity-name").TextContent);
        Assert.Contains(Ar.FirstRun.Lock.DraftsTitle, cut.Markup);
        Assert.Contains(cut.FindComponents<WButton>(), b => b.Instance.Label == Ar.FirstRun.Lock.Continue);
    }

    [Fact]
    public async Task The_right_password_opens_it_again()
    {
        await ActivateAsync();
        Session.Lock();

        var cut = Render<W06LockOverlay>();
        var password = cut.FindComponents<WInput>().First();
        await cut.InvokeAsync(() => password.Instance.ValueChanged.InvokeAsync(AccountPassword));

        var continueButton = cut.FindComponents<WButton>()
            .Single(b => b.Instance.Label == Ar.FirstRun.Lock.Continue);
        await cut.InvokeAsync(() => continueButton.Instance.OnClick.InvokeAsync());

        Assert.True(Session.IsOpen);
        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public async Task A_wrong_password_leaves_it_locked_and_says_so()
    {
        await ActivateAsync();
        Session.Lock();

        var cut = Render<W06LockOverlay>();
        var password = cut.FindComponents<WInput>().First();
        await cut.InvokeAsync(() => password.Instance.ValueChanged.InvokeAsync("ليست كلمة المرور"));

        var continueButton = cut.FindComponents<WButton>()
            .Single(b => b.Instance.Label == Ar.FirstRun.Lock.Continue);
        await cut.InvokeAsync(() => continueButton.Instance.OnClick.InvokeAsync());

        Assert.True(Session.IsLocked);
        Assert.Contains(Ar.FirstRun.Login.WrongPassword, cut.Markup);
    }
}

/// <summary>
/// W07 «خطأ واسترداد»: the dialog that takes a recovery code — typed out, or read off a picture of
/// the sheet's square — and sets a new password behind it.
/// </summary>
public class W07RecoveryDialogTests : FirstRunScreenContext
{
    [Fact]
    public void Draws_nothing_until_it_is_opened()
    {
        var cut = Render<W07RecoveryDialog>(p => p.Add(x => x.Open, false));

        Assert.Empty(cut.FindAll(".w07-steps"));
    }

    [Fact]
    public void Shows_its_two_steps_and_both_ways_of_giving_it_the_code()
    {
        var cut = Render<W07RecoveryDialog>(p => p.Add(x => x.Open, true));

        Assert.Equal(2, cut.FindAll(".w07-steps .w07-step").Count);
        Assert.Contains(Ar.FirstRun.Recovery.SourceLabel, cut.Find(".w07-source").TextContent);
        Assert.Contains(Ar.FirstRun.Recovery.SourceType, cut.Markup);
        Assert.Contains(Ar.FirstRun.Recovery.SourceScan, cut.Markup);
    }

    [Fact]
    public void Asks_for_a_new_password_with_its_own_strength_reading()
    {
        var cut = Render<W07RecoveryDialog>(p => p.Add(x => x.Open, true));

        Assert.Contains(Ar.FirstRun.Recovery.NewPasswordLabel, cut.Find(".w07-passwords").TextContent);
        Assert.Contains(PasswordStrength.Describe(null), cut.Find(".w07-strength").TextContent);
    }

    [Fact]
    public void Offers_a_fresh_sheet_but_does_not_force_one()
    {
        var cut = Render<W07RecoveryDialog>(p => p.Add(x => x.Open, true));

        Assert.Contains(Ar.FirstRun.Recovery.IssueNewSheetHint, cut.Markup);
        Assert.NotEmpty(cut.FindComponents<WCheckbox>());
    }

    [Fact]
    public void Nothing_in_the_dialog_is_written_in_Latin_letters()
    {
        var cut = Render<W07RecoveryDialog>(p => p.Add(x => x.Open, true));

        foreach (var label in cut.FindAll(".w07-label"))
        {
            Assert.DoesNotContain(label.TextContent, c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
        }
    }
}
