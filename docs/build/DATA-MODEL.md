# نموذج بيانات الوكيل v0.21 (SQLite + SQLCipher)

مرجع ملزم لكل حزم العمل. الجداول بصيغة `snake_case`؛ الكيانات في C# بأسماء PascalCase مطابقة (`Correspondence`, `Task`…). كل التواريخ ISO-8601 UTC نصًا (`TEXT`)، المبالغ `INTEGER` بالأغورة (1 ₪ = 100)، المعرّفات `TEXT` UUID v7، القيم المنطقية `INTEGER` 0/1.

## 0. الأعمدة المشتركة

كل جدول رسمي (يُزامَن) يحمل:

| عمود | الوصف |
|---|---|
| `id` | UUID v7 |
| `created_at`, `updated_at` | UTC |
| `origin_device` | معرّف الجهاز الذي أنشأ السجل |
| `row_version` | عداد يزيد عند كل تعديل محلي |
| `base_version` | آخر نسخة مشتركة مع الأجهزة الأخرى (للتعارضات) |
| `deleted_at` | حذف منطقي فقط؛ لا حذف فعلي للسجلات الرسمية |

محفّزات `AFTER INSERT/UPDATE` على هذه الجداول تكتب في `change_log`. الجداول المحلية البحتة (إعدادات، سجلات تقنية، فهارس) لا تحمل هذه الأعمدة ولا تُزامَن.

قرارات (2026-09-16، بعد مراجعة B0):
- `WakeelDb` يختم `created_at/updated_at/row_version/origin_device` تلقائيًا عند الحفظ؛ `Remove()` على كيان مزامَن يتحوّل إلى حذف منطقي (`deleted_at`) ولا يُنفَّذ حذف فعلي أبدًا.
- مسار الاستيراد في المزامنة يُدرج السجلات الواردة بأختامها الأصلية كما جاءت من الجهاز المصدر عبر نطاق `WakeelDb.SuppressAuditStamps()`.
- الفهارس الفريدة للمعرّفات الرسمية (`correspondence.official_number`، `assets.inventory_number`، `financial_cycles.start_date`، `monthly_reports.cycle_id`) غير مصفّاة، أي تشمل السجلات المحذوفة منطقيًا؛ الخدمات تبحث بـ`IgnoreDeleted()` وتعالج حالة «سجل مخفي يملك هذا الرقم» صراحةً (استرجاع أو رسالة عربية)، لا تُعاد الأرقام الرسمية أبدًا.

القيم المعدودة (الحالات والأنواع) تُخزَّن نصًا إنجليزيًا ثابتًا (`draft`, `new`…) وتُعرض بالعربية من `Ar.Enums` فقط (مصدر واحد للتسميات).

## 1. النسخة والهوية

| جدول | الأعمدة الرئيسية | ملاحظات |
|---|---|---|
| `installation` | صف واحد: `org_id`, `org_name`, `logo_document_id`, `office_id`, `office_name`, `office_unit_id`, `office_code`, `device_id`, `device_no` (1–9), `employee_no` (1–9), `employee_name`, `role` (manager/secretary/custodian), `sync_scope` (full/custody), `cycle_start_day` (1–28), `numbering_format`, `setup_version`, `activated_at`, `app_version`, `build_date`, `org_x25519_pub`, `org_ed25519_pub` | يُملأ من `.wakeel-setup`؛ للقراءة فقط في الوكيل |
| `org_units` | `id`, `parent_id`, `level` (org/department/section/unit), `name`, `head_name`, `head_title`, `office_code`, `sort_order`, `x25519_pub` | الهيكلية كاملة من ملف الإعداد؛ الدليل الداخلي مشتق منها |
| `devices` | `id`, `unit_id`, `device_no`, `employee_no`, `employee_name`, `role`, `kind` (pc/phone), `ed25519_pub`, `x25519_pub`, `certificate` (BLOB)، `issued_at`, `revoked_at`, `paired_at`, `last_sync_at`, `sync_scope` | أجهزة المكتب والهيئة المعروفة + الهواتف المقترنة |
| `account` | صف واحد: `employee_id`, `display_name`, `photo_document_id`, `password_changed_at`, `failed_attempts`, `locked_until`, `auto_lock_minutes` (افتراضي 10) | الأغلفة نفسها في `keys\installation.key` لا في القاعدة |
| `settings` | `key` PK, `value` (JSON), `updated_at` | حدود الانتباه (متأخر/قريب/راكد)، تذكير التقرير (أيام)، وقت تنبيه الاجتماع، إرسال التنبيهات للهاتف، الأصوات، المظهر، الخط والكثافة، الطابعة، الماسح، مجلد الحزم، تذكير النسخ، قواعد الرواتب والمكافآت، قواعد تضمين التقرير |
| `official_numbers` | `kind` (out/in), `year`, `last_seq`, `last_date` | البند 5 |
| `audit_log` | `id`, `at`, `actor`, `action`, `entity_type`, `entity_id`, `summary_ar`, `details` (JSON بلا أسرار) | إضافة فقط؛ بلا أعمدة §0 ولا محفّز؛ يُصدَّر في حزم المزامنة بمدى `at` كقراءة فقط (إدراج عند الاستيراد إن لم يوجد المعرّف، لا تحديث ولا تعارض) |
| `notifications` | `id`, `kind`, `title`, `body`, `entity_type`, `entity_id`, `created_at`, `due_at`, `read_at`, `dismissed_at`, `source` | الجرس ولوحة الإشعارات |
| `clock_checks` | `at`, `verdict` (ok/suspect/bad), `details` | البند 20 |
| `health_snapshots` | `component`, `status` (ok/warning/error), `message_ar`, `action`, `checked_at` | مركز الصحة (W12) |

## 2. الجهات والدليل

| جدول | الأعمدة | ملاحظات |
|---|---|---|
| `parties` | `id`, `name`, `kind` (ministry/municipality/authority/company/person/other), `contact_name`, `phone`, `email`, `address`, `notes`, `x25519_pub` (لتبادل `.wakeel-msg`), `qr_token` | الجهات الخارجية |
| `party_names` | `id`, `party_id`, `name`, `valid_from`, `valid_to` | لقطات الاسم عبر الزمن؛ المراسلة تحفظ اسم وقت الإصدار |

الجهة الداخلية = `org_units.id`؛ في المراسلة يُميَّز بـ`counterparty_kind` (external/internal).

## 3. المراسلات والمستندات

| جدول | الأعمدة | ملاحظات |
|---|---|---|
| `correspondence` | `id`, `direction` (in/out), `official_number`, `number_issued_at`, `external_number`, `external_date`, `subject`, `type`, `confidentiality` (public/private/secret/top_secret), `recipient_only`, `counterparty_kind`, `party_id`, `unit_id`, `party_name_snapshot`, `cc` (JSON), `status` (draft/new/in_progress/awaiting_reply/done/closed/cancelled/archived), `next_step_ar`, `due_at`, `linked_correspondence_id`, `case_id`, `meeting_id`, `template_id`, `body_text`, `approved_at`, `cancel_reason`, `close_note`, `archived_at`, `report_include`, `report_highlight`, `report_comment` | الرقم يُصدر مع الاعتماد فقط |
| `correspondence_documents` | `id`, `correspondence_id`, `document_id`, `kind` (original/derived_print/attachment), `sort` | |
| `documents` | `id`, `sha256`, `size`, `mime`, `original_name`, `source` (scan/import/phone/generated/backup), `page_count`, `ocr_status` (pending/running/done/unsupported/failed), `ocr_lang`, `pinned_on_phone`, `derived_from_id` | فهرس الخزنة؛ البايتات في `vault\` بالاسم `sha256` |
| `document_pages` | `id`, `document_id`, `page_no`, `text`, `words` (JSON مربعات), `confidence` | ناتج OCR؛ جدول رسمي مزامَن (أعمدة §0 ومحفّزات) لأن نص OCR ينتج على الهاتف أو على جهاز واحد ويجب أن يصل للآخر دون إعادة OCR؛ فريد `(document_id, page_no)` |
| `document_links` | `id`, `document_id`, `entity_type`, `entity_id` | الوثائق المرتبطة بأي كيان؛ جدول رسمي مزامَن (أعمدة §0 ومحفّزات)؛ فريد `(document_id, entity_type, entity_id)` |
| `referrals` | `id`, `correspondence_id`, `to_unit_id`, `to_name`, `text`, `created_at`, `due_at`, `status` (open/answered/overdue/closed), `derived_document_id`, `extra_page_added` | البند 31 |
| `followups` | `id`, `correspondence_id`, `kind` (call/visit/reply/note/status), `note`, `next_at`, `reminder_at`, `status_from`, `status_to` | الخط الزمني |
| `corrections` | `id`, `correspondence_id`, `changes` (JSON: الحقل/القديم/الجديد), `reason`, `at` | البند 19 والسياسات الموروثة |
| `duplicate_reviews` | `id`, `correspondence_id`, `similar_id`, `score`, `verdict` (pending/not_duplicate/duplicate) | البند 14 |
| `templates` | `id`, `name`, `document_id` (docx), `fields` (JSON), `is_default`, `kind` (letter/report) | |
| `exchange_log` | `id`, `direction`, `kind` (msg/transfer/inventory/payroll), `file_name`, `other_org`, `other_office`, `entity_id`, `signature_ok`, `at` | البندان 22 و49 |

## 4. المتابعة والمهام

| جدول | الأعمدة | ملاحظات |
|---|---|---|
| `tasks` | `id`, `title`, `description`, `due_at`, `priority` (low/normal/high), `status` (open/in_progress/done/postponed/transferred), `progress`, `assignee_name`, `source_type`, `source_id`, `reminder_at`, `postpone_reason`, `completed_at`, `report_include`, `report_highlight`, `report_comment`, `source_device_kind` (pc/phone) | |
| `decisions` | `id`, `text`, `source_type` (meeting/correspondence/other), `source_id`, `decided_at`, `owner_name`, `status` (not_started/in_progress/done), `execution_pct`, `due_at`, تقرير… | |
| `commitments` | `id`, `title`, `party_id`, `amount`, `required_text`, `due_at`, `status` (open/partial/paid/overdue), تقرير… | الالتزام والسداد منفصلان |
| `commitment_payments` | `id`, `commitment_id`, `amount`, `paid_at`, `note`, `transaction_id` | |
| `obstacles` | `id`, `description`, `impact`, `required_from_parent`, `status` (open/resolved), تقرير… | |
| `needs` | `id`, `item`, `justification`, `priority`, `status`, تقرير… | |
| `notes` | `id`, `text`, `voice_document_id`, `entity_type`, `entity_id`, `for_report`, تقرير…, `source_device_kind` | ملاحظات وملاحظات التقرير والملاحظات الصوتية |

## 5. الاجتماعات والتقويم

| جدول | الأعمدة | ملاحظات |
|---|---|---|
| `meetings` | `id`, `title`, `starts_at`, `duration_min`, `location`, `agenda` (JSON مرتب), `minutes_text`, `status` (planned/held/cancelled), `reminder_minutes` (NULL = افتراضي الإعدادات؛ البند 56), `correspondence_id`, `case_id`, تقرير… | |
| `meeting_attendees` | `id`, `meeting_id`, `kind` (employee/party/unit), `ref_id`, `name`, `role` (organizer/attendee), `attended` | |
| `appointments` | `id`, `title`, `starts_at`, `ends_at`, `notes`, `reminder_minutes`, `status` (planned/done/cancelled) | مواعيد التقويم المضافة يدويًا |

التقويم = اتحاد محسوب: الاجتماعات، المواعيد، استحقاقات الالتزامات والمهام، تذكير الدورة المالية. التنبيهات تُولَّد في `notifications` بمهمة خلفية (`ReminderScheduler`) وتُزامَن مع الهاتف كإعدادات + مواعيد لا كإشعارات.

## 6. القضايا

| جدول | الأعمدة |
|---|---|
| `cases` | `id`, `case_number`, `title`, `party_id`, `stage`, `next_hearing_at`, `responsible_name`, `status` (open/pending/closed), `notes` |
| `case_events` | `id`, `case_id`, `at`, `kind` (hearing/filing/decision/note), `description` |
| `case_parties` | `id`, `case_id`, `party_id`, `role` |

## 7. الموظفون والرواتب

| جدول | الأعمدة | ملاحظات |
|---|---|---|
| `employees` | `id`, `name`, `employee_number`, `unit_id`, `job_title`, `status` (active/suspended/terminated), `hired_at`, `terminated_at`, `phone`, `photo_document_id`, `notes` | إنهاء الخدمة ممنوع حتى تسوية العُهد |
| `salary_components` | `id`, `employee_id`, `kind` (basic/allowance/deduction/bonus_rule), `name`, `amount`, `valid_from`, `valid_to` | قابلة للتعديل في أي وقت |
| `payroll_runs` | `id`, `period` (YYYY-MM), `unit_id`, `status` (draft/committed), `committed_at`, `totals` (JSON) | |
| `payroll_lines` | `id`, `run_id`, `employee_id`, `basic`, `allowances`, `deductions`, `bonuses`, `net`, `overrides` (JSON) | التحرير في السطر يُخزَّن في `overrides` |
| `bonuses` | `id`, `employee_id`, `type`, `amount`, `reason`, `granted_at`, `run_id` | |
| `payroll_imports` | `id`, `file_name`, `unit_id`, `period`, `totals` (JSON), `signature_ok`, `signer_device`, `imported_at`, `run_id` | البند 48 |

## 8. الأصول والعُهد

| جدول | الأعمدة | ملاحظات |
|---|---|---|
| `assets` | `id`, `inventory_number` (`<office_code>-<seq>`), `name`, `category`, `status` (in_service/in_transfer/lost/damaged/retired), `location`, `custodian_employee_id`, `acquired_at`, `value`, `notes`, `qr_token`, `origin_office_code` | البند 45 |
| `custody_movements` | `id`, `asset_id`, `kind` (handover/receive/confirm/return/transfer_out/transfer_in), `from_employee_id`, `to_employee_id`, `at`, `note`, `confirmed_at`, `receipt_document_id` | سجل تراكمي؛ إضافة فقط |
| `asset_transfers` | `id`, `direction` (out/in), `other_office_unit_id`, `asset_ids` (JSON), `status` (pending/accepted/rejected), `package_file`, `at`, `decided_at` | البند 46 |
| `inventory_sessions` | `id`, `started_at`, `ended_at`, `status`, `exported_file` | |
| `inventory_items` | `id`, `session_id`, `asset_id`, `result` (present/missing/damaged), `checked_at`, `via` (pc/phone_qr) | |

## 9. المالية

| جدول | الأعمدة | ملاحظات |
|---|---|---|
| `financial_cycles` | `id`, `name_ar`, `start_date`, `end_date`, `status` (open/awaiting_issue/issued), `issued_at`, `report_id` | البند 52 |
| `categories` | `id`, `kind` (expense/income), `name`, `sort` | |
| `transactions` | `id`, `kind` (expense/income), `amount`, `purpose`, `category_id`, `at`, `source` (pc/phone), `receipt_document_id`, `note`, `cycle_id`, `phone_expense_id`, `correction_of_id`, `original_at` | حرّة حتى إصدار التقرير (البند 51) |
| `ledger_entries` | `id`, `transaction_id`, `account` (cash/expense:<cat>/income:<cat>/adjustment), `debit`, `credit`, `at` | الدفتر المزدوج الداخلي |
| `phone_expenses` | `id`, `phone_device_id`, `amount`, `purpose`, `category_name`, `at`, `receipt_document_id`, `note`, `status` (pending/confirmed/rejected), `reject_reason`, `transaction_id`, `decided_at` | البند 50 |
| `cash_counts` | `id`, `at`, `book_balance`, `counted` (JSON فئات), `counted_total`, `difference`, `note`, `cycle_id` | |

## 10. التقارير

| جدول | الأعمدة | ملاحظات |
|---|---|---|
| `monthly_reports` | `id`, `cycle_id`, `status` (draft/issued), `sections` (JSON: الترتيب، الإخفاء، النص المولّد، النص المعدّل), `readiness` (JSON), `director_word`, `issued_at`, `docx_document_id`, `pdf_document_id`, `sha256`, `outgoing_correspondence_id` | البند 53 |
| `report_addenda` | `id`, `report_id`, `number`, `reason`, `item`, `text`, `issued_at`, `document_id` | ملحق تصحيحي |
| `other_reports` | `id`, `kind` (register/payroll/custody/late_tasks/meetings), `filters` (JSON), `generated_at`, `document_id` | W74 |

## 11. المزامنة والنسخ والهاتف

| جدول | الأعمدة | ملاحظات |
|---|---|---|
| `change_log` | `seq` PK, `table_name`, `row_id`, `op` (I/U), `at`, `device` | محفّزات؛ `device` = الجهاز الذي أجرى العملية محليًا (`installation.device_id`) لا `origin_device` للسجل، فالتصدير بمدى تاريخ يختار ما تغيّر على هذا الجهاز |
| `sync_packages` | `id`, `direction` (export/import), `kind` (sync/phone/msg/transfer/inventory), `from_date`, `to_date`, `target_device_id`, `source_device_id`, `file_name`, `counts` (JSON), `status` (created/verified/previewed/applied/rejected), `created_at`, `applied_at` | |
| `sync_conflicts` | `id`, `package_id`, `table_name`, `row_id`, `ours` (JSON), `theirs` (JSON), `resolution` (ours/theirs/merged), `resolved_at` | لا كتابة تلقائية |
| `phone_queue` | `id`, `direction` (to_phone/from_phone), `seq`, `file_name`, `status` (pending/written/applied/failed), `at`, `items` (JSON) | صناديق الصادر والوارد |
| `pairing_sessions` | `id`, `token_hash`, `short_code_hash`, `expires_at`, `status` (waiting/paired/expired/used), `phone_device_id` | |
| `pinned_files` | `document_id`, `pinned_at`, `sent_at` | البند 14 من v0.2 |
| `backups` | `id`, `at`, `file_path`, `size`, `includes_vault`, `app_version` | |
| `restore_log` | `id`, `at`, `file_path`, `backup_at`, `sequence_check` (ok/needs_review), `details` | البند 37 |

## 12. البحث والنماذج

| جدول | الأعمدة | ملاحظات |
|---|---|---|
| `search_chunks` | `id`, `entity_type`, `entity_id`, `document_id`, `page_no`, `text_norm`, `embedding` (BLOB float32×384), `model_id`, `updated_at` | |
| `search_fts` | FTS5(`text_norm`, content=`search_chunks`) | unicode61 remove_diacritics 2 |
| `models` | `id`, `path`, `kind` (embedding/ocr), `name`, `status` (ok/unsuitable/preparing/active), `reason_ar`, `dims`, `checked_at` | البند 30 |

## 13. قاعدة أداة المدير (`admin.db`)

`org` (صف واحد: الاسم، الشعار، يوم بداية الدورة، صيغة الترقيم، قالب التقرير، مفاتيح الهيئة مغلّفة بكلمة مرور المدير وورقة الاسترداد)، `org_units` (كما أعلاه، قابلة للتحرير)، `offices` (unit_id, office_code)، `devices` (كل الأجهزة بمفاتيحها العامة وشهاداتها وحالة الإلغاء وبذور المفاتيح المصدَّرة)، `accounts` (device_id, employee_name, employee_no, role, status)، `setup_exports` (device_id, version, exported_at, file_name, includes JSON)، `pending_changes` (تغييرات الهيكلية غير الموزعة)، `audit_log`.

## 14. قاعدة الهاتف (Room + SQLCipher)

مرآة مبسطة: `correspondence_summary`, `tasks`, `meetings`, `appointments`, `decisions`, `commitments`, `contacts` (الجهات + الهيكلية), `notifications`, `phone_expenses`, `captures` (صور المستندات ونصوص OCR بحالة الإرسال), `notes`, `pinned_files`, `settings`, `outbox` (العناصر بانتظار المزامنة التالية)، `sync_state` (آخر حزمة مستلمة/مرسلة). الملفات في التخزين الخاص للتطبيق، مشفّرة بمفتاح في Keystore.
