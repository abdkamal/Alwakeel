using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using Wakeel.Core.Services;

namespace Wakeel.Core.Tests;

/// <summary>
/// The quick-capture dialog's back end (W94): five kinds of record saved immediately, each with a
/// single-use undo token that soft-deletes what it created (AGREEMENT item 32).
/// </summary>
public sealed class QuickCaptureServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 16, 9, 0, 0, DateTimeKind.Utc);

    private readonly DailyShellWorld _world = new(Now);

    public QuickCaptureServiceTests() => _world.SeedInstallation();

    public void Dispose() => _world.Dispose();

    [Fact]
    public async Task ATaskIsSavedImmediatelyWithAnArabicConfirmation()
    {
        var result = await _world.QuickCapture.CaptureTaskAsync("تجهيز محضر الاجتماع", Now.AddDays(2), TaskPriority.High, "أمين السر");

        Assert.Equal(QuickCaptureKind.Task, result.Kind);
        Assert.Equal("حُفظت المهمة", result.MessageAr);
        Assert.NotEmpty(result.UndoToken);

        var row = await _world.Db.Tasks.SingleAsync();
        Assert.Equal(result.Id, row.Id);
        Assert.Equal("تجهيز محضر الاجتماع", row.Title);
        Assert.Equal(TaskPriority.High, row.Priority);
        Assert.Equal(WorkTaskStatus.Open, row.Status);
        Assert.Equal("أمين السر", row.AssigneeName);
        Assert.Equal(DeviceKind.Pc, row.SourceDeviceKind);
    }

    [Fact]
    public async Task ANoteAndAReportNoteDifferOnlyInBeingFlaggedForTheReport()
    {
        var note = await _world.QuickCapture.CaptureNoteAsync("ملاحظة عابرة");
        var reportNote = await _world.QuickCapture.CaptureReportNoteAsync("يُذكر في التقرير الشهري");

        Assert.Equal("حُفظت الملاحظة", note.MessageAr);
        Assert.Equal("حُفظت الملاحظة للتقرير الشهري", reportNote.MessageAr);

        var plain = await _world.Db.Notes.SingleAsync(n => n.Id == note.Id);
        var forReport = await _world.Db.Notes.SingleAsync(n => n.Id == reportNote.Id);

        Assert.False(plain.ForReport);
        Assert.False(plain.ReportInclude);
        Assert.True(forReport.ForReport);
        Assert.True(forReport.ReportInclude);
    }

    [Fact]
    public async Task AnExpenseIsBookedIntoTheOpenCycleWithItsDoubleEntryLines()
    {
        _world.Db.Categories.Add(new Category { Kind = TransactionKind.Expense, Name = "قرطاسية", Sort = 1 });
        await _world.Db.SaveChangesAsync();

        var result = await _world.QuickCapture.CaptureExpenseAsync(12550, "أوراق وأقلام", "قرطاسية", "من المكتبة");

        Assert.Equal("سُجّل المصروف", result.MessageAr);

        var transaction = await _world.Db.Transactions.SingleAsync();
        Assert.Equal(12550, transaction.Amount);
        Assert.Equal("أوراق وأقلام", transaction.Purpose);
        Assert.Equal(TransactionKind.Expense, transaction.Kind);
        Assert.Equal(DeviceKind.Pc, transaction.Source);
        Assert.NotNull(transaction.CategoryId);
        Assert.NotNull(transaction.CycleId);

        var ledger = await _world.Db.LedgerEntries.Where(l => l.TransactionId == transaction.Id).ToListAsync();
        Assert.Equal(2, ledger.Count);
        Assert.Equal(12550, ledger.Sum(l => l.Debit));
        Assert.Equal(12550, ledger.Sum(l => l.Credit));
    }

    [Fact]
    public async Task AnAppointmentKeepsItsOwnReminderLeadTime()
    {
        var result = await _world.QuickCapture.CaptureAppointmentAsync("زيارة البلدية", Now.AddDays(1), reminderMinutes: 60);

        var row = await _world.Db.Appointments.SingleAsync();
        Assert.Equal(result.Id, row.Id);
        Assert.Equal("زيارة البلدية", row.Title);
        Assert.Equal(60, row.ReminderMinutes);
        Assert.Equal(AppointmentStatus.Planned, row.Status);
        Assert.Equal("حُفظ الموعد", result.MessageAr);
    }

    [Fact]
    public async Task UndoRemovesTheRecordFromEveryListWithoutDeletingItFromTheDatabase()
    {
        var result = await _world.QuickCapture.CaptureTaskAsync("مهمة عن طريق الخطأ");

        Assert.True(await _world.QuickCapture.UndoAsync(result.UndoToken));

        // Gone from every query that goes through the soft-delete filter...
        Assert.Equal(0, await _world.Db.Tasks.CountAsync());

        // ...but the row is still there, as DATA-MODEL.md §0 requires for an official record.
        var row = await _world.Db.Tasks.IgnoreDeleted().SingleAsync(t => t.Id == result.Id);
        Assert.NotNull(row.DeletedAt);
    }

    [Fact]
    public async Task UndoingAnExpenseAlsoRemovesItsLedgerLinesSoTheBalanceGoesBack()
    {
        var result = await _world.QuickCapture.CaptureExpenseAsync(5000, "مصروف خاطئ");

        Assert.True(await _world.QuickCapture.UndoAsync(result.UndoToken));

        Assert.Equal(0, await _world.Db.Transactions.CountAsync());
        Assert.Equal(0, await _world.Db.LedgerEntries.CountAsync());
        Assert.Equal(2, await _world.Db.LedgerEntries.IgnoreDeleted().CountAsync());
    }

    [Fact]
    public async Task AnUndoTokenWorksOnceOnly()
    {
        var result = await _world.QuickCapture.CaptureTaskAsync("مهمة");

        Assert.True(await _world.QuickCapture.UndoAsync(result.UndoToken));
        Assert.False(await _world.QuickCapture.UndoAsync(result.UndoToken));
    }

    [Fact]
    public async Task AnUnknownOrEmptyTokenIsRefusedRatherThanGuessingWhatToRemove()
    {
        await _world.QuickCapture.CaptureTaskAsync("مهمة تبقى");

        Assert.False(await _world.QuickCapture.UndoAsync("not-a-token"));
        Assert.False(await _world.QuickCapture.UndoAsync(string.Empty));
        Assert.Equal(1, await _world.Db.Tasks.CountAsync());
    }

    [Fact]
    public async Task AnExpiredTokenIsRefused_SoAToastFromHoursAgoCannotDeleteAnEditedRecord()
    {
        var result = await _world.QuickCapture.CaptureTaskAsync("مهمة");

        _world.Clock.UtcNow = result.UndoableUntil.AddSeconds(1);

        Assert.False(await _world.QuickCapture.UndoAsync(result.UndoToken));
        Assert.Equal(1, await _world.Db.Tasks.CountAsync());
    }

    [Fact]
    public async Task EachCaptureGetsItsOwnTokenAndUndoingOneLeavesTheOthersAlone()
    {
        var first = await _world.QuickCapture.CaptureTaskAsync("الأولى");
        var second = await _world.QuickCapture.CaptureTaskAsync("الثانية");

        Assert.NotEqual(first.UndoToken, second.UndoToken);
        Assert.True(await _world.QuickCapture.UndoAsync(second.UndoToken));

        var remaining = await _world.Db.Tasks.SingleAsync();
        Assert.Equal("الأولى", remaining.Title);
    }

    [Fact]
    public async Task CapturingAndUndoing_BothTellTheBadgeServiceItsNumbersAreStale()
    {
        var stale = 0;
        _world.Badges.Changed += (_, e) =>
        {
            if (e.IsStale)
            {
                stale++;
            }
        };

        var result = await _world.QuickCapture.CaptureTaskAsync("مهمة", Now.AddDays(-1));
        Assert.Equal(1, stale);

        await _world.QuickCapture.UndoAsync(result.UndoToken);
        Assert.Equal(2, stale);
    }

    [Fact]
    public async Task ACapturedTaskShowsUpInTheAttentionCenterAndDisappearsOnUndo()
    {
        var result = await _world.QuickCapture.CaptureTaskAsync("مهمة متأخرة", Now.AddDays(-3));

        Assert.Equal(1, (await _world.Attention.GetCountsAsync(Now)).Late);

        await _world.QuickCapture.UndoAsync(result.UndoToken);

        Assert.Equal(0, (await _world.Attention.GetCountsAsync(Now)).Total);
    }

    [Fact]
    public async Task EmptyTextAndANonPositiveAmountAreCallerBugs_NotSilentlySavedRecords()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _world.QuickCapture.CaptureTaskAsync("   "));
        await Assert.ThrowsAsync<ArgumentException>(() => _world.QuickCapture.CaptureNoteAsync(string.Empty));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _world.QuickCapture.CaptureExpenseAsync(0, "بلا مبلغ"));

        Assert.Equal(0, await _world.Db.Tasks.CountAsync());
        Assert.Equal(0, await _world.Db.Notes.CountAsync());
        Assert.Equal(0, await _world.Db.Transactions.CountAsync());
    }

    [Fact]
    public async Task TitlesAreTrimmedSoAStrayNewlineNeverBecomesPartOfTheRecord()
    {
        var result = await _world.QuickCapture.CaptureTaskAsync("  مهمة بمسافات  ");

        var row = await _world.Db.Tasks.SingleAsync(t => t.Id == result.Id);
        Assert.Equal("مهمة بمسافات", row.Title);
    }
}
