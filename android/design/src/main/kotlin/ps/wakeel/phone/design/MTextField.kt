package ps.wakeel.phone.design

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.material3.LocalTextStyle
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.foundation.text.KeyboardOptions

/**
 * M.TextField — a label, a fifty two point box and a helper line. [error] switches to the
 * M.TextField.Error variant: a danger border and the message in the danger colour.
 */
@Composable
fun MTextField(
    label: String,
    value: String,
    onValueChange: (String) -> Unit,
    modifier: Modifier = Modifier,
    placeholder: String = "",
    helper: String? = null,
    error: String? = null,
    enabled: Boolean = true,
    singleLine: Boolean = true,
    keyboardType: KeyboardType = KeyboardType.Text,
) {
    val colors = WakeelTheme.colors
    val hasError = !error.isNullOrBlank()
    Column(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text(
            text = Bidi.wrap(label),
            style = WakeelTheme.typography.meta,
            color = colors.muted,
        )
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .heightIn(min = 52.dp)
                .background(if (enabled) colors.surface else colors.disabledBg, WakeelTheme.shapes.field)
                .border(
                    width = 1.dp,
                    color = if (hasError) colors.danger else colors.borderStrong,
                    shape = WakeelTheme.shapes.field,
                )
                .padding(horizontal = 14.dp, vertical = 14.dp),
            contentAlignment = Alignment.CenterStart,
        ) {
            if (value.isEmpty() && placeholder.isNotEmpty()) {
                Text(
                    text = Bidi.wrap(placeholder),
                    style = WakeelTheme.typography.itemTitle,
                    color = colors.placeholder,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            BasicTextField(
                value = value,
                onValueChange = onValueChange,
                enabled = enabled,
                singleLine = singleLine,
                textStyle = WakeelTheme.typography.itemTitle.copy(color = colors.text),
                cursorBrush = SolidColor(colors.primary),
                keyboardOptions = KeyboardOptions(keyboardType = keyboardType),
                modifier = Modifier.fillMaxWidth(),
            )
        }
        if (hasError) {
            Text(
                text = Bidi.wrap(error),
                style = WakeelTheme.typography.meta,
                color = colors.danger,
            )
        } else if (!helper.isNullOrBlank()) {
            Text(
                text = Bidi.wrap(helper),
                style = WakeelTheme.typography.meta,
                color = colors.muted,
            )
        }
    }
}

/**
 * M.TextField.Amount — the shekel field. The number is written large and the sign sits beside
 * it, isolated so the two never swap places in a right to left line.
 */
@Composable
fun MAmountField(
    label: String,
    value: String,
    onValueChange: (String) -> Unit,
    modifier: Modifier = Modifier,
    helper: String? = null,
    error: String? = null,
) {
    val colors = WakeelTheme.colors
    val hasError = !error.isNullOrBlank()
    Column(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text(
            text = Bidi.wrap(label),
            style = WakeelTheme.typography.meta,
            color = colors.muted,
        )
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .heightIn(min = 72.dp)
                .background(colors.surface, WakeelTheme.shapes.field)
                .border(1.dp, if (hasError) colors.danger else colors.borderStrong, WakeelTheme.shapes.field)
                .padding(horizontal = 14.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Text(text = "₪", style = WakeelTheme.typography.amount, color = colors.muted)
            androidx.compose.runtime.CompositionLocalProvider(LocalTextStyle provides WakeelTheme.typography.amount) {
                BasicTextField(
                    value = value,
                    onValueChange = onValueChange,
                    singleLine = true,
                    textStyle = WakeelTheme.typography.amount.copy(color = colors.text),
                    cursorBrush = SolidColor(colors.primary),
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal),
                    modifier = Modifier.weight(1f),
                )
            }
        }
        if (hasError) {
            Text(text = Bidi.wrap(error), style = WakeelTheme.typography.meta, color = colors.danger)
        } else if (!helper.isNullOrBlank()) {
            Text(text = Bidi.wrap(helper), style = WakeelTheme.typography.meta, color = colors.muted)
        }
    }
}

/**
 * M.SearchBar — a forty eight point capsule over the second surface colour with the search
 * icon on the reading side and the placeholder the design wrote.
 */
@Composable
fun MSearchBar(
    value: String,
    onValueChange: (String) -> Unit,
    modifier: Modifier = Modifier,
    placeholder: String = "ابحث في هذه القائمة…",
) {
    val colors = WakeelTheme.colors
    Row(
        modifier = modifier
            .fillMaxWidth()
            .height(48.dp)
            .background(colors.surface2, WakeelTheme.shapes.searchBar)
            .padding(horizontal = 16.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        MIcon(WakeelIcons.Search, null, size = 18.dp, tint = colors.placeholder)
        Box(Modifier.weight(1f), contentAlignment = Alignment.CenterStart) {
            if (value.isEmpty()) {
                Text(
                    text = Bidi.wrap(placeholder),
                    style = WakeelTheme.typography.body,
                    color = colors.placeholder,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            BasicTextField(
                value = value,
                onValueChange = onValueChange,
                singleLine = true,
                textStyle = WakeelTheme.typography.body.copy(color = colors.text),
                cursorBrush = SolidColor(colors.primary),
                modifier = Modifier.fillMaxWidth(),
            )
        }
    }
}
