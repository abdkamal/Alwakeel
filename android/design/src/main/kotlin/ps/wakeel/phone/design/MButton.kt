package ps.wakeel.phone.design

import androidx.annotation.DrawableRes
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp

/** The four button kinds of the kit. */
enum class MButtonKind { Filled, Outlined, Text, Danger }

/**
 * M.Button — forty four points tall with fully rounded ends. The optional icon sits beside
 * the label, on the side the reading direction puts it.
 */
@Composable
fun MButton(
    text: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    kind: MButtonKind = MButtonKind.Filled,
    @DrawableRes icon: Int? = null,
    enabled: Boolean = true,
) {
    val colors = WakeelTheme.colors
    val fill = when {
        !enabled -> colors.disabledBg
        kind == MButtonKind.Filled -> colors.primaryFill
        kind == MButtonKind.Danger -> colors.dangerFill
        else -> Color.Transparent
    }
    val label = when {
        !enabled -> colors.disabled
        kind == MButtonKind.Filled || kind == MButtonKind.Danger -> colors.onFill
        kind == MButtonKind.Outlined -> colors.primary
        else -> colors.primary
    }
    val stroke = when {
        !enabled && kind == MButtonKind.Outlined -> colors.disabled
        kind == MButtonKind.Outlined -> colors.primary
        else -> null
    }

    Row(
        modifier = modifier
            .height(44.dp)
            .widthIn(min = 88.dp)
            .background(fill, WakeelTheme.shapes.button)
            .then(stroke?.let { Modifier.border(1.dp, it, WakeelTheme.shapes.button) } ?: Modifier)
            .clickableRow(enabled = enabled, onClick = onClick)
            .padding(horizontal = if (kind == MButtonKind.Text) 12.dp else 20.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp, Alignment.CenterHorizontally),
    ) {
        Text(
            text = Bidi.wrap(text),
            style = WakeelTheme.typography.itemTitle,
            color = label,
            maxLines = 1,
        )
        if (icon != null) {
            MIcon(icon, null, size = 18.dp, tint = label)
        }
    }
}

/**
 * M.FAB — the round action button. [text] turns it into M.FAB.Extended, which is the form the
 * home screen uses for "إدخال سريع".
 */
@Composable
fun MFab(
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    text: String? = null,
    @DrawableRes icon: Int = WakeelIcons.Plus,
    contentDescription: String = "",
) {
    val colors = WakeelTheme.colors
    if (text.isNullOrBlank()) {
        Box(
            modifier = modifier
                .size(56.dp)
                .background(colors.primaryFill, CircleShape)
                .clickableRow(onClick = onClick),
            contentAlignment = Alignment.Center,
        ) {
            MIcon(icon, contentDescription, size = 24.dp, tint = colors.onFill)
        }
    } else {
        Row(
            modifier = modifier
                .height(56.dp)
                .background(colors.primaryFill, CircleShape)
                .clickableRow(onClick = onClick)
                .padding(horizontal = 20.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            MIcon(icon, contentDescription, size = 22.dp, tint = colors.onFill)
            Text(
                text = Bidi.wrap(text),
                style = WakeelTheme.typography.itemTitle,
                color = colors.onFill,
                maxLines = 1,
            )
        }
    }
}

/**
 * M.Chip — a thirty two point capsule. The selected form fills with the soft primary colour,
 * writes its text in the primary colour and shows a check.
 */
@Composable
fun MChip(
    text: String,
    modifier: Modifier = Modifier,
    selected: Boolean = false,
    count: Int? = null,
    onClick: (() -> Unit)? = null,
) {
    val colors = WakeelTheme.colors
    Row(
        modifier = modifier
            .height(32.dp)
            .background(if (selected) colors.primarySoft else colors.surface, WakeelTheme.shapes.pill)
            .border(1.dp, if (selected) colors.primaryBorder else colors.border, WakeelTheme.shapes.pill)
            .clickableRow(onClick = onClick)
            .padding(horizontal = 12.dp, vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        if (selected) {
            MIcon(WakeelIcons.Check, null, size = 14.dp, tint = colors.primary)
        }
        Text(
            text = Bidi.wrap(text),
            style = WakeelTheme.typography.body,
            color = if (selected) colors.primary else colors.text,
            maxLines = 1,
        )
        if (count != null && count > 0) {
            Text(
                text = Bidi.isolate(count.toString()),
                style = WakeelTheme.typography.meta,
                color = if (selected) colors.primary else colors.muted,
            )
        }
    }
}
