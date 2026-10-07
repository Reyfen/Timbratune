@echo off
rem Builds the published Timbratune builds into publish\ (created if missing):
rem   win      publish\Timbratune-win-x64\Timbratune-Desktop-v<ver>-win-x64.exe
rem   linux    publish\Timbratune-linux-x64\Timbratune-Desktop-v<ver>-linux-x64.deb + .AppImage.tar.gz (packed in WSL)
rem   android  publish\Timbratune-android\Timbratune-v<ver>-android.apk
rem
rem Usage:  build.bat                 all three
rem         build.bat win android     only those
rem Double-clicking it builds all three and keeps the window open at the end.
setlocal EnableDelayedExpansion

rem ---- settings ----
set "WSL_DISTRO=Ubuntu-24.04"
set "WSL_USER=tester"
set "ANDROID_SDK=%LOCALAPPDATA%\Android\Sdk"
set "ANDROID_JDK=%LOCALAPPDATA%\Android\jdk"

rem Run from the repository root (where this file is), wherever it was started from.
pushd "%~dp0"
set "ROOT=%CD%"
set "FAILED="

set "DO_WIN=" & set "DO_LINUX=" & set "DO_ANDROID="
if "%~1"=="" (set "DO_WIN=1" & set "DO_LINUX=1" & set "DO_ANDROID=1")
for %%A in (%*) do (
  if /i "%%~A"=="win"     set "DO_WIN=1"
  if /i "%%~A"=="linux"   set "DO_LINUX=1"
  if /i "%%~A"=="android" set "DO_ANDROID=1"
  if /i not "%%~A"=="win" if /i not "%%~A"=="linux" if /i not "%%~A"=="android" (
    echo Unknown platform "%%~A" ^(use win, linux, android^).
    set "FAILED=1"
    goto :done
  )
)

rem A running published build locks its exe, so the Windows publish can't replace it.
if defined DO_WIN (
  powershell -NoProfile -Command "if (Get-Process | Where-Object { $_.Path -like '%ROOT%\publish\*' }) { exit 1 }"
  if errorlevel 1 (
    echo Timbratune is running from publish\ - close it first, then run this again.
    set "FAILED=1"
    goto :done
  )
)

rem dotnet publish creates its own output folder; this just makes sure the parent is there.
if not exist publish mkdir publish

if defined DO_WIN (
  echo.
  echo ===== Windows =====
  dotnet publish src\Reyfen.Timbratune.Desktop -p:PublishProfile=win-x64 -v quiet -nologo
  if errorlevel 1 (set "FAILED=1" & echo Windows build FAILED.) else echo Windows build done.
)

if defined DO_LINUX (
  echo.
  echo ===== Linux =====
  dotnet publish src\Reyfen.Timbratune.Desktop -p:PublishProfile=linux-x64 -v quiet -nologo
  if errorlevel 1 (
    set "FAILED=1" & echo Linux build FAILED.
  ) else (
    echo Packing .deb and AppImage in WSL ^(%WSL_DISTRO%^)...
    wsl.exe -d %WSL_DISTRO% -u %WSL_USER% --cd "%ROOT%" -- bash scripts/package-linux.sh >nul 2>&1
    if errorlevel 1 (set "FAILED=1" & echo Linux packaging FAILED - run scripts/package-linux.sh in WSL to see why.) else echo Linux build done.
  )
)

if defined DO_ANDROID (
  echo.
  echo ===== Android =====
  rem No reused build servers: a long-running MSBuild node can keep a failed SDK lookup and
  rem then report "Android SDK directory could not be found" (XA5300) although it's there.
  dotnet publish src\Reyfen.Timbratune.Android -c Release -o publish\Timbratune-android -v quiet -nologo --disable-build-servers -p:AndroidSdkDirectory="%ANDROID_SDK%" -p:JavaSdkDirectory="%ANDROID_JDK%"
  if errorlevel 1 (set "FAILED=1" & echo Android build FAILED.) else echo Android build done.
)

echo.
echo ===== publish\ =====
for %%D in (Timbratune-win-x64 Timbratune-linux-x64 Timbratune-android) do (
  if exist "publish\%%D\" (
    for %%F in ("publish\%%D\*.exe" "publish\%%D\*.deb" "publish\%%D\*.tar.gz" "publish\%%D\*.apk") do echo   %%~tF   %%~zF bytes   %%D\%%~nxF
  )
)

:done
popd
echo.
if defined FAILED (echo Something failed - see above.) else (echo All done.)
rem Keep the window open when started by double-click (Explorer runs "cmd /c ...").
echo %CMDCMDLINE% | find /i "/c" >nul && pause
if defined FAILED exit /b 1
exit /b 0
