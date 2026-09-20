package ps.wakeel.phone.core

import androidx.test.core.app.ApplicationProvider
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import ps.wakeel.phone.core.certificates.DeviceCertificate
import ps.wakeel.phone.core.containers.ContainerEntrySource
import ps.wakeel.phone.core.containers.ContainerKeySource
import ps.wakeel.phone.core.containers.ContainerKind
import ps.wakeel.phone.core.containers.ContainerOpenOptions
import ps.wakeel.phone.core.containers.ContainerReader
import ps.wakeel.phone.core.containers.ContainerWriteRequest
import ps.wakeel.phone.core.containers.ContainerWriter
import ps.wakeel.phone.core.crypto.CryptoErrorCode
import ps.wakeel.phone.core.crypto.InMemoryKeyVault
import ps.wakeel.phone.core.db.OutboxRow
import ps.wakeel.phone.core.db.WakeelPhoneDatabase
import ps.wakeel.phone.core.files.PhoneFileStore
import ps.wakeel.phone.core.folder.FileWakeelFolder
import ps.wakeel.phone.core.folder.WakeelFolder
import ps.wakeel.phone.core.json.CanonicalJson
import ps.wakeel.phone.core.json.JsonValue
import ps.wakeel.phone.core.json.jsonObject
import ps.wakeel.phone.core.json.jsonString
import ps.wakeel.phone.core.sync.IncomingPackage
import ps.wakeel.phone.core.sync.OutgoingPackage
import ps.wakeel.phone.core.sync.PhonePackageEntries
import ps.wakeel.phone.core.sync.PhonePackageNames
import ps.wakeel.phone.core.sync.PhoneSyncEngine
import ps.wakeel.phone.core.sync.SyncAcknowledgement
import java.io.File
import java.time.Instant

/**
 * The exchange itself: packets applied in order and each one whole, the receipt the phone
 * leaves, an interruption that loses nothing, and the outbox packed into packets no larger than
 * the agreement allows.
 */
@RunWith(RobolectricTestRunner::class)
@Config(sdk = [35])
class PhoneSyncEngineTest {

    @get:Rule
    val temporary = TemporaryFolder()

    private lateinit var database: WakeelPhoneDatabase
    private lateinit var folder: FileWakeelFolder
    private lateinit var folderDirectory: File
    private lateinit var fileStore: PhoneFileStore
    private lateinit var engine: PhoneSyncEngine

    private val pcCertificate = DeviceCertificate.fromJson(Vectors.jsonValue("certificates/pc.json"))
    private val phoneCertificate = DeviceCertificate.fromJson(Vectors.jsonValue("certificates/phone.json"))
    private val now: Instant get() = Vectors.now

    @Before
    fun setUp() {
        database = WakeelPhoneDatabase.inMemory(ApplicationProvider.getApplicationContext())
        folderDirectory = temporary.newFolder("Wakeel")
        folder = FileWakeelFolder(folderDirectory)
        val vault = InMemoryKeyVault()
        fileStore = PhoneFileStore(temporary.newFolder("files"), vault, PhoneFileStore.newWrappedKey(vault))
        engine = PhoneSyncEngine(
            database = database,
            folder = folder,
            fileStore = fileStore,
            identity = Vectors.phone,
            phoneCertificate = phoneCertificate,
            issuerCertificate = pcCertificate,
            orgSigningPublicKey = Vectors.org.signingPublicKey,
            sessionKey = Vectors.sessionKey,
            stagingDirectory = temporary.newFolder("stage"),
        )
    }

    @After
    fun tearDown() {
        database.close()
    }

    // ---------------------------------------------------------------- incoming

    @Test
    fun packetsAreAppliedInOrderAndTheStateFollows() = runBlocking {
        writeToPhone(3, taskPackage(3, "T-3", "ثالثة"))
        writeToPhone(1, taskPackage(1, "T-1", "أولى"))
        writeToPhone(2, taskPackage(2, "T-2", "ثانية"))

        assertEquals(listOf(1, 2, 3), engine.discoverIncoming())

        val result = engine.applyIncoming(now)
        assertEquals(listOf(1, 2, 3), result.applied)
        assertEquals(3, result.rows)
        assertEquals(listOf("T-1", "T-2", "T-3"), database.tasks().all().map { it.id }.sorted())
        assertEquals(3, database.syncState().get()!!.lastAppliedSeq)
    }

    @Test
    fun aPacketAlreadyAppliedIsNotAppliedTwice() = runBlocking {
        writeToPhone(1, taskPackage(1, "T-1", "أولى"))
        assertEquals(listOf(1), engine.applyIncoming(now).applied)
        assertEquals(emptyList<Int>(), engine.applyIncoming(now).applied)
        assertEquals(1, database.tasks().all().size)
    }

    @Test
    fun aMissingPacketStopsTheRunRatherThanSkippingIt() = runBlocking {
        writeToPhone(1, taskPackage(1, "T-1", "أولى"))
        writeToPhone(3, taskPackage(3, "T-3", "ثالثة"))

        val result = engine.applyIncoming(now)
        assertEquals(listOf(1), result.applied)
        assertEquals(listOf("T-1"), database.tasks().all().map { it.id })
        assertEquals(1, database.syncState().get()!!.lastAppliedSeq)

        // Once the missing packet arrives, both it and the one behind it go in.
        writeToPhone(2, taskPackage(2, "T-2", "ثانية"))
        assertEquals(listOf(2, 3), engine.applyIncoming(now).applied)
    }

    @Test
    fun aPacketIsAppliedWholeOrNotAtAll() = runBlocking {
        // The second row carries no title, which the mapping refuses; the first row must not
        // survive the refusal, or the phone would be left inside a half applied packet.
        val broken = IncomingPackage(
            seq = 1,
            createdAt = now,
            tables = mapOf(
                "tasks" to listOf(
                    taskRow("T-GOOD", "سليمة"),
                    jsonObject(
                        "id" to jsonString("T-BROKEN"),
                        "updated_at" to jsonString("2026-09-17T09:00:00.000Z"),
                    ),
                ),
            ),
        )
        writeToPhone(1, broken)

        val result = engine.applyIncoming(now)
        assertEquals(emptyList<Int>(), result.applied)
        assertEquals(CryptoErrorCode.Corrupt, result.refused[1])
        assertTrue("nothing from a refused packet may stay", database.tasks().all().isEmpty())
        assertEquals(0, database.syncState().get()?.lastAppliedSeq ?: 0)
    }

    @Test
    fun aPacketNumberedDifferentlyInsideIsRefused() = runBlocking {
        writeToPhone(1, taskPackage(2, "T-1", "أولى"))
        val result = engine.applyIncoming(now)
        assertEquals(emptyList<Int>(), result.applied)
        assertEquals(CryptoErrorCode.Corrupt, result.refused[1])
    }

    @Test
    fun aPacketWithAStrangersSignatureIsRefused() = runBlocking {
        val body = CanonicalJson.toBytes(taskPackage(1, "T-1", "أولى").toJson())
        folder.write(PhonePackageNames.toPhone(1)) { output ->
            ContainerWriter.write(
                output,
                ContainerWriteRequest(
                    kind = ContainerKind.Phone,
                    // The phone's own certificate cannot stand in for the computer's: the chain
                    // the reader builds expects the packet's producer to be the paired computer.
                    producer = phoneCertificate,
                    signer = Vectors.phone,
                    key = ContainerKeySource.sessionKey(Vectors.sessionKey),
                    entries = listOf(ContainerEntrySource.ofBytes(PhonePackageEntries.BODY, body)),
                    createdAt = now,
                ),
            )
        }
        val result = engine.applyIncoming(now)
        assertEquals(emptyList<Int>(), result.applied)
        assertNotNull(result.refused[1])
    }

    @Test
    fun aFileStillBeingWrittenIsInvisibleUntilItIsWhole() = runBlocking {
        // The computer is halfway through packet one: only the partial name exists.
        File(folderDirectory, PhonePackageNames.toPhone(1) + WakeelFolder.PARTIAL_SUFFIX)
            .writeBytes(ByteArray(4096))

        assertEquals(emptyList<Int>(), engine.discoverIncoming())
        assertEquals(emptyList<Int>(), engine.applyIncoming(now).applied)

        // It finishes; now the same packet is seen and applied, nothing having been lost.
        writeToPhone(1, taskPackage(1, "T-1", "أولى"))
        assertEquals(listOf(1), engine.applyIncoming(now).applied)
    }

    @Test
    fun aTruncatedPacketIsRefusedAndTheRunStops() = runBlocking {
        writeToPhone(1, taskPackage(1, "T-1", "أولى"))
        writeToPhone(2, taskPackage(2, "T-2", "ثانية"))

        val cut = File(folderDirectory, PhonePackageNames.toPhone(1))
        val bytes = cut.readBytes()
        cut.writeBytes(bytes.copyOfRange(0, bytes.size / 2))

        val result = engine.applyIncoming(now)
        assertEquals(emptyList<Int>(), result.applied)
        assertNotNull(result.refused[1])
        assertTrue(database.tasks().all().isEmpty())
    }

    @Test
    fun theFilesInAPacketLandInTheApplicationsOwnStorage() = runBlocking {
        val contents = "ملف مثبّت".toByteArray(Charsets.UTF_8)
        val body = IncomingPackage(
            seq = 1,
            createdAt = now,
            tables = mapOf(
                "pinned_files" to listOf(
                    jsonObject(
                        "document_id" to jsonString("D-1"),
                        "name" to jsonString("عقد.pdf"),
                        "file_name" to jsonString("pinned-1.bin"),
                        "size" to JsonValue.Num(contents.size.toString()),
                        "pinned_at" to jsonString("2026-09-17T09:00:00.000Z"),
                    ),
                ),
            ),
        )
        writeToPhone(1, body, mapOf("pinned-1.bin" to contents))

        val result = engine.applyIncoming(now)
        assertEquals(listOf(1), result.applied)
        assertEquals(1, result.files)
        assertTrue(fileStore.exists("pinned-1.bin"))
        assertEquals(String(contents, Charsets.UTF_8), String(fileStore.read("pinned-1.bin"), Charsets.UTF_8))
        assertEquals(1, database.pinnedFiles().all().size)
    }

    @Test
    fun theReceiptNamesTheLastPacketAppliedInFull() = runBlocking {
        writeToPhone(1, taskPackage(1, "T-1", "أولى"))
        writeToPhone(2, taskPackage(2, "T-2", "ثانية"))
        engine.applyIncoming(now)

        assertTrue(folder.exists(PhonePackageNames.ACK))
        val acknowledgement = readAcknowledgement()
        assertEquals(2, acknowledgement.lastAppliedSeq)
    }

    // ---------------------------------------------------------------- outgoing

    @Test
    fun theOutboxLeavesInOnePacketAndIsThenEmpty() = runBlocking {
        enqueue("O-1", "expense", """{"amount":12000,"purpose":"وقود"}""")
        enqueue("O-2", "note", """{"text":"ملاحظة"}""")

        val result = engine.writeOutgoing(now)
        assertEquals(listOf(PhonePackageNames.fromPhone(1)), result.written)
        assertEquals(2, result.items)
        assertEquals(0, database.outbox().pendingCount())
        assertEquals(1, database.syncState().get()!!.lastSentSeq)

        val packet = readOutgoing(1)
        assertEquals(1, packet.seq)
        assertEquals(listOf("O-1", "O-2"), packet.items.map { it.id })
        assertEquals("expense", packet.items.first().kind)
    }

    @Test
    fun nothingIsWrittenWhenNothingIsOwed() = runBlocking {
        val result = engine.writeOutgoing(now)
        assertTrue(result.written.isEmpty())
        assertFalse(folder.exists(PhonePackageNames.fromPhone(1)))
    }

    @Test
    fun aFileTravelsWithItsItem() = runBlocking {
        val receipt = "صورة إيصال".toByteArray(Charsets.UTF_8)
        fileStore.write("receipt-1.jpg", receipt)
        enqueue("O-1", "expense", """{"amount":12000}""", "receipt-1.jpg", receipt.size.toLong())

        engine.writeOutgoing(now)

        folder.openRead(PhonePackageNames.fromPhone(1)).use { stream ->
            ContainerReader.open(stream, openOptions()).use { reader ->
                val entries = reader.readEntries(ContainerKeySource.sessionKey(Vectors.sessionKey))
                assertEquals(
                    String(receipt, Charsets.UTF_8),
                    String(entries.getValue(PhonePackageEntries.FILE_PREFIX + "receipt-1.jpg"), Charsets.UTF_8),
                )
            }
        }
    }

    @Test
    fun moreThanTwentyMegabytesIsSplitAcrossNumberedPackets() = runBlocking {
        // Three attachments of eight megabytes cannot share one packet, because the agreement
        // caps a packet at twenty; the third one has to open a second packet.
        repeat(3) { index ->
            val name = "big-$index.bin"
            val payload = ByteArray(8 * 1024 * 1024) { (index + it).toByte() }
            fileStore.write(name, payload)
            enqueue("O-$index", "capture", """{"page":$index}""", name, payload.size.toLong())
        }

        val result = engine.writeOutgoing(now)
        assertEquals(3, result.items)
        assertEquals(
            listOf(PhonePackageNames.fromPhone(1), PhonePackageNames.fromPhone(2)),
            result.written,
        )

        folder.list().filter { PhonePackageNames.fromPhoneSequence(it.name) != null }.forEach { file ->
            assertTrue(
                "a packet may never exceed twenty megabytes, ${file.name} is ${file.size}",
                file.size <= PhoneSyncEngine.MAX_PACKAGE_BYTES,
            )
        }
        assertEquals(0, database.outbox().pendingCount())
        assertEquals(2, database.syncState().get()!!.lastSentSeq)
    }

    @Test
    fun aRunTakesWhatArrivedAndLeavesWhatIsOwed() = runBlocking {
        writeToPhone(1, taskPackage(1, "T-1", "أولى"))
        enqueue("O-1", "note", """{"text":"ملاحظة"}""")

        val result = engine.run(now)
        assertEquals(listOf(1), result.incoming.applied)
        assertEquals(1, result.outgoing.items)
        assertEquals(1, readAcknowledgement().lastAppliedSeq)
    }

    // ---------------------------------------------------------------- helpers

    private suspend fun enqueue(
        id: String,
        kind: String,
        payload: String,
        fileName: String? = null,
        fileSize: Long = 0,
    ) {
        database.outbox().add(
            OutboxRow(
                id = id,
                kind = kind,
                payload = payload,
                fileName = fileName,
                fileSize = fileSize,
                createdAt = CanonicalJson.instant(now),
            ),
        )
    }

    private fun taskRow(id: String, title: String): JsonValue.Obj = jsonObject(
        "id" to jsonString(id),
        "title" to jsonString(title),
        "status" to jsonString("open"),
        "updated_at" to jsonString("2026-09-17T09:00:00.000Z"),
    )

    private fun taskPackage(seq: Int, id: String, title: String) = IncomingPackage(
        seq = seq,
        createdAt = now,
        tables = mapOf("tasks" to listOf(taskRow(id, title))),
    )

    private fun writeToPhone(
        seq: Int,
        body: IncomingPackage,
        files: Map<String, ByteArray> = emptyMap(),
    ) {
        val entries = ArrayList<ContainerEntrySource>()
        entries.add(ContainerEntrySource.ofBytes(PhonePackageEntries.BODY, CanonicalJson.toBytes(body.toJson())))
        files.forEach { (name, bytes) ->
            entries.add(ContainerEntrySource.ofBytes(PhonePackageEntries.FILE_PREFIX + name, bytes))
        }
        folder.write(PhonePackageNames.toPhone(seq)) { output ->
            ContainerWriter.write(
                output,
                ContainerWriteRequest(
                    kind = ContainerKind.Phone,
                    producer = pcCertificate,
                    signer = Vectors.pc,
                    key = ContainerKeySource.sessionKey(Vectors.sessionKey),
                    entries = entries,
                    createdAt = now,
                ),
            )
        }
    }

    private fun openOptions() = ContainerOpenOptions(
        expectedKind = ContainerKind.Phone,
        orgSigningPublicKey = Vectors.org.signingPublicKey,
        issuerCertificate = pcCertificate,
        now = now,
    )

    private fun readOutgoing(seq: Int): OutgoingPackage =
        folder.openRead(PhonePackageNames.fromPhone(seq)).use { stream ->
            ContainerReader.open(stream, openOptions()).use { reader ->
                val entries = reader.readEntries(ContainerKeySource.sessionKey(Vectors.sessionKey))
                OutgoingPackage.fromJson(CanonicalJson.parse(entries.getValue(PhonePackageEntries.BODY)))
            }
        }

    private fun readAcknowledgement(): SyncAcknowledgement =
        folder.openRead(PhonePackageNames.ACK).use { stream ->
            ContainerReader.open(stream, openOptions()).use { reader ->
                val entries = reader.readEntries(ContainerKeySource.sessionKey(Vectors.sessionKey))
                SyncAcknowledgement.fromJson(CanonicalJson.parse(entries.getValue(PhonePackageEntries.ACK_BODY)))
            }
        }
}

