package ps.wakeel.phone.core.certificates

import ps.wakeel.phone.core.crypto.Base64Url
import ps.wakeel.phone.core.crypto.CryptoErrorCode
import ps.wakeel.phone.core.crypto.CryptoException
import ps.wakeel.phone.core.crypto.DeviceIdentity
import ps.wakeel.phone.core.crypto.DomainSeparation
import ps.wakeel.phone.core.json.CanonicalJson
import ps.wakeel.phone.core.json.JsonValue
import ps.wakeel.phone.core.json.asObject
import ps.wakeel.phone.core.json.jsonArray
import ps.wakeel.phone.core.json.jsonNumber
import ps.wakeel.phone.core.json.jsonObject
import ps.wakeel.phone.core.json.jsonString
import ps.wakeel.phone.core.json.requireInt
import ps.wakeel.phone.core.json.requireText
import ps.wakeel.phone.core.json.text
import java.time.Duration
import java.time.Instant

/** Whether the certified party is an office computer, a paired phone, or the organisation. */
enum class DeviceKind(val token: String) {
    Pc("pc"),
    Phone("phone"),

    /** The organisation root: the single root of trust, certifying itself. */
    Org("org");

    companion object {
        fun fromToken(token: String?): DeviceKind = entries.firstOrNull { it.token == token }
            ?: throw CryptoException(CryptoErrorCode.UnknownKind, "unknown device kind")
    }
}

/** The signed body of a device certificate, in the field order the Windows record declares. */
data class DeviceCertificateBody(
    val orgId: String,
    val officeId: String,
    val deviceId: String,
    val deviceNo: Int,
    val employeeNo: Int,
    val role: String,
    val kind: DeviceKind,
    val ed25519Pub: String,
    val x25519Pub: String,
    val issuedAt: Instant,
    val issuerId: String,
) {
    fun toJson(): JsonValue = jsonObject(
        "orgId" to jsonString(orgId),
        "officeId" to jsonString(officeId),
        "deviceId" to jsonString(deviceId),
        "deviceNo" to jsonNumber(deviceNo),
        "employeeNo" to jsonNumber(employeeNo),
        "role" to jsonString(role),
        "kind" to jsonString(kind.token),
        "ed25519Pub" to jsonString(ed25519Pub),
        "x25519Pub" to jsonString(x25519Pub),
        "issuedAt" to CanonicalJson.jsonInstant(issuedAt),
        "issuerId" to jsonString(issuerId),
    )

    companion object {
        fun fromJson(value: JsonValue): DeviceCertificateBody {
            val obj = value.asObject()
            return DeviceCertificateBody(
                orgId = obj.requireText("orgId"),
                officeId = obj.text("officeId").orEmpty(),
                deviceId = obj.requireText("deviceId"),
                deviceNo = obj.requireInt("deviceNo"),
                employeeNo = obj.requireInt("employeeNo"),
                role = obj.requireText("role"),
                kind = DeviceKind.fromToken(obj.requireText("kind")),
                ed25519Pub = obj.requireText("ed25519Pub"),
                x25519Pub = obj.requireText("x25519Pub"),
                issuedAt = CanonicalJson.parseInstant(obj.requireText("issuedAt")),
                issuerId = obj.requireText("issuerId"),
            )
        }
    }
}

/**
 * A device certificate: the body plus the issuer's Ed25519 signature over its canonical bytes.
 * Computers are certified by the organisation key, phones by the computer that paired them.
 */
data class DeviceCertificate(val body: DeviceCertificateBody, val signature: String) {

    companion object {
        /** The role written into an organisation root certificate. */
        const val ORG_ROLE = "organisation"

        fun issue(body: DeviceCertificateBody, issuer: DeviceIdentity): DeviceCertificate =
            DeviceCertificate(body, issuer.signText(signedBytes(body)))

        /** The labelled bytes an issuer signature covers. */
        fun signedBytes(body: DeviceCertificateBody): ByteArray =
            DomainSeparation.wrap(DomainSeparation.CERTIFICATE, CanonicalJson.toBytes(body.toJson()))

        fun fromJson(value: JsonValue): DeviceCertificate {
            val obj = value.asObject()
            return DeviceCertificate(
                body = DeviceCertificateBody.fromJson(obj.requireText2("body")),
                signature = obj.requireText("signature"),
            )
        }

        private fun JsonValue.Obj.requireText2(name: String): JsonValue =
            members[name] ?: throw CryptoException(CryptoErrorCode.Corrupt, "the certificate carries no contents")
    }

    fun toJson(): JsonValue = jsonObject(
        "body" to body.toJson(),
        "signature" to jsonString(signature),
    )

    val signingPublicKey: ByteArray get() = Base64Url.decode(body.ed25519Pub)

    val agreementPublicKey: ByteArray get() = Base64Url.decode(body.x25519Pub)

    fun verifySignature(issuerSigningPublicKey: ByteArray): Boolean {
        val bytes = Base64Url.tryDecode(signature) ?: return false
        return DeviceIdentity.verify(issuerSigningPublicKey, signedBytes(body), bytes)
    }
}

/** One revoked device and the instant the revocation takes effect. */
data class RevocationEntry(val deviceId: String, val revokedAt: Instant)

/** The signed body of a revocation list. */
data class RevocationListBody(
    val orgId: String,
    val issuedAt: Instant,
    val entries: List<RevocationEntry>,
) {
    fun toJson(): JsonValue = jsonObject(
        "orgId" to jsonString(orgId),
        "issuedAt" to CanonicalJson.jsonInstant(issuedAt),
        "entries" to jsonArray(
            entries.map {
                jsonObject(
                    "deviceId" to jsonString(it.deviceId),
                    "revokedAt" to CanonicalJson.jsonInstant(it.revokedAt),
                )
            },
        ),
    )
}

/**
 * The revocation list the organisation distributes inside updated setup files. Only the
 * organisation signing key may issue it; the phone only ever checks one.
 */
data class RevocationList(val body: RevocationListBody, val signature: String) {

    companion object {
        fun signedBytes(body: RevocationListBody): ByteArray =
            DomainSeparation.wrap(DomainSeparation.REVOCATION_LIST, CanonicalJson.toBytes(body.toJson()))

        fun fromJson(value: JsonValue): RevocationList {
            val obj = value.asObject()
            val bodyObj = (obj.members["body"] ?: throw CryptoException(CryptoErrorCode.Corrupt, "no contents"))
                .asObject()
            val entries = (bodyObj.members["entries"] as? JsonValue.Arr)?.items.orEmpty().map { entry ->
                val item = entry.asObject()
                RevocationEntry(
                    deviceId = item.requireText("deviceId"),
                    revokedAt = CanonicalJson.parseInstant(item.requireText("revokedAt")),
                )
            }
            return RevocationList(
                body = RevocationListBody(
                    orgId = bodyObj.requireText("orgId"),
                    issuedAt = CanonicalJson.parseInstant(bodyObj.requireText("issuedAt")),
                    entries = entries,
                ),
                signature = obj.requireText("signature"),
            )
        }
    }

    fun toJson(): JsonValue = jsonObject(
        "body" to body.toJson(),
        "signature" to jsonString(signature),
    )

    fun verifySignature(orgSigningPublicKey: ByteArray): Boolean {
        val bytes = Base64Url.tryDecode(signature) ?: return false
        return DeviceIdentity.verify(orgSigningPublicKey, signedBytes(body), bytes)
    }

    fun isRevoked(deviceId: String, at: Instant): Boolean =
        body.entries.any { it.deviceId == deviceId && !at.isBefore(it.revokedAt) }
}

/**
 * Verifies the two chains the product uses: organisation → computer, and organisation →
 * computer → phone. The rules are the ones in `Wakeel.Crypto/Certificates/CertificateChain.cs`;
 * a refusal here is what stops a stranger's packet from ever being opened.
 */
object CertificateChain {
    /** How far ahead of the verifier's clock an issuance instant may sit. */
    val CLOCK_SKEW: Duration = Duration.ofDays(1)

    fun verify(
        certificate: DeviceCertificate,
        orgSigningPublicKey: ByteArray,
        revocations: RevocationList?,
        now: Instant,
        issuerCertificate: DeviceCertificate? = null,
        maxAge: Duration? = null,
        maxRevocationListAge: Duration? = null,
    ) {
        if (orgSigningPublicKey.size != DeviceIdentity.PUBLIC_KEY_SIZE) {
            throw CryptoException(CryptoErrorCode.Corrupt, "the organisation public key has the wrong length")
        }
        validateShape(certificate)

        if (revocations != null) {
            if (!revocations.verifySignature(orgSigningPublicKey)) {
                throw CryptoException(CryptoErrorCode.BadSignature, "the revocation list is not signed by the organisation")
            }
            if (revocations.body.orgId != certificate.body.orgId) {
                throw CryptoException(CryptoErrorCode.BadSignature, "the revocation list belongs to another organisation")
            }
            if (maxRevocationListAge != null &&
                Duration.between(revocations.body.issuedAt, now) > maxRevocationListAge
            ) {
                throw CryptoException(CryptoErrorCode.Expired, "the revocation list is older than the accepted window")
            }
            if (revocations.body.issuedAt.isAfter(now.plus(CLOCK_SKEW))) {
                throw CryptoException(CryptoErrorCode.Expired, "the revocation list is dated in the future")
            }
        }

        verifyNode(certificate, orgSigningPublicKey, revocations, now, issuerCertificate, maxAge, depth = 0)
    }

    fun tryVerify(
        certificate: DeviceCertificate,
        orgSigningPublicKey: ByteArray,
        revocations: RevocationList?,
        now: Instant,
        issuerCertificate: DeviceCertificate? = null,
    ): CryptoErrorCode? = try {
        verify(certificate, orgSigningPublicKey, revocations, now, issuerCertificate)
        null
    } catch (exception: CryptoException) {
        exception.code
    }

    private fun verifyNode(
        certificate: DeviceCertificate,
        orgSigningPublicKey: ByteArray,
        revocations: RevocationList?,
        now: Instant,
        issuerCertificate: DeviceCertificate?,
        maxAge: Duration?,
        depth: Int,
    ) {
        if (depth > 2) {
            throw CryptoException(CryptoErrorCode.Corrupt, "the certificate chain is longer than the product allows")
        }

        when (certificate.body.kind) {
            DeviceKind.Org -> {
                if (certificate.body.deviceId != certificate.body.orgId ||
                    certificate.body.issuerId != certificate.body.orgId
                ) {
                    throw CryptoException(
                        CryptoErrorCode.BadSignature,
                        "an organisation certificate must name the organisation itself",
                    )
                }
                if (!certificate.signingPublicKey.contentEquals(orgSigningPublicKey)) {
                    throw CryptoException(
                        CryptoErrorCode.Tampered,
                        "the organisation certificate carries a different key than the one trusted here",
                    )
                }
                if (!certificate.verifySignature(orgSigningPublicKey)) {
                    throw CryptoException(CryptoErrorCode.BadSignature, "the organisation certificate is not self signed")
                }
            }

            DeviceKind.Pc -> {
                if (certificate.body.issuerId != certificate.body.orgId) {
                    throw CryptoException(
                        CryptoErrorCode.BadSignature,
                        "a computer certificate must be issued by the organisation",
                    )
                }
                if (!certificate.verifySignature(orgSigningPublicKey)) {
                    throw CryptoException(
                        CryptoErrorCode.BadSignature,
                        "the computer certificate does not match the organisation key",
                    )
                }
            }

            DeviceKind.Phone -> {
                if (issuerCertificate == null) {
                    throw CryptoException(
                        CryptoErrorCode.BadSignature,
                        "a phone certificate cannot be checked without the computer certificate that issued it",
                    )
                }
                validateShape(issuerCertificate)
                if (issuerCertificate.body.kind != DeviceKind.Pc) {
                    throw CryptoException(CryptoErrorCode.BadSignature, "a phone certificate may only be issued by a computer")
                }
                if (certificate.body.issuerId != issuerCertificate.body.deviceId) {
                    throw CryptoException(CryptoErrorCode.BadSignature, "the phone certificate names a different computer")
                }
                if (certificate.body.orgId != issuerCertificate.body.orgId ||
                    certificate.body.officeId != issuerCertificate.body.officeId
                ) {
                    throw CryptoException(
                        CryptoErrorCode.BadSignature,
                        "the phone and the issuing computer are not in the same office",
                    )
                }
                verifyNode(issuerCertificate, orgSigningPublicKey, revocations, now, null, null, depth + 1)
                if (!certificate.verifySignature(issuerCertificate.signingPublicKey)) {
                    throw CryptoException(
                        CryptoErrorCode.BadSignature,
                        "the phone certificate does not match the issuing computer key",
                    )
                }
            }
        }

        if (certificate.body.issuedAt.isAfter(now.plus(CLOCK_SKEW))) {
            throw CryptoException(CryptoErrorCode.Expired, "the certificate is dated in the future")
        }
        if (maxAge != null && Duration.between(certificate.body.issuedAt, now) > maxAge) {
            throw CryptoException(CryptoErrorCode.Expired, "the certificate is older than the accepted window")
        }
        if (revocations != null && revocations.isRevoked(certificate.body.deviceId, now)) {
            throw CryptoException(CryptoErrorCode.Revoked, "the device appears on the revocation list")
        }
    }

    private fun validateShape(certificate: DeviceCertificate) {
        val body = certificate.body
        if (body.orgId.isBlank() || body.deviceId.isBlank() || body.issuerId.isBlank()) {
            throw CryptoException(CryptoErrorCode.Corrupt, "the certificate is missing one of its identifiers")
        }
        // The organisation belongs to no office; every other kind must name one.
        val officeWrong = if (body.kind == DeviceKind.Org) body.officeId.isNotEmpty() else body.officeId.isBlank()
        if (officeWrong) {
            throw CryptoException(CryptoErrorCode.Corrupt, "the certificate names its office incorrectly")
        }
        val signing = Base64Url.tryDecode(body.ed25519Pub)
        if (signing == null || signing.size != DeviceIdentity.PUBLIC_KEY_SIZE) {
            throw CryptoException(CryptoErrorCode.Corrupt, "the certificate signing key is not readable")
        }
        val agreement = Base64Url.tryDecode(body.x25519Pub)
        if (agreement == null || agreement.size != DeviceIdentity.PUBLIC_KEY_SIZE) {
            throw CryptoException(CryptoErrorCode.Corrupt, "the certificate agreement key is not readable")
        }
        if (certificate.signature.isEmpty()) {
            throw CryptoException(CryptoErrorCode.BadSignature, "the certificate carries no signature")
        }
    }
}
