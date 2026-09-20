package ps.wakeel.phone.design

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

/** One tab: its Arabic label and the count on its badge. */
data class MTab(val label: String, val badge: Int = 0)

/**
 * M.Tabs — the tab strip across the full width. The active tab is underlined in the primary
 * colour; a tab that carries a count shows an M.Badge beside its label.
 */
@Composable
fun MTabs(
    tabs: List<MTab>,
    selectedIndex: Int,
    onSelect: (Int) -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = WakeelTheme.colors
    Column(modifier.fillMaxWidth().background(colors.surface)) {
        Row(Modifier.fillMaxWidth()) {
            tabs.forEachIndexed { index, tab ->
                val selected = index == selectedIndex
                Column(
                    modifier = Modifier
                        .weight(1f)
                        .clickableRow(onClick = { onSelect(index) }),
                    horizontalAlignment = Alignment.CenterHorizontally,
                ) {
                    Row(
                        modifier = Modifier.padding(vertical = 12.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(6.dp),
                    ) {
                        Text(
                            text = Bidi.wrap(tab.label),
                            style = WakeelTheme.typography.bodyLarge.copy(
                                fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Normal,
                            ),
                            color = if (selected) colors.primary else colors.muted,
                            maxLines = 1,
                        )
                        MBadge(tab.badge)
                    }
                    Box(
                        Modifier
                            .fillMaxWidth()
                            .height(2.dp)
                            .background(if (selected) colors.primary else Color.Transparent),
                    )
                }
            }
        }
        MDivider()
    }
}
