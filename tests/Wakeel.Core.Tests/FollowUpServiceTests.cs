using Wakeel.Core.Data;
using Wakeel.Core.Services;
using Wakeel.Core.Services.Correspondence;

namespace Wakeel.Core.Tests;

/// <summary>
/// B3-1 «FollowUpService»: the call/visit/reply/note timeline, the next date, the reminder, the
/// state change that rides on the same entry, and the «بانتظار رد منذ N أيام» card — including
/// the attention center and badge counts that read the states this service writes.
/// </summary>
public sealed class FollowUpServiceTests
{
    [Fact]
    public async Task A_follow_up_entry_records_its_kind_note_and_next_date_in_arabic()
    {
        using var world = new CorrespondenceWorld();
        var registered = await world.RegisterIncomingAsync();

        var entry = await world.FollowUps.AddAsync(
            registered.Id,
            FollowupKind.Call,
            "اتصلت بالجهة وأفادت أن الرد قيد الإعداد",
            world.Now,
            nextAt: world.Now.AddDays(3));

        Assert.Equal(CoreAr.CorrFollowupCall, entry.KindAr);
        Assert.Equal("اتصلت بالجهة وأفادت أن الرد قيد الإعداد", entry.NoteAr);
        Assert.Equal(world.Now.AddDays(3), entry.NextAt);
        Assert.Equal(ArabicRelativeTime.Describe(world.Now.AddDays(3), world.Now), entry.NextAr);

        // The next agreed date is what the worklist reads as the item's due date.
        Assert.Equal(world.Now.AddDays(3), world.Row(registered.Id).DueAt);
    }

    [Fact]
    public async Task One_entry_carries_both_the_note_and_the_state_change()
    {
        using var world = new CorrespondenceWorld();
        var registered = await world.RegisterIncomingAsync();

        var entry = await world.FollowUps.AddAsync(
            registered.Id,
            FollowupKind.Reply,
            "وصل الرد الرسمي",
            world.Now,
            statusTo: CorrespondenceStatus.Done);

        Assert.Equal(CorrespondenceStatus.New, entry.StatusFrom);
        Assert.Equal(CorrespondenceStatus.Done, entry.StatusTo);
        Assert.Equal(
            CoreAr.CorrAuditTransition(CoreAr.CorrStatusNew, CoreAr.CorrStatusDone),
            entry.StatusChangeAr);
        Assert.Equal(CorrespondenceStatus.Done, world.Row(registered.Id).Status);

        // One office action, one timeline row.
        Assert.Single(await world.FollowUps.ListAsync(registered.Id, world.Now));
    }

    [Fact]
    public async Task A_refused_state_change_writes_no_entry_at_all()
    {
        using var world = new CorrespondenceWorld();
        var registered = await world.RegisterIncomingAsync();
        await world.Correspondence.ArchiveAsync(registered.Id, world.Now);

        await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.FollowUps.AddAsync(registered.Id, FollowupKind.Note, "محاولة", world.Now, statusTo: CorrespondenceStatus.InProgress));

        // The archive transition wrote one entry of its own; the refused one wrote none.
        var entries = await world.FollowUps.ListAsync(registered.Id, world.Now);
        Assert.Equal(CorrespondenceStatus.Archived, Assert.Single(entries).StatusTo);
    }

    [Theory]
    [InlineData(0, "أقل من يوم")]
    [InlineData(1, "يوم واحد")]
    [InlineData(2, "يومان")]
    [InlineData(5, "5 أيام")]
    [InlineData(13, "13 يومًا")]
    public async Task The_awaiting_reply_card_counts_whole_days_with_arabic_agreement(int days, string expectedPhrase)
    {
        using var world = new CorrespondenceWorld();
        var registered = await world.RegisterIncomingAsync();
        var start = world.Now;

        await world.FollowUps.AddAsync(
            registered.Id,
            FollowupKind.Note,
            "أُرسل الطلب وننتظر الرد",
            start,
            statusTo: CorrespondenceStatus.AwaitingReply);

        var later = start.AddDays(days).AddHours(3);
        var card = await world.FollowUps.GetAwaitingReplyCardAsync(registered.Id, later);

        Assert.NotNull(card);
        Assert.Equal(days, card.Days);
        Assert.Equal($"بانتظار رد منذ {expectedPhrase}", card.TextAr);
        Assert.Equal(registered.Subject, card.Subject);
        Assert.Equal(registered.OfficialNumber, card.OfficialNumber);
    }

    [Fact]
    public async Task Only_items_actually_waiting_produce_a_card_and_the_longest_wait_comes_first()
    {
        using var world = new CorrespondenceWorld();

        var today = world.Now;
        var oldest = await world.RegisterIncomingAsync(world.IncomingInput(subject: "الأقدم", externalNumber: "1/1"));
        var newest = await world.RegisterIncomingAsync(world.IncomingInput(subject: "الأحدث", externalNumber: "2/2"));
        var busy = await world.RegisterIncomingAsync(world.IncomingInput(subject: "قيد المتابعة", externalNumber: "3/3"));

        // The wait starts when the entry was written, and WakeelDb stamps that from the clock —
        // so the clock is what a test moves to craft a wait of a given length.
        world.Clock.UtcNow = today.AddDays(-9);
        await world.FollowUps.AddAsync(oldest.Id, FollowupKind.Note, null, world.Clock.UtcNow, statusTo: CorrespondenceStatus.AwaitingReply);
        world.Clock.UtcNow = today.AddDays(-2);
        await world.FollowUps.AddAsync(newest.Id, FollowupKind.Note, null, world.Clock.UtcNow, statusTo: CorrespondenceStatus.AwaitingReply);
        world.Clock.UtcNow = today;
        await world.Correspondence.ChangeStatusAsync(busy.Id, CorrespondenceStatus.InProgress, today);

        var cards = await world.FollowUps.ListAwaitingReplyAsync(today);

        Assert.Equal(2, cards.Count);
        Assert.Equal("الأقدم", cards[0].Subject);
        Assert.Equal(9, cards[0].Days);
        Assert.Equal("الأحدث", cards[1].Subject);
        Assert.Equal(2, cards[1].Days);
        Assert.Null(await world.FollowUps.GetAwaitingReplyCardAsync(busy.Id, today));
    }

    [Fact]
    public async Task A_due_reminder_becomes_one_notification_however_many_times_the_pass_runs()
    {
        using var world = new CorrespondenceWorld();
        var registered = await world.RegisterIncomingAsync();

        await world.FollowUps.AddAsync(
            registered.Id,
            FollowupKind.Call,
            "نتابع بعد ساعتين",
            world.Now,
            nextAt: world.Now.AddHours(2),
            reminderAt: world.Now.AddHours(2));

        // Not due yet.
        Assert.Equal(0, (await world.FollowUps.RunDueRemindersAsync(world.Now)).Created);

        var due = world.Now.AddHours(2).AddMinutes(1);
        var first = await world.FollowUps.RunDueRemindersAsync(due);
        Assert.Equal(1, first.Created);

        var second = await world.FollowUps.RunDueRemindersAsync(due);
        Assert.Equal(0, second.Created);
        Assert.Equal(1, second.Skipped);

        var notification = Assert.Single(world.Db.Notifications.Where(n => n.Kind == FollowUpService.ReminderKind));
        Assert.Equal(CoreAr.CorrFollowupReminderTitle, notification.Title);
        Assert.Equal(CoreAr.CorrFollowupReminderBody(registered.Subject), notification.Body);
        Assert.Equal(registered.Id, notification.EntityId);
    }

    [Fact]
    public async Task A_reminder_on_a_closed_item_never_rings()
    {
        using var world = new CorrespondenceWorld();
        var registered = await world.RegisterIncomingAsync();

        await world.FollowUps.AddAsync(
            registered.Id,
            FollowupKind.Call,
            null,
            world.Now,
            reminderAt: world.Now.AddHours(1));
        await world.Correspondence.CloseAsync(registered.Id, "لم يعد المطلوب قائمًا", world.Now);

        var run = await world.FollowUps.RunDueRemindersAsync(world.Now.AddHours(2));

        Assert.Equal(0, run.Created);
        Assert.Empty(world.Db.Notifications.Where(n => n.Kind == FollowUpService.ReminderKind));
    }

    [Fact]
    public async Task An_overdue_awaiting_reply_reaches_the_attention_center_and_the_correspondence_badge()
    {
        using var world = new CorrespondenceWorld();
        var registered = await world.RegisterIncomingAsync();

        // A reply that was due three days ago and has not come.
        await world.FollowUps.AddAsync(
            registered.Id,
            FollowupKind.Note,
            "مهلة الرد ثلاثة أيام",
            world.Now,
            nextAt: world.Now.AddDays(-3),
            statusTo: CorrespondenceStatus.AwaitingReply);

        var items = await world.Attention.GetAllAsync(world.Now);
        var item = Assert.Single(items, i => i.Id == registered.Id);
        Assert.Equal(AttentionEntityKind.CorrespondenceIn, item.Kind);
        Assert.Equal(CoreAr.KindCorrespondenceIn, item.KindLabelAr);
        Assert.Equal(AttentionBucket.Late, item.Bucket);
        Assert.Equal(3, item.DaysLate);
        Assert.Equal(registered.OfficialNumber, item.NumberAr);

        var badges = await world.Badges.RefreshAsync(world.Now);
        Assert.Equal(1, badges.For(BadgeKeys.Correspondence));

        // Answering it clears both.
        await world.FollowUps.AddAsync(registered.Id, FollowupKind.Reply, "وصل الرد", world.Now, statusTo: CorrespondenceStatus.Done);
        Assert.DoesNotContain(await world.Attention.GetAllAsync(world.Now), i => i.Id == registered.Id);
        Assert.Equal(0, (await world.Badges.RefreshAsync(world.Now)).For(BadgeKeys.Correspondence));
    }

    [Theory]
    [InlineData(CorrespondenceStatus.Cancelled)]
    [InlineData(CorrespondenceStatus.Closed)]
    [InlineData(CorrespondenceStatus.Archived)]
    public async Task The_timeline_cannot_set_a_state_that_carries_a_stored_field(CorrespondenceStatus target)
    {
        using var world = new CorrespondenceWorld();
        var registered = await world.RegisterIncomingAsync();

        // AGREEMENT item 19: a numbered letter is cancelled BY A REASON. Reaching «ملغى» through
        // the timeline would leave that reason — and the closing note, and the archiving instant —
        // empty, so the three are refused here and only their own command may set them.
        var error = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.FollowUps.AddAsync(registered.Id, FollowupKind.Note, "ملاحظة", world.Now, statusTo: target));

        Assert.Equal(CoreAr.CorrRefusedFollowupStatus, error.MessageAr);
        Assert.DoesNotContain(error.MessageAr, c => char.IsAsciiLetter(c));

        var row = world.Row(registered.Id);
        Assert.Equal(CorrespondenceStatus.New, row.Status);
        Assert.Null(row.CancelReason);
        Assert.Null(row.CloseNote);
        Assert.Null(row.ArchivedAt);
        Assert.Empty(await world.FollowUps.ListAsync(registered.Id, world.Now));
    }

    [Fact]
    public async Task The_dedicated_commands_store_the_reason_the_note_and_the_archiving_instant()
    {
        using var world = new CorrespondenceWorld();

        var cancelled = await world.RegisterIncomingAsync(world.IncomingInput(externalNumber: "2026/701"));
        var cancelledView = await world.Correspondence.CancelAsync(cancelled.Id, "صدر بدلًا عنه كتاب آخر", world.Now);
        Assert.Equal("صدر بدلًا عنه كتاب آخر", cancelledView.CancelReasonAr);
        Assert.Equal(cancelled.OfficialNumber, cancelledView.OfficialNumber);

        var closed = await world.RegisterIncomingAsync(world.IncomingInput(externalNumber: "2026/702"));
        var closedView = await world.Correspondence.CloseAsync(closed.Id, "أُنجز الموضوع", world.Now);
        Assert.Equal("أُنجز الموضوع", closedView.CloseNoteAr);

        var archived = await world.RegisterIncomingAsync(world.IncomingInput(externalNumber: "2026/703"));
        var archivedView = await world.Correspondence.ArchiveAsync(archived.Id, world.Now);
        Assert.NotNull(archivedView.ArchivedAt);
        Assert.Equal(world.Now, archivedView.ArchivedAt);
    }

    /// <summary>
    /// The pass is not enough on its own: the product's own per-minute run is what has to call it,
    /// or the agreed next date passes and no bell ever rings. Running it twice in the same window
    /// still leaves exactly one notification, the same way every other reminder kind behaves.
    /// </summary>
    [Fact]
    public async Task A_due_follow_up_rings_through_the_whole_reminder_pass_exactly_once()
    {
        using var world = new CorrespondenceWorld();
        var registered = await world.RegisterIncomingAsync();

        await world.FollowUps.AddAsync(
            registered.Id,
            FollowupKind.Call,
            "نتابع بعد ساعتين",
            world.Now,
            nextAt: world.Now.AddHours(2),
            reminderAt: world.Now.AddHours(2));

        // The whole pass, before the instant: nothing of this kind is raised. (Other kinds may
        // well be — a fresh installation has never taken a backup — which is exactly why the
        // assertions below count follow-up notifications rather than the pass's total.)
        await world.Reminders.RunOnceAsync(world.Now);
        Assert.Empty(world.Db.Notifications.Where(n => n.Kind == FollowUpService.ReminderKind));

        var due = world.Now.AddHours(2).AddMinutes(1);
        var first = await world.Reminders.RunOnceAsync(due);
        Assert.Equal(1, first.Created);

        var again = await world.Reminders.RunOnceAsync(due);
        Assert.Equal(0, again.Created);

        var notification = Assert.Single(world.Db.Notifications.Where(n => n.Kind == FollowUpService.ReminderKind));
        Assert.Equal(CoreAr.CorrFollowupReminderTitle, notification.Title);
        Assert.Equal(registered.Id, notification.EntityId);
    }
}
