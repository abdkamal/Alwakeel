using System.Text.Json.Serialization;

namespace Wakeel.Crypto;

/// <summary>
/// What the computer puts in the pairing QR image: canonical JSON rendered as base64url.
/// It is valid for ten minutes and is consumed once.
/// </summary>
public sealed record PairingQrPayload(
    string OrgId,
    string OfficeId,
    string PcDeviceId,
    string PcX25519Pub,
    string Token,
    DateTimeOffset IssuedAt)
{
    /// <summary>How long a pairing session stays open.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public static PairingQrPayload Create(
        string orgId,
        string officeId,
        string pcDeviceId,
        ReadOnlySpan<byte> pcAgreementPublicKey,
        PairingToken token,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(timeProvider);
        return new PairingQrPayload(
            orgId,
            officeId,
            pcDeviceId,
            Base64Url.Encode(pcAgreementPublicKey),
            token.Text,
            timeProvider.GetUtcNow());
    }

    [JsonIgnore]
    public PairingToken PairingToken => PairingToken.FromText(Token);

    /// <summary>The six digit fallback code for this session.</summary>
    public string ShortCode() => PairingCodes.ShortCode(Base64Url.Decode(Token));

    /// <summary>The text placed in the QR image.</summary>
    public string ToBase64Url() => Base64Url.Encode(CanonicalJson.SerializeToUtf8Bytes(this));

    public static PairingQrPayload FromBase64Url(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        return CanonicalJson.Deserialize<PairingQrPayload>(Base64Url.Decode(value));
    }

    public bool IsExpired(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return timeProvider.GetUtcNow() >= IssuedAt + Lifetime;
    }

    public void EnsureValid(TimeProvider timeProvider)
    {
        if (IsExpired(timeProvider))
        {
            throw new CryptoException(ErrorCode.Expired, "The pairing session is no longer open.");
        }
    }
}
