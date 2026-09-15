# سكربتات سير العمل للبناء

كل مرحلة من BUILD-PLAN.md تُنفَّذ بسكربت Workflow (بناء بالتوازي، مراجعة Opus، إصلاح، تحقق). النسخة المرجعية هنا؛ عند التشغيل يُمرَّر السكربت نصًا (inline) أو من مسار الجلسة.

| السكربت | المرحلة | الحزم |
|---|---|---|
| `alwakeel-b0-foundation.js` | B0 | التشفير والحزم الموقّعة (Opus)، طبقة البيانات (Sonnet + مراجعة Opus)، نظام التصميم والهيكل (Sonnet + مراجعة Opus) |

- `alwakeel-b0-closeout.js` — إغلاق B0: تطبيق ما تبقى من ملاحظات المراجعة وقرارات المشرف لكل حزمة (crypto/core/ui) ثم تحقق Opus بحد جولتين؛ يأخذ الحزم عبر `args.packages`.
- `alwakeel-b05-foundation-plus.js` — B0.5: فصل نظام التصميم إلى `Wakeel.Design`، صيغة `.wakeel-setup` في `Wakeel.Crypto/Setup`، ثم نموذج أوّلي لحزام E2E عبر Playwright/CDP.
- `alwakeel-b1-admin-firstrun.js` — B1: أداة المدير (A01–A12) والتشغيل الأول (W02–W07) بالتوازي (Opus بناءً ومراجعة).
