using Bunit;
using Microsoft.AspNetCore.Components;
using Wakeel.Crypto;
using Wakeel.Design.Components;
using Wakeel.Design.Text;
using Wakeel.UI.Pages.Account;
using Wakeel.UI.Pages.FirstRun;
using Wakeel.UI.Services.Account;

namespace Wakeel.UI.Tests.FirstRun;

/// <summary>
/// The states the six screens reach when something is already there, or when something the screen
/// depends on is not: a machine that carries an installation must never be walked through the first
/// run again, a sealed card this machine cannot open must not be drawn as if it could be, and a
/// clock nobody has checked must not be reported as sound.
/// </summary>
public class FirstRunGuardTests : FirstRunScreenContext
{
    private NavigationManager Nav => (NavigationManager)Services.GetService(typeof(NavigationManager))!;

    [Fact]
    public async Task The_first_run_screens_send_an_activated_machine_to_its_own_sign_in_screen()
    {
        await ActivateAsync();
        Session.SignOut();

        Render<W02SetupFile>();
        Assert.EndsWith("/login", Nav.Uri, StringComparison.Ordinal);

        Nav.NavigateTo("/first-run/check");
        Render<W03SetupCheck>();
        Assert.EndsWith("/login", Nav.Uri, StringComparison.Ordinal);

        Nav.NavigateTo("/first-run/account");
        Render<W04Account>();
        Assert.EndsWith("/login", Nav.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Activating_a_machine_that_already_carries_an_installation_is_refused_in_plain_words()
    {
        await ActivateAsync();
        var keyFileBefore = await File.ReadAllBytesAsync(Paths.InstallationKeyPath);
        Session.SignOut();

        // The service is asked directly, because the screen in front of it now refuses to draw at
        // all on such a machine — and both refusals matter.
        await InspectAsync();
        Activation.Begin();
        var result = await Activation.ActivateAsync("كلمة-مرور-أخرى-تمامًا-2026");

        Assert.Equal(ActivationOutcome.AlreadyActivated, result.Outcome);
        Assert.Equal(Ar.FirstRun.Account.AlreadyActivated, result.Message);
        Assert.Equal(keyFileBefore, await File.ReadAllBytesAsync(Paths.InstallationKeyPath));
    }

    [Fact]
    public async Task A_machine_that_cannot_read_its_own_card_shows_the_product_and_asks_for_the_password()
    {
        await ActivateAsync();
        Session.SignOut();

        // What a folder restored onto another computer leaves behind: the keys and the database are
        // there, the sealed card is not.
        File.Delete(Profiles.Path);

        var cut = Render<W05Login>();

        Assert.Empty(cut.FindAll(".w05-identity"));
        Assert.DoesNotContain(Ar.FirstRun.Login.NotYou, cut.Markup);
        Assert.Contains(Ar.AppName, cut.Markup);
        Assert.Contains(Ar.FirstRun.Login.FooterLineWithoutDevice(Ar.FirstRun.AppVersion), cut.Markup);
        Assert.Contains(Ar.FirstRun.Login.PasswordLabel, cut.Markup);
    }

    [Fact]
    public async Task The_identity_block_comes_back_as_soon_as_the_card_can_be_read()
    {
        await ActivateAsync();
        Session.SignOut();

        var cut = Render<W05Login>();

        Assert.Single(cut.FindAll(".w05-identity"));
        Assert.Contains(
            Ar.FirstRun.Login.FooterLine(Ar.FirstRun.AppVersion, 1),
            cut.Find(".w05-footer").TextContent);
    }

    [Fact]
    public async Task A_clock_that_sits_before_the_last_sign_in_is_reported_as_wrong()
    {
        await ActivateAsync();
        Session.SignOut();

        // The screen's own clock is fixed; the card is the thing that says somebody was already
        // here — a day later than the machine now believes it is.
        SaveProfile(profile => profile.LastSignInAt = Time.GetUtcNow().AddDays(1));

        var cut = Render<W05Login>();

        var chip = cut.FindComponents<WChip>().Single(c => c.Instance.Label!.Contains(Ar.FirstRun.Login.ClockBad));
        Assert.Equal(WChipVariant.Danger, chip.Instance.Variant);
    }

    [Fact]
    public async Task A_clock_that_agrees_with_everything_the_screen_knows_is_reported_as_sound()
    {
        await ActivateAsync();
        Session.SignOut();
        SaveProfile(profile => profile.LastSignInAt = Time.GetUtcNow().AddMinutes(-30));

        var cut = Render<W05Login>();

        var chip = cut.FindComponents<WChip>().Single(c => c.Instance.Label!.Contains(Ar.FirstRun.Login.ClockOk));
        Assert.Equal(WChipVariant.Success, chip.Instance.Variant);
    }

    [Fact]
    public async Task The_caps_lock_hint_keeps_the_key_name_in_one_piece()
    {
        await ActivateAsync();
        Session.SignOut();

        var cut = Render<W05Login>();
        var password = cut.FindComponents<WInput>().First(i => i.Instance.Label == Ar.FirstRun.Login.PasswordLabel);
        await cut.InvokeAsync(() => password.Instance.ValueChanged.InvokeAsync("PASSWORD-2026"));

        // Item 55: the Latin run sits inside an isolate, and it is rendered through <bdi> as every
        // other mixed line on this screen is.
        var hint = cut.Find(".w05-caps bdi");
        Assert.Contains("⁦Caps Lock⁩", hint.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_new_recovery_sheet_can_be_printed_or_saved_rather_than_copied_by_hand()
    {
        var code = await ActivateAsync();
        Session.SignOut();

        var cut = Render<W07RecoveryDialog>(p => p.Add(x => x.Open, true));
        var codeInput = cut.FindComponents<WInput>()
            .First(i => i.Instance.Label == Ar.FirstRun.Recovery.CodeLabel(RecoveryCode.TotalCharacters));
        await cut.InvokeAsync(() => codeInput.Instance.ValueChanged.InvokeAsync(code));

        var passwords = cut.FindComponents<WInput>()
            .Where(i => i.Instance.Label is not null
                && (i.Instance.Label == Ar.FirstRun.Recovery.NewPasswordLabel
                    || i.Instance.Label == Ar.FirstRun.Recovery.ConfirmLabel))
            .ToList();
        foreach (var field in passwords)
        {
            await cut.InvokeAsync(() => field.Instance.ValueChanged.InvokeAsync(AccountPassword + "-جديدة"));
        }

        var newSheetBox = cut.FindComponents<WCheckbox>().First();
        await cut.InvokeAsync(() => newSheetBox.Instance.ValueChanged.InvokeAsync(true));
        var dialog = cut.FindComponent<WDialog>();
        await cut.InvokeAsync(() => dialog.Instance.OnConfirm.InvokeAsync());

        Assert.Contains(Ar.FirstRun.Recovery.NewSheetTitle, cut.Markup);
        Assert.Contains(Ar.FirstRun.Recovery.NewSheetPrint, cut.Markup);
        Assert.Contains(Ar.FirstRun.Recovery.NewSheetSavePdf, cut.Markup);
        Assert.NotEmpty(cut.FindAll(".w07-sheet-actions"));

        // The form is gone: the recovery is done, and the one thing still worth doing here is
        // getting the new sheet off the screen and onto paper.
        Assert.Empty(cut.FindAll(".w07-passwords"));
    }

    /// <summary>
    /// The pause the sign-in screen announces is a wait, not a state it stays in. Once it has run
    /// out the button has to come back by itself; making the person close الوكيل and open it again
    /// after waiting exactly as long as they were told would be a defect with no visible cause.
    /// </summary>
    [Fact]
    public async Task The_sign_in_button_comes_back_by_itself_once_the_wait_is_over()
    {
        await ActivateAsync();
        Session.SignOut();
        SaveProfile(profile => profile.LockedUntil = Time.GetUtcNow().AddMinutes(5));

        var cut = Render<W05Login>();
        var button = cut.FindComponents<WButton>().First(b => b.Instance.Label == Ar.FirstRun.Login.SignIn);
        Assert.True(button.Instance.Disabled);
        Assert.Contains(Ar.FirstRun.Login.LockedOut(5), cut.Markup, StringComparison.Ordinal);

        // The wait runs out while the screen is still standing there.
        SaveProfile(profile => profile.LockedUntil = null);

        var password = cut.FindComponents<WInput>().First(i => i.Instance.Label == Ar.FirstRun.Login.PasswordLabel);
        await cut.InvokeAsync(() => password.Instance.ValueChanged.InvokeAsync(AccountPassword));

        button = cut.FindComponents<WButton>().First(b => b.Instance.Label == Ar.FirstRun.Login.SignIn);
        Assert.False(button.Instance.Disabled);
        Assert.DoesNotContain(Ar.FirstRun.Login.LockedOut(5), cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same wait-is-over recheck, but for a person who never touches the keyboard: the idle
    /// timer W05Login starts on its own re-checks the standing lock-out and clears it without any
    /// keystroke driving <see cref="Wakeel.UI.Pages.Account.W05Login.OnPasswordChanged"/>.
    /// </summary>
    [Fact]
    public async Task The_idle_lock_timer_clears_the_lock_out_on_its_own()
    {
        await ActivateAsync();
        Session.SignOut();
        SaveProfile(profile => profile.LockedUntil = Time.GetUtcNow().AddMinutes(5));

        var clock = (FixedTime)Time;
        var cut = Render<W05Login>();
        var button = cut.FindComponents<WButton>().First(b => b.Instance.Label == Ar.FirstRun.Login.SignIn);
        Assert.True(button.Instance.Disabled);
        Assert.Contains(Ar.FirstRun.Login.LockedOut(5), cut.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(cut.FindAll(".w-card--danger"));

        var timer = clock.LastTimer;
        Assert.NotNull(timer);
        Assert.False(timer!.Disposed);

        // The wait runs out on its own; nothing is typed.
        SaveProfile(profile => profile.LockedUntil = null);
        await cut.InvokeAsync(timer.Fire);

        button = cut.FindComponents<WButton>().First(b => b.Instance.Label == Ar.FirstRun.Login.SignIn);
        Assert.False(button.Instance.Disabled);
        Assert.DoesNotContain(Ar.FirstRun.Login.LockedOut(5), cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".w-card--danger"));

        // A lock timer that just cleared the lock must not keep ticking behind the screen.
        Assert.True(timer.Disposed);
    }

    /// <summary>A lock timer still running when the screen itself goes away must not be left ticking.</summary>
    [Fact]
    public async Task Disposing_the_sign_in_screen_stops_a_still_running_lock_timer()
    {
        await ActivateAsync();
        Session.SignOut();
        SaveProfile(profile => profile.LockedUntil = Time.GetUtcNow().AddMinutes(5));

        var clock = (FixedTime)Time;
        Render<W05Login>();
        var timer = clock.LastTimer;
        Assert.NotNull(timer);
        Assert.False(timer!.Disposed);

        // bUnit only runs a rendered component's own Dispose (IDisposable.Dispose, which is where
        // W05Login stops its timer) when the test context is asked to dispose its components.
        await Renderer.DisposeComponents();

        Assert.True(timer.Disposed);
    }

    /// <summary>
    /// Checking a code costs one deliberately slow derivation. A character that lands while one is
    /// running must not be dropped: the code the person finished typing is the one that has to get a
    /// verdict, or a complete and correct code stays refused until they edit the field again.
    /// </summary>
    [Fact]
    public async Task A_code_finished_while_the_previous_one_was_being_checked_still_turns_green()
    {
        var code = await ActivateAsync();
        Session.SignOut();

        var cut = Render<W07RecoveryDialog>(p => p.Add(x => x.Open, true));
        var codeInput = cut.FindComponents<WInput>()
            .First(i => i.Instance.Label == Ar.FirstRun.Recovery.CodeLabel(RecoveryCode.TotalCharacters));

        // Two texts one after the other without waiting: a wrong code of the full length, and the
        // right one typed over it while the first is still being checked.
        var first = cut.InvokeAsync(() => codeInput.Instance.ValueChanged.InvokeAsync(RecoveryCode.Generate().Display));
        var second = cut.InvokeAsync(() => codeInput.Instance.ValueChanged.InvokeAsync(code));
        await Task.WhenAll(first, second);

        cut.WaitForAssertion(
            () => Assert.Contains(Ar.FirstRun.Recovery.CodeVerified, cut.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(10));
    }
}
