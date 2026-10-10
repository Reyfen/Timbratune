# Builds the published Timbratune builds into publish/ (created if missing). One script for
# Windows (Windows PowerShell 5.1 or PowerShell 7) and Linux (PowerShell 7, `pwsh`):
#   win      publish/Timbratune-win-x64/Timbratune-v<ver>-win-x64.exe
#   linux    publish/Timbratune-linux-x64/Timbratune-v<ver>-linux-x64.deb + .AppImage.tar.gz
#            (packed by scripts/package-linux.sh: in WSL on Windows, directly on Linux)
#   android  publish/Timbratune-android/Timbratune-v<ver>-android.apk
#
# Usage:  ./build.ps1                 all three
#         ./build.ps1 win android     only those
# On Windows it can also be double-clicked or started with right-click -> "Run with PowerShell";
# the window then stays open at the end.
#
# Needs the .NET 10 SDK or newer (https://dotnet.microsoft.com/download). Android also needs
# the .NET android workload (dotnet workload install android), an Android SDK and a JDK 17-21,
# found through ANDROID_SDK / ANDROID_HOME / ANDROID_SDK_ROOT and ANDROID_JDK / JAVA_HOME or in
# the usual install folders (Android Studio, Visual Studio, README setup, Linux packages); if
# they aren't found, the script lists every place it looked.
# Linux packaging needs dpkg-deb, ImageMagick and appimagetool ($APPIMAGETOOL, on PATH or ~/tools/appimagetool).
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Targets)

$ErrorActionPreference = 'Continue'
$onWindows = $env:OS -eq 'Windows_NT'
$root = $PSScriptRoot
$failed = $false
$built = @()   # the published files of the platforms that built, listed at the end
$version = ([regex]::Match((Get-Content (Join-Path $root 'Directory.Build.props') -Raw), '<Version>([^<]+)</Version>')).Groups[1].Value

# Settings (Windows): the WSL distribution and user that pack the Linux build.
$wslDistro = 'Ubuntu-24.04'
$wslUser = 'tester'

function Finish([bool]$ok) {
    Pop-Location
    Write-Host ''
    if ($ok) { Write-Host 'All done.' } else { Write-Host 'Something failed - see above.' }
    # Started from Explorer (double-click or "Run with PowerShell"): its window would close
    # at once, so wait. From a terminal the parent is the shell, and it doesn't wait.
    if ($onWindows) {
        $parent = $null
        try {
            $parentId = (Get-CimInstance Win32_Process -Filter "ProcessId=$PID" -ErrorAction Stop).ParentProcessId
            $parent = (Get-Process -Id $parentId -ErrorAction Stop).ProcessName
        }
        catch { }
        if ($parent -eq 'explorer') { Read-Host 'Press Enter to close' | Out-Null }
    }
    if ($ok) { exit 0 } else { exit 1 }
}

Push-Location $root

$doWin = $false; $doLinux = $false; $doAndroid = $false
if (-not $Targets -or $Targets.Count -eq 0) { $doWin = $true; $doLinux = $true; $doAndroid = $true }
foreach ($t in $Targets) {
    switch ($t.ToLowerInvariant()) {
        'win' { $doWin = $true }
        'linux' { $doLinux = $true }
        'android' { $doAndroid = $true }
        default { Write-Host "Unknown platform `"$t`" (use win, linux, android)."; Finish $false }
    }
}

# The projects target net10.0: an older SDK fails with a confusing error, so check first.
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "dotnet wasn't found. Install the .NET 10 SDK or newer: https://dotnet.microsoft.com/download"
    Finish $false
}
$sdkMajors = @(dotnet --list-sdks 2>$null | ForEach-Object { if ($_ -match '^(\d+)\.') { [int]$Matches[1] } })
$newest = ($sdkMajors | Measure-Object -Maximum).Maximum
if (-not $newest -or $newest -lt 10) {
    Write-Host "The .NET 10 SDK or newer is needed (found: $(if ($newest) { $newest } else { 'none' })). Install it: https://dotnet.microsoft.com/download"
    Finish $false
}

# A running published build locks its exe, so the Windows publish can't replace it.
if ($doWin -and $onWindows) {
    $publishDir = Join-Path $root 'publish'
    if (Get-Process | Where-Object { $_.Path -and $_.Path.StartsWith($publishDir, [StringComparison]::OrdinalIgnoreCase) }) {
        Write-Host 'Timbratune is running from publish\ - close it first, then run this again.'
        Finish $false
    }
}

New-Item -ItemType Directory -Force (Join-Path $root 'publish') | Out-Null

if ($doWin) {
    Write-Host ''
    Write-Host '===== Windows ====='
    dotnet publish src/Reyfen.Timbratune.Desktop -p:PublishProfile=win-x64 -v quiet -nologo
    if ($LASTEXITCODE -eq 0) { Write-Host 'Windows build done.'; $built += "Timbratune-win-x64/Timbratune-v$version-win-x64.exe" }
    else { $failed = $true; Write-Host 'Windows build FAILED.' }
}

if ($doLinux) {
    Write-Host ''
    Write-Host '===== Linux ====='
    dotnet publish src/Reyfen.Timbratune.Desktop -p:PublishProfile=linux-x64 -v quiet -nologo
    if ($LASTEXITCODE -ne 0) {
        $failed = $true; Write-Host 'Linux build FAILED.'
    }
    else {
        if ($onWindows) {
            Write-Host "Packing .deb and AppImage in WSL ($wslDistro)..."
            wsl.exe -d $wslDistro -u $wslUser --cd "$root" -- bash scripts/package-linux.sh *> $null
        }
        else {
            Write-Host 'Packing .deb and AppImage...'
            $tool = $env:APPIMAGETOOL
            if (-not $tool) { $tool = (Get-Command appimagetool -ErrorAction SilentlyContinue).Source }
            if (-not $tool) { $tool = Join-Path $HOME 'tools/appimagetool' }
            bash scripts/package-linux.sh "$tool" *> $null
        }
        if ($LASTEXITCODE -eq 0) {
            Write-Host 'Linux build done.'
            $built += "Timbratune-linux-x64/Timbratune-v$version-linux-x64.deb", "Timbratune-linux-x64/Timbratune-v$version-linux-x64.AppImage.tar.gz"
        }
        else { $failed = $true; Write-Host 'Linux packaging FAILED - run scripts/package-linux.sh to see why.' }
    }
}

if ($doAndroid) {
    Write-Host ''
    Write-Host '===== Android ====='

    # Where an Android SDK and a JDK are installed on real machines, in the order they're tried:
    # environment variables first, then the default folders of Android Studio, Visual Studio,
    # this project's README setup and the usual Linux packages. Wildcards pick up versioned folders.
    if ($onWindows) {
        $sdkPlaces = @(
            @('ANDROID_SDK', $env:ANDROID_SDK), @('ANDROID_HOME', $env:ANDROID_HOME), @('ANDROID_SDK_ROOT', $env:ANDROID_SDK_ROOT),
            @('Android Studio / README default', (Join-Path $env:LOCALAPPDATA 'Android\Sdk')),
            @('Visual Studio default', (Join-Path ${env:ProgramFiles(x86)} 'Android\android-sdk')),
            @('Program Files', (Join-Path $env:ProgramFiles 'Android\android-sdk')),
            @('drive root', 'C:\Android\Sdk'))
        $jdkPlaces = @(
            @('ANDROID_JDK', $env:ANDROID_JDK), @('JAVA_HOME', $env:JAVA_HOME),
            @('README default', (Join-Path $env:LOCALAPPDATA 'Android\jdk')),
            @('Android Studio JBR', (Join-Path $env:ProgramFiles 'Android\Android Studio\jbr')),
            @('Microsoft OpenJDK', (Join-Path $env:ProgramFiles 'Microsoft\jdk-*')),
            @('Eclipse Adoptium', (Join-Path $env:ProgramFiles 'Eclipse Adoptium\jdk-*')))
        $javac = 'bin\javac.exe'
    }
    else {
        $sdkPlaces = @(
            @('ANDROID_SDK', $env:ANDROID_SDK), @('ANDROID_HOME', $env:ANDROID_HOME), @('ANDROID_SDK_ROOT', $env:ANDROID_SDK_ROOT),
            @('Android Studio default', (Join-Path $HOME 'Android/Sdk')),
            @('macOS Android Studio default', (Join-Path $HOME 'Library/Android/sdk')),
            @('Debian/Ubuntu package', '/usr/lib/android-sdk'), @('/opt', '/opt/android-sdk'))
        $jdkPlaces = @(
            @('ANDROID_JDK', $env:ANDROID_JDK), @('JAVA_HOME', $env:JAVA_HOME),
            @('system JDKs', '/usr/lib/jvm/*'),
            @('Android Studio JBR', '/opt/android-studio/jbr'))
        $javac = 'bin/javac'
    }

    # Every place with what was found there, so a failure says exactly where it looked.
    $sdkReport = @()
    $sdk = $null
    foreach ($place in $sdkPlaces) {
        $label, $path = $place
        if (-not $path) { $sdkReport += "  $label - not set"; continue }
        try {
            $entries = @(Get-ChildItem -LiteralPath $path -Name -ErrorAction Stop)
            if ($entries | Where-Object { $_ -in 'platform-tools', 'build-tools', 'platforms' }) {
                $sdkReport += "  $label - $path - OK"
                if (-not $sdk) { $sdk = $path }
            }
            else { $sdkReport += "  $label - $path - not an SDK (no platform-tools, build-tools or platforms)" }
        }
        catch [System.Management.Automation.ItemNotFoundException] { $sdkReport += "  $label - $path - doesn't exist" }
        catch { $sdkReport += "  $label - $path - $($_.Exception.Message)" }
    }

    # .NET for Android takes JDK 17 to 21 (MinimumSupportedJavaVersion / LatestSupportedJavaVersion).
    $jdkReport = @()
    $jdk = $null
    foreach ($place in $jdkPlaces) {
        $label, $pattern = $place
        if (-not $pattern) { $jdkReport += "  $label - not set"; continue }
        $dirs = @(Get-Item -Path $pattern -ErrorAction SilentlyContinue | Where-Object { $_.PSIsContainer })
        if ($dirs.Count -eq 0) { $jdkReport += "  $label - $pattern - doesn't exist"; continue }
        foreach ($dir in $dirs) {
            if (-not (Test-Path -LiteralPath (Join-Path $dir.FullName $javac))) { $jdkReport += "  $label - $($dir.FullName) - no $javac"; continue }
            $major = $null
            $release = Join-Path $dir.FullName 'release'
            if ((Test-Path -LiteralPath $release) -and ((Get-Content -LiteralPath $release -Raw) -match 'JAVA_VERSION="(?:1\.)?(\d+)')) { $major = [int]$Matches[1] }
            if ($major -and ($major -lt 17 -or $major -gt 21)) { $jdkReport += "  $label - $($dir.FullName) - Java $major (17 to 21 needed)"; continue }
            $jdkReport += "  $label - $($dir.FullName) - OK$(if ($major) { " (Java $major)" })"
            if (-not $jdk) { $jdk = $dir.FullName }
        }
    }

    if (-not (dotnet workload list 2>$null | Select-String -Pattern '^\s*android\s')) {
        $failed = $true; Write-Host 'Android build FAILED: the .NET android workload isn''t installed (dotnet workload install android).'
    }
    elseif (-not $sdk -or -not $jdk) {
        $failed = $true
        if (-not $sdk) {
            Write-Host 'Android build FAILED: no Android SDK found. Looked in:'
            $sdkReport | ForEach-Object { Write-Host $_ }
            Write-Host '  Install one with Android Studio, or see README (Android), or set ANDROID_HOME to an existing SDK.'
        }
        if (-not $jdk) {
            Write-Host 'Android build FAILED: no JDK 17-21 found. Looked in:'
            $jdkReport | ForEach-Object { Write-Host $_ }
            Write-Host '  Install one (e.g. Microsoft OpenJDK 17, or the JDK that comes with Android Studio) or set JAVA_HOME / ANDROID_JDK.'
        }
    }
    else {
        Write-Host "SDK: $sdk"
        Write-Host "JDK: $jdk"
        # "The Android SDK directory could not be found" (XA5300) with a valid SDK comes from a
        # long-running MSBuild node that kept a failed SDK lookup (e.g. from another build that
        # didn't pass the SDK path). Stop those nodes first, tell every MSBuild process where the
        # SDK and JDK are (environment as well as properties), and don't reuse nodes.
        dotnet build-server shutdown *> $null
        $env:ANDROID_HOME = $sdk
        $env:ANDROID_SDK_ROOT = $sdk
        $env:AndroidSdkDirectory = $sdk
        $env:JavaSdkDirectory = $jdk
        $env:JAVA_HOME = $jdk
        dotnet publish src/Reyfen.Timbratune.Android -c Release -o publish/Timbratune-android -v quiet -nologo `
            --disable-build-servers "-p:AndroidSdkDirectory=$sdk" "-p:JavaSdkDirectory=$jdk"
        if ($LASTEXITCODE -eq 0) { Write-Host 'Android build done.'; $built += "Timbratune-android/Timbratune-v$version-android.apk" }
        else { $failed = $true; Write-Host 'Android build FAILED.' }
    }
}

# The files of the platforms built in this run (other files in publish/ are older builds).
Write-Host ''
Write-Host '===== built now (publish/) ====='
if ($built.Count -eq 0) { Write-Host '  (nothing)' }
foreach ($name in $built) {
    $f = Get-Item -LiteralPath (Join-Path (Join-Path $root 'publish') $name) -ErrorAction SilentlyContinue
    if ($f) { Write-Host ('  {0}  {1,6:0.0} MB  {2}' -f $f.LastWriteTime.ToString('yyyy-MM-dd HH:mm'), ($f.Length / 1MB), $name) }
    else { $failed = $true; Write-Host "  missing: $name" }
}

Finish (-not $failed)
