package ps.wakeel.phone.core.json

import ps.wakeel.phone.core.crypto.CryptoErrorCode
import ps.wakeel.phone.core.crypto.CryptoException
import java.math.BigDecimal
import java.time.Instant
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.time.format.DateTimeParseException

/**
 * A JSON value. The product needs its own small model because the bytes it signs have to come
 * out exactly the way `Wakeel.Crypto/Json/CanonicalJson.cs` produces them on Windows, and no
 * library on the phone makes that promise.
 */
sealed interface JsonValue {
    data class Obj(val members: Map<String, JsonValue>) : JsonValue
    data class Arr(val items: List<JsonValue>) : JsonValue
    data class Str(val value: String) : JsonValue
    data class Num(val raw: String) : JsonValue
    data class Bool(val value: Boolean) : JsonValue
    data object Null : JsonValue
}

/** Builds an object, dropping members whose value is null — the rule the .NET writer follows. */
fun jsonObject(vararg members: Pair<String, JsonValue?>): JsonValue.Obj =
    JsonValue.Obj(LinkedHashMap<String, JsonValue>().apply {
        members.forEach { (name, value) -> if (value != null) put(name, value) }
    })

fun jsonString(value: String): JsonValue = JsonValue.Str(value)

fun jsonNumber(value: Long): JsonValue = JsonValue.Num(value.toString())

fun jsonNumber(value: Int): JsonValue = JsonValue.Num(value.toString())

fun jsonBool(value: Boolean): JsonValue = JsonValue.Bool(value)

fun jsonArray(items: List<JsonValue>): JsonValue = JsonValue.Arr(items)

/** Convenience accessors used by the readers; a wrong shape is damage, not a bug. */
fun JsonValue.asObject(): JsonValue.Obj =
    this as? JsonValue.Obj ?: throw CryptoException(CryptoErrorCode.Corrupt, "expected an object")

fun JsonValue.asArray(): List<JsonValue> =
    (this as? JsonValue.Arr)?.items ?: throw CryptoException(CryptoErrorCode.Corrupt, "expected a list")

fun JsonValue.asString(): String =
    (this as? JsonValue.Str)?.value ?: throw CryptoException(CryptoErrorCode.Corrupt, "expected text")

fun JsonValue.asInt(): Int =
    (this as? JsonValue.Num)?.raw?.toIntOrNull()
        ?: throw CryptoException(CryptoErrorCode.Corrupt, "expected a whole number")

fun JsonValue.Obj.member(name: String): JsonValue? = members[name]

fun JsonValue.Obj.require(name: String): JsonValue =
    members[name] ?: throw CryptoException(CryptoErrorCode.Corrupt, "the structure is missing a part")

fun JsonValue.Obj.text(name: String): String? = (members[name] as? JsonValue.Str)?.value

fun JsonValue.Obj.requireText(name: String): String = require(name).asString()

fun JsonValue.Obj.requireInt(name: String): Int = require(name).asInt()

/**
 * Deterministic JSON, identical to the Windows implementation: members sorted by ordinal key
 * order, no whitespace, instants as UTC with millisecond precision, UTF-8 output.
 */
object CanonicalJson {
    private val instantFormat: DateTimeFormatter =
        DateTimeFormatter.ofPattern("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'").withZone(ZoneOffset.UTC)

    /** The instant rendering every signed structure uses. */
    fun instant(value: Instant): String = instantFormat.format(value.truncatedToMillis())

    fun jsonInstant(value: Instant): JsonValue = JsonValue.Str(instant(value))

    /** Reads an instant the way the Windows reader does: anything ISO-8601, normalised to UTC. */
    fun parseInstant(text: String): Instant = try {
        java.time.OffsetDateTime.parse(text).toInstant()
    } catch (_: DateTimeParseException) {
        try {
            Instant.parse(text)
        } catch (exception: DateTimeParseException) {
            throw CryptoException(CryptoErrorCode.Corrupt, "unreadable instant", exception)
        }
    }

    /** Serialises a value in canonical form. */
    fun toBytes(value: JsonValue): ByteArray = write(value).toByteArray(Charsets.UTF_8)

    fun toText(value: JsonValue): String = write(value)

    /** Rewrites arbitrary JSON into canonical form, which is how a parsed file is re-signed. */
    fun canonicalize(utf8Json: ByteArray): ByteArray = toBytes(parse(utf8Json))

    fun parse(utf8Json: ByteArray): JsonValue = JsonParser(String(utf8Json, Charsets.UTF_8)).parseDocument()

    fun parse(text: String): JsonValue = JsonParser(text).parseDocument()

    private fun write(value: JsonValue): String = StringBuilder().also { write(value, it) }.toString()

    private fun write(value: JsonValue, out: StringBuilder) {
        when (value) {
            is JsonValue.Obj -> {
                out.append('{')
                var first = true
                // Ordinal order over the UTF-16 code units, which is what StringComparer.Ordinal
                // does on Windows and what Kotlin's own String.compareTo does here.
                for (name in value.members.keys.sortedWith { left, right -> left.compareTo(right) }) {
                    if (!first) out.append(',')
                    first = false
                    writeString(name, out)
                    out.append(':')
                    write(value.members.getValue(name), out)
                }
                out.append('}')
            }

            is JsonValue.Arr -> {
                out.append('[')
                value.items.forEachIndexed { index, item ->
                    if (index > 0) out.append(',')
                    write(item, out)
                }
                out.append(']')
            }

            is JsonValue.Str -> writeString(value.value, out)
            is JsonValue.Num -> out.append(normalizeNumber(value.raw))
            is JsonValue.Bool -> out.append(if (value.value) "true" else "false")
            JsonValue.Null -> out.append("null")
        }
    }

    /**
     * The .NET writer re-reads every number: a whole number that fits in sixty four bits is
     * written back as that integer, anything else through the decimal form. Matching it keeps
     * the signed bytes identical on both sides.
     */
    private fun normalizeNumber(raw: String): String {
        raw.toLongOrNull()?.let { return it.toString() }
        return try {
            BigDecimal(raw).stripTrailingZeros().toPlainString()
        } catch (_: NumberFormatException) {
            raw
        }
    }

    /**
     * The escaping of `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`: only the quote, the
     * backslash and the control characters are escaped, the last ones in lower case hex, and
     * every other character — Arabic included — is written as it is.
     */
    private fun writeString(value: String, out: StringBuilder) {
        out.append('"')
        for (character in value) {
            when (character) {
                '"' -> out.append("\\\"")
                '\\' -> out.append("\\\\")
                '\b' -> out.append("\\b")
                '' -> out.append("\\f")
                '\n' -> out.append("\\n")
                '\r' -> out.append("\\r")
                '\t' -> out.append("\\t")
                else ->
                    if (character < ' ') {
                        out.append("\\u")
                        val code = character.code
                        out.append("0123456789abcdef"[(code ushr 12) and 0xF])
                        out.append("0123456789abcdef"[(code ushr 8) and 0xF])
                        out.append("0123456789abcdef"[(code ushr 4) and 0xF])
                        out.append("0123456789abcdef"[code and 0xF])
                    } else {
                        out.append(character)
                    }
            }
        }
        out.append('"')
    }

    private fun Instant.truncatedToMillis(): Instant =
        Instant.ofEpochMilli(toEpochMilli())
}

/** A small recursive descent parser; anything it cannot read is reported as damage. */
private class JsonParser(private val text: String) {
    private var position = 0

    fun parseDocument(): JsonValue {
        skipWhitespace()
        val value = parseValue()
        skipWhitespace()
        if (position != text.length) fail("trailing content")
        return value
    }

    private fun parseValue(): JsonValue {
        skipWhitespace()
        if (position >= text.length) fail("unexpected end")
        return when (text[position]) {
            '{' -> parseObject()
            '[' -> parseArray()
            '"' -> JsonValue.Str(parseString())
            't' -> literal("true", JsonValue.Bool(true))
            'f' -> literal("false", JsonValue.Bool(false))
            'n' -> literal("null", JsonValue.Null)
            else -> parseNumber()
        }
    }

    private fun parseObject(): JsonValue {
        expect('{')
        val members = LinkedHashMap<String, JsonValue>()
        skipWhitespace()
        if (peek() == '}') { position++; return JsonValue.Obj(members) }
        while (true) {
            skipWhitespace()
            val name = parseString()
            skipWhitespace()
            expect(':')
            members[name] = parseValue()
            skipWhitespace()
            when (peek()) {
                ',' -> position++
                '}' -> { position++; return JsonValue.Obj(members) }
                else -> fail("a member is not closed")
            }
        }
    }

    private fun parseArray(): JsonValue {
        expect('[')
        val items = ArrayList<JsonValue>()
        skipWhitespace()
        if (peek() == ']') { position++; return JsonValue.Arr(items) }
        while (true) {
            items.add(parseValue())
            skipWhitespace()
            when (peek()) {
                ',' -> position++
                ']' -> { position++; return JsonValue.Arr(items) }
                else -> fail("a list is not closed")
            }
        }
    }

    private fun parseString(): String {
        expect('"')
        val builder = StringBuilder()
        while (true) {
            if (position >= text.length) fail("unterminated text")
            when (val character = text[position++]) {
                '"' -> return builder.toString()
                '\\' -> {
                    if (position >= text.length) fail("unterminated escape")
                    when (val escape = text[position++]) {
                        '"' -> builder.append('"')
                        '\\' -> builder.append('\\')
                        '/' -> builder.append('/')
                        'b' -> builder.append('\b')
                        'f' -> builder.append('')
                        'n' -> builder.append('\n')
                        'r' -> builder.append('\r')
                        't' -> builder.append('\t')
                        'u' -> {
                            if (position + 4 > text.length) fail("unterminated escape")
                            val code = text.substring(position, position + 4).toIntOrNull(16)
                                ?: fail("unreadable escape")
                            position += 4
                            builder.append(code.toChar())
                        }
                        else -> fail("unknown escape '$escape'")
                    }
                }
                else -> builder.append(character)
            }
        }
    }

    private fun parseNumber(): JsonValue {
        val start = position
        if (peek() == '-') position++
        while (position < text.length && (text[position].isDigit() || text[position] in ".eE+-")) position++
        val raw = text.substring(start, position)
        if (raw.isEmpty() || raw.toBigDecimalOrNull() == null) fail("unreadable number")
        return JsonValue.Num(raw)
    }

    private fun literal(word: String, value: JsonValue): JsonValue {
        if (!text.startsWith(word, position)) fail("unknown word")
        position += word.length
        return value
    }

    private fun peek(): Char = if (position < text.length) text[position] else ' '

    private fun expect(character: Char) {
        if (peek() != character) fail("expected '$character'")
        position++
    }

    private fun skipWhitespace() {
        while (position < text.length && text[position] in " \t\r\n") position++
    }

    private fun fail(reason: String): Nothing =
        throw CryptoException(CryptoErrorCode.Corrupt, "the structure could not be read: $reason")
}

private fun String.toBigDecimalOrNull(): BigDecimal? = try {
    BigDecimal(this)
} catch (_: NumberFormatException) {
    null
}
