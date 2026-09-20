package ps.wakeel.phone.design

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

/** Item 55: mixed Arabic and Latin or numeric text keeps each fragment on its own side. */
@RunWith(RobolectricTestRunner::class)
@Config(sdk = [35])
class BidiTest {

    @Test
    fun emptyTextIsLeftAlone() {
        assertEquals("", Bidi.wrap(""))
        assertEquals("", Bidi.wrap(null))
        assertEquals("", Bidi.isolate(null))
    }

    @Test
    fun latinFragmentInsideArabicIsMarked() {
        val wrapped = Bidi.wrap("المرفق report-3.pdf جاهز")
        assertTrue("the text keeps its words", wrapped.contains("report-3.pdf"))
    }

    @Test
    fun aValueIsIsolatedOnBothSides() {
        val isolated = Bidi.isolate("20260917/1101")
        assertEquals(Bidi.FirstStrongIsolate, isolated.first())
        assertEquals(Bidi.PopDirectionalIsolate, isolated.last())
        assertTrue(isolated.contains("20260917/1101"))
    }

    @Test
    fun aLabelAndItsValueAreJoinedWithTheValueIsolated() {
        val line = Bidi.label("الرقم", "20260917/1101")
        assertTrue(line.startsWith("الرقم "))
        assertTrue(line.contains(Bidi.FirstStrongIsolate))
        assertEquals("الرقم", Bidi.label("الرقم", null))
    }
}
