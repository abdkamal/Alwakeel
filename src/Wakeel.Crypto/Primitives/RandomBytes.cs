using System.Security.Cryptography;

namespace Wakeel.Crypto;

/// <summary>
/// The single source of randomness for the whole product.
/// </summary>
public static class RandomBytes
{
    /// <summary>Returns <paramref name="count"/> cryptographically strong random bytes.</summary>
    public static byte[] Next(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0)
        {
            return [];
        }

        return RandomNumberGenerator.GetBytes(count);
    }

    /// <summary>Fills <paramref name="destination"/> with cryptographically strong random bytes.</summary>
    public static void Fill(Span<byte> destination) => RandomNumberGenerator.Fill(destination);

    /// <summary>Returns a random integer in <c>[0, exclusiveUpperBound)</c> without modulo bias.</summary>
    public static int NextInt32(int exclusiveUpperBound) =>
        RandomNumberGenerator.GetInt32(exclusiveUpperBound);
}
