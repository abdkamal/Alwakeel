namespace Wakeel.Crypto;

/// <summary>
/// Verifies the two chains the product uses: organisation → computer, and
/// organisation → computer → phone.
/// </summary>
public static class CertificateChain
{
    /// <summary>How far ahead of the verifier's clock an issuance instant may sit.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromDays(1);

    /// <summary>
    /// Throws <see cref="CryptoException"/> when the certificate is not trustworthy right now.
    /// </summary>
    /// <param name="certificate">The certificate being presented.</param>
    /// <param name="orgSigningPublicKey">The organisation Ed25519 public key, the single root of trust.</param>
    /// <param name="revocations">The newest signed revocation list, when one is available.</param>
    /// <param name="now">The verifier's current instant.</param>
    /// <param name="issuerCertificate">The computer certificate, required for a phone certificate.</param>
    /// <param name="maxAge">Optional freshness window measured from the issuance instant.</param>
    /// <param name="maxRevocationListAge">Optional freshness window for the revocation list itself.</param>
    public static void Verify(
        DeviceCertificate certificate,
        ReadOnlySpan<byte> orgSigningPublicKey,
        RevocationList? revocations,
        DateTimeOffset now,
        DeviceCertificate? issuerCertificate = null,
        TimeSpan? maxAge = null,
        TimeSpan? maxRevocationListAge = null)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (orgSigningPublicKey.Length != DeviceIdentity.PublicKeySize)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The organisation public key has the wrong length.");
        }

        ValidateShape(certificate);

        if (revocations is not null)
        {
            if (!revocations.VerifySignature(orgSigningPublicKey))
            {
                throw new CryptoException(ErrorCode.BadSignature, "The revocation list is not signed by the organisation.");
            }

            // A list signed by the organisation for a different organisation says nothing here.
            if (revocations.Body is null
                || !string.Equals(revocations.Body.OrgId, certificate.Body.OrgId, StringComparison.Ordinal))
            {
                throw new CryptoException(ErrorCode.BadSignature, "The revocation list belongs to a different organisation.");
            }

            if (maxRevocationListAge is { } listAge && now - revocations.Body.IssuedAt > listAge)
            {
                throw new CryptoException(ErrorCode.Expired, "The revocation list is older than the accepted window.");
            }

            if (revocations.Body.IssuedAt > now + ClockSkew)
            {
                throw new CryptoException(ErrorCode.Expired, "The revocation list is dated in the future.");
            }
        }

        var orgKey = orgSigningPublicKey.ToArray();
        VerifyNode(certificate, orgKey, revocations, now, issuerCertificate, maxAge, depth: 0);
    }

    /// <summary>Non throwing form used where a boolean is enough.</summary>
    public static bool TryVerify(
        DeviceCertificate certificate,
        ReadOnlySpan<byte> orgSigningPublicKey,
        RevocationList? revocations,
        DateTimeOffset now,
        out ErrorCode error,
        DeviceCertificate? issuerCertificate = null,
        TimeSpan? maxAge = null,
        TimeSpan? maxRevocationListAge = null)
    {
        try
        {
            Verify(certificate, orgSigningPublicKey, revocations, now, issuerCertificate, maxAge, maxRevocationListAge);
            error = default;
            return true;
        }
        catch (CryptoException exception)
        {
            error = exception.Code;
            return false;
        }
    }

    private static void VerifyNode(
        DeviceCertificate certificate,
        byte[] orgSigningPublicKey,
        RevocationList? revocations,
        DateTimeOffset now,
        DeviceCertificate? issuerCertificate,
        TimeSpan? maxAge,
        int depth)
    {
        if (depth > 2)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The certificate chain is longer than the product allows.");
        }

        switch (certificate.Body.Kind)
        {
            case DeviceKind.Pc:
                if (!string.Equals(certificate.Body.IssuerId, certificate.Body.OrgId, StringComparison.Ordinal))
                {
                    throw new CryptoException(ErrorCode.BadSignature, "A computer certificate must be issued by the organisation.");
                }

                if (!certificate.VerifySignature(orgSigningPublicKey))
                {
                    throw new CryptoException(ErrorCode.BadSignature, "The computer certificate signature does not match the organisation key.");
                }

                break;

            case DeviceKind.Phone:
                if (issuerCertificate is null)
                {
                    throw new CryptoException(ErrorCode.BadSignature, "A phone certificate cannot be checked without the computer certificate that issued it.");
                }

                ValidateShape(issuerCertificate);

                if (issuerCertificate.Body.Kind != DeviceKind.Pc)
                {
                    throw new CryptoException(ErrorCode.BadSignature, "A phone certificate may only be issued by a computer.");
                }

                if (!string.Equals(certificate.Body.IssuerId, issuerCertificate.Body.DeviceId, StringComparison.Ordinal))
                {
                    throw new CryptoException(ErrorCode.BadSignature, "The phone certificate names a different issuing computer.");
                }

                if (!string.Equals(certificate.Body.OrgId, issuerCertificate.Body.OrgId, StringComparison.Ordinal)
                    || !string.Equals(certificate.Body.OfficeId, issuerCertificate.Body.OfficeId, StringComparison.Ordinal))
                {
                    throw new CryptoException(ErrorCode.BadSignature, "The phone and the issuing computer do not belong to the same office.");
                }

                // The freshness window applies to the certificate that was presented, not to the
                // computer certificate above it, which is naturally older.
                VerifyNode(issuerCertificate, orgSigningPublicKey, revocations, now, issuerCertificate: null, maxAge: null, depth + 1);

                if (!certificate.VerifySignature(issuerCertificate.SigningPublicKey))
                {
                    throw new CryptoException(ErrorCode.BadSignature, "The phone certificate signature does not match the issuing computer key.");
                }

                break;

            default:
                throw new CryptoException(ErrorCode.UnknownKind, "The certificate names a device kind this version does not know.");
        }

        if (certificate.Body.IssuedAt > now + ClockSkew)
        {
            throw new CryptoException(ErrorCode.Expired, "The certificate is dated in the future.");
        }

        if (maxAge is { } age && now - certificate.Body.IssuedAt > age)
        {
            throw new CryptoException(ErrorCode.Expired, "The certificate is older than the accepted window.");
        }

        if (revocations is not null && revocations.IsRevoked(certificate.Body.DeviceId, now))
        {
            throw new CryptoException(ErrorCode.Revoked, "The device appears on the revocation list.");
        }
    }

    private static void ValidateShape(DeviceCertificate certificate)
    {
        var body = certificate.Body;
        if (body is null)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The certificate carries no contents.");
        }

        if (string.IsNullOrWhiteSpace(body.OrgId)
            || string.IsNullOrWhiteSpace(body.OfficeId)
            || string.IsNullOrWhiteSpace(body.DeviceId)
            || string.IsNullOrWhiteSpace(body.IssuerId))
        {
            throw new CryptoException(ErrorCode.Corrupt, "The certificate is missing one of its identifiers.");
        }

        if (!Base64Url.TryDecode(body.Ed25519Pub, out var signing) || signing.Length != DeviceIdentity.PublicKeySize)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The certificate signing key is not readable.");
        }

        if (!Base64Url.TryDecode(body.X25519Pub, out var agreement) || agreement.Length != DeviceIdentity.PublicKeySize)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The certificate agreement key is not readable.");
        }

        if (string.IsNullOrEmpty(certificate.Signature))
        {
            throw new CryptoException(ErrorCode.BadSignature, "The certificate carries no signature.");
        }
    }
}
