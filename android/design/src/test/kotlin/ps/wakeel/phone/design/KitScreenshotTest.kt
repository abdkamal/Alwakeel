package ps.wakeel.phone.design

import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onRoot
import com.github.takahirom.roborazzi.captureRoboImage
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode

/**
 * Renders the whole phone kit through the real Android graphics stack and writes one image per
 * theme. The images are what a reviewer puts beside the design previews, so they are drawn at
 * medium density: four hundred and twelve device independent points come out as four hundred
 * and twelve pixels, the same width the design file uses.
 */
@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
// The sheet is taller than a phone because it carries the whole kit: the window is made tall
// enough for the last composable of the gallery — the bottom bar — to be drawn in full rather
// than cut off at the edge of the image.
@Config(sdk = [35], qualifiers = "w412dp-h3400dp-mdpi")
class KitScreenshotTest {

    @get:Rule
    val composeRule = createComposeRule()

    @Test
    fun kitGalleryLight() {
        composeRule.setContent { WakeelTheme(darkTheme = false) { KitGallery() } }
        composeRule.onRoot().captureRoboImage(screenshotPath("kit-gallery-light.png"))
    }

    @Test
    fun kitGalleryDark() {
        composeRule.setContent { WakeelTheme(darkTheme = true) { KitGallery() } }
        composeRule.onRoot().captureRoboImage(screenshotPath("kit-gallery-dark.png"))
    }
}
