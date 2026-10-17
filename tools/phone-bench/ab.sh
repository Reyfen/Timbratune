#!/bin/bash
# A/B of two Release profiling APKs on one phone: N pairs, alternating the order (A B, B A, …),
# each run a cold start + fake-mic recording (run.sh). Stops if Battery Saver comes on.
# One driver per phone at a time (a lock), so two drivers never mix their runs.
# Usage: ab.sh <serial> <package> <wav> <out-dir> <record-x> <record-y> <stop-x> <stop-y> <pairs> <labelA>=<apkA> <labelB>=<apkB>
# Both APKs must have the same package (build them with -p:ApplicationId=<package>).
set -u
export MSYS_NO_PATHCONV=1
ADB=${ADB:-"$LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe"}
here=$(cd "$(dirname "$0")" && pwd)
serial=$1; pkg=$2; wav=$3; out=$4; rx=$5; ry=$6; sx=$7; sy=$8; pairs=$9
la=${10%%=*}; apka=${10#*=}; lb=${11%%=*}; apkb=${11#*=}
a() { "$ADB" -s "$serial" "$@"; }

lock="${TMPDIR:-/tmp}/phone-bench-$serial.lock"
if ! mkdir "$lock" 2>/dev/null; then echo "another driver holds $lock"; exit 1; fi
trap 'rmdir "$lock"' EXIT
mkdir -p "$out"

install() {
  a install -r "$1" | grep -v -e Success -e Streamed
}

# The app's external files folder exists once it has run: install A, start it once, push the take.
install "$apka"
a shell monkey -p "$pkg" -c android.intent.category.LAUNCHER 1 > /dev/null 2>&1
sleep 8
a shell am force-stop "$pkg"
a push "$wav" "/sdcard/Android/data/$pkg/files/fake-mic.wav" > /dev/null || { echo "push failed"; exit 1; }

current=$la
for i in $(seq 1 "$pairs"); do
  if [ $((i % 2)) -eq 1 ]; then order="$la $lb"; else order="$lb $la"; fi
  for x in $order; do
    saver=$(a shell settings get global low_power | tr -d '\r')
    level=$(a shell dumpsys battery | tr -d '\r' | awk '/ level:/ {print $2}')
    echo "$x-$i battery $level% saver $saver"
    if [ "$saver" = 1 ]; then echo "STOPPED: Battery Saver is on (runs would be throttled)"; exit 2; fi
    if [ "$x" != "$current" ]; then
      if [ "$x" = "$la" ]; then install "$apka"; else install "$apkb"; fi
      current=$x
    fi
    bash "$here/run.sh" "$serial" "$pkg" "$x-$i" "$out" "$rx" "$ry" "$sx" "$sy" 28
  done
done
echo ALL-DONE
