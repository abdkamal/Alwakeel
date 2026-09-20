package ps.wakeel.phone.design

import androidx.annotation.DrawableRes
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import androidx.compose.runtime.CompositionLocalProvider

/**
 * One Lucide icon, tinted. Every icon in the product goes through here so no screen reaches
 * for a drawable by hand.
 */
@Composable
fun MIcon(
    @DrawableRes icon: Int,
    contentDescription: String? = null,
    modifier: Modifier = Modifier,
    size: Dp = 24.dp,
    tint: Color = WakeelTheme.colors.text,
) {
    Icon(
        painter = painterResource(icon),
        contentDescription = contentDescription,
        modifier = modifier.size(size),
        tint = tint,
    )
}

/** A one pixel rule in the border colour, the divider the kit draws under rows and bars. */
@Composable
fun MDivider(modifier: Modifier = Modifier, color: Color = WakeelTheme.colors.border) {
    Box(modifier.fillMaxWidth().height(1.dp).background(color))
}

/**
 * M.Badge — a red circle of eighteen with a white number. Counts above ninety nine are shown
 * as "+99" so the circle never stretches into an oval. Digits are always Western (0-9).
 */
@Composable
fun MBadge(count: Int, modifier: Modifier = Modifier) {
    if (count <= 0) return
    val text = if (count > 99) "+99" else count.toString()
    Box(
        modifier = modifier
            .size(18.dp)
            .background(WakeelTheme.colors.danger, CircleShape),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = Bidi.isolate(text),
            style = WakeelTheme.typography.badge,
            color = WakeelTheme.colors.white,
        )
    }
}

/**
 * M.StatusBar — 412×44 with the time on the left and the battery, signal and connection icons
 * on the right. On a real phone the system draws this; the kit carries it so a preview and a
 * screenshot show the same frame as the design.
 */
@Composable
fun MStatusBar(
    time: String = "10:24",
    connected: Boolean = false,
    modifier: Modifier = Modifier,
) {
    val colors = WakeelTheme.colors
    // The status bar is a physical strip: the clock sits at the left edge of the glass whatever
    // the reading direction is, so it is laid out left to right on purpose.
    CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
        Row(
            modifier = modifier
                .fillMaxWidth()
                .height(44.dp)
                .background(colors.surface)
                .padding(horizontal = 16.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                text = Bidi.isolate(time),
                style = WakeelTheme.typography.meta,
                color = colors.text,
            )
            Spacer(Modifier.weight(1f))
            Row(
                horizontalArrangement = Arrangement.spacedBy(6.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                MIcon(if (connected) WakeelIcons.Usb else WakeelIcons.WifiOff, null, size = 14.dp, tint = colors.text)
                MIcon(WakeelIcons.Signal, null, size = 14.dp, tint = colors.text)
                MIcon(WakeelIcons.Battery, null, size = 14.dp, tint = colors.text)
            }
        }
    }
}

/** A round 40×40 tap target for a bar icon, the size the kit gives every bar button. */
@Composable
internal fun MIconButton(
    @DrawableRes icon: Int,
    contentDescription: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    tint: Color = WakeelTheme.colors.text,
    enabled: Boolean = true,
) {
    Box(
        modifier = modifier
            .size(40.dp)
            .clickableRow(enabled = enabled, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        MIcon(icon, contentDescription, size = 20.dp, tint = tint)
    }
}

/** A fixed width spacer, for the few places the kit sets an exact gap. */
@Composable
internal fun MGap(width: Dp) = Spacer(Modifier.width(width))
