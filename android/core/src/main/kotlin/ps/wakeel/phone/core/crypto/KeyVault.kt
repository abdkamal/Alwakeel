package ps.wakeel.phone.core.crypto

import android.os.Build
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import java.security.KeyStore
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey

/**
 * Where the phone's own secrets are kept. The private device keys and the database key are
 * generated on the phone and never stored in the clear: they are wrapped by a key that lives
 * inside the Android Keystore and cannot be exported from it.
 *
 * The interface exists so the same code runs in a test with an ordinary key in memory.
 */
interface KeyVault {
    /** Wraps a secret. [label] is bound into the wrap, so a wrap cannot be replayed elsewhere. */
    fun wrap(secret: ByteArray, label: String): ByteArray

    /** Unwraps a secret produced by [wrap] under the same label. */
    fun unwrap(wrapped: ByteArray, label: String): ByteArray

    /** A fresh random secret of [size] bytes, for a database key or a device seed. */
    fun newSecret(size: Int): ByteArray = RandomBytes.next(size)
}

/** Labels of the wrapped secrets, so one wrap can never be mistaken for another. */
object KeyVaultLabels {
    const val DEVICE_SEEDS = "wakeel.phone.device-seeds"
    const val DATABASE_KEY = "wakeel.phone.database-key"
    const val FILE_KEY = "wakeel.phone.file-key"
    const val SESSION_KEY = "wakeel.phone.session-key"
    const val OFFICE_KEY = "wakeel.phone.office-key"
}

/**
 * The real vault: an AES-256-GCM key held by the Android Keystore. The key material never
 * enters the application's own memory; only the wrapped blocks do.
 */
class AndroidKeyVault(
    private val alias: String = DEFAULT_ALIAS,
) : KeyVault {

    companion object {
        const val DEFAULT_ALIAS = "wakeel.phone.vault"
        private const val PROVIDER = "AndroidKeyStore"
    }

    override fun wrap(secret: ByteArray, label: String): ByteArray {
        val nonce = RandomBytes.next(Aead.NONCE_SIZE)
        val cipher = javax.crypto.Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(
            javax.crypto.Cipher.ENCRYPT_MODE,
            key(),
            javax.crypto.spec.GCMParameterSpec(Aead.TAG_SIZE * 8, nonce),
        )
        cipher.updateAAD(label.toByteArray(Charsets.UTF_8))
        return nonce + cipher.doFinal(secret)
    }

    override fun unwrap(wrapped: ByteArray, label: String): ByteArray {
        if (wrapped.size <= Aead.NONCE_SIZE + Aead.TAG_SIZE) {
            throw CryptoException(CryptoErrorCode.Corrupt, "the wrapped secret is too short")
        }
        val nonce = wrapped.copyOfRange(0, Aead.NONCE_SIZE)
        val body = wrapped.copyOfRange(Aead.NONCE_SIZE, wrapped.size)
        val cipher = javax.crypto.Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(
            javax.crypto.Cipher.DECRYPT_MODE,
            key(),
            javax.crypto.spec.GCMParameterSpec(Aead.TAG_SIZE * 8, nonce),
        )
        cipher.updateAAD(label.toByteArray(Charsets.UTF_8))
        return try {
            cipher.doFinal(body)
        } catch (exception: Exception) {
            throw CryptoException(CryptoErrorCode.Tampered, "the stored secret failed its check", exception)
        }
    }

    private fun key(): SecretKey {
        val store = KeyStore.getInstance(PROVIDER).apply { load(null) }
        (store.getEntry(alias, null) as? KeyStore.SecretKeyEntry)?.let { return it.secretKey }

        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, PROVIDER)
        val builder = KeyGenParameterSpec.Builder(
            alias,
            KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT,
        )
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
            .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
            .setKeySize(256)
            .setRandomizedEncryptionRequired(true)

        // On the devices that have one, the key is bound to the secure element; where there is
        // none the software backed key is still outside the application's own memory.
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            try {
                builder.setIsStrongBoxBacked(true)
                generator.init(builder.build())
                return generator.generateKey()
            } catch (_: Exception) {
                builder.setIsStrongBoxBacked(false)
            }
        }

        generator.init(builder.build())
        return generator.generateKey()
    }
}

/**
 * The vault used by the tests and by the interoperability vectors: an ordinary key held in
 * memory. It is never reachable from the application itself.
 */
class InMemoryKeyVault(private val key: ByteArray = RandomBytes.next(Aead.KEY_SIZE)) : KeyVault {
    override fun wrap(secret: ByteArray, label: String): ByteArray =
        Aead.encrypt(key, secret, label.toByteArray(Charsets.UTF_8))

    override fun unwrap(wrapped: ByteArray, label: String): ByteArray =
        Aead.decrypt(key, wrapped, label.toByteArray(Charsets.UTF_8))
}
