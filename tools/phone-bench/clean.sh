#!/bin/bash
# Removes what the bench put on the phone: the fake mic, the flags file and the profiling takes.
# Never uninstalls anything. Usage: clean.sh <serial> <package>
export MSYS_NO_PATHCONV=1
ADB=${ADB:-"$LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe"}
files=/sdcard/Android/data/$2/files
"$ADB" -s "$1" shell am force-stop "$2"
"$ADB" -s "$1" shell rm -rf "$files/fake-mic.wav" "$files/perf-flags.txt" "$files/profiling-data"
"$ADB" -s "$1" shell ls "$files"
