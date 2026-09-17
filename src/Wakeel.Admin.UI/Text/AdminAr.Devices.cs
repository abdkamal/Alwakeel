using System.Globalization;
using Wakeel.Admin.UI.Services.Devices;

namespace Wakeel.Admin.UI.Text;

/// <summary>A06 «المكاتب والأجهزة»: the computers of each office and who sits at them.</summary>
public static partial class AdminAr
{
    /// <summary>Everything A06 says.</summary>
    public static class Devices
    {
        public const string Title = "المكاتب والأجهزة";
        public const string Sub = "لكل مكتب أجهزته وموظفوه، ولكل جهاز رقمه ودوره ونطاق مزامنته.";

        public const string OfficesHeading = "المكاتب";
        public const string SearchPlaceholder = "ابحث عن مكتب…";

        /// <summary>What the offices rail says when the search hides every office it holds.</summary>
        public const string SearchNothing = "لا مكتب بهذا الاسم.";
        public const string ChooseOffice = "اختر مكتبًا";
        public const string ChooseOfficeDesc = "اضغط على مكتب من القائمة لعرض أجهزته وموظفيه.";
        public const string OfficeCodeLabel = "رمز المكتب للجرد";

        /// <summary>The inventory code as one chip label: the words and the code the person reads together.</summary>
        public static string OfficeCodeChip(string code) => Bidi($"{OfficeCodeLabel} {code}");
        public const string SaveLabel = "حفظ";
        public const string RemoveBlocked = "صدر ملف إعداد لهذا الجهاز، فلا يُزال.";
        public const string NumbersFull = "أرقام الأجهزة التسعة مأخوذة كلها في هذا المكتب.";
        public const string Refresh = "تحديث";
        public const string RefreshTooltip = "إعادة قراءة المكاتب والأجهزة";
        public const string OfficesEmpty = "لا توجد مكاتب بعد";
        public const string OfficesEmptyDesc = "افتح «الهيكلية» وحدّد العناصر التي فيها حواسيب لتصير مكاتب.";
        public const string OpenStructure = "فتح الهيكلية";
        public const string OpenStructureTooltip = "فتح شجرة الهيكلية لتحديد المكاتب";

        public const string DevicesHeading = "الأجهزة";
        public const string DevicesEmpty = "لا أجهزة في هذا المكتب";
        public const string DevicesEmptyDesc = "أضف أول جهاز لتُنشأ شهادته ويصير للمكتب حساب.";
        public const string Add = "إضافة جهاز";
        public const string AddTooltip = "إضافة جهاز إلى هذا المكتب";
        public const string Edit = "تعديل";
        public const string EditTooltip = "تعديل بيانات هذا الجهاز";
        public const string Remove = "إزالة";
        public const string RemoveTooltip = "إزالة هذا الجهاز قبل تصدير ملف إعداده";
        public const string OpenKeys = "الحسابات والمفاتيح";
        public const string OpenKeysTooltip = "فتح حسابات هذا المكتب ومفاتيحه";

        /// <summary>
        /// A device whose employee record was never written — a registration that stopped between
        /// its two steps. It is shown as what it is instead of being filled in with a role and an
        /// employee nobody chose.
        /// </summary>
        public const string IncompleteChip = "سجل ناقص";

        public const string IncompleteTooltip =
            "سُجّل هذا الجهاز دون بيانات موظفه ودوره. أزِله وأعد إضافته.";

        /// <summary>Stands in a cell whose value the tool does not have.</summary>
        public const string Unknown = "—";

        public const string ColumnDevice = "الجهاز";
        public const string ColumnEmployee = "الموظف";
        public const string ColumnRole = "الدور";
        public const string ColumnScope = "نطاق المزامنة";
        public const string ColumnStatus = "الحالة";
        public const string ColumnActions = "إجراءات";

        public const string DeviceNoLabel = "رقم الجهاز";
        public const string DeviceNoHint = "من 1 إلى 9، ولا يتكرّر داخل المكتب نفسه.";
        public const string EmployeeNameLabel = "اسم الموظف";
        public const string EmployeeNamePlaceholder = "الاسم الكامل";
        public const string EmployeeNoLabel = "رقم الموظف";
        public const string EmployeeNoHint = "من 1 إلى 9.";
        public const string RoleLabel = "الدور";
        public const string ScopeLabel = "نطاق المزامنة";
        public const string ScopeHint = "«العُهد فقط» لموظف العُهد الذي لا يحتاج إلى بقية عمل المكتب.";

        public const string AddTitle = "جهاز جديد";
        public const string EditTitle = "تعديل الجهاز";
        public const string RemoveTitle = "إزالة هذا الجهاز؟";

        public const string RemoveWarning =
            "لم يُصدَّر ملف إعداد لهذا الجهاز بعد، فتُزال بياناته ومفاتيحه من الأداة ولا يبقى له أثر. لا يمكن التراجع عن هذا.";

        public const string Saved = "حُفظ الجهاز.";
        public const string Removed = "أُزيل الجهاز.";

        public const string PhoneChip = "هاتف";
        public const string PhoneReadOnly = "الهاتف يُدار من الحاسوب الذي اقترن به، ويظهر هنا للاطّلاع.";
        public const string RevokedChip = "مُلغى";
        public const string PendingChip = "بانتظار التفعيل";
        public const string ActiveChip = "مفعّل";
        public const string SeedsHeldChip = "بانتظار ملف الإعداد";
        public const string KeyMissing = "لا مفتاح للمكتب بعد";
        public const string NeverSynced = "لم تُسجَّل مزامنة بعد";
        public const string NeverPaired = "لم يُسجَّل اقتران";

        /// <summary>The name of a role.</summary>
        public static string RoleName(string role) => role switch
        {
            DeviceRoles.Manager => "مدير المكتب",
            DeviceRoles.Secretary => "سكرتير",
            DeviceRoles.Custodian => "موظف العُهد",
            _ => role,
        };

        /// <summary>The name of a sync scope.</summary>
        public static string ScopeName(string scope) => scope switch
        {
            SyncScopes.Full => "كامل",
            SyncScopes.Custody => "العُهد فقط",
            _ => scope,
        };

        /// <summary>The three roles, ready for a chooser.</summary>
        public static IReadOnlyList<(string Value, string Label)> RoleOptions { get; } =
            DeviceRoles.All.Select(role => (role, RoleName(role))).ToList();

        /// <summary>The two scopes, ready for a chooser.</summary>
        public static IReadOnlyList<(string Value, string Label)> ScopeOptions { get; } =
            SyncScopes.All.Select(scope => (scope, ScopeName(scope))).ToList();

        /// <summary>The nine numbers, ready for a chooser.</summary>
        public static IReadOnlyList<(string Value, string Label)> NumberOptions { get; } =
            Enumerable.Range(AdminDeviceService.MinNumber, AdminDeviceService.MaxNumber)
                .Select(number => (number.ToString(CultureInfo.InvariantCulture), Digits(number)))
                .ToList();

        /// <summary>What a refusal means, in the words a person reads.</summary>
        public static string Refusal(DeviceRefusal refusal) => refusal switch
        {
            DeviceRefusal.OfficeNotFound => "لم يعد هذا المكتب موجودًا. أعد فتح قائمة المكاتب.",
            DeviceRefusal.DeviceNotFound => "لم يعد هذا الجهاز موجودًا. أعد فتح قائمة الأجهزة.",
            DeviceRefusal.DeviceNoOutOfRange => "رقم الجهاز من 1 إلى 9.",
            DeviceRefusal.DeviceNoTaken => "هذا الرقم مأخوذ في هذا المكتب. اختر رقمًا آخر.",
            DeviceRefusal.EmployeeNoOutOfRange => "رقم الموظف من 1 إلى 9.",
            DeviceRefusal.EmployeeNameRequired => "اسم الموظف مطلوب.",
            DeviceRefusal.RoleInvalid => "اختر دور الموظف.",
            DeviceRefusal.ScopeInvalid => "اختر نطاق المزامنة.",
            DeviceRefusal.PhoneIsReadOnly => PhoneReadOnly,
            DeviceRefusal.AlreadyRevoked => "هذا الجهاز مُلغى، ولا يُعدَّل بعد إلغائه.",
            DeviceRefusal.AlreadyExported => "صدر ملف إعداد لهذا الجهاز، فلا يُزال. ألغِه من «الحسابات والمفاتيح» إن لزم.",
            DeviceRefusal.NoOrganisation => "لم تُسجَّل بيانات الهيئة بعد.",
            DeviceRefusal.CouldNotSave => "تعذّر حفظ الجهاز على هذا الحاسوب. أعد المحاولة، وإن تكرّر الأمر فافتح «الصيانة».",
            _ => string.Empty,
        };

        /// <summary>How many devices this office has, under the heading.</summary>
        public static string DevicesCount(int count) => Bidi($"{Counting.Devices(count)} في هذا المكتب");

        /// <summary>The strip under the devices table: how a device actually reaches its desk.</summary>
        public const string HowToAdd = "إضافة جهاز إلى هذا المكتب";

        public const string HowToAddDesc =
            "يُسجَّل الجهاز هنا أولًا، ثم يُصدَّر له ملف إعداد من «تصدير ملفات الإعداد» ويُنقل إلى حاسوب الموظف "
            + "على ذاكرة محمولة ليفتح عليه. لا يعمل الجهاز قبل فتح ملف الإعداد عليه.";

        /// <summary>When the rows on screen were last read off the tool's own records.</summary>
        public static string LastChecked(DateTimeOffset at) => Bidi(
            $"آخر تحديث لحالة الأجهزة {at.ToLocalTime().ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture)}");

        /// <summary>The line under «المكاتب»: how many there are, as a sentence rather than a lone word.</summary>
        public static string OfficesCount(int count) => Bidi($"{Counting.Offices(count)} في الهيكلية");

        /// <summary>How an office is named in a list: its name and its inventory code.</summary>
        public static string OfficeLine(string name, string code) => Bidi($"{name} · {code}");

        /// <summary>The line under an office in the list.</summary>
        public static string OfficeSummary(int devices, int phones, bool hasKey)
        {
            var parts = new List<string> { Counting.Devices(devices) };
            if (phones > 0)
            {
                parts.Add($"منها {Counting.Devices(phones)} هاتف");
            }

            if (!hasKey)
            {
                parts.Add(KeyMissing);
            }

            return Bidi(string.Join(" · ", parts));
        }

        /// <summary>How a device is named: «الجهاز 3».</summary>
        public static string DeviceName(int deviceNo) => Bidi($"الجهاز {Digits(deviceNo)}");

        /// <summary>How an employee is named: the name and the number.</summary>
        public static string EmployeeLine(string name, int employeeNo) =>
            Bidi($"{name} · الموظف {Digits(employeeNo)}");

        /// <summary>The heading over the devices of the selected office.</summary>
        public static string DevicesOf(string officeName) => Bidi($"أجهزة «{officeName}»");

        /// <summary>What the operations log records about A06.</summary>
        public static class Log
        {
            public static string Added(string office, int deviceNo, string employee) =>
                Bidi($"أُضيف الجهاز {Digits(deviceNo)} في «{office}» باسم {employee}.");

            public static string Updated(string office, int deviceNo, string employee) =>
                Bidi($"عُدّل الجهاز {Digits(deviceNo)} في «{office}» باسم {employee}.");

            public static string Removed(string office, int deviceNo) =>
                Bidi($"أُزيل الجهاز {Digits(deviceNo)} من «{office}» قبل تصدير إعداده.");
        }
    }
}
