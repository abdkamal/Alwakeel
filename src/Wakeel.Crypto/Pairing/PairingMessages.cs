using System.Security.Cryptography;
using System.Text;

namespace Wakeel.Crypto;

/// <summary>The signed body a phone writes when it answers a pairing invitation.</summary>
public sealed record PairingRequestBody(
    string OrgId,
    string OfficeId,
    string PcDeviceId,
    string PhoneDeviceId,
    string Ed25519Pub,
    string X25519Pub,
    string TokenProof,
    DateTimeOffset CreatedAt);

/// <summary>
/// The phone's answer: its own public keys plus a proof that it really saw the pairing token.
/// Self signed, because at this point the phone has no certificate yet.
/// </summary>
public sealed record PairingRequest(PairingRequestBody Body, string Signature)
{
    public static PairingRequest Create(PairingRequestBody body, DeviceIdentity phoneIdentity)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(phoneIdentity);
        return new PairingRequest(body, phoneIdentity.SignText(SignedBytes(body)));
    }

    public byte[] CanonicalBody() => CanonicalJson.SerializeToUtf8Bytes(Body);

    /// <summary>The labelled bytes the self signature covers.</summary>
    internal static byte[] SignedBytes(PairingRequestBody body) =>
        DomainSeparation.Wrap(DomainSeparation.PairingRequest, CanonicalJson.SerializeToUtf8Bytes(body));

    /// <summary>Checks the self signature against the public key inside the body.</summary>
    public bool VerifySignature() =>
        Body is not null && DeviceIdentity.Verify(Body.Ed25519Pub, SignedBytes(Body), Signature);

    /// <summary>Checks the token proof, in constant time.</summary>
    public bool VerifyToken(ReadOnlySpan<byte> token)
    {
        if (Body is null)
        {
            return false;
        }

        var expected = PairingCodes.TokenProof(token, Body.PhoneDeviceId);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(Body.TokenProof ?? string.Empty));
    }

    /// <summary>Signature, token proof and the session window, in one call.</summary>
    public void EnsureAcceptable(PairingQrPayload invitation, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (Body is null
            || string.IsNullOrEmpty(Body.Ed25519Pub)
            || string.IsNullOrEmpty(Body.X25519Pub)
            || string.IsNullOrEmpty(Body.PhoneDeviceId))
        {
            throw new CryptoException(ErrorCode.Corrupt, "The phone's answer is incomplete.");
        }

        if (!VerifySignature())
        {
            throw new CryptoException(ErrorCode.BadSignature, "The phone's answer is not signed by the keys it presents.");
        }

        if (!VerifyToken(Base64Url.Decode(invitation.Token)))
        {
            throw new CryptoException(ErrorCode.BadSignature, "The phone's answer does not belong to this pairing session.");
        }

        if (!string.Equals(Body.OrgId, invitation.OrgId, StringComparison.Ordinal)
            || !string.Equals(Body.OfficeId, invitation.OfficeId, StringComparison.Ordinal)
            || !string.Equals(Body.PcDeviceId, invitation.PcDeviceId, StringComparison.Ordinal))
        {
            throw new CryptoException(ErrorCode.BadSignature, "The phone's answer names a different office or computer.");
        }

        invitation.EnsureValid(timeProvider);

        var now = timeProvider.GetUtcNow();
        if (Body.CreatedAt < invitation.IssuedAt - TimeSpan.FromMinutes(1)
            || Body.CreatedAt > now + TimeSpan.FromMinutes(1))
        {
            throw new CryptoException(ErrorCode.Expired, "The phone's answer is dated outside the pairing session.");
        }
    }
}

/// <summary>The signed body the computer writes back once it accepts a phone.</summary>
public sealed record PairingAcceptBody(
    string OrgId,
    string OfficeId,
    string PcDeviceId,

    // The computer's own certificate, issued by the organisation. The phone receives nothing
    // else that carries the computer's signing key, so without it the phone could not build
    // the organisation → computer → phone chain from the material that crosses the cable.
    DeviceCertificate PcCertificate,
    string PhoneDeviceId,
    DeviceCertificate PhoneCertificate,
    string SealedSessionKey,
    string SealedOfficeKey,
    DateTimeOffset CreatedAt);

/// <summary>
/// The computer's answer: its own certificate and the phone certificate it just issued, plus
/// the session key and the office key sealed to the phone's X25519 public key.
/// </summary>
public sealed record PairingAccept(PairingAcceptBody Body, string Signature)
{
    /// <summary>Context label of the sealed session key.</summary>
    public const string SessionKeyContext = "wakeel.pairing.session";

    /// <summary>Context label of the sealed office key.</summary>
    public const string OfficeKeyContext = "wakeel.pairing.officekey";

    public static PairingAccept Create(PairingAcceptBody body, DeviceIdentity pcIdentity)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(pcIdentity);
        return new PairingAccept(body, pcIdentity.SignText(SignedBytes(body)));
    }

    /// <summary>
    /// Builds the accept message: checks the request, issues the phone certificate and seals
    /// the two keys to the phone. The invitation is a required argument precisely so that the
    /// check can never be forgotten by a caller.
    /// </summary>
    public static PairingAccept Issue(
        PairingRequest request,
        PairingQrPayload invitation,
        DeviceCertificate pcCertificate,
        DeviceIdentity pcIdentity,
        int phoneDeviceNo,
        int phoneEmployeeNo,
        string phoneRole,
        ReadOnlySpan<byte> sessionKey,
        ReadOnlySpan<byte> officeKey,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(invitation);
        ArgumentNullException.ThrowIfNull(pcCertificate);
        ArgumentNullException.ThrowIfNull(pcIdentity);
        ArgumentNullException.ThrowIfNull(timeProvider);

        // A pairing request is a file anyone able to write the phone's folder could have put
        // there, so it is verified before a certificate is issued or any key is sealed to it.
        request.EnsureAcceptable(invitation, timeProvider);

        if (pcCertificate.Body is null
            || !string.Equals(pcCertificate.Body.DeviceId, invitation.PcDeviceId, StringComparison.Ordinal))
        {
            throw new CryptoException(ErrorCode.BadSignature, "The invitation was not issued by this computer.");
        }

        var now = timeProvider.GetUtcNow();
        var phoneAgreementPublicKey = Base64Url.Decode(request.Body.X25519Pub);

        // The office and organisation names come from the computer's own certificate, never
        // from the request, so an answer can never mint a certificate for another office.
        var certificate = DeviceCertificate.Issue(
            new DeviceCertificateBody(
                pcCertificate.Body.OrgId,
                pcCertificate.Body.OfficeId,
                request.Body.PhoneDeviceId,
                phoneDeviceNo,
                phoneEmployeeNo,
                phoneRole,
                DeviceKind.Phone,
                request.Body.Ed25519Pub,
                request.Body.X25519Pub,
                now,
                pcCertificate.Body.DeviceId),
            pcIdentity);

        var body = new PairingAcceptBody(
            pcCertificate.Body.OrgId,
            pcCertificate.Body.OfficeId,
            pcCertificate.Body.DeviceId,
            pcCertificate,
            request.Body.PhoneDeviceId,
            certificate,
            Base64Url.Encode(DeviceIdentity.SealFor(phoneAgreementPublicKey, sessionKey, SessionKeyContext)),
            Base64Url.Encode(DeviceIdentity.SealFor(phoneAgreementPublicKey, officeKey, OfficeKeyContext)),
            now);

        return Create(body, pcIdentity);
    }

    public byte[] CanonicalBody() => CanonicalJson.SerializeToUtf8Bytes(Body);

    /// <summary>The labelled bytes the computer signature covers.</summary>
    internal static byte[] SignedBytes(PairingAcceptBody body) =>
        DomainSeparation.Wrap(DomainSeparation.PairingAccept, CanonicalJson.SerializeToUtf8Bytes(body));

    public bool VerifySignature(ReadOnlySpan<byte> pcSigningPublicKey) =>
        Body is not null
        && Base64Url.TryDecode(Signature, out var signature)
        && DeviceIdentity.Verify(pcSigningPublicKey, SignedBytes(Body), signature);

    /// <summary>
    /// Phone side: establishes the whole chain from material that actually crosses the cable —
    /// the invitation read from the image, this file, and the organisation key that arrived in
    /// the setup file. Returns the phone's own certificate once everything holds.
    /// </summary>
    public DeviceCertificate EnsureTrusted(
        ReadOnlySpan<byte> orgSigningPublicKey,
        PairingQrPayload invitation,
        RevocationList? revocations,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (Body is null || Body.PcCertificate?.Body is null || Body.PhoneCertificate?.Body is null)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The computer's answer is incomplete.");
        }

        var now = timeProvider.GetUtcNow();
        var pc = Body.PcCertificate;

        // Organisation → computer, from the organisation key the setup file carried.
        CertificateChain.Verify(pc, orgSigningPublicKey, revocations, now);

        if (!string.Equals(pc.Body.DeviceId, invitation.PcDeviceId, StringComparison.Ordinal)
            || !string.Equals(pc.Body.OrgId, invitation.OrgId, StringComparison.Ordinal)
            || !string.Equals(pc.Body.OfficeId, invitation.OfficeId, StringComparison.Ordinal)
            || !string.Equals(pc.Body.X25519Pub, invitation.PcX25519Pub, StringComparison.Ordinal))
        {
            throw new CryptoException(ErrorCode.BadSignature, "The answer comes from a different computer than the invitation.");
        }

        if (!VerifySignature(pc.SigningPublicKey))
        {
            throw new CryptoException(ErrorCode.BadSignature, "The computer's answer is not signed by that computer.");
        }

        if (Body.CreatedAt > now + TimeSpan.FromDays(1))
        {
            throw new CryptoException(ErrorCode.Expired, "The computer's answer is dated in the future.");
        }

        if (!string.Equals(Body.PhoneCertificate.Body.DeviceId, Body.PhoneDeviceId, StringComparison.Ordinal))
        {
            throw new CryptoException(ErrorCode.BadSignature, "The answer carries a certificate for another phone.");
        }

        // Organisation → computer → phone, with the computer certificate this file supplied.
        CertificateChain.Verify(Body.PhoneCertificate, orgSigningPublicKey, revocations, now, pc);

        return Body.PhoneCertificate;
    }

    /// <summary>Phone side: opens the session key sealed to this phone.</summary>
    public byte[] OpenSessionKey(DeviceIdentity phoneIdentity)
    {
        ArgumentNullException.ThrowIfNull(phoneIdentity);
        return phoneIdentity.Open(Base64Url.Decode(Body.SealedSessionKey), SessionKeyContext);
    }

    /// <summary>Phone side: opens the office key sealed to this phone.</summary>
    public byte[] OpenOfficeKey(DeviceIdentity phoneIdentity)
    {
        ArgumentNullException.ThrowIfNull(phoneIdentity);
        return phoneIdentity.Open(Base64Url.Decode(Body.SealedOfficeKey), OfficeKeyContext);
    }
}
