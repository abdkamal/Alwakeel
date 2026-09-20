package ps.wakeel.phone.core.crypto

import java.security.MessageDigest
import java.security.SecureRandom
import javax.crypto.Cipher
import javax.crypto.Mac
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

/**
 * Why the phone refused something. The codes are the same set the Windows side uses
 * (`Wakeel.Crypto/CryptoException.cs`); they never reach the screen — the user interface turns
 * them into Arabic sentences, item 15 of the agreement.
 */
enum class CryptoErrorCode {
    Corrupt,
    BadSignature,
    Tampered,
    WrongPassword,
    Expired,
    Revoked,
    UnknownKind,
}

class CryptoException(
    val code: CryptoErrorCode,
    message: String,
    cause: Throwable? = null,
) : Exception(message, cause)

/** Unpadded base64url (RFC 4648 §5), the form every binary value takes inside JSON. */
object Base64Url {
    private val encoder = java.util.Base64.getUrlEncoder().withoutPadding()
    private val decoder = java.util.Base64.getUrlDecoder()

    fun encode(data: ByteArray): String = if (data.isEmpty()) "" else encoder.encodeToString(data)

    fun decode(value: String): ByteArray =
        tryDecode(value) ?: throw CryptoException(CryptoErrorCode.Corrupt, "not base64url text")

    fun tryDecode(value: String?): ByteArray? {
        if (value == null) return null
        if (value.isEmpty()) return ByteArray(0)
        return try {
            decoder.decode(value)
        } catch (_: IllegalArgumentException) {
            null
        }
    }
}

/** Standard base64 with padding — the form .NET gives a byte array inside JSON. */
object Base64Standard {
    private val encoder = java.util.Base64.getEncoder()
    private val decoder = java.util.Base64.getDecoder()

    fun encode(data: ByteArray): String = encoder.encodeToString(data)

    fun decode(value: String): ByteArray = try {
        decoder.decode(value)
    } catch (exception: IllegalArgumentException) {
        throw CryptoException(CryptoErrorCode.Corrupt, "not base64 text", exception)
    }
}

object Sha256 {
    const val HASH_SIZE = 32

    fun hash(data: ByteArray): ByteArray = MessageDigest.getInstance("SHA-256").digest(data)

    fun hash(stream: java.io.InputStream): ByteArray {
        val digest = MessageDigest.getInstance("SHA-256")
        val buffer = ByteArray(81920)
        while (true) {
            val read = stream.read(buffer)
            if (read <= 0) break
            digest.update(buffer, 0, read)
        }
        return digest.digest()
    }

    /** Lower case hexadecimal, the form a container manifest stores. */
    fun toHex(hash: ByteArray): String {
        val builder = StringBuilder(hash.size * 2)
        for (byte in hash) {
            val value = byte.toInt() and 0xFF
            builder.append("0123456789abcdef"[value ushr 4])
            builder.append("0123456789abcdef"[value and 0x0F])
        }
        return builder.toString()
    }

    /** Constant time comparison, for hashes and proofs. */
    fun fixedTimeEquals(left: ByteArray, right: ByteArray): Boolean {
        if (left.size != right.size) return false
        var difference = 0
        for (index in left.indices) {
            difference = difference or (left[index].toInt() xor right[index].toInt())
        }
        return difference == 0
    }
}

/** HMAC-SHA256, used by the pairing codes and by HKDF. */
object HmacSha256 {
    fun compute(key: ByteArray, data: ByteArray): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        // An all zero key of the block length is what HKDF extract uses when no salt is given,
        // and javax.crypto refuses an empty key, so the empty case is widened here.
        val material = if (key.isEmpty()) ByteArray(32) else key
        mac.init(SecretKeySpec(material, "HmacSHA256"))
        return mac.doFinal(data)
    }
}

/**
 * HKDF-SHA256, byte for byte the same derivation the Windows side performs
 * (`Wakeel.Crypto/Primitives/Hkdf.cs`). Every derived key carries a context label, so the same
 * input material can never produce the same sub key for two purposes.
 */
object Hkdf {
    fun extract(inputKeyMaterial: ByteArray, salt: ByteArray): ByteArray =
        HmacSha256.compute(salt, inputKeyMaterial)

    fun expand(pseudoRandomKey: ByteArray, length: Int, info: ByteArray): ByteArray {
        require(length > 0) { "length" }
        val output = ByteArray(length)
        var previous = ByteArray(0)
        var produced = 0
        var counter = 1
        while (produced < length) {
            val input = previous + info + byteArrayOf(counter.toByte())
            previous = HmacSha256.compute(pseudoRandomKey, input)
            val take = minOf(previous.size, length - produced)
            previous.copyInto(output, produced, 0, take)
            produced += take
            counter++
        }
        return output
    }

    fun deriveKey(inputKeyMaterial: ByteArray, length: Int, salt: ByteArray, info: ByteArray): ByteArray =
        expand(extract(inputKeyMaterial, salt), length, info)

    fun deriveKey(inputKeyMaterial: ByteArray, length: Int, salt: ByteArray, info: String): ByteArray =
        deriveKey(inputKeyMaterial, length, salt, info.toByteArray(Charsets.UTF_8))

    fun deriveKey(inputKeyMaterial: ByteArray, length: Int, info: String): ByteArray =
        deriveKey(inputKeyMaterial, length, ByteArray(0), info.toByteArray(Charsets.UTF_8))
}

/** Random bytes from the platform's own generator; nothing in this product invents entropy. */
object RandomBytes {
    private val random = SecureRandom()

    fun next(size: Int): ByteArray = ByteArray(size).also { random.nextBytes(it) }

    fun fill(buffer: ByteArray) = random.nextBytes(buffer)
}

/**
 * AES-256-GCM in the exact layout of `Wakeel.Crypto/Primitives/Aead.cs`.
 *
 * Buffer: `[version:1][nonce:12][ciphertext][tag:16]`.
 * Stream: `[version:1][chunkSize:4 little endian]` then frames
 * `[final:1][nonce:12][length:4 little endian][ciphertext+tag]`; the last frame carries
 * `final = 1` with an empty plaintext, which is what makes a truncated file detectable.
 */
object Aead {
    const val FORMAT_VERSION: Byte = 1
    const val KEY_SIZE = 32
    const val NONCE_SIZE = 12
    const val TAG_SIZE = 16
    const val DEFAULT_CHUNK_SIZE = 1 shl 20

    internal const val HEADER_SIZE = 5
    private const val MIN_CHUNK_SIZE = 1024
    private const val MAX_CHUNK_SIZE = 16 shl 20

    fun encrypt(key: ByteArray, plaintext: ByteArray, associatedData: ByteArray): ByteArray {
        checkKey(key)
        val nonce = RandomBytes.next(NONCE_SIZE)
        val cipher = gcm(Cipher.ENCRYPT_MODE, key, nonce, buildAssociatedData(FORMAT_VERSION, associatedData))
        val sealed = cipher.doFinal(plaintext)
        val result = ByteArray(1 + NONCE_SIZE + sealed.size)
        result[0] = FORMAT_VERSION
        nonce.copyInto(result, 1)
        sealed.copyInto(result, 1 + NONCE_SIZE)
        return result
    }

    fun encrypt(key: ByteArray, plaintext: ByteArray, associatedData: String): ByteArray =
        encrypt(key, plaintext, associatedData.toByteArray(Charsets.UTF_8))

    fun decrypt(key: ByteArray, sealedData: ByteArray, associatedData: ByteArray): ByteArray {
        checkKey(key)
        if (sealedData.size < 1 + NONCE_SIZE + TAG_SIZE) {
            throw CryptoException(CryptoErrorCode.Corrupt, "encrypted block too short")
        }
        if (sealedData[0] != FORMAT_VERSION) {
            throw CryptoException(CryptoErrorCode.UnknownKind, "unsupported encrypted block version")
        }
        val nonce = sealedData.copyOfRange(1, 1 + NONCE_SIZE)
        val body = sealedData.copyOfRange(1 + NONCE_SIZE, sealedData.size)
        val cipher = gcm(Cipher.DECRYPT_MODE, key, nonce, buildAssociatedData(sealedData[0], associatedData))
        return try {
            cipher.doFinal(body)
        } catch (exception: GeneralSecurityFailure) {
            throw exception
        } catch (exception: Exception) {
            throw CryptoException(CryptoErrorCode.Tampered, "integrity check failed", exception)
        }
    }

    fun decrypt(key: ByteArray, sealedData: ByteArray, associatedData: String): ByteArray =
        decrypt(key, sealedData, associatedData.toByteArray(Charsets.UTF_8))

    fun encryptStream(
        key: ByteArray,
        input: java.io.InputStream,
        output: java.io.OutputStream,
        associatedData: ByteArray,
        chunkSize: Int = DEFAULT_CHUNK_SIZE,
    ) {
        checkKey(key)
        if (chunkSize < MIN_CHUNK_SIZE || chunkSize > MAX_CHUNK_SIZE) {
            throw CryptoException(CryptoErrorCode.Corrupt, "chunk size out of range")
        }

        val header = ByteArray(HEADER_SIZE)
        header[0] = FORMAT_VERSION
        writeInt32LittleEndian(header, 1, chunkSize)
        output.write(header)

        val plainBuffer = ByteArray(chunkSize)
        var index = 0L
        while (true) {
            val read = readAtMost(input, plainBuffer)
            val isFinal = read == 0
            val frameAad = frameAssociatedData(header, associatedData, index, isFinal)
            val nonce = RandomBytes.next(NONCE_SIZE)
            val sealed = gcm(Cipher.ENCRYPT_MODE, key, nonce, frameAad)
                .doFinal(plainBuffer, 0, read)

            val frameHeader = ByteArray(1 + NONCE_SIZE + 4)
            frameHeader[0] = if (isFinal) 1 else 0
            nonce.copyInto(frameHeader, 1)
            writeInt32LittleEndian(frameHeader, 1 + NONCE_SIZE, sealed.size)
            output.write(frameHeader)
            output.write(sealed)

            index++
            if (isFinal) break
        }
        output.flush()
    }

    fun decryptStream(
        key: ByteArray,
        input: java.io.InputStream,
        output: java.io.OutputStream,
        associatedData: ByteArray,
    ) {
        checkKey(key)
        val header = ByteArray(HEADER_SIZE)
        readExactly(input, header)
        if (header[0] != FORMAT_VERSION) {
            throw CryptoException(CryptoErrorCode.UnknownKind, "unsupported payload version")
        }
        val chunkSize = readInt32LittleEndian(header, 1)
        if (chunkSize < MIN_CHUNK_SIZE || chunkSize > MAX_CHUNK_SIZE) {
            throw CryptoException(CryptoErrorCode.Corrupt, "impossible chunk size")
        }

        val frameHeader = ByteArray(1 + NONCE_SIZE + 4)
        var index = 0L
        while (true) {
            readExactly(input, frameHeader)
            val flag = frameHeader[0].toInt()
            if (flag > 1) throw CryptoException(CryptoErrorCode.Corrupt, "invalid frame")

            val length = readInt32LittleEndian(frameHeader, 1 + NONCE_SIZE)
            if (length < TAG_SIZE || length > chunkSize + TAG_SIZE) {
                throw CryptoException(CryptoErrorCode.Corrupt, "invalid frame length")
            }

            val cipherBuffer = ByteArray(length)
            readExactly(input, cipherBuffer)
            val nonce = frameHeader.copyOfRange(1, 1 + NONCE_SIZE)
            val frameAad = frameAssociatedData(header, associatedData, index, flag == 1)
            val plain = try {
                gcm(Cipher.DECRYPT_MODE, key, nonce, frameAad).doFinal(cipherBuffer)
            } catch (exception: Exception) {
                throw CryptoException(CryptoErrorCode.Tampered, "payload integrity check failed", exception)
            }
            if (plain.isNotEmpty()) output.write(plain)

            index++
            if (flag == 1) {
                if (input.read() != -1) {
                    throw CryptoException(CryptoErrorCode.Tampered, "data after the final frame")
                }
                break
            }
        }
        output.flush()
    }

    private fun gcm(mode: Int, key: ByteArray, nonce: ByteArray, associatedData: ByteArray): Cipher {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(mode, SecretKeySpec(key, "AES"), GCMParameterSpec(TAG_SIZE * 8, nonce))
        cipher.updateAAD(associatedData)
        return cipher
    }

    private fun buildAssociatedData(version: Byte, associatedData: ByteArray): ByteArray {
        val buffer = ByteArray(1 + associatedData.size)
        buffer[0] = version
        associatedData.copyInto(buffer, 1)
        return buffer
    }

    /**
     * A frame authenticates the whole stream header, the caller's own associated data, the
     * frame's position and whether it is the last one — so a reordered, dropped or re-labelled
     * frame breaks the tag instead of passing silently.
     */
    private fun frameAssociatedData(
        header: ByteArray,
        associated: ByteArray,
        index: Long,
        isFinal: Boolean,
    ): ByteArray {
        val buffer = ByteArray(header.size + associated.size + 8 + 1)
        header.copyInto(buffer, 0)
        associated.copyInto(buffer, header.size)
        var value = index
        for (offset in 7 downTo 0) {
            buffer[header.size + associated.size + offset] = (value and 0xFF).toByte()
            value = value ushr 8
        }
        buffer[buffer.size - 1] = if (isFinal) 1 else 0
        return buffer
    }

    private fun checkKey(key: ByteArray) {
        if (key.size != KEY_SIZE) {
            throw CryptoException(CryptoErrorCode.Corrupt, "the key must be thirty two bytes")
        }
    }

    private fun readAtMost(stream: java.io.InputStream, buffer: ByteArray): Int {
        var total = 0
        while (total < buffer.size) {
            val read = stream.read(buffer, total, buffer.size - total)
            if (read <= 0) break
            total += read
        }
        return total
    }

    internal fun readExactly(stream: java.io.InputStream, buffer: ByteArray) {
        var total = 0
        while (total < buffer.size) {
            val read = stream.read(buffer, total, buffer.size - total)
            if (read <= 0) {
                throw CryptoException(CryptoErrorCode.Corrupt, "the payload ended earlier than expected")
            }
            total += read
        }
    }

    private fun writeInt32LittleEndian(buffer: ByteArray, offset: Int, value: Int) {
        buffer[offset] = (value and 0xFF).toByte()
        buffer[offset + 1] = ((value ushr 8) and 0xFF).toByte()
        buffer[offset + 2] = ((value ushr 16) and 0xFF).toByte()
        buffer[offset + 3] = ((value ushr 24) and 0xFF).toByte()
    }

    private fun readInt32LittleEndian(buffer: ByteArray, offset: Int): Int =
        (buffer[offset].toInt() and 0xFF) or
            ((buffer[offset + 1].toInt() and 0xFF) shl 8) or
            ((buffer[offset + 2].toInt() and 0xFF) shl 16) or
            ((buffer[offset + 3].toInt() and 0xFF) shl 24)
}

/** Marker so a deliberate refusal inside a cipher call is not re-wrapped as damage. */
internal class GeneralSecurityFailure(message: String) : Exception(message)
