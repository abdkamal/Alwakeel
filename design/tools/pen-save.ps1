# Saves alwakeel-windows.pen from the running Pen window through its native File menu using the keyboard:
# Alt (focus menu bar) -> Down (open File) -> End (last item "Close") -> Up x3 (Save) -> Enter.
# Safety: keys are sent ONLY when the Pen window is verified to be the foreground window and the
# user has been idle for at least -MinIdleSeconds; otherwise it waits (up to -MaxWaitMinutes).
# Note: the injected Alt press resets GetLastInputInfo, so idle is checked only BEFORE the injection.
# Verified by the file mtime. Exit 0 saved, 2 not saved, 3 gave up waiting.
param(
    [string]$File = 'D:\AI\Administration2\design\alwakeel-windows.pen',
    [int]$MinIdleSeconds = 20,
    [int]$MaxWaitMinutes = 30,
    [int]$Attempts = 3
)
$sig = @'
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
[StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
[DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
[DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
'@
Add-Type -MemberDefinition $sig -Name Native -Namespace PenSave
Add-Type -AssemblyName Microsoft.VisualBasic
Add-Type -AssemblyName System.Windows.Forms
function IdleSeconds {
    $lii = New-Object PenSave.Native+LASTINPUTINFO
    $lii.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf($lii)
    [PenSave.Native]::GetLastInputInfo([ref]$lii) | Out-Null
    $now = [uint32]([Environment]::TickCount64 -band 0xFFFFFFFF)
    $delta = [int64]$now - [int64]$lii.dwTime
    if ($delta -lt 0) { $delta += 4294967296 }
    return $delta / 1000.0
}
$name = [System.IO.Path]::GetFileName($File)
$before = (Get-Item $File).LastWriteTimeUtc
$deadline = (Get-Date).AddMinutes($MaxWaitMinutes)
$attempt = 0
while ((Get-Date) -lt $deadline -and $attempt -lt $Attempts) {
    $win = Get-Process Pen -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*$name*" } | Select-Object -First 1
    if (-not $win) { Write-Output "no-window"; exit 1 }
    if ((IdleSeconds) -lt $MinIdleSeconds) { Start-Sleep -Seconds 5; continue }
    $h = $win.MainWindowHandle
    [PenSave.Native]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
    [PenSave.Native]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
    [PenSave.Native]::ShowWindow($h, 9) | Out-Null
    [PenSave.Native]::SetForegroundWindow($h) | Out-Null
    try { [Microsoft.VisualBasic.Interaction]::AppActivate($win.Id) } catch {}
    Start-Sleep -Milliseconds 800
    if ([PenSave.Native]::GetForegroundWindow() -ne $h) { Start-Sleep -Seconds 5; continue }
    $attempt++
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Milliseconds 300
    if ([PenSave.Native]::GetForegroundWindow() -ne $h) { continue }
    [System.Windows.Forms.SendKeys]::SendWait('%')
    Start-Sleep -Milliseconds 500
    [System.Windows.Forms.SendKeys]::SendWait('{DOWN}')
    Start-Sleep -Milliseconds 500
    [System.Windows.Forms.SendKeys]::SendWait('{END}')
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait('{UP}{UP}{UP}')
    Start-Sleep -Milliseconds 400
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    $wait = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $wait) {
        Start-Sleep -Milliseconds 500
        if ((Get-Item $File).LastWriteTimeUtc -gt $before) { break }
    }
    $after = Get-Item $File
    if ($after.LastWriteTimeUtc -gt $before) { Write-Output "saved $($after.Length) bytes at $($after.LastWriteTime) (attempt $attempt)"; exit 0 }
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}{ESC}')
    Start-Sleep -Seconds 3
}
if ($attempt -ge $Attempts) { Write-Output "NOT-SAVED after $attempt attempts (mtime $((Get-Item $File).LastWriteTime))"; exit 2 }
Write-Output "GAVE-UP waiting for the Pen window to be foreground and the user idle"
exit 3
