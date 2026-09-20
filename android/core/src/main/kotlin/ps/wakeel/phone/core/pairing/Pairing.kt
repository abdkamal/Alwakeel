package ps.wakeel.phone.core.pairing

import ps.wakeel.phone.core.certificates.CertificateChain
import ps.wakeel.phone.core.certificates.DeviceCertificate
import ps.wakeel.phone.core.certificates.RevocationList
import ps.wakeel.phone.core.crypto.Base64Url
import ps.wakeel.phone.core.crypto.CryptoErrorCode
import ps.wakeel.phone.core.crypto.CryptoException
import ps.wakeel.phone.core.crypto.DeviceIdentity
import ps.wakeel.phone.core.crypto.DomainSeparation
import ps.wakeel.phone.core.crypto.HmacSha256
import ps.wakeel.phone.core.crypto.Sha256
import ps.wakeel.phone.core.json.CanonicalJson
import ps.wakeel.phone.core.json.JsonValue
import ps.wakeel.phone.core.json.asObject
import ps.wakeel.phone.core.json.jsonObject
import ps.wakeel.phone.core.json.jsonString
import ps.wakeel.phone.core.json.requireText
import java.math.BigInteger
import java.time.Duration
import java.time.Instant

/** Derivations shared by both sides of a pairing session. */
object PairingCodes {
    const val DIGITS = 6
    private const val SHORT_CODE_LABEL = "code"
    private val MODULUS = BigInteger.valueOf(1_000_000L)

    /**
     * Six digits derived as HMAC-SHA256 of the literal "code" under the pairing secret, read as
     * one unsigned number and reduced modulo a million — the same derivation the computer does,
     * which is why a person can type the code instead of scanning the image.
     */
    fun shortCode(token: ByteArray): String {
        val mac = hmac(token, SHORT_CODE_LABEL)
        val value = BigInteger(1, mac)
        return value.mod(MODULUS).toString().padStart(DIGITS, '0')
    }

    fun hmac(token: ByteArray, label: String): ByteArray =
        HmacSha256.compute(token, label.toByteArray(Charsets.UTF_8))

    /** The proof a phone puts in its pairing request to show it saw the secret. */
    fun tokenProof(token: ByteArray, phoneDeviceId: String): String =
        Base64Url.encode(hmac(token, "pairing-request|$phoneDeviceId"))
}

/** The one time secret behind a pairing session. */
class PairingToken(value: ByteArray) {
    companion object {
        const val TOKEN_SIZE = 32

        fun fromText(text: String) = PairingToken(Base64Url.decode(text))
    }

    val value: ByteArray = value.copyOf()

    init {
        if (this.value.size != TOKEN_SIZE) {
            throw CryptoException(CryptoErrorCode.Corrupt, "the pairing secret must be thirty two bytes")
        }
    }

    val text: String get() = Base64Url.encode(value)

    val shortCode: String get() = PairingCodes.shortCode(value)
}

/**
 * What the computer puts in the pairing image: canonical JSON rendered as base64url. It is
 * valid for ten minutes and is consumed once.
 */
data class PairingQrPayload(
    val orgId: String,
    val officeId: String,
    val pcDeviceId: String,
    val pcX25519Pub: String,
    val token: String,
    val issuedAt: Instant,
) {
    companion object {
        /** How long a pairing session stays open. */
        val LIFETIME: Duration = Duration.ofMinutes(10)

        fun fromBase64Url(value: String): PairingQrPayload =
            fromJson(CanonicalJson.parse(Base64Url.decode(value)))

        fun fromJson(value: JsonValue): PairingQrPayload {
            val obj = value.asObject()
            return PairingQrPayload(
                orgId = obj.requireText("orgId"),
                officeId = obj.requireText("officeId"),
                pcDeviceId = obj.requireText("pcDeviceId"),
                pcX25519Pub = obj.requireText("pcX25519Pub"),
                token = obj.requireText("token"),
                issuedAt = CanonicalJson.parseInstant(obj.requireText("issuedAt")),
            )
        }
    }

    fun toJson(): JsonValue = jsonObject(
        "orgId" to jsonString(orgId),
        "officeId" to jsonString(officeId),
        "pcDeviceId" to jsonString(pcDeviceId),
        "pcX25519Pub" to jsonString(pcX25519Pub),
        "token" to jsonString(token),
        "issuedAt" to CanonicalJson.jsonInstant(issuedAt),
    )

    fun toBase64Url(): String = Base64Url.encode(CanonicalJson.toBytes(toJson()))

    val pairingToken: PairingToken get() = PairingToken.fromText(token)

    /** The six digit code a person may type instead of scanning. */
    fun shortCode(): String = PairingCodes.shortCode(Base64Url.decode(token))

    fun isExpired(now: Instant): Boolean = !now.isBefore(issuedAt.plus(LIFETIME))

    fun ensureValid(now: Instant) {
        if (isExpired(now)) {
            throw CryptoException(CryptoErrorCode.Expired, "the pairing session is no longer open")
        }
    }
}

/** The signed body a phone writes when it answers a pairing invitation. */
data class PairingRequestBody(
    val orgId: String,
    val officeId: String,
    val pcDeviceId: String,
    val phoneDeviceId: String,
    val ed25519Pub: String,
    val x25519Pub: String,
    val tokenProof: String,
    val createdAt: Instant,
) {
    fun toJson(): JsonValue = jsonObject(
        "orgId" to jsonString(orgId),
        "officeId" to jsonString(officeId),
        "pcDeviceId" to jsonString(pcDeviceId),
        "phoneDeviceId" to jsonString(phoneDeviceId),
        "ed25519Pub" to jsonString(ed25519Pub),
        "x25519Pub" to jsonString(x25519Pub),
        "tokenProof" to jsonString(tokenProof),
        "createdAt" to CanonicalJson.jsonInstant(createdAt),
    )

    companion object {
        fun fromJson(value: JsonValue): PairingRequestBody {
            val obj = value.asObject()
            return PairingRequestBody(
                orgId = obj.requireText("orgId"),
                officeId = obj.requireText("officeId"),
                pcDeviceId = obj.requireText("pcDeviceId"),
                phoneDeviceId = obj.requireText("phoneDeviceId"),
                ed25519Pub = obj.requireText("ed25519Pub"),
                x25519Pub = obj.requireText("x25519Pub"),
                tokenProof = obj.requireText("tokenProof"),
                createdAt = CanonicalJson.parseInstant(obj.requireText("createdAt")),
            )
        }
    }
}

/**
 * The phone's answer: its own public keys plus a proof that it really saw the pairing secret.
 * Self signed, because at this point the phone has no certificate yet.
 */
data class PairingRequest(val body: PairingRequestBody, val signature: String) {
    companion object {
        fun create(body: PairingRequestBody, phoneIdentity: DeviceIdentity): PairingRequest =
            PairingRequest(body, phoneIdentity.signText(signedBytes(body)))

        /**
         * Builds the answer the phone writes into the folder: its own identity, the proof of the
         * secret it read from the image or from the short code file, and its self signature.
         */
        fun build(
            invitation: PairingQrPayload,
            phoneDeviceId: String,
            phoneIdentity: DeviceIdentity,
            now: Instant,
        ): PairingRequest {
            invitation.ensureValid(now)
            val body = PairingRequestBody(
                orgId = invitation.orgId,
                officeId = invitation.officeId,
                pcDeviceId = invitation.pcDeviceId,
                phoneDeviceId = phoneDeviceId,
                ed25519Pub = phoneIdentity.signingPublicKeyText,
                x25519Pub = phoneIdentity.agreementPublicKeyText,
                tokenProof = PairingCodes.tokenProof(Base64Url.decode(invitation.token), phoneDeviceId),
                createdAt = Instant.ofEpochMilli(now.toEpochMilli()),
            )
            return create(body, phoneIdentity)
        }

        fun signedBytes(body: PairingRequestBody): ByteArray =
            DomainSeparation.wrap(DomainSeparation.PAIRING_REQUEST, CanonicalJson.toBytes(body.toJson()))

        fun fromJson(value: JsonValue): PairingRequest {
            val obj = value.asObject()
            val bodyValue = obj.members["body"]
                ?: throw CryptoException(CryptoErrorCode.Corrupt, "the answer is incomplete")
            return PairingRequest(PairingRequestBody.fromJson(bodyValue), obj.requireText("signature"))
        }
    }

    fun toJson(): JsonValue = jsonObject("body" to body.toJson(), "signature" to jsonString(signature))

    fun verifySignature(): Boolean =
        DeviceIdentity.verify(body.ed25519Pub, signedBytes(body), signature)

    /** Checks the proof of the secret, in constant time. */
    fun verifyToken(token: ByteArray): Boolean = Sha256.fixedTimeEquals(
        PairingCodes.tokenProof(token, body.phoneDeviceId).toByteArray(Charsets.UTF_8),
        body.tokenProof.toByteArray(Charsets.UTF_8),
    )
}

/** The signed body the computer writes back once it accepts a phone. */
data class PairingAcceptBody(
    val orgId: String,
    val officeId: String,
    val pcDeviceId: String,
    val pcCertificate: DeviceCertificate,
    val phoneDeviceId: String,
    val phoneCertificate: DeviceCertificate,
    val sealedSessionKey: String,
    val sealedOfficeKey: String,
    val createdAt: Instant,
) {
    fun toJson(): JsonValue = jsonObject(
        "orgId" to jsonString(orgId),
        "officeId" to jsonString(officeId),
        "pcDeviceId" to jsonString(pcDeviceId),
        "pcCertificate" to pcCertificate.toJson(),
        "phoneDeviceId" to jsonString(phoneDeviceId),
        "phoneCertificate" to phoneCertificate.toJson(),
        "sealedSessionKey" to jsonString(sealedSessionKey),
        "sealedOfficeKey" to jsonString(sealedOfficeKey),
        "createdAt" to CanonicalJson.jsonInstant(createdAt),
    )

    companion object {
        fun fromJson(value: JsonValue): PairingAcceptBody {
            val obj = value.asObject()
            val pc = obj.members["pcCertificate"]
                ?: throw CryptoException(CryptoErrorCode.Corrupt, "the answer carries no computer certificate")
            val phone = obj.members["phoneCertificate"]
                ?: throw CryptoException(CryptoErrorCode.Corrupt, "the answer carries no phone certificate")
            return PairingAcceptBody(
                orgId = obj.requireText("orgId"),
                officeId = obj.requireText("officeId"),
                pcDeviceId = obj.requireText("pcDeviceId"),
                pcCertificate = DeviceCertificate.fromJson(pc),
                phoneDeviceId = obj.requireText("phoneDeviceId"),
                phoneCertificate = DeviceCertificate.fromJson(phone),
                sealedSessionKey = obj.requireText("sealedSessionKey"),
                sealedOfficeKey = obj.requireText("sealedOfficeKey"),
                createdAt = CanonicalJson.parseInstant(obj.requireText("createdAt")),
            )
        }
    }
}

/**
 * The computer's answer: its own certificate and the phone certificate it just issued, plus the
 * session key and the office key sealed to the phone's X25519 public key.
 */
data class PairingAccept(val body: PairingAcceptBody, val signature: String) {
    companion object {
        const val SESSION_KEY_CONTEXT = "wakeel.pairing.session"
        const val OFFICE_KEY_CONTEXT = "wakeel.pairing.officekey"

        fun signedBytes(body: PairingAcceptBody): ByteArray =
            DomainSeparation.wrap(DomainSeparation.PAIRING_ACCEPT, CanonicalJson.toBytes(body.toJson()))

        fun fromJson(value: JsonValue): PairingAccept {
            val obj = value.asObject()
            val bodyValue = obj.members["body"]
                ?: throw CryptoException(CryptoErrorCode.Corrupt, "the computer's answer is incomplete")
            return PairingAccept(PairingAcceptBody.fromJson(bodyValue), obj.requireText("signature"))
        }
    }

    fun toJson(): JsonValue = jsonObject("body" to body.toJson(), "signature" to jsonString(signature))

    fun verifySignature(pcSigningPublicKey: ByteArray): Boolean {
        val bytes = Base64Url.tryDecode(signature) ?: return false
        return DeviceIdentity.verify(pcSigningPublicKey, signedBytes(body), bytes)
    }

    /**
     * Establishes the whole chain from material that actually crossed the cable: the invitation
     * read from the image, this file, and the organisation key that arrived with it. Returns the
     * phone's own certificate once everything holds.
     */
    fun ensureTrusted(
        orgSigningPublicKey: ByteArray,
        invitation: PairingQrPayload,
        revocations: RevocationList?,
        now: Instant,
        phoneIdentity: DeviceIdentity,
    ): DeviceCertificate {
        val pc = body.pcCertificate

        // Organisation → computer, from the organisation key the setup material carried.
        CertificateChain.verify(pc, orgSigningPublicKey, revocations, now)

        if (pc.body.deviceId != invitation.pcDeviceId ||
            pc.body.orgId != invitation.orgId ||
            pc.body.officeId != invitation.officeId ||
            pc.body.x25519Pub != invitation.pcX25519Pub
        ) {
            throw CryptoException(
                CryptoErrorCode.BadSignature,
                "the answer comes from a different computer than the invitation",
            )
        }

        if (!verifySignature(pc.signingPublicKey)) {
            throw CryptoException(CryptoErrorCode.BadSignature, "the computer's answer is not signed by that computer")
        }

        if (body.orgId != pc.body.orgId || body.officeId != pc.body.officeId || body.pcDeviceId != pc.body.deviceId) {
            throw CryptoException(
                CryptoErrorCode.BadSignature,
                "the answer's plain fields disagree with its own certificate",
            )
        }

        if (body.createdAt.isAfter(now.plus(Duration.ofDays(1)))) {
            throw CryptoException(CryptoErrorCode.Expired, "the computer's answer is dated in the future")
        }

        val phone = body.phoneCertificate
        if (phone.body.deviceId != body.phoneDeviceId ||
            phone.body.orgId != pc.body.orgId ||
            phone.body.officeId != pc.body.officeId ||
            phone.body.issuerId != pc.body.deviceId
        ) {
            throw CryptoException(CryptoErrorCode.BadSignature, "the answer carries a certificate for another phone")
        }

        // Nothing above proves the certificate was issued to THIS phone's own keys; a phone that
        // stored it anyway would find every packet it later signs refused by the far side, long
        // after pairing had looked like it succeeded.
        if (phone.body.ed25519Pub != phoneIdentity.signingPublicKeyText ||
            phone.body.x25519Pub != phoneIdentity.agreementPublicKeyText
        ) {
            throw CryptoException(
                CryptoErrorCode.BadSignature,
                "the issued certificate does not carry this phone's own keys",
            )
        }

        CertificateChain.verify(phone, orgSigningPublicKey, revocations, now, pc)
        return phone
    }

    /** Opens the session key sealed to this phone. */
    fun openSessionKey(phoneIdentity: DeviceIdentity): ByteArray =
        phoneIdentity.open(Base64Url.decode(body.sealedSessionKey), SESSION_KEY_CONTEXT)

    /** Opens the office key sealed to this phone. */
    fun openOfficeKey(phoneIdentity: DeviceIdentity): ByteArray =
        phoneIdentity.open(Base64Url.decode(body.sealedOfficeKey), OFFICE_KEY_CONTEXT)
}
