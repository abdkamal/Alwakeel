using Wakeel.Core.Data;
using Wakeel.Core.Services;
using Wakeel.Core.Services.Correspondence;

namespace Wakeel.Core.Tests;

/// <summary>
/// B3-1: «الحالات … بانتقالات مسموحة». Every allowed move and every refused one, asserted from
/// the table itself so the lifecycle cannot drift without a test turning red.
/// </summary>
public sealed class CorrespondenceStateMachineTests
{
    [Theory]
    // A draft leaves only towards «جديد», through registration or approval.
    [InlineData(CorrespondenceStatus.Draft, CorrespondenceStatus.New)]
    // The three working states move freely between each other.
    [InlineData(CorrespondenceStatus.New, CorrespondenceStatus.InProgress)]
    [InlineData(CorrespondenceStatus.New, CorrespondenceStatus.AwaitingReply)]
    [InlineData(CorrespondenceStatus.InProgress, CorrespondenceStatus.AwaitingReply)]
    [InlineData(CorrespondenceStatus.AwaitingReply, CorrespondenceStatus.InProgress)]
    [InlineData(CorrespondenceStatus.AwaitingReply, CorrespondenceStatus.Done)]
    // Everything open can be finished, closed, cancelled or archived.
    [InlineData(CorrespondenceStatus.New, CorrespondenceStatus.Done)]
    [InlineData(CorrespondenceStatus.New, CorrespondenceStatus.Closed)]
    [InlineData(CorrespondenceStatus.New, CorrespondenceStatus.Cancelled)]
    [InlineData(CorrespondenceStatus.New, CorrespondenceStatus.Archived)]
    // A finished or closed file can be reopened when a late reply arrives.
    [InlineData(CorrespondenceStatus.Done, CorrespondenceStatus.InProgress)]
    [InlineData(CorrespondenceStatus.Closed, CorrespondenceStatus.InProgress)]
    [InlineData(CorrespondenceStatus.Done, CorrespondenceStatus.Closed)]
    [InlineData(CorrespondenceStatus.Closed, CorrespondenceStatus.Archived)]
    // A cancelled item still reaches the archive, keeping its number.
    [InlineData(CorrespondenceStatus.Cancelled, CorrespondenceStatus.Archived)]
    public void Allowed_transitions_are_permitted(CorrespondenceStatus from, CorrespondenceStatus to)
    {
        Assert.True(CorrespondenceStateMachine.CanTransition(from, to));
        CorrespondenceStateMachine.EnsureTransition(from, to);
    }

    [Theory]
    // A draft is never cancelled, closed or archived: an unnumbered draft is deleted outright.
    [InlineData(CorrespondenceStatus.Draft, CorrespondenceStatus.Cancelled)]
    [InlineData(CorrespondenceStatus.Draft, CorrespondenceStatus.Closed)]
    [InlineData(CorrespondenceStatus.Draft, CorrespondenceStatus.Archived)]
    [InlineData(CorrespondenceStatus.Draft, CorrespondenceStatus.InProgress)]
    [InlineData(CorrespondenceStatus.Draft, CorrespondenceStatus.AwaitingReply)]
    // Nothing ever goes back to being a draft: the number is already issued.
    [InlineData(CorrespondenceStatus.New, CorrespondenceStatus.Draft)]
    [InlineData(CorrespondenceStatus.InProgress, CorrespondenceStatus.Draft)]
    [InlineData(CorrespondenceStatus.Closed, CorrespondenceStatus.Draft)]
    // Nothing leaves the archive.
    [InlineData(CorrespondenceStatus.Archived, CorrespondenceStatus.InProgress)]
    [InlineData(CorrespondenceStatus.Archived, CorrespondenceStatus.New)]
    [InlineData(CorrespondenceStatus.Archived, CorrespondenceStatus.Closed)]
    // A cancelled item is never revived.
    [InlineData(CorrespondenceStatus.Cancelled, CorrespondenceStatus.InProgress)]
    [InlineData(CorrespondenceStatus.Cancelled, CorrespondenceStatus.Done)]
    // A closed file is not "finished" again without being reopened first.
    [InlineData(CorrespondenceStatus.Closed, CorrespondenceStatus.Done)]
    // Standing still is not a transition.
    [InlineData(CorrespondenceStatus.New, CorrespondenceStatus.New)]
    [InlineData(CorrespondenceStatus.Archived, CorrespondenceStatus.Archived)]
    public void Refused_transitions_throw_an_arabic_refusal(CorrespondenceStatus from, CorrespondenceStatus to)
    {
        Assert.False(CorrespondenceStateMachine.CanTransition(from, to));

        var error = Assert.Throws<CorrespondenceRefusedException>(() => CorrespondenceStateMachine.EnsureTransition(from, to));
        Assert.Equal(
            CoreAr.CorrRefusedTransition(CorrespondenceAr.Status(from), CorrespondenceAr.Status(to)),
            error.MessageAr);

        // AGREEMENT item 15: no technical term, no code, and both states named in Arabic.
        Assert.Contains(CorrespondenceAr.Status(from), error.MessageAr, StringComparison.Ordinal);
        Assert.Contains(CorrespondenceAr.Status(to), error.MessageAr, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_status_has_an_entry_so_no_state_is_a_dead_end_by_accident()
    {
        foreach (var status in Enum.GetValues<CorrespondenceStatus>())
        {
            var allowed = CorrespondenceStateMachine.AllowedFrom(status);
            Assert.DoesNotContain(status, allowed);

            // Archived is the only terminal state; every other status must offer a way out, or a
            // screen would show an item with no command at all.
            if (status != CorrespondenceStatus.Archived)
            {
                Assert.NotEmpty(allowed);
            }
            else
            {
                Assert.Empty(allowed);
            }
        }
    }

    [Fact]
    public void Open_and_final_classify_every_status_exactly_once()
    {
        foreach (var status in Enum.GetValues<CorrespondenceStatus>())
        {
            Assert.False(CorrespondenceStateMachine.IsOpen(status) && CorrespondenceStateMachine.IsFinal(status));
        }

        // The three the attention center and the badges read.
        Assert.True(CorrespondenceStateMachine.IsOpen(CorrespondenceStatus.New));
        Assert.True(CorrespondenceStateMachine.IsOpen(CorrespondenceStatus.InProgress));
        Assert.True(CorrespondenceStateMachine.IsOpen(CorrespondenceStatus.AwaitingReply));
        Assert.False(CorrespondenceStateMachine.IsOpen(CorrespondenceStatus.Draft));
    }

    [Fact]
    public void Every_status_and_step_has_arabic_wording_with_no_latin_letters()
    {
        foreach (var status in Enum.GetValues<CorrespondenceStatus>())
        {
            var text = CorrespondenceAr.Status(status);
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain(text, c => char.IsAsciiLetter(c));
        }

        foreach (var step in Enum.GetValues<OutgoingStep>())
        {
            var text = CorrespondenceAr.Step(step);
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain(text, c => char.IsAsciiLetter(c));
        }
    }

    /// <summary>
    /// The table allows «مسودة» → «جديد» because that is the lifecycle, but the only operations
    /// that may make the move are the two that issue the official number. A plain status change
    /// would leave an open numberless row: it could no longer be deleted (no longer a draft), nor
    /// cancelled (no number), nor corrected (not numbered), while the attention centre and the
    /// correspondence badge would go on counting it.
    /// </summary>
    [Fact]
    public async Task A_draft_cannot_be_promoted_to_new_by_a_plain_status_change()
    {
        using var world = new CorrespondenceWorld();
        var draft = await world.Correspondence.CreateDraftAsync(world.IncomingInput(), world.Now);

        var error = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.Correspondence.ChangeStatusAsync(draft.Id, CorrespondenceStatus.New, world.Now));

        Assert.Equal(CoreAr.CorrRefusedDraftNeedsNumbering, error.MessageAr);
        Assert.DoesNotContain(error.MessageAr, c => char.IsAsciiLetter(c) || char.IsAsciiDigit(c));

        var row = world.Row(draft.Id);
        Assert.Equal(CorrespondenceStatus.Draft, row.Status);
        Assert.Null(row.OfficialNumber);
        Assert.Equal(0, world.Sequence(InOutDirection.In));

        // Still an ordinary draft: the delete command, which only accepts «مسودة», still takes it.
        Assert.True(await world.Correspondence.DeleteDraftAsync(draft.Id, world.Now));
    }

    /// <summary>The same door seen from the timeline: a follow-up entry may not promote a draft either.</summary>
    [Fact]
    public async Task A_follow_up_entry_cannot_promote_a_draft_to_new()
    {
        using var world = new CorrespondenceWorld();
        var draft = await world.Correspondence.CreateDraftAsync(world.IncomingInput(), world.Now);

        var error = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            world.FollowUps.AddAsync(
                draft.Id,
                FollowupKind.Note,
                "وصل الكتاب",
                world.Now,
                statusTo: CorrespondenceStatus.New));

        Assert.Equal(CoreAr.CorrRefusedDraftNeedsNumbering, error.MessageAr);

        var row = world.Row(draft.Id);
        Assert.Equal(CorrespondenceStatus.Draft, row.Status);
        Assert.Null(row.OfficialNumber);
        Assert.Empty(await world.FollowUps.ListAsync(draft.Id, world.Now));
    }
}
