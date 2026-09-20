package ps.wakeel.phone.design

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp

/** One preview frame: the theme, the page background and the kit's own padding. */
@Composable
private fun PreviewFrame(dark: Boolean = false, content: @Composable () -> Unit) {
    WakeelTheme(darkTheme = dark) {
        Column(
            Modifier.fillMaxWidth().background(WakeelTheme.colors.bg).padding(12.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) { content() }
    }
}

@Preview(name = "M.StatusBar", widthDp = 412, locale = "ar")
@Composable
private fun PreviewStatusBar() = WakeelTheme { MStatusBar(connected = true) }

@Preview(name = "M.TopBar", widthDp = 412, locale = "ar")
@Composable
private fun PreviewTopBar() = WakeelTheme {
    MTopBar("المراسلات", orgLine = "بلدية الوكيل — مكتب المدير", onSearch = {}, onMore = {}, onNavigation = {})
}

@Preview(name = "M.OrgHeader", widthDp = 412, locale = "ar")
@Composable
private fun PreviewOrgHeader() = WakeelTheme {
    MOrgHeader("الهيئة العامة للتنظيم", "مكتب المدير التنفيذي")
}

@Preview(name = "M.BottomNav", widthDp = 412, locale = "ar")
@Composable
private fun PreviewBottomNav() = WakeelTheme {
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

@Preview(name = "M.FAB", widthDp = 412, locale = "ar")
@Composable
private fun PreviewFab() = PreviewFrame {
    Row(horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
        MFab(onClick = {}, contentDescription = "إدخال سريع")
        MFab(onClick = {}, text = "إدخال سريع")
    }
}

@Preview(name = "M.ListItem", widthDp = 412, locale = "ar")
@Composable
private fun PreviewListItem() = WakeelTheme {
    Column {
        MListItem("طلب صيانة مبنى الدائرة", subtitle = "وزارة الأشغال — اليوم 09:14", trailingValue = "20260917/1101", leadingIcon = WakeelIcons.Mail)
        MListItem("رد على كتاب التزويد", subtitle = "متأخر منذ يومين", trailingValue = "أمس", leadingIcon = WakeelIcons.AlertTriangle, attention = true)
    }
}

@Preview(name = "M.Card", widthDp = 412, locale = "ar")
@Composable
private fun PreviewCards() = PreviewFrame {
    MCard(title = "بطاقة") { MCardText("نص البطاقة.") }
    MCard(title = "معلومة", tone = MCardTone.Info) { MCardText("نص المعلومة.") }
    MCard(title = "تنبيه", tone = MCardTone.Warning) { MCardText("نص التنبيه.") }
    MCard(title = "خطأ", tone = MCardTone.Danger) { MCardText("نص الخطأ.") }
    MCard(title = "تمّ", tone = MCardTone.Success) { MCardText("نص النجاح.") }
}

@Preview(name = "M.Chip / M.Badge", widthDp = 412, locale = "ar")
@Composable
private fun PreviewChips() = PreviewFrame {
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
        MChip("الكل", selected = true)
        MChip("متأخر", count = 4)
        MChip("قريب")
        MBadge(7)
        MBadge(128)
    }
}

@Preview(name = "M.Button", widthDp = 412, locale = "ar")
@Composable
private fun PreviewButtons() = PreviewFrame {
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        MButton("حفظ", {})
        MButton("إلغاء", {}, kind = MButtonKind.Outlined)
    }
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        MButton("تفاصيل", {}, kind = MButtonKind.Text)
        MButton("حذف", {}, kind = MButtonKind.Danger, icon = WakeelIcons.Trash)
    }
}

@Preview(name = "M.TextField", widthDp = 412, locale = "ar")
@Composable
private fun PreviewTextFields() = PreviewFrame {
    MTextField("الموضوع", "طلب صيانة", {}, helper = "يظهر في المراسلة")
    MTextField("رقم الكتاب", "", {}, placeholder = "مثال 20260917/1101", error = "أدخل رقم الكتاب")
    MAmountField("المبلغ", "120", {}, helper = "بالشيكل")
}

@Preview(name = "M.SearchBar", widthDp = 412, locale = "ar")
@Composable
private fun PreviewSearchBar() = PreviewFrame { MSearchBar(value = "", onValueChange = {}) }

@Preview(name = "M.Sheet", widthDp = 412, locale = "ar")
@Composable
private fun PreviewSheet() = WakeelTheme {
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
}

@Preview(name = "M.Dialog", widthDp = 412, locale = "ar")
@Composable
private fun PreviewDialog() = PreviewFrame {
    MDialog(
        title = "إلغاء المصروف",
        body = "لن يصل هذا المصروف إلى الحاسوب.",
        confirmText = "إلغاء المصروف",
        onConfirm = {},
        cancelText = "تراجع",
        onCancel = {},
        danger = true,
    )
}

@Preview(name = "M.Snackbar", widthDp = 412, locale = "ar")
@Composable
private fun PreviewSnackbar() = PreviewFrame {
    MSnackbar("حُفظ وسيُرسل عند المزامنة التالية", actionText = "تراجع", onAction = {})
}

@Preview(name = "M.Avatar", widthDp = 412, locale = "ar")
@Composable
private fun PreviewAvatar() = PreviewFrame {
    Row(horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
        MAvatar("عبد الكريم")
        MAvatar("عبد الكريم", size = 72.dp)
    }
}

@Preview(name = "M.Tabs", widthDp = 412, locale = "ar")
@Composable
private fun PreviewTabs() = WakeelTheme {
    MTabs(listOf(MTab("الوارد", 3), MTab("الصادر"), MTab("المتابعة", 12)), 0, {})
}

@Preview(name = "M.SegmentedRow", widthDp = 412, locale = "ar")
@Composable
private fun PreviewSegmentedRow() = PreviewFrame {
    MSegmentedRow(listOf("12" to "مهام اليوم", "3" to "اجتماعات", "5" to "متابعات"))
}

@Preview(name = "M.StateView", widthDp = 412, locale = "ar")
@Composable
private fun PreviewStateView() = PreviewFrame {
    MStateView(
        title = "لا توجد عناصر",
        description = "يظهر هنا ما يصل من الحاسوب عند المزامنة التالية.",
        actionText = "مزامنة الآن",
        onAction = {},
    )
}

@Preview(name = "M.SyncRow", widthDp = 412, locale = "ar")
@Composable
private fun PreviewSyncRow() = PreviewFrame {
    MSyncRow("آخر مزامنة اليوم 09:10", connected = true)
    MSyncRow("الكابل غير موصول", connected = false)
}
