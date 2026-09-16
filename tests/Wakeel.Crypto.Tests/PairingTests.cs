namespace Wakeel.Crypto.Tests;

public class PairingTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_pairing_token_is_thirty_two_bytes_and_never_repeats()
    {
        var first = PairingToken.Generate();
        var second = PairingToken.Generate();

        Assert.Equal(PairingToken.TokenSize, first.Value.Length);
        Assert.NotEqual(first.Text, second.Text);
        Assert.Equal(first.Value, PairingToken.FromText(first.Text).Value);
    }

    [Fact]
    public void The_short_code_is_six_digits_and_always_the_same_for_one_token()
    {
        var token = PairingToken.Generate();

        var code = token.ShortCode;

        Assert.Equal(PairingCodes.Digits, code.Length);
        Assert.All(code, character => Assert.True(char.IsAsciiDigit(character)));
        Assert.Equal(code, PairingCodes.ShortCode(token.Value));
    }

    [Fact]
    public void The_short_code_of_a_known_token_is_stable()
    {
        var token = new PairingToken(new byte[PairingToken.TokenSize]);

        Assert.Equal(PairingCodes.ShortCode(new byte[PairingToken.TokenSize]), token.ShortCode);
        Assert.Equal(6, token.ShortCode.Length);
    }

    [Fact]
    public void Two_tokens_almost_never_share_a_short_code()
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        for (var attempt = 0; attempt < 200; attempt++)
        {
            codes.Add(PairingToken.Generate().ShortCode);
        }

        Assert.True(codes.Count >= 198, $"expected nearly two hundred distinct codes, got {codes.Count}");
    }

    [Fact]
    public void The_qr_payload_round_trips_through_base64url()
    {
        using var pki = TestPki.Create();
        var clock = new FixedClock(Now);
        var token = PairingToken.Generate();

        var payload = PairingQrPayload.Create(
            TestPki.OrgId,
            TestPki.OfficeId,
            TestPki.PcDeviceId,
            pki.Pc.AgreementPublicKey,
            token,
            clock);

        var restored = PairingQrPayload.FromBase64Url(payload.ToBase64Url());

        Assert.Equal(payload, restored);
        Assert.Equal(pki.Pc.AgreementPublicKeyText, restored.PcX25519Pub);
        Assert.Equal(token.ShortCode, restored.ShortCode());
    }

    [Fact]
    public void A_pairing_session_closes_after_ten_minutes()
    {
        using var pki = TestPki.Create();
        var clock = new FixedClock(Now);
        var payload = PairingQrPayload.Create(
            TestPki.OrgId,
            TestPki.OfficeId,
            TestPki.PcDeviceId,
            pki.Pc.AgreementPublicKey,
            PairingToken.Generate(),
            clock);

        clock.Advance(TimeSpan.FromMinutes(9));
        Assert.False(payload.IsExpired(clock));
        payload.EnsureValid(clock);

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(payload.IsExpired(clock));
        var error = Assert.Throws<CryptoException>(() => payload.EnsureValid(clock));
        Assert.Equal(ErrorCode.Expired, error.Code);
    }

    [Fact]
    public void A_pairing_request_is_self_signed_and_carries_a_token_proof()
    {
        using var pki = TestPki.Create();
        var token = PairingToken.Generate();
        var request = BuildRequest(pki, token, Now);

        Assert.True(request.VerifySignature());
        Assert.True(request.VerifyToken(token.Value));
        Assert.False(request.VerifyToken(PairingToken.Generate().Value));
    }

    [Fact]
    public void A_changed_pairing_request_body_breaks_its_signature()
    {
        using var pki = TestPki.Create();
        var request = BuildRequest(pki, PairingToken.Generate(), Now);

        var forged = request with { Body = request.Body with { PhoneDeviceId = "PHONE-9" } };

        Assert.False(forged.VerifySignature());
    }

    [Fact]
    public void A_pairing_request_from_another_session_is_refused()
    {
        using var pki = TestPki.Create();
        var clock = new FixedClock(Now);
        var invitation = PairingQrPayload.Create(
            TestPki.OrgId,
            TestPki.OfficeId,
            TestPki.PcDeviceId,
            pki.Pc.AgreementPublicKey,
            PairingToken.Generate(),
            clock);

        var request = BuildRequest(pki, PairingToken.Generate(), Now);

        var error = Assert.Throws<CryptoException>(() => request.EnsureAcceptable(invitation, clock));
        Assert.Equal(ErrorCode.BadSignature, error.Code);
    }

    [Fact]
    public void A_pairing_request_that_arrives_too_late_is_refused()
    {
        using var pki = TestPki.Create();
        var clock = new FixedClock(Now);
        var token = PairingToken.Generate();
        var invitation = PairingQrPayload.Create(
            TestPki.OrgId,
            TestPki.OfficeId,
            TestPki.PcDeviceId,
            pki.Pc.AgreementPublicKey,
            token,
            clock);

        var request = BuildRequest(pki, token, Now);
        clock.Advance(TimeSpan.FromMinutes(11));

        var error = Assert.Throws<CryptoException>(() => request.EnsureAcceptable(invitation, clock));
        Assert.Equal(ErrorCode.Expired, error.Code);
    }

    [Fact]
    public void The_whole_pairing_exchange_ends_with_a_phone_that_can_be_trusted()
    {
        using var pki = TestPki.Create();
        var clock = new FixedClock(Now);
        var token = PairingToken.Generate();
        var officeKey = RandomBytes.Next(32);

        var invitation = PairingQrPayload.Create(
            TestPki.OrgId,
            TestPki.OfficeId,
            TestPki.PcDeviceId,
            pki.Pc.AgreementPublicKey,
            token,
            clock);

        var request = BuildRequest(pki, token, Now);
        request.EnsureAcceptable(invitation, clock);

        var sessionKey = pki.Pc.Agree(Base64Url.Decode(request.Body.X25519Pub), "wakeel.phone.session");
        var accept = PairingAccept.Issue(
            request,
            invitation,
            pki.PcCertificate,
            pki.Pc,
            phoneDeviceNo: 1,
            phoneEmployeeNo: 2,
            phoneRole: "secretary",
            sessionKey,
            officeKey,
            clock);

        Assert.True(accept.VerifySignature(pki.Pc.SigningPublicKey));
        Assert.Equal(sessionKey, accept.OpenSessionKey(pki.Phone));
        Assert.Equal(officeKey, accept.OpenOfficeKey(pki.Phone));
        Assert.Equal(sessionKey, pki.Phone.Agree(pki.Pc.AgreementPublicKey, "wakeel.phone.session"));

        CertificateChain.Verify(
            accept.Body.PhoneCertificate,
            pki.Org.SigningPublicKey,
            revocations: null,
            Now,
            pki.PcCertificate);
    }

    [Fact]
    public void Another_phone_cannot_open_the_keys_in_an_accept_message()
    {
        using var pki = TestPki.Create();
        using var stranger = DeviceIdentity.Generate();
        var clock = new FixedClock(Now);
        var token = PairingToken.Generate();

        var request = BuildRequest(pki, token, Now);
        var accept = PairingAccept.Issue(
            request,
            BuildInvitation(pki, token, clock),
            pki.PcCertificate,
            pki.Pc,
            1,
            2,
            "secretary",
            RandomBytes.Next(32),
            RandomBytes.Next(32),
            clock);

        Assert.Throws<CryptoException>(() => accept.OpenOfficeKey(stranger));
    }

    [Fact]
    public void A_changed_accept_body_breaks_its_signature()
    {
        using var pki = TestPki.Create();
        var clock = new FixedClock(Now);
        var token = PairingToken.Generate();
        var request = BuildRequest(pki, token, Now);
        var accept = PairingAccept.Issue(
            request,
            BuildInvitation(pki, token, clock),
            pki.PcCertificate,
            pki.Pc,
            1,
            2,
            "secretary",
            RandomBytes.Next(32),
            RandomBytes.Next(32),
            clock);

        var forged = accept with { Body = accept.Body with { PhoneDeviceId = "PHONE-9" } };

        Assert.False(forged.VerifySignature(pki.Pc.SigningPublicKey));
        Assert.False(accept.VerifySignature(pki.Org.SigningPublicKey));
    }

    [Fact]
    public void An_answer_is_never_issued_for_a_request_that_does_not_hold_up()
    {
        using var pki = TestPki.Create();
        var clock = new FixedClock(Now);
        var token = PairingToken.Generate();
        var invitation = BuildInvitation(pki, token, clock);

        // A request whose proof belongs to a different session.
        var strayToken = BuildRequest(pki, PairingToken.Generate(), Now);
        var first = Assert.Throws<CryptoException>(() => Issue(pki, strayToken, invitation, clock));
        Assert.Equal(ErrorCode.BadSignature, first.Code);

        // A request whose body was edited after it was signed.
        var good = BuildRequest(pki, token, Now);
        var forged = good with { Body = good.Body with { X25519Pub = DeviceIdentity.Generate().AgreementPublicKeyText } };
        var second = Assert.Throws<CryptoException>(() => Issue(pki, forged, invitation, clock));
        Assert.Equal(ErrorCode.BadSignature, second.Code);

        // A request that arrives after the session has closed.
        var late = new FixedClock(Now);
        var invitationBefore = BuildInvitation(pki, token, late);
        late.Advance(TimeSpan.FromMinutes(11));
        var third = Assert.Throws<CryptoException>(() => Issue(pki, good, invitationBefore, late));
        Assert.Equal(ErrorCode.Expired, third.Code);
    }

    [Fact]
    public void An_answer_certifies_the_office_of_the_computer_that_issued_it()
    {
        using var pki = TestPki.Create();
        var clock = new FixedClock(Now);
        var token = PairingToken.Generate();
        var invitation = BuildInvitation(pki, token, clock);

        // The phone signs a request that names another office; the answer must still certify
        // the office of the computer that is doing the pairing.
        var request = PairingRequest.Create(
            new PairingRequestBody(
                TestPki.OrgId,
                TestPki.OfficeId,
                TestPki.PcDeviceId,
                TestPki.PhoneDeviceId,
                pki.Phone.SigningPublicKeyText,
                pki.Phone.AgreementPublicKeyText,
                PairingCodes.TokenProof(token.Value, TestPki.PhoneDeviceId),
                Now),
            pki.Phone);

        var accept = Issue(pki, request, invitation, clock);

        Assert.Equal(pki.PcCertificate.Body.OfficeId, accept.Body.PhoneCertificate.Body.OfficeId);
        Assert.Equal(pki.PcCertificate.Body.OrgId, accept.Body.PhoneCertificate.Body.OrgId);
        Assert.Equal(pki.PcCertificate, accept.Body.PcCertificate);
    }

    [Fact]
    public void The_phone_builds_the_whole_chain_from_what_crosses_the_cable()
    {
        using var pki = TestPki.Create();
        var clock = new FixedClock(Now);
        var token = PairingToken.Generate();

        // What the phone really has: the invitation it read from the image, the answer file,
        // and the organisation key that came in the setup file. Nothing else.
        var invitation = PairingQrPayload.FromBase64Url(BuildInvitation(pki, token, clock).ToBase64Url());
        var request = BuildRequest(pki, token, Now);
        var accept = CanonicalJson.Deserialize<PairingAccept>(
            CanonicalJson.SerializeToUtf8Bytes(Issue(pki, request, invitation, clock)));
        var orgKey = pki.Org.SigningPublicKey;

        var phoneCertificate = accept.EnsureTrusted(orgKey, invitation, pki.NoRevocations(Now), clock, pki.Phone);

        Assert.Equal(TestPki.PhoneDeviceId, phoneCertificate.Body.DeviceId);
        Assert.Equal(DeviceKind.Phone, phoneCertificate.Body.Kind);
        Assert.Equal(32, accept.OpenOfficeKey(pki.Phone).Length);
    }

    [Fact]
    public void The_phone_refuses_an_answer_from_a_computer_the_organisation_never_certified()
    {
        using var pki = TestPki.Create();
        using var impostor = DeviceIdentity.Generate();
        var clock = new FixedClock(Now);
        var token = PairingToken.Generate();

        var impostorCertificate = DeviceCertificate.Issue(
            new DeviceCertificateBody(
                TestPki.OrgId,
                TestPki.OfficeId,
                TestPki.PcDeviceId,
                1,
                2,
                "secretary",
                DeviceKind.Pc,
                impostor.SigningPublicKeyText,
                impostor.AgreementPublicKeyText,
                Now,
                TestPki.OrgId),
            impostor);

        var invitation = PairingQrPayload.Create(
            TestPki.OrgId,
            TestPki.OfficeId,
            TestPki.PcDeviceId,
            impostor.AgreementPublicKey,
            token,
            clock);

        var request = BuildRequest(pki, token, Now);
        var accept = PairingAccept.Issue(
            request,
            invitation,
            impostorCertificate,
            impostor,
            1,
            2,
            "secretary",
            RandomBytes.Next(32),
            RandomBytes.Next(32),
            clock);

        var error = Assert.Throws<CryptoException>(
            () => accept.EnsureTrusted(pki.Org.SigningPublicKey, invitation, revocations: null, clock, pki.Phone));
        Assert.Equal(ErrorCode.BadSignature, error.Code);
    }

    [Fact]
    public void The_phone_refuses_an_answer_that_belongs_to_another_invitation()
    {
        using var pki = TestPki.Create();
        var clock = new FixedClock(Now);
        var token = PairingToken.Generate();
        var invitation = BuildInvitation(pki, token, clock);
        var accept = Issue(pki, BuildRequest(pki, token, Now), invitation, clock);

        var otherInvitation = PairingQrPayload.Create(
            TestPki.OrgId,
            TestPki.OfficeId,
            "PC-7",
            pki.Pc.AgreementPublicKey,
            token,
            clock);

        var error = Assert.Throws<CryptoException>(
            () => accept.EnsureTrusted(pki.Org.SigningPublicKey, otherInvitation, revocations: null, clock, pki.Phone));
        Assert.Equal(ErrorCode.BadSignature, error.Code);
    }

    [Fact]
    public void An_issued_certificate_for_different_keys_than_this_phone_is_refused()
    {
        using var pki = TestPki.Create();
        using var impostor = DeviceIdentity.Generate();
        var clock = new FixedClock(Now);
        var token = PairingToken.Generate();
        var invitation = BuildInvitation(pki, token, clock);

        // A request signed with someone else's keys, but still claiming to be TestPki.PhoneDeviceId.
        var request = PairingRequest.Create(
            new PairingRequestBody(
                TestPki.OrgId,
                TestPki.OfficeId,
                TestPki.PcDeviceId,
                TestPki.PhoneDeviceId,
                impostor.SigningPublicKeyText,
                impostor.AgreementPublicKeyText,
                PairingCodes.TokenProof(token.Value, TestPki.PhoneDeviceId),
                Now),
            impostor);

        var accept = Issue(pki, request, invitation, clock);

        // The real phone (pki.Phone) receives this answer file. The certificate it carries
        // chains cleanly to the organisation and names the right device id, but it was issued
        // for someone else's public keys, so this phone must refuse it rather than store a
        // certificate it can never actually use to sign anything.
        var error = Assert.Throws<CryptoException>(
            () => accept.EnsureTrusted(pki.Org.SigningPublicKey, invitation, pki.NoRevocations(Now), clock, pki.Phone));
        Assert.Equal(ErrorCode.BadSignature, error.Code);
    }

    [Fact]
    public void An_answer_with_no_body_reports_corruption_not_a_null_reference()
    {
        using var pki = TestPki.Create();
        var accept = CanonicalJson.Deserialize<PairingAccept>("{\"signature\":\"AA\"}");

        var sessionError = Assert.Throws<CryptoException>(() => accept.OpenSessionKey(pki.Phone));
        Assert.Equal(ErrorCode.Corrupt, sessionError.Code);

        var officeError = Assert.Throws<CryptoException>(() => accept.OpenOfficeKey(pki.Phone));
        Assert.Equal(ErrorCode.Corrupt, officeError.Code);
    }

    [Theory]
    [InlineData("orgId")]
    [InlineData("officeId")]
    [InlineData("pcDeviceId")]
    public void An_answer_whose_plain_fields_disagree_with_its_own_certificate_is_refused(string field)
    {
        using var pki = TestPki.Create();
        var clock = new FixedClock(Now);
        var token = PairingToken.Generate();
        var invitation = BuildInvitation(pki, token, clock);
        var request = BuildRequest(pki, token, Now);

        var phoneCertificate = IssuePhoneCertificate(
            pki,
            request.Body.PhoneDeviceId,
            request.Body.Ed25519Pub,
            request.Body.X25519Pub);

        // Exactly one plain field disagrees with the same value inside the certificate the body
        // carries, and the whole file is still validly signed by the computer's real key: only
        // an already org-certified computer could ever produce such a thing, so this proves the
        // phone checks each field rather than trusting the plain copy. One case per field, so a
        // regression that dropped a single comparison cannot hide behind the other two.
        var body = BuildAcceptBody(
            pki,
            phoneCertificate,
            orgId: field == "orgId" ? "ANOTHER-ORG" : pki.PcCertificate.Body.OrgId,
            officeId: field == "officeId" ? "ANOTHER-OFFICE" : pki.PcCertificate.Body.OfficeId,
            pcDeviceId: field == "pcDeviceId" ? "ANOTHER-PC" : pki.PcCertificate.Body.DeviceId,
            phoneDeviceId: request.Body.PhoneDeviceId);

        var accept = PairingAccept.Create(body, pki.Pc);

        var error = Assert.Throws<CryptoException>(
            () => accept.EnsureTrusted(pki.Org.SigningPublicKey, invitation, pki.NoRevocations(Now), clock, pki.Phone));
        Assert.Equal(ErrorCode.BadSignature, error.Code);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void An_issued_certificate_that_does_not_carry_both_of_this_phones_keys_is_refused(
        bool signingMatches,
        bool agreementMatches)
    {
        using var pki = TestPki.Create();
        using var impostor = DeviceIdentity.Generate();
        var clock = new FixedClock(Now);
        var token = PairingToken.Generate();
        var invitation = BuildInvitation(pki, token, clock);

        // The certificate chains cleanly and names this very phone, but one of its two keys is
        // somebody else's. The signing key alone is not enough to check: a certificate with the
        // right signing key and a foreign agreement key would leave every packet later sealed
        // to "this phone" readable by the device that holds the other private half.
        var phoneCertificate = IssuePhoneCertificate(
            pki,
            TestPki.PhoneDeviceId,
            signingMatches ? pki.Phone.SigningPublicKeyText : impostor.SigningPublicKeyText,
            agreementMatches ? pki.Phone.AgreementPublicKeyText : impostor.AgreementPublicKeyText);

        var accept = PairingAccept.Create(
            BuildAcceptBody(
                pki,
                phoneCertificate,
                pki.PcCertificate.Body.OrgId,
                pki.PcCertificate.Body.OfficeId,
                pki.PcCertificate.Body.DeviceId,
                TestPki.PhoneDeviceId),
            pki.Pc);

        var error = Assert.Throws<CryptoException>(
            () => accept.EnsureTrusted(pki.Org.SigningPublicKey, invitation, pki.NoRevocations(Now), clock, pki.Phone));
        Assert.Equal(ErrorCode.BadSignature, error.Code);
    }

    private static DeviceCertificate IssuePhoneCertificate(
        TestPki pki,
        string phoneDeviceId,
        string ed25519Pub,
        string x25519Pub) =>
        DeviceCertificate.Issue(
            new DeviceCertificateBody(
                pki.PcCertificate.Body.OrgId,
                pki.PcCertificate.Body.OfficeId,
                phoneDeviceId,
                1,
                2,
                "secretary",
                DeviceKind.Phone,
                ed25519Pub,
                x25519Pub,
                Now,
                pki.PcCertificate.Body.DeviceId),
            pki.Pc);

    private static PairingAcceptBody BuildAcceptBody(
        TestPki pki,
        DeviceCertificate phoneCertificate,
        string orgId,
        string officeId,
        string pcDeviceId,
        string phoneDeviceId) =>
        new(
            orgId,
            officeId,
            pcDeviceId,
            pki.PcCertificate,
            phoneDeviceId,
            phoneCertificate,
            Base64Url.Encode(DeviceIdentity.SealFor(pki.Phone.AgreementPublicKey, RandomBytes.Next(32), PairingAccept.SessionKeyContext)),
            Base64Url.Encode(DeviceIdentity.SealFor(pki.Phone.AgreementPublicKey, RandomBytes.Next(32), PairingAccept.OfficeKeyContext)),
            Now);

    private static PairingAccept Issue(TestPki pki, PairingRequest request, PairingQrPayload invitation, TimeProvider clock) =>
        PairingAccept.Issue(
            request,
            invitation,
            pki.PcCertificate,
            pki.Pc,
            1,
            2,
            "secretary",
            RandomBytes.Next(32),
            RandomBytes.Next(32),
            clock);

    private static PairingQrPayload BuildInvitation(TestPki pki, PairingToken token, TimeProvider clock) =>
        PairingQrPayload.Create(
            TestPki.OrgId,
            TestPki.OfficeId,
            TestPki.PcDeviceId,
            pki.Pc.AgreementPublicKey,
            token,
            clock);

    private static PairingRequest BuildRequest(TestPki pki, PairingToken token, DateTimeOffset createdAt) =>
        PairingRequest.Create(
            new PairingRequestBody(
                TestPki.OrgId,
                TestPki.OfficeId,
                TestPki.PcDeviceId,
                TestPki.PhoneDeviceId,
                pki.Phone.SigningPublicKeyText,
                pki.Phone.AgreementPublicKeyText,
                PairingCodes.TokenProof(token.Value, TestPki.PhoneDeviceId),
                createdAt),
            pki.Phone);
}
