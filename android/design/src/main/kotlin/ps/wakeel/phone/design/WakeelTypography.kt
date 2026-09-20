package ps.wakeel.phone.design

import androidx.compose.runtime.Immutable
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.LineHeightStyle
import androidx.compose.ui.text.style.TextDirection
import androidx.compose.ui.unit.sp

/**
 * Noto Sans Arabic is bundled because it is not installed on the phones this product runs on.
 * The four weights are the ones the design uses: 400 body, 500 labels, 600 emphasis, 700 titles.
 */
val NotoSansArabic: FontFamily = FontFamily(
    Font(R.font.noto_sans_arabic_regular, FontWeight.Normal),
    Font(R.font.noto_sans_arabic_medium, FontWeight.Medium),
    Font(R.font.noto_sans_arabic_semibold, FontWeight.SemiBold),
    Font(R.font.noto_sans_arabic_bold, FontWeight.Bold),
)

/**
 * Arabic runs right to left while numbers and Latin fragments inside it run left to right.
 * Giving every style `TextDirection.Content` lets the paragraph take its direction from what
 * it actually contains, which is the Compose equivalent of `unicode-bidi: plaintext`.
 */
private fun wakeelStyle(size: Int, weight: FontWeight, lineHeight: Int): TextStyle = TextStyle(
    fontFamily = NotoSansArabic,
    fontSize = size.sp,
    fontWeight = weight,
    lineHeight = lineHeight.sp,
    lineHeightStyle = LineHeightStyle(
        alignment = LineHeightStyle.Alignment.Center,
        trim = LineHeightStyle.Trim.None,
    ),
)

/**
 * The type ramp of the phone kit. The names say what the text is, not how big it is, so a
 * screen never picks a size by hand.
 */
@Immutable
data class WakeelTypography(
    /** 11 — bottom navigation labels, the office line under a title. */
    val nav: TextStyle = wakeelStyle(11, FontWeight.Normal, 14),

    /** 11/700 — the number inside a badge. */
    val badge: TextStyle = wakeelStyle(11, FontWeight.Bold, 13),

    /** 12 — field labels, helper lines, secondary values. */
    val meta: TextStyle = wakeelStyle(12, FontWeight.Normal, 16),

    /** 13 — second line of a list item, card body, chip text. */
    val body: TextStyle = wakeelStyle(13, FontWeight.Normal, 19),

    /** 14 — dialog body and tab labels. */
    val bodyLarge: TextStyle = wakeelStyle(14, FontWeight.Normal, 20),

    /** 15/600 — list item titles, button labels, field values. */
    val itemTitle: TextStyle = wakeelStyle(15, FontWeight.SemiBold, 21),

    /** 15/700 — card titles. */
    val cardTitle: TextStyle = wakeelStyle(15, FontWeight.Bold, 21),

    /** 16/700 — the title of a state view. */
    val sectionTitle: TextStyle = wakeelStyle(16, FontWeight.Bold, 22),

    /** 18/700 — the screen title in the top bar and the dialog title. */
    val screenTitle: TextStyle = wakeelStyle(18, FontWeight.Bold, 24),

    /** 22/700 — the number of a small indicator. */
    val kpi: TextStyle = wakeelStyle(22, FontWeight.Bold, 28),

    /** 32/700 — the amount field. */
    val amount: TextStyle = wakeelStyle(32, FontWeight.Bold, 40),
) {
    /** Applies the content direction rule to every style at once. */
    internal fun withContentDirection(): WakeelTypography = copy(
        nav = nav.contentDirection(),
        badge = badge.contentDirection(),
        meta = meta.contentDirection(),
        body = body.contentDirection(),
        bodyLarge = bodyLarge.contentDirection(),
        itemTitle = itemTitle.contentDirection(),
        cardTitle = cardTitle.contentDirection(),
        sectionTitle = sectionTitle.contentDirection(),
        screenTitle = screenTitle.contentDirection(),
        kpi = kpi.contentDirection(),
        amount = amount.contentDirection(),
    )
}

private fun TextStyle.contentDirection(): TextStyle = copy(textDirection = TextDirection.Content)
