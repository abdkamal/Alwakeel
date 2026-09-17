using Bunit;
using Microsoft.AspNetCore.Components;
using Wakeel.Design.Components;
using Wakeel.Design.Text;

namespace Wakeel.UI.Tests.Components;

/// <summary>
/// The pieces package B2 added to the design system so the daily screens could be composed out of
/// it instead of hand-rolling: a KPI card that leads somewhere, a segmented strip that carries
/// icons, the notification panel and its rows, and the dialog's inline surface and notice strip.
/// </summary>
public sealed class DailyShellComponentTests : WakeelTestContext
{
    // -------------------------------------------------------------------------------------------
    // WKpiCard
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void WKpiCard_WithoutAnOnClick_IsPlainAndNotFocusable()
    {
        var cut = Render<WKpiCard>(p => p
            .Add(x => x.Label, "متأخر")
            .Add(x => x.Icon, "alert-triangle")
            .Add(x => x.Value, "12"));

        Assert.Empty(cut.FindAll("button"));
        Assert.NotNull(cut.Find(".w-kpi"));
    }

    [Fact]
    public void WKpiCard_WithAnOnClick_IsAButtonThatCarriesItsTooltipAndRaisesTheClick()
    {
        var clicks = 0;
        var cut = Render<WKpiCard>(p => p
            .Add(x => x.Label, "متأخر")
            .Add(x => x.Icon, "alert-triangle")
            .Add(x => x.Value, "12")
            .Add(x => x.Tooltip, "فتح قائمة «متأخر»")
            .Add(x => x.Variant, WSemanticVariant.Danger)
            .Add(x => x.OnClick, EventCallback.Factory.Create(this, () => clicks++)));

        var button = cut.Find("button.w-kpi--action");
        Assert.Equal("فتح قائمة «متأخر»", button.GetAttribute("title"));
        Assert.NotNull(cut.Find(".w-kpi-value--danger"));

        button.Click();
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void WKpiCard_IsolatesItsNumber_SoLatinDigitsStayPutInAnArabicSentence()
    {
        var cut = Render<WKpiCard>(p => p
            .Add(x => x.Label, "قرب الاستحقاق")
            .Add(x => x.Icon, "calendar")
            .Add(x => x.Value, "8"));

        Assert.Equal("8", cut.Find("bdi.w-kpi-value").TextContent);
    }

    // -------------------------------------------------------------------------------------------
    // WSegmented
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void WSegmented_Stacked_DrawsAnIconPerOptionAndMarksTheSelectedOne()
    {
        var picked = string.Empty;
        var cut = Render<WSegmented>(p => p
            .Add(x => x.Options, [("task", "مهمة"), ("note", "ملاحظة")])
            .Add(x => x.Icons, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["task"] = "check-square",
                ["note"] = "file-text",
            })
            .Add(x => x.Stacked, true)
            .Add(x => x.SelectedValue, "task")
            .Add(x => x.SelectedValueChanged, EventCallback.Factory.Create<string>(this, v => picked = v)));

        Assert.NotNull(cut.Find(".w-segmented--stacked"));
        Assert.Equal(2, cut.FindAll(".w-segmented-icon").Count);

        var items = cut.FindAll(".w-segmented-item");
        Assert.Equal("true", items[0].GetAttribute("aria-selected"));
        Assert.Equal("مهمة", items[0].GetAttribute("title"));

        items[1].Click();
        Assert.Equal("note", picked);
    }

    [Fact]
    public void WSegmented_WithoutIcons_StaysThePlainTextStripEveryOtherScreenUses()
    {
        var cut = Render<WSegmented>(p => p
            .Add(x => x.Options, [("light", "فاتح"), ("dark", "داكن")])
            .Add(x => x.SelectedValue, "light"));

        Assert.Empty(cut.FindAll(".w-segmented-icon"));
        Assert.Empty(cut.FindAll(".w-segmented--stacked"));
    }

    // -------------------------------------------------------------------------------------------
    // WNotificationPanel / WNotificationItem
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void WNotificationPanel_Closed_DrawsNothing()
    {
        var cut = Render<WNotificationPanel>(p => p.Add(x => x.Open, false));

        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public void WNotificationPanel_Open_ShowsItsTitleCountAndEverySlot_AndClosesOnTheScrim()
    {
        var closes = 0;
        var cut = Render<WNotificationPanel>(p => p
            .Add(x => x.Open, true)
            .Add(x => x.Title, Ar.Notifications.Title)
            .Add(x => x.Count, 4)
            .Add(x => x.HeadActions, (RenderFragment)(b => b.AddMarkupContent(0, "<span class='head'>h</span>")))
            .Add(x => x.Tabs, (RenderFragment)(b => b.AddMarkupContent(0, "<span class='tabs'>t</span>")))
            .Add(x => x.ChildContent, (RenderFragment)(b => b.AddMarkupContent(0, "<span class='list'>l</span>")))
            .Add(x => x.FooterContent, (RenderFragment)(b => b.AddMarkupContent(0, "<span class='foot'>f</span>")))
            .Add(x => x.OnClose, EventCallback.Factory.Create(this, () => closes++)));

        Assert.Contains(Ar.Notifications.Title, cut.Find(".w-notification-panel-title").TextContent);
        Assert.Equal("4", cut.Find(".w-notification-panel-title .w-badge").TextContent);
        Assert.NotNull(cut.Find(".head"));
        Assert.NotNull(cut.Find(".tabs"));
        Assert.NotNull(cut.Find(".list"));
        Assert.NotNull(cut.Find(".foot"));

        cut.Find(".w-notification-panel-scrim").Click();
        Assert.Equal(1, closes);
    }

    [Fact]
    public void WNotificationItem_Unread_CarriesTheDotTheKindTheTimeAndOpensOnClick()
    {
        var clicks = 0;
        var cut = Render<WNotificationItem>(p => p
            .Add(x => x.Icon, "users")
            .Add(x => x.KindLabel, "اجتماع")
            .Add(x => x.RelativeTime, "قبل 10 دقائق")
            .Add(x => x.Unread, true)
            .Add(x => x.Tooltip, "فتح «اجتماع اللجنة الفنية»")
            .Add(x => x.Title, (RenderFragment)(b => b.AddContent(0, "اجتماع اللجنة الفنية")))
            .Add(x => x.Body, (RenderFragment)(b => b.AddContent(0, "قاعة الاجتماعات")))
            .Add(x => x.OnClick, EventCallback.Factory.Create(this, () => clicks++)));

        var row = cut.Find("button.w-notification");
        Assert.Contains("w-notification--unread", row.ClassName);
        Assert.Equal("فتح «اجتماع اللجنة الفنية»", row.GetAttribute("title"));
        Assert.Equal("اجتماع", cut.Find(".w-notification-kind").TextContent);
        Assert.Equal("قبل 10 دقائق", cut.Find("bdi.w-notification-time").TextContent);
        Assert.Contains("اجتماع اللجنة الفنية", cut.Find(".w-notification-title").TextContent);
        Assert.Contains("قاعة الاجتماعات", cut.Find(".w-notification-body").TextContent);

        row.Click();
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void WNotificationItem_Read_DropsTheUnreadDotAndTheHeavierTitle()
    {
        var cut = Render<WNotificationItem>(p => p
            .Add(x => x.Icon, "bell")
            .Add(x => x.KindLabel, "تنبيه")
            .Add(x => x.RelativeTime, "أمس 16:40")
            .Add(x => x.Title, (RenderFragment)(b => b.AddContent(0, "تنبيه قديم"))));

        Assert.DoesNotContain("w-notification--unread", cut.Find("button").ClassName);
        Assert.Contains("w-notification-dot--read", cut.Find(".w-notification-dot").ClassName);
    }

    [Fact]
    public void WNotificationItem_AWarningRow_CarriesItsTintOnTheWholeRow()
    {
        var cut = Render<WNotificationItem>(p => p
            .Add(x => x.Icon, "bar-chart-3")
            .Add(x => x.KindLabel, "الدورة المالية")
            .Add(x => x.RelativeTime, "قبل ساعة")
            .Add(x => x.Variant, WSemanticVariant.Warning)
            .Add(x => x.Title, (RenderFragment)(b => b.AddContent(0, "بقي 3 أيام"))));

        Assert.Contains("w-notification--warning", cut.Find("button").ClassName);
    }

    [Fact]
    public void WNotificationItem_DrawsTheKindsIconOnTheStartEdgeAndTheUnreadDotOnTheEndEdge()
    {
        // The export puts the icon square on the row's start edge and the unread dot on its end
        // edge; the row was built the other way round, which mirrored every row of the panel.
        var cut = Render<WNotificationItem>(p => p
            .Add(x => x.Icon, "mail")
            .Add(x => x.KindLabel, "مراسلة واردة")
            .Add(x => x.RelativeTime, "قبل 5 دقائق")
            .Add(x => x.Unread, true)
            .Add(x => x.Title, (RenderFragment)(b => b.AddContent(0, "طلب تزويد بيانات"))));

        var children = cut.Find("button.w-notification").Children;
        Assert.Equal(3, children.Length);
        Assert.Contains("w-notification-icon", children[0].ClassName);
        Assert.Contains("w-notification-text", children[1].ClassName);
        Assert.Contains("w-notification-dot", children[2].ClassName);
    }

    // -------------------------------------------------------------------------------------------
    // WHealthCard: the footer of W12's tiles
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void WHealthCard_PutsTheRunsTimeOnTheStartEdge_WhetherOrNotTheTileCanBeActedOn()
    {
        // With the action rendered first, a tile that had one pushed its timestamp to the opposite
        // edge from a tile that had none, so the grid's timestamps zig-zagged from column to column.
        var withAction = Render<WHealthCard>(p => p
            .Add(x => x.Title, "الخزنة")
            .Add(x => x.StatusLabel, "عطل")
            .Add(x => x.Variant, WSemanticVariant.Danger)
            .Add(x => x.ActionLabel, "استعادة نسخة")
            .Add(x => x.ActionTooltip, "استعادة آخر نسخة سليمة")
            .Add(x => x.FooterText, "آخر فحص 10:24"));

        var footer = withAction.Find(".w-health-foot").Children;
        Assert.Contains("w-health-checked", footer[0].ClassName);
        Assert.Contains("استعادة نسخة", footer[^1].TextContent);

        var withoutAction = Render<WHealthCard>(p => p
            .Add(x => x.Title, "قاعدة البيانات")
            .Add(x => x.StatusLabel, "سليم")
            .Add(x => x.FooterText, "آخر فحص 10:24"));

        Assert.Contains("w-health-checked", withoutAction.Find(".w-health-foot").Children[0].ClassName);
    }

    [Fact]
    public void WHealthCard_LeadsWithTheIsolatedTitleAndHangsTheStatusChipOffTheEndEdge()
    {
        // The export draws icon+title on every tile's start edge and the status chip on its end
        // edge; the head was emitted the other way round, which mirrored all eleven tiles of W12.
        var cut = Render<WHealthCard>(p => p
            .Add(x => x.Title, "Word 2019")
            .Add(x => x.Icon, "file-text")
            .Add(x => x.StatusLabel, "سليم")
            .Add(x => x.FooterText, "آخر فحص 10:24"));

        var head = cut.Find(".w-health-head").Children;
        Assert.Equal(2, head.Length);
        Assert.Contains("w-health-title", head[0].ClassName);
        Assert.Contains("w-chip", head[1].ClassName);

        // Inside the title the icon leads the name, and the name is bidi-isolated because the
        // component names HealthService supplies are Latin runs inside an Arabic screen.
        var title = head[0].Children;
        Assert.Contains("w-health-icon", title[0].ClassName);
        var text = title[^1];
        Assert.Equal("BDI", text.TagName);
        Assert.Contains("w-health-title-text", text.ClassName);
        Assert.Equal("Word 2019", text.TextContent);
    }

    // -------------------------------------------------------------------------------------------
    // WDialog: the inline surface and the notice strip
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void WDialog_Inline_DrawsTheSurfaceInThePageWithNoScrimAndNoModality()
    {
        var cut = Render<WDialog>(p => p
            .Add(x => x.Inline, true)
            .Add(x => x.Title, Ar.StandardDialogs.NormalTitle)
            .Add(x => x.Desc, Ar.StandardDialogs.NormalDesc));

        Assert.Empty(cut.FindAll(".w-dialog-scrim"));
        var surface = cut.Find(".w-dialog--inline");
        Assert.Null(surface.GetAttribute("aria-modal"));
        Assert.Contains(Ar.StandardDialogs.NormalTitle, surface.TextContent);
    }

    [Fact]
    public void WDialog_ANotice_IsRenderedUnderTheDescription()
    {
        var cut = Render<WDialog>(p => p
            .Add(x => x.Inline, true)
            .Add(x => x.Title, Ar.StandardDialogs.DangerTitle)
            .Add(x => x.Desc, Ar.StandardDialogs.DangerDesc)
            .Add(x => x.Notice, Ar.StandardDialogs.DangerNotice));

        Assert.Equal(Ar.StandardDialogs.DangerNotice, cut.Find(".w-dialog-notice").TextContent);
    }

    [Fact]
    public void WDialog_MaxWidthPx_WidensTheSurfaceForAFormThatNeedsTwoColumns()
    {
        var cut = Render<WDialog>(p => p
            .Add(x => x.Open, true)
            .Add(x => x.Title, Ar.QuickCapture.Title)
            .Add(x => x.MaxWidthPx, 560));

        Assert.Equal("max-width:560px", cut.Find(".w-dialog").GetAttribute("style"));
    }

    [Fact]
    public void WDialog_TheCloseCross_CarriesBothAnAccessibleNameAndATooltip()
    {
        var cut = Render<WDialog>(p => p
            .Add(x => x.Inline, true)
            .Add(x => x.Title, Ar.StandardDialogs.NormalTitle));

        var close = cut.Find(".w-dialog-close");
        Assert.Equal(Ar.Buttons.Close, close.GetAttribute("aria-label"));
        Assert.Equal(Ar.Buttons.Close, close.GetAttribute("title"));
    }

    // -------------------------------------------------------------------------------------------
    // WUndoToast — the toast-with-action an immediate save raises (AGREEMENT item 32).
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void WUndoToast_WithNoText_DrawsNothing()
    {
        var cut = Render<WUndoToast>();

        Assert.Empty(cut.FindAll(".w-undo"));
    }

    [Fact]
    public void WUndoToast_ShowsWhatWasSaved_AndOffersAnUndoThatCarriesItsTooltip()
    {
        var undone = 0;
        var cut = Render<WUndoToast>(p => p
            .Add(x => x.Text, Ar.Shell.GalleryUndoText)
            .Add(x => x.CanUndo, true)
            .Add(x => x.OnUndo, EventCallback.Factory.Create(this, () => undone++)));

        Assert.Equal(Ar.Shell.GalleryUndoText, cut.Find(".w-undo-text").TextContent);

        var undo = cut.Find(".w-undo-action");
        Assert.Contains(Ar.QuickCapture.Undo, undo.TextContent, StringComparison.Ordinal);
        Assert.Equal(Ar.QuickCapture.UndoTooltip, TooltipOf(undo));

        undo.Click();
        Assert.Equal(1, undone);
    }

    [Fact]
    public void WUndoToast_WhenTheWindowHasPassed_KeepsTheSentenceButTakesTheUndoAway()
    {
        var closed = 0;
        var cut = Render<WUndoToast>(p => p
            .Add(x => x.Text, Ar.Shell.GalleryUndoText)
            .Add(x => x.CanUndo, false)
            .Add(x => x.OnClose, EventCallback.Factory.Create(this, () => closed++)));

        Assert.Empty(cut.FindAll(".w-undo-action"));

        var close = cut.Find(".w-undo-close");
        Assert.Equal(Ar.Buttons.Close, TooltipOf(close));

        close.Click();
        Assert.Equal(1, closed);
    }

    // -------------------------------------------------------------------------------------------
    // WStateCard — the tone of a state.
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void WStateCard_ByDefault_DrawsTheNeutralIconCircle()
    {
        var cut = Render<WStateCard>(p => p
            .Add(x => x.Icon, "inbox")
            .Add(x => x.Title, Ar.States.NoDataTitle));

        Assert.NotNull(cut.Find(".w-state-icon--neutral"));
    }

    [Theory]
    [InlineData(WSemanticVariant.Danger, "w-state-icon--danger")]
    [InlineData(WSemanticVariant.Warning, "w-state-icon--warning")]
    [InlineData(WSemanticVariant.Success, "w-state-icon--success")]
    public void WStateCard_AVariant_TintsTheIconSoTheStateSaysItsOwnTone(WSemanticVariant variant, string expected)
    {
        var cut = Render<WStateCard>(p => p
            .Add(x => x.Icon, "alert-circle")
            .Add(x => x.Title, Ar.StandardStates.FailedTitle)
            .Add(x => x.Variant, variant));

        Assert.NotNull(cut.Find($".{expected}"));
    }

    // -------------------------------------------------------------------------------------------
    // WDialog — the confirm/cancel tooltips W92 asks for.
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void WDialog_TheActionTooltips_DefaultToTheLabelsAndFollowThemWhenGiven()
    {
        var cut = Render<WDialog>(p => p
            .Add(x => x.Inline, true)
            .Add(x => x.Title, Ar.StandardDialogs.NormalTitle)
            .Add(x => x.ConfirmLabel, Ar.StandardDialogs.NormalConfirm)
            .Add(x => x.CancelLabel, Ar.Buttons.Cancel));

        var buttons = cut.FindAll(".w-dialog-actions button");
        Assert.Equal(Ar.StandardDialogs.NormalConfirm, TooltipOf(buttons[0]));
        Assert.Equal(Ar.Buttons.Cancel, TooltipOf(buttons[1]));

        cut.Render(p => p
            .Add(x => x.Inline, true)
            .Add(x => x.Title, Ar.StandardDialogs.DangerTitle)
            .Add(x => x.ConfirmLabel, Ar.StandardDialogs.DangerConfirm)
            .Add(x => x.ConfirmTooltip, Ar.StandardDialogs.DangerConfirmTooltip)
            .Add(x => x.CancelLabel, Ar.Buttons.Cancel)
            .Add(x => x.CancelTooltip, Ar.StandardDialogs.ConflictKeepMineTooltip));

        buttons = cut.FindAll(".w-dialog-actions button");
        Assert.Equal(Ar.StandardDialogs.DangerConfirmTooltip, TooltipOf(buttons[0]));
        Assert.Equal(Ar.StandardDialogs.ConflictKeepMineTooltip, TooltipOf(buttons[1]));
    }

    /// <summary>
    /// The sentence a button's tooltip shows. WButton draws it as the design system's own bubble
    /// (WTooltip) rather than as a browser title, so the bubble beside the button is where it is.
    /// </summary>
    private static string TooltipOf(AngleSharp.Dom.IElement button)
    {
        var wrap = button.Closest(".w-tooltip-wrap");
        Assert.NotNull(wrap);
        return wrap!.QuerySelector(".w-tooltip-bubble")!.TextContent.Trim();
    }

    // -------------------------------------------------------------------------------------------
    // The scoped stylesheets themselves. bUnit renders markup, never CSS, so these two checks are
    // the only place in the suite that can catch a rule the browser silently drops, or a strip whose
    // text and ground resolve to the same colour in one of the two themes.
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void ScopedStylesheets_NeverUseTheFunctionalDeepForm_WhichEveryBrowserDiscards()
    {
        var offenders = ScopedStylesheets()
            .Where(file => File.ReadAllText(file).Contains("::deep(", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void TheDialogsConsequenceStrip_InvertsWithTheTheme_InsteadOfPinningItsTextToWhite()
    {
        var css = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "Wakeel.Design", "Components", "WDialog.razor.css"));

        var start = css.IndexOf(".w-dialog-notice", StringComparison.Ordinal);
        Assert.True(start >= 0, "The dialog's consequence strip has no rule of its own.");
        var block = css[start..css.IndexOf('}', start)];

        var background = TokenOf(block, "background");
        var colour = TokenOf(block, "color");

        // A fixed white would be invisible in the dark theme, where --w-text is nearly white.
        Assert.DoesNotContain("--w-white", colour, StringComparison.Ordinal);
        Assert.NotEqual(background, colour);
    }

    [Fact]
    public void TheRestingTooltip_LeavesTheLayout_SoItCannotScrollTheTableItSitsIn()
    {
        // A bubble that is only `visibility:hidden` still counts towards the scrollable overflow of
        // every ancestor that scrolls, which drew a permanent horizontal scrollbar under the tables
        // of W08 and W09 and a stray strip of scroll inside every header with a tooltipped button.
        var css = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "Wakeel.Design", "Components", "WTooltip.razor.css"));

        var start = css.IndexOf(".w-tooltip-bubble", StringComparison.Ordinal);
        Assert.True(start >= 0, "The tooltip bubble has no rule of its own.");
        var block = css[start..css.IndexOf('}', start)];

        Assert.Equal("none", TokenOf(block, "display"));
        Assert.DoesNotContain("visibility", block, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSelectsCaret_HangsOffTheFieldsEndEdge_WhereTheControlReservesItsPadding()
    {
        // Pinned to the start edge it sat underneath the right-aligned Arabic value: invisible, and
        // overlapping the text, while the 34px of reserved room stayed empty on the other side.
        var css = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "Wakeel.Design", "Components", "WSelect.razor.css"));

        var start = css.IndexOf(".w-select-caret", StringComparison.Ordinal);
        Assert.True(start >= 0, "The select's caret has no rule of its own.");
        var block = css[start..css.IndexOf('}', start)];

        Assert.Contains("inset-inline-end", block, StringComparison.Ordinal);
        Assert.DoesNotContain("inset-inline-start", block, StringComparison.Ordinal);
    }

    [Fact]
    public void ANamedMenu_DrawsAPillWithItsIconWordsAndATooltip_InsteadOfTheAnonymousThreeDots()
    {
        var items = new[] { new WMenuItem("كل الأنواع", "list", EventCallback.Empty) };

        var cut = Render<WMenu>(p => p
            .Add(x => x.Items, items)
            .Add(x => x.Label, Ar.Overdue.Filter)
            .Add(x => x.Icon, "filter")
            .Add(x => x.Tooltip, Ar.Overdue.FilterTooltip));

        var trigger = cut.Find("button.w-menu-trigger");
        Assert.Contains("w-menu-trigger--pill", trigger.ClassList);
        Assert.Contains(Ar.Overdue.Filter, trigger.TextContent, StringComparison.Ordinal);
        Assert.Contains(Ar.Overdue.FilterTooltip, cut.Markup, StringComparison.Ordinal);

        // It is still a menu: the pill opens the same list the three-dot trigger would.
        trigger.Click();
        Assert.Contains("كل الأنواع", cut.Find(".w-menu-popover").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNamedMenusPill_WearsTheSameShapeAsASecondaryButton_SoAControlStripReadsAsOneRow()
    {
        var css = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "Wakeel.Design", "Components", "WMenu.razor.css"));

        var start = css.IndexOf(".w-menu-trigger--pill {", StringComparison.Ordinal);
        Assert.True(start >= 0, "The named menu trigger has no rule of its own.");
        var block = css[start..css.IndexOf('}', start)];

        Assert.Equal("var(--w-radius-pill)", TokenOf(block, "border-radius"));
        Assert.Equal("38px", TokenOf(block, "height"));
    }

    private static string TokenOf(string block, string property)
    {
        var at = block.IndexOf(property + ":", StringComparison.Ordinal);
        Assert.True(at >= 0, $"The strip declares no {property}.");
        var end = block.IndexOf(';', at);
        return block[(at + property.Length + 1)..end].Trim();
    }

    private static IEnumerable<string> ScopedStylesheets() =>
        new[] { "Wakeel.Design", "Wakeel.UI" }
            .Select(project => Path.Combine(RepositoryRoot(), "src", project))
            .SelectMany(root => Directory.EnumerateFiles(root, "*.razor.css", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>The working tree this test assembly was built from, found by its solution file.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Wakeel.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
