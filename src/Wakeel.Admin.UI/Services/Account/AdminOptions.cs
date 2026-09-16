using Wakeel.Crypto;

namespace Wakeel.Admin.UI.Services.Account;

/// <summary>
/// The few decisions the administration services cannot make for themselves: how expensive the
/// password derivation should be, and how patient the sign-in screen is. A shipped tool leaves all
/// of them alone; the test suite lowers the derivation cost so proving a round trip does not cost a
/// minute of Argon2id, and shortens the lock-out so its timing can be watched.
/// </summary>
public sealed class AdminOptions
{
    /// <summary>Wrong passwords in a row before the temporary lock-out starts.</summary>
    public const int DefaultMaxAttempts = 5;

    /// <summary>Seconds of the first temporary lock-out; every further round lasts twice as long.</summary>
    public const int DefaultLockOutSeconds = 30;

    /// <summary>The longest a temporary lock-out is ever allowed to grow to (half an hour).</summary>
    public const int MaxLockOutSeconds = 30 * 60;

    /// <summary>
    /// Fixed derivation cost. When null — the shipped case — this machine is measured once, at
    /// account creation, and the cost it can actually afford is used (ARCHITECTURE.md §3).
    /// </summary>
    public Argon2Params? Kdf { get; set; }

    /// <summary>How long one password derivation should take on a healthy machine.</summary>
    public int KdfTargetMs { get; set; } = Argon2Kdf.DefaultTargetMs;

    /// <summary>Wrong passwords in a row before the temporary lock-out starts.</summary>
    public int MaxAttempts { get; set; } = DefaultMaxAttempts;

    /// <summary>Seconds of the first temporary lock-out.</summary>
    public int LockOutSeconds { get; set; } = DefaultLockOutSeconds;

    /// <summary>The name of this computer, shown under the sign-in card so a person knows where they are.</summary>
    public string MachineName { get; set; } = SafeMachineName();

    /// <summary>How long the <paramref name="round"/>-th lock-out lasts, doubling each time.</summary>
    public int LockOutSecondsForRound(int round)
    {
        if (round <= 0)
        {
            return 0;
        }

        var seconds = (long)LockOutSeconds;
        for (var doubling = 1; doubling < round; doubling++)
        {
            seconds *= 2;
            if (seconds >= MaxLockOutSeconds)
            {
                return MaxLockOutSeconds;
            }
        }

        return (int)Math.Min(seconds, MaxLockOutSeconds);
    }

    private static string SafeMachineName()
    {
        try
        {
            return Environment.MachineName;
        }
        catch (InvalidOperationException)
        {
            // A machine that will not say its own name is not a reason to fail to start; the line
            // under the sign-in card simply leaves it out.
            return string.Empty;
        }
    }
}
