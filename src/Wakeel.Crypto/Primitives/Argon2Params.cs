namespace Wakeel.Crypto;

/// <summary>
/// Argon2id cost parameters plus the salt they were measured with.
/// Serialised inside key wraps and container manifests so that a file can always be
/// reopened with the parameters it was created with.
/// </summary>
public sealed record Argon2Params(int MemoryKb, int Iterations, int Parallelism, byte[] Salt)
{
    /// <summary>Lowest memory cost the product will ever fall back to (32 MB).</summary>
    public const int MinMemoryKb = 32 * 1024;

    /// <summary>Memory cost used on a healthy machine (64 MB).</summary>
    public const int DefaultMemoryKb = 64 * 1024;

    public const int DefaultIterations = 3;
    public const int DefaultParallelism = 2;
    public const int SaltSize = 16;

    public static Argon2Params CreateDefault() =>
        new(DefaultMemoryKb, DefaultIterations, DefaultParallelism, RandomBytes.Next(SaltSize));

    /// <summary>Same cost parameters, fresh salt.</summary>
    public Argon2Params WithFreshSalt() =>
        new(MemoryKb, Iterations, Parallelism, RandomBytes.Next(SaltSize));

    public void Validate()
    {
        if (MemoryKb < MinMemoryKb)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The key derivation memory cost is below the allowed minimum.");
        }

        if (Iterations < 1 || Parallelism < 1)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The key derivation cost parameters are not usable.");
        }

        if (Salt is null || Salt.Length < 8)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The key derivation salt is missing or too short.");
        }

        if (MemoryKb < 8 * Parallelism)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The key derivation memory cost is too small for the requested parallelism.");
        }
    }

    public bool Equals(Argon2Params? other) =>
        other is not null
        && MemoryKb == other.MemoryKb
        && Iterations == other.Iterations
        && Parallelism == other.Parallelism
        && (Salt is null ? other.Salt is null : other.Salt is not null && Salt.AsSpan().SequenceEqual(other.Salt));

    public override int GetHashCode() =>
        HashCode.Combine(MemoryKb, Iterations, Parallelism, Salt is null ? 0 : Convert.ToHexString(Salt).GetHashCode(StringComparison.Ordinal));
}
