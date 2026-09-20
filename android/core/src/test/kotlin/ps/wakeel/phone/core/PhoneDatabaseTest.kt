package ps.wakeel.phone.core

import androidx.test.core.app.ApplicationProvider
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import ps.wakeel.phone.core.db.CaptureRow
import ps.wakeel.phone.core.db.ContactRow
import ps.wakeel.phone.core.db.CorrespondenceSummaryRow
import ps.wakeel.phone.core.db.NoteRow
import ps.wakeel.phone.core.db.NotificationRow
import ps.wakeel.phone.core.db.OutboxRow
import ps.wakeel.phone.core.db.PhoneExpenseRow
import ps.wakeel.phone.core.db.PinnedFileRow
import ps.wakeel.phone.core.db.SettingRow
import ps.wakeel.phone.core.db.SyncStateRow
import ps.wakeel.phone.core.db.TaskRow
import ps.wakeel.phone.core.db.WakeelPhoneDatabase

/**
 * The phone database of `docs/build/DATA-MODEL.md` §14. The tests run it in memory, because the
 * SQLCipher library the phone opens it with is native to the device; the schema, the queries and
 * the rule that a retired row stays hidden are the same either way.
 */
@RunWith(RobolectricTestRunner::class)
@Config(sdk = [35])
class PhoneDatabaseTest {

    private lateinit var database: WakeelPhoneDatabase

    @Before
    fun open() {
        database = WakeelPhoneDatabase.inMemory(ApplicationProvider.getApplicationContext())
    }

    @After
    fun close() {
        database.close()
    }

    @Test
    fun everyTableOfTheModelExists() = runBlocking {
        // Touching each accessor is what proves the schema Room built carries all fifteen.
        assertTrue(database.correspondence().all().isEmpty())
        assertTrue(database.tasks().all().isEmpty())
        assertTrue(database.meetings().all().isEmpty())
        assertTrue(database.appointments().all().isEmpty())
        assertTrue(database.decisions().all().isEmpty())
        assertTrue(database.commitments().all().isEmpty())
        assertTrue(database.contacts().all().isEmpty())
        assertTrue(database.notifications().all().isEmpty())
        assertTrue(database.phoneExpenses().all().isEmpty())
        assertTrue(database.captures().all().isEmpty())
        assertTrue(database.notes().all().isEmpty())
        assertTrue(database.pinnedFiles().all().isEmpty())
        assertTrue(database.settings().all().isEmpty())
        assertTrue(database.outbox().all().isEmpty())
        assertNull(database.syncState().get())
    }

    @Test
    fun aRetiredRowDisappearsFromEveryList() = runBlocking {
        database.tasks().upsert(
            listOf(
                TaskRow(id = "T-1", title = "مراجعة كتاب", updatedAt = "2026-09-17T09:00:00.000Z"),
                TaskRow(
                    id = "T-2",
                    title = "مهمة ملغاة",
                    updatedAt = "2026-09-17T09:00:00.000Z",
                    deletedAt = "2026-09-17T09:05:00.000Z",
                ),
            ),
        )
        assertEquals(listOf("T-1"), database.tasks().all().map { it.id })
        assertNull(database.tasks().byId("T-2"))
    }

    @Test
    fun aRowThatArrivesTwiceIsReplacedNotDuplicated() = runBlocking {
        database.tasks().upsert(TaskRow(id = "T-1", title = "قديم", updatedAt = "2026-09-17T09:00:00.000Z"))
        database.tasks().upsert(TaskRow(id = "T-1", title = "جديد", updatedAt = "2026-09-17T09:10:00.000Z"))
        assertEquals(1, database.tasks().all().size)
        assertEquals("جديد", database.tasks().byId("T-1")?.title)
    }

    @Test
    fun theOpenTasksAndTheLateCountAreWhatTheHomeScreenShows() = runBlocking {
        database.tasks().upsert(
            listOf(
                TaskRow(id = "T-1", title = "متأخرة", dueAt = "2026-09-10T09:00:00.000Z", updatedAt = "x"),
                TaskRow(id = "T-2", title = "قادمة", dueAt = "2026-09-30T09:00:00.000Z", updatedAt = "x"),
                TaskRow(id = "T-3", title = "منتهية", status = "done", dueAt = "2026-09-01T09:00:00.000Z", updatedAt = "x"),
            ),
        )
        assertEquals(2, database.tasks().open().size)
        assertEquals(1, database.tasks().lateCount("2026-09-17T09:00:00.000Z"))
    }

    @Test
    fun correspondenceIsFoundByItsNumberItsSubjectOrItsParty() = runBlocking {
        database.correspondence().upsert(
            listOf(
                CorrespondenceSummaryRow(
                    id = "C-1",
                    direction = "in",
                    officialNumber = "20260917/1101",
                    subject = "طلب صيانة مبنى الدائرة",
                    partyName = "وزارة الأشغال",
                    status = "new",
                    updatedAt = "2026-09-17T09:00:00.000Z",
                ),
                CorrespondenceSummaryRow(
                    id = "C-2",
                    direction = "out",
                    subject = "رد التعميم",
                    status = "done",
                    updatedAt = "2026-09-16T09:00:00.000Z",
                ),
            ),
        )
        assertEquals(listOf("C-1"), database.correspondence().search("1101").map { it.id })
        assertEquals(listOf("C-1"), database.correspondence().search("صيانة").map { it.id })
        assertEquals(listOf("C-1"), database.correspondence().search("الأشغال").map { it.id })
        assertEquals(listOf("C-2"), database.correspondence().byDirection("out").map { it.id })
        assertEquals(1, database.correspondence().openCount())
    }

    @Test
    fun anEntityIsFoundByTheCodeOnItsLabel() = runBlocking {
        database.contacts().upsert(
            listOf(
                ContactRow(id = "P-1", name = "بلدية بيت لحم", kind = "municipality", qrToken = "QR-1", updatedAt = "x"),
            ),
        )
        assertEquals("P-1", database.contacts().byQrToken("QR-1")?.id)
        assertNull(database.contacts().byQrToken("QR-2"))
        assertEquals(1, database.contacts().search("بيت").size)
    }

    @Test
    fun theExpenseStatesAreKeptApart() = runBlocking {
        database.phoneExpenses().upsert(
            listOf(
                PhoneExpenseRow(id = "E-1", amount = 12000, purpose = "وقود", at = "2026-09-17T08:00:00.000Z", updatedAt = "x"),
                PhoneExpenseRow(
                    id = "E-2",
                    amount = 8500,
                    purpose = "قرطاسية",
                    at = "2026-09-16T08:00:00.000Z",
                    status = "rejected",
                    rejectReason = "بلا إيصال",
                    updatedAt = "x",
                ),
            ),
        )
        assertEquals(listOf("E-1"), database.phoneExpenses().byStatus("pending").map { it.id })
        assertEquals("بلا إيصال", database.phoneExpenses().byId("E-2")?.rejectReason)
    }

    @Test
    fun theBellCountsOnlyWhatHasNotBeenReadOrDismissed() = runBlocking {
        database.notifications().upsert(
            listOf(
                NotificationRow(id = "N-1", kind = "meeting", title = "اجتماع", createdAt = "x"),
                NotificationRow(id = "N-2", kind = "task", title = "مهمة", createdAt = "x", readAt = "y"),
                NotificationRow(id = "N-3", kind = "task", title = "مهمة", createdAt = "x", dismissedAt = "y"),
            ),
        )
        assertEquals(1, database.notifications().unreadCount())
        database.notifications().markRead("N-1", "2026-09-17T09:00:00.000Z")
        assertEquals(0, database.notifications().unreadCount())
        assertEquals(2, database.notifications().all().size)
    }

    @Test
    fun thePinnedFilesReportTheirTotalSizeAndCanBeRemoved() = runBlocking {
        database.pinnedFiles().upsert(
            listOf(
                PinnedFileRow(documentId = "D-1", name = "عقد.pdf", size = 1000, fileName = "a.bin", pinnedAt = "x"),
                PinnedFileRow(documentId = "D-2", name = "كشف.pdf", size = 2500, fileName = "b.bin", pinnedAt = "x"),
            ),
        )
        assertEquals(3500L, database.pinnedFiles().totalSize())
        database.pinnedFiles().remove("D-1")
        assertEquals(2500L, database.pinnedFiles().totalSize())
    }

    @Test
    fun theOutboxKeepsWhatIsStillOwedAndForgetsWhatLeft() = runBlocking {
        database.outbox().add(
            listOf(
                OutboxRow(id = "O-1", kind = "expense", payload = "{}", createdAt = "2026-09-17T09:00:00.000Z"),
                OutboxRow(id = "O-2", kind = "note", payload = "{}", createdAt = "2026-09-17T09:01:00.000Z"),
            ),
        )
        assertEquals(2, database.outbox().pendingCount())
        database.outbox().markSent(listOf("O-1"), 4)
        assertEquals(1, database.outbox().pendingCount())
        assertEquals(listOf("O-2"), database.outbox().pending().map { it.id })
    }

    @Test
    fun capturesNotesAndSettingsBehaveTheWayTheScreensNeed() = runBlocking {
        database.captures().upsert(
            CaptureRow(id = "S-1", documentName = "كتاب وارد", createdAt = "2026-09-17T09:00:00.000Z"),
        )
        assertEquals(1, database.captures().waiting().size)

        database.notes().upsert(
            listOf(
                NoteRow(id = "M-1", text = "ملاحظة", createdAt = "x", updatedAt = "x"),
                NoteRow(id = "M-2", text = "للتقرير", forReport = 1, createdAt = "x", updatedAt = "x"),
            ),
        )
        assertEquals(listOf("M-2"), database.notes().forReport().map { it.id })

        database.settings().upsert(SettingRow("meeting_reminder_minutes", "15", "x"))
        assertEquals("15", database.settings().value("meeting_reminder_minutes"))
        assertNull(database.settings().value("nothing"))
    }

    @Test
    fun theExchangeStateIsOneRow() = runBlocking {
        database.syncState().put(SyncStateRow(lastAppliedSeq = 3, lastSentSeq = 2, pairedDeviceId = "PC-1"))
        database.syncState().put(SyncStateRow(lastAppliedSeq = 4, lastSentSeq = 2, pairedDeviceId = "PC-1"))
        val state = database.syncState().get()
        assertNotNull(state)
        assertEquals(4, state!!.lastAppliedSeq)
        assertEquals("PC-1", state.pairedDeviceId)
    }
}
