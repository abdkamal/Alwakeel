using Wakeel.Admin.UI.Services.Keys;

namespace Wakeel.Admin.UI.Text;

/// <summary>A07 «الحسابات والمفاتيح»: certificates, office keys, recovery and revocation.</summary>
public static partial class AdminAr
{
    /// <summary>Everything A07 says.</summary>
    public static class Keys
    {
        public const string Title = "الحسابات والمفاتيح";
        public const string Sub = "شهادات الأجهزة ومفاتيح المكاتب وتفعيل الحسابات واستردادها وإلغاء جهاز.";

        public const string AccountsHeading = "حسابات الأجهزة";
        public const string AccountsEmpty = "لا حسابات بعد";
        public const string AccountsEmptyDesc = "سجّل أجهزة المكاتب أولًا في «المكاتب والأجهزة».";
        public const string OpenDevices = "فتح المكاتب والأجهزة";
        public const string OpenDevicesTooltip = "فتح شاشة المكاتب والأجهزة لتسجيل جهاز";

        public const string ColumnOffice = "المكتب";
        public const string ColumnDevice = "الجهاز";
        public const string ColumnEmployee = "الموظف";
        public const string ColumnCertificate = "الشهادة";
        public const string ColumnStatus = "الحالة";
        public const string ColumnActions = "إجراءات";

        public const string OfficeKeyHeading = "مفتاح المكتب";

        public const string OfficeKeyDesc =
            "مفتاح واحد يشترك فيه حواسيب المكتب ليقرأ بعضها ما يكتبه بعض. يصل إلى كل جهاز داخل ملف إعداده.";

        public const string OfficeKeyNone = "لم يُصدر مفتاح لهذا المكتب بعد";
        public const string OfficeKeyIssue = "إصدار مفتاح المكتب";
        public const string OfficeKeyIssueTooltip = "إصدار مفتاح جديد لهذا المكتب";
        public const string OfficeKeyRotate = "تدوير مفتاح المكتب";
        public const string OfficeKeyRotateTooltip = "استبدال مفتاح هذا المكتب بمفتاح جديد";
        public const string OfficeKeyRotateTitle = "تدوير مفتاح المكتب؟";

        public const string OfficeKeyRotateWarning =
            "بعد التدوير لن يقرأ أي جهاز في هذا المكتب ما يكتبه غيره حتى يستلم ملف إعداد جديد. جهّز ملفات الإعداد لكل أجهزة المكتب قبل أن تبدأ.";

        public const string OfficeKeyIssued = "صدر مفتاح المكتب.";
        public const string OfficeKeyRotated = "دُوّر مفتاح المكتب. كل أجهزة المكتب تحتاج ملف إعداد جديد.";

        public const string Activate = "تفعيل الحساب";
        public const string ActivateTooltip = "تفعيل حساب هذا الموظف بعد تسليمه ملف الإعداد";
        public const string Activated = "فُعّل الحساب.";
        public const string ActivateCustodianHint = "فعّل حساب موظف العُهد بعد أن يستلم ملف إعداده ويفتحه.";

        public const string Recover = "استرداد الحساب";
        public const string RecoverTooltip = "إصدار شهادة جديدة وغلاف جديد لهذا الجهاز";
        public const string RecoverTitle = "استرداد حساب هذا الجهاز؟";

        public const string RecoverWarning =
            "تُصدر للجهاز مفاتيح جديدة وشهادة جديدة، فيتوقّف كل ما كان على الحاسوب القديم عن العمل. لا بدّ من تصدير ملف إعداد جديد وتسليمه للموظف.";

        public const string Recovered = "صدرت شهادة جديدة. صدّر ملف إعداد جديد لهذا الجهاز.";

        public const string Revoke = "إلغاء الجهاز";
        public const string RevokeTooltip = "منع هذا الجهاز من العمل نهائيًا";
        public const string RevokeTitle = "إلغاء هذا الجهاز نهائيًا؟";

        public const string RevokeWarning =
            "الجهاز الملغى لا يعود يعمل ولا يُستردّ. تُضاف هويّته إلى قائمة الإلغاء الموقّعة التي تحملها ملفات الإعداد التالية، فتعرفه كل حواسيب الهيئة وترفضه. لا يمكن التراجع عن هذا.";

        public const string Revoked = "أُلغي الجهاز وحُدّثت قائمة الإلغاء.";

        public const string RevocationHeading = "قائمة الإلغاء";

        public const string RevocationDesc =
            "قائمة موقّعة بمفتاح الهيئة تحمل الأجهزة الملغاة. تُرفق بكل ملف إعداد يُصدَّر بعد اليوم.";

        public const string RevocationEmpty = "لا أجهزة ملغاة";
        public const string RevocationEmptyDesc = "لم يُلغَ أي جهاز في هذه الهيئة بعد.";

        public const string CertificateIssued = "صدرت";
        public const string CertificateNone = "لا شهادة";
        public const string SeedsHeld = "بذرة المفتاح محفوظة بانتظار أول ملف إعداد";
        public const string SeedsGone = "غادرت بذرة المفتاح الأداة مع ملف الإعداد";

        public const string FilterAll = "الكل";
        public const string FilterPending = "بانتظار التفعيل";
        public const string FilterActive = "المفعّلة";
        public const string FilterRevoked = "الملغاة";

        /// <summary>What a refusal means, in the words a person reads.</summary>
        public static string Refusal(KeyRefusal refusal) => refusal switch
        {
            KeyRefusal.NoOrganisation => "لم تُسجَّل بيانات الهيئة بعد.",
            KeyRefusal.OfficeNotFound => "لم يعد هذا المكتب موجودًا. أعد فتح القائمة.",
            KeyRefusal.DeviceNotFound => "لم يعد هذا الجهاز موجودًا. أعد فتح القائمة.",
            KeyRefusal.AlreadyRevoked => "هذا الجهاز مُلغى من قبل.",
            KeyRefusal.NoOfficeKey => "أصدر مفتاح المكتب أولًا.",
            KeyRefusal.SeedsAlreadyExported => "غادرت بذرة مفتاح هذا الجهاز الأداة مع ملف إعداده. استردّ الحساب إن لزم.",
            KeyRefusal.AccountNotFound => "لم يعد هذا الحساب موجودًا. أعد فتح القائمة.",
            _ => string.Empty,
        };

        /// <summary>The line naming an office key's issue.</summary>
        public static string KeyVersion(int version) =>
            version <= 0 ? OfficeKeyNone : Bidi($"الإصدار {Digits(version)}");

        /// <summary>How many devices would need a new setup file if the key were rotated.</summary>
        public static string RotateAffects(int devices) =>
            Bidi($"{Counting.Devices(devices)} في هذا المكتب تحتاج ملف إعداد جديد.");

        /// <summary>The heading over the keys of the selected office.</summary>
        public static string KeysOf(string officeName) => Bidi($"مفتاح «{officeName}»");

        /// <summary>The line under the revocation list: how many and when it was signed.</summary>
        public static string RevocationSummary(int count, DateTimeOffset? at) => at is null
            ? Counting.Devices(count)
            : Bidi($"{Counting.Devices(count)} · وُقّعت في {at.Value.ToLocalTime():yyyy/MM/dd}");

        /// <summary>What the operations log records about A07.</summary>
        public static class Log
        {
            public static string ReIssued(string office, int deviceNo) =>
                Bidi($"استُرِدّ حساب الجهاز {Digits(deviceNo)} في «{office}» بشهادة جديدة.");

            public static string Revoked(string office, int deviceNo) =>
                Bidi($"أُلغي الجهاز {Digits(deviceNo)} في «{office}».");

            public static string OfficeKeyIssued(string office) => $"صدر مفتاح المكتب «{office}».";

            public static string OfficeKeyRotated(string office, int version) =>
                Bidi($"دُوّر مفتاح المكتب «{office}» إلى الإصدار {Digits(version)}.");

            public static string AccountActivated(string employee, string office) =>
                $"فُعّل حساب {employee} في «{office}».";
        }
    }
}
