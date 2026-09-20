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
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp

/**
 * M.ListItem — the one row of every list in the application: a round mark at the right, the
 * title and its second line beside it, a secondary value and a chevron at the left.
 *
 * [attention] switches to the M.ListItem.Attention variant: a three pixel danger edge on the
 * right and the warm attention background, which is how the product shows a row that is late
 * or waiting.
 */
@Composable
fun MListItem(
    title: String,
    modifier: Modifier = Modifier,
    subtitle: String? = null,
    trailingValue: String? = null,
    @DrawableRes leadingIcon: Int? = null,
    leadingText: String? = null,
    attention: Boolean = false,
    showChevron: Boolean = true,
    onClick: (() -> Unit)? = null,
) {
    val colors = WakeelTheme.colors
    Column(modifier.fillMaxWidth()) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .heightIn(min = 72.dp)
                .background(if (attention) colors.attentionBg else colors.surface)
                .clickableRow(onClick = onClick),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            if (attention) {
                // The edge is on the reading side, which in this application is the right.
                Box(Modifier.width(3.dp).height(72.dp).background(colors.danger))
            }
            Row(
                modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 12.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                MListItemMark(leadingIcon, leadingText)
                Column(
                    modifier = Modifier.weight(1f),
                    verticalArrangement = Arrangement.spacedBy(2.dp),
                ) {
                    Text(
                        text = Bidi.wrap(title),
                        style = WakeelTheme.typography.itemTitle,
                        color = colors.text,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                    if (!subtitle.isNullOrBlank()) {
                        Text(
                            text = Bidi.wrap(subtitle),
                            style = WakeelTheme.typography.body,
                            color = colors.muted,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                }
                // The design puts the chevron beside the text and the secondary value at the
                // far left, so the row reads title → chevron → value from the reading side.
                if (showChevron) {
                    MIcon(WakeelIcons.ChevronLeft, null, size = 16.dp, tint = colors.muted)
                }
                if (!trailingValue.isNullOrBlank()) {
                    Text(
                        text = Bidi.wrap(trailingValue),
                        style = WakeelTheme.typography.meta,
                        color = colors.muted,
                        maxLines = 1,
                    )
                }
            }
        }
        MDivider()
    }
}

@Composable
private fun MListItemMark(@DrawableRes icon: Int?, text: String?) {
    val colors = WakeelTheme.colors
    if (icon == null && text.isNullOrBlank()) {
        Spacer(Modifier.width(0.dp))
        return
    }
    Box(
        modifier = Modifier.size(40.dp).background(colors.primarySoft, CircleShape),
        contentAlignment = Alignment.Center,
    ) {
        if (icon != null) {
            MIcon(icon, null, size = 18.dp, tint = colors.primary)
        } else {
            Text(
                text = text!!.take(1),
                style = WakeelTheme.typography.itemTitle,
                color = colors.primary,
            )
        }
    }
}

/**
 * M.Avatar — a forty point circle in the soft primary colour with the first letter of the
 * name. [size] takes the seventy two point form the photo screen uses.
 */
@Composable
fun MAvatar(
    name: String,
    modifier: Modifier = Modifier,
    size: androidx.compose.ui.unit.Dp = 40.dp,
    photo: androidx.compose.ui.graphics.painter.Painter? = null,
) {
    val colors = WakeelTheme.colors
    Box(
        modifier = modifier.size(size).background(colors.primarySoft, CircleShape),
        contentAlignment = Alignment.Center,
    ) {
        if (photo != null) {
            androidx.compose.foundation.Image(
                painter = photo,
                contentDescription = name,
                modifier = Modifier.size(size).background(Color.Transparent, CircleShape),
                contentScale = androidx.compose.ui.layout.ContentScale.Crop,
            )
        } else {
            Text(
                text = name.trim().take(1),
                style = if (size >= 72.dp) WakeelTheme.typography.kpi else WakeelTheme.typography.itemTitle,
                color = colors.primary,
            )
        }
    }
}

/** M.SyncRow — the cable state, when the last exchange happened, and a status dot. */
@Composable
fun MSyncRow(
    text: String,
    modifier: Modifier = Modifier,
    connected: Boolean = false,
    onClick: (() -> Unit)? = null,
) {
    val colors = WakeelTheme.colors
    Row(
        modifier = modifier
            .fillMaxWidth()
            .background(colors.surface, WakeelTheme.shapes.card)
            .clickableRow(onClick = onClick)
            .padding(horizontal = 16.dp, vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        MIcon(WakeelIcons.Usb, null, size = 18.dp, tint = if (connected) colors.success else colors.muted)
        Text(
            text = Bidi.wrap(text),
            style = WakeelTheme.typography.body,
            color = colors.text,
            modifier = Modifier.weight(1f),
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        MStatusDot(if (connected) colors.success else colors.muted)
    }
}
