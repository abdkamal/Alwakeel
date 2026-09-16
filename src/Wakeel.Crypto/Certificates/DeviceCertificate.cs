using System.Text.Json.Serialization;

namespace Wakeel.Crypto;

/// <summary>Whether the certified party is an office computer, a paired phone, or the organisation itself.</summary>
public enum DeviceKind
{
    Pc,
    Phone,

    /// <summary>
    /// The organisation root. It is not a device at all: it is the single root of trust,
    /// certifying itself, and it exists so that the one thing the organisation signs directly —
    /// the setup file — can travel through the same manifest and certificate model as everything
    /// else instead of needing a parallel one. Only a setup container may be produced by it,
    /// and it may never issue a phone certificate.
    /// </summary>
    Org,
}

/// <summary>
/// The signed body of a device certificate. Serialised canonically before signing, so the
/// bytes are identical on every machine.
/// </summary>
public sealed record DeviceCertificateBody(
    string OrgId,
    string OfficeId,
    string DeviceId,
    int DeviceNo,
    int EmployeeNo,
    string Role,
    DeviceKind Kind,
    string Ed25519Pub,
    string X25519Pub,
    DateTimeOffset IssuedAt,
    string IssuerId);

/// <summary>
/// A device certificate: the body plus the issuer's Ed25519 signature over its canonical bytes.
/// Computers are certified by the organisation key, phones by the computer that paired them.
/// </summary>
public sealed record DeviceCertificate(DeviceCertificateBody Body, string Signature)
{
    /// <summary>
    /// The role written into an organisation root certificate. It names no employee and
    /// carries no office, so it can never be mistaken for one of the three working roles.
    /// </summary>
    public const string OrgRole = "organisation";

    public static DeviceCertificate Issue(DeviceCertificateBody body, DeviceIdentity issuer)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(issuer);
        return new DeviceCertificate(body, issuer.SignText(SignedBytes(body)));
    }

    /// <summary>
    /// The organisation's own self signed certificate, the producer of every setup file.
    /// Its device identifier is the organisation identifier and it certifies its own keys, so
    /// a reader that already pinned the organisation key can tell at a glance whether this is
    /// the same organisation, and one that has not yet pinned anything learns the key it is
    /// about to trust from the same signed structure that proves possession of it.
    /// </summary>
    public static DeviceCertificate IssueOrgRoot(string orgId, DeviceIdentity orgIdentity, DateTimeOffset issuedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orgId);
        ArgumentNullException.ThrowIfNull(orgIdentity);

        return Issue(
            new DeviceCertificateBody(
                orgId,
                OfficeId: string.Empty,
                DeviceId: orgId,
                DeviceNo: 0,
                EmployeeNo: 0,
                OrgRole,
                DeviceKind.Org,
                orgIdentity.SigningPublicKeyText,
                orgIdentity.AgreementPublicKeyText,
                issuedAt,
                IssuerId: orgId),
            orgIdentity);
    }

    /// <summary>The labelled bytes an issuer signature covers.</summary>
    internal static byte[] SignedBytes(DeviceCertificateBody body) =>
        DomainSeparation.Wrap(DomainSeparation.Certificate, CanonicalJson.SerializeToUtf8Bytes(body));

    /// <summary>The certified device's Ed25519 public key.</summary>
    [JsonIgnore]
    public byte[] SigningPublicKey => Base64Url.Decode(Body.Ed25519Pub);

    /// <summary>The certified device's X25519 public key.</summary>
    [JsonIgnore]
    public byte[] AgreementPublicKey => Base64Url.Decode(Body.X25519Pub);

    public byte[] CanonicalBody() => CanonicalJson.SerializeToUtf8Bytes(Body);

    public bool VerifySignature(ReadOnlySpan<byte> issuerSigningPublicKey)
    {
        // A certificate parsed out of a hostile file can arrive without a body at all.
        if (Body is null || !Base64Url.TryDecode(Signature, out var signature))
        {
            return false;
        }

        return DeviceIdentity.Verify(issuerSigningPublicKey, SignedBytes(Body), signature);
    }
}
