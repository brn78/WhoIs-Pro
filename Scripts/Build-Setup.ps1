<#
.SYNOPSIS
  Crea il programma di installazione di WhoIs Pro (Inno Setup 6 o 7).

.DESCRIPTION
  1. Pubblica il progetto con Scripts\Publish.ps1 (artifacts\publish\WhoIsPro e lo zip portabile), salvo -SkipPublish.
  2. Genera le immagini della procedura guidata dall'icona del programma (Setup\Images).
  3. Compila Setup\WhoIsPro.iss con ISCC.exe in artifacts\WhoIsPro-<versione>-Setup.exe.

  Richiede Inno Setup: https://jrsoftware.org/isdl.php

.EXAMPLE
  .\Scripts\Build-Setup.ps1
  .\Scripts\Build-Setup.ps1 -SkipPublish     # riusa l'ultima pubblicazione
#>
param(
  [switch]$SkipPublish,
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "artifacts\publish\WhoIsPro"
$issFile = Join-Path $root "Setup\WhoIsPro.iss"
$outDir = Join-Path $root "artifacts"

if (-not $SkipPublish -or -not (Test-Path (Join-Path $publishDir "WhoIsPro.exe"))) {
  & (Join-Path $PSScriptRoot "Publish.ps1") -Configuration $Configuration
}

[xml]$proj = Get-Content (Join-Path $root "WhoIs Pro.vbproj")
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { $version = "1.0.0" }

# ISCC.exe: Inno Setup 7 (preferito) o 6, nelle cartelle standard o dal registro
$candidates = @()
foreach ($v in 7, 6) {
  foreach ($base in @($env:ProgramFiles, ${env:ProgramFiles(x86)}, (Join-Path $env:LOCALAPPDATA "Programs"))) {
    if ($base) { $candidates += Join-Path $base "Inno Setup $v\ISCC.exe" }
  }
  foreach ($key in @(
      "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup ${v}_is1",
      "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup ${v}_is1",
      "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup ${v}_is1")) {
    $loc = (Get-ItemProperty $key -ErrorAction SilentlyContinue).InstallLocation
    if ($loc) { $candidates += Join-Path $loc "ISCC.exe" }
  }
}
$iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup non trovato. Installalo da https://jrsoftware.org/isdl.php e riprova." }

& (Join-Path $PSScriptRoot "New-SetupImages.ps1")

Write-Host "Compilazione del setup $version con $iscc ..." -ForegroundColor Cyan
& $iscc /Qp "/DAppVersion=$version" "/DSourceDir=$publishDir" "/DOutputDir=$outDir" $issFile
if ($LASTEXITCODE -ne 0) { throw "Compilazione del setup non riuscita (codice $LASTEXITCODE)." }

$setup = Join-Path $outDir "WhoIsPro-$version-Setup.exe"
Write-Host ""
Write-Host "Setup:    $setup" -ForegroundColor Green
Write-Host "Silente:  WhoIsPro-$version-Setup.exe /SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS"
