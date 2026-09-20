package ps.wakeel.phone.core.sync

import ps.wakeel.phone.core.crypto.CryptoErrorCode
import ps.wakeel.phone.core.crypto.CryptoException
import ps.wakeel.phone.core.db.AppointmentRow
import ps.wakeel.phone.core.db.CommitmentRow
import ps.wakeel.phone.core.db.ContactRow
import ps.wakeel.phone.core.db.CorrespondenceSummaryRow
import ps.wakeel.phone.core.db.DecisionRow
import ps.wakeel.phone.core.db.MeetingRow
import ps.wakeel.phone.core.db.NoteRow
import ps.wakeel.phone.core.db.NotificationRow
import ps.wakeel.phone.core.db.PhoneExpenseRow
import ps.wakeel.phone.core.db.PinnedFileRow
import ps.wakeel.phone.core.db.SettingRow
import ps.wakeel.phone.core.db.TaskRow
import ps.wakeel.phone.core.json.JsonValue
import ps.wakeel.phone.core.json.requireText

/**
 * Turns the rows of a packet into the phone's own records. Each field is read by name and
 * checked on the way in: a packet is a file, and a file is never trusted because it decrypted.
 */
internal object RowMapping {

    fun correspondence(row: JsonValue.Obj) = CorrespondenceSummaryRow(
        id = row.id(),
        direction = row.requireText("direction"),
        officialNumber = row.opt("official_number"),
        subject = row.requireText("subject"),
        partyName = row.opt("party_name"),
        counterpartyKind = row.opt("counterparty_kind"),
        confidentiality = row.opt("confidentiality"),
        status = row.requireText("status"),
        nextStepAr = row.opt("next_step_ar"),
        dueAt = row.opt("due_at"),
        updatedAt = row.requireText("updated_at"),
        deletedAt = row.opt("deleted_at"),
    )

    fun task(row: JsonValue.Obj) = TaskRow(
        id = row.id(),
        title = row.requireText("title"),
        description = row.opt("description"),
        dueAt = row.opt("due_at"),
        priority = row.opt("priority") ?: "normal",
        status = row.opt("status") ?: "open",
        progress = row.int("progress", 0),
        assigneeName = row.opt("assignee_name"),
        sourceType = row.opt("source_type"),
        sourceId = row.opt("source_id"),
        reminderAt = row.opt("reminder_at"),
        completedAt = row.opt("completed_at"),
        sourceDeviceKind = row.opt("source_device_kind") ?: "pc",
        updatedAt = row.requireText("updated_at"),
        deletedAt = row.opt("deleted_at"),
    )

    fun meeting(row: JsonValue.Obj) = MeetingRow(
        id = row.id(),
        title = row.requireText("title"),
        startsAt = row.requireText("starts_at"),
        durationMin = row.int("duration_min", 60),
        location = row.opt("location"),
        agenda = row.opt("agenda"),
        minutesText = row.opt("minutes_text"),
        status = row.opt("status") ?: "planned",
        reminderMinutes = row.intOrNull("reminder_minutes"),
        updatedAt = row.requireText("updated_at"),
        deletedAt = row.opt("deleted_at"),
    )

    fun appointment(row: JsonValue.Obj) = AppointmentRow(
        id = row.id(),
        title = row.requireText("title"),
        startsAt = row.requireText("starts_at"),
        endsAt = row.opt("ends_at"),
        notes = row.opt("notes"),
        reminderMinutes = row.intOrNull("reminder_minutes"),
        status = row.opt("status") ?: "planned",
        updatedAt = row.requireText("updated_at"),
        deletedAt = row.opt("deleted_at"),
    )

    fun decision(row: JsonValue.Obj) = DecisionRow(
        id = row.id(),
        text = row.requireText("text"),
        sourceType = row.opt("source_type"),
        sourceId = row.opt("source_id"),
        decidedAt = row.opt("decided_at"),
        ownerName = row.opt("owner_name"),
        status = row.opt("status") ?: "not_started",
        executionPct = row.int("execution_pct", 0),
        dueAt = row.opt("due_at"),
        updatedAt = row.requireText("updated_at"),
        deletedAt = row.opt("deleted_at"),
    )

    fun commitment(row: JsonValue.Obj) = CommitmentRow(
        id = row.id(),
        title = row.requireText("title"),
        partyName = row.opt("party_name"),
        amount = row.long("amount", 0),
        requiredText = row.opt("required_text"),
        dueAt = row.opt("due_at"),
        status = row.opt("status") ?: "open",
        updatedAt = row.requireText("updated_at"),
        deletedAt = row.opt("deleted_at"),
    )

    fun contact(row: JsonValue.Obj) = ContactRow(
        id = row.id(),
        name = row.requireText("name"),
        kind = row.requireText("kind"),
        contactName = row.opt("contact_name"),
        phone = row.opt("phone"),
        email = row.opt("email"),
        address = row.opt("address"),
        parentId = row.opt("parent_id"),
        level = row.intOrNull("level"),
        qrToken = row.opt("qr_token"),
        updatedAt = row.requireText("updated_at"),
        deletedAt = row.opt("deleted_at"),
    )

    fun notification(row: JsonValue.Obj) = NotificationRow(
        id = row.id(),
        kind = row.requireText("kind"),
        title = row.requireText("title"),
        body = row.opt("body"),
        entityType = row.opt("entity_type"),
        entityId = row.opt("entity_id"),
        createdAt = row.requireText("created_at"),
        dueAt = row.opt("due_at"),
        readAt = row.opt("read_at"),
        dismissedAt = row.opt("dismissed_at"),
        source = row.opt("source") ?: "pc",
    )

    fun phoneExpense(row: JsonValue.Obj) = PhoneExpenseRow(
        id = row.id(),
        amount = row.long("amount", 0),
        purpose = row.requireText("purpose"),
        categoryName = row.opt("category_name"),
        at = row.requireText("at"),
        receiptFile = row.opt("receipt_file"),
        note = row.opt("note"),
        status = row.opt("status") ?: "pending",
        rejectReason = row.opt("reject_reason"),
        decidedAt = row.opt("decided_at"),
        updatedAt = row.requireText("updated_at"),
        deletedAt = row.opt("deleted_at"),
    )

    fun note(row: JsonValue.Obj) = NoteRow(
        id = row.id(),
        text = row.opt("text"),
        voiceFile = row.opt("voice_file"),
        entityType = row.opt("entity_type"),
        entityId = row.opt("entity_id"),
        forReport = row.int("for_report", 0),
        sourceDeviceKind = row.opt("source_device_kind") ?: "pc",
        createdAt = row.requireText("created_at"),
        updatedAt = row.requireText("updated_at"),
        deletedAt = row.opt("deleted_at"),
    )

    fun pinnedFile(row: JsonValue.Obj) = PinnedFileRow(
        documentId = row.requireText("document_id"),
        name = row.requireText("name"),
        mime = row.opt("mime"),
        size = row.long("size", 0),
        sha256 = row.opt("sha256"),
        fileName = row.requireText("file_name"),
        pinnedAt = row.requireText("pinned_at"),
    )

    fun setting(row: JsonValue.Obj) = SettingRow(
        key = row.requireText("key"),
        value = row.requireText("value"),
        updatedAt = row.requireText("updated_at"),
    )
}

private fun JsonValue.Obj.id(): String {
    val value = requireText("id")
    if (value.isBlank()) {
        throw CryptoException(CryptoErrorCode.Corrupt, "a row in the packet carries no identifier")
    }
    return value
}

internal fun JsonValue.Obj.opt(name: String): String? = (members[name] as? JsonValue.Str)?.value

internal fun JsonValue.Obj.intOrNull(name: String): Int? = (members[name] as? JsonValue.Num)?.raw?.toIntOrNull()

internal fun JsonValue.Obj.int(name: String, default: Int): Int = intOrNull(name) ?: default

internal fun JsonValue.Obj.long(name: String, default: Long): Long =
    (members[name] as? JsonValue.Num)?.raw?.toLongOrNull() ?: default
