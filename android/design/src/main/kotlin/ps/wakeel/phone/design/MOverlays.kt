package ps.wakeel.phone.design

import androidx.annotation.DrawableRes
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp

/**
 * One choice in a bottom sheet: a round icon over its caption. The design gives each choice its
 * own soft circle — money green, sound blue, a task amber — so [tone] names which of the kit's
 * tones the circle and the icon take; without one the choice wears the main colour.
 */
data class MSheetOption(
    val label: String,
    @param:DrawableRes val icon: Int,
    val tone: MCardTone = MCardTone.Plain,
    val onClick: () -> Unit = {},
)

/**
 * M.Sheet — the bottom sheet: a handle, a title and the options laid out three to a row. It
 * is drawn as content, so a screen can place it inside its own scrim layer.
 */
@Composable
fun MSheet(
    title: String,
    options: List<MSheetOption>,
    modifier: Modifier = Modifier,
) {
    val colors = WakeelTheme.colors
    Column(
        modifier = modifier
            .fillMaxWidth()
            .background(colors.surface, WakeelTheme.shapes.sheet)
            .padding(horizontal = 16.dp, vertical = 12.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Box(Modifier.width(32.dp).height(4.dp).background(colors.borderStrong, CircleShape))
        Text(
            text = Bidi.wrap(title),
            style = WakeelTheme.typography.sectionTitle,
            color = colors.text,
            modifier = Modifier.fillMaxWidth(),
            textAlign = TextAlign.Start,
        )
        options.chunked(3).forEach { row ->
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                row.forEach { option ->
                    Column(
                        modifier = Modifier.weight(1f).clickableRow(onClick = option.onClick).padding(vertical = 8.dp),
                        horizontalAlignment = Alignment.CenterHorizontally,
                        verticalArrangement = Arrangement.spacedBy(6.dp),
                    ) {
                        val fill: androidx.compose.ui.graphics.Color
                        val accent: androidx.compose.ui.graphics.Color
                        when (option.tone) {
                            MCardTone.Plain -> { fill = colors.primarySoft; accent = colors.primary }
                            MCardTone.Info -> { fill = colors.infoSoft; accent = colors.info }
                            MCardTone.Warning -> { fill = colors.warningSoft; accent = colors.warning }
                            MCardTone.Danger -> { fill = colors.dangerSoft; accent = colors.danger }
                            MCardTone.Success -> { fill = colors.successSoft; accent = colors.success }
                        }
                        Box(
                            modifier = Modifier.size(48.dp).background(fill, CircleShape),
                            contentAlignment = Alignment.Center,
                        ) {
                            MIcon(option.icon, null, size = 22.dp, tint = accent)
                        }
                        Text(
                            text = Bidi.wrap(option.label),
                            style = WakeelTheme.typography.meta,
                            color = colors.text,
                            textAlign = TextAlign.Center,
                            maxLines = 2,
                        )
                    }
                }
                repeat(3 - row.size) { Box(Modifier.weight(1f)) }
            }
        }
    }
}

/**
 * M.Dialog — three hundred and forty points wide with a title, a body and text buttons. The
 * confirming button takes the danger colour when [danger] is set.
 */
@Composable
fun MDialog(
    title: String,
    body: String,
    confirmText: String,
    onConfirm: () -> Unit,
    modifier: Modifier = Modifier,
    cancelText: String? = null,
    onCancel: (() -> Unit)? = null,
    danger: Boolean = false,
) {
    val colors = WakeelTheme.colors
    Column(
        modifier = modifier
            .widthIn(max = 340.dp)
            .background(colors.surface, WakeelTheme.shapes.dialog)
            .padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Text(text = Bidi.wrap(title), style = WakeelTheme.typography.screenTitle, color = colors.text)
        Text(text = Bidi.wrap(body), style = WakeelTheme.typography.bodyLarge, color = colors.muted)
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(8.dp, Alignment.End),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            if (!cancelText.isNullOrBlank() && onCancel != null) {
                MButton(cancelText, onCancel, kind = MButtonKind.Text)
            }
            MButton(
                text = confirmText,
                onClick = onConfirm,
                kind = if (danger) MButtonKind.Danger else MButtonKind.Text,
            )
        }
    }
}

/**
 * M.Snackbar — a short dark strip with a message and one action, the way the product confirms
 * that something was saved or queued for the next exchange.
 */
@Composable
fun MSnackbar(
    message: String,
    modifier: Modifier = Modifier,
    actionText: String? = null,
    onAction: (() -> Unit)? = null,
) {
    val colors = WakeelTheme.colors
    Row(
        modifier = modifier
            .fillMaxWidth()
            .heightIn(min = 48.dp)
            .background(colors.text, RoundedCornerShape(10.dp))
            .padding(horizontal = 16.dp, vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Text(
            text = Bidi.wrap(message),
            style = WakeelTheme.typography.body,
            color = colors.surface,
            modifier = Modifier.weight(1f),
        )
        if (!actionText.isNullOrBlank() && onAction != null) {
            Text(
                text = Bidi.wrap(actionText),
                style = WakeelTheme.typography.body,
                color = colors.primarySoft,
                modifier = Modifier.clickableRow(onClick = onAction),
            )
        }
    }
}

/**
 * M.StateView — the empty, error and not-paired states: a large icon in a soft circle, a
 * title, an explanation and one outlined action.
 */
@Composable
fun MStateView(
    title: String,
    description: String,
    modifier: Modifier = Modifier,
    @DrawableRes icon: Int = WakeelIcons.Inbox,
    actionText: String? = null,
    onAction: (() -> Unit)? = null,
) {
    val colors = WakeelTheme.colors
    Column(
        modifier = modifier.fillMaxWidth().padding(24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Box(
            modifier = Modifier.size(56.dp).background(colors.neutralSoft, CircleShape),
            contentAlignment = Alignment.Center,
        ) {
            MIcon(icon, null, size = 26.dp, tint = colors.muted)
        }
        Text(
            text = Bidi.wrap(title),
            style = WakeelTheme.typography.sectionTitle,
            color = colors.text,
            textAlign = TextAlign.Center,
        )
        Text(
            text = Bidi.wrap(description),
            style = WakeelTheme.typography.body,
            color = colors.muted,
            textAlign = TextAlign.Center,
        )
        if (!actionText.isNullOrBlank() && onAction != null) {
            MButton(actionText, onAction, kind = MButtonKind.Outlined)
        }
    }
}
