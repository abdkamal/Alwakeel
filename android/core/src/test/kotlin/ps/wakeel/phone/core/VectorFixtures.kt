package ps.wakeel.phone.core

import ps.wakeel.phone.core.crypto.Base64Url
import ps.wakeel.phone.core.crypto.DeviceIdentity
import ps.wakeel.phone.core.crypto.DeviceSeeds
import ps.wakeel.phone.core.json.CanonicalJson
import ps.wakeel.phone.core.json.JsonValue
import ps.wakeel.phone.core.json.asObject
import ps.wakeel.phone.core.json.requireText
import java.io.File

/**
 * The interoperability vectors. They are produced by
 * `tests/Wakeel.Crypto.Tests/AndroidVectorTests.cs` on the Windows side and read here, so the
 * two implementations of the same format are compared against each other rather than each
 * against its own idea of the format.
 */
object Vectors {

    fun bytes(path: String): ByteArray =
        Vectors::class.java.classLoader!!.getResourceAsStream("vectors/$path")?.use { it.readBytes() }
            ?: error(
                "The vector 'vectors/$path' is missing. Produce the vectors first:\n" +
                    "  dotnet build tests/Wakeel.Crypto.Tests\n" +
                    "  dotnet test tests/Wakeel.Crypto.Tests --no-build --filter AndroidVector",
            )

    fun text(path: String): String = String(bytes(path), Charsets.UTF_8)

    fun json(path: String): JsonValue.Obj = CanonicalJson.parse(bytes(path)).asObject()

    fun jsonValue(path: String): JsonValue = CanonicalJson.parse(bytes(path))

    /** Where this side writes its own samples, for the Windows test to read in turn. */
    fun outputDirectory(name: String): File =
        File(System.getProperty("wakeel.vectors.out") ?: "build/vectors-out", name).apply { mkdirs() }

    val keys: JsonValue.Obj by lazy { json("keys.json") }

    val orgId: String get() = keys.requireText("orgId")
    val officeId: String get() = keys.requireText("officeId")
    val pcDeviceId: String get() = keys.requireText("pcDeviceId")
    val phoneDeviceId: String get() = keys.requireText("phoneDeviceId")
    val revokedDeviceId: String get() = keys.requireText("revokedDeviceId")

    val now: java.time.Instant get() = CanonicalJson.parseInstant(keys.requireText("now"))
    val issuedAt: java.time.Instant get() = CanonicalJson.parseInstant(keys.requireText("issuedAt"))

    val officeKey: ByteArray get() = Base64Url.decode(keys.requireText("officeKey"))
    val sessionKey: ByteArray get() = Base64Url.decode(keys.requireText("sessionKey"))
    val pairingToken: ByteArray get() = Base64Url.decode(keys.requireText("pairingToken"))
    val pairingShortCode: String get() = keys.requireText("pairingShortCode")

    fun identity(which: String): DeviceIdentity {
        val part = (keys.members[which] ?: error("no keys for $which")).asObject()
        return DeviceIdentity.import(
            DeviceSeeds(
                Base64Url.decode(part.requireText("signingSeed")),
                Base64Url.decode(part.requireText("agreementSeed")),
            ),
        )
    }

    val org: DeviceIdentity by lazy { identity("org") }
    val pc: DeviceIdentity by lazy { identity("pc") }
    val phone: DeviceIdentity by lazy { identity("phone") }
}
