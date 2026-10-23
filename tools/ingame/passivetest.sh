#!/usr/bin/env bash
# Passive check (needs testgod): from the main menu start a run as Megabonk survivor N, leave the drop pod, take LEVELS
# team levels with F8 (default 5), spawn SPAWNS monsters from BepInEx/RiskOfBonk/testspawn with F10 (default 3), then
# move around for SECONDS (default 30) so they reach you, with screenshots; quit back to the main menu.
# The log shows the "passive:" lines (stats per level, flexes, zaps, marks, executes, ...).
# Usage: passivetest.sh N outPrefix
set -e
cd "$(dirname "$0")"
I=$(cygpath -m "$(pwd)")
ps() { powershell -ExecutionPolicy Bypass -File "$I/$1" "${@:2}"; }
out() { cygpath -w "$2_$1.png"; }
ps click.ps1 -X 255 -Y 554; sleep 26
bash pick.sh "$1" "$2_select.png" 4 >/dev/null
ps click.ps1 -X 1030 -Y 868; sleep 32
ps key.ps1 -Key e; sleep 6
for i in $(seq 1 "${LEVELS:-5}"); do ps key.ps1 -Key F8; sleep 1; done
for i in $(seq 1 "${SPAWNS:-3}"); do ps key.ps1 -Key F10; sleep 1; done
ps holdshot.ps1 -Keys W -HoldMs 1500 -Out "$(out run "$2")" >/dev/null
for i in $(seq 1 $(( ${SECONDS_:-30} / 6 ))); do
  ps hold.ps1 -Vks "${MOVE:-0x41}" -Ms ${HOLDMS:-1000} >/dev/null 2>&1 || true
  sleep ${PAUSE:-5}
done
ps shot.ps1 -Out "$(out end "$2")" >/dev/null
ps key.ps1 -Key escape
sleep 2
ps click.ps1 -X 960 -Y 655; sleep 2
ps click.ps1 -X 811 -Y 604; sleep 25
