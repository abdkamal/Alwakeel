package ps.wakeel.phone.core

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import ps.wakeel.phone.core.certificates.DeviceCertificate
import ps.wakeel.phone.core.containers.ContainerEntrySource
import ps.wakeel.phone.core.containers.ContainerKeySource
import ps.wakeel.phone.core.containers.ContainerKind
import ps.wakeel.phone.core.containers.ContainerOpenOptions
import ps.wakeel.phone.core.containers.ContainerReader
import ps.wakeel.phone.core.containers.ContainerWriteRequest
import ps.wakeel.phone.core.containers.ContainerWriter
import ps.wakeel.phone.core.containers.PayloadMode
import ps.wakeel.phone.core.crypto.Base64Url
import ps.wakeel.phone.core.crypto.CryptoErrorCode
import ps.wakeel.phone.core.crypto.CryptoException
import ps.wakeel.phone.core.crypto.RandomBytes
import ps.wakeel.phone.core.json.CanonicalJson
import ps.wakeel.phone.core.json.requireText
import java.io.ByteArrayOutputStream
import java.io.File
import java.time.Duration
import java.util.zip.ZipFile

/**
 * The `.wakeel-phone` container, both ways: a packet the computer wrote is opened here, and a
 * packet written here is left where the Windows test reads it back.
 */
class ContainerVectorTest {

    private val pcCertificate = DeviceCertificate.fromJson(Vectors.jsonValue("certificates/pc.json"))
    private val phoneCertificate = DeviceCertificate.fromJson(Vectors.jsonValue("certificates/phone.json"))
    private val expected = Vectors.json("container/to-phone-1.expected.json")

    private fun containerFile(): File {
        // The vector travels as a resource, so it is staged to a real file to be opened.
        val staged = File.createTempFile("wakeel-vector-", ".wakeel-phone")
        staged.writeBytes(Vectors.bytes("container/to-phone-1.wakeel-phone"))
        staged.deleteOnExit()
        return staged
    }

    private fun options(now: java.time.Instant = Vectors.now) = ContainerOpenOptions(
        expectedKind = ContainerKind.Phone,
        orgSigningPublicKey = Vectors.org.signingPublicKey,
        issuerCertificate = pcCertificate,
        now = now,
    )

    @Test
    fun aPacketWrittenOnWindowsOpensHere() {
        ContainerReader.open(containerFile(), options()).use { reader ->
            assertEquals(ContainerKind.Phone, reader.manifest.type)
            assertEquals(PayloadMode.Session, reader.manifest.mode)
            assertEquals(Vectors.pcDeviceId, reader.manifest.producer.body.deviceId)
            assertEquals(2, reader.manifest.entries.size)

            val entries = reader.readEntries(ContainerKeySource.sessionKey(Vectors.sessionKey))
            assertEquals(
                expected.requireText("packageJson"),
                String(entries.getValue("package.json"), Charsets.UTF_8),
            )
            assertArrayEquals(
                Base64Url.decode(expected.requireText("pinnedFileBase64Url")),
                entries.getValue("files/pinned-1.txt"),
            )
        }
    }

    @Test
    fun theManifestReadsBackToTheVeryBytesWindowsSigned() {
        ContainerReader.open(containerFile(), options()).use { reader ->
            assertEquals(
                expected.requireText("manifestCanonical"),
                CanonicalJson.toText(reader.manifest.toJson()),
            )
        }
    }

    @Test
    fun oneItemCanBeTakenOutWithoutTheOthers() {
        ContainerReader.open(containerFile(), options()).use { reader ->
            val body = reader.readEntry("package.json", ContainerKeySource.sessionKey(Vectors.sessionKey))
            assertEquals(expected.requireText("packageJson"), String(body, Charsets.UTF_8))
            assertThrows(CryptoException::class.java) {
                reader.readEntry("no-such-item", ContainerKeySource.sessionKey(Vectors.sessionKey))
            }
        }
    }

    @Test
    fun theWrongSessionKeyDoesNotOpenThePayload() {
        ContainerReader.open(containerFile(), options()).use { reader ->
            assertThrows(CryptoException::class.java) {
                reader.readEntries(ContainerKeySource.sessionKey(RandomBytes.next(32)))
            }
        }
    }

    @Test
    fun aPacketWithoutTheOrganisationKeyIsRefused() {
        val error = assertThrows(CryptoException::class.java) {
            ContainerReader.open(
                containerFile(),
                ContainerOpenOptions(expectedKind = ContainerKind.Phone, now = Vectors.now),
            ).close()
        }
        assertEquals(CryptoErrorCode.BadSignature, error.code)
    }

    @Test
    fun aTamperedPayloadIsRefused() {
        val original = containerFile()
        val tampered = File.createTempFile("wakeel-tampered-", ".wakeel-phone")
        tampered.deleteOnExit()

        val parts = LinkedHashMap<String, ByteArray>()
        ZipFile(original).use { zip ->
            zip.entries().asSequence().forEach { entry ->
                parts[entry.name] = zip.getInputStream(entry).readBytes()
            }
        }
        val payload = parts.getValue("payload.bin")
        payload[payload.size - 1] = (payload[payload.size - 1].toInt() xor 0xFF).toByte()

        java.util.zip.ZipOutputStream(tampered.outputStream()).use { zip ->
            parts.forEach { (name, bytes) ->
                zip.putNextEntry(java.util.zip.ZipEntry(name))
                zip.write(bytes)
                zip.closeEntry()
            }
        }

        val error = assertThrows(CryptoException::class.java) {
            ContainerReader.open(tampered, options()).close()
        }
        assertEquals(CryptoErrorCode.BadSignature, error.code)
    }

    @Test
    fun aPacketDatedInTheFutureIsRefused() {
        val error = assertThrows(CryptoException::class.java) {
            ContainerReader.open(containerFile(), options(Vectors.now.minus(Duration.ofDays(3)))).close()
        }
        assertEquals(CryptoErrorCode.Expired, error.code)
    }

    @Test
    fun aPacketOfAnotherKindIsRefused() {
        val error = assertThrows(CryptoException::class.java) {
            ContainerReader.open(
                containerFile(),
                ContainerOpenOptions(
                    expectedKind = ContainerKind.Backup,
                    orgSigningPublicKey = Vectors.org.signingPublicKey,
                    issuerCertificate = pcCertificate,
                    now = Vectors.now,
                ),
            ).close()
        }
        assertEquals(CryptoErrorCode.UnknownKind, error.code)
    }

    @Test
    fun aPacketWrittenHereComesBackTheSame() {
        val body = CanonicalJson.toBytes(CanonicalJson.parse(expected.requireText("packageJson")))
        val attachment = "صورة إيصال".toByteArray(Charsets.UTF_8)

        val target = File.createTempFile("wakeel-out-", ".wakeel-phone")
        target.deleteOnExit()
        ContainerWriter.write(
            target,
            ContainerWriteRequest(
                kind = ContainerKind.Phone,
                producer = phoneCertificate,
                signer = Vectors.phone,
                key = ContainerKeySource.sessionKey(Vectors.sessionKey),
                entries = listOf(
                    ContainerEntrySource.ofBytes("package.json", body),
                    ContainerEntrySource.ofBytes("files/receipt-1.jpg", attachment),
                ),
                createdAt = Vectors.now,
            ),
        )

        ContainerReader.open(
            target,
            ContainerOpenOptions(
                expectedKind = ContainerKind.Phone,
                orgSigningPublicKey = Vectors.org.signingPublicKey,
                issuerCertificate = pcCertificate,
                now = Vectors.now,
            ),
        ).use { reader ->
            val entries = reader.readEntries(ContainerKeySource.sessionKey(Vectors.sessionKey))
            assertArrayEquals(body, entries.getValue("package.json"))
            assertArrayEquals(attachment, entries.getValue("files/receipt-1.jpg"))
            // The items are listed in name order, the way the writer sorts them.
            assertEquals(listOf("files/receipt-1.jpg", "package.json"), reader.manifest.entries.map { it.name })
        }
    }

    @Test
    fun anItemNameThatWouldEscapeTheFolderIsRefused() {
        listOf("../escape.txt", "/absolute.txt", "c:\\windows\\x", "", "files/").forEach { name ->
            assertThrows("the name $name must be refused", CryptoException::class.java) {
                ContainerEntrySource.ofBytes(name, ByteArray(1))
            }
        }
        assertTrue(ContainerEntrySource.isAcceptableName("files/pinned-1.txt"))
    }

    @Test
    fun aPacketWrittenHereIsLeftForTheWindowsSide() {
        val out = Vectors.outputDirectory("container")
        val body = CanonicalJson.toBytes(CanonicalJson.parse(expected.requireText("packageJson")))
        val bytes = ByteArrayOutputStream()
        ContainerWriter.write(
            bytes,
            ContainerWriteRequest(
                kind = ContainerKind.Phone,
                producer = phoneCertificate,
                signer = Vectors.phone,
                key = ContainerKeySource.sessionKey(Vectors.sessionKey),
                entries = listOf(ContainerEntrySource.ofBytes("package.json", body)),
                createdAt = Vectors.now,
            ),
        )
        File(out, "from-phone-1.wakeel-phone").writeBytes(bytes.toByteArray())
    }
}
