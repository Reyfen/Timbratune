#!/usr/bin/env bash
# Dev aid for checking the Linux build inside WSL (WSLg) or any X11 session, without a person.
#   linux-ui.sh start <exe> [data-dir] [fake-mic.wav]   launch the app in the background
#   linux-ui.sh shot <out.png>                           screenshot the Euphonia window
#   linux-ui.sh click <x> <y>                            click at window-relative pixels
#   linux-ui.sh scroll <notches>                         scroll down (negative = up)
#   linux-ui.sh stop                                     close the app
# Clicks go to the X window only (xdotool), not to the Windows desktop's mouse.
set -euo pipefail

win() { xdotool search --name '^Euphonia$' | head -1; }

case "${1:-}" in
  start)
    exe="$2"; data="${3:-$HOME/eu-data}"; mic="${4:-}"
    mkdir -p "$data"
    (cd "$(dirname "$exe")" && EUPHONIA_DATA_DIR="$data" ${mic:+EUPHONIA_FAKE_MIC="$mic"} \
      nohup "$exe" > "$HOME/eu.log" 2>&1 &)
    for _ in $(seq 1 30); do [ -n "$(win)" ] && break; sleep 1; done
    w="$(win)"; [ -n "$w" ] || { echo "no window; log:"; tail -40 "$HOME/eu.log"; exit 1; }
    echo "window $w"
    ;;
  shot)
    w="$(win)"; xwininfo -id "$w" | grep -E 'Width|Height' | tr '\n' ' '; echo
    import -window "$w" "$2" && echo "saved $2"
    ;;
  click)
    w="$(win)"; xdotool mousemove --window "$w" "$2" "$3"; sleep 0.3
    xdotool mousedown 1; sleep 0.1; xdotool mouseup 1; sleep 0.8
    ;;
  scroll)
    w="$(win)"; n="$2"; b=5; [ "$n" -lt 0 ] && { b=4; n=$(( -n )); }
    xdotool mousemove --window "$w" 600 400
    for _ in $(seq 1 "$n"); do xdotool click "$b"; sleep 0.05; done; sleep 0.6
    ;;
  stop)
    pkill -x Euphonia.Deskto || true   # process names are cut to 15 characters
    ;;
  *)
    sed -n '2,8p' "$0"; exit 2
    ;;
esac
