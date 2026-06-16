#requires -version 5
<#
.SYNOPSIS
  Build an (unsigned) MSIX package for ScadBundler from a portable single-file publish.
.DESCRIPTION
  Stages the published scadbundler.exe together with the manifest and logo assets, injects the
  release version into the manifest, and runs makeappx to produce the package. Signing is left to
  the Microsoft Store (which signs at ingestion) or to a separate, secret-gated signtool step.
.EXAMPLE
  ./build-msix.ps1 -AppDir publish/win-x64 -Version 0.2.0 -OutFile dist/ScadBundler-win-x64.msix
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$AppDir,                 # folder containing scadbundler.exe
  [Parameter(Mandatory)][string]$Version,                # e.g. 0.2.0 (or a MinVer prerelease)
  [string]$OutFile = "ScadBundler.msix",
  [ValidateSet("x64","arm64")][string]$Architecture = "x64"
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

# MSIX requires a 4-part Major.Minor.Build.Revision with Revision = 0. Strip any prerelease suffix.
$clean = ($Version -replace '[-+].*$','')
$parts = @($clean.Split('.'))
while ($parts.Count -lt 3) { $parts += '0' }
$msixVersion = "{0}.{1}.{2}.0" -f $parts[0], $parts[1], $parts[2]

$exe = Join-Path $AppDir 'scadbundler.exe'
if (-not (Test-Path $exe)) { throw "scadbundler.exe not found in '$AppDir'." }

# Stage the package layout in a temp dir.
$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("scadbundler-msix-" + [guid]::NewGuid())
New-Item -ItemType Directory -Path $staging -Force | Out-Null
Copy-Item (Join-Path $here 'Images') (Join-Path $staging 'Images') -Recurse
Copy-Item $exe $staging

$manifest = Get-Content (Join-Path $here 'AppxManifest.xml') -Raw
$manifest = $manifest -replace 'Version="0\.0\.0\.0"', ("Version=`"$msixVersion`"")
$manifest = $manifest -replace 'ProcessorArchitecture="x64"', ("ProcessorArchitecture=`"$Architecture`"")
Set-Content -Path (Join-Path $staging 'AppxManifest.xml') -Value $manifest -Encoding UTF8

# Locate makeappx.exe from the newest installed Windows SDK.
$sdkBin = "C:\Program Files (x86)\Windows Kits\10\bin"
$makeappx = Get-ChildItem $sdkBin -Directory -ErrorAction SilentlyContinue |
  Sort-Object Name -Descending |
  ForEach-Object { Join-Path $_.FullName "$Architecture\makeappx.exe" } |
  Where-Object { Test-Path $_ } |
  Select-Object -First 1
if (-not $makeappx) { throw "makeappx.exe not found under '$sdkBin'. Install the Windows SDK." }

$outDir = Split-Path -Parent $OutFile
if ($outDir -and -not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

& $makeappx pack /d $staging /p $OutFile /o
if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed with exit code $LASTEXITCODE." }

Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Built '$OutFile' (version $msixVersion, $Architecture)."
