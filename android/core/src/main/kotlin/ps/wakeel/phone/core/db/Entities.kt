package ps.wakeel.phone.core.db

import androidx.room.ColumnInfo
import androidx.room.Entity
import androidx.room.PrimaryKey

/**
 * The phone's own database, `docs/build/DATA-MODEL.md` §14: a simplified mirror of what the
 * office computer holds, plus the two tables that belong to the phone alone — the outbox of
 * what is waiting for the next exchange, and the state of the exchange itself.
 *
 * Every mirrored table carries `updated_at` and `deleted_at`, because a packet from the
 * computer can retire a row, and a retired row is hidden, never removed.
 */

@Entity(tableName = "correspondence_summary")
data class CorrespondenceSummaryRow(
    @PrimaryKey val id: String,
    val direction: String,
    @ColumnInfo(name = "official_number") val officialNumber: String? = null,
    val subject: String,
    @ColumnInfo(name = "party_name") val partyName: String? = null,
    @ColumnInfo(name = "counterparty_kind") val counterpartyKind: String? = null,
    val confidentiality: String? = null,
    val status: String,
    @ColumnInfo(name = "next_step_ar") val nextStepAr: String? = null,
    @ColumnInfo(name = "due_at") val dueAt: String? = null,
    @ColumnInfo(name = "updated_at") val updatedAt: String,
    @ColumnInfo(name = "deleted_at") val deletedAt: String? = null,
)

@Entity(tableName = "tasks")
data class TaskRow(
    @PrimaryKey val id: String,
    val title: String,
    val description: String? = null,
    @ColumnInfo(name = "due_at") val dueAt: String? = null,
    val priority: String = "normal",
    val status: String = "open",
    val progress: Int = 0,
    @ColumnInfo(name = "assignee_name") val assigneeName: String? = null,
    @ColumnInfo(name = "source_type") val sourceType: String? = null,
    @ColumnInfo(name = "source_id") val sourceId: String? = null,
    @ColumnInfo(name = "reminder_at") val reminderAt: String? = null,
    @ColumnInfo(name = "completed_at") val completedAt: String? = null,
    @ColumnInfo(name = "source_device_kind") val sourceDeviceKind: String = "pc",
    @ColumnInfo(name = "updated_at") val updatedAt: String,
    @ColumnInfo(name = "deleted_at") val deletedAt: String? = null,
)

@Entity(tableName = "meetings")
data class MeetingRow(
    @PrimaryKey val id: String,
    val title: String,
    @ColumnInfo(name = "starts_at") val startsAt: String,
    @ColumnInfo(name = "duration_min") val durationMin: Int = 60,
    val location: String? = null,
    val agenda: String? = null,
    @ColumnInfo(name = "minutes_text") val minutesText: String? = null,
    val status: String = "planned",
    @ColumnInfo(name = "reminder_minutes") val reminderMinutes: Int? = null,
    @ColumnInfo(name = "updated_at") val updatedAt: String,
    @ColumnInfo(name = "deleted_at") val deletedAt: String? = null,
)

@Entity(tableName = "appointments")
data class AppointmentRow(
    @PrimaryKey val id: String,
    val title: String,
    @ColumnInfo(name = "starts_at") val startsAt: String,
    @ColumnInfo(name = "ends_at") val endsAt: String? = null,
    val notes: String? = null,
    @ColumnInfo(name = "reminder_minutes") val reminderMinutes: Int? = null,
    val status: String = "planned",
    @ColumnInfo(name = "updated_at") val updatedAt: String,
    @ColumnInfo(name = "deleted_at") val deletedAt: String? = null,
)

@Entity(tableName = "decisions")
data class DecisionRow(
    @PrimaryKey val id: String,
    val text: String,
    @ColumnInfo(name = "source_type") val sourceType: String? = null,
    @ColumnInfo(name = "source_id") val sourceId: String? = null,
    @ColumnInfo(name = "decided_at") val decidedAt: String? = null,
    @ColumnInfo(name = "owner_name") val ownerName: String? = null,
    val status: String = "not_started",
    @ColumnInfo(name = "execution_pct") val executionPct: Int = 0,
    @ColumnInfo(name = "due_at") val dueAt: String? = null,
    @ColumnInfo(name = "updated_at") val updatedAt: String,
    @ColumnInfo(name = "deleted_at") val deletedAt: String? = null,
)

@Entity(tableName = "commitments")
data class CommitmentRow(
    @PrimaryKey val id: String,
    val title: String,
    @ColumnInfo(name = "party_name") val partyName: String? = null,
    /** Amounts are whole agorot, one shekel being a hundred of them. */
    val amount: Long = 0,
    @ColumnInfo(name = "required_text") val requiredText: String? = null,
    @ColumnInfo(name = "due_at") val dueAt: String? = null,
    val status: String = "open",
    @ColumnInfo(name = "updated_at") val updatedAt: String,
    @ColumnInfo(name = "deleted_at") val deletedAt: String? = null,
)

@Entity(tableName = "contacts")
data class ContactRow(
    @PrimaryKey val id: String,
    val name: String,
    /** `ministry`, `municipality`, `company`, `person`, or `unit` for a part of the structure. */
    val kind: String,
    @ColumnInfo(name = "contact_name") val contactName: String? = null,
    val phone: String? = null,
    val email: String? = null,
    val address: String? = null,
    @ColumnInfo(name = "parent_id") val parentId: String? = null,
    val level: Int? = null,
    @ColumnInfo(name = "qr_token") val qrToken: String? = null,
    @ColumnInfo(name = "updated_at") val updatedAt: String,
    @ColumnInfo(name = "deleted_at") val deletedAt: String? = null,
)

@Entity(tableName = "notifications")
data class NotificationRow(
    @PrimaryKey val id: String,
    val kind: String,
    val title: String,
    val body: String? = null,
    @ColumnInfo(name = "entity_type") val entityType: String? = null,
    @ColumnInfo(name = "entity_id") val entityId: String? = null,
    @ColumnInfo(name = "created_at") val createdAt: String,
    @ColumnInfo(name = "due_at") val dueAt: String? = null,
    @ColumnInfo(name = "read_at") val readAt: String? = null,
    @ColumnInfo(name = "dismissed_at") val dismissedAt: String? = null,
    val source: String = "pc",
)

@Entity(tableName = "phone_expenses")
data class PhoneExpenseRow(
    @PrimaryKey val id: String,
    val amount: Long,
    val purpose: String,
    @ColumnInfo(name = "category_name") val categoryName: String? = null,
    val at: String,
    @ColumnInfo(name = "receipt_file") val receiptFile: String? = null,
    val note: String? = null,
    /** `pending`, `confirmed` or `rejected`, as the computer decided. */
    val status: String = "pending",
    @ColumnInfo(name = "reject_reason") val rejectReason: String? = null,
    @ColumnInfo(name = "decided_at") val decidedAt: String? = null,
    @ColumnInfo(name = "updated_at") val updatedAt: String,
    @ColumnInfo(name = "deleted_at") val deletedAt: String? = null,
)

@Entity(tableName = "captures")
data class CaptureRow(
    @PrimaryKey val id: String,
    @ColumnInfo(name = "document_name") val documentName: String,
    @ColumnInfo(name = "page_count") val pageCount: Int = 1,
    @ColumnInfo(name = "ocr_text") val ocrText: String? = null,
    @ColumnInfo(name = "ocr_lang") val ocrLang: String? = null,
    @ColumnInfo(name = "correspondence_id") val correspondenceId: String? = null,
    @ColumnInfo(name = "file_name") val fileName: String? = null,
    /** `pending`, `queued` or `sent` — what the phone still owes the computer. */
    val status: String = "pending",
    @ColumnInfo(name = "created_at") val createdAt: String,
    @ColumnInfo(name = "sent_at") val sentAt: String? = null,
)

@Entity(tableName = "notes")
data class NoteRow(
    @PrimaryKey val id: String,
    val text: String? = null,
    @ColumnInfo(name = "voice_file") val voiceFile: String? = null,
    @ColumnInfo(name = "entity_type") val entityType: String? = null,
    @ColumnInfo(name = "entity_id") val entityId: String? = null,
    @ColumnInfo(name = "for_report") val forReport: Int = 0,
    @ColumnInfo(name = "source_device_kind") val sourceDeviceKind: String = "phone",
    @ColumnInfo(name = "created_at") val createdAt: String,
    @ColumnInfo(name = "updated_at") val updatedAt: String,
    @ColumnInfo(name = "deleted_at") val deletedAt: String? = null,
)

@Entity(tableName = "pinned_files")
data class PinnedFileRow(
    @PrimaryKey @ColumnInfo(name = "document_id") val documentId: String,
    val name: String,
    val mime: String? = null,
    val size: Long = 0,
    val sha256: String? = null,
    @ColumnInfo(name = "file_name") val fileName: String,
    @ColumnInfo(name = "pinned_at") val pinnedAt: String,
)

@Entity(tableName = "settings")
data class SettingRow(
    @PrimaryKey val key: String,
    val value: String,
    @ColumnInfo(name = "updated_at") val updatedAt: String,
)

/** One thing the phone owes the computer; it leaves in the next `from-phone` packet. */
@Entity(tableName = "outbox")
data class OutboxRow(
    @PrimaryKey val id: String,
    /** `expense`, `capture`, `note`, `task`, `decision`, `commitment`, `followup`, `inventory`. */
    val kind: String,
    /** The item itself, as canonical JSON. */
    val payload: String,
    /** The name of the attached file in the application's own private storage, when there is one. */
    @ColumnInfo(name = "file_name") val fileName: String? = null,
    @ColumnInfo(name = "file_size") val fileSize: Long = 0,
    @ColumnInfo(name = "created_at") val createdAt: String,
    /** `pending` while it waits, `sent` once it has left in a numbered packet. */
    val status: String = "pending",
    val seq: Int? = null,
)

/** Where the exchange stands. One row, always. */
@Entity(tableName = "sync_state")
data class SyncStateRow(
    @PrimaryKey val id: Int = 1,
    @ColumnInfo(name = "last_applied_seq") val lastAppliedSeq: Int = 0,
    @ColumnInfo(name = "last_sent_seq") val lastSentSeq: Int = 0,
    @ColumnInfo(name = "last_sync_at") val lastSyncAt: String? = null,
    @ColumnInfo(name = "paired_device_id") val pairedDeviceId: String? = null,
    @ColumnInfo(name = "org_id") val orgId: String? = null,
    @ColumnInfo(name = "office_id") val officeId: String? = null,
)
