package ps.wakeel.phone.design

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxScope
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp

/**
 * The frame every phone screen is built in, straight out of `docs/design/ANDROID-KIT.md`:
 * the status strip, the bar, a scrolling content area with the kit's own padding and gap, and
 * the bottom navigation. The action button floats over the lower reading corner.
 *
 * A screen fills [content] with kit composables and nothing else; it never lays out its own
 * bars, padding or background.
 */
@Composable
fun MPhoneScreen(
    modifier: Modifier = Modifier,
    statusBar: @Composable () -> Unit = { MStatusBar() },
    // The dark organisation band of the design sits between the status strip and the bar on
    // every screen that knows which office the device belongs to; it stays empty until the
    // device is paired and the name is known.
    orgHeader: @Composable () -> Unit = {},
    topBar: @Composable () -> Unit = {},
    bottomBar: @Composable () -> Unit = {},
    fab: @Composable (() -> Unit)? = null,
    scrollable: Boolean = true,
    overlay: @Composable (BoxScope.() -> Unit)? = null,
    content: @Composable ColumnScope.() -> Unit,
) {
    val colors = WakeelTheme.colors
    val spacing = WakeelTheme.spacing
    Box(modifier.fillMaxSize().background(colors.bg)) {
        Column(Modifier.fillMaxSize()) {
            statusBar()
            orgHeader()
            topBar()
            Column(
                modifier = Modifier
                    .weight(1f)
                    .fillMaxWidth()
                    .then(if (scrollable) Modifier.verticalScroll(rememberScrollState()) else Modifier)
                    .padding(horizontal = spacing.screenHorizontal, vertical = spacing.screenVertical),
                verticalArrangement = Arrangement.spacedBy(spacing.contentGap),
                content = content,
            )
            bottomBar()
        }
        if (fab != null) {
            Box(
                modifier = Modifier
                    // The kit fixes the action button at x = 16, y = 815 in the 412×915 frame:
                    // sixteen points from the far edge of the reading direction — the left in
                    // Arabic — and forty four points from the bottom, so it rides over the last
                    // item of the bar exactly as the design shows it.
                    .align(Alignment.BottomEnd)
                    .padding(end = 16.dp, bottom = 44.dp),
            ) {
                fab()
            }
        }
        if (overlay != null) {
            Box(Modifier.fillMaxSize().background(colors.scrim), content = overlay)
        }
    }
}
