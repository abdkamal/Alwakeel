package ps.wakeel.phone.core.containers

import ps.wakeel.phone.core.certificates.CertificateChain
import ps.wakeel.phone.core.certificates.DeviceCertificate
import ps.wakeel.phone.core.certificates.DeviceKind
import ps.wakeel.phone.core.certificates.RevocationList
import ps.wakeel.phone.core.crypto.Aead
import ps.wakeel.phone.core.crypto.Base64Standard
import ps.wakeel.phone.core.crypto.Base64Url
import ps.wakeel.phone.core.crypto.CryptoErrorCode
import ps.wakeel.phone.core.crypto.CryptoException
import ps.wakeel.phone.core.crypto.DeviceIdentity
import ps.wakeel.phone.core.crypto.DomainSeparation
import ps.wakeel.phone.core.crypto.Hkdf
import ps.wakeel.phone.core.crypto.RandomBytes
import ps.wakeel.phone.core.crypto.Sha256
import ps.wakeel.phone.core.json.CanonicalJson
import ps.wakeel.phone.core.json.JsonValue
import ps.wakeel.phone.core.json.asObject
import ps.wakeel.phone.core.json.jsonArray
import ps.wakeel.phone.core.json.jsonNumber
import ps.wakeel.phone.core.json.jsonObject
import ps.wakeel.phone.core.json.jsonString
import ps.wakeel.phone.core.json.requireText
import ps.wakeel.phone.core.json.text
import java.io.File
import java.io.InputStream
import java.io.OutputStream
import java.security.DigestOutputStream
import java.security.MessageDigest
import java.time.Duration
import java.time.Instant
import java.util.zip.ZipEntry
import java.util.zip.ZipFile
import java.util.zip.ZipOutputStream

/** The seven signed container kinds the product exchanges. */
enum class ContainerKind(val token: String) {
    Setup("setup"),
    Sync("sync"),
    Msg("msg"),
    Transfer("transfer"),
    Inventory("inventory"),
    Phone("phone"),
    Backup("backup");

    val extension: String get() = ".wakeel-$token"

    companion object {
        fun fromToken(token: String?): ContainerKind = entries.firstOrNull { it.token == token }
            ?: throw CryptoException(CryptoErrorCode.UnknownKind, "unknown container kind")
    }
}

/** How the payload of a container is encrypted. */
enum class PayloadMode(val token: String) {
    OfficeKey("officeKey"),
    Password("password"),
    SealedFor("sealedFor"),
    Session("session");

    companion object {
        fun fromToken(token: String?): PayloadMode = entries.firstOrNull { it.token == token }
            ?: throw CryptoException(CryptoErrorCode.UnknownKind, "unknown payload mode")
    }
}

/** One logical file carried inside the payload, with its plain size and hash. */
data class ContainerEntry(val name: String, val size: Long, val sha256: String)

/**
 * The plain text, signed description of a container. It is read and checked in full before a
 * single byte of the payload is decrypted.
 */
data class ContainerManifest(
    val type: ContainerKind,
    val version: Int,
    val producer: DeviceCertificate,
    val createdAt: Instant,
    val mode: PayloadMode,
    val kdf: JsonValue? = null,
    val sealedKey: String? = null,
    val adminSealedKey: String? = null,
    val keySalt: ByteArray? = null,
    val entries: List<ContainerEntry>,
) {
    companion object {
        const val CURRENT_VERSION = 1
        const val FILE_NAME = "manifest.json"
        const val PAYLOAD_FILE_NAME = "payload.bin"
        const val SIGNATURE_FILE_NAME = "signature.bin"

        fun fromJson(value: JsonValue): ContainerManifest {
            val obj = value.asObject()
            val producer = obj.members["producer"]
                ?: throw CryptoException(CryptoErrorCode.Corrupt, "the file does not name its producer")
            val entries = (obj.members["entries"] as? JsonValue.Arr)?.items.orEmpty().map { item ->
                val entry = item.asObject()
                ContainerEntry(
                    name = entry.requireText("name"),
                    size = (entry.members["size"] as? JsonValue.Num)?.raw?.toLongOrNull()
                        ?: throw CryptoException(CryptoErrorCode.Corrupt, "an item declares no size"),
                    sha256 = entry.requireText("sha256"),
                )
            }
            return ContainerManifest(
                type = ContainerKind.fromToken(obj.requireText("type")),
                version = (obj.members["version"] as? JsonValue.Num)?.raw?.toIntOrNull()
                    ?: throw CryptoException(CryptoErrorCode.Corrupt, "the file declares no version"),
                producer = DeviceCertificate.fromJson(producer),
                createdAt = CanonicalJson.parseInstant(obj.requireText("createdAt")),
                mode = PayloadMode.fromToken(obj.requireText("mode")),
                kdf = obj.members["kdf"],
                sealedKey = obj.text("sealedKey"),
                adminSealedKey = obj.text("adminSealedKey"),
                keySalt = obj.text("keySalt")?.let { Base64Standard.decode(it) },
                entries = entries,
            )
        }
    }

    fun toJson(): JsonValue = jsonObject(
        "type" to jsonString(type.token),
        "version" to jsonNumber(version),
        "producer" to producer.toJson(),
        "createdAt" to CanonicalJson.jsonInstant(createdAt),
        "mode" to jsonString(mode.token),
        "kdf" to kdf,
        "sealedKey" to sealedKey?.let { jsonString(it) },
        "adminSealedKey" to adminSealedKey?.let { jsonString(it) },
        // A byte array crosses JSON as standard base64 with padding, because that is what the
        // Windows serializer writes for one.
        "keySalt" to keySalt?.let { jsonString(Base64Standard.encode(it)) },
        "entries" to jsonArray(
            entries.map {
                jsonObject(
                    "name" to jsonString(it.name),
                    "size" to JsonValue.Num(it.size.toString()),
                    "sha256" to jsonString(it.sha256),
                )
            },
        ),
    )

    override fun equals(other: Any?): Boolean = this === other
    override fun hashCode(): Int = System.identityHashCode(this)
}

/** Tells the writer how to protect a payload, and the reader how to open one. */
class ContainerKeySource private constructor(
    val mode: PayloadMode,
    private val rawKey: ByteArray? = null,
    private val recipient: DeviceIdentity? = null,
    private val recipientPublicKey: ByteArray? = null,
) {
    companion object {
        private const val SUB_KEY_LABEL = "wakeel.container"
        private const val SEALED_KEY_LABEL = "wakeel.container.key"
        internal const val KEY_SALT_SIZE = 16

        /** The office key shared by every device of one office. */
        fun officeKey(officeKey: ByteArray) = ContainerKeySource(PayloadMode.OfficeKey, rawKey = officeKey.copyOf())

        /** The session key agreed between the computer and this phone while pairing. */
        fun sessionKey(sessionKey: ByteArray) = ContainerKeySource(PayloadMode.Session, rawKey = sessionKey.copyOf())

        /** Write side: seal a fresh content key to one recipient. */
        fun sealFor(recipientAgreementPublicKey: ByteArray) =
            ContainerKeySource(PayloadMode.SealedFor, recipientPublicKey = recipientAgreementPublicKey.copyOf())

        /** Read side: open a sealed content key with this device's own keys. */
        fun forRecipient(identity: DeviceIdentity) = ContainerKeySource(PayloadMode.SealedFor, recipient = identity)

        internal fun subKey(rootKey: ByteArray, kind: ContainerKind, salt: ByteArray): ByteArray {
            if (rootKey.size != Aead.KEY_SIZE) {
                throw CryptoException(CryptoErrorCode.Corrupt, "the supplied key must be thirty two bytes")
            }
            return Hkdf.deriveKey(rootKey, Aead.KEY_SIZE, salt, "$SUB_KEY_LABEL|${kind.token}")
        }
    }

    /** Produces the content key used to encrypt a new payload. */
    internal fun createContentKey(kind: ContainerKind): CreatedKey = when (mode) {
        PayloadMode.OfficeKey, PayloadMode.Session -> {
            val salt = RandomBytes.next(KEY_SALT_SIZE)
            CreatedKey(subKey(rawKey!!, kind, salt), keySalt = salt)
        }

        PayloadMode.SealedFor -> {
            val contentKey = RandomBytes.next(Aead.KEY_SIZE)
            CreatedKey(
                contentKey,
                sealedKey = Base64Url.encode(
                    DeviceIdentity.sealFor(recipientPublicKey!!, contentKey, SEALED_KEY_LABEL),
                ),
            )
        }

        PayloadMode.Password ->
            throw CryptoException(CryptoErrorCode.UnknownKind, "the phone never writes a password protected file")
    }

    /** Recovers the content key of an existing payload. */
    internal fun openContentKey(manifest: ContainerManifest): ByteArray {
        if (manifest.mode != mode) {
            throw CryptoException(CryptoErrorCode.UnknownKind, "this file is not protected the way you are opening it")
        }
        return when (mode) {
            PayloadMode.OfficeKey, PayloadMode.Session -> {
                val salt = manifest.keySalt
                if (salt == null || salt.size != KEY_SALT_SIZE) {
                    throw CryptoException(CryptoErrorCode.Corrupt, "the file carries no key derivation salt")
                }
                subKey(rawKey!!, manifest.type, salt)
            }

            PayloadMode.SealedFor -> {
                val identity = recipient
                    ?: throw CryptoException(CryptoErrorCode.Corrupt, "opening a sealed file needs the device keys")
                val sealed = manifest.sealedKey
                    ?: throw CryptoException(CryptoErrorCode.Corrupt, "the file carries no sealed content key")
                try {
                    identity.open(Base64Url.decode(sealed), SEALED_KEY_LABEL)
                } catch (exception: CryptoException) {
                    if (exception.code == CryptoErrorCode.Tampered) {
                        throw CryptoException(CryptoErrorCode.WrongPassword, "this file is not addressed to this device", exception)
                    }
                    throw exception
                }
            }

            PayloadMode.Password ->
                throw CryptoException(CryptoErrorCode.UnknownKind, "the phone cannot open a password protected file")
        }
    }

    internal class CreatedKey(
        val contentKey: ByteArray,
        val keySalt: ByteArray? = null,
        val sealedKey: String? = null,
    )
}

/** One logical file handed to the container writer. */
class ContainerEntrySource(val name: String, private val open: () -> InputStream) {
    companion object {
        /**
         * The rules an item name has to follow: a plain relative name, sub folders separated by
         * a forward slash only. The reader applies the same rules to a foreign producer's names,
         * so nothing can ever be written outside the chosen folder.
         */
        fun isAcceptableName(name: String?): Boolean {
            if (name.isNullOrBlank()) return false
            if (name.contains('\\') || name.contains("..") || name.contains(':')) return false
            if (name.startsWith('/') || name.endsWith('/')) return false
            return name.split('/').all { segment ->
                segment.isNotBlank() &&
                    segment.none { it.code < 0x20 || it in "<>:\"|?*" } &&
                    segment.trimEnd('.', ' ') == segment
            }
        }

        fun ofBytes(name: String, bytes: ByteArray) = ContainerEntrySource(name) { bytes.inputStream() }

        fun ofFile(name: String, file: File) = ContainerEntrySource(name) { file.inputStream() }
    }

    init {
        if (!isAcceptableName(name)) {
            throw CryptoException(CryptoErrorCode.Corrupt, "a container item name must be a plain relative name")
        }
    }

    fun openRead(): InputStream = open()
}

/** Everything the writer needs to produce one container. */
class ContainerWriteRequest(
    val kind: ContainerKind,
    val producer: DeviceCertificate,
    val signer: DeviceIdentity,
    val key: ContainerKeySource,
    val entries: List<ContainerEntrySource>,
    val createdAt: Instant,
    val version: Int = ContainerManifest.CURRENT_VERSION,
    val stagingDirectory: File? = null,
)

/**
 * Produces the ZIP shaped containers — `manifest.json`, `payload.bin`, `signature.bin` — byte
 * compatible with `Wakeel.Crypto/Containers/ContainerWriter.cs`. The payload is staged through
 * a temporary file, so a packet is never held in memory twice.
 */
object ContainerWriter {
    private const val PAYLOAD_LABEL = "wakeel.payload"

    /**
     * Writes the container to a file. The bytes go to a neighbouring temporary name and are
     * moved into place only when the file is complete, so an interrupted write can never leave
     * a half written file that looks like a good one.
     */
    fun write(target: File, request: ContainerWriteRequest): ContainerManifest {
        target.parentFile?.mkdirs()
        val temporary = File(target.parentFile, target.name + ".part")
        try {
            val manifest = temporary.outputStream().buffered().use { write(it, request) }
            if (target.exists() && !target.delete()) {
                throw CryptoException(CryptoErrorCode.Corrupt, "the target file could not be replaced")
            }
            if (!temporary.renameTo(target)) {
                throw CryptoException(CryptoErrorCode.Corrupt, "the finished file could not be put in place")
            }
            return manifest
        } catch (exception: Throwable) {
            temporary.delete()
            throw exception
        }
    }

    fun write(output: OutputStream, request: ContainerWriteRequest): ContainerManifest {
        validate(request)

        val staging = File.createTempFile("wakeel-", ".stage", request.stagingDirectory)
        try {
            val entries = buildPayload(staging, request.entries)
            val created = request.key.createContentKey(request.kind)

            val zip = ZipOutputStream(output)
            val payloadDigest = MessageDigest.getInstance("SHA-256")

            zip.putNextEntry(ZipEntry(ContainerManifest.PAYLOAD_FILE_NAME))
            val hashing = DigestOutputStream(NonClosing(zip), payloadDigest)
            staging.inputStream().buffered().use { input ->
                Aead.encryptStream(
                    created.contentKey,
                    input,
                    hashing,
                    payloadAssociatedData(request.kind, request.version),
                )
            }
            hashing.flush()
            zip.closeEntry()
            val payloadHash = payloadDigest.digest()

            val manifest = ContainerManifest(
                type = request.kind,
                version = request.version,
                producer = request.producer,
                createdAt = truncateToMillis(request.createdAt),
                mode = request.key.mode,
                kdf = null,
                sealedKey = created.sealedKey,
                adminSealedKey = null,
                keySalt = created.keySalt,
                entries = entries,
            )
            val manifestBytes = CanonicalJson.toBytes(manifest.toJson())

            zip.putNextEntry(ZipEntry(ContainerManifest.FILE_NAME))
            zip.write(manifestBytes)
            zip.closeEntry()

            val signature = request.signer.sign(signingInput(Sha256.hash(manifestBytes), payloadHash))
            zip.putNextEntry(ZipEntry(ContainerManifest.SIGNATURE_FILE_NAME))
            zip.write(signature)
            zip.closeEntry()

            zip.finish()
            zip.flush()
            created.contentKey.fill(0)
            return manifest
        } finally {
            staging.delete()
        }
    }

    /**
     * The bytes an Ed25519 signature covers: a constant label, then the two hashes in order.
     * The label keeps a container signature from ever being read as a signature over one of the
     * other structures the same device key signs.
     */
    internal fun signingInput(manifestHash: ByteArray, payloadHash: ByteArray): ByteArray =
        DomainSeparation.wrap(DomainSeparation.CONTAINER, manifestHash + payloadHash)

    internal fun payloadAssociatedData(kind: ContainerKind, version: Int): ByteArray =
        "$PAYLOAD_LABEL|${kind.token}|v$version".toByteArray(Charsets.UTF_8)

    private fun truncateToMillis(value: Instant): Instant = Instant.ofEpochMilli(value.toEpochMilli())

    private fun buildPayload(staging: File, sources: List<ContainerEntrySource>): List<ContainerEntry> {
        val entries = ArrayList<ContainerEntry>(sources.size)
        val seen = HashSet<String>()
        ZipOutputStream(staging.outputStream().buffered()).use { zip ->
            for (source in sources) {
                if (!seen.add(source.name)) {
                    throw CryptoException(CryptoErrorCode.Corrupt, "two container items carry the same name")
                }
                zip.putNextEntry(ZipEntry(source.name))
                val digest = MessageDigest.getInstance("SHA-256")
                var size = 0L
                source.openRead().use { input ->
                    val buffer = ByteArray(81920)
                    while (true) {
                        val read = input.read(buffer)
                        if (read <= 0) break
                        digest.update(buffer, 0, read)
                        zip.write(buffer, 0, read)
                        size += read
                    }
                }
                zip.closeEntry()
                entries.add(ContainerEntry(source.name, size, Sha256.toHex(digest.digest())))
            }
        }
        entries.sortWith { left, right -> left.name.compareTo(right.name) }
        return entries
    }

    private fun validate(request: ContainerWriteRequest) {
        if (request.entries.isEmpty()) {
            throw CryptoException(CryptoErrorCode.Corrupt, "a container must carry at least one item")
        }
        if (request.version < 1) {
            throw CryptoException(CryptoErrorCode.UnknownKind, "the container format version is not usable")
        }
        if (request.producer.body.kind == DeviceKind.Org && request.kind != ContainerKind.Setup) {
            throw CryptoException(CryptoErrorCode.Corrupt, "only a setup file may be produced by the organisation")
        }
        val declared = Base64Url.tryDecode(request.producer.body.ed25519Pub)
        if (declared == null || !declared.contentEquals(request.signer.signingPublicKey)) {
            throw CryptoException(CryptoErrorCode.BadSignature, "the signing keys do not match the producer certificate")
        }
    }

    /** The zip stream must stay open after the AEAD stream finishes writing one entry. */
    private class NonClosing(private val inner: OutputStream) : OutputStream() {
        override fun write(byte: Int) = inner.write(byte)
        override fun write(buffer: ByteArray, offset: Int, length: Int) = inner.write(buffer, offset, length)
        override fun flush() = inner.flush()
        override fun close() { /* the caller closes the zip entry itself */ }
    }
}

/** What the reader is allowed to accept. */
class ContainerOpenOptions(
    val expectedKind: ContainerKind? = null,
    val orgSigningPublicKey: ByteArray? = null,
    val issuerCertificate: DeviceCertificate? = null,
    val revocations: RevocationList? = null,
    val now: Instant = Instant.now(),
    val maxFutureSkew: Duration = Duration.ofDays(1),
    val verifyCertificateChain: Boolean = true,
    val stagingDirectory: File? = null,
)

/**
 * Opens a container. The signature, the certificate chain, the kind and the creation instant
 * are all checked while opening, before a single payload byte is decrypted.
 */
class ContainerReader private constructor(
    private val zip: ZipFile,
    private val temporary: File?,
    val manifest: ContainerManifest,
    private val options: ContainerOpenOptions,
) : AutoCloseable {

    companion object {
        /** The most a manifest or a signature will ever be read: both are tiny by construction. */
        private const val MAX_METADATA_ENTRY_SIZE = 1024L * 1024L

        fun open(file: File, options: ContainerOpenOptions): ContainerReader = open(file, null, options)

        /**
         * Opens a container that arrives as a stream — the shape a folder chosen through the
         * system picker hands one over. The bytes are staged to a private temporary file, which
         * is removed when the reader closes.
         */
        fun open(stream: InputStream, options: ContainerOpenOptions): ContainerReader {
            val staged = File.createTempFile("wakeel-in-", ".container", options.stagingDirectory)
            try {
                staged.outputStream().buffered().use { output -> stream.copyTo(output) }
                return open(staged, staged, options)
            } catch (exception: Throwable) {
                staged.delete()
                throw exception
            }
        }

        private fun open(file: File, temporary: File?, options: ContainerOpenOptions): ContainerReader {
            val zip = try {
                ZipFile(file)
            } catch (exception: Exception) {
                temporary?.delete()
                throw CryptoException(CryptoErrorCode.Corrupt, "the file is not one of the product's own files", exception)
            }
            try {
                val manifestBytes = readAll(zip, ContainerManifest.FILE_NAME, MAX_METADATA_ENTRY_SIZE)
                val signature = readAll(zip, ContainerManifest.SIGNATURE_FILE_NAME, MAX_METADATA_ENTRY_SIZE)
                val manifest = ContainerManifest.fromJson(CanonicalJson.parse(manifestBytes))
                validateShape(manifest)

                val payloadHash = entryStream(zip, ContainerManifest.PAYLOAD_FILE_NAME).use { Sha256.hash(it) }
                val signingInput = ContainerWriter.signingInput(Sha256.hash(manifestBytes), payloadHash)
                val producerKey = Base64Url.tryDecode(manifest.producer.body.ed25519Pub)
                if (producerKey == null || !DeviceIdentity.verify(producerKey, signingInput, signature)) {
                    throw CryptoException(CryptoErrorCode.BadSignature, "the file signature does not match its contents")
                }

                if (options.verifyCertificateChain) {
                    val orgKey = options.orgSigningPublicKey
                    if (orgKey == null || orgKey.size != DeviceIdentity.PUBLIC_KEY_SIZE) {
                        throw CryptoException(
                            CryptoErrorCode.BadSignature,
                            "the organisation key is needed to check who produced this file",
                        )
                    }
                    CertificateChain.verify(
                        manifest.producer,
                        orgKey,
                        options.revocations,
                        options.now,
                        options.issuerCertificate,
                    )
                }

                if (manifest.producer.body.kind == DeviceKind.Org && manifest.type != ContainerKind.Setup) {
                    throw CryptoException(CryptoErrorCode.BadSignature, "only a setup file may be produced by the organisation")
                }
                if (options.expectedKind != null && manifest.type != options.expectedKind) {
                    throw CryptoException(CryptoErrorCode.UnknownKind, "this file is not of the kind the operation expects")
                }
                if (manifest.version > ContainerManifest.CURRENT_VERSION || manifest.version < 1) {
                    throw CryptoException(CryptoErrorCode.UnknownKind, "the file comes from a newer version of the product")
                }
                if (manifest.createdAt.isAfter(options.now.plus(options.maxFutureSkew))) {
                    throw CryptoException(CryptoErrorCode.Expired, "the file is dated in the future")
                }

                return ContainerReader(zip, temporary, manifest, options)
            } catch (exception: Throwable) {
                zip.close()
                temporary?.delete()
                throw exception
            }
        }

        private fun entryStream(zip: ZipFile, name: String): InputStream {
            val entry = zip.getEntry(name)
                ?: throw CryptoException(CryptoErrorCode.Corrupt, "the file is missing one of its required parts")
            return zip.getInputStream(entry)
        }

        private fun readAll(zip: ZipFile, name: String, limit: Long): ByteArray {
            entryStream(zip, name).use { stream ->
                val buffer = java.io.ByteArrayOutputStream()
                val chunk = ByteArray(81920)
                var total = 0L
                while (true) {
                    val read = stream.read(chunk)
                    if (read <= 0) break
                    total += read
                    if (total > limit) {
                        throw CryptoException(CryptoErrorCode.Corrupt, "a required part is larger than the product writes")
                    }
                    buffer.write(chunk, 0, read)
                }
                return buffer.toByteArray()
            }
        }

        private fun validateShape(manifest: ContainerManifest) {
            if (manifest.producer.signature.isEmpty()) {
                throw CryptoException(CryptoErrorCode.Corrupt, "the file does not name the device that produced it")
            }
            if (manifest.entries.isEmpty()) {
                throw CryptoException(CryptoErrorCode.Corrupt, "the file lists no contents")
            }
            val seen = HashSet<String>()
            for (entry in manifest.entries) {
                if (entry.sha256.isEmpty() || entry.size < 0) {
                    throw CryptoException(CryptoErrorCode.Corrupt, "the file describes one of its contents incorrectly")
                }
                if (!ContainerEntrySource.isAcceptableName(entry.name)) {
                    throw CryptoException(CryptoErrorCode.Corrupt, "the file names a content in a way that is not allowed")
                }
                if (!seen.add(entry.name.lowercase())) {
                    throw CryptoException(CryptoErrorCode.Corrupt, "the file lists the same item twice")
                }
            }
        }
    }

    /** Decrypts and returns every item, checking each recorded size and hash. */
    fun readEntries(key: ContainerKeySource): Map<String, ByteArray> {
        val result = LinkedHashMap<String, ByteArray>()
        val plain = decryptPayload(key)
        try {
            ZipFile(plain).use { inner ->
                for (expected in manifest.entries) {
                    result[expected.name] = readOne(inner, expected)
                }
            }
        } finally {
            plain.delete()
        }
        return result
    }

    /** Decrypts the payload once and returns a single item. */
    fun readEntry(name: String, key: ContainerKeySource): ByteArray {
        val expected = manifest.entries.firstOrNull { it.name == name }
            ?: throw CryptoException(CryptoErrorCode.Corrupt, "the file does not contain that item")
        val plain = decryptPayload(key)
        try {
            ZipFile(plain).use { inner -> return readOne(inner, expected) }
        } finally {
            plain.delete()
        }
    }

    override fun close() {
        zip.close()
        temporary?.delete()
    }

    private fun readOne(inner: ZipFile, expected: ContainerEntry): ByteArray {
        val entry = inner.getEntry(expected.name)
            ?: throw CryptoException(CryptoErrorCode.Tampered, "the file is missing one of the contents it lists")
        val buffer = java.io.ByteArrayOutputStream()
        val digest = MessageDigest.getInstance("SHA-256")
        var total = 0L
        inner.getInputStream(entry).use { source ->
            val chunk = ByteArray(81920)
            while (true) {
                val read = source.read(chunk)
                if (read <= 0) break
                total += read
                // The declared size is checked while the bytes are still being decompressed, so
                // an item that lies about its own size cannot exhaust memory before the check.
                if (total > expected.size) {
                    throw CryptoException(CryptoErrorCode.Tampered, "one of the contents is larger than the file declares")
                }
                digest.update(chunk, 0, read)
                buffer.write(chunk, 0, read)
            }
        }
        if (total != expected.size || Sha256.toHex(digest.digest()) != expected.sha256.lowercase()) {
            throw CryptoException(CryptoErrorCode.Tampered, "one of the contents does not match what the file declares")
        }
        return buffer.toByteArray()
    }

    private fun decryptPayload(key: ContainerKeySource): File {
        val contentKey = key.openContentKey(manifest)
        val output = File.createTempFile("wakeel-plain-", ".stage", options.stagingDirectory)
        try {
            val associated = ContainerWriter.payloadAssociatedData(manifest.type, manifest.version)
            entryStream(zip, ContainerManifest.PAYLOAD_FILE_NAME).use { encrypted ->
                output.outputStream().buffered().use { plain ->
                    Aead.decryptStream(contentKey, encrypted, plain, associated)
                }
            }
            return output
        } catch (exception: Throwable) {
            output.delete()
            throw exception
        } finally {
            contentKey.fill(0)
        }
    }
}
