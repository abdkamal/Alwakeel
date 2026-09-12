# Saves alwakeel-windows.pen from the running Pen window through its native File menu using the keyboard:
# Alt (focus menu bar) -> Down (open File) -> End (last item "Close") -> Up x3 (Save) -> Enter.
# Ctrl+S via SendKeys, CloseMainWindow and the MCP "save" requests do not write the file; this does.
# Verified by the file's mtime. Do not run while the user is typing; it steals focus for ~3 seconds.
param(
    [string]$File = 'D:\AI\Administration2\design\alwakeel-windows.pen',
    [int]$Attempts = 2
)
Add-Type -AssemblyName Microsoft.VisualBasic
Add-Type -AssemblyName System.Windows.Forms
$name = [System.IO.Path]::GetFileName($File)
$before = (Get-Item $File).LastWriteTimeUtc
$win = Get-Process Pen -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*$name*" } | Select-Object -First 1
if (-not $win) { Write-Output "no-window"; exit 1 }
for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
    [Microsoft.VisualBasic.Interaction]::AppActivate($win.Id)
    Start-Sleep -Milliseconds 900
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait('%')
    Start-Sleep -Milliseconds 500
    [System.Windows.Forms.SendKeys]::SendWait('{DOWN}')
    Start-Sleep -Milliseconds 500
    [System.Windows.Forms.SendKeys]::SendWait('{END}')
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait('{UP}{UP}{UP}')
    Start-Sleep -Milliseconds 400
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        if ((Get-Item $File).LastWriteTimeUtc -gt $before) { break }
    }
    $after = Get-Item $File
    if ($after.LastWriteTimeUtc -gt $before) { Write-Output "saved $($after.Length) bytes at $($after.LastWriteTime) (attempt $attempt)"; exit 0 }
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}{ESC}')
    Start-Sleep -Seconds 1
}
Write-Output "NOT-SAVED (mtime unchanged: $((Get-Item $File).LastWriteTime))"
exit 2
