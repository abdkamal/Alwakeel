package ps.wakeel.phone.core.files

import ps.wakeel.phone.core.crypto.Aead
import ps.wakeel.phone.core.crypto.KeyVault
import ps.wakeel.phone.core.crypto.KeyVaultLabels
import ps.wakeel.phone.core.crypto.Sha256
import java.io.File

/**
 * The files that belong to the phone's own records — a scanned page, a receipt, a voice note,
 * a pinned document from the computer. They live in the application's private storage and are
 * encrypted there with a key wrapped by the Android Keystore, so a copy of the storage taken
 * off the phone reveals nothing (item 24 of the agreement).
 */
class PhoneFileStore(
    private val directory: File,
    private val vault: KeyVault,
    private val wrappedKey: ByteArray,
) {
    init {
        directory.mkdirs()
    }

    /** Makes the file key for a phone that has none yet, and returns it wrapped. */
    companion object {
        fun newWrappedKey(vault: KeyVault): ByteArray =
            vault.wrap(vault.newSecret(Aead.KEY_SIZE), KeyVaultLabels.FILE_KEY)
    }

    /** Stores bytes under a name of the product's own choosing and returns that name. */
    fun put(bytes: ByteArray, suffix: String = ".bin"): String {
        val name = Sha256.toHex(Sha256.hash(bytes)).take(32) + suffix
        write(name, bytes)
        return name
    }

    fun write(name: String, bytes: ByteArray) {
        val key = unwrapKey()
        try {
            val sealed = Aead.encrypt(key, bytes, associatedData(name))
            val partial = File(directory, "$name.part")
            partial.writeBytes(sealed)
            val target = File(directory, name)
            if (target.exists()) target.delete()
            if (!partial.renameTo(target)) {
                partial.delete()
                throw java.io.IOException("the file could not be put in place")
            }
        } finally {
            key.fill(0)
        }
    }

    fun read(name: String): ByteArray {
        val key = unwrapKey()
        try {
            return Aead.decrypt(key, File(directory, name).readBytes(), associatedData(name))
        } finally {
            key.fill(0)
        }
    }

    fun exists(name: String): Boolean = File(directory, name).isFile

    fun size(name: String): Long = File(directory, name).length()

    fun delete(name: String) {
        File(directory, name).delete()
    }

    /** The name is bound into the encryption, so one stored file cannot be passed off as another. */
    private fun associatedData(name: String): ByteArray = "wakeel.phone.file|$name".toByteArray(Charsets.UTF_8)

    private fun unwrapKey(): ByteArray = vault.unwrap(wrappedKey, KeyVaultLabels.FILE_KEY)
}
