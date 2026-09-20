# بناء الحل كاملًا وتشغيل الاختبارات. الاستخدام: .\build\build.ps1 [-Configuration Release] [-NoTest]
param([string]$Configuration = 'Debug', [switch]$NoTest)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet build Wakeel.slnx -c $Configuration -warnaserror
if ($LASTEXITCODE -ne 0) { throw "فشل البناء" }
if (-not $NoTest) {
  dotnet test Wakeel.slnx -c $Configuration --no-build --logger "trx;LogFileName=results.trx"
  if ($LASTEXITCODE -ne 0) { throw "فشلت الاختبارات" }
}
Write-Host "تم البناء والاختبار بنجاح ($Configuration)"
