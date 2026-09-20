package ps.wakeel.phone.design

import androidx.compose.foundation.clickable
import androidx.compose.ui.Modifier

/**
 * Makes a row, card or button tappable only when there is something to do. A component that
 * takes an optional `onClick` stays inert — and keeps its ripple off — when none was given.
 */
internal fun Modifier.clickableRow(enabled: Boolean = true, onClick: (() -> Unit)?): Modifier =
    if (onClick == null) this else this.clickable(enabled = enabled) { onClick() }
