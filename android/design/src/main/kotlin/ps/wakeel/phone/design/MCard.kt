package ps.wakeel.phone.design

import androidx.annotation.DrawableRes
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp

/** The five card tones of the kit: the plain card and the four meaning-bearing variants. */
enum class MCardTone { Plain, Info, Warning, Danger, Success }

/**
 * M.Card — a bordered panel with a title and a body, stacked with a ten point gap. The
 * variants Info, Warning, Danger and Success take the soft fill, the matching border and the
 * icon that belongs to that meaning, exactly as `docs/design/ANDROID-KIT.md` describes them.
 */
@Composable
fun MCard(
    modifier: Modifier = Modifier,
    title: String? = null,
    tone: MCardTone = MCardTone.Plain,
    @DrawableRes icon: Int? = null,
    onClick: (() -> Unit)? = null,
    content: @Composable ColumnScope.() -> Unit = {},
) {
    val colors = WakeelTheme.colors
    val fill: Color
    val stroke: Color
    val accent: Color
    val toneIcon: Int?
    when (tone) {
        MCardTone.Plain -> {
            fill = colors.surface; stroke = colors.border; accent = colors.text; toneIcon = null
        }
        MCardTone.Info -> {
            fill = colors.infoSoft; stroke = colors.infoBorder; accent = colors.info; toneIcon = WakeelIcons.Info
        }
        MCardTone.Warning -> {
            fill = colors.warningSoft; stroke = colors.warningBorder; accent = colors.warning
            toneIcon = WakeelIcons.AlertTriangle
        }
        MCardTone.Danger -> {
            fill = colors.dangerSoft; stroke = colors.dangerBorder; accent = colors.danger
            toneIcon = WakeelIcons.AlertTriangle
        }
        MCardTone.Success -> {
            fill = colors.successSoft; stroke = colors.successBorder; accent = colors.success
            toneIcon = WakeelIcons.CheckCircle
        }
    }

    Column(
        modifier = modifier
            .fillMaxWidth()
            .background(fill, WakeelTheme.shapes.card)
            .border(1.dp, stroke, WakeelTheme.shapes.card)
            .clickableRow(onClick = onClick)
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        val shownIcon = icon ?: toneIcon
        if (!title.isNullOrBlank() || shownIcon != null) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                if (shownIcon != null) {
                    MIcon(shownIcon, null, size = 18.dp, tint = accent)
                }
                if (!title.isNullOrBlank()) {
                    Text(
                        text = Bidi.wrap(title),
                        style = WakeelTheme.typography.cardTitle,
                        color = if (tone == MCardTone.Plain) colors.text else accent,
                        modifier = Modifier.weight(1f),
                    )
                }
            }
        }
        content()
    }
}

/** The body line of a card; a card never styles its own text by hand. */
@Composable
fun MCardText(text: String, modifier: Modifier = Modifier) {
    Text(
        text = Bidi.wrap(text),
        style = WakeelTheme.typography.body,
        color = WakeelTheme.colors.muted,
        modifier = modifier.fillMaxWidth(),
    )
}

/**
 * M.SegmentedRow — three small indicators of equal width, each a number over its label. The
 * home screen and the reports use it to show counts at a glance.
 */
@Composable
fun MSegmentedRow(
    items: List<Pair<String, String>>,
    modifier: Modifier = Modifier,
) {
    val colors = WakeelTheme.colors
    Row(
        modifier = modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        items.forEach { (value, label) ->
            Column(
                modifier = Modifier
                    .weight(1f)
                    .background(colors.surface, WakeelTheme.shapes.card)
                    .border(1.dp, colors.border, WakeelTheme.shapes.card)
                    .padding(vertical = 12.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(2.dp),
            ) {
                Text(
                    text = Bidi.isolate(value),
                    style = WakeelTheme.typography.kpi,
                    color = colors.text,
                )
                Text(
                    text = Bidi.wrap(label),
                    style = WakeelTheme.typography.meta,
                    color = colors.muted,
                    maxLines = 1,
                )
            }
        }
    }
}
