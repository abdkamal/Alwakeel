package ps.wakeel.phone.core

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertThrows
import org.junit.Test
import ps.wakeel.phone.core.crypto.Aead
import ps.wakeel.phone.core.crypto.Base64Url
import ps.wakeel.phone.core.crypto.CryptoException
import ps.wakeel.phone.core.crypto.DeviceIdentity
import ps.wakeel.phone.core.crypto.Hkdf
import ps.wakeel.phone.core.crypto.RandomBytes
import ps.wakeel.phone.core.crypto.Sha256
import ps.wakeel.phone.core.json.requireInt
import ps.wakeel.phone.core.json.requireText
import ps.wakeel.phone.core.json.asObject
import ps.wakeel.phone.core.pairing.PairingCodes
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream

/** Every primitive answers with the same bytes the Windows implementation produced. */
class PrimitivesVectorTest {

    private val vectors = Vectors.json("primitives.json")
    private val message = Base64Url.decode(vectors.requireText("messageBase64Url"))

    @Test
    fun sha256MatchesTheVector() {
        assertEquals(vectors.requireText("sha256OfMessageHex"), Sha256.toHex(Sha256.hash(message)))
    }

    @Test
    fun hkdfWithASaltMatchesTheVector() {
        val part = vectors.members.getValue("hkdf").asObject()
        val derived = Hkdf.deriveKey(
            Vectors.sessionKey,
            32,
            Base64Url.decode(part.requireText("saltBase64Url")),
            part.requireText("info"),
        )
        assertEquals(part.requireText("outputBase64Url"), Base64Url.encode(derived))
    }

    @Test
    fun hkdfWithoutASaltMatchesTheVector() {
        val part = vectors.members.getValue("hkdfNoSalt").asObject()
        val derived = Hkdf.deriveKey(Vectors.officeKey, 64, part.requireText("info"))
        assertEquals(part.requireText("outputBase64Url"), Base64Url.encode(derived))
    }

    @Test
    fun aBufferSealedOnWindowsOpensHere() {
        val part = vectors.members.getValue("aeadBuffer").asObject()
        val opened = Aead.decrypt(
            Vectors.sessionKey,
            Base64Url.decode(part.requireText("sealedBase64Url")),
            part.requireText("associatedData"),
        )
        assertArrayEquals(message, opened)
    }

    @Test
    fun aBufferSealedHereCarriesTheSameShape() {
        val sealed = Aead.encrypt(Vectors.sessionKey, message, "wakeel.vector")
        assertEquals(Aead.FORMAT_VERSION, sealed[0])
        assertEquals(1 + Aead.NONCE_SIZE + message.size + Aead.TAG_SIZE, sealed.size)
        assertArrayEquals(message, Aead.decrypt(Vectors.sessionKey, sealed, "wakeel.vector"))
    }

    @Test
    fun aStreamSealedOnWindowsOpensHere() {
        val part = vectors.members.getValue("aeadStream").asObject()
        val output = ByteArrayOutputStream()
        Aead.decryptStream(
            Vectors.sessionKey,
            ByteArrayInputStream(Base64Url.decode(part.requireText("sealedBase64Url"))),
            output,
            part.requireText("associatedData").toByteArray(Charsets.UTF_8),
        )
        assertArrayEquals(message, output.toByteArray())
        assertEquals(1024, part.requireInt("chunkSize"))
    }

    @Test
    fun aStreamSealedHereSurvivesARoundTripAndRefusesATruncation() {
        val body = RandomBytes.next(5000)
        val sealed = ByteArrayOutputStream()
        Aead.encryptStream(
            Vectors.sessionKey,
            ByteArrayInputStream(body),
            sealed,
            "wakeel.vector.stream".toByteArray(Charsets.UTF_8),
            1024,
        )

        val opened = ByteArrayOutputStream()
        Aead.decryptStream(
            Vectors.sessionKey,
            ByteArrayInputStream(sealed.toByteArray()),
            opened,
            "wakeel.vector.stream".toByteArray(Charsets.UTF_8),
        )
        assertArrayEquals(body, opened.toByteArray())

        // The final frame is what makes a cut file detectable; without it the payload is refused.
        val truncated = sealed.toByteArray().copyOfRange(0, sealed.size() - 40)
        assertThrows(CryptoException::class.java) {
            Aead.decryptStream(
                Vectors.sessionKey,
                ByteArrayInputStream(truncated),
                ByteArrayOutputStream(),
                "wakeel.vector.stream".toByteArray(Charsets.UTF_8),
            )
        }
    }

    @Test
    fun theWrongAssociatedDataIsRefused() {
        val sealed = Aead.encrypt(Vectors.sessionKey, message, "wakeel.vector")
        assertThrows(CryptoException::class.java) {
            Aead.decrypt(Vectors.sessionKey, sealed, "wakeel.other")
        }
    }

    @Test
    fun theShortCodeAndTheTokenProofMatchTheVector() {
        assertEquals(vectors.requireText("shortCodeOfPairingToken"), PairingCodes.shortCode(Vectors.pairingToken))
        assertEquals(Vectors.pairingShortCode, PairingCodes.shortCode(Vectors.pairingToken))
        assertEquals(
            vectors.requireText("tokenProof"),
            PairingCodes.tokenProof(Vectors.pairingToken, Vectors.phoneDeviceId),
        )
        // The proof names the phone, so one phone's proof cannot be replayed by another.
        assertNotEquals(
            vectors.requireText("tokenProof"),
            PairingCodes.tokenProof(Vectors.pairingToken, "ANOTHER-PHONE"),
        )
    }

    @Test
    fun theDevicePublicKeysMatchTheVector() {
        val org = Vectors.keys.members.getValue("org").asObject()
        assertEquals(org.requireText("ed25519Pub"), Vectors.org.signingPublicKeyText)
        assertEquals(org.requireText("x25519Pub"), Vectors.org.agreementPublicKeyText)

        val pc = Vectors.keys.members.getValue("pc").asObject()
        assertEquals(pc.requireText("ed25519Pub"), Vectors.pc.signingPublicKeyText)
        assertEquals(pc.requireText("x25519Pub"), Vectors.pc.agreementPublicKeyText)

        val phone = Vectors.keys.members.getValue("phone").asObject()
        assertEquals(phone.requireText("ed25519Pub"), Vectors.phone.signingPublicKeyText)
        assertEquals(phone.requireText("x25519Pub"), Vectors.phone.agreementPublicKeyText)
    }

    @Test
    fun bothSidesAgreeOnTheSameSharedKeyWhicheverStarted() {
        val fromPhone = Vectors.phone.agreeWith(Vectors.pc.agreementPublicKey, "test")
        val fromPc = Vectors.pc.agreeWith(Vectors.phone.agreementPublicKey, "test")
        assertArrayEquals(fromPhone, fromPc)
        assertNotEquals(
            Base64Url.encode(fromPhone),
            Base64Url.encode(Vectors.phone.agreeWith(Vectors.pc.agreementPublicKey, "other")),
        )
    }

    @Test
    fun aSealedBoxOpensOnlyForItsRecipient() {
        val payload = "سرّ قصير".toByteArray(Charsets.UTF_8)
        val box = DeviceIdentity.sealFor(Vectors.phone.agreementPublicKey, payload, "wakeel.vector.seal")
        assertArrayEquals(payload, Vectors.phone.open(box, "wakeel.vector.seal"))
        assertThrows(CryptoException::class.java) { Vectors.pc.open(box, "wakeel.vector.seal") }
        assertThrows(CryptoException::class.java) { Vectors.phone.open(box, "wakeel.other") }
    }
}
