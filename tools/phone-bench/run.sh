#!/bin/bash
# One measured fake-mic recording on a phone (HANDOFF §8d, §8i): the Release profiling build is
# already installed and files/fake-mic.wav is in place. Cold start with an empty takes list,
# record for <seconds> while sampling SurfaceFlinger's frame times, Stop, then save the probes.
# The CPU time of each of the app's threads is snapshotted just after the record tap and just
# before the stop tap (<label>.threads1 / .threads2: name, utime, stime in clock ticks), and the
# whole device's (<label>.cpu1 / .cpu2: the first line of /proc/stat).
# Usage: run.sh <serial> <package> <label> <out-dir> <record-x> <record-y> <stop-x> <stop-y> [seconds]
set -u
export MSYS_NO_PATHCONV=1
ADB=${ADB:-"$LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe"}
serial=$1; pkg=$2; label=$3; out=$4; rx=$5; ry=$6; sx=$7; sy=$8; secs=${9:-28}
files=/sdcard/Android/data/$pkg/files
mkdir -p "$out"
a() { "$ADB" -s "$serial" "$@"; }

threads() {
  local pid
  pid=$(a shell pidof "$pkg" | tr -d '\r')
  a shell "for t in /proc/$pid/task/*; do echo \"\${t##*/} \$(cut -d' ' -f14,15 \$t/stat) \$(cat \$t/comm)\"; done" | tr -d '\r' > "$1"
  echo "pid $pid" >> "$1"
}

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
threads "$out/$label.threads1"
a shell head -1 /proc/stat | tr -d '\r' > "$out/$label.cpu1"
: > "$out/$label.latency"
end=$((SECONDS + secs))
while [ $SECONDS -lt $end ]; do
  sleep 1.5
  a shell dumpsys SurfaceFlinger --latency "'$layer'" >> "$out/$label.latency"
  echo "--" >> "$out/$label.latency"
done
threads "$out/$label.threads2"
a shell head -1 /proc/stat | tr -d '\r' > "$out/$label.cpu2"
a shell input tap "$sx" "$sy"
sleep 15
a logcat -d -v epoch -s Timbratune > "$out/$label.logcat"
a exec-out screencap -p > "$out/$label.png"
echo "$label done: layer $layer"
