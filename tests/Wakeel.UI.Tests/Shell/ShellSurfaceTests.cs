using System.Globalization;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Wakeel.Core.Data.Entities;
using Wakeel.Core.Services;
using Wakeel.Design.Components;
using Wakeel.Design.Text;
using Wakeel.UI.Layout;
using Wakeel.UI.Pages;
using Wakeel.UI.Services.Shell;
using Wakeel.UI.Tests.FirstRun;

namespace Wakeel.UI.Tests.Shell;

/// <summary>
/// The three surfaces that live over a screen rather than being one: W10 (the bell's panel),
/// W11 (the clock banner) and W94 (the quick-entry dialog).
/// </summary>
public sealed class ShellSurfaceTests : ShellScreenContext
{
    // ---------------------------------------------------------------------------------------
    // W10 — لوحة الإشعارات
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void W10_Closed_DrawsNothingAtAll()
    {
        var cut = Render<W10NotificationPanel>(p => p.Add(x => x.Open, false));

        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public async Task W10_OpenWithNoNotifications_ShowsTheEmptyStateAndTheThreeChromeControls()
    {
        await OpenSessionAsync();

        var cut = Render<W10NotificationPanel>(p => p
            .Add(x => x.Open, true)
            .Add(x => x.Now, Now));

        Assert.Contains(Ar.Notifications.Title, cut.Markup);
        Assert.Contains(Ar.Notifications.EmptyTitle, cut.Markup);
        Assert.NotNull(cut.Find(".w10-mark-all"));
        Assert.NotNull(cut.Find(".w10-settings"));
        Assert.NotNull(cut.Find(".w10-view-all"));
        Assert.Empty(cut.FindComponents<WNotificationItem>());
    }

    [Fact]
    public async Task W10_IsThe380WidePanelOfTheSpecification()
    {
        await OpenSessionAsync();

        var cut = Render<W10NotificationPanel>(p => p.Add(x => x.Open, true).Add(x => x.Now, Now));

        Assert.NotNull(cut.Find(".w-notification-panel"));
        Assert.Equal(380, WNotificationPanel.WidthPx);
    }

    [Fact]
    public async Task W10_Loaded_ListsEveryRowWithoutDayHeadingsAndCountsAllAndUnreadSeparately()
    {
        await OpenSessionAsync();
        await SeedNotificationsAsync();

        var cut = Render<W10NotificationPanel>(p => p.Add(x => x.Open, true).Add(x => x.Now, Now));

        var tabs = cut.FindComponents<WTabItem>();
        Assert.Equal(Ar.Notifications.TabAll, tabs[0].Instance.Label);
        Assert.Equal(3, tabs[0].Instance.Count);
        Assert.Equal(Ar.Notifications.TabUnread, tabs[1].Instance.Label);
        Assert.Equal(2, tabs[1].Instance.Count);

        // One continuous list, as the export draws it: every row is there and no day heading is.
        // («أمس» itself is not the probe — it is also how a row states its own relative time.)
        Assert.Equal(3, cut.FindComponents<WNotificationItem>().Count);
        Assert.Empty(cut.FindComponents<WSectionHeader>());
    }

    [Fact]
    public async Task W10_EveryRowCarriesItsKindIconItsKindInWordsAndARelativeTime()
    {
        await OpenSessionAsync();
        await SeedNotificationsAsync();

        var cut = Render<W10NotificationPanel>(p => p.Add(x => x.Open, true).Add(x => x.Now, Now));

        var rows = cut.FindComponents<WNotificationItem>();
        Assert.All(rows, row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Instance.Icon));
            Assert.False(string.IsNullOrWhiteSpace(row.Instance.KindLabel));
            Assert.False(string.IsNullOrWhiteSpace(row.Instance.RelativeTime));
            Assert.False(string.IsNullOrWhiteSpace(row.Instance.Tooltip));
        });

        // The relative time is isolated, because it carries digits (AGREEMENT item 55).
        Assert.NotEmpty(cut.FindAll(".w-notification-time"));
    }

    [Fact]
    public async Task W10_TheUnreadTab_ShowsOnlyWhatHasNotBeenRead()
    {
        await OpenSessionAsync();
        await SeedNotificationsAsync();

        var cut = Render<W10NotificationPanel>(p => p.Add(x => x.Open, true).Add(x => x.Now, Now));
        cut.FindComponents<WTabItem>()[1].Find(".w-tab").Click();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindComponents<WNotificationItem>().Count));
        Assert.DoesNotContain("نسخة احتياطية قديمة", cut.Markup);
    }

    [Fact]
    public async Task W10_MarkAllRead_EmptiesTheUnreadCountAndSaysSo()
    {
        await OpenSessionAsync();
        await SeedNotificationsAsync();

        var cut = Render<W10NotificationPanel>(p => p.Add(x => x.Open, true).Add(x => x.Now, Now));
        cut.Find(".w10-mark-all").Click();

        cut.WaitForAssertion(() => Assert.Equal(0, cut.FindComponents<WTabItem>()[1].Instance.Count));
        Assert.Equal(0, await Notifications.GetUnreadCountAsync());
    }

    [Fact]
    public async Task W10_ClickingANotification_MarksItReadClosesThePanelAndOpensItsRecord()
    {
        await OpenSessionAsync();
        var taskId = Guid.NewGuid();
        await Notifications.CreateAsync(
            NotificationKinds.TaskDue,
            "مهمة تستحق اليوم Alpha-7",
            entityType: "tasks",
            entityId: taskId,
            createdAt: Now.AddMinutes(-10));

        var nav = Services.GetRequiredService<NavigationManager>();
        var closed = false;
        var cut = Render<W10NotificationPanel>(p => p
            .Add(x => x.Open, true)
            .Add(x => x.Now, Now)
            .Add(x => x.OpenChanged, (bool open) => closed = !open));

        cut.Find(".w-notification").Click();

        cut.WaitForAssertion(() => Assert.True(closed));
        Assert.EndsWith($"/w29/{taskId}", nav.Uri, StringComparison.Ordinal);
        Assert.Equal(0, await Notifications.GetUnreadCountAsync());
    }

    [Fact]
    public async Task W10_TheSettingsButton_GoesToTheAlertSettingsScreen()
    {
        await OpenSessionAsync();

        var nav = Services.GetRequiredService<NavigationManager>();
        var cut = Render<W10NotificationPanel>(p => p.Add(x => x.Open, true).Add(x => x.Now, Now));

        cut.Find(".w10-settings").Click();

        Assert.EndsWith(W10NotificationPanel.SettingsRoute, nav.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void W10_WithNoSessionOpen_SaysItCouldNotBeShownRatherThanThrowing()
    {
        var cut = Render<W10NotificationPanel>(p => p.Add(x => x.Open, true).Add(x => x.Now, Now));

        Assert.Contains(Ar.Shell.ReadFailedTitle, cut.Markup);
    }

    private async Task SeedNotificationsAsync()
    {
        var today = await Notifications.CreateAsync(
            NotificationKinds.Meeting,
            "اجتماع اللجنة الفنية يبدأ بعد نصف ساعة",
            "قاعة الاجتماعات الرئيسية",
            createdAt: Now.AddMinutes(-30));
        Assert.NotNull(today);

        await Notifications.CreateAsync(
            NotificationKinds.FinancialCycle,
            "بقي يومان على نهاية الدورة المالية",
            createdAt: Now.AddHours(-2));

        var older = await Notifications.CreateAsync(
            NotificationKinds.Backup,
            "نسخة احتياطية قديمة",
            createdAt: Now.AddDays(-1));

        // One of the three has already been read, so «الكل» and «غير المقروء» cannot be the same
        // number by accident.
        await Notifications.MarkReadAsync(older.Id, Now);
    }

    // ---------------------------------------------------------------------------------------
    // W11 — تنبيه الساعة
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void W11_WithNoSessionOpen_DrawsNothing()
    {
        var cut = Render<W11ClockBanner>();

        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public async Task W11_WithAHealthyClock_DrawsNothing()
    {
        await OpenSessionAsync();
        await Shell.Clock.CheckAsync(ClockCheckTrigger.Startup, Now);

        var cut = Render<W11ClockBanner>();

        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public async Task W11_WithABadClock_ShowsTheBannerItsExplanationAndTheNumberingNotice()
    {
        await OpenSessionAsync();
        var state = await Shell.Clock.CheckAsync(ClockCheckTrigger.Startup, Now.AddYears(-5));
        Assert.True(state.Visible);

        var cut = Render<W11ClockBanner>();

        Assert.Contains(state.TitleAr, cut.Markup);
        Assert.Contains(state.NumberingNoticeAr, cut.Markup);
        Assert.NotNull(cut.Find(".w11-fix"));
        Assert.NotNull(cut.Find(".w11-dismiss"));
        Assert.True(Shell.Clock.NumberingBlocked);
    }

    [Fact]
    public async Task W11_BothButtonsCarryATooltip()
    {
        await OpenSessionAsync();
        await Shell.Clock.CheckAsync(ClockCheckTrigger.Startup, Now.AddYears(-5));

        var cut = Render<W11ClockBanner>();
        var buttons = cut.FindComponents<WButton>();

        Assert.All(buttons, button => Assert.False(string.IsNullOrWhiteSpace(button.Instance.Tooltip)));
    }

    [Fact]
    public async Task W11_DismissForThisSession_HidesTheBannerButKeepsNumberingBlocked()
    {
        await OpenSessionAsync();
        await Shell.Clock.CheckAsync(ClockCheckTrigger.Startup, Now.AddYears(-5));

        var cut = Render<W11ClockBanner>();
        cut.Find(".w11-dismiss").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.Markup.Trim()));
        Assert.True(Shell.Clock.NumberingBlocked);
    }

    [Fact]
    public async Task W11_FixTheClock_AsksTheHostForTheWindowsDateAndTimeSettings()
    {
        var launcher = new RecordingLauncher();
        Services.AddSingleton<ISystemSettingsLauncher>(launcher);
        await OpenSessionAsync();
        await Shell.Clock.CheckAsync(ClockCheckTrigger.Startup, Now.AddYears(-5));

        var cut = Render<W11ClockBanner>();
        cut.Find(".w11-fix").Click();

        Assert.Equal(1, launcher.Calls);
    }

    private sealed class RecordingLauncher : ISystemSettingsLauncher
    {
        internal int Calls { get; private set; }

        public bool OpenDateAndTime()
        {
            Calls++;
            return true;
        }
    }

    // ---------------------------------------------------------------------------------------
    // W94 — الإدخال السريع
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void W94_Closed_DrawsNothing()
    {
        var cut = Render<W94QuickCapture>(p => p.Add(x => x.Open, false));

        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public async Task W94_Open_OffersTheFiveKindsAsOneSegmentedStripWithTaskSelected()
    {
        await OpenSessionAsync();

        var cut = Render<W94QuickCapture>(p => p.Add(x => x.Open, true).Add(x => x.Now, Now.ToLocalTime()));

        var segmented = cut.FindComponent<WSegmented>();
        Assert.True(segmented.Instance.Stacked);
        Assert.Equal(5, segmented.Instance.Options.Count);
        Assert.Equal(nameof(QuickCaptureKind.Task), segmented.Instance.SelectedValue);
        Assert.Contains(Ar.QuickCapture.KindReportNote, cut.Markup);
    }

    [Fact]
    public async Task W94_SavingWithAnEmptyTitle_RefusesAndSaysWhichFieldIsMissing()
    {
        await OpenSessionAsync();

        var saved = 0;
        var cut = Render<W94QuickCapture>(p => p
            .Add(x => x.Open, true)
            .Add(x => x.Now, Now.ToLocalTime())
            .Add(x => x.OnCaptured, (QuickCaptureResult _) => saved++));

        await cut.InvokeAsync(() => cut.FindComponent<WDialog>().Instance.OnConfirm.InvokeAsync());
        cut.Render();

        Assert.Equal(0, saved);
        Assert.Empty(Db.Tasks);
        Assert.NotEmpty(cut.FindAll(".w-field--error"));
    }

    [Fact]
    public async Task W94_SavingATask_WritesItImmediatelyAndOffersTheUndo()
    {
        await OpenSessionAsync();

        QuickCaptureResult? captured = null;
        var cut = Render<W94QuickCapture>(p => p
            .Add(x => x.Open, true)
            .Add(x => x.Now, Now.ToLocalTime())
            .Add(x => x.OnCaptured, (QuickCaptureResult r) => captured = r));

        var title = cut.FindComponents<WInput>()[0];
        await cut.InvokeAsync(() => title.Instance.ValueChanged.InvokeAsync("متابعة صيانة الطريق Ring-Road"));
        await cut.InvokeAsync(() => cut.FindComponent<WDialog>().Instance.OnConfirm.InvokeAsync());

        cut.WaitForAssertion(() => Assert.NotNull(captured));
        Assert.False(string.IsNullOrWhiteSpace(captured!.MessageAr));
        Assert.False(string.IsNullOrWhiteSpace(captured.UndoToken));

        var task = Db.Tasks.Single();
        Assert.Equal("متابعة صيانة الطريق Ring-Road", task.Title);

        // The undo the shell's toast offers actually takes it back.
        Assert.True(await Shell.QuickCapture.UndoAsync(captured.UndoToken!));
    }

    [Fact]
    public async Task W94_SwitchingToTheExpenseSection_AsksForAnAmountAndAPurpose()
    {
        await OpenSessionAsync();

        var cut = Render<W94QuickCapture>(p => p.Add(x => x.Open, true).Add(x => x.Now, Now.ToLocalTime()));

        var segmented = cut.FindComponent<WSegmented>();
        await cut.InvokeAsync(() =>
            segmented.Instance.SelectedValueChanged.InvokeAsync(nameof(QuickCaptureKind.Expense)));

        Assert.Contains(Ar.QuickCapture.ExpenseAmountLabel, cut.Markup);
        Assert.Contains(Ar.QuickCapture.ExpensePurposeLabel, cut.Markup);
        Assert.DoesNotContain(Ar.QuickCapture.TaskDueLabel, cut.Markup);
    }

    [Fact]
    public async Task W10_TheFooterLink_GoesToTheNotificationsListAndNotToTheSettingsScreen()
    {
        await OpenSessionAsync();

        var nav = Services.GetRequiredService<NavigationManager>();
        var cut = Render<W10NotificationPanel>(p => p.Add(x => x.Open, true).Add(x => x.Now, Now));

        // The footer promises the rest of the notifications, so it must not quietly land on the
        // settings screen the gear owns.
        Assert.NotEqual(W10NotificationPanel.SettingsRoute, W10NotificationPanel.AllNotificationsRoute);

        cut.Find(".w10-view-all").Click();
        Assert.EndsWith(W10NotificationPanel.AllNotificationsRoute, nav.Uri, StringComparison.Ordinal);

        cut.Render(p => p.Add(x => x.Open, true).Add(x => x.Now, Now));
        cut.Find(".w10-settings").Click();
        Assert.EndsWith(W10NotificationPanel.SettingsRoute, nav.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task W94_TheTaskForm_CarriesTheDefaultDueDateAndAnOptionalNoteThatIsSavedWithIt()
    {
        await OpenSessionAsync();

        QuickCaptureResult? captured = null;
        var cut = Render<W94QuickCapture>(p => p
            .Add(x => x.Open, true)
            .Add(x => x.Now, Now.ToLocalTime())
            .Add(x => x.OnCaptured, (QuickCaptureResult r) => captured = r));

        var fields = cut.FindComponents<WInput>();

        // العنوان *، تاريخ الاستحقاق *، المكلّف، ملاحظة — four fields, as the export draws them.
        Assert.Equal(4, fields.Count);
        Assert.Equal(Ar.QuickCapture.TaskDueLabel, fields[1].Instance.Label);
        Assert.True(fields[1].Instance.Required);
        Assert.Equal(
            Now.ToLocalTime().Date.AddDays(W94QuickCapture.DefaultTaskDueDays)
                .ToString(W94QuickCapture.DateFormat, CultureInfo.InvariantCulture),
            fields[1].Instance.Value);
        Assert.Equal(Ar.QuickCapture.ExtraNoteLabel, fields[3].Instance.Label);

        await cut.InvokeAsync(() => fields[0].Instance.ValueChanged.InvokeAsync("متابعة تقرير اللجنة Ring-Road"));
        await cut.InvokeAsync(() => cut.FindComponents<WInput>()[3].Instance.ValueChanged
            .InvokeAsync("يُراجَع مع قسم الهندسة قبل الإرسال"));
        await cut.InvokeAsync(() => cut.FindComponent<WDialog>().Instance.OnConfirm.InvokeAsync());

        cut.WaitForAssertion(() => Assert.NotNull(captured));

        var task = Db.Tasks.Single();
        Assert.Equal("متابعة تقرير اللجنة Ring-Road", task.Title);
        Assert.Equal("يُراجَع مع قسم الهندسة قبل الإرسال", task.Description);
        Assert.NotNull(task.DueAt);
    }

    [Fact]
    public async Task TheUndoStrip_DisappearsWithItsButtonOnceTheUndoWindowHasPassed()
    {
        await OpenSessionAsync();

        var cut = Render<MainLayout>(p => p.Add(
            x => x.Body,
            (RenderFragment)(builder =>
            {
                builder.OpenComponent<W08AttentionCenter>(0);
                builder.CloseComponent();
            })));

        // Ctrl+N, then a task with nothing but a title: the dialog fills the due date itself.
        await cut.InvokeAsync(() => Services.GetRequiredService<ShellCommands>().RequestQuickCapture());
        var capture = cut.FindComponent<W94QuickCapture>();
        await cut.InvokeAsync(() => capture.FindComponents<WInput>()[0].Instance.ValueChanged
            .InvokeAsync("متابعة عاجلة"));
        await cut.InvokeAsync(() => capture.FindComponent<WDialog>().Instance.OnConfirm.InvokeAsync());

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".w-undo")));
        Assert.NotEmpty(cut.FindAll(".w-undo-action"));

        // The window the capture granted runs out; the strip goes with it, so «تراجع» is never
        // offered for a token that would only answer «انتهت مهلة التراجع».
        var timer = ((FixedTime)Time).LastTimer;
        Assert.NotNull(timer);
        await cut.InvokeAsync(() => timer!.Fire());

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".w-undo")));
        Assert.DoesNotContain(Ar.QuickCapture.Undo, cut.Markup, StringComparison.Ordinal);
    }
}
