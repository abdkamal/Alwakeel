using Wakeel.Admin.UI.Services.Structure;

namespace Wakeel.Admin.UI.Text;

/// <summary>A05 «الهيكلية»: the four-layer tree and the offices inside it.</summary>
public static partial class AdminAr
{
    /// <summary>Everything A05 says.</summary>
    public static class Structure
    {
        public const string Title = "الهيكلية";
        public const string Sub = "أربع طبقات ثابتة: هيئة ثم دوائر ثم أقسام ثم وحدات، وتحديد أي منها مكتب.";

        public const string AddDepartment = "إضافة دائرة";
        public const string AddDepartmentTooltip = "إضافة دائرة جديدة إلى الهيئة";
        public const string AddChild = "إضافة تابع";
        public const string Edit = "تعديل";
        public const string Move = "نقل";
        public const string Disable = "إيقاف";
        public const string Enable = "إعادة التشغيل";
        public const string Delete = "حذف";
        public const string Collapse = "طيّ";
        public const string Expand = "فتح";

        public const string TreeLabel = "شجرة الهيكلية";
        public const string Empty = "لا توجد دوائر بعد";
        public const string EmptyDesc = "ابدأ بإضافة دائرة، ثم أقسامها، ثم وحداتها، ثم حدّد أي منها مكتب.";
        public const string DisabledChip = "موقوف";
        public const string OfficeChip = "مكتب";
        public const string OutOfServiceHint = "موقوف ضمن شيء موقوف فوقه.";

        public const string NameLabel = "الاسم";
        public const string NamePlaceholder = "اكتب الاسم كما يُكتب في المراسلات";
        public const string HeadNameLabel = "اسم الرئيس";
        public const string HeadTitleLabel = "المسمّى الوظيفي للرئيس";
        public const string HeadTitlePlaceholder = "رئيس الدائرة، رئيس القسم، …";
        public const string HeadNone = "لم يُسجَّل رئيس";

        public const string OfficeHeading = "المكتب والجرد";
        public const string OfficeDesc = "إن كان في هذا العنصر حاسوب، فهو مكتب وله رمز جرد خاص به.";
        public const string OfficeCodeLabel = "رمز المكتب للجرد";
        public const string OfficeCodePlaceholder = "مثال: ٣٢-٠١";
        public const string OfficeCodeHint = "رمز لا يتكرّر في الهيئة، يُلصق على أجهزة المكتب وعُهده.";
        public const string MarkAsOffice = "جعله مكتبًا";
        public const string MarkAsOfficeTooltip = "تسجيل هذا العنصر كمكتب فيه حاسوب";
        public const string UnmarkOffice = "إلغاء صفة المكتب";
        public const string UnmarkOfficeTooltip = "إلغاء صفة المكتب عن هذا العنصر";
        public const string OpenDevices = "أجهزة هذا المكتب";
        public const string OpenDevicesTooltip = "فتح أجهزة هذا المكتب";

        public const string AddTitle = "إضافة عنصر";
        public const string EditTitle = "تعديل العنصر";
        public const string MoveTitle = "نقل العنصر";
        public const string MoveLabel = "الأب الجديد";
        public const string MoveHint = "لا يمكن نقل عنصر إلى شيء يتبعه.";
        public const string DisableTitle = "إيقاف هذا العنصر؟";

        public const string DisableWarning =
            "الإيقاف يخرجه وكل ما يتبعه من الخدمة: تختفي مكاتبه وأجهزته من اللوحة، ويبقى كل ما سُجّل عنه كما هو. يمكن إعادة تشغيله لاحقًا.";

        public const string DeleteTitle = "حذف هذا العنصر نهائيًا؟";

        public const string DeleteWarning =
            "الحذف لا يمكن التراجع عنه. لا يُحذف إلا عنصر لا يتبعه شيء ولم يُسجَّل فيه جهاز قط؛ وإلا فالإيقاف هو الصواب.";

        public const string Saved = "حُفظ.";
        public const string Disabled = "أُوقف العنصر.";
        public const string Enabled = "أُعيد تشغيل العنصر.";
        public const string Deleted = "حُذف العنصر.";
        public const string Moved = "نُقل العنصر.";
        public const string OfficeSaved = "حُفظ رمز المكتب.";
        public const string OfficeCleared = "أُلغيت صفة المكتب.";

        /// <summary>What a refusal means, in the words a person reads.</summary>
        public static string Refusal(StructureRefusal refusal) => refusal switch
        {
            StructureRefusal.NameRequired => "الاسم مطلوب.",
            StructureRefusal.DuplicateName => "يوجد عنصر بهذا الاسم في المستوى نفسه. اختر اسمًا آخر.",
            StructureRefusal.NotFound => "لم يعد هذا العنصر موجودًا. أعد فتح الشجرة.",
            StructureRefusal.ParentNotFound => "لم يعد العنصر الأب موجودًا. أعد فتح الشجرة.",
            StructureRefusal.WrongLevel => "الطبقات ثابتة: دائرة تحت الهيئة، وقسم تحت دائرة، ووحدة تحت قسم.",
            StructureRefusal.WouldLoop => "لا يمكن نقل عنصر إلى شيء يتبعه.",
            StructureRefusal.HasActiveDevices => "لا يمكن حذف عنصر سُجّلت فيه أجهزة. أوقفه بدل حذفه.",
            StructureRefusal.HasChildren => "لا يمكن حذف عنصر يتبعه شيء. احذف ما تحته أولًا أو أوقفه.",
            StructureRefusal.OfficeCodeTaken => "رمز المكتب مطلوب ولا يصحّ تكراره في الهيئة.",
            StructureRefusal.NoOrganisation => "لم تُسجَّل بيانات الهيئة بعد.",
            _ => string.Empty,
        };

        /// <summary>The name of a layer.</summary>
        public static string LevelName(OrgLevel level) => level switch
        {
            OrgLevel.Org => "هيئة",
            OrgLevel.Department => "دائرة",
            OrgLevel.Section => "قسم",
            _ => "وحدة",
        };

        /// <summary>The name of the layer that hangs from a layer, for «إضافة …».</summary>
        public static string ChildLevelName(OrgLevel level) =>
            AdminStructureService.ChildLevelOf(level) is { } child ? LevelName(child) : string.Empty;

        /// <summary>The «إضافة قسم» button of a node.</summary>
        public static string AddChildLabel(OrgLevel level) =>
            AdminStructureService.ChildLevelOf(level) is { } child ? $"إضافة {LevelName(child)}" : AddChild;

        /// <summary>The tooltip of that button.</summary>
        public static string AddChildTooltip(OrgLevel level, string parentName) =>
            $"إضافة {ChildLevelName(level)} إلى «{parentName}»";

        /// <summary>The line under a node's name: what it holds.</summary>
        public static string NodeSummary(int children, OrgLevel level, int devices)
        {
            var childLevel = AdminStructureService.ChildLevelOf(level);
            var parts = new List<string>(2);

            if (childLevel is { } child)
            {
                parts.Add(child switch
                {
                    OrgLevel.Department => Counting.Departments(children),
                    OrgLevel.Section => Counting.Sections(children),
                    _ => Counting.Units(children),
                });
            }

            if (devices > 0)
            {
                parts.Add(Counting.Devices(devices));
            }

            return parts.Count == 0 ? string.Empty : Bidi(string.Join(" · ", parts));
        }

        /// <summary>The heading of the pane on the side, naming what is selected.</summary>
        public static string SelectedHeading(OrgLevel level, string name) => Bidi($"{LevelName(level)}: {name}");

        /// <summary>What the operations log records about A05.</summary>
        public static class Log
        {
            public static string Added(string level, string name) => $"أُضيفت {level} «{name}».";

            public static string Updated(string level, string name) => $"عُدّلت {level} «{name}».";

            public static string Moved(string name, string parent) => $"نُقل «{name}» إلى «{parent}».";

            public static string Disabled(string level, string name) => $"أُوقفت {level} «{name}».";

            public static string Enabled(string level, string name) => $"أُعيد تشغيل {level} «{name}».";

            public static string Deleted(string level, string name) => $"حُذفت {level} «{name}».";

            public static string OfficeSet(string name, string code) => Bidi($"صار «{name}» مكتبًا برمز {code}.");

            public static string OfficeCleared(string name) => $"أُلغيت صفة المكتب عن «{name}».";
        }
    }
}
