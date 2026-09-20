package ps.wakeel.phone.core.db

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import androidx.room.Update

/**
 * Every list the phone shows hides retired rows, because a packet from the computer can retire
 * one and the product never removes a row that the two sides share.
 */

@Dao
interface CorrespondenceDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(rows: List<CorrespondenceSummaryRow>)

    @Query("SELECT * FROM correspondence_summary WHERE deleted_at IS NULL ORDER BY updated_at DESC")
    suspend fun all(): List<CorrespondenceSummaryRow>

    @Query("SELECT * FROM correspondence_summary WHERE deleted_at IS NULL AND direction = :direction ORDER BY updated_at DESC")
    suspend fun byDirection(direction: String): List<CorrespondenceSummaryRow>

    @Query("SELECT * FROM correspondence_summary WHERE id = :id AND deleted_at IS NULL")
    suspend fun byId(id: String): CorrespondenceSummaryRow?

    @Query(
        "SELECT * FROM correspondence_summary WHERE deleted_at IS NULL AND " +
            "(subject LIKE '%' || :text || '%' OR official_number LIKE '%' || :text || '%' " +
            "OR party_name LIKE '%' || :text || '%') ORDER BY updated_at DESC",
    )
    suspend fun search(text: String): List<CorrespondenceSummaryRow>

    @Query("SELECT COUNT(*) FROM correspondence_summary WHERE deleted_at IS NULL AND status IN ('new', 'in_progress')")
    suspend fun openCount(): Int
}

@Dao
interface TaskDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(rows: List<TaskRow>)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(row: TaskRow)

    @Update
    suspend fun update(row: TaskRow)

    @Query("SELECT * FROM tasks WHERE deleted_at IS NULL ORDER BY due_at IS NULL, due_at")
    suspend fun all(): List<TaskRow>

    @Query("SELECT * FROM tasks WHERE deleted_at IS NULL AND status != 'done' ORDER BY due_at IS NULL, due_at")
    suspend fun open(): List<TaskRow>

    @Query("SELECT * FROM tasks WHERE id = :id AND deleted_at IS NULL")
    suspend fun byId(id: String): TaskRow?

    @Query("SELECT COUNT(*) FROM tasks WHERE deleted_at IS NULL AND status != 'done' AND due_at IS NOT NULL AND due_at < :now")
    suspend fun lateCount(now: String): Int
}

@Dao
interface MeetingDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(rows: List<MeetingRow>)

    @Query("SELECT * FROM meetings WHERE deleted_at IS NULL ORDER BY starts_at")
    suspend fun all(): List<MeetingRow>

    @Query("SELECT * FROM meetings WHERE deleted_at IS NULL AND starts_at >= :from AND starts_at < :to ORDER BY starts_at")
    suspend fun between(from: String, to: String): List<MeetingRow>
}

@Dao
interface AppointmentDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(rows: List<AppointmentRow>)

    @Query("SELECT * FROM appointments WHERE deleted_at IS NULL ORDER BY starts_at")
    suspend fun all(): List<AppointmentRow>
}

@Dao
interface DecisionDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(rows: List<DecisionRow>)

    @Query("SELECT * FROM decisions WHERE deleted_at IS NULL ORDER BY decided_at DESC")
    suspend fun all(): List<DecisionRow>
}

@Dao
interface CommitmentDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(rows: List<CommitmentRow>)

    @Query("SELECT * FROM commitments WHERE deleted_at IS NULL ORDER BY due_at IS NULL, due_at")
    suspend fun all(): List<CommitmentRow>
}

@Dao
interface ContactDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(rows: List<ContactRow>)

    @Query("SELECT * FROM contacts WHERE deleted_at IS NULL ORDER BY name")
    suspend fun all(): List<ContactRow>

    @Query("SELECT * FROM contacts WHERE deleted_at IS NULL AND name LIKE '%' || :text || '%' ORDER BY name")
    suspend fun search(text: String): List<ContactRow>

    @Query("SELECT * FROM contacts WHERE qr_token = :token AND deleted_at IS NULL")
    suspend fun byQrToken(token: String): ContactRow?
}

@Dao
interface NotificationDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(rows: List<NotificationRow>)

    @Query("SELECT * FROM notifications WHERE dismissed_at IS NULL ORDER BY created_at DESC")
    suspend fun all(): List<NotificationRow>

    @Query("SELECT COUNT(*) FROM notifications WHERE read_at IS NULL AND dismissed_at IS NULL")
    suspend fun unreadCount(): Int

    @Query("UPDATE notifications SET read_at = :at WHERE id = :id")
    suspend fun markRead(id: String, at: String)
}

@Dao
interface PhoneExpenseDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(rows: List<PhoneExpenseRow>)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(row: PhoneExpenseRow)

    @Query("SELECT * FROM phone_expenses WHERE deleted_at IS NULL ORDER BY at DESC")
    suspend fun all(): List<PhoneExpenseRow>

    @Query("SELECT * FROM phone_expenses WHERE deleted_at IS NULL AND status = :status ORDER BY at DESC")
    suspend fun byStatus(status: String): List<PhoneExpenseRow>

    @Query("SELECT * FROM phone_expenses WHERE id = :id")
    suspend fun byId(id: String): PhoneExpenseRow?
}

@Dao
interface CaptureDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(row: CaptureRow)

    @Query("SELECT * FROM captures ORDER BY created_at DESC")
    suspend fun all(): List<CaptureRow>

    @Query("SELECT * FROM captures WHERE status != 'sent' ORDER BY created_at")
    suspend fun waiting(): List<CaptureRow>
}

@Dao
interface NoteDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(rows: List<NoteRow>)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(row: NoteRow)

    @Query("SELECT * FROM notes WHERE deleted_at IS NULL ORDER BY created_at DESC")
    suspend fun all(): List<NoteRow>

    @Query("SELECT * FROM notes WHERE deleted_at IS NULL AND for_report = 1 ORDER BY created_at DESC")
    suspend fun forReport(): List<NoteRow>
}

@Dao
interface PinnedFileDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(rows: List<PinnedFileRow>)

    @Query("SELECT * FROM pinned_files ORDER BY pinned_at DESC")
    suspend fun all(): List<PinnedFileRow>

    @Query("SELECT COALESCE(SUM(size), 0) FROM pinned_files")
    suspend fun totalSize(): Long

    @Query("DELETE FROM pinned_files WHERE document_id = :documentId")
    suspend fun remove(documentId: String)
}

@Dao
interface SettingDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(rows: List<SettingRow>)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(row: SettingRow)

    @Query("SELECT * FROM settings")
    suspend fun all(): List<SettingRow>

    @Query("SELECT value FROM settings WHERE key = :key")
    suspend fun value(key: String): String?
}

@Dao
interface OutboxDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun add(row: OutboxRow)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun add(rows: List<OutboxRow>)

    @Query("SELECT * FROM outbox WHERE status = 'pending' ORDER BY created_at, id")
    suspend fun pending(): List<OutboxRow>

    @Query("SELECT COUNT(*) FROM outbox WHERE status = 'pending'")
    suspend fun pendingCount(): Int

    @Query("UPDATE outbox SET status = 'sent', seq = :seq WHERE id IN (:ids)")
    suspend fun markSent(ids: List<String>, seq: Int)

    @Query("SELECT * FROM outbox ORDER BY created_at")
    suspend fun all(): List<OutboxRow>
}

@Dao
interface SyncStateDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun put(row: SyncStateRow)

    @Query("SELECT * FROM sync_state WHERE id = 1")
    suspend fun get(): SyncStateRow?
}
