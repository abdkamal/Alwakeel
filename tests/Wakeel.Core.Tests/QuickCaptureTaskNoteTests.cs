using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Services;

namespace Wakeel.Core.Tests;

/// <summary>
/// The optional note the quick-entry dialog collects under «ملاحظة (اختياري)» when the kind is a
/// task (W94). It is part of the capture itself rather than a second write, so the one undo token
/// the toast holds takes the note back with the task.
/// </summary>
public sealed class QuickCaptureTaskNoteTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 17, 8, 30, 0, DateTimeKind.Utc);

    private readonly DailyShellWorld _world = new(Now);

    public QuickCaptureTaskNoteTests() => _world.SeedInstallation();

    public void Dispose() => _world.Dispose();

    [Fact]
    public async Task TheNoteIsSavedInTheSameWriteAsTheTask()
    {
        var result = await _world.QuickCapture.CaptureTaskAsync(
            "متابعة كتاب الديوان",
            Now.AddDays(3),
            TaskPriority.Normal,
            "أمين السر",
            "يُرفق محضر اللجنة السابق قبل الإرسال");

        var row = await _world.Db.Tasks.SingleAsync();
        Assert.Equal(result.Id, row.Id);
        Assert.Equal("يُرفق محضر اللجنة السابق قبل الإرسال", row.Description);
    }

    [Fact]
    public async Task ANoteOfNothingButSpaceIsNotStored()
    {
        await _world.QuickCapture.CaptureTaskAsync("متابعة كتاب الديوان", noteAr: "   ");

        var row = await _world.Db.Tasks.SingleAsync();
        Assert.Null(row.Description);
    }

    [Fact]
    public async Task TheSurroundingSpaceOfANoteIsTrimmed()
    {
        await _world.QuickCapture.CaptureTaskAsync("متابعة كتاب الديوان", noteAr: "  يُرفق المحضر  ");

        var row = await _world.Db.Tasks.SingleAsync();
        Assert.Equal("يُرفق المحضر", row.Description);
    }

    [Fact]
    public async Task OmittingTheNoteLeavesTheTaskWithout()
    {
        await _world.QuickCapture.CaptureTaskAsync("متابعة كتاب الديوان");

        var row = await _world.Db.Tasks.SingleAsync();
        Assert.Null(row.Description);
    }

    /// <summary>
    /// The point of putting the note in the capture call: one «تراجع» removes the whole thing. A
    /// note written in a second step would have survived the undo on its own row.
    /// </summary>
    [Fact]
    public async Task UndoTakesTheNoteBackWithTheTask()
    {
        var result = await _world.QuickCapture.CaptureTaskAsync(
            "متابعة كتاب الديوان",
            noteAr: "يُرفق محضر اللجنة السابق");

        Assert.True(await _world.QuickCapture.UndoAsync(result.UndoToken));

        Assert.Empty(await _world.Db.Tasks.ToListAsync());
        var hidden = await _world.Db.Tasks.IgnoreQueryFilters().SingleAsync();
        Assert.NotNull(hidden.DeletedAt);
        Assert.Equal("يُرفق محضر اللجنة السابق", hidden.Description);
    }
}
