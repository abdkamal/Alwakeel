# Closes the Pen window holding alwakeel-windows.pen (Pen writes the file to disk on close),
# waits for the file to be written, then reopens it so it becomes the active canvas again.
# Run only when no Pencil MCP execute calls are in flight.
param(
    [string]$File = 'D:\AI\Administration2\design\alwakeel-windows.pen',
    [string]$PenExe = 'C:\Program Files\Pen\Pen.exe',
    [int]$WaitSeconds = 30
)
Add-Type -AssemblyName Microsoft.VisualBasic
Add-Type -AssemblyName System.Windows.Forms

$name = [System.IO.Path]::GetFileName($File)
$before = (Get-Item $File).LastWriteTimeUtc
$win = Get-Process Pen -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*$name*" } | Select-Object -First 1
if (-not $win) { Write-Output "no-window" }
else {
    $closed = $win.CloseMainWindow()
    Write-Output "CloseMainWindow -> $closed (pid $($win.Id))"
    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        if ((Get-Item $File).LastWriteTimeUtc -gt $before) { break }
    }
    if ((Get-Item $File).LastWriteTimeUtc -le $before) {
        $still = Get-Process Pen -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*$name*" } | Select-Object -First 1
        if ($still) {
            Write-Output "window still open, trying Alt+F4"
            [Microsoft.VisualBasic.Interaction]::AppActivate($still.Id)
            Start-Sleep -Milliseconds 700
            [System.Windows.Forms.SendKeys]::SendWait('%{F4}')
            $deadline = (Get-Date).AddSeconds($WaitSeconds)
            while ((Get-Date) -lt $deadline) {
                Start-Sleep -Milliseconds 500
                if ((Get-Item $File).LastWriteTimeUtc -gt $before) { break }
            }
        }
    }
    $after = Get-Item $File
    if ($after.LastWriteTimeUtc -gt $before) { Write-Output "saved $($after.Length) bytes at $($after.LastWriteTime)" }
    else { Write-Output "NOT-SAVED (mtime unchanged: $($after.LastWriteTime))" }
}
Start-Sleep -Seconds 2
$open = Get-Process Pen -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*$name*" } | Select-Object -First 1
if (-not $open) {
    Start-Process -FilePath $PenExe -ArgumentList "`"$File`""
    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 800
        $w = Get-Process Pen -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*$name*" } | Select-Object -First 1
        if ($w) { Write-Output "reopened window $($w.Id)"; break }
    }
} else { Write-Output "window already open $($open.Id) (no reload)" }
