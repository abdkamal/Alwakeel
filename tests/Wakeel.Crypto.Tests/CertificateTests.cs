namespace Wakeel.Crypto.Tests;

public class CertificateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_computer_certificate_issued_by_the_organisation_verifies()
    {
        using var pki = TestPki.Create();

        CertificateChain.Verify(pki.PcCertificate, pki.Org.SigningPublicKey, revocations: null, Now);

        Assert.True(pki.PcCertificate.VerifySignature(pki.Org.SigningPublicKey));
        Assert.Equal(DeviceKind.Pc, pki.PcCertificate.Body.Kind);
    }

    [Fact]
    public void A_computer_certificate_signed_by_a_stranger_is_refused()
    {
        using var pki = TestPki.Create();
        using var stranger = DeviceIdentity.Generate();

        Assert.False(CertificateChain.TryVerify(
            pki.PcCertificate,
            stranger.SigningPublicKey,
            revocations: null,
            Now,
            out var error));
        Assert.Equal(ErrorCode.BadSignature, error);
    }

    [Fact]
    public void A_changed_certificate_body_breaks_the_signature()
    {
        using var pki = TestPki.Create();
        var forged = pki.PcCertificate with
        {
            Body = pki.PcCertificate.Body with { Role = "manager" },
        };

        var failure = Assert.Throws<CryptoException>(
            () => CertificateChain.Verify(forged, pki.Org.SigningPublicKey, revocations: null, Now));
        Assert.Equal(ErrorCode.BadSignature, failure.Code);
    }

    [Fact]
    public void A_computer_certificate_that_names_another_issuer_is_refused()
    {
        using var pki = TestPki.Create();
        var forged = DeviceCertificate.Issue(
            pki.PcCertificate.Body with { IssuerId = "SOMEONE-ELSE" },
            pki.Org);

        var failure = Assert.Throws<CryptoException>(
            () => CertificateChain.Verify(forged, pki.Org.SigningPublicKey, revocations: null, Now));
        Assert.Equal(ErrorCode.BadSignature, failure.Code);
    }

    [Fact]
    public void A_phone_certificate_verifies_through_its_computer()
    {
        using var pki = TestPki.Create();

        CertificateChain.Verify(
            pki.PhoneCertificate,
            pki.Org.SigningPublicKey,
            revocations: null,
            Now,
            pki.PcCertificate);

        Assert.Equal("PC-1", pki.PhoneCertificate.Body.IssuerId);
    }

    [Fact]
    public void A_phone_certificate_without_its_computer_certificate_is_refused()
    {
        using var pki = TestPki.Create();

        var failure = Assert.Throws<CryptoException>(
            () => CertificateChain.Verify(pki.PhoneCertificate, pki.Org.SigningPublicKey, revocations: null, Now));
        Assert.Equal(ErrorCode.BadSignature, failure.Code);
    }

    [Fact]
    public void A_phone_certificate_signed_by_another_computer_is_refused()
    {
        using var pki = TestPki.Create();
        using var otherPc = DeviceIdentity.Generate();
        var forged = DeviceCertificate.Issue(pki.PhoneCertificate.Body, otherPc);

        var failure = Assert.Throws<CryptoException>(
            () => CertificateChain.Verify(forged, pki.Org.SigningPublicKey, revocations: null, Now, pki.PcCertificate));
        Assert.Equal(ErrorCode.BadSignature, failure.Code);
    }

    [Fact]
    public void A_revoked_computer_is_refused_from_the_moment_of_revocation()
    {
        using var pki = TestPki.Create();
        var revokedAt = Now.AddDays(-1);
        var list = pki.Revoke(TestPki.PcDeviceId, revokedAt);

        var failure = Assert.Throws<CryptoException>(
            () => CertificateChain.Verify(pki.PcCertificate, pki.Org.SigningPublicKey, list, Now));
        Assert.Equal(ErrorCode.Revoked, failure.Code);

        CertificateChain.Verify(pki.PcCertificate, pki.Org.SigningPublicKey, list, revokedAt.AddHours(-1));
    }

    [Fact]
    public void A_revoked_computer_takes_its_phone_down_with_it()
    {
        using var pki = TestPki.Create();
        var list = pki.Revoke(TestPki.PcDeviceId, Now.AddDays(-1));

        var failure = Assert.Throws<CryptoException>(
            () => CertificateChain.Verify(pki.PhoneCertificate, pki.Org.SigningPublicKey, list, Now, pki.PcCertificate));
        Assert.Equal(ErrorCode.Revoked, failure.Code);
    }

    [Fact]
    public void A_revocation_list_that_is_not_signed_by_the_organisation_is_refused()
    {
        using var pki = TestPki.Create();
        using var stranger = DeviceIdentity.Generate();
        var forged = RevocationList.Issue(
            new RevocationListBody(TestPki.OrgId, Now, [new RevocationEntry(TestPki.PcDeviceId, Now)]),
            stranger);

        var failure = Assert.Throws<CryptoException>(
            () => CertificateChain.Verify(pki.PcCertificate, pki.Org.SigningPublicKey, forged, Now));
        Assert.Equal(ErrorCode.BadSignature, failure.Code);
    }

    [Fact]
    public void An_empty_revocation_list_changes_nothing()
    {
        using var pki = TestPki.Create();
        var list = pki.NoRevocations(Now);

        CertificateChain.Verify(pki.PcCertificate, pki.Org.SigningPublicKey, list, Now);

        Assert.True(list.VerifySignature(pki.Org.SigningPublicKey));
        Assert.False(list.IsRevoked(TestPki.PcDeviceId, Now));
    }

    [Fact]
    public void A_certificate_dated_in_the_future_is_refused()
    {
        using var pki = TestPki.Create(Now.AddDays(5));

        var failure = Assert.Throws<CryptoException>(
            () => CertificateChain.Verify(pki.PcCertificate, pki.Org.SigningPublicKey, revocations: null, Now));
        Assert.Equal(ErrorCode.Expired, failure.Code);
    }

    [Fact]
    public void A_certificate_older_than_the_accepted_window_is_refused()
    {
        using var pki = TestPki.Create(Now.AddYears(-3));

        var failure = Assert.Throws<CryptoException>(
            () => CertificateChain.Verify(
                pki.PcCertificate,
                pki.Org.SigningPublicKey,
                revocations: null,
                Now,
                issuerCertificate: null,
                maxAge: TimeSpan.FromDays(365)));
        Assert.Equal(ErrorCode.Expired, failure.Code);

        CertificateChain.Verify(
            pki.PcCertificate,
            pki.Org.SigningPublicKey,
            revocations: null,
            Now,
            issuerCertificate: null,
            maxAge: TimeSpan.FromDays(4000));
    }

    [Fact]
    public void A_certificate_with_an_unreadable_key_is_reported_as_corrupt()
    {
        using var pki = TestPki.Create();
        var broken = pki.PcCertificate with
        {
            Body = pki.PcCertificate.Body with { X25519Pub = "###" },
        };

        var failure = Assert.Throws<CryptoException>(
            () => CertificateChain.Verify(broken, pki.Org.SigningPublicKey, revocations: null, Now));
        Assert.Equal(ErrorCode.Corrupt, failure.Code);
    }

    [Fact]
    public void A_revocation_list_issued_for_another_organisation_is_refused()
    {
        using var pki = TestPki.Create();
        var foreign = RevocationList.Issue(
            new RevocationListBody("ORG-OTHER", Now, [new RevocationEntry("PC-9", Now)]),
            pki.Org);

        var failure = Assert.Throws<CryptoException>(
            () => CertificateChain.Verify(pki.PcCertificate, pki.Org.SigningPublicKey, foreign, Now));
        Assert.Equal(ErrorCode.BadSignature, failure.Code);
    }

    [Fact]
    public void A_revocation_list_older_than_the_accepted_window_is_reported()
    {
        using var pki = TestPki.Create();
        var stale = pki.NoRevocations(Now.AddDays(-90));

        var failure = Assert.Throws<CryptoException>(
            () => CertificateChain.Verify(
                pki.PcCertificate,
                pki.Org.SigningPublicKey,
                stale,
                Now,
                issuerCertificate: null,
                maxAge: null,
                maxRevocationListAge: TimeSpan.FromDays(30)));
        Assert.Equal(ErrorCode.Expired, failure.Code);

        CertificateChain.Verify(
            pki.PcCertificate,
            pki.Org.SigningPublicKey,
            stale,
            Now,
            issuerCertificate: null,
            maxAge: null,
            maxRevocationListAge: TimeSpan.FromDays(365));
    }

    [Fact]
    public void A_revocation_list_dated_in_the_future_is_refused()
    {
        using var pki = TestPki.Create();
        var ahead = pki.NoRevocations(Now.AddDays(5));

        var failure = Assert.Throws<CryptoException>(
            () => CertificateChain.Verify(pki.PcCertificate, pki.Org.SigningPublicKey, ahead, Now));
        Assert.Equal(ErrorCode.Expired, failure.Code);
    }

    [Fact]
    public void A_freshness_window_applies_to_the_phone_and_not_to_the_computer_above_it()
    {
        // The computer was certified three years ago and the phone was paired yesterday: a
        // caller asking for a recent phone certificate must not be tripped by the older
        // computer certificate that stands above it.
        using var org = DeviceIdentity.Generate();
        using var pc = DeviceIdentity.Generate();
        using var phone = DeviceIdentity.Generate();

        var pcCertificate = DeviceCertificate.Issue(
            new DeviceCertificateBody(
                TestPki.OrgId,
                TestPki.OfficeId,
                TestPki.PcDeviceId,
                1,
                2,
                "secretary",
                DeviceKind.Pc,
                pc.SigningPublicKeyText,
                pc.AgreementPublicKeyText,
                Now.AddYears(-3),
                TestPki.OrgId),
            org);

        var phoneCertificate = DeviceCertificate.Issue(
            new DeviceCertificateBody(
                TestPki.OrgId,
                TestPki.OfficeId,
                TestPki.PhoneDeviceId,
                1,
                2,
                "secretary",
                DeviceKind.Phone,
                phone.SigningPublicKeyText,
                phone.AgreementPublicKeyText,
                Now.AddDays(-1),
                TestPki.PcDeviceId),
            pc);

        CertificateChain.Verify(
            phoneCertificate,
            org.SigningPublicKey,
            revocations: null,
            Now,
            pcCertificate,
            maxAge: TimeSpan.FromDays(30));
    }

    [Fact]
    public void A_certificate_signature_cannot_be_replayed_from_another_kind_of_structure()
    {
        using var pki = TestPki.Create();

        // The signature covers a labelled buffer, never the bare canonical body, so signing
        // the body alone does not produce a certificate that verifies.
        var unlabelled = new DeviceCertificate(
            pki.PcCertificate.Body,
            pki.Org.SignText(pki.PcCertificate.CanonicalBody()));

        Assert.False(unlabelled.VerifySignature(pki.Org.SigningPublicKey));
        Assert.True(pki.PcCertificate.VerifySignature(pki.Org.SigningPublicKey));
    }

    [Fact]
    public void A_certificate_survives_the_canonical_json_round_trip()
    {
        using var pki = TestPki.Create();

        var restored = CanonicalJson.Deserialize<DeviceCertificate>(
            CanonicalJson.SerializeToUtf8Bytes(pki.PcCertificate));

        Assert.Equal(pki.PcCertificate, restored);
        CertificateChain.Verify(restored, pki.Org.SigningPublicKey, revocations: null, Now);
    }
}
