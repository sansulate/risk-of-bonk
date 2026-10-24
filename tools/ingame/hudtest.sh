#!/usr/bin/env bash
# HUD, balance and Megabonk interactables check (needs testgod): pick Megabonk survivor N (or a grid cell with
# CELL=k for a Risk of Rain 2 survivor), Bonk style, start, leave the pod, screenshot the HUD, take LEVELS levels with
# F8 picking the first card each time, screenshot, use the special skill (R) for a cooldown shot, then
# visit INTERACT interactables with F1 (screenshot each), open the nearest chest with F2, quit.
# The log shows "Balance:" lines and "test: at ..." lines.  Usage: hudtest.sh N outPrefix
set -e
cd "$(dirname "$0")"
I=$(cygpath -m "$(pwd)")
ps() { powershell -ExecutionPolicy Bypass -File "$I/$1" "${@:2}"; }
out() { cygpath -w "$2_$1.png"; }
ps click.ps1 -X 255 -Y 554; sleep 26
if [ -n "$CELL" ]; then GRID_START=0 bash pick.sh "$CELL" "$2_select.png" 4 >/dev/null; else bash pick.sh "$1" "$2_select.png" 4 >/dev/null; fi
[ -n "$NOTOGGLE" ] || { ps click.ps1 -X 1030 -Y 940; sleep 2; }
ps click.ps1 -X 1030 -Y 868; sleep 40
ps key.ps1 -Key e; sleep 6
ps shot.ps1 -Out "$(out hud0 "$2")" >/dev/null
for i in $(seq 1 "${LEVELS:-4}"); do ps key.ps1 -Key F8; sleep 2; ps key.ps1 -Key 1; sleep 2; done
ps shot.ps1 -Out "$(out hud1 "$2")" >/dev/null
ps key.ps1 -Key r; sleep 0.5
ps shot.ps1 -Out "$(out hud2 "$2")" >/dev/null
for i in $(seq 1 "${INTERACT:-6}"); do ps key.ps1 -Key F1; sleep 2; ps shot.ps1 -Out "$(out int$i "$2")" >/dev/null; done
ps key.ps1 -Key F2; sleep 3
ps shot.ps1 -Out "$(out chest "$2")" >/dev/null
ps key.ps1 -Key escape; sleep 2
ps click.ps1 -X 960 -Y 655; sleep 2
ps click.ps1 -X 811 -Y 604; sleep 25
