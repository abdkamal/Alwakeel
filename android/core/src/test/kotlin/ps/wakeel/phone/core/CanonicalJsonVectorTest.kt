package ps.wakeel.phone.core

import org.junit.Assert.assertEquals
import org.junit.Test
import ps.wakeel.phone.core.json.CanonicalJson
import ps.wakeel.phone.core.json.JsonValue
import ps.wakeel.phone.core.json.asArray
import ps.wakeel.phone.core.json.asObject
import ps.wakeel.phone.core.json.requireText
import java.io.File

/**
 * The bytes a signature covers have to be identical on both sides of the cable, so the phone's
 * canonical form is checked against the one the Windows side produced, not against itself.
 */
class CanonicalJsonVectorTest {

    @Test
    fun everySampleCanonicalisesTheWayWindowsDoes() {
        val samples = Vectors.jsonValue("canonical-json.json").asArray()
        assertEquals("the vectors carry the samples", 8, samples.size)

        samples.forEach { sample ->
            val obj = sample.asObject()
            val name = obj.requireText("name")
            val input = obj.requireText("input")
            val expected = obj.requireText("canonical")
            val produced = String(CanonicalJson.canonicalize(input.toByteArray(Charsets.UTF_8)), Charsets.UTF_8)
            assertEquals(name, expected, produced)
        }
    }

    @Test
    fun canonicalisingTwiceChangesNothing() {
        val samples = Vectors.jsonValue("canonical-json.json").asArray()
        samples.forEach { sample ->
            val canonical = sample.asObject().requireText("canonical").toByteArray(Charsets.UTF_8)
            assertEquals(
                String(canonical, Charsets.UTF_8),
                String(CanonicalJson.canonicalize(canonical), Charsets.UTF_8),
            )
        }
    }

    @Test
    fun instantsCarryMillisecondsAndTheZuluMarker() {
        val instant = CanonicalJson.parseInstant("2026-09-17T09:10:00.000Z")
        assertEquals("2026-09-17T09:10:00.000Z", CanonicalJson.instant(instant))
        // A stamp with more precision than the format carries is cut, not rounded up, so a
        // structure signed here and read on Windows describes the very same instant.
        assertEquals(
            "2026-09-17T09:10:00.123Z",
            CanonicalJson.instant(CanonicalJson.parseInstant("2026-09-17T09:10:00.123456Z")),
        )
        assertEquals(
            "2026-09-17T07:10:00.000Z",
            CanonicalJson.instant(CanonicalJson.parseInstant("2026-09-17T09:10:00+02:00")),
        )
    }

    @Test
    fun theSamplesAreWrittenBackForTheWindowsSide() {
        val out = Vectors.outputDirectory("canonical")
        val samples = Vectors.jsonValue("canonical-json.json").asArray()
        val produced = samples.map { sample ->
            val obj = sample.asObject()
            val input = obj.requireText("input")
            JsonValue.Obj(
                linkedMapOf(
                    "name" to JsonValue.Str(obj.requireText("name")),
                    "input" to JsonValue.Str(input),
                    "canonical" to JsonValue.Str(
                        String(CanonicalJson.canonicalize(input.toByteArray(Charsets.UTF_8)), Charsets.UTF_8),
                    ),
                ),
            )
        }
        File(out, "canonical-json.json").writeBytes(
            CanonicalJson.toBytes(JsonValue.Arr(produced)),
        )
    }
}
