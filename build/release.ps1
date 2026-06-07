# =============================================================================
#  Lumos - Release build script
# =============================================================================
#  Produces a single-file Windows installer (Setup.exe) plus the update
#  packages Velopack needs, ready to upload to a GitHub Release.
#
#  PREREQUISITES (one-time):
#    1. .NET 8 SDK installed              (dotnet --version  -> 8.x)
#    2. Velopack CLI installed globally:  dotnet tool install -g vpk
#    3. EFF wordlist at src\Lumos.Core\Resources\eff_large_wordlist.txt
#
#  USAGE:
#    powershell -ExecutionPolicy Bypass -File .\build\release.ps1 -Version 1.0.0
#
#  OUTPUT:
#    build\releases\   <- upload the CONTENTS to the matching GitHub Release.
# =============================================================================

param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$RepoUrl = "https://github.com/Arrowh-0h/LUMOS"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$DesktopProj = Join-Path $ProjectRoot "src\Lumos.Desktop\Lumos.Desktop.csproj"
$PublishDir  = Join-Path $ProjectRoot "build\publish"
$ReleaseDir  = Join-Path $ProjectRoot "build\releases"
$Wordlist    = Join-Path $ProjectRoot "src\Lumos.Core\Resources\eff_large_wordlist.txt"

Write-Host "=== Lumos release build v$Version ===" -ForegroundColor Cyan

# --- Sanity checks -----------------------------------------------------------
if (-not (Test-Path $Wordlist)) {
    Write-Warning "EFF wordlist not found at $Wordlist"
    Write-Warning "The passphrase generator will fail at runtime without it."
    $continue = Read-Host "Continue anyway? (y/N)"
    if ($continue -ne "y") { exit 1 }
}

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Error "vpk (Velopack CLI) not found. Install with: dotnet tool install -g vpk"
    exit 1
}

# --- Clean previous publish output ------------------------------------------
if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }
New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null
New-Item -ItemType Directory -Force -Path $ReleaseDir | Out-Null

# --- Publish (self-contained, win-x64, NOT single-file) ----------------------
Write-Host "[1/2] Publishing self-contained build..." -ForegroundColor Yellow
dotnet publish $DesktopProj -c Release -r win-x64 --self-contained true -p:Version=$Version -p:PublishSingleFile=false -o $PublishDir
if ($LASTEXITCODE -ne 0) { Write-Error "Publish failed."; exit 1 }

# Confirm the native encryption library made it into the publish output.
$nativeDll = Get-ChildItem -Recurse -Path $PublishDir -Filter "e_sqlite3mc.dll" -ErrorAction SilentlyContinue
if (-not $nativeDll) {
    Write-Warning "e_sqlite3mc.dll not found in publish output - encrypted vaults may fail."
} else {
    Write-Host "Native SQLite library present: OK" -ForegroundColor Green
}

# --- Package with Velopack ---------------------------------------------------
Write-Host "[2/2] Packaging with Velopack..." -ForegroundColor Yellow
$IconPath = Join-Path $ProjectRoot "src\Lumos.Desktop\lumos.ico"
vpk pack --packId Lumos --packVersion $Version --packDir $PublishDir --mainExe Lumos.exe --packTitle Lumos --icon $IconPath --outputDir $ReleaseDir
if ($LASTEXITCODE -ne 0) { Write-Error "Velopack packaging failed."; exit 1 }

Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Green
Write-Host "Installer and update packages are in: $ReleaseDir" -ForegroundColor Green
Write-Host ""
Write-Host "To release:" -ForegroundColor Cyan
Write-Host "  1. Create a GitHub Release tagged v$Version"
Write-Host "  2. Upload ALL files from the releases folder to that release."
Write-Host "  Setup.exe is what users download. The nupkg and RELEASES files power updates."
Write-Host ""
Write-Host "Repo configured for updates: $RepoUrl"
