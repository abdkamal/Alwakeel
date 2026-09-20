package ps.wakeel.phone.design

import androidx.compose.runtime.Composable
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onRoot
import com.github.takahirom.roborazzi.captureRoboImage
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode

/**
 * The screen frame of `docs/design/ANDROID-KIT.md` at the size every phone screen is drawn at:
 * 412×915, light and dark. Every screen of the later packages is built inside this frame, so
 * an error in the frame itself shows up here once instead of in thirty two screens.
 */
@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(sdk = [35], qualifiers = "w412dp-h915dp-mdpi")
class PhoneFrameScreenshotTest {

    @get:Rule
    val composeRule = createComposeRule()

    @Test
    fun phoneFrameLight() {
        composeRule.setContent { WakeelTheme(darkTheme = false) { Frame() } }
        composeRule.onRoot().captureRoboImage(screenshotPath("phone-frame-light.png"))
    }

    @Test
    fun phoneFrameDark() {
        composeRule.setContent { WakeelTheme(darkTheme = true) { Frame() } }
        composeRule.onRoot().captureRoboImage(screenshotPath("phone-frame-dark.png"))
    }

    @Composable
    private fun Frame() {
        MPhoneScreen(
            statusBar = { MStatusBar(connected = true) },
            topBar = {
                // The home screen heads the frame with the organisation strip, then the bar,
                // exactly as the kit describes the screen structure.
                MOrgHeader("هيئة تنمية المناطق الريفية", "مكتب مدير دائرة التخطيط")
                MTopBar(
                    title = "الرئيسية",
                    onSearch = {},
                    onMore = {},
                    onNavigation = {},
                )
            },
            bottomBar = {
                MBottomNav(
                    items = listOf(
                        MNavItem("الرئيسية", WakeelIcons.Home),
                        MNavItem("المراسلات", WakeelIcons.Mail, badge = 4),
                        MNavItem("المهام", WakeelIcons.ListChecks, badge = 12),
                        MNavItem("الاجتماعات", WakeelIcons.Calendar),
                        MNavItem("البحث", WakeelIcons.Search),
                    ),
                    selectedIndex = 0,
                    onSelect = {},
                )
            },
            fab = { MFab(onClick = {}, contentDescription = "إدخال سريع") },
        ) {
            MSyncRow("آخر مزامنة اليوم 09:10 — كل شيء محدّث", connected = true)
            MSegmentedRow(listOf("12" to "مهام اليوم", "3" to "اجتماعات", "5" to "متابعات"))
            MCard(title = "مصروفات بانتظار التأكيد", tone = MCardTone.Warning) {
                MCardText("ثلاثة مصروفات بقيمة 420 ₪ تنتظر التأكيد على الحاسوب.")
            }
            MListItem(
                title = "طلب صيانة مبنى الدائرة",
                subtitle = "وزارة الأشغال — اليوم 09:14",
                trailingValue = "20260917/1101",
                leadingIcon = WakeelIcons.Mail,
            )
        }
    }
}
