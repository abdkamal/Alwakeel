using System.Reflection;
using Wakeel.Crypto;

namespace Wakeel.UI.Services.Account;

/// <summary>
/// The few decisions the account services cannot make for themselves: how expensive the password
/// derivation should be, which build this is, and how many wrong passwords the sign-in screen
/// tolerates. A shipped installation leaves all of them at their defaults; the test suite lowers
/// the derivation cost so proving a round trip does not cost a minute of Argon2id.
/// </summary>
public sealed class AccountOptions
{
    /// <summary>Wrong passwords in a row before the sign-in screen stops accepting any (W07).</summary>
    public const int DefaultMaxAttempts = 5;

    /// <summary>Minutes of the first temporary lock-out; each further round doubles it.</summary>
    public const int DefaultLockOutMinutes = 15;

    /// <summary>Longest a temporary lock-out is ever allowed to become.</summary>
    public const int MaxLockOutMinutes = 60;

    /// <summary>
    /// Fixed derivation cost. When null — the shipped case — the machine is measured at activation
    /// and the cost that machine can actually afford is used (ARCHITECTURE.md §3).
    /// </summary>
    public Argon2Params? Kdf { get; set; }

    /// <summary>How long one password derivation should take on a healthy machine.</summary>
    public int KdfTargetMs { get; set; } = Argon2Kdf.DefaultTargetMs;

    /// <summary>Wrong passwords in a row before the temporary lock-out starts.</summary>
    public int MaxAttempts { get; set; } = DefaultMaxAttempts;

    /// <summary>Minutes of the first temporary lock-out.</summary>
    public int LockOutMinutes { get; set; } = DefaultLockOutMinutes;

    /// <summary>The product version written into the installation row.</summary>
    public string AppVersion { get; set; } = ResolveAppVersion();

    /// <summary>
    /// When this build was produced. The clock check refuses to believe a machine whose clock sits
    /// before it (AGREEMENT item 20), so it is taken from the build itself rather than guessed.
    /// </summary>
    public DateTimeOffset BuildDate { get; set; } = ResolveBuildDate();

    private static string ResolveAppVersion() =>
        typeof(AccountOptions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(AccountOptions).Assembly.GetName().Version?.ToString()
            ?? "0.21.0";

    private static DateTimeOffset ResolveBuildDate()
    {
        try
        {
            var location = typeof(AccountOptions).Assembly.Location;
            if (!string.IsNullOrEmpty(location) && File.Exists(location))
            {
                return new DateTimeOffset(File.GetLastWriteTimeUtc(location), TimeSpan.Zero);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A single-file or trimmed publish may report no location at all; the fallback below is
            // then simply a date that can never sit ahead of a sane clock.
        }

        return new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
