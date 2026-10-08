#!/usr/bin/env bash
# Builds the published Timbratune builds into publish/ (created if missing) on Linux,
# the counterpart of build.bat on Windows:
#   win      publish/Timbratune-win-x64/Timbratune-v<ver>-win-x64.exe
#   linux    publish/Timbratune-linux-x64/Timbratune-v<ver>-linux-x64.deb + .AppImage.tar.gz
#   android  publish/Timbratune-android/Timbratune-v<ver>-android.apk
#
# Usage:  ./build.sh                 all three
#         ./build.sh linux android   only those
#
# Needs the .NET 10 SDK (https://dotnet.microsoft.com/download). Linux packaging also needs
# dpkg-deb, ImageMagick and appimagetool (see scripts/package-linux.sh). Android needs the
# .NET android workload (dotnet workload install android), the Android SDK and a JDK 17+:
#   ANDROID_SDK  default $ANDROID_HOME, $ANDROID_SDK_ROOT or ~/Android/Sdk
#   ANDROID_JDK  default $JAVA_HOME
#   APPIMAGETOOL default appimagetool on PATH, or ~/tools/appimagetool
set -uo pipefail

cd "$(dirname "$0")"
root="$(pwd)"
failed=""

do_win="" do_linux="" do_android=""
if [ $# -eq 0 ]; then do_win=1 do_linux=1 do_android=1; fi
for arg in "$@"; do
  case "${arg,,}" in
    win) do_win=1 ;;
    linux) do_linux=1 ;;
    android) do_android=1 ;;
    *) echo "Unknown platform \"$arg\" (use win, linux, android)."; exit 1 ;;
  esac
done

# The projects target net10.0: an older SDK fails with a confusing error, so check first.
if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet wasn't found. Install the .NET 10 SDK or newer: https://dotnet.microsoft.com/download"
  exit 1
fi
newest="$(dotnet --list-sdks 2>/dev/null | sed -n 's/^\([0-9]*\)\..*/\1/p' | sort -n | tail -1)"
if [ -z "$newest" ] || [ "$newest" -lt 10 ]; then
  echo "The .NET 10 SDK or newer is needed (found: ${newest:-none}). Install it: https://dotnet.microsoft.com/download"
  exit 1
fi

mkdir -p publish

if [ -n "$do_win" ]; then
  echo
  echo "===== Windows ====="
  if dotnet publish src/Reyfen.Timbratune.Desktop -p:PublishProfile=win-x64 -v quiet -nologo; then
    echo "Windows build done."
  else
    failed=1; echo "Windows build FAILED."
  fi
fi

if [ -n "$do_linux" ]; then
  echo
  echo "===== Linux ====="
  if dotnet publish src/Reyfen.Timbratune.Desktop -p:PublishProfile=linux-x64 -v quiet -nologo; then
    echo "Packing .deb and AppImage..."
    tool="${APPIMAGETOOL:-$(command -v appimagetool || echo "$HOME/tools/appimagetool")}"
    if bash scripts/package-linux.sh "$tool" >/dev/null 2>&1; then
      echo "Linux build done."
    else
      failed=1; echo "Linux packaging FAILED - run scripts/package-linux.sh to see why."
    fi
  else
    failed=1; echo "Linux build FAILED."
  fi
fi

if [ -n "$do_android" ]; then
  echo
  echo "===== Android ====="
  sdk="${ANDROID_SDK:-${ANDROID_HOME:-${ANDROID_SDK_ROOT:-$HOME/Android/Sdk}}}"
  jdk="${ANDROID_JDK:-${JAVA_HOME:-}}"
  if ! dotnet workload list 2>/dev/null | grep -q '^ *android '; then
    failed=1; echo "Android build FAILED: the .NET android workload isn't installed (dotnet workload install android)."
  elif [ ! -d "$sdk/platform-tools" ]; then
    failed=1; echo "Android build FAILED: no Android SDK at $sdk (set ANDROID_SDK)."
  elif [ -z "$jdk" ] || [ ! -x "$jdk/bin/javac" ]; then
    failed=1; echo "Android build FAILED: no JDK found (set ANDROID_JDK or JAVA_HOME to a JDK 17+)."
  # No reused build servers: a long-running MSBuild node can keep a failed SDK lookup (XA5300).
  elif dotnet publish src/Reyfen.Timbratune.Android -c Release -o publish/Timbratune-android -v quiet -nologo \
      --disable-build-servers "-p:AndroidSdkDirectory=$sdk" "-p:JavaSdkDirectory=$jdk"; then
    echo "Android build done."
  else
    failed=1; echo "Android build FAILED."
  fi
fi

echo
echo "===== publish/ ====="
for dir in Timbratune-win-x64 Timbratune-linux-x64 Timbratune-android; do
  for f in publish/$dir/*.exe publish/$dir/*.deb publish/$dir/*.tar.gz publish/$dir/*.apk; do
    [ -f "$f" ] && printf '  %s  %10s bytes  %s\n' "$(date -r "$f" '+%d-%b-%y %H:%M')" "$(stat -c %s "$f")" "${f#publish/}"
  done
done

echo
if [ -n "$failed" ]; then echo "Something failed - see above."; exit 1; fi
echo "All done."
