package ps.wakeel.phone.design

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.ReadOnlyComposable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.foundation.shape.RoundedCornerShape

/** The spacing scale of the phone kit; a screen only ever uses these steps. */
@Immutable
data class WakeelSpacing(
    val xs: Dp = 4.dp,
    val sm: Dp = 8.dp,
    val md: Dp = 10.dp,
    val lg: Dp = 12.dp,
    val xl: Dp = 16.dp,
    val xxl: Dp = 20.dp,
    val xxxl: Dp = 24.dp,

    /** The horizontal padding of a screen's content area (`docs/design/ANDROID-KIT.md`). */
    val screenHorizontal: Dp = 16.dp,

    /** The vertical padding of a screen's content area. */
    val screenVertical: Dp = 12.dp,

    /** The gap between two blocks inside the content area. */
    val contentGap: Dp = 12.dp,
)

/** The corner radii of the kit, by the thing they belong to. */
@Immutable
data class WakeelShapes(
    val small: Shape = RoundedCornerShape(6.dp),
    val field: Shape = RoundedCornerShape(10.dp),
    val card: Shape = RoundedCornerShape(12.dp),
    val sheet: Shape = RoundedCornerShape(topStart = 20.dp, topEnd = 20.dp),
    val dialog: Shape = RoundedCornerShape(20.dp),
    val pill: Shape = RoundedCornerShape(999.dp),
    val button: Shape = RoundedCornerShape(22.dp),
    val searchBar: Shape = RoundedCornerShape(24.dp),
)

val LocalWakeelColors = staticCompositionLocalOf { WakeelLightColors }
val LocalWakeelTypography = staticCompositionLocalOf { WakeelTypography() }
val LocalWakeelSpacing = staticCompositionLocalOf { WakeelSpacing() }
val LocalWakeelShapes = staticCompositionLocalOf { WakeelShapes() }

/** How a screen reaches the design tokens: `WakeelTheme.colors.primary`, and so on. */
object WakeelTheme {
    val colors: WakeelColors
        @Composable @ReadOnlyComposable get() = LocalWakeelColors.current

    val typography: WakeelTypography
        @Composable @ReadOnlyComposable get() = LocalWakeelTypography.current

    val spacing: WakeelSpacing
        @Composable @ReadOnlyComposable get() = LocalWakeelSpacing.current

    val shapes: WakeelShapes
        @Composable @ReadOnlyComposable get() = LocalWakeelShapes.current
}

/**
 * The theme of the whole application. The layout direction is right to left everywhere, not
 * per screen, so no screen can forget it; Material 3 is still configured underneath because a
 * few of its own surfaces (ripples, text selection) read the Material colour scheme.
 */
@Composable
fun WakeelTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit,
) {
    val colors = if (darkTheme) WakeelDarkColors else WakeelLightColors
    val typography = remembered(darkTheme)

    val materialScheme = if (darkTheme) {
        darkColorScheme(
            primary = colors.primary,
            onPrimary = colors.onFill,
            primaryContainer = colors.primarySoft,
            onPrimaryContainer = colors.primary,
            background = colors.bg,
            onBackground = colors.text,
            surface = colors.surface,
            onSurface = colors.text,
            surfaceVariant = colors.surface2,
            onSurfaceVariant = colors.muted,
            outline = colors.border,
            error = colors.danger,
            onError = colors.onFill,
        )
    } else {
        lightColorScheme(
            primary = colors.primary,
            onPrimary = colors.onFill,
            primaryContainer = colors.primarySoft,
            onPrimaryContainer = colors.primary,
            background = colors.bg,
            onBackground = colors.text,
            surface = colors.surface,
            onSurface = colors.text,
            surfaceVariant = colors.surface2,
            onSurfaceVariant = colors.muted,
            outline = colors.border,
            error = colors.danger,
            onError = colors.onFill,
        )
    }

    CompositionLocalProvider(
        LocalLayoutDirection provides LayoutDirection.Rtl,
        LocalWakeelColors provides colors,
        LocalWakeelTypography provides typography,
        LocalWakeelSpacing provides WakeelSpacing(),
        LocalWakeelShapes provides WakeelShapes(),
    ) {
        MaterialTheme(
            colorScheme = materialScheme,
            typography = Typography(
                bodyLarge = typography.bodyLarge,
                bodyMedium = typography.body,
                bodySmall = typography.meta,
                titleLarge = typography.screenTitle,
                titleMedium = typography.sectionTitle,
                titleSmall = typography.itemTitle,
                labelLarge = typography.itemTitle,
                labelMedium = typography.meta,
                labelSmall = typography.nav,
            ),
            content = content,
        )
    }
}

@Composable
private fun remembered(darkTheme: Boolean): WakeelTypography {
    // The ramp does not depend on the scheme, but building it once per theme change keeps the
    // content-direction copies out of every recomposition.
    return androidx.compose.runtime.remember(darkTheme) { WakeelTypography().withContentDirection() }
}
