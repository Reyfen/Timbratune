<#
.SYNOPSIS
  Downloads the official Praat binary into tools/praat/ (git-ignored).

.DESCRIPTION
  Euphonia's analysis runs analyze.praat through Praat (GPLv3,
  https://www.fon.hum.uva.nl/praat/). The binary isn't committed; this script
  fetches a release from github.com/praat/praat. The Desktop build copies
  tools/praat/ next to the app as praat/.

.PARAMETER Version
  Release tag, e.g. "v7.0.02" (default: latest).

.PARAMETER Flavor
  Windows asset flavor: win-x64v1 (widest CPU support, default), win-x64v3, win-arm64.
#>
param(
    [string]$Version = "latest",
    [string]$Flavor = "win-x64v1"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $root "tools\praat"

$api = if ($Version -eq "latest") {
    "https://api.github.com/repos/praat/praat/releases/latest"
} else {
    "https://api.github.com/repos/praat/praat/releases/tags/$Version"
}
$release = Invoke-RestMethod $api -Headers @{ "User-Agent" = "euphonia-fetch-praat" }
$asset = $release.assets | Where-Object { $_.name -like "*_$Flavor.zip" } | Select-Object -First 1
if (-not $asset) { throw "No '$Flavor' zip in Praat $($release.tag_name)." }

Write-Host "Praat $($release.tag_name): $($asset.name) ($([math]::Round($asset.size / 1MB, 1)) MB)"
$zip = Join-Path ([IO.Path]::GetTempPath()) $asset.name
Invoke-WebRequest $asset.browser_download_url -OutFile $zip -UseBasicParsing

if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
New-Item -ItemType Directory -Force $dest | Out-Null
Expand-Archive $zip -DestinationPath $dest -Force
Remove-Item $zip

# Keep the version around so the About text / bug reports can quote it.
Set-Content (Join-Path $dest "VERSION.txt") $release.tag_name -Encoding utf8
# GPLv3 notice travels with the binary.
try {
    Invoke-WebRequest "https://www.gnu.org/licenses/gpl-3.0.txt" `
        -OutFile (Join-Path $dest "LICENSE-Praat-GPLv3.txt") -UseBasicParsing
} catch {
    Write-Warning "Couldn't fetch the GPLv3 text; add LICENSE-Praat-GPLv3.txt by hand before distributing."
}

Get-ChildItem $dest | Format-Table Name, Length
Write-Host "Done -> $dest"
