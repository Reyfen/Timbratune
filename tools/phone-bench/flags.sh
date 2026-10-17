#!/bin/bash
# The same build with different experiment switches (Perf.Flags, read from files/perf-flags.txt at
# startup), in rotating order, N rounds: what each part of the work costs. "none" = no switches.
# Usage: flags.sh <serial> <package> <apk> <wav> <out-dir> <record-x> <record-y> <stop-x> <stop-y> <rounds> <variant>…
#   a variant is a label=flag,flag (e.g. none= noease=noease nocharts=nocharts)
set -u
export MSYS_NO_PATHCONV=1
ADB=${ADB:-"$LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe"}
here=$(cd "$(dirname "$0")" && pwd)
serial=$1; pkg=$2; apk=$3; wav=$4; out=$5; rx=$6; ry=$7; sx=$8; sy=$9; rounds=${10}
shift 10
variants=("$@")
a() { "$ADB" -s "$serial" "$@"; }

lock="${TMPDIR:-/tmp}/phone-bench-$serial.lock"
if ! mkdir "$lock" 2>/dev/null; then echo "another driver holds $lock"; exit 1; fi
trap 'rmdir "$lock"; a shell rm -f "/sdcard/Android/data/$pkg/files/perf-flags.txt"' EXIT
mkdir -p "$out"

a install -r -g "$apk" | grep -v -e Success -e Streamed
a shell monkey -p "$pkg" -c android.intent.category.LAUNCHER 1 > /dev/null 2>&1
sleep 8
a shell am force-stop "$pkg"
a push "$wav" "/sdcard/Android/data/$pkg/files/fake-mic.wav" > /dev/null || { echo "push failed"; exit 1; }

n=${#variants[@]}
for i in $(seq 1 "$rounds"); do
  for k in $(seq 0 $((n - 1))); do
    v=${variants[$(((k + i - 1) % n))]}
    label=${v%%=*}; flags=${v#*=}
    saver=$(a shell settings get global low_power | tr -d '\r')
    level=$(a shell dumpsys battery | tr -d '\r' | awk '/ level:/ {print $2}')
    echo "$label-$i battery $level% saver $saver flags '$flags'"
    if [ "$saver" = 1 ]; then echo "STOPPED: Battery Saver is on (runs would be throttled)"; exit 2; fi
    if [ -n "$flags" ]; then
      echo "${flags//,/ }" > "$out/perf-flags.txt"
      a push "$out/perf-flags.txt" "/sdcard/Android/data/$pkg/files/perf-flags.txt" > /dev/null
    else
      a shell rm -f "/sdcard/Android/data/$pkg/files/perf-flags.txt"
    fi
    bash "$here/run.sh" "$serial" "$pkg" "$label-$i" "$out" "$rx" "$ry" "$sx" "$sy" 28
  done
done
echo ALL-DONE
