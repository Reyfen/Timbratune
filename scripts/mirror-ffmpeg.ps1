# Makes the FFmpeg downloads the app offers (REFACTOR-PLAN 4.3) and, with -Upload, puts them on
# the Timbratune GitHub release "ffmpeg-<version>". For each platform it fetches BtbN's LGPL-only
# static build (FFmpeg-Builds, https://github.com/BtbN/FFmpeg-Builds), checks it against BtbN's
# checksums.sha256, and repacks just ffmpeg, FFmpeg's licence and a README (where the source is)
# into ffmpeg-<version>-<rid>.tar.gz. It prints the FfmpegBuild lines to paste into
# src/Reyfen.Timbratune.Audio.Ffmpeg/FfmpegInstaller.cs (Pinned).
#
#   ./scripts/mirror-ffmpeg.ps1 -Tag autobuild-2026-10-15-13-03 -Build n9.0.2-28-g669f5ab083 -Version 9.0.2 [-Upload]
#
# BtbN's archives already in -Out (e.g. downloaded by hand) are used instead of downloading
# them again. -UrlBase is where the repacked files will be served from, when they are hosted
# somewhere other than the Timbratune release (the pinned URLs are built from it).
#
# Needs: tar, xz (Windows' tar can't read .xz; Git for Windows has xz), gh (logged in) for -Upload.
# Windows PowerShell 5.1 or pwsh. Keep this file ASCII.
param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [Parameter(Mandatory = $true)][string]$Build,
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Out = (Join-Path ([IO.Path]::GetTempPath()) 'timbratune-ffmpeg-mirror'),
    [string]$Repo = 'Reyfen/Timbratune',
    [string]$UrlBase,
    [switch]$Upload
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
if (-not $UrlBase) { $UrlBase = "https://github.com/$Repo/releases/download/ffmpeg-$Version" }
$UrlBase = $UrlBase.TrimEnd('/')
# Windows' own tar (bsdtar) for .tar and zip: a GNU tar first on the PATH (Git Bash) takes C:\ for a host.
$tar = 'tar'
if ($env:SystemRoot -and (Test-Path (Join-Path $env:SystemRoot 'System32\tar.exe'))) { $tar = Join-Path $env:SystemRoot 'System32\tar.exe' }
$base = "https://github.com/BtbN/FFmpeg-Builds/releases/download/$Tag"
$series = ($Version -split '\.')[0..1] -join '.'
$targets = @(
    @{ Rid = 'linux-x64';   Asset = "ffmpeg-$Build-linux64-lgpl-$series.tar.xz";    Exe = 'ffmpeg' },
    @{ Rid = 'linux-arm64'; Asset = "ffmpeg-$Build-linuxarm64-lgpl-$series.tar.xz"; Exe = 'ffmpeg' },
    @{ Rid = 'win-x64';     Asset = "ffmpeg-$Build-win64-lgpl-$series.zip";         Exe = 'ffmpeg.exe' },
    @{ Rid = 'win-arm64';   Asset = "ffmpeg-$Build-winarm64-lgpl-$series.zip";      Exe = 'ffmpeg.exe' }
)
New-Item -ItemType Directory -Force $Out | Out-Null

$sums = @{}
if (-not (Test-Path (Join-Path $Out 'checksums.sha256'))) {
    Invoke-WebRequest "$base/checksums.sha256" -OutFile (Join-Path $Out 'checksums.sha256') -UseBasicParsing
}
foreach ($line in Get-Content (Join-Path $Out 'checksums.sha256')) {
    $parts = $line -split '\s+', 2
    if ($parts.Count -eq 2) { $sums[$parts[1].TrimStart('*')] = $parts[0].ToLowerInvariant() }
}

$readme = @"
FFmpeg $Version for Timbratune
==============================

This is FFmpeg (https://ffmpeg.org), licensed under the GNU Lesser General Public License,
version 2.1 or later (LICENSE.txt). It is an LGPL-only static build from BtbN's FFmpeg-Builds
(https://github.com/BtbN/FFmpeg-Builds, release $Tag, build $Build), with only the ffmpeg
program kept. Timbratune runs it as a separate program to decode audio files it imports.

Source code: https://ffmpeg.org/download.html#releases (FFmpeg $Version), and
https://github.com/BtbN/FFmpeg-Builds/tree/master for how this build was made.
"@

$pins = @()
foreach ($t in $targets) {
    $archive = Join-Path $Out $t.Asset
    if (-not (Test-Path $archive)) {
        Write-Host "downloading $($t.Asset)"
        Invoke-WebRequest "$base/$($t.Asset)" -OutFile $archive -UseBasicParsing
    }
    $hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($sums[$t.Asset] -ne $hash) { throw "$($t.Asset): SHA-256 $hash doesn't match BtbN's checksums.sha256" }

    $work = Join-Path $Out $t.Rid
    if (Test-Path $work) { Remove-Item -Recurse -Force $work }
    New-Item -ItemType Directory $work | Out-Null
    $unpack = $archive
    if ($archive.EndsWith('.xz')) {
        # Windows' tar.exe has no xz support (it waits forever for an external xz): decompress first.
        if (-not (Get-Command xz -ErrorAction SilentlyContinue)) { throw 'xz not found (Git for Windows ships one; or run this in WSL)' }
        $unpack = Join-Path $work 'archive.tar'
        & cmd /c "xz -dc `"$archive`" > `"$unpack`""
        if ($LASTEXITCODE -ne 0) { throw "xz couldn't decompress $($t.Asset)" }
    }
    & $tar -xf $unpack -C $work
    if ($LASTEXITCODE -ne 0) { throw "tar couldn't unpack $($t.Asset)" }
    if ($unpack -ne $archive) { Remove-Item $unpack }
    $exe = Get-ChildItem $work -Recurse -Filter $t.Exe | Where-Object { $_.Directory.Name -eq 'bin' } | Select-Object -First 1
    $license = Get-ChildItem $work -Recurse -Filter 'LICENSE.txt' | Select-Object -First 1
    if (-not $exe -or -not $license) { throw "$($t.Asset): no bin/$($t.Exe) or LICENSE.txt inside" }

    $pack = Join-Path $work 'pack'
    New-Item -ItemType Directory $pack | Out-Null
    Copy-Item $exe.FullName (Join-Path $pack $t.Exe)
    Copy-Item $license.FullName (Join-Path $pack 'LICENSE.txt')
    [IO.File]::WriteAllText((Join-Path $pack 'README.txt'), $readme.Replace("`r`n", "`n"))
    $name = "ffmpeg-$Version-$($t.Rid).tar.gz"
    $result = Join-Path $Out $name
    if (Test-Path $result) { Remove-Item $result }
    & $tar -czf $result -C $pack $t.Exe 'LICENSE.txt' 'README.txt'
    if ($LASTEXITCODE -ne 0) { throw "tar couldn't pack $name" }
    $size = (Get-Item $result).Length
    $sha = (Get-FileHash $result -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host ("{0}: {1:N1} MB" -f $name, ($size / 1MB))
    $pins += "        new(`"$($t.Rid)`", `"$Version`", new(`"$UrlBase/$name`"), $size, `"$sha`"),"
    $t.Result = $result
}

Write-Host ''
Write-Host 'Pinned builds for FfmpegInstaller.cs:'
$pins | ForEach-Object { Write-Host $_ }

if ($Upload) {
    $notes = "FFmpeg $Version (LGPL 2.1 or later) for Timbratune's optional import decoder: BtbN FFmpeg-Builds $Tag ($Build), LGPL static builds repacked to just ffmpeg, LICENSE.txt and README.txt. Source: https://ffmpeg.org/download.html#releases"
    & gh release view "ffmpeg-$Version" --repo $Repo *> $null
    if ($LASTEXITCODE -ne 0) {
        & gh release create "ffmpeg-$Version" --repo $Repo --title "FFmpeg $Version for Timbratune" --notes $notes --latest=false
        if ($LASTEXITCODE -ne 0) { throw 'gh release create failed' }
    }
    & gh release upload "ffmpeg-$Version" --repo $Repo --clobber ($targets | ForEach-Object { $_.Result })
    if ($LASTEXITCODE -ne 0) { throw 'gh release upload failed' }
    Write-Host "uploaded to https://github.com/$Repo/releases/tag/ffmpeg-$Version"
}
