using System.Text;
using Konscious.Security.Cryptography;

namespace Wakeel.Crypto;

/// <summary>
/// Argon2id password hashing, used for the account password, the recovery code and
/// the password of exported containers.
/// </summary>
public static class Argon2Kdf
{
    public const int DefaultKeySize = 32;

    /// <summary>The target a healthy machine should hit, in milliseconds.</summary>
    public const int DefaultTargetMs = 1000;

    private const string ProbeSecret = "wakeel-autotune-probe";

    public static byte[] DeriveKey(ReadOnlySpan<byte> secret, Argon2Params parameters, int length = DefaultKeySize)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, 0);
        parameters.Validate();

        return DeriveRaw(
            secret,
            parameters.Salt,
            parameters.MemoryKb,
            parameters.Iterations,
            parameters.Parallelism,
            length);
    }

    /// <summary>
    /// Argon2id with every parameter stated explicitly, including the optional secret and
    /// associated data of the specification. The memory cost is counted in kibibytes, exactly
    /// as the specification counts it. It exists so published test vectors can be checked
    /// against the wiring the product actually uses, and so the phone side can be proved to
    /// derive the same key from the same recovery code.
    /// </summary>
    public static byte[] DeriveRaw(
        ReadOnlySpan<byte> password,
        ReadOnlySpan<byte> salt,
        int memoryKb,
        int iterations,
        int parallelism,
        int length,
        ReadOnlySpan<byte> knownSecret = default,
        ReadOnlySpan<byte> associatedData = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(memoryKb, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(iterations, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(parallelism, 0);

        using var argon = new Argon2id(password.ToArray())
        {
            Salt = salt.ToArray(),
            MemorySize = memoryKb,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };

        if (!knownSecret.IsEmpty)
        {
            argon.KnownSecret = knownSecret.ToArray();
        }

        if (!associatedData.IsEmpty)
        {
            argon.AssociatedData = associatedData.ToArray();
        }

        return argon.GetBytes(length);
    }

    public static byte[] DeriveKey(string secret, Argon2Params parameters, int length = DefaultKeySize) =>
        DeriveKey(Encoding.UTF8.GetBytes(secret ?? string.Empty), parameters, length);

    /// <summary>
    /// Measures this machine and returns parameters it can actually afford.
    /// Starts at 64 MB / 3 iterations / 2 lanes and halves the memory while a probe run
    /// takes longer than one and a half times the target, never dropping below 32 MB.
    /// </summary>
    public static Argon2Params AutoTune(int targetMs = DefaultTargetMs, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(targetMs, 0);
        var clock = timeProvider ?? TimeProvider.System;
        var limitMs = targetMs * 3.0 / 2.0;

        var memoryKb = Argon2Params.DefaultMemoryKb;
        while (true)
        {
            var probe = new Argon2Params(
                memoryKb,
                Argon2Params.DefaultIterations,
                Argon2Params.DefaultParallelism,
                RandomBytes.Next(Argon2Params.SaltSize));

            var started = clock.GetTimestamp();
            _ = DeriveKey(ProbeSecret, probe);
            var elapsed = clock.GetElapsedTime(started);

            var halved = memoryKb / 2;
            if (elapsed.TotalMilliseconds <= limitMs || halved < Argon2Params.MinMemoryKb)
            {
                return new Argon2Params(
                    memoryKb,
                    Argon2Params.DefaultIterations,
                    Argon2Params.DefaultParallelism,
                    RandomBytes.Next(Argon2Params.SaltSize));
            }

            memoryKb = halved;
        }
    }
}
