#!/bin/bash
# One measured fake-mic recording on a phone (HANDOFF §8d, §8i): the Release profiling build is
# already installed and files/fake-mic.wav is in place. Cold start with an empty takes list,
# record for <seconds> while sampling SurfaceFlinger's frame times, Stop, then save the probes.
# Usage: run.sh <serial> <package> <label> <out-dir> <record-x> <record-y> <stop-x> <stop-y> [seconds]
set -u
export MSYS_NO_PATHCONV=1
ADB=${ADB:-"$LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe"}
serial=$1; pkg=$2; label=$3; out=$4; rx=$5; ry=$6; sx=$7; sy=$8; secs=${9:-28}
files=/sdcard/Android/data/$pkg/files
mkdir -p "$out"
a() { "$ADB" -s "$serial" "$@"; }

a shell am force-stop "$pkg"
a shell rm -rf "$files/profiling-data"       # the same empty takes list for every run
a logcat -c
a shell monkey -p "$pkg" -c android.intent.category.LAUNCHER 1 > /dev/null 2>&1
sleep 12
# Older Android lists the layer by name; Android 17 as "RequestedLayerState{<name> parentId=…}".
# The newest entry is this launch's.
pattern="SurfaceView\[${pkg//./\\.}/.*\(BLAST\)"
layer=$(a shell dumpsys SurfaceFlinger --list | tr -d '\r' | grep -E "$pattern" | tail -1 \
    | sed -E 's/^RequestedLayerState\{(.*) parentId=.*\}$/\1/')
[ -n "$layer" ] || { echo "no layer for $pkg"; a shell dumpsys SurfaceFlinger --list | grep -i timbratune; exit 1; }
a shell dumpsys SurfaceFlinger --latency-clear "'$layer'" > /dev/null
a shell input tap "$rx" "$ry"
: > "$out/$label.latency"
end=$((SECONDS + secs))
while [ $SECONDS -lt $end ]; do
  sleep 1.5
  a shell dumpsys SurfaceFlinger --latency "'$layer'" >> "$out/$label.latency"
  echo "--" >> "$out/$label.latency"
done
a shell input tap "$sx" "$sy"
sleep 15
a logcat -d -v epoch -s Timbratune > "$out/$label.logcat"
a exec-out screencap -p > "$out/$label.png"
echo "$label done: layer $layer"
