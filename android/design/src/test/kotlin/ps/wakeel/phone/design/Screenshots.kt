package ps.wakeel.phone.design

import java.io.File

/**
 * Where a screenshot is written. The build points this at
 * `android/app/build/outputs/roborazzi`, so the kit images sit beside the screen images of the
 * phone application; a run started by hand falls back to the module's own build folder.
 */
internal fun screenshotPath(name: String): String {
    val directory = System.getProperty("wakeel.screenshot.dir")
        ?: File("build/outputs/roborazzi").absolutePath
    File(directory).mkdirs()
    return File(directory, name).absolutePath
}
