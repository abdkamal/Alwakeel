package ps.wakeel.phone.design

import androidx.annotation.DrawableRes
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp

/**
 * M.TopBar — 412×64 over the surface colour with a rule underneath. The screen title sits at
 * the right next to the back or menu button; the search and overflow buttons sit at the left.
 * The optional office line belongs to this component, not to the screen.
 */
@Composable
fun MTopBar(
    title: String,
    modifier: Modifier = Modifier,
    orgLine: String? = null,
    @DrawableRes navigationIcon: Int = WakeelIcons.Menu,
    navigationDescription: String = "",
    onNavigation: (() -> Unit)? = null,
    onSearch: (() -> Unit)? = null,
    searchDescription: String = "",
    onMore: (() -> Unit)? = null,
    moreDescription: String = "",
) {
    val colors = WakeelTheme.colors
    Column(modifier.fillMaxWidth().background(colors.surface)) {
        // A bar is a physical strip: the title and the navigation button belong at the right
        // edge of the glass in this product, so the row is placed left to right on purpose and
        // the text inside it keeps its own right to left rendering.
        CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
            Row(
                modifier = Modifier.fillMaxWidth().height(64.dp).padding(horizontal = 8.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                if (onSearch != null) {
                    MIconButton(WakeelIcons.Search, searchDescription, onSearch)
                }
                if (onMore != null) {
                    MIconButton(WakeelIcons.MoreVertical, moreDescription, onMore)
                }
                Spacer(Modifier.weight(1f))
                CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Rtl) {
                    Text(
                        text = Bidi.wrap(title),
                        style = WakeelTheme.typography.screenTitle,
                        color = colors.text,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                        textAlign = TextAlign.End,
                        modifier = Modifier.padding(horizontal = 8.dp),
                    )
                }
                MIconButton(navigationIcon, navigationDescription, onNavigation ?: {})
            }
        }
        if (!orgLine.isNullOrBlank()) {
            Text(
                text = Bidi.wrap(orgLine),
                style = WakeelTheme.typography.nav,
                color = colors.muted,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                textAlign = TextAlign.End,
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 16.dp)
                    .padding(bottom = 6.dp),
            )
        }
        MDivider()
    }
}

/**
 * M.OrgHeader — 412×56 in the dark sidebar colour with the organisation mark on the right,
 * the organisation name above the office name. It heads the home screen and the settings.
 */
@Composable
fun MOrgHeader(
    orgName: String,
    officeName: String,
    modifier: Modifier = Modifier,
) {
    val colors = WakeelTheme.colors
    Row(
        modifier = modifier
            .fillMaxWidth()
            .height(56.dp)
            .background(colors.sidebar)
            .padding(horizontal = 16.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Box(
            modifier = Modifier.size(32.dp).background(colors.white, RoundedCornerShape(8.dp)),
            contentAlignment = Alignment.Center,
        ) {
            MIcon(WakeelIcons.Landmark, null, size = 18.dp, tint = colors.sidebar)
        }
        Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(
                text = Bidi.wrap(orgName),
                style = WakeelTheme.typography.body.copy(fontWeight = androidx.compose.ui.text.font.FontWeight.Bold),
                color = colors.white,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = Bidi.wrap(officeName),
                style = WakeelTheme.typography.nav,
                color = colors.sidebarMuted,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

/** One destination of the bottom navigation, with the count on its badge. */
data class MNavItem(
    val label: String,
    @param:DrawableRes val icon: Int,
    val badge: Int = 0,
)

/**
 * M.BottomNav — 412×80 with five equal destinations. The active one carries a soft capsule
 * behind its icon and its label in the primary colour. The order in [items] is the reading
 * order, so the first item ends up at the right edge, which is where the design puts home.
 */
@Composable
fun MBottomNav(
    items: List<MNavItem>,
    selectedIndex: Int,
    onSelect: (Int) -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = WakeelTheme.colors
    Column(modifier.fillMaxWidth().background(colors.surface)) {
        MDivider()
        Row(
            modifier = Modifier.fillMaxWidth().height(79.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            items.forEachIndexed { index, item ->
                MBottomNavItem(
                    item = item,
                    selected = index == selectedIndex,
                    onClick = { onSelect(index) },
                    modifier = Modifier.weight(1f),
                )
            }
        }
    }
}

@Composable
private fun MBottomNavItem(
    item: MNavItem,
    selected: Boolean,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = WakeelTheme.colors
    Column(
        modifier = modifier.clickableRow(onClick = onClick).padding(vertical = 8.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(4.dp),
    ) {
        Box(contentAlignment = Alignment.Center) {
            Box(
                modifier = Modifier
                    .height(32.dp)
                    .then(if (selected) Modifier.width(64.dp) else Modifier.width(32.dp))
                    .background(
                        color = if (selected) colors.primarySoft else androidx.compose.ui.graphics.Color.Transparent,
                        shape = RoundedCornerShape(16.dp),
                    ),
                contentAlignment = Alignment.Center,
            ) {
                MIcon(
                    icon = item.icon,
                    contentDescription = item.label,
                    size = 24.dp,
                    tint = if (selected) colors.primary else colors.muted,
                )
            }
            if (item.badge > 0) {
                // The badge rides on the upper outer corner of the icon, which in a right to
                // left bar is its upper left.
                MBadge(item.badge, Modifier.align(Alignment.TopStart).offset(x = (-6).dp, y = (-4).dp))
            }
        }
        Text(
            text = Bidi.wrap(item.label),
            style = if (selected) {
                WakeelTheme.typography.nav.copy(fontWeight = androidx.compose.ui.text.font.FontWeight.SemiBold)
            } else {
                WakeelTheme.typography.nav
            },
            color = if (selected) colors.primary else colors.muted,
            maxLines = 1,
        )
    }
}

/** A round status dot, used by the sync row and the connection state. */
@Composable
internal fun MStatusDot(color: androidx.compose.ui.graphics.Color, modifier: Modifier = Modifier) {
    Box(modifier.size(8.dp).background(color, CircleShape))
}
