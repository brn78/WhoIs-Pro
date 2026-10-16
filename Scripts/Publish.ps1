<#
.SYNOPSIS
  Pubblica WhoIs Pro come applicazione autonoma per Windows x64 (runtime .NET incluso).

.DESCRIPTION
  Crea artifacts\publish\WhoIsPro (eseguibile, runtime .NET, WebView2, pagina incorporata, manuf.txt) e lo archivia
  in artifacts\WhoIsPro-<versione>-win-x64.zip, il pacchetto portabile: si estrae dove si vuole e si avvia
  WhoIsPro.exe. Il setup si crea con Scripts\Build-Setup.ps1.

.EXAMPLE
  .\Scripts\Publish.ps1
#>
param(
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "WhoIs Pro.vbproj"
$publishDir = Join-Path $root "artifacts\publish\WhoIsPro"

[xml]$proj = Get-Content $project
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { $version = "1.0.0" }

Write-Host "Pubblicazione di WhoIs Pro $version..." -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

dotnet publish $project -c $Configuration -r win-x64 --self-contained true -o $publishDir -p:DebugType=none -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish non riuscito." }

# Documentazione dei pacchetti NuGet: non serve a chi usa il programma
Get-ChildItem $publishDir -Filter "*.xml" | Remove-Item -Force
Copy-Item (Join-Path $root "README.md") $publishDir
Copy-Item (Join-Path $root "CHANGELOG.md") $publishDir
# Componenti e dati di terze parti con le loro licenze (WebView2, icone Lucide, produttori dei MAC)
Copy-Item (Join-Path $root "THIRD-PARTY-NOTICES.md") $publishDir

$zip = Join-Path $root "artifacts\WhoIsPro-$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zip

Write-Host ""
Write-Host "Cartella: $publishDir" -ForegroundColor Green
Write-Host "Archivio: $zip" -ForegroundColor Green
