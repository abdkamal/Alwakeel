package ps.wakeel.phone.core.sync

import ps.wakeel.phone.core.crypto.CryptoErrorCode
import ps.wakeel.phone.core.crypto.CryptoException
import ps.wakeel.phone.core.json.CanonicalJson
import ps.wakeel.phone.core.json.JsonValue
import ps.wakeel.phone.core.json.asObject
import ps.wakeel.phone.core.json.jsonArray
import ps.wakeel.phone.core.json.jsonNumber
import ps.wakeel.phone.core.json.jsonObject
import ps.wakeel.phone.core.json.jsonString
import ps.wakeel.phone.core.json.requireText
import java.time.Instant

/**
 * The names the two sides agree on inside the `Wakeel` folder. Nothing else is ever written
 * there, and a file that does not match one of these is left alone.
 */
object PhonePackageNames {
    const val EXTENSION = ".wakeel-phone"

    /** The computer's packets, numbered and applied in order. */
    fun toPhone(seq: Int): String = "to-phone-$seq$EXTENSION"

    /** The phone's packets. */
    fun fromPhone(seq: Int): String = "from-phone-$seq$EXTENSION"

    /** The receipt the phone leaves, naming the last packet it applied in full. */
    const val ACK = "phone-ack$EXTENSION"

    /** The phone's answer while pairing. */
    const val PAIRING_REQUEST = "pairing-request$EXTENSION"

    /** The computer's answer while pairing. */
    const val PAIRING_ACCEPT = "pairing-accept$EXTENSION"

    /** The invitation the computer leaves, named after the six digit code. */
    fun pairingInvitation(shortCode: String): String = "pairing-$shortCode$EXTENSION"

    private val toPhonePattern = Regex("^to-phone-(\\d{1,9})\\.wakeel-phone$")
    private val fromPhonePattern = Regex("^from-phone-(\\d{1,9})\\.wakeel-phone$")
    private val invitationPattern = Regex("^pairing-(\\d{6})\\.wakeel-phone$")

    fun toPhoneSequence(name: String): Int? = toPhonePattern.find(name)?.groupValues?.get(1)?.toIntOrNull()

    fun fromPhoneSequence(name: String): Int? = fromPhonePattern.find(name)?.groupValues?.get(1)?.toIntOrNull()

    fun invitationCode(name: String): String? = invitationPattern.find(name)?.groupValues?.get(1)
}

/** The names of the two items inside a packet. */
object PhonePackageEntries {
    /** The records themselves, as canonical JSON. */
    const val BODY = "package.json"

    /** The receipt inside an acknowledgement packet. */
    const val ACK_BODY = "ack.json"

    /** The prefix of every attached file inside a packet. */
    const val FILE_PREFIX = "files/"
}

/**
 * A packet from the computer: rows for the mirrored tables, and the files they refer to. The
 * column names are the phone's own, so the computer writes the phone's shape and the phone
 * applies it without a second mapping.
 */
data class IncomingPackage(
    val seq: Int,
    val createdAt: Instant,
    val tables: Map<String, List<JsonValue.Obj>>,
) {
    companion object {
        fun fromJson(value: JsonValue): IncomingPackage {
            val obj = value.asObject()
            val seq = (obj.members["seq"] as? JsonValue.Num)?.raw?.toIntOrNull()
                ?: throw CryptoException(CryptoErrorCode.Corrupt, "the packet carries no number")
            val tablesValue = obj.members["tables"]?.asObject()
            val tables = LinkedHashMap<String, List<JsonValue.Obj>>()
            tablesValue?.members?.forEach { (name, rows) ->
                tables[name] = (rows as? JsonValue.Arr)?.items.orEmpty().map { it.asObject() }
            }
            return IncomingPackage(
                seq = seq,
                createdAt = CanonicalJson.parseInstant(obj.requireText("createdAt")),
                tables = tables,
            )
        }
    }

    fun toJson(): JsonValue = jsonObject(
        "seq" to jsonNumber(seq),
        "createdAt" to CanonicalJson.jsonInstant(createdAt),
        "tables" to JsonValue.Obj(tables.mapValues { (_, rows) -> jsonArray(rows) }),
    )

    val rowCount: Int get() = tables.values.sumOf { it.size }
}

/** One thing the phone sends: the record itself and, when there is one, its file. */
data class OutgoingItem(
    val id: String,
    val kind: String,
    val createdAt: Instant,
    val payload: JsonValue,
    /** The entry name inside the packet, `files/…`, when the item carries a file. */
    val file: String? = null,
) {
    fun toJson(): JsonValue = jsonObject(
        "id" to jsonString(id),
        "kind" to jsonString(kind),
        "createdAt" to CanonicalJson.jsonInstant(createdAt),
        "payload" to payload,
        "file" to file?.let { jsonString(it) },
    )

    companion object {
        fun fromJson(value: JsonValue): OutgoingItem {
            val obj = value.asObject()
            return OutgoingItem(
                id = obj.requireText("id"),
                kind = obj.requireText("kind"),
                createdAt = CanonicalJson.parseInstant(obj.requireText("createdAt")),
                payload = obj.members["payload"] ?: JsonValue.Null,
                file = (obj.members["file"] as? JsonValue.Str)?.value,
            )
        }
    }
}

/** A packet the phone writes. */
data class OutgoingPackage(
    val seq: Int,
    val createdAt: Instant,
    val items: List<OutgoingItem>,
) {
    fun toJson(): JsonValue = jsonObject(
        "seq" to jsonNumber(seq),
        "createdAt" to CanonicalJson.jsonInstant(createdAt),
        "items" to jsonArray(items.map { it.toJson() }),
    )

    companion object {
        fun fromJson(value: JsonValue): OutgoingPackage {
            val obj = value.asObject()
            val seq = (obj.members["seq"] as? JsonValue.Num)?.raw?.toIntOrNull()
                ?: throw CryptoException(CryptoErrorCode.Corrupt, "the packet carries no number")
            return OutgoingPackage(
                seq = seq,
                createdAt = CanonicalJson.parseInstant(obj.requireText("createdAt")),
                items = (obj.members["items"] as? JsonValue.Arr)?.items.orEmpty().map { OutgoingItem.fromJson(it) },
            )
        }
    }
}

/** The receipt: the number of the last packet the phone applied in full. */
data class SyncAcknowledgement(val lastAppliedSeq: Int, val at: Instant) {
    fun toJson(): JsonValue = jsonObject(
        "lastAppliedSeq" to jsonNumber(lastAppliedSeq),
        "at" to CanonicalJson.jsonInstant(at),
    )

    companion object {
        fun fromJson(value: JsonValue): SyncAcknowledgement {
            val obj = value.asObject()
            return SyncAcknowledgement(
                lastAppliedSeq = (obj.members["lastAppliedSeq"] as? JsonValue.Num)?.raw?.toIntOrNull()
                    ?: throw CryptoException(CryptoErrorCode.Corrupt, "the receipt carries no number"),
                at = CanonicalJson.parseInstant(obj.requireText("at")),
            )
        }
    }
}
