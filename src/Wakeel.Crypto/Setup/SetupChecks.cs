namespace Wakeel.Crypto;

/// <summary>
/// The items the first run screen shows, in the order it shows them. The enumeration order
/// is the display order, so a check list never has to be sorted by the interface.
/// </summary>
public enum SetupCheckItem
{
    /// <summary>The file as a whole: its format version, its date and its export sequence.</summary>
    Package,

    /// <summary>The organisation signature over the file and the root certificate it carries.</summary>
    Signature,

    /// <summary>The organisation named inside the file, and whether it is the expected one.</summary>
    Organisation,

    /// <summary>The office and the structure it sits in.</summary>
    Office,

    /// <summary>The computer account, its certificate and its private seeds.</summary>
    Device,

    /// <summary>The person the account belongs to.</summary>
    Employee,

    /// <summary>The office key that opens the packets exchanged inside the office.</summary>
    OfficeKey,

    /// <summary>The organisation logo.</summary>
    Logo,

    /// <summary>The user guide.</summary>
    Guide,

    /// <summary>The monthly report template.</summary>
    ReportTemplate,

    /// <summary>The signed revocation list the file distributes.</summary>
    Revocation,
}

/// <summary>The three outcomes one item of the check list can have.</summary>
public enum SetupCheckStatus
{
    /// <summary>The item is present and everything about it holds.</summary>
    Ok,

    /// <summary>The item is present but wrong. <see cref="SetupCheck.Error"/> says why.</summary>
    Failed,

    /// <summary>The file deliberately does not carry this item. Only the optional items.</summary>
    Absent,
}

/// <summary>
/// One line of the check list. The reason is a code, never a sentence: the interface owns
/// every word a person reads, this project owns none of them.
/// </summary>
public sealed record SetupCheck(SetupCheckItem Item, SetupCheckStatus Status, ErrorCode? Error)
{
    public static SetupCheck Ok(SetupCheckItem item) => new(item, SetupCheckStatus.Ok, null);

    public static SetupCheck Absent(SetupCheckItem item) => new(item, SetupCheckStatus.Absent, null);

    public static SetupCheck Failed(SetupCheckItem item, ErrorCode error) => new(item, SetupCheckStatus.Failed, error);
}

/// <summary>
/// The whole check list. It carries only the items that could actually be examined: when a
/// file fails so early that nothing after it can be judged — a broken signature, a wrong
/// password — the list stops there rather than inventing verdicts for the rest.
/// </summary>
public sealed record SetupCheckResult(IReadOnlyList<SetupCheck> Checks)
{
    /// <summary>The items a file must carry before an installation may be activated from it.</summary>
    private static readonly SetupCheckItem[] RequiredItems =
    [
        SetupCheckItem.Package,
        SetupCheckItem.Signature,
        SetupCheckItem.Organisation,
        SetupCheckItem.Office,
        SetupCheckItem.Device,
        SetupCheckItem.Employee,
        SetupCheckItem.OfficeKey,
    ];

    /// <summary>The first line that failed, in display order.</summary>
    public SetupCheck? FirstFailure =>
        Checks.FirstOrDefault(check => check.Status == SetupCheckStatus.Failed);

    /// <summary>
    /// Whether this file may be used to activate an installation: nothing failed, and every
    /// item that is not optional was actually examined and held.
    /// </summary>
    public bool IsAcceptable =>
        FirstFailure is null
        && RequiredItems.All(item => Find(item)?.Status == SetupCheckStatus.Ok);

    public SetupCheck? Find(SetupCheckItem item) =>
        Checks.FirstOrDefault(check => check.Item == item);

    /// <summary>
    /// Turns the first failure into the refusal the caller has to honour. The interface shows
    /// the whole list; the code that actually writes an installation calls this first.
    /// </summary>
    public void EnsureAcceptable()
    {
        if (FirstFailure is { } failure)
        {
            throw new CryptoException(
                failure.Error ?? ErrorCode.Corrupt,
                $"The setup file did not pass the {failure.Item} check.");
        }

        if (!IsAcceptable)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The setup file could not be examined completely.");
        }
    }
}

/// <summary>Collects check outcomes and hands them back in display order.</summary>
internal sealed class SetupCheckList
{
    private readonly SortedDictionary<SetupCheckItem, SetupCheck> _checks = [];

    internal void Ok(SetupCheckItem item) => _checks[item] = SetupCheck.Ok(item);

    internal void Absent(SetupCheckItem item) => _checks[item] = SetupCheck.Absent(item);

    internal void Failed(SetupCheckItem item, ErrorCode error) => _checks[item] = SetupCheck.Failed(item, error);

    /// <summary>Records a failure only when there is one, and answers whether the item held.</summary>
    internal bool Record(SetupCheckItem item, ErrorCode? error)
    {
        if (error is { } code)
        {
            Failed(item, code);
            return false;
        }

        Ok(item);
        return true;
    }

    internal SetupCheckResult Build() => new([.. _checks.Values]);
}
