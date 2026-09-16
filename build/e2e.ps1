# تشغيل حزمة اختبارات Wakeel.E2E (Playwright عبر CDP على WebView2). الاستخدام: .\build\e2e.ps1
#
# المتطلبات المسبقة على هذا الجهاز:
#   - .NET 10 SDK (10.0.401 أو ما يوافقه بحسب global.json) وويندوز 10/11 (WebView2 وWPF).
#   - وقت تشغيل WebView2 (WebView2 Runtime) مثبَّت — عادة موجود مسبقًا على ويندوز 10/11 الحديث؛ إن لم
#     يكن موجودًا: يفشل Wakeel.Desktop.exe عند بدء التشغيل قبل فتح منفذ CDP، وتفشل الاختبارات بمهلة
#     زمنية توضّح السبب في ذيل السجل.
#   - Microsoft.Playwright مثبَّت مسبقًا ضمن Directory.Packages.props؛ الاختبارات تتصل بـWebView2
#     القائم عبر CDP (ConnectOverCDPAsync) فلا حاجة لتنزيل متصفح Playwright (`playwright install`).
#     السائق (driver) الذي تستخدمه حزمة Playwright الخاصة بـ.NET داخليًا يُنسَخ تلقائيًا لمجلد الإخراج
#     أثناء البناء ولا يحتاج تثبيتًا يدويًا. إن ظهر خطأ من نوع "Driver not found" على جهاز جديد نادرًا
#     (تلف نسخة NuGet المخزَّنة محليًا مثلًا)، شغِّل مرة واحدة:
#       pwsh tests/Wakeel.E2E/bin/Debug/net10.0-windows10.0.19041.0/playwright.ps1 install
#     (هذا يثبّت السائق/المتصفحات المطلوبة فقط عند الحاجة الفعلية، وهو أمر مستقل عن هذه الحزمة).
#
# ملاحظة: هذا السكربت لا يستخدم "dotnet test --no-dependencies" — الـSDK المثبَّت هنا يرفضها؛ يبني
# المشروعين بأمر build منفصل ثم يشغّل الاختبارات بـ--no-build، تمامًا كأمر البناء المعتمد لهذه الحزمة.

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

dotnet build src/Wakeel.Desktop
if ($LASTEXITCODE -ne 0) { throw "فشل بناء Wakeel.Desktop" }

dotnet build tests/Wakeel.E2E
if ($LASTEXITCODE -ne 0) { throw "فشل بناء Wakeel.E2E" }

dotnet test tests/Wakeel.E2E --no-build
if ($LASTEXITCODE -ne 0) { throw "فشلت اختبارات Wakeel.E2E" }

Write-Host "نجحت اختبارات Wakeel.E2E"
