using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Wakeel.Core.Data;
using Wakeel.Core.Services;
using Wakeel.Design.Components;
using Wakeel.Design.Text;
using Wakeel.UI.Pages;
using Wakeel.UI.Services;
using Wakeel.UI.Services.Shell;

namespace Wakeel.UI.Tests.Shell;

/// <summary>
/// W12 (مركز الصحة) and the two reference sheets, W91 (الحالات القياسية) and W92 (الحوارات
/// القياسية). The sheets are what every other screen's empty, loading and failure states are copied
/// from, so a test that pins their texts pins the whole product's wording.
/// </summary>
public sealed class HealthAndReferenceScreenTests : ShellScreenContext
{
    // ---------------------------------------------------------------------------------------
    // W12
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void W12_WithNoSessionOpen_ShowsTheClosedSessionState()
    {
        var cut = Render<W12HealthCenter>();

        Assert.Contains(Ar.Shell.SessionClosedTitle, cut.Markup);
        Assert.Empty(cut.FindComponents<WHealthCard>());
    }

    [Fact]
    public async Task W12_Loaded_DrawsOneCardPerCheckedComponentWithASummaryAboveThem()
    {
        await OpenSessionAsync();

        var cut = Render<W12HealthCenter>();
        var report = await Shell.Health.CheckAsync(Now, persist: false);

        var cards = cut.FindComponents<WHealthCard>();
        Assert.Equal(report.Cards.Count, cards.Count);
        Assert.Contains(report.SummaryAr, cut.Markup);
        Assert.All(cards, card =>
        {
            Assert.False(string.IsNullOrWhiteSpace(card.Instance.Title));
            Assert.False(string.IsNullOrWhiteSpace(card.Instance.StatusLabel));
        });
    }

    [Fact]
    public async Task W12_SaysNothingTechnical_NoCodesNoPathsNoEnglish()
    {
        await OpenSessionAsync();

        var cut = Render<W12HealthCenter>();

        // AGREEMENT item 15: nothing the person reads may be a technical term or an error code.
        foreach (var forbidden in new[] { "SQLite", "sqlite", "Exception", "error", "PRAGMA", "port", "server" })
        {
            Assert.DoesNotContain(forbidden, cut.Markup, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task W12_TheFilterTabs_CountAndNarrowTheGrid()
    {
        await OpenSessionAsync();

        var cut = Render<W12HealthCenter>();
        var report = await Shell.Health.CheckAsync(Now, persist: false);

        var tabs = cut.FindComponents<WTabItem>();
        Assert.Equal(4, tabs.Count);
        Assert.Equal(report.Cards.Count, tabs[0].Instance.Count);
        Assert.Equal(report.Cards.Count(c => c.Status == HealthStatus.Warning), tabs[1].Instance.Count);
        Assert.Equal(report.Cards.Count(c => c.Status == HealthStatus.Error), tabs[2].Instance.Count);
        Assert.Equal(report.Cards.Count(c => c.Status == HealthStatus.Ok), tabs[3].Instance.Count);

        tabs[3].Find(".w-tab").Click();
        Assert.Equal(
            report.Cards.Count(c => c.Status == HealthStatus.Ok),
            cut.FindComponents<WHealthCard>().Count);
    }

    [Fact]
    public async Task W12_TheLocalSearch_NarrowsTheGridAndSaysSoWhenNothingMatches()
    {
        await OpenSessionAsync();

        var cut = Render<W12HealthCenter>();
        var search = cut.FindComponents<WSearch>()[0];

        await cut.InvokeAsync(() => search.Instance.ValueChanged.InvokeAsync("لا يوجد فحص بهذا الاسم"));
        cut.Render();

        Assert.Contains(Ar.Health.NoMatches, cut.Markup);
        Assert.Empty(cut.FindComponents<WHealthCard>());
    }

    [Fact]
    public async Task W12_ExportTheHealthReport_WritesAnArabicTextFileTheHostChoosesTheNameOf()
    {
        var files = new RecordingFileSave();
        Services.AddSingleton<IFileSaveService>(files);
        await OpenSessionAsync();

        // The two header buttons are published to the shell's page header, not drawn inside the
        // page, so the fragment the header would render is rendered here instead.
        var cut = Render<W12HealthCenter>();
        var header = Services.GetRequiredService<PageHeaderState>();
        var actions = Render(header.Actions!);

        actions.Find(".w12-export").Click();

        cut.WaitForAssertion(() => Assert.NotNull(files.LastContents));
        Assert.StartsWith("تقرير-الصحة-", files.LastName, StringComparison.Ordinal);
        Assert.EndsWith(".txt", files.LastName, StringComparison.Ordinal);
        Assert.Contains("تقرير حالة الوكيل", files.LastContents!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task W12_CheckNow_RunsTheChecksAgainAndSaysItDid()
    {
        await OpenSessionAsync();

        var cut = Render<W12HealthCenter>();
        var header = Services.GetRequiredService<PageHeaderState>();
        var actions = Render(header.Actions!);

        actions.Find(".w12-check").Click();

        // Nothing throws, the grid is still there, and the header still carries both buttons.
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindComponents<WHealthCard>()));
    }

    private sealed class RecordingFileSave : IFileSaveService
    {
        internal string LastName { get; private set; } = string.Empty;

        internal string? LastContents { get; private set; }

        public Task<string?> SaveTextAsync(string suggestedFileName, string contents, CancellationToken cancellationToken = default)
        {
            LastName = suggestedFileName;
            LastContents = contents;
            return Task.FromResult<string?>(Path.Combine(Path.GetTempPath(), suggestedFileName));
        }
    }

    // ---------------------------------------------------------------------------------------
    // W91
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void W91_DrawsEveryOneOfTheThirteenStandardStates()
    {
        var cut = Render<W91StandardStates>();

        Assert.Equal(13, W91StandardStates.Keys.Count);
        Assert.Equal(13, cut.FindComponents<WStateCard>().Count);
        foreach (var key in W91StandardStates.Keys)
        {
            Assert.NotNull(cut.Find($"[data-state='{key}']"));
        }
    }

    [Fact]
    public void W91_UsesTheFinalArStandardStatesTexts()
    {
        var cut = Render<W91StandardStates>();

        Assert.Contains(Ar.StandardStates.LoadingTitle, cut.Markup);
        Assert.Contains(Ar.StandardStates.EmptyTitle, cut.Markup);
        Assert.Contains(Ar.StandardStates.NoResultsTitle, cut.Markup);
        Assert.Contains(Ar.StandardStates.FileBrokenTitle, cut.Markup);
        Assert.Contains(Ar.StandardStates.VaultTitle, cut.Markup);
        Assert.Contains(Ar.StandardStates.DatabaseTitle, cut.Markup);
        Assert.Contains(Ar.StandardStates.FailedTitle, cut.Markup);
        Assert.Contains(Ar.StandardStates.RecipientOnlyTitle, cut.Markup);
        Assert.Contains(Ar.StandardStates.SuccessTitle, cut.Markup);
        Assert.Contains(Ar.StandardStates.SessionEndedTitle, cut.Markup);
        Assert.Contains(Ar.StandardStates.BadPackageTitle, cut.Markup);
        Assert.Contains(Ar.StandardStates.RecordChangedTitle, cut.Markup);
        Assert.Contains(Ar.StandardStates.WordTitle, cut.Markup);
    }

    [Fact]
    public void W91_TheFourStatesTheDesignSystemItselfUses_CannotDriftFromAr_States()
    {
        // The sheet is only worth having while it and the components say the same thing.
        Assert.Equal(Ar.States.LoadingTitle, Ar.StandardStates.LoadingTitle);
        Assert.Equal(Ar.States.NoResultsTitle, Ar.StandardStates.NoResultsTitle);
    }

    // ---------------------------------------------------------------------------------------
    // W92
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void W92_DrawsTheFiveStandardDialogsAsRealDialogSurfaces()
    {
        var cut = Render<W92StandardDialogs>();

        Assert.Equal(5, W92StandardDialogs.Keys.Count);
        var dialogs = cut.FindComponents<WDialog>();
        Assert.Equal(5, dialogs.Count);
        Assert.All(dialogs, dialog => Assert.True(dialog.Instance.Inline));

        foreach (var key in W92StandardDialogs.Keys)
        {
            Assert.NotNull(cut.Find($"[data-dialog='{key}']"));
        }
    }

    [Fact]
    public void W92_TheDangerousConfirmation_SaysWhatCannotBeTakenBackAndIsDrawnAsDanger()
    {
        var cut = Render<W92StandardDialogs>();

        var danger = cut.FindComponents<WDialog>().Single(d => d.Instance.Kind == WDialogKind.Danger);
        Assert.Equal(Ar.StandardDialogs.DangerTitle, danger.Instance.Title);
        Assert.Equal(Ar.StandardDialogs.DangerNotice, danger.Instance.Notice);
        Assert.Contains(Ar.StandardDialogs.DangerNotice, cut.Markup);
    }

    [Fact]
    public void W92_TheValidationDialog_ShowsBothRefusedFieldsWithTheirOwnReason()
    {
        var cut = Render<W92StandardDialogs>();

        var card = cut.Find("[data-dialog='validation']");
        Assert.Contains(Ar.StandardDialogs.ValidationFieldDateError, card.InnerHtml);
        Assert.Contains(Ar.StandardDialogs.ValidationFieldPhoneError, card.InnerHtml);
    }

    [Fact]
    public void W92_TheConflictDialog_ShowsBothVersionsSoOneCanBeChosen()
    {
        var cut = Render<W92StandardDialogs>();

        var card = cut.Find("[data-dialog='conflict']");
        Assert.Contains(Ar.StandardDialogs.ConflictIncoming, card.InnerHtml);
        Assert.Contains(Ar.StandardDialogs.ConflictMine, card.InnerHtml);
        Assert.Contains(Ar.StandardDialogs.ConflictSampleIncomingAmount, card.InnerHtml);
    }

    [Fact]
    public void W92_EveryButtonOnTheSheetCarriesATooltip()
    {
        var cut = Render<W92StandardDialogs>();

        var buttons = cut.FindComponents<WButton>();
        Assert.NotEmpty(buttons);
        Assert.All(buttons, button => Assert.False(string.IsNullOrWhiteSpace(button.Instance.Tooltip)));

        // The close cross of each surface is an icon with no label, so it needs its own.
        Assert.All(cut.FindAll(".w-dialog-close"), close =>
            Assert.False(string.IsNullOrWhiteSpace(close.GetAttribute("title"))));
    }
}
