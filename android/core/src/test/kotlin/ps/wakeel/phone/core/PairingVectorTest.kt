package ps.wakeel.phone.core

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import ps.wakeel.phone.core.certificates.DeviceCertificate
import ps.wakeel.phone.core.certificates.RevocationList
import ps.wakeel.phone.core.containers.ContainerKeySource
import ps.wakeel.phone.core.containers.ContainerKind
import ps.wakeel.phone.core.containers.ContainerOpenOptions
import ps.wakeel.phone.core.containers.ContainerReader
import ps.wakeel.phone.core.crypto.Base64Url
import ps.wakeel.phone.core.crypto.CryptoErrorCode
import ps.wakeel.phone.core.crypto.CryptoException
import ps.wakeel.phone.core.crypto.Sha256
import ps.wakeel.phone.core.json.CanonicalJson
import ps.wakeel.phone.core.json.requireText
import ps.wakeel.phone.core.pairing.PairingAccept
import ps.wakeel.phone.core.pairing.PairingQrPayload
import ps.wakeel.phone.core.pairing.PairingRequest
import java.io.File
import java.time.Duration

/**
 * Pairing, from the phone's side: the invitation it reads, the answer it writes, and the
 * acceptance it has to check before it stores anything. The answer is compared byte for byte
 * with the one the Windows test produced from the same fixed keys.
 */
class PairingVectorTest {

    /** The label both sides derive the short code invitation key with. */
    private val invitationKeyLabel = "wakeel.pairing.invite|v1|"

    private val invitation = PairingQrPayload.fromJson(Vectors.jsonValue("pairing/qr.json"))
    private val expected = Vectors.json("pairing/expected.json")
    private val pcCertificate = DeviceCertificate.fromJson(Vectors.jsonValue("certificates/pc.json"))
    private val noRevocations = RevocationList.fromJson(Vectors.jsonValue("certificates/revocations-empty.json"))
    private val accept = PairingAccept.fromJson(Vectors.jsonValue("pairing/accept.json"))

    private val requestedAt = CanonicalJson.parseInstant("2026-09-17T09:01:00.000Z")

    @Test
    fun theImageTextReadsBackToTheSamePayload() {
        val fromImage = PairingQrPayload.fromBase64Url(Vectors.text("pairing/qr.txt").trim())
        assertEquals(invitation, fromImage)
        assertEquals(Vectors.text("pairing/qr.txt").trim(), invitation.toBase64Url())
        assertArrayEquals(Vectors.bytes("pairing/qr.json"), CanonicalJson.toBytes(invitation.toJson()))

        assertEquals(Vectors.orgId, invitation.orgId)
        assertEquals(Vectors.pcDeviceId, invitation.pcDeviceId)
        assertEquals(Vectors.pc.agreementPublicKeyText, invitation.pcX25519Pub)
    }

    @Test
    fun theShortCodeOfTheInvitationIsTheOneWindowsShows() {
        assertEquals(expected.requireText("shortCode"), invitation.shortCode())
    }

    @Test
    fun anInvitationOlderThanTenMinutesIsRefused() {
        assertFalse(invitation.isExpired(invitation.issuedAt.plus(Duration.ofMinutes(9))))
        assertTrue(invitation.isExpired(invitation.issuedAt.plus(Duration.ofMinutes(10))))
        assertThrows(CryptoException::class.java) {
            invitation.ensureValid(invitation.issuedAt.plus(Duration.ofMinutes(11)))
        }
    }

    @Test
    fun theAnswerThisPhoneBuildsIsTheOneWindowsProduced() {
        val request = PairingRequest.build(invitation, Vectors.phoneDeviceId, Vectors.phone, requestedAt)
        assertArrayEquals(Vectors.bytes("pairing/request.json"), CanonicalJson.toBytes(request.toJson()))
        assertEquals(expected.requireText("requestSignature"), request.signature)
        assertEquals(expected.requireText("tokenProof"), request.body.tokenProof)
        assertTrue(request.verifySignature())
        assertTrue(request.verifyToken(Vectors.pairingToken))
    }

    @Test
    fun anAnswerForAnotherPhoneDoesNotProveThisSecret() {
        val request = PairingRequest.build(invitation, "ANOTHER-PHONE", Vectors.phone, requestedAt)
        assertTrue(request.verifySignature())
        assertFalse(
            "a proof naming another phone must not pass for this one",
            PairingRequest.fromJson(Vectors.jsonValue("pairing/request.json")).body.tokenProof == request.body.tokenProof,
        )
    }

    @Test
    fun anAnswerSignedByOtherKeysIsRefused() {
        val genuine = PairingRequest.fromJson(Vectors.jsonValue("pairing/request.json"))
        val forged = genuine.copy(body = genuine.body.copy(x25519Pub = Vectors.pc.agreementPublicKeyText))
        assertFalse(forged.verifySignature())
    }

    @Test
    fun theComputersAcceptanceIsTrustedAndCarriesBothKeys() {
        val certificate = accept.ensureTrusted(
            Vectors.org.signingPublicKey,
            invitation,
            noRevocations,
            Vectors.now,
            Vectors.phone,
        )
        assertEquals(Vectors.phoneDeviceId, certificate.body.deviceId)
        assertEquals(Vectors.pcDeviceId, certificate.body.issuerId)
        assertEquals(Vectors.phone.signingPublicKeyText, certificate.body.ed25519Pub)

        assertArrayEquals(
            Base64Url.decode(expected.requireText("sessionKeyBase64Url")),
            accept.openSessionKey(Vectors.phone),
        )
        assertArrayEquals(
            Base64Url.decode(expected.requireText("officeKeyBase64Url")),
            accept.openOfficeKey(Vectors.phone),
        )
    }

    @Test
    fun anAcceptanceForAnotherOrganisationKeyIsRefused() {
        val error = assertThrows(CryptoException::class.java) {
            accept.ensureTrusted(Vectors.pc.signingPublicKey, invitation, null, Vectors.now, Vectors.phone)
        }
        assertEquals(CryptoErrorCode.BadSignature, error.code)
    }

    @Test
    fun anAcceptanceThatDoesNotMatchTheInvitationIsRefused() {
        val other = invitation.copy(pcDeviceId = "PC-OTHER")
        assertThrows(CryptoException::class.java) {
            accept.ensureTrusted(Vectors.org.signingPublicKey, other, null, Vectors.now, Vectors.phone)
        }
    }

    @Test
    fun anAcceptanceIssuedToOtherPhoneKeysIsRefused() {
        val error = assertThrows(CryptoException::class.java) {
            accept.ensureTrusted(Vectors.org.signingPublicKey, invitation, null, Vectors.now, Vectors.pc)
        }
        assertEquals(CryptoErrorCode.BadSignature, error.code)
    }

    @Test
    fun theOtherSideCannotOpenTheKeysSealedToThisPhone() {
        assertThrows(CryptoException::class.java) { accept.openSessionKey(Vectors.pc) }
    }

    @Test
    fun theShortCodeFileCarriesTheSameInvitation() {
        val code = invitation.shortCode()
        val name = "pairing-$code.wakeel-phone"
        assertEquals(expected.requireText("invitationFileName"), name)

        val staged = File.createTempFile("wakeel-invitation-", ".wakeel-phone")
        staged.deleteOnExit()
        staged.writeBytes(Vectors.bytes("pairing/$name"))

        // The person types the six digits; the key of the file is derived from them, so nothing
        // has to be shared before the two devices have ever met.
        val key = Sha256.hash((invitationKeyLabel + code).toByteArray(Charsets.UTF_8))
        ContainerReader.open(
            staged,
            ContainerOpenOptions(
                expectedKind = ContainerKind.Phone,
                orgSigningPublicKey = Vectors.org.signingPublicKey,
                issuerCertificate = pcCertificate,
                now = Vectors.now,
            ),
        ).use { reader ->
            val entries = reader.readEntries(ContainerKeySource.sessionKey(key))
            val carried = PairingQrPayload.fromJson(CanonicalJson.parse(entries.getValue("invitation.json")))
            assertEquals(invitation, carried)
        }
    }

    @Test
    fun aWrongShortCodeDoesNotOpenTheInvitation() {
        val staged = File.createTempFile("wakeel-invitation-", ".wakeel-phone")
        staged.deleteOnExit()
        staged.writeBytes(Vectors.bytes("pairing/pairing-${invitation.shortCode()}.wakeel-phone"))

        val wrong = Sha256.hash((invitationKeyLabel + "000000").toByteArray(Charsets.UTF_8))
        ContainerReader.open(
            staged,
            ContainerOpenOptions(
                expectedKind = ContainerKind.Phone,
                orgSigningPublicKey = Vectors.org.signingPublicKey,
                issuerCertificate = pcCertificate,
                now = Vectors.now,
            ),
        ).use { reader ->
            assertThrows(CryptoException::class.java) {
                reader.readEntries(ContainerKeySource.sessionKey(wrong))
            }
        }
    }

    @Test
    fun theAnswerIsWrittenBackForTheWindowsSide() {
        val out = Vectors.outputDirectory("pairing")
        val request = PairingRequest.build(invitation, Vectors.phoneDeviceId, Vectors.phone, requestedAt)
        File(out, "request.json").writeBytes(CanonicalJson.toBytes(request.toJson()))
        File(out, "qr.json").writeBytes(CanonicalJson.toBytes(invitation.toJson()))
    }
}
