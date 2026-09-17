using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using Wakeel.Core.Services;
using Wakeel.Design.Components;
using Wakeel.Design.Text;
using Wakeel.UI.Pages;
using Wakeel.UI.Services;
using Wakeel.UI.Services.Shell;

namespace Wakeel.UI.Tests.Shell;

/// <summary>
/// W08 (مركز الانتباه) and W09 (المتأخر) against a real, open session: the three states each screen
/// has (closed/loading-then-loaded, empty, failed-read is the same WStateCard as closed), the live
/// numbers, the local search, and the two links out — a KPI card into W09's tab and a row into the
/// record it names.
/// </summary>
public sealed class AttentionScreenTests : ShellScreenContext
{
    private static readonly string LateTitle = "طلب تزويد بيانات مشروع الطريق الدائري Ring-Road";
    private const string NearTitle = "الرد على استفسار وزارة الحكم المحلي";
    private const string StaleTitle = "متابعة اتفاقية الصرف الصحي";

    private void SeedMixedWork()
    {
        AddWithStamps(
            OpenTask(LateTitle, due: Now.AddDays(-2), assignee: "سامر أبو غزالة"),
            OpenTask(NearTitle, due: Now.AddDays(1), assignee: "ليلى الشريف"),
            OpenTask(StaleTitle, due: null, updated: Now.AddDays(-30), assignee: "أحمد الخطيب"),
            OpenTask("مهمة مستقرة", due: Now.AddDays(90)),
            Meeting("لجنة المشتريات — عطاء صيانة المركبات", Now.AddHours(2), "قاعة الاجتماعات"),
            PendingExpense("اتصالات دولية — مناقصة الطريق", 4250, "فاتورة جوال 0599-482-117"));
    }

    // ---------------------------------------------------------------------------------------
    // W08
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void W08_WithNoSessionOpen_ShowsTheClosedSessionStateAndNothingElse()
    {
        var cut = Render<W08AttentionCenter>();

        Assert.Contains(Ar.Shell.SessionClosedTitle, cut.Markup);
        Assert.Empty(cut.FindComponents<WKpiCard>());
        Assert.Empty(cut.FindComponents<WTable>());
    }

    [Fact]
    public async Task W08_WithAnEmptyOffice_ShowsTheFourZeroKpisAndTheNothingToDoState()
    {
        await OpenSessionAsync();

        var cut = Render<W08AttentionCenter>();

        var kpis = cut.FindComponents<WKpiCard>();
        Assert.Equal(4, kpis.Count);
        Assert.All(kpis, kpi => Assert.Equal("0", kpi.Instance.Value));
        Assert.Contains(Ar.Attention.NothingToday, cut.Markup);
        Assert.Contains(Ar.Attention.NoExpenses, cut.Markup);
        Assert.Contains(Ar.Attention.NoMeetings, cut.Markup);
    }

    [Fact]
    public async Task W08_Loaded_CountsTheFourBucketsAndListsTodaysWorkWithItsTypeAndAssignee()
    {
        await OpenSessionAsync();
        SeedMixedWork();

        var cut = Render<W08AttentionCenter>();

        var kpis = cut.FindComponents<WKpiCard>();
        Assert.Equal(Ar.AttentionCenter.OverdueLabel, kpis[0].Instance.Label);
        Assert.Equal("1", kpis[0].Instance.Value);
        Assert.Equal(WSemanticVariant.Danger, kpis[0].Instance.Variant);
        Assert.Equal("1", kpis[1].Instance.Value);
        Assert.Equal(WSemanticVariant.Warning, kpis[1].Instance.Variant);
        Assert.Equal("1", kpis[2].Instance.Value);
        Assert.Equal("1", kpis[3].Instance.Value);

        // The mixed row carries its type chip, its subject (Latin token isolated) and its assignee.
        Assert.Contains("Ring-Road", cut.Markup);
        Assert.Contains("سامر أبو غزالة", cut.Markup);
        Assert.NotEmpty(cut.FindComponents<WChip>());

        // A late row is drawn with the attention edge; a near one is not.
        Assert.Contains("w-table-row--attention", cut.Markup);

        // Today's meeting and the pending expense are both on screen.
        Assert.Contains("لجنة المشتريات", cut.Markup);
        Assert.Contains("اتصالات دولية", cut.Markup);
    }

    [Fact]
    public async Task W08_EveryKpiCardIsAButtonWithATooltip_ThatOpensW09OnItsOwnTab()
    {
        await OpenSessionAsync();
        SeedMixedWork();

        var nav = Services.GetRequiredService<NavigationManager>();
        var cut = Render<W08AttentionCenter>();

        var cards = cut.FindAll(".w-kpi--action");
        Assert.Equal(4, cards.Count);
        Assert.All(cards, card => Assert.False(string.IsNullOrWhiteSpace(card.GetAttribute("title"))));

        cards[0].Click();
        Assert.EndsWith($"/w09?tab={BadgeTabs.Late}", nav.Uri, StringComparison.Ordinal);

        cut.FindAll(".w-kpi--action")[2].Click();
        Assert.EndsWith($"/w09?tab={BadgeTabs.Stale}", nav.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task W08_TheRowsOpenButton_OpensTheRecordItNamesAndCarriesATooltip()
    {
        await OpenSessionAsync();
        SeedMixedWork();

        var taskId = Db.Tasks.Single(t => t.Title == LateTitle).Id;
        var nav = Services.GetRequiredService<NavigationManager>();
        var cut = Render<W08AttentionCenter>();

        // The row itself is not a click target — one that cannot be reached from the keyboard is
        // not a target at all — so the way into the record is the button in the last cell, which is
        // the same interaction W09 teaches.
        Assert.Empty(cut.FindAll("tbody tr[onclick]"));

        var open = cut.FindAll("tbody tr .w08-open")[0];
        Assert.False(string.IsNullOrWhiteSpace(
            open.Closest(".w-tooltip-wrap")?.QuerySelector(".w-tooltip-bubble")?.TextContent));

        open.Click();

        Assert.EndsWith($"/w29/{taskId}", nav.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task W08_TheLocalSearch_NarrowsTheTodayListAndSaysSoWhenNothingMatches()
    {
        await OpenSessionAsync();
        SeedMixedWork();

        var cut = Render<W08AttentionCenter>();
        var search = cut.FindComponents<WSearch>()[0];

        await cut.InvokeAsync(() => search.Instance.ValueChanged.InvokeAsync("الحكم المحلي"));
        cut.Render();
        Assert.Contains(NearTitle, cut.Markup);
        Assert.DoesNotContain("Ring-Road", cut.Markup);

        await cut.InvokeAsync(() => search.Instance.ValueChanged.InvokeAsync("لا شيء يطابق هذا"));
        cut.Render();
        Assert.Contains(Ar.States.NoResultsTitle, cut.Markup);
    }

    [Fact]
    public async Task W08_TheTypeTabs_CountAndFilterTheTodayList()
    {
        await OpenSessionAsync();
        SeedMixedWork();

        var cut = Render<W08AttentionCenter>();

        var tabs = cut.FindComponents<WTabItem>();
        var all = tabs.Single(t => t.Instance.Label == Ar.AttentionCenter.TabAll);
        var tasks = tabs.Single(t => t.Instance.Label == Ar.AttentionCenter.TabTasks);
        var correspondence = tabs.Single(t => t.Instance.Label == Ar.AttentionCenter.TabCorrespondence);

        // Four tabs, as W08 draws them. «الكل» counts exactly what the sub-tabs can reach: the
        // phone expense awaiting confirmation is NOT in this list, because no tab could reach it
        // and the card below already carries it with «تأكيد» / «رفض» — so the counts add up.
        Assert.Equal(4, tabs.Count);
        Assert.Equal(3, all.Instance.Count);
        Assert.Equal(3, tasks.Instance.Count);
        Assert.Equal(0, correspondence.Instance.Count);
        Assert.Equal(3, cut.FindAll(".w08-open").Count);

        correspondence.Find(".w-tab").Click();
        Assert.Contains(Ar.Attention.NothingToday, cut.Markup);
    }

    [Fact]
    public async Task W08_ConfirmingAPhoneExpense_TakesItOffTheListAndSaysSo()
    {
        await OpenSessionAsync();
        SeedMixedWork();

        var cut = Render<W08AttentionCenter>();
        Assert.Contains("اتصالات دولية", cut.Markup);

        cut.Find(".w08-expense-confirm").Click();
        cut.WaitForAssertion(() => Assert.Contains(Ar.Attention.NoExpenses, cut.Markup));

        var expense = Db.PhoneExpenses.Single();
        Assert.Equal(PhoneExpenseStatus.Confirmed, expense.Status);
    }

    [Fact]
    public async Task W08_RejectingAPhoneExpense_MarksItRejectedWithoutBookingIt()
    {
        await OpenSessionAsync();
        SeedMixedWork();

        var cut = Render<W08AttentionCenter>();
        cut.Find(".w08-expense-reject").Click();
        cut.WaitForAssertion(() => Assert.Contains(Ar.Attention.NoExpenses, cut.Markup));

        var expense = Db.PhoneExpenses.Single();
        Assert.Equal(PhoneExpenseStatus.Rejected, expense.Status);
        Assert.Null(expense.TransactionId);
    }

    [Fact]
    public async Task W08_PublishesItsTitleAndTheAttentionNavKey_ToThePageHeader()
    {
        var headerState = new PageHeaderState();
        Services.AddSingleton(headerState);
        await OpenSessionAsync();

        Render<W08AttentionCenter>();

        Assert.Equal(Ar.AttentionCenter.Title, headerState.Title);
        Assert.Equal(WSidebar.Keys.Attention, headerState.NavKey);
        Assert.NotNull(headerState.Actions);
    }

    /// <summary>
    /// Regression for the B0-closeout review's expenses-table finding, carried over to live data:
    /// header and body column counts must agree (5 each — الموظف/البيان/التاريخ/المبلغ plus a
    /// visually-hidden actions label), and the date must be isolated in a bdi (AGREEMENT item 55).
    /// </summary>
    [Fact]
    public async Task W08_TheExpensesTable_HasFiveMatchingHeaderAndBodyColumnsAndIsolatesItsDate()
    {
        await OpenSessionAsync();
        SeedMixedWork();

        var cut = Render<W08AttentionCenter>();
        var table = cut.FindAll(".w-table").Last(t => t.QuerySelector(".w08-expense-meta") is not null);

        var headerCells = table.QuerySelectorAll("thead th");
        Assert.Equal(5, headerCells.Length);
        Assert.Equal(Ar.AttentionCenter.ExpensesColEmployee, headerCells[0].TextContent.Trim());
        Assert.Equal(Ar.AttentionCenter.ExpensesColSubject, headerCells[1].TextContent.Trim());
        Assert.Equal(Ar.AttentionCenter.ExpensesColDate, headerCells[2].TextContent.Trim());
        Assert.Equal(Ar.AttentionCenter.ExpensesColAmount, headerCells[3].TextContent.Trim());
        Assert.Contains(
            Ar.AttentionCenter.ExpensesActionsColLabel,
            headerCells[4].QuerySelector(".w-visually-hidden")!.TextContent);

        var bodyRows = table.QuerySelectorAll("tbody tr");
        Assert.Single(bodyRows);
        Assert.Equal(5, bodyRows[0].QuerySelectorAll("td").Length);
        Assert.NotNull(bodyRows[0].QuerySelectorAll("td")[2].QuerySelector("bdi"));
        Assert.NotNull(bodyRows[0].QuerySelectorAll("td")[3].QuerySelector("bdi"));
    }

    // ---------------------------------------------------------------------------------------
    // W09
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void W09_WithNoSessionOpen_ShowsTheClosedSessionState()
    {
        var cut = Render<W09Overdue>();

        Assert.Contains(Ar.Shell.SessionClosedTitle, cut.Markup);
        Assert.Empty(cut.FindComponents<WTable>());
    }

    [Fact]
    public async Task W09_WithNothingOverdue_ShowsTheEmptyLateState()
    {
        await OpenSessionAsync();

        var cut = Render<W09Overdue>();

        Assert.Contains(Ar.Overdue.EmptyLateTitle, cut.Markup);
        Assert.Empty(cut.FindComponents<WTable>());
    }

    [Fact]
    public async Task W09_Loaded_OpensOnTheLateTabWithBadgesOnAllFour()
    {
        await OpenSessionAsync();
        SeedMixedWork();

        var cut = Render<W09Overdue>();

        var tabs = cut.FindComponents<WTabItem>();
        Assert.Equal(4, tabs.Count);
        Assert.Equal(Ar.Overdue.TabLate, tabs[0].Instance.Label);
        Assert.True(tabs[0].Instance.Active);
        Assert.Equal(1, tabs[0].Instance.Count);
        Assert.Equal(1, tabs[1].Instance.Count);
        Assert.Equal(1, tabs[2].Instance.Count);
        Assert.Equal(1, tabs[3].Instance.Count);

        Assert.Contains("Ring-Road", cut.Markup);
        Assert.DoesNotContain(NearTitle, cut.Markup);
    }

    [Fact]
    public async Task W09_SwitchingTab_ShowsThatBucketOnly()
    {
        await OpenSessionAsync();
        SeedMixedWork();

        var cut = Render<W09Overdue>();
        cut.FindComponents<WTabItem>()[2].Find(".w-tab").Click();

        Assert.Contains(StaleTitle, cut.Markup);
        Assert.DoesNotContain("Ring-Road", cut.Markup);
    }

    [Fact]
    public async Task W09_OpensOnTheTabAKpiCardAsksFor()
    {
        await OpenSessionAsync();
        SeedMixedWork();

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.GetUriWithQueryParameter("tab", BadgeTabs.Near));

        var cut = Render<W09Overdue>();

        Assert.Contains(NearTitle, cut.Markup);
        Assert.True(cut.FindComponents<WTabItem>()[1].Instance.Active);
    }

    [Fact]
    public async Task W09_TheHandleButton_OpensTheRecordOfItsOwnRow()
    {
        await OpenSessionAsync();
        SeedMixedWork();

        var taskId = Db.Tasks.Single(t => t.Title == LateTitle).Id;
        var nav = Services.GetRequiredService<NavigationManager>();
        var cut = Render<W09Overdue>();

        cut.Find("tbody .w09-handle").Click();

        Assert.EndsWith($"/w29/{taskId}", nav.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task W09_TheLocalSearch_NarrowsTheOpenTab()
    {
        await OpenSessionAsync();
        AddWithStamps(
            OpenTask("مهمة متأخرة أولى Alpha", due: Now.AddDays(-2)),
            OpenTask("مهمة متأخرة ثانية", due: Now.AddDays(-4)));

        var cut = Render<W09Overdue>();
        Assert.Equal(2, cut.FindAll("tbody tr").Count);

        var search = cut.FindComponents<WSearch>()[0];
        await cut.InvokeAsync(() => search.Instance.ValueChanged.InvokeAsync("Alpha"));
        cut.Render();

        Assert.Single(cut.FindAll("tbody tr"));
    }

    // ---------------------------------------------------------------------------------------
    // W08's monthly banner and the side column's sync card.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task W08_TheMonthlyBanner_KeepsItsReadinessBarOnItsOwnRowBesideTheSentence()
    {
        await OpenSessionAsync();

        var cut = Render<W08AttentionCenter>();

        // The bar is the banner's trailing figure, not content stacked under its title: a banner
        // that grows a second row pushes the whole screen down.
        var banner = cut.FindComponents<WBanner>()[0];
        Assert.NotNull(banner.Instance.TrailingContent);
        Assert.Null(banner.Instance.BodyContent);
        Assert.Single(cut.FindAll(".w-banner-trailing .w-progress"));
    }

    [Fact]
    public async Task W08_TheSyncCard_CarriesARefreshControlWithATooltipInItsOwnHeader()
    {
        await OpenSessionAsync();

        var cut = Render<W08AttentionCenter>();

        var refresh = cut.FindComponents<WButton>()
            .Single(b => b.Instance.Class == "w08-sync-refresh");
        Assert.Equal(Ar.Attention.SyncRefreshTooltip, refresh.Instance.Tooltip);

        // It re-reads rather than redecorating: pressing it leaves the screen standing.
        cut.Find(".w08-sync-refresh").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindComponents<WKpiCard>()));
    }

    // ---------------------------------------------------------------------------------------
    // Coming back from a lock.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task W08_AfterTheSessionOpensAgain_ItCountsTheOfficeAgainInsteadOfKeepingItsOldSnapshot()
    {
        await OpenSessionAsync();

        var cut = Render<W08AttentionCenter>();
        Assert.Contains(Ar.Attention.NothingToday, cut.Markup);

        // Work arrives while the screen sits under the lock overlay, then the person unlocks.
        AddWithStamps(OpenTask("مهمة وصلت أثناء القفل", due: Now.AddDays(-2)));
        await ReopenSessionAsync();

        cut.WaitForAssertion(() =>
            Assert.Contains("مهمة وصلت أثناء القفل", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public async Task W09_AfterTheSessionOpensAgain_ItReadsTheListAgain()
    {
        await OpenSessionAsync();

        var cut = Render<W09Overdue>();
        Assert.Contains(Ar.Overdue.EmptyLateTitle, cut.Markup);

        AddWithStamps(OpenTask("مهمة وصلت أثناء القفل", due: Now.AddDays(-2)));
        await ReopenSessionAsync();

        cut.WaitForAssertion(() =>
            Assert.Contains("مهمة وصلت أثناء القفل", cut.Markup, StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------------------------
    // W09's second control strip (the export's «تصفية» / «الأكثر تأخرًا أولًا» / summary row).
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task W09_TheTypeFilter_OffersOnlyTheTypesTheOpenTabActuallyHolds()
    {
        await OpenSessionAsync();
        AddWithStamps(
            OpenTask("مهمة متأخرة", due: Now.AddDays(-2)),
            OpenTask("مهمة متأخرة ثانية", due: Now.AddDays(-5)));

        var cut = Render<W09Overdue>();

        var filter = cut.FindComponents<WMenu>()[0];
        Assert.Equal(Ar.Overdue.Filter, filter.Instance.Label);

        // «كل الأنواع» plus the one type the seeded late rows have, and nothing the tab cannot show.
        Assert.Equal(2, filter.Instance.Items.Count);
        Assert.Equal(Ar.Overdue.FilterAllTypes, filter.Instance.Items[0].Label);
    }

    [Fact]
    public async Task W09_TheTypeFilter_NarrowsTheOpenTabToOneType()
    {
        await OpenSessionAsync();
        AddWithStamps(
            OpenTask("مهمة متأخرة", due: Now.AddDays(-2)),
            OpenTask("مهمة متأخرة ثانية", due: Now.AddDays(-5)));

        var cut = Render<W09Overdue>();
        Assert.Equal(2, cut.FindAll("tbody tr").Count);

        var filter = cut.FindComponents<WMenu>()[0];
        var onlyType = filter.Instance.Items[1];
        await cut.InvokeAsync(() => onlyType.OnClick.InvokeAsync());
        cut.Render();

        // The one type the rows have keeps them all, and the pill says what it is filtering by.
        Assert.Equal(2, cut.FindAll("tbody tr").Count);
        Assert.Equal(onlyType.Label, cut.FindComponents<WMenu>()[0].Instance.Label);

        await cut.InvokeAsync(() => filter.Instance.Items[0].OnClick.InvokeAsync());
        cut.Render();

        Assert.Equal(Ar.Overdue.Filter, cut.FindComponents<WMenu>()[0].Instance.Label);
    }

    [Fact]
    public async Task W09_TheSortToggle_PutsTheLongestDelayFirst()
    {
        await OpenSessionAsync();
        AddWithStamps(
            OpenTask("تأخر قصير", due: Now.AddDays(-2)),
            OpenTask("تأخر طويل", due: Now.AddDays(-30)));

        var cut = Render<W09Overdue>();

        var sort = cut.FindComponents<WButton>().Single(b => b.Instance.Class == "w09-sort");
        Assert.Equal(Ar.Overdue.SortMostLate, sort.Instance.Label);
        Assert.Equal(WButtonVariant.Secondary, sort.Instance.Variant);

        cut.Find(".w09-sort").Click();
        cut.Render();

        var firstRow = cut.FindAll("tbody tr")[0].TextContent;
        Assert.Contains("تأخر طويل", firstRow, StringComparison.Ordinal);

        // Pressed, the control says so by filling in — there is no other way to see it is on.
        Assert.Equal(
            WButtonVariant.Primary,
            cut.FindComponents<WButton>().Single(b => b.Instance.Class == "w09-sort").Instance.Variant);
    }

    [Fact]
    public async Task W09_TheSummaryNote_CountsTheLongestDelayAndTheRowsPastTwoWeeks()
    {
        await OpenSessionAsync();
        AddWithStamps(
            OpenTask("تأخر قصير", due: Now.AddDays(-2)),
            OpenTask("تأخر طويل", due: Now.AddDays(-30)));

        var cut = Render<W09Overdue>();

        var summary = cut.Find(".w09-summary").TextContent;
        Assert.Contains("أطول تأخر", summary, StringComparison.Ordinal);
        Assert.Contains("30", summary, StringComparison.Ordinal);
        Assert.Contains("تجاوزت أسبوعين", summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task W09_WithNothingLate_TheSummarySaysSoRatherThanShowingAZero()
    {
        await OpenSessionAsync();
        AddWithStamps(OpenTask("قرب الاستحقاق", due: Now.AddDays(1)));

        var cut = Render<W09Overdue>();
        var tabs = cut.FindComponents<WTabItem>();
        await cut.InvokeAsync(() => tabs[1].Instance.OnClick.InvokeAsync());
        cut.Render();

        Assert.Equal(Ar.Overdue.SummaryNoDelay, cut.Find(".w09-summary").TextContent.Trim());
    }

    [Fact]
    public async Task W09_TheFooter_NamesTheOpenTabsOwnRecordNoun()
    {
        await OpenSessionAsync();
        AddWithStamps(OpenTask("مهمة متأخرة", due: Now.AddDays(-2)));

        var cut = Render<W09Overdue>();

        Assert.Contains(Ar.Overdue.RecordsLate(1, "1"), cut.FindComponents<WTable>()[0].Instance.FooterText);
    }

    [Fact]
    public async Task W09_EveryDelayCell_CarriesItsBucketsSemanticDot()
    {
        await OpenSessionAsync();
        AddWithStamps(OpenTask("مهمة متأخرة", due: Now.AddDays(-2)));

        var cut = Render<W09Overdue>();

        var cell = cut.Find("tbody .w09-late");
        Assert.Contains("w-dot", cell.ClassList);
        Assert.Contains("w-dot--danger", cell.ClassList);
    }

    [Fact]
    public async Task W09_ExportTheList_WritesTheVisibleRowsAndEscapesACellASpreadsheetWouldReadAsAFormula()
    {
        var files = new RecordingListSave();
        Services.AddSingleton<IFileSaveService>(files);
        await OpenSessionAsync();
        AddWithStamps(
            OpenTask("=مهمة تبدأ بعلامة يساوي", due: Now.AddDays(-2)),
            OpenTask("مهمة متأخرة عادية", due: Now.AddDays(-4)));

        var cut = Render<W09Overdue>();
        var header = Services.GetRequiredService<PageHeaderState>();
        var actions = Render(header.Actions!);

        actions.Find(".w09-export").Click();

        cut.WaitForAssertion(() => Assert.NotNull(files.LastContents));
        Assert.StartsWith(Ar.Overdue.TabLate, files.LastName, StringComparison.Ordinal);
        Assert.EndsWith(".txt", files.LastName, StringComparison.Ordinal);

        var contents = files.LastContents!;
        Assert.Contains(Ar.Overdue.ColDaysLate, contents, StringComparison.Ordinal);
        Assert.Contains("مهمة متأخرة عادية", contents, StringComparison.Ordinal);

        // The apostrophe keeps the subject a subject wherever the file is opened.
        Assert.Contains("'=مهمة تبدأ بعلامة يساوي", contents, StringComparison.Ordinal);
    }

    private sealed class RecordingListSave : IFileSaveService
    {
        internal string LastName { get; private set; } = string.Empty;

        internal string? LastContents { get; private set; }

        public Task<string?> SaveTextAsync(string suggestedFileName, string contents, CancellationToken cancellationToken = default)
        {
            LastName = suggestedFileName;
            LastContents = contents;
            return Task.FromResult<string?>(Path.Combine(Path.GetTempPath(), "list.txt"));
        }
    }
}
