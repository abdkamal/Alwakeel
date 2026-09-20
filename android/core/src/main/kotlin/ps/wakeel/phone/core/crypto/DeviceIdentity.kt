package ps.wakeel.phone.core.crypto

import org.bouncycastle.crypto.agreement.X25519Agreement
import org.bouncycastle.crypto.params.Ed25519PrivateKeyParameters
import org.bouncycastle.crypto.params.Ed25519PublicKeyParameters
import org.bouncycastle.crypto.params.X25519PrivateKeyParameters
import org.bouncycastle.crypto.params.X25519PublicKeyParameters
import org.bouncycastle.crypto.signers.Ed25519Signer

/**
 * Every signature the product makes is taken over a labelled buffer, so a signature produced
 * for one kind of structure can never be read as a signature over another. The labels are the
 * ones in `Wakeel.Crypto/Json/DomainSeparation.cs`, character for character.
 */
object DomainSeparation {
    const val CERTIFICATE = "wakeel.cert|v1"
    const val REVOCATION_LIST = "wakeel.revocations|v1"
    const val PAIRING_REQUEST = "wakeel.pairing.request|v1"
    const val PAIRING_ACCEPT = "wakeel.pairing.accept|v1"
    const val CONTAINER = "wakeel.container.sig|v1"

    /** Returns `label || body`, the exact bytes a signature covers. */
    fun wrap(label: String, body: ByteArray): ByteArray {
        val prefix = label.toByteArray(Charsets.UTF_8)
        val buffer = ByteArray(prefix.size + body.size)
        prefix.copyInto(buffer, 0)
        body.copyInto(buffer, prefix.size)
        return buffer
    }
}

/**
 * The two private seeds of a device. They are generated on the phone, never leave it, and are
 * kept wrapped by an Android Keystore key — see [KeyVault].
 */
class DeviceSeeds(signingSeed: ByteArray, agreementSeed: ByteArray) {
    companion object {
        const val SEED_SIZE = 32

        fun generate(): DeviceSeeds = DeviceSeeds(RandomBytes.next(SEED_SIZE), RandomBytes.next(SEED_SIZE))
    }

    val signingSeed: ByteArray = signingSeed.copyOf()
    val agreementSeed: ByteArray = agreementSeed.copyOf()

    init {
        if (this.signingSeed.size != SEED_SIZE || this.agreementSeed.size != SEED_SIZE) {
            throw CryptoException(CryptoErrorCode.Corrupt, "a device seed must be thirty two bytes")
        }
    }

    /** The two seeds, one after the other — the form the key vault wraps. */
    fun toBytes(): ByteArray = signingSeed + agreementSeed

    fun clear() {
        signingSeed.fill(0)
        agreementSeed.fill(0)
    }
}

fun deviceSeedsFromBytes(bytes: ByteArray): DeviceSeeds {
    if (bytes.size != DeviceSeeds.SEED_SIZE * 2) {
        throw CryptoException(CryptoErrorCode.Corrupt, "the stored device seeds are the wrong length")
    }
    return DeviceSeeds(
        bytes.copyOfRange(0, DeviceSeeds.SEED_SIZE),
        bytes.copyOfRange(DeviceSeeds.SEED_SIZE, bytes.size),
    )
}

/**
 * A device key pair set: Ed25519 for signing everything the device produces and X25519 for
 * agreeing session keys and opening sealed boxes. Byte compatible with
 * `Wakeel.Crypto/Identity/DeviceIdentity.cs`.
 */
class DeviceIdentity private constructor(private val seeds: DeviceSeeds) {

    companion object {
        const val PUBLIC_KEY_SIZE = 32
        const val SIGNATURE_SIZE = 64

        private const val AGREE_LABEL = "wakeel.agree"
        private const val SEAL_LABEL = "wakeel.seal"

        fun generate(): DeviceIdentity = DeviceIdentity(DeviceSeeds.generate())

        fun import(seeds: DeviceSeeds): DeviceIdentity = DeviceIdentity(seeds)

        fun verify(signingPublicKey: ByteArray, data: ByteArray, signature: ByteArray): Boolean {
            if (signingPublicKey.size != PUBLIC_KEY_SIZE || signature.size != SIGNATURE_SIZE) return false
            return try {
                val signer = Ed25519Signer()
                signer.init(false, Ed25519PublicKeyParameters(signingPublicKey, 0))
                signer.update(data, 0, data.size)
                signer.verifySignature(signature)
            } catch (_: IllegalArgumentException) {
                false
            } catch (_: RuntimeException) {
                false
            }
        }

        fun verify(signingPublicKeyText: String?, data: ByteArray, signatureText: String?): Boolean {
            val key = Base64Url.tryDecode(signingPublicKeyText) ?: return false
            val signature = Base64Url.tryDecode(signatureText) ?: return false
            return verify(key, data, signature)
        }

        /**
         * Sealed box: an ephemeral X25519 key pair encrypts a payload to a recipient public key.
         * The ephemeral public key is the first thirty two bytes of the result.
         */
        fun sealFor(recipientAgreementPublicKey: ByteArray, payload: ByteArray, context: String): ByteArray {
            if (recipientAgreementPublicKey.size != PUBLIC_KEY_SIZE) {
                throw CryptoException(CryptoErrorCode.Corrupt, "the recipient public key has the wrong length")
            }
            val ephemeralSeed = RandomBytes.next(DeviceSeeds.SEED_SIZE)
            val ephemeralPrivate = X25519PrivateKeyParameters(ephemeralSeed, 0)
            val ephemeralPublic = ephemeralPrivate.generatePublicKey().encoded

            val salt = ephemeralPublic + recipientAgreementPublicKey
            val info = "$SEAL_LABEL|$context".toByteArray(Charsets.UTF_8)
            val shared = agree(ephemeralPrivate, recipientAgreementPublicKey)
            val key = Hkdf.deriveKey(shared, Aead.KEY_SIZE, salt, info)

            val sealed = Aead.encrypt(key, payload, salt)
            return ephemeralPublic + sealed
        }

        internal fun agree(privateKey: X25519PrivateKeyParameters, peerPublicKey: ByteArray): ByteArray {
            if (peerPublicKey.size != PUBLIC_KEY_SIZE) {
                throw CryptoException(CryptoErrorCode.Corrupt, "the peer public key has the wrong length")
            }
            val agreement = X25519Agreement()
            agreement.init(privateKey)
            val shared = ByteArray(agreement.agreementSize)
            try {
                agreement.calculateAgreement(X25519PublicKeyParameters(peerPublicKey, 0), shared, 0)
            } catch (exception: RuntimeException) {
                throw CryptoException(CryptoErrorCode.Corrupt, "the two devices could not agree on a key", exception)
            }
            return shared
        }
    }

    private val signingPrivate = Ed25519PrivateKeyParameters(seeds.signingSeed, 0)
    private val agreementPrivate = X25519PrivateKeyParameters(seeds.agreementSeed, 0)

    /** Raw Ed25519 public key. */
    val signingPublicKey: ByteArray = signingPrivate.generatePublicKey().encoded

    /** Raw X25519 public key. */
    val agreementPublicKey: ByteArray = agreementPrivate.generatePublicKey().encoded

    val signingPublicKeyText: String get() = Base64Url.encode(signingPublicKey)

    val agreementPublicKeyText: String get() = Base64Url.encode(agreementPublicKey)

    /** The seeds, so the caller can wrap and store them; nothing else may read them. */
    fun exportSeeds(): DeviceSeeds = seeds

    fun sign(data: ByteArray): ByteArray {
        val signer = Ed25519Signer()
        signer.init(true, signingPrivate)
        signer.update(data, 0, data.size)
        return signer.generateSignature()
    }

    fun signText(data: ByteArray): String = Base64Url.encode(sign(data))

    /**
     * Derives a shared key with a peer. Both sides get the same bytes because the salt orders
     * the two public keys, so neither side has to know who started.
     */
    fun agreeWith(peerAgreementPublicKey: ByteArray, context: String, length: Int = 32): ByteArray {
        val salt = orderedSalt(agreementPublicKey, peerAgreementPublicKey)
        val info = "$AGREE_LABEL|$context".toByteArray(Charsets.UTF_8)
        val shared = agree(agreementPrivate, peerAgreementPublicKey)
        return Hkdf.deriveKey(shared, length, salt, info)
    }

    /** Opens a sealed box addressed to this identity's X25519 public key. */
    fun open(sealedBox: ByteArray, context: String): ByteArray {
        if (sealedBox.size <= PUBLIC_KEY_SIZE) {
            throw CryptoException(CryptoErrorCode.Corrupt, "the sealed block is too short")
        }
        val ephemeralPublic = sealedBox.copyOfRange(0, PUBLIC_KEY_SIZE)
        val body = sealedBox.copyOfRange(PUBLIC_KEY_SIZE, sealedBox.size)
        val salt = ephemeralPublic + agreementPublicKey
        val info = "$SEAL_LABEL|$context".toByteArray(Charsets.UTF_8)
        val shared = agree(agreementPrivate, ephemeralPublic)
        val key = Hkdf.deriveKey(shared, Aead.KEY_SIZE, salt, info)
        return Aead.decrypt(key, body, salt)
    }

    private fun orderedSalt(first: ByteArray, second: ByteArray): ByteArray =
        if (compareUnsigned(first, second) <= 0) first + second else second + first

    /**
     * Bytes compare as unsigned numbers here, because that is what .NET's span comparison does;
     * Kotlin's own byte comparison is signed and would order the two keys the other way round
     * for any key whose first differing byte is above 0x7F.
     */
    private fun compareUnsigned(left: ByteArray, right: ByteArray): Int {
        val shared = minOf(left.size, right.size)
        for (index in 0 until shared) {
            val difference = (left[index].toInt() and 0xFF) - (right[index].toInt() and 0xFF)
            if (difference != 0) return difference
        }
        return left.size - right.size
    }
}
