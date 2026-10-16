<#
.SYNOPSIS
  Genera le immagini della procedura guidata del setup (BMP) a partire dall'icona del programma.

.DESCRIPTION
  Crea in Setup\Images le immagini richieste da Inno Setup nelle dimensioni per 100/125/150/200 % DPI:
  WizardSmall-*.bmp (icona su sfondo bianco, intestazione delle pagine) e WizardLarge-*.bmp (pannello
  laterale delle pagine iniziale e finale). Le rigenera solo se mancano o se l'icona è più recente.
#>
param(
  [string]$IconPath = (Join-Path (Split-Path -Parent $PSScriptRoot) "Assets\WhoIsPro.ico"),
  [string]$OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) "Setup\Images"),
  [switch]$Force
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $IconPath)) { throw "Icona non trovata: $IconPath" }
New-Item -ItemType Directory -Force $OutDir | Out-Null
$iconTime = (Get-Item $IconPath).LastWriteTimeUtc

$targets = @(
  @{ Name = "WizardSmall-55.bmp";  W = 55;  H = 55;  Kind = "small" },
  @{ Name = "WizardSmall-69.bmp";  W = 69;  H = 69;  Kind = "small" },
  @{ Name = "WizardSmall-83.bmp";  W = 83;  H = 83;  Kind = "small" },
  @{ Name = "WizardSmall-110.bmp"; W = 110; H = 110; Kind = "small" },
  @{ Name = "WizardLarge-164.bmp"; W = 164; H = 314; Kind = "large" },
  @{ Name = "WizardLarge-205.bmp"; W = 205; H = 393; Kind = "large" },
  @{ Name = "WizardLarge-246.bmp"; W = 246; H = 471; Kind = "large" },
  @{ Name = "WizardLarge-328.bmp"; W = 328; H = 628; Kind = "large" }
)
$todo = @($targets | Where-Object {
    $path = Join-Path $OutDir $_.Name
    $Force -or -not (Test-Path $path) -or ((Get-Item $path).LastWriteTimeUtc -lt $iconTime)
  })
if ($todo.Count -eq 0) { return }

# Il fotogramma più grande dell'icona (256 px, compresso PNG) letto direttamente dal file .ico
function Get-LargestIconFrame([string]$path) {
  $bytes = [IO.File]::ReadAllBytes($path)
  $count = [BitConverter]::ToUInt16($bytes, 4)
  $best = $null
  for ($i = 0; $i -lt $count; $i++) {
    $o = 6 + $i * 16
    $w = [int]$bytes[$o]; if ($w -eq 0) { $w = 256 }
    $entry = @{ W = $w; Size = [BitConverter]::ToUInt32($bytes, $o + 8); Offset = [BitConverter]::ToUInt32($bytes, $o + 12) }
    if (-not $best -or $entry.W -gt $best.W) { $best = $entry }
  }
  $data = New-Object byte[] $best.Size
  [Array]::Copy($bytes, [int]$best.Offset, $data, 0, [int]$best.Size)
  if ($data[0] -eq 0x89 -and $data[1] -eq 0x50) {
    $script:frameStream = New-Object IO.MemoryStream(,$data)
    return [System.Drawing.Image]::FromStream($script:frameStream)
  }
  $ico = New-Object System.Drawing.Icon($path, $best.W, $best.W)
  try { return $ico.ToBitmap() } finally { $ico.Dispose() }
}

$logo = Get-LargestIconFrame $IconPath
try {
  foreach ($t in $todo) {
    $bmp = New-Object System.Drawing.Bitmap($t.W, $t.H, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

    if ($t.Kind -eq "small") {
      $g.Clear([System.Drawing.Color]::White)
      $size = [int]($t.W * 0.86)
      $off = [int](($t.W - $size) / 2)
      $g.DrawImage($logo, $off, $off, $size, $size)
    } else {
      # Pannello laterale: sfondo blu notte come la copertina, logo e nome
      $rect = New-Object System.Drawing.Rectangle(0, 0, $t.W, $t.H)
      $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(11, 19, 34), [System.Drawing.Color]::FromArgb(13, 26, 51), [single]90)
      $g.FillRectangle($brush, $rect)
      $scale = $t.W / 164
      $size = [int]($t.W * 0.5)
      $x = [int](($t.W - $size) / 2)
      $y = [int]($t.H * 0.16)
      $g.DrawImage($logo, $x, $y, $size, $size)
      $fmt = New-Object System.Drawing.StringFormat
      $fmt.Alignment = [System.Drawing.StringAlignment]::Center
      $fontTitle = New-Object System.Drawing.Font("Segoe UI", [single](15 * $scale), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
      $fontSub = New-Object System.Drawing.Font("Segoe UI", [single](10 * $scale), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
      $grey = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(159, 177, 204))
      $ty = [single]($y + $size + 14 * $scale)
      $rTitle = New-Object System.Drawing.RectangleF([single]0, $ty, [single]$t.W, [single](24 * $scale))
      $rSub = New-Object System.Drawing.RectangleF([single]0, [single]($ty + 25 * $scale), [single]$t.W, [single](18 * $scale))
      $rFoot = New-Object System.Drawing.RectangleF([single]0, [single]($t.H - 30 * $scale), [single]$t.W, [single](18 * $scale))
      $g.DrawString("WhoIs Pro", $fontTitle, [System.Drawing.Brushes]::White, $rTitle, $fmt)
      $g.DrawString("RETE E DOMINI", $fontSub, $grey, $rSub, $fmt)
      $g.DrawString("Scansioni, WHOIS, DNS e traceroute", $fontSub, $grey, $rFoot, $fmt)
      $brush.Dispose(); $fontTitle.Dispose(); $fontSub.Dispose(); $grey.Dispose(); $fmt.Dispose()
    }
    $g.Dispose()
    $bmp.Save((Join-Path $OutDir $t.Name), [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bmp.Dispose()
  }
} finally {
  $logo.Dispose()
  if ($script:frameStream) { $script:frameStream.Dispose() }
}
Write-Host "Immagini del setup generate in $OutDir ($($todo.Count) file)."
