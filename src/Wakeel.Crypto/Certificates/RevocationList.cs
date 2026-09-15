namespace Wakeel.Crypto;

/// <summary>One revoked device and the instant the revocation takes effect.</summary>
public sealed record RevocationEntry(string DeviceId, DateTimeOffset RevokedAt);

/// <summary>The signed body of a revocation list.</summary>
public sealed record RevocationListBody(
    string OrgId,
    DateTimeOffset IssuedAt,
    IReadOnlyList<RevocationEntry> Entries);

/// <summary>
/// The revocation list the organisation distributes inside updated setup files.
/// Only the organisation signing key may issue it.
/// </summary>
public sealed record RevocationList(RevocationListBody Body, string Signature)
{
    public static RevocationList Empty(string orgId, DateTimeOffset issuedAt, DeviceIdentity orgIdentity) =>
        Issue(new RevocationListBody(orgId, issuedAt, []), orgIdentity);

    public static RevocationList Issue(RevocationListBody body, DeviceIdentity orgIdentity)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(orgIdentity);
        return new RevocationList(body, orgIdentity.SignText(SignedBytes(body)));
    }

    public byte[] CanonicalBody() => CanonicalJson.SerializeToUtf8Bytes(Body);

    /// <summary>The labelled bytes the organisation signature covers.</summary>
    internal static byte[] SignedBytes(RevocationListBody body) =>
        DomainSeparation.Wrap(DomainSeparation.RevocationList, CanonicalJson.SerializeToUtf8Bytes(body));

    public bool VerifySignature(ReadOnlySpan<byte> orgSigningPublicKey)
    {
        if (Body is null || !Base64Url.TryDecode(Signature, out var signature))
        {
            return false;
        }

        return DeviceIdentity.Verify(orgSigningPublicKey, SignedBytes(Body), signature);
    }

    public bool IsRevoked(string deviceId, DateTimeOffset at)
    {
        if (string.IsNullOrEmpty(deviceId) || Body is null || Body.Entries is null)
        {
            return false;
        }

        foreach (var entry in Body.Entries)
        {
            if (entry is not null
                && string.Equals(entry.DeviceId, deviceId, StringComparison.Ordinal)
                && at >= entry.RevokedAt)
            {
                return true;
            }
        }

        return false;
    }
}
