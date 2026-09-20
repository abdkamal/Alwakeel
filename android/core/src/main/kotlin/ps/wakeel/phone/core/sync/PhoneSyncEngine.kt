package ps.wakeel.phone.core.sync

import androidx.room.withTransaction
import ps.wakeel.phone.core.certificates.DeviceCertificate
import ps.wakeel.phone.core.certificates.RevocationList
import ps.wakeel.phone.core.containers.ContainerEntrySource
import ps.wakeel.phone.core.containers.ContainerKeySource
import ps.wakeel.phone.core.containers.ContainerKind
import ps.wakeel.phone.core.containers.ContainerOpenOptions
import ps.wakeel.phone.core.containers.ContainerReader
import ps.wakeel.phone.core.containers.ContainerWriteRequest
import ps.wakeel.phone.core.containers.ContainerWriter
import ps.wakeel.phone.core.crypto.CryptoErrorCode
import ps.wakeel.phone.core.crypto.CryptoException
import ps.wakeel.phone.core.crypto.DeviceIdentity
import ps.wakeel.phone.core.db.SyncStateRow
import ps.wakeel.phone.core.db.WakeelPhoneDatabase
import ps.wakeel.phone.core.files.PhoneFileStore
import ps.wakeel.phone.core.folder.WakeelFolder
import ps.wakeel.phone.core.json.CanonicalJson
import ps.wakeel.phone.core.json.JsonValue
import java.io.File
import java.time.Instant

/** What one exchange did, for the screens that report it in Arabic. */
data class SyncApplyResult(
    val applied: List<Int> = emptyList(),
    val rows: Int = 0,
    val files: Int = 0,
    val refused: Map<Int, CryptoErrorCode> = emptyMap(),
)

data class SyncPushResult(
    val written: List<String> = emptyList(),
    val items: Int = 0,
)

data class SyncRunResult(val incoming: SyncApplyResult, val outgoing: SyncPushResult)

/**
 * The exchange with the office computer, over nothing but files in one folder.
 *
 * Packets are applied strictly in order and each one in a single database transaction, so an
 * interruption — the cable pulled out halfway, the application stopped — leaves the phone
 * either fully before a packet or fully after it, never inside one (item 35 of the agreement).
 * A file still being written carries the partial suffix and is simply not seen.
 */
class PhoneSyncEngine(
    private val database: WakeelPhoneDatabase,
    private val folder: WakeelFolder,
    private val fileStore: PhoneFileStore,
    private val identity: DeviceIdentity,
    private val phoneCertificate: DeviceCertificate,
    private val issuerCertificate: DeviceCertificate,
    private val orgSigningPublicKey: ByteArray,
    private val sessionKey: ByteArray,
    private val revocations: RevocationList? = null,
    private val stagingDirectory: File? = null,
) {
    companion object {
        /** No packet is ever larger than this; the agreement fixes it at twenty megabytes. */
        const val MAX_PACKAGE_BYTES: Long = 20L * 1024L * 1024L

        /**
         * How much room the packet's own wrapping is given inside that limit: the manifest, the
         * certificates, the signature and the frame headers of the encrypted payload.
         */
        private const val OVERHEAD_BYTES: Long = 256L * 1024L
    }

    private val budget: Long = MAX_PACKAGE_BYTES - OVERHEAD_BYTES

    /** One full exchange: take what the computer left, then leave what the phone owes. */
    suspend fun run(now: Instant): SyncRunResult {
        val incoming = applyIncoming(now)
        val outgoing = writeOutgoing(now)
        return SyncRunResult(incoming, outgoing)
    }

    /** What is waiting in the folder, by packet number, lowest first. */
    fun discoverIncoming(): List<Int> =
        folder.list().mapNotNull { PhonePackageNames.toPhoneSequence(it.name) }.distinct().sorted()

    /**
     * Applies every packet that follows the last one applied, in order. A gap stops the run:
     * the phone waits for the missing packet rather than applying a later one out of order.
     */
    suspend fun applyIncoming(now: Instant): SyncApplyResult {
        var state = state()
        var last = state.lastAppliedSeq
        val applied = ArrayList<Int>()
        val refused = LinkedHashMap<Int, CryptoErrorCode>()
        var rows = 0
        var files = 0

        for (seq in discoverIncoming()) {
            if (seq <= last) continue
            if (seq != last + 1) break

            val name = PhonePackageNames.toPhone(seq)
            val opened = try {
                val package_ = readIncoming(name, now)
                if (package_.body.seq != seq) {
                    throw CryptoException(CryptoErrorCode.Corrupt, "the packet is numbered differently inside")
                }
                // The rows are turned into records before a transaction is opened, so a packet
                // the phone cannot read costs nothing and leaves nothing behind.
                Prepared(package_, prepare(package_.body))
            } catch (exception: CryptoException) {
                // A packet that does not hold up is left where it is, untouched, and the run
                // stops: applying the next one would put the phone out of order.
                refused[seq] = exception.code
                break
            }

            database.withTransaction {
                opened.rows.apply(database)
                opened.source.files.forEach { (entryName, bytes) ->
                    fileStore.write(entryName.removePrefix(PhonePackageEntries.FILE_PREFIX), bytes)
                }
                database.syncState().put(
                    state.copy(lastAppliedSeq = seq, lastSyncAt = CanonicalJson.instant(now)),
                )
            }

            state = state.copy(lastAppliedSeq = seq, lastSyncAt = CanonicalJson.instant(now))
            rows += opened.source.body.rowCount
            files += opened.source.files.size
            applied.add(seq)
            last = seq
        }

        if (applied.isNotEmpty() || !folder.exists(PhonePackageNames.ACK)) {
            writeAcknowledgement(last, now)
        }

        return SyncApplyResult(applied, rows, files, refused)
    }

    /**
     * Packs everything still waiting in the outbox into numbered packets of at most twenty
     * megabytes, writes them, and marks what left in the same transaction that raises the
     * packet number — so an item can never be sent twice or lost between the two.
     */
    suspend fun writeOutgoing(now: Instant): SyncPushResult {
        val pending = database.outbox().pending()
        if (pending.isEmpty()) return SyncPushResult()

        var state = state()
        val written = ArrayList<String>()
        var count = 0

        var batch = ArrayList<ps.wakeel.phone.core.db.OutboxRow>()
        var batchBytes = 0L

        suspend fun flush() {
            if (batch.isEmpty()) return
            val seq = state.lastSentSeq + 1
            val name = PhonePackageNames.fromPhone(seq)
            val items = ArrayList<OutgoingItem>(batch.size)
            val entries = ArrayList<ContainerEntrySource>()

            batch.forEach { row ->
                val entryName = row.fileName?.let { PhonePackageEntries.FILE_PREFIX + it }
                items.add(
                    OutgoingItem(
                        id = row.id,
                        kind = row.kind,
                        createdAt = CanonicalJson.parseInstant(row.createdAt),
                        payload = CanonicalJson.parse(row.payload),
                        file = entryName,
                    ),
                )
                if (entryName != null && row.fileName != null) {
                    entries.add(ContainerEntrySource.ofBytes(entryName, fileStore.read(row.fileName)))
                }
            }

            val body = OutgoingPackage(seq, now, items)
            entries.add(
                0,
                ContainerEntrySource.ofBytes(PhonePackageEntries.BODY, CanonicalJson.toBytes(body.toJson())),
            )

            folder.write(name) { output ->
                ContainerWriter.write(output, request(entries, now))
            }

            val ids = batch.map { it.id }
            database.withTransaction {
                database.outbox().markSent(ids, seq)
                database.syncState().put(state.copy(lastSentSeq = seq, lastSyncAt = CanonicalJson.instant(now)))
            }

            state = state.copy(lastSentSeq = seq, lastSyncAt = CanonicalJson.instant(now))
            written.add(name)
            count += batch.size
            batch = ArrayList()
            batchBytes = 0
        }

        for (row in pending) {
            val size = row.payload.toByteArray(Charsets.UTF_8).size.toLong() + row.fileSize
            if (size > budget && batch.isEmpty()) {
                // One item larger than a whole packet cannot be split; it travels alone and the
                // far side refuses it if it really does not fit, rather than blocking the queue.
                batch.add(row)
                flush()
                continue
            }
            if (batch.isNotEmpty() && batchBytes + size > budget) flush()
            batch.add(row)
            batchBytes += size
        }
        flush()

        return SyncPushResult(written, count)
    }

    /** Leaves the receipt naming the last packet the phone applied in full. */
    suspend fun writeAcknowledgement(lastAppliedSeq: Int, now: Instant) {
        val body = SyncAcknowledgement(lastAppliedSeq, now)
        val entries = listOf(
            ContainerEntrySource.ofBytes(PhonePackageEntries.ACK_BODY, CanonicalJson.toBytes(body.toJson())),
        )
        folder.write(PhonePackageNames.ACK) { output ->
            ContainerWriter.write(output, request(entries, now))
        }
    }

    private suspend fun state(): SyncStateRow =
        database.syncState().get() ?: SyncStateRow().also { database.syncState().put(it) }

    private fun request(entries: List<ContainerEntrySource>, now: Instant) = ContainerWriteRequest(
        kind = ContainerKind.Phone,
        producer = phoneCertificate,
        signer = identity,
        key = ContainerKeySource.sessionKey(sessionKey),
        entries = entries,
        createdAt = now,
        stagingDirectory = stagingDirectory,
    )

    private class OpenedPackage(val body: IncomingPackage, val files: Map<String, ByteArray>)

    private class Prepared(val source: OpenedPackage, val rows: PreparedRows)

    private fun readIncoming(name: String, now: Instant): OpenedPackage {
        val options = ContainerOpenOptions(
            expectedKind = ContainerKind.Phone,
            orgSigningPublicKey = orgSigningPublicKey,
            issuerCertificate = issuerCertificate,
            revocations = revocations,
            now = now,
            stagingDirectory = stagingDirectory,
        )
        folder.openRead(name).use { stream ->
            ContainerReader.open(stream, options).use { reader ->
                // The chain proves the producer belongs to this organisation; this proves it is
                // the very computer this phone is paired with, and not another device of the
                // same office that happened to reach the folder.
                if (reader.manifest.producer.body.deviceId != issuerCertificate.body.deviceId) {
                    throw CryptoException(
                        CryptoErrorCode.BadSignature,
                        "the packet was not produced by the paired computer",
                    )
                }
                val entries = reader.readEntries(ContainerKeySource.sessionKey(sessionKey))
                val bodyBytes = entries[PhonePackageEntries.BODY]
                    ?: throw CryptoException(CryptoErrorCode.Corrupt, "the packet carries no records")
                val body = IncomingPackage.fromJson(CanonicalJson.parse(bodyBytes))
                val files = entries.filterKeys { it.startsWith(PhonePackageEntries.FILE_PREFIX) }
                return OpenedPackage(body, files)
            }
        }
    }

    /**
     * Turns every row of a packet into a record, refusing the whole packet if one row cannot be
     * read. Nothing touches the database until this has succeeded for all of them.
     */
    private fun prepare(body: IncomingPackage): PreparedRows {
        val prepared = PreparedRows()
        body.tables.forEach { (table, rows) ->
            if (rows.isEmpty()) return@forEach
            when (table) {
                "correspondence_summary" -> prepared.correspondence = rows.map(RowMapping::correspondence)
                "tasks" -> prepared.tasks = rows.map(RowMapping::task)
                "meetings" -> prepared.meetings = rows.map(RowMapping::meeting)
                "appointments" -> prepared.appointments = rows.map(RowMapping::appointment)
                "decisions" -> prepared.decisions = rows.map(RowMapping::decision)
                "commitments" -> prepared.commitments = rows.map(RowMapping::commitment)
                "contacts" -> prepared.contacts = rows.map(RowMapping::contact)
                "notifications" -> prepared.notifications = rows.map(RowMapping::notification)
                "phone_expenses" -> prepared.phoneExpenses = rows.map(RowMapping::phoneExpense)
                "notes" -> prepared.notes = rows.map(RowMapping::note)
                "pinned_files" -> prepared.pinnedFiles = rows.map(RowMapping::pinnedFile)
                "settings" -> prepared.settings = rows.map(RowMapping::setting)
                else -> throw CryptoException(
                    CryptoErrorCode.UnknownKind,
                    "the packet carries records this version does not know",
                )
            }
        }
        return prepared
    }
}

/** The records of one packet, ready to be written in a single transaction. */
internal class PreparedRows {
    var correspondence: List<ps.wakeel.phone.core.db.CorrespondenceSummaryRow> = emptyList()
    var tasks: List<ps.wakeel.phone.core.db.TaskRow> = emptyList()
    var meetings: List<ps.wakeel.phone.core.db.MeetingRow> = emptyList()
    var appointments: List<ps.wakeel.phone.core.db.AppointmentRow> = emptyList()
    var decisions: List<ps.wakeel.phone.core.db.DecisionRow> = emptyList()
    var commitments: List<ps.wakeel.phone.core.db.CommitmentRow> = emptyList()
    var contacts: List<ps.wakeel.phone.core.db.ContactRow> = emptyList()
    var notifications: List<ps.wakeel.phone.core.db.NotificationRow> = emptyList()
    var phoneExpenses: List<ps.wakeel.phone.core.db.PhoneExpenseRow> = emptyList()
    var notes: List<ps.wakeel.phone.core.db.NoteRow> = emptyList()
    var pinnedFiles: List<ps.wakeel.phone.core.db.PinnedFileRow> = emptyList()
    var settings: List<ps.wakeel.phone.core.db.SettingRow> = emptyList()

    suspend fun apply(database: WakeelPhoneDatabase) {
        if (contacts.isNotEmpty()) database.contacts().upsert(contacts)
        if (correspondence.isNotEmpty()) database.correspondence().upsert(correspondence)
        if (tasks.isNotEmpty()) database.tasks().upsert(tasks)
        if (meetings.isNotEmpty()) database.meetings().upsert(meetings)
        if (appointments.isNotEmpty()) database.appointments().upsert(appointments)
        if (decisions.isNotEmpty()) database.decisions().upsert(decisions)
        if (commitments.isNotEmpty()) database.commitments().upsert(commitments)
        if (notifications.isNotEmpty()) database.notifications().upsert(notifications)
        if (phoneExpenses.isNotEmpty()) database.phoneExpenses().upsert(phoneExpenses)
        if (notes.isNotEmpty()) database.notes().upsert(notes)
        if (pinnedFiles.isNotEmpty()) database.pinnedFiles().upsert(pinnedFiles)
        if (settings.isNotEmpty()) database.settings().upsert(settings)
    }
}

/** Puts one item in the outbox; every screen that captures something goes through here. */
suspend fun WakeelPhoneDatabase.enqueue(
    id: String,
    kind: String,
    payload: JsonValue,
    createdAt: Instant,
    fileName: String? = null,
    fileSize: Long = 0,
) {
    outbox().add(
        ps.wakeel.phone.core.db.OutboxRow(
            id = id,
            kind = kind,
            payload = CanonicalJson.toText(payload),
            fileName = fileName,
            fileSize = fileSize,
            createdAt = CanonicalJson.instant(createdAt),
        ),
    )
}
