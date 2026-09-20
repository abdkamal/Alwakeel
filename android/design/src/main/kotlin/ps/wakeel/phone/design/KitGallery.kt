package ps.wakeel.phone.design

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp

/**
 * Every composable of the phone kit on one sheet, in the order `docs/design/ANDROID-KIT.md`
 * lists them. This is what the screenshot test renders in both themes, and it is the image a
 * reviewer puts beside the design preview.
 */
@Composable
fun KitGallery(modifier: Modifier = Modifier) {
    val colors = WakeelTheme.colors
    Column(
        modifier = modifier.width(412.dp).background(colors.bg),
        verticalArrangement = Arrangement.spacedBy(0.dp),
    ) {
        GallerySection("الأشرطة")
        MStatusBar(connected = true)
        MTopBar(
            title = "المراسلات",
            orgLine = "بلدية الوكيل — مكتب المدير",
            onSearch = {},
            searchDescription = "بحث",
            onMore = {},
            moreDescription = "المزيد",
            onNavigation = {},
            navigationDescription = "القائمة",
        )
        MOrgHeader(orgName = "الهيئة العامة للتنظيم", officeName = "مكتب المدير التنفيذي")
        MTabs(
            tabs = listOf(MTab("الوارد", 3), MTab("الصادر"), MTab("المتابعة", 12)),
            selectedIndex = 0,
            onSelect = {},
        )

        GallerySection("الصفوف")
        MListItem(
            title = "طلب صيانة مبنى الدائرة",
            subtitle = "وزارة الأشغال — اليوم 09:14",
            trailingValue = "20260917/1101",
            leadingIcon = WakeelIcons.Mail,
        )
        MListItem(
            title = "رد على كتاب التزويد",
            subtitle = "متأخر منذ يومين",
            trailingValue = "أمس",
            leadingIcon = WakeelIcons.AlertTriangle,
            attention = true,
        )

        GallerySection("البطاقات والمؤشرات")
        GalleryBody {
            MCard(title = "مصروفات بانتظار التأكيد") {
                MCardText("ثلاثة مصروفات بقيمة 420 ₪ تنتظر التأكيد على الحاسوب.")
            }
            MCard(title = "تنبيه", tone = MCardTone.Warning) { MCardText("الكابل غير موصول الآن.") }
            MCard(title = "خطأ", tone = MCardTone.Danger) { MCardText("تعذّر قراءة حزمة واحدة.") }
            MCard(title = "تمّت المزامنة", tone = MCardTone.Success) { MCardText("وصلت كل العناصر إلى الحاسوب.") }
            MCard(title = "معلومة", tone = MCardTone.Info) { MCardText("التقرير الشهري يُذكَّر به بعد ثلاثة أيام.") }
            MSegmentedRow(listOf("12" to "مهام اليوم", "3" to "اجتماعات", "5" to "متابعات"))
            MSyncRow(text = "آخر مزامنة اليوم 09:10", connected = true)
        }

        GallerySection("العناصر")
        GalleryBody {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                MChip("الكل", selected = true)
                MChip("متأخر", count = 4)
                MChip("قريب")
            }
            Row(
                horizontalArrangement = Arrangement.spacedBy(8.dp),
                verticalAlignment = androidx.compose.ui.Alignment.CenterVertically,
            ) {
                MAvatar("عبد الكريم")
                MBadge(7)
                MBadge(128)
                MFab(onClick = {}, contentDescription = "إدخال سريع")
            }
            MFab(onClick = {}, text = "إدخال سريع")
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                MButton("حفظ", {})
                MButton("إلغاء", {}, kind = MButtonKind.Outlined)
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                MButton("تفاصيل", {}, kind = MButtonKind.Text)
                MButton("حذف", {}, kind = MButtonKind.Danger, icon = WakeelIcons.Trash)
            }
            MSearchBar(value = "", onValueChange = {})
            MTextField(
                label = "الموضوع",
                value = "طلب صيانة",
                onValueChange = {},
                helper = "يظهر في المراسلة",
            )
            MTextField(
                label = "رقم الكتاب",
                value = "",
                onValueChange = {},
                placeholder = "مثال 20260917/1101",
                error = "أدخل رقم الكتاب",
            )
            MAmountField(label = "المبلغ", value = "120", onValueChange = {}, helper = "بالشيكل")
        }

        GallerySection("الأوراق والحوارات والحالات")
        GalleryBody {
            MSnackbar("حُفظ وسيُرسل عند المزامنة التالية", actionText = "تراجع", onAction = {})
            MDialog(
                title = "إلغاء المصروف",
                body = "لن يصل هذا المصروف إلى الحاسوب.",
                confirmText = "إلغاء المصروف",
                onConfirm = {},
                cancelText = "تراجع",
                onCancel = {},
                danger = true,
            )
            MStateView(
                title = "لا توجد عناصر",
                description = "يظهر هنا ما يصل من الحاسوب عند المزامنة التالية.",
                icon = WakeelIcons.Inbox,
                actionText = "مزامنة الآن",
                onAction = {},
            )
        }
        MSheet(
            title = "إدخال سريع",
            options = listOf(
                MSheetOption("ملاحظة", WakeelIcons.FileText),
                MSheetOption("تسجيل صوتي", WakeelIcons.Mic, MCardTone.Info),
                MSheetOption("مصروف", WakeelIcons.Wallet, MCardTone.Success),
                MSheetOption("صورة مستند", WakeelIcons.Camera),
                MSheetOption("مهمة", WakeelIcons.ListChecks, MCardTone.Warning),
                MSheetOption("ملاحظة للتقرير", WakeelIcons.FileText, MCardTone.Info),
            ),
        )

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
    }
}

@Composable
private fun GallerySection(title: String) {
    Box(
        Modifier
            .fillMaxWidth()
            .background(WakeelTheme.colors.tableHeader)
            .padding(horizontal = 16.dp, vertical = 6.dp),
    ) {
        Text(
            text = Bidi.wrap(title),
            style = WakeelTheme.typography.meta,
            color = WakeelTheme.colors.muted,
        )
    }
}

@Composable
private fun GalleryBody(content: @Composable androidx.compose.foundation.layout.ColumnScope.() -> Unit) {
    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 12.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
        content = content,
    )
}

@Preview(name = "عدة المكوّنات — فاتح", widthDp = 412, heightDp = 2400, locale = "ar")
@Composable
private fun KitGalleryLightPreview() {
    WakeelTheme(darkTheme = false) { KitGallery() }
}

@Preview(name = "عدة المكوّنات — داكن", widthDp = 412, heightDp = 2400, locale = "ar")
@Composable
private fun KitGalleryDarkPreview() {
    WakeelTheme(darkTheme = true) { KitGallery() }
}

@Preview(name = "هيكل شاشة الهاتف", widthDp = 412, heightDp = 915, locale = "ar")
@Composable
private fun PhoneScreenPreview() {
    WakeelTheme {
        MPhoneScreen(
            topBar = {
                MTopBar(
                    title = "الرئيسية",
                    orgLine = "بلدية الوكيل — مكتب المدير",
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
            fab = { MFab(onClick = {}, text = "إدخال سريع") },
        ) {
            MSyncRow(text = "آخر مزامنة اليوم 09:10", connected = true)
            MSegmentedRow(listOf("12" to "مهام اليوم", "3" to "اجتماعات", "5" to "متابعات"))
            MCard(title = "مصروفات بانتظار التأكيد") {
                MCardText("ثلاثة مصروفات بقيمة 420 ₪ تنتظر التأكيد على الحاسوب.")
            }
        }
    }
}
