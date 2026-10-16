<#
.SYNOPSIS
  Pubblica una versione di WhoIs Pro su GitHub (repo brn78/WhoIs-Pro): setup, pacchetto portabile e note.

.DESCRIPTION
  1. Legge la versione da WhoIs Pro.vbproj e le novità dalla sezione "## <versione>" di CHANGELOG.md.
  2. Crea setup e zip con Scripts\Build-Setup.ps1 (salvo -SkipBuild).
  3. Controlla che il repository sia pulito e che il tag v<versione> non esista ancora, poi crea tag e release con
     gh (GitHub CLI) e carica WhoIsPro-<versione>-Setup.exe e WhoIsPro-<versione>-win-x64.zip.
  I programmi installati trovano la nuova versione da soli (Panoramica → Aggiornamenti → Scarica e installa): GitHub
  pubblica l'impronta SHA-256 degli allegati e il programma la verifica prima di avviare il setup.

.EXAMPLE
  .\Scripts\Publish-Release.ps1
  .\Scripts\Publish-Release.ps1 -Bozza        # release in bozza, da pubblicare a mano su GitHub
#>
param(
  [switch]$SkipBuild,
  [switch]$Bozza,
  [string]$Repository = "brn78/WhoIs-Pro"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

[xml]$proj = Get-Content (Join-Path $root "WhoIs Pro.vbproj")
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw "Versione non trovata nel progetto." }
$tag = "v$version"

# Note della versione dal CHANGELOG
$changelog = Get-Content (Join-Path $root "CHANGELOG.md") -Encoding UTF8
$start = ($changelog | Select-String -Pattern "^## $([regex]::Escape($version))\b" | Select-Object -First 1).LineNumber
if (-not $start) { throw "Nel CHANGELOG.md manca la sezione ""## $version""." }
$notes = @()
for ($i = $start; $i -lt $changelog.Count; $i++) {
  if ($changelog[$i] -match "^## ") { break }
  $notes += $changelog[$i]
}
$notesFile = Join-Path $env:TEMP "WhoIsPro-release-$version.md"
[IO.File]::WriteAllLines($notesFile, [string[]]($notes | ForEach-Object { $_ }), (New-Object Text.UTF8Encoding($false)))

if ((git status --porcelain) -ne $null) { throw "Ci sono modifiche non ancora salvate nel repository: fai il commit prima di pubblicare." }
if (git tag --list $tag) { throw "Il tag $tag esiste già: aumenta la versione nel progetto e nel CHANGELOG." }
gh auth status 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "GitHub CLI non autenticata: esegui gh auth login." }

if (-not $SkipBuild) { & (Join-Path $PSScriptRoot "Build-Setup.ps1") }
$setup = Join-Path $root "artifacts\WhoIsPro-$version-Setup.exe"
$zip = Join-Path $root "artifacts\WhoIsPro-$version-win-x64.zip"
foreach ($f in $setup, $zip) { if (-not (Test-Path $f)) { throw "Manca $f" } }

Write-Host "Impronte SHA-256:" -ForegroundColor Cyan
foreach ($f in $setup, $zip) { Write-Host ("  {0}  {1}" -f (Get-FileHash $f -Algorithm SHA256).Hash.ToLower(), (Split-Path $f -Leaf)) }

git push origin HEAD
$args = @("release", "create", $tag, $setup, $zip, "--repo", $Repository, "--title", "WhoIs Pro $version", "--notes-file", $notesFile)
if ($Bozza) { $args += "--draft" }
gh @args
if ($LASTEXITCODE -ne 0) { throw "Creazione della release non riuscita." }
Write-Host ""
Write-Host "Release $tag pubblicata: https://github.com/$Repository/releases/tag/$tag" -ForegroundColor Green
