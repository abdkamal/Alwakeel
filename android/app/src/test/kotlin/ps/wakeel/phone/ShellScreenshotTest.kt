package ps.wakeel.phone

import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onRoot
import androidx.compose.ui.test.performClick
import com.github.takahirom.roborazzi.captureRoboImage
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode
import java.io.File

/**
 * The shell of the application at the size every phone screen is drawn at: the frame, the bar
 * and the five destinations, in both themes. The screen packages that follow are compared
 * against their own design previews inside this same frame.
 */
@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(sdk = [35], qualifiers = "w412dp-h915dp-mdpi")
class ShellScreenshotTest {

    @get:Rule
    val composeRule = createComposeRule()

    @Test
    fun shellLight() {
        composeRule.setContent { WakeelShell() }
        composeRule.onRoot().captureRoboImage(path("shell-light.png"))
    }

    @Test
    fun shellDark() {
        // The dark scheme is the system's choice, so the test asks the system for it rather
        // than reaching past the theme.
        org.robolectric.RuntimeEnvironment.setQualifiers("+night")
        composeRule.setContent { WakeelShell() }
        composeRule.onRoot().captureRoboImage(path("shell-dark.png"))
    }

    @Test
    fun theBottomBarMovesBetweenTheFiveDestinations() {
        composeRule.setContent { WakeelShell() }
        // Only the bar carries the word to begin with; once it has been tapped the title in the
        // bar above carries it too, which is how the test knows the host really moved.
        composeRule.onAllNodesWithText("المهام").assertCountEquals(1)
        composeRule.onAllNodesWithText("المهام")[0].performClick()
        composeRule.onAllNodesWithText("المهام").assertCountEquals(2)
        composeRule.onRoot().captureRoboImage(path("shell-tasks.png"))
    }

    private fun path(name: String): String {
        val directory = System.getProperty("wakeel.screenshot.dir") ?: "build/outputs/roborazzi"
        File(directory).mkdirs()
        return File(directory, name).absolutePath
    }
}
