using System.Text.Json.Serialization;

namespace Wakeel.Crypto;

/// <summary>Whether the certified device is an office computer or a paired phone.</summary>
public enum DeviceKind
{
    Pc,
    Phone,
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
    public static DeviceCertificate Issue(DeviceCertificateBody body, DeviceIdentity issuer)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(issuer);
        return new DeviceCertificate(body, issuer.SignText(SignedBytes(body)));
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
