# Generates small JPEG previews of the design exports for agents to view (light payload).
# Usage: pwsh build/make-previews.ps1 [-MaxWidth 960] [-Quality 60]
param([int]$MaxWidth = 960, [int]$Quality = 60)
Add-Type -AssemblyName System.Drawing
$root = Join-Path $PSScriptRoot '..\design\exports' | Resolve-Path
$out = Join-Path $root 'preview'
$codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' }
$params = New-Object System.Drawing.Imaging.EncoderParameters(1)
$params.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter([System.Drawing.Imaging.Encoder]::Quality, [long]$Quality)
$count = 0; $bytes = 0
foreach ($theme in 'light', 'dark') {
  Get-ChildItem -Path (Join-Path $root $theme) -Filter *.png -Recurse | ForEach-Object {
    $rel = $_.FullName.Substring((Join-Path $root $theme).Length).TrimStart('\')
    $dest = Join-Path (Join-Path $out $theme) ([IO.Path]::ChangeExtension($rel, '.jpg'))
    New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
    if ((Test-Path $dest) -and ((Get-Item $dest).LastWriteTime -ge $_.LastWriteTime)) { $count++; $bytes += (Get-Item $dest).Length; return }
    $img = [System.Drawing.Image]::FromFile($_.FullName)
    try {
      $scale = [Math]::Min(1.0, $MaxWidth / $img.Width)
      $w = [int]($img.Width * $scale); $h = [int]($img.Height * $scale)
      $bmp = New-Object System.Drawing.Bitmap($w, $h)
      $g = [System.Drawing.Graphics]::FromImage($bmp)
      $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
      $g.Clear([System.Drawing.Color]::White)
      $g.DrawImage($img, 0, 0, $w, $h)
      $g.Dispose()
      $bmp.Save($dest, $codec, $params)
      $bmp.Dispose()
    } finally { $img.Dispose() }
    $count++; $bytes += (Get-Item $dest).Length
  }
}
"previews: $count files, {0:N1} MB under $out" -f ($bytes / 1MB)
