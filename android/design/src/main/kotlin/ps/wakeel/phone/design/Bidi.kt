package ps.wakeel.phone.design

import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.core.text.BidiFormatter
import androidx.core.text.TextDirectionHeuristicsCompat
import java.util.Locale

/**
 * Item 55 of the agreement: every piece of text that mixes Arabic with Latin letters, digits,
 * a file name or an amount has to keep each fragment on its own side, in every screen of the
 * product. Two tools do that here:
 *
 *  * [wrap] hands a whole string to [BidiFormatter], which adds the marks a right-to-left
 *    paragraph needs around a left-to-right fragment it contains.
 *  * [isolate] puts one value inside a first-strong isolate, which is what to use when a value
 *    is pasted into a sentence built elsewhere ("المرفق: <isolate>report-3.pdf</isolate>").
 *
 * Nothing else in the product is allowed to concatenate a value into Arabic text by hand.
 */
object Bidi {
    /** Unicode FIRST STRONG ISOLATE: the value decides its own direction. */
    const val FirstStrongIsolate: Char = '⁨'

    /** Unicode POP DIRECTIONAL ISOLATE: ends the isolate opened above. */
    const val PopDirectionalIsolate: Char = '⁩'

    /** Unicode RIGHT-TO-LEFT MARK, used to anchor a trailing neutral character. */
    const val RightToLeftMark: Char = '‏'

    private val arabic: BidiFormatter = BidiFormatter.getInstance(Locale("ar"))

    /**
     * Returns the text with the direction marks a right-to-left paragraph needs. An empty or
     * blank string is returned untouched, so a placeholder never gains an invisible character.
     */
    fun wrap(text: String?): String {
        if (text.isNullOrEmpty()) return text.orEmpty()
        return arabic.unicodeWrap(text, TextDirectionHeuristicsCompat.FIRSTSTRONG_LTR)
    }

    /** Wraps one value in a first-strong isolate, for pasting into a sentence. */
    fun isolate(value: String?): String {
        if (value.isNullOrEmpty()) return value.orEmpty()
        return "$FirstStrongIsolate$value$PopDirectionalIsolate"
    }

    /**
     * Builds "<arabic label> <isolated value>" with the value kept whole. This is the one way a
     * label and a value are joined anywhere in the application.
     */
    fun label(label: String, value: String?): String =
        if (value.isNullOrEmpty()) label else "$label ${isolate(value)}"
}

/** The composable form: `val title = bidi(correspondence.subject)`. */
@Composable
fun bidi(text: String?): String = remember(text) { Bidi.wrap(text) }

/** The composable form of [Bidi.isolate]. */
@Composable
fun bidiIsolate(value: String?): String = remember(value) { Bidi.isolate(value) }
