# Closes the Pen window holding alwakeel-windows.pen (Pen writes the file to disk on close),
# waits for the file to be written, then reopens it so it becomes the active canvas again.
# Run only when no Pencil MCP execute calls are in flight.
param(
    [string]$File = 'D:\AI\Administration2\design\alwakeel-windows.pen',
    [string]$PenExe = 'C:\Program Files\Pen\Pen.exe',
    [int]$WaitSeconds = 25
)
Add-Type -AssemblyName Microsoft.VisualBasic
Add-Type -AssemblyName System.Windows.Forms

$name = [System.IO.Path]::GetFileName($File)
$before = (Get-Item $File).LastWriteTimeUtc
$win = Get-Process Pen -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*$name*" } | Select-Object -First 1
if (-not $win) { Write-Output "no-window"; }
else {
    [Microsoft.VisualBasic.Interaction]::AppActivate($win.Id)
    Start-Sleep -Milliseconds 700
    [System.Windows.Forms.SendKeys]::SendWait('%{F4}')
    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $now = (Get-Item $File).LastWriteTimeUtc
        if ($now -gt $before) { break }
    }
    $after = (Get-Item $File)
    if ($after.LastWriteTimeUtc -gt $before) { Write-Output "saved $($after.Length) bytes at $($after.LastWriteTime)" }
    else { Write-Output "NOT-SAVED (mtime unchanged: $($after.LastWriteTime))" }
}
Start-Sleep -Seconds 2
Start-Process -FilePath $PenExe -ArgumentList "`"$File`""
$deadline = (Get-Date).AddSeconds($WaitSeconds)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 800
    $w = Get-Process Pen -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*$name*" } | Select-Object -First 1
    if ($w) { Write-Output "reopened window $($w.Id)"; break }
}
