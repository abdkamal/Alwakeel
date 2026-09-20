package ps.wakeel.phone.core

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import ps.wakeel.phone.core.certificates.CertificateChain
import ps.wakeel.phone.core.certificates.DeviceCertificate
import ps.wakeel.phone.core.certificates.DeviceKind
import ps.wakeel.phone.core.certificates.RevocationList
import ps.wakeel.phone.core.crypto.CryptoErrorCode
import ps.wakeel.phone.core.crypto.CryptoException
import ps.wakeel.phone.core.json.CanonicalJson
import java.io.File
import java.time.Duration

/**
 * The chain organisation → computer → phone, checked against the certificates the Windows side
 * issued. A packet whose producer does not chain cleanly is never opened, so these refusals are
 * the outer wall of the whole exchange.
 */
class CertificateVectorTest {

    private val orgRoot = DeviceCertificate.fromJson(Vectors.jsonValue("certificates/org-root.json"))
    private val pcCertificate = DeviceCertificate.fromJson(Vectors.jsonValue("certificates/pc.json"))
    private val phoneCertificate = DeviceCertificate.fromJson(Vectors.jsonValue("certificates/phone.json"))
    private val revokedCertificate = DeviceCertificate.fromJson(Vectors.jsonValue("certificates/phone-revoked.json"))
    private val revocations = RevocationList.fromJson(Vectors.jsonValue("certificates/revocations.json"))
    private val noRevocations = RevocationList.fromJson(Vectors.jsonValue("certificates/revocations-empty.json"))

    private val orgKey get() = Vectors.org.signingPublicKey
    private val now get() = Vectors.now

    @Test
    fun aCertificateReSerialisesToTheVeryBytesWindowsWrote() {
        assertArrayEquals(Vectors.bytes("certificates/pc.json"), CanonicalJson.toBytes(pcCertificate.toJson()))
        assertArrayEquals(Vectors.bytes("certificates/phone.json"), CanonicalJson.toBytes(phoneCertificate.toJson()))
        assertArrayEquals(Vectors.bytes("certificates/org-root.json"), CanonicalJson.toBytes(orgRoot.toJson()))
        assertArrayEquals(Vectors.bytes("certificates/revocations.json"), CanonicalJson.toBytes(revocations.toJson()))
    }

    @Test
    fun theCertificatesCarryWhatTheyShould() {
        assertEquals(DeviceKind.Org, orgRoot.body.kind)
        assertEquals(Vectors.orgId, orgRoot.body.deviceId)
        assertEquals("", orgRoot.body.officeId)
        assertEquals(DeviceKind.Pc, pcCertificate.body.kind)
        assertEquals(Vectors.pcDeviceId, pcCertificate.body.deviceId)
        assertEquals(DeviceKind.Phone, phoneCertificate.body.kind)
        assertEquals(Vectors.pcDeviceId, phoneCertificate.body.issuerId)
    }

    @Test
    fun theOrganisationRootCertifiesItself() {
        CertificateChain.verify(orgRoot, orgKey, null, now)
        assertTrue(orgRoot.verifySignature(orgKey))
    }

    @Test
    fun theComputerChainsToTheOrganisation() {
        CertificateChain.verify(pcCertificate, orgKey, noRevocations, now)
        assertNull(CertificateChain.tryVerify(pcCertificate, orgKey, noRevocations, now))
    }

    @Test
    fun thePhoneChainsThroughItsComputer() {
        CertificateChain.verify(phoneCertificate, orgKey, noRevocations, now, pcCertificate)
    }

    @Test
    fun aPhoneCertificateWithoutItsComputerIsRefused() {
        val error = CertificateChain.tryVerify(phoneCertificate, orgKey, null, now)
        assertEquals(CryptoErrorCode.BadSignature, error)
    }

    @Test
    fun aRevokedDeviceIsRefused() {
        assertTrue(revocations.isRevoked(Vectors.revokedDeviceId, now))
        val error = CertificateChain.tryVerify(revokedCertificate, orgKey, revocations, now, pcCertificate)
        assertEquals(CryptoErrorCode.Revoked, error)

        // The very same certificate is fine against a list that does not name it.
        assertNull(CertificateChain.tryVerify(revokedCertificate, orgKey, noRevocations, now, pcCertificate))
    }

    @Test
    fun aRevocationListSignedByAnotherKeyIsRefused() {
        val stranger = Vectors.pc.signingPublicKey
        assertEquals(
            CryptoErrorCode.BadSignature,
            CertificateChain.tryVerify(pcCertificate, stranger, revocations, now),
        )
    }

    @Test
    fun aTamperedCertificateIsRefused() {
        val tampered = pcCertificate.copy(
            body = pcCertificate.body.copy(role = "manager"),
        )
        assertEquals(CryptoErrorCode.BadSignature, CertificateChain.tryVerify(tampered, orgKey, null, now))
    }

    @Test
    fun aCertificateDatedInTheFutureIsRefused() {
        val error = CertificateChain.tryVerify(
            pcCertificate,
            orgKey,
            null,
            Vectors.issuedAt.minus(Duration.ofDays(3)),
        )
        assertEquals(CryptoErrorCode.Expired, error)
    }

    @Test
    fun aPhoneMayNotIssueACertificate() {
        val forged = DeviceCertificate.issue(
            phoneCertificate.body.copy(deviceId = "PHONE-FORGED", issuerId = phoneCertificate.body.deviceId),
            Vectors.phone,
        )
        assertThrows(CryptoException::class.java) {
            CertificateChain.verify(forged, orgKey, null, now, phoneCertificate)
        }
    }

    @Test
    fun theCertificatesAreWrittenBackForTheWindowsSide() {
        val out = Vectors.outputDirectory("certificates")
        File(out, "pc.json").writeBytes(CanonicalJson.toBytes(pcCertificate.toJson()))
        File(out, "phone.json").writeBytes(CanonicalJson.toBytes(phoneCertificate.toJson()))
        File(out, "org-root.json").writeBytes(CanonicalJson.toBytes(orgRoot.toJson()))
        File(out, "revocations.json").writeBytes(CanonicalJson.toBytes(revocations.toJson()))
    }
}
