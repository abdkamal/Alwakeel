using Bunit;
using Microsoft.AspNetCore.Components;
using Wakeel.Design.Components;
using Wakeel.Design.Text;
using Wakeel.UI.Layout;

namespace Wakeel.UI.Tests.FirstRun;

/// <summary>
/// What the shell owes the account screens: the automatic lock lives inside it (W06), the user menu
/// can lock and sign out by hand, and the top bar names whoever is actually signed in.
/// </summary>
public class ShellAccountWiringTests : FirstRunScreenContext
{
    [Fact]
    public void The_lock_overlay_is_part_of_the_shell_and_shows_nothing_until_it_locks()
    {
        var cut = RenderShell();

        Assert.Single(cut.FindComponents<Wakeel.UI.Pages.Account.W06LockOverlay>());
        Assert.Empty(cut.FindAll(".w06-scrim"));
    }

    [Fact]
    public async Task Locking_from_the_user_menu_lays_the_overlay_over_the_open_screen()
    {
        await ActivateAsync();

        var cut = RenderShell();
        var topBar = cut.FindComponent<WTopBar>();
        await cut.InvokeAsync(() => topBar.Instance.OnLock.InvokeAsync());

        Assert.True(Session.IsLocked);
        Assert.Single(cut.FindAll(".w06-scrim"));
        Assert.Contains("محتوى الصفحة", cut.Markup);
    }

    [Fact]
    public async Task Signing_out_from_the_user_menu_closes_the_session_and_asks_again()
    {
        await ActivateAsync();
        var nav = (NavigationManager)Services.GetService(typeof(NavigationManager))!;

        var cut = RenderShell();
        var topBar = cut.FindComponent<WTopBar>();
        await cut.InvokeAsync(() => topBar.Instance.OnSignOut.InvokeAsync());

        Assert.False(Session.IsOpen);
        Assert.EndsWith("/login", nav.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_top_bar_names_the_person_signed_in_rather_than_the_sample_identity()
    {
        await ActivateAsync();

        var topBar = RenderShell().FindComponent<WTopBar>();

        Assert.Equal(SharedSetupFile.EmployeeName, topBar.Instance.UserName);
        Assert.Equal(Ar.FirstRun.Check.RoleSecretary, topBar.Instance.UserRole);
    }

    [Fact]
    public void Before_anybody_signs_in_the_top_bar_keeps_the_design_system_sample_identity()
    {
        var topBar = RenderShell().FindComponent<WTopBar>();

        Assert.Equal(Ar.Sample.UserName, topBar.Instance.UserName);
        Assert.Equal(Ar.Sample.UserRole, topBar.Instance.UserRole);
    }

    private IRenderedComponent<MainLayout> RenderShell() =>
        Render<MainLayout>(p => p.Add(
            x => x.Body,
            (RenderFragment)(b => b.AddContent(0, "محتوى الصفحة"))));
}

/// <summary>
/// The one place in this package where a bidi control character is written by hand, and the reason
/// it has to be: a line of Arabic that ends in a file extension.
/// </summary>
public class SetupExtensionBidiTests
{
    private const char Lri = '⁦';
    private const char Pdi = '⁩';

    [Fact]
    public void The_extension_carries_its_leading_dot_inside_the_isolate()
    {
        Assert.Equal(Ar.FirstRun.SetupExtensionInline, Lri + Ar.FirstRun.SetupExtension + Pdi);
        Assert.StartsWith(Lri.ToString() + '.', Ar.FirstRun.SetupExtensionInline, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(Ar.FirstRun.Setup.WelcomeBody))]
    [InlineData(nameof(Ar.FirstRun.Setup.DropZoneHint))]
    [InlineData(nameof(Ar.FirstRun.Setup.WrongExtension))]
    public void Every_sentence_that_names_the_extension_isolates_it(string which)
    {
        var sentence = which switch
        {
            nameof(Ar.FirstRun.Setup.WelcomeBody) => Ar.FirstRun.Setup.WelcomeBody,
            nameof(Ar.FirstRun.Setup.DropZoneHint) => Ar.FirstRun.Setup.DropZoneHint,
            _ => Ar.FirstRun.Setup.WrongExtension,
        };

        Assert.Contains(Ar.FirstRun.SetupExtensionInline, sentence, StringComparison.Ordinal);

        // A bare, unisolated occurrence anywhere else in the same sentence would reintroduce exactly
        // the misplaced dot the isolate exists to prevent.
        var stripped = sentence.Replace(Ar.FirstRun.SetupExtensionInline, string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(Ar.FirstRun.SetupExtension, stripped, StringComparison.Ordinal);

        // And every isolate that is opened is closed again.
        Assert.Equal(sentence.Count(c => c == Lri), sentence.Count(c => c == Pdi));
    }
}
