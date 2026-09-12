# Saves alwakeel-windows.pen from the running Pen window by clicking File > Save in its menu
# (Ctrl+S via SendKeys and CloseMainWindow do not persist the document; the menu click does).
# Coordinates are for the Pen window docked at x=218,y=0 on a 1920x1080 display at 125% scaling,
# expressed in DPI-unaware logical pixels (physical / 1.25). Verify by mtime.
param(
    [string]$File = 'D:\AI\Administration2\design\alwakeel-windows.pen',
    [int]$FileMenuX = 190, [int]$FileMenuY = 42,
    [int]$SaveX = 207, [int]$SaveY = 430
)
$sig = @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, System.IntPtr dwExtraInfo);
'@
Add-Type -MemberDefinition $sig -Name U32 -Namespace PenSave
Add-Type -AssemblyName Microsoft.VisualBasic
Add-Type -AssemblyName System.Windows.Forms
function Click($x, $y) {
    [PenSave.U32]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 200
    [PenSave.U32]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero)
    [PenSave.U32]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero)
}
$name = [System.IO.Path]::GetFileName($File)
$before = (Get-Item $File).LastWriteTimeUtc
$win = Get-Process Pen -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*$name*" } | Select-Object -First 1
if (-not $win) { Write-Output "no-window"; exit 1 }
[Microsoft.VisualBasic.Interaction]::AppActivate($win.Id)
Start-Sleep -Milliseconds 700
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Milliseconds 300
for ($attempt = 1; $attempt -le 3; $attempt++) {
    Click $FileMenuX $FileMenuY
    Start-Sleep -Milliseconds 900
    Click $SaveX $SaveY
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        if ((Get-Item $File).LastWriteTimeUtc -gt $before) { break }
    }
    $after = Get-Item $File
    if ($after.LastWriteTimeUtc -gt $before) { Write-Output "saved $($after.Length) bytes at $($after.LastWriteTime) (attempt $attempt)"; exit 0 }
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Milliseconds 500
}
Write-Output "NOT-SAVED (mtime unchanged: $((Get-Item $File).LastWriteTime))"
exit 2
