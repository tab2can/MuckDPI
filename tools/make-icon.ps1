$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$docs = Join-Path $root "docs"
$assets = Join-Path $root "src\MuckDPI\Assets"
New-Item -ItemType Directory -Force $docs, $assets | Out-Null

function New-LogoBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::FromArgb(255, 11, 12, 16))
    $gold = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 232, 184, 74))
    $ink = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 26, 20, 8))
    $pad = [int]($size * 0.12)
    $g.FillEllipse($gold, $pad, $pad, $size - 2 * $pad, $size - 2 * $pad)
    $fontSize = [float]($size * 0.42)
    $font = New-Object System.Drawing.Font("Segoe UI", $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $g.DrawString("M", $font, $ink, (New-Object System.Drawing.RectangleF 0, ($size * 0.04), $size, $size), $sf)
    $g.Dispose()
    $gold.Dispose()
    $ink.Dispose()
    $font.Dispose()
    return $bmp
}

$png = New-LogoBitmap 256
$pngPath = Join-Path $docs "icon.png"
$png.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$png.Save((Join-Path $assets "icon.png"), [System.Drawing.Imaging.ImageFormat]::Png)

$icoBmp = New-LogoBitmap 32
$iconHandle = $icoBmp.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($iconHandle)
$icoPath = Join-Path $assets "muckdpi.ico"
$fs = [System.IO.File]::Open($icoPath, [System.IO.FileMode]::Create)
$icon.Save($fs)
$fs.Close()
$png.Dispose()
$icoBmp.Dispose()
Write-Host "Wrote $pngPath and $icoPath"
