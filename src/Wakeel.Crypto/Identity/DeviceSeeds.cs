namespace Wakeel.Crypto;

/// <summary>
/// The two private seeds of a device identity. They never leave the installation key
/// file unencrypted; on the phone the equivalent material lives in the platform key store.
/// </summary>
public sealed record DeviceSeeds(byte[] SigningSeed, byte[] AgreementSeed)
{
    public const int SeedSize = 32;

    public void Validate()
    {
        if (SigningSeed is null || SigningSeed.Length != SeedSize
            || AgreementSeed is null || AgreementSeed.Length != SeedSize)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The device seeds are missing or have the wrong length.");
        }
    }

    public bool Equals(DeviceSeeds? other) =>
        other is not null
        && SigningSeed.AsSpan().SequenceEqual(other.SigningSeed)
        && AgreementSeed.AsSpan().SequenceEqual(other.AgreementSeed);

    public override int GetHashCode() =>
        HashCode.Combine(
            Convert.ToHexString(SigningSeed).GetHashCode(StringComparison.Ordinal),
            Convert.ToHexString(AgreementSeed).GetHashCode(StringComparison.Ordinal));
}
