using Wakeel.Core.Data;

namespace Wakeel.Core.Services.Correspondence;

/// <summary>
/// The allowed status transitions of a correspondence item (B3-1: «الحالات … بانتقالات مسموحة»).
/// Kept as data rather than as scattered <c>if</c>s so the whole lifecycle can be read — and
/// tested — in one place, and so the screens can grey out the commands a state does not offer.
/// </summary>
/// <remarks>
/// The shape of the lifecycle:
/// <list type="bullet">
/// <item><description>
/// <see cref="CorrespondenceStatus.Draft"/> leaves only towards <see cref="CorrespondenceStatus.New"/>,
/// and only through registration (incoming) or approval (outgoing) — the two operations that
/// issue the official number. A draft is never cancelled: an unnumbered draft is deleted
/// outright (AGREEMENT item 19).
/// </description></item>
/// <item><description>
/// The three working states (<see cref="CorrespondenceStatus.New"/>,
/// <see cref="CorrespondenceStatus.InProgress"/>, <see cref="CorrespondenceStatus.AwaitingReply"/>)
/// move freely between each other and towards Done, Closed, Cancelled and Archived.
/// </description></item>
/// <item><description>
/// <see cref="CorrespondenceStatus.Done"/> and <see cref="CorrespondenceStatus.Closed"/> can be
/// reopened into <see cref="CorrespondenceStatus.InProgress"/> — a reply that arrives after the
/// file was closed is an ordinary event in an office, and forcing a new item for it would break
/// the thread. They can also be cancelled: the number stays consumed either way.
/// </description></item>
/// <item><description>
/// <see cref="CorrespondenceStatus.Cancelled"/> leads only to the archive; the item stays
/// visible with its number and its reason (AGREEMENT item 19).
/// </description></item>
/// <item><description>
/// <see cref="CorrespondenceStatus.Archived"/> is terminal. Nothing leaves the archive, so a
/// screen never has to explain a half-archived item.
/// </description></item>
/// </list>
/// </remarks>
public static class CorrespondenceStateMachine
{
    private static readonly IReadOnlyDictionary<CorrespondenceStatus, CorrespondenceStatus[]> Allowed =
        new Dictionary<CorrespondenceStatus, CorrespondenceStatus[]>
        {
            [CorrespondenceStatus.Draft] = [CorrespondenceStatus.New],
            [CorrespondenceStatus.New] =
            [
                CorrespondenceStatus.InProgress,
                CorrespondenceStatus.AwaitingReply,
                CorrespondenceStatus.Done,
                CorrespondenceStatus.Closed,
                CorrespondenceStatus.Cancelled,
                CorrespondenceStatus.Archived,
            ],
            [CorrespondenceStatus.InProgress] =
            [
                CorrespondenceStatus.New,
                CorrespondenceStatus.AwaitingReply,
                CorrespondenceStatus.Done,
                CorrespondenceStatus.Closed,
                CorrespondenceStatus.Cancelled,
                CorrespondenceStatus.Archived,
            ],
            [CorrespondenceStatus.AwaitingReply] =
            [
                CorrespondenceStatus.New,
                CorrespondenceStatus.InProgress,
                CorrespondenceStatus.Done,
                CorrespondenceStatus.Closed,
                CorrespondenceStatus.Cancelled,
                CorrespondenceStatus.Archived,
            ],
            [CorrespondenceStatus.Done] =
            [
                CorrespondenceStatus.InProgress,
                CorrespondenceStatus.Closed,
                CorrespondenceStatus.Cancelled,
                CorrespondenceStatus.Archived,
            ],
            [CorrespondenceStatus.Closed] =
            [
                CorrespondenceStatus.InProgress,
                CorrespondenceStatus.Cancelled,
                CorrespondenceStatus.Archived,
            ],
            [CorrespondenceStatus.Cancelled] = [CorrespondenceStatus.Archived],
            [CorrespondenceStatus.Archived] = [],
        };

    /// <summary>Every status an item in <paramref name="from"/> may move to, in lifecycle order.</summary>
    public static IReadOnlyList<CorrespondenceStatus> AllowedFrom(CorrespondenceStatus from) =>
        Allowed.TryGetValue(from, out var targets) ? targets : [];

    /// <summary>True when <paramref name="from"/> → <paramref name="to"/> is one of the allowed moves.</summary>
    public static bool CanTransition(CorrespondenceStatus from, CorrespondenceStatus to) =>
        AllowedFrom(from).Contains(to);

    /// <summary>
    /// Throws a <see cref="CorrespondenceRefusedException"/> naming both states in Arabic when
    /// the move is not allowed. A same-to-same move is refused too: it would write an audit row
    /// and a follow-up entry saying nothing happened.
    /// </summary>
    public static void EnsureTransition(CorrespondenceStatus from, CorrespondenceStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new CorrespondenceRefusedException(
                CoreAr.CorrRefusedTransition(CorrespondenceAr.Status(from), CorrespondenceAr.Status(to)));
        }
    }

    /// <summary>The states that still count as open work for the attention center and the badges.</summary>
    public static bool IsOpen(CorrespondenceStatus status) =>
        status is CorrespondenceStatus.New or CorrespondenceStatus.InProgress or CorrespondenceStatus.AwaitingReply;

    /// <summary>The states that end the item's life: nothing more is expected of it.</summary>
    public static bool IsFinal(CorrespondenceStatus status) =>
        status is CorrespondenceStatus.Closed or CorrespondenceStatus.Cancelled or CorrespondenceStatus.Archived;
}
