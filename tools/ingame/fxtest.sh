#!/usr/bin/env bash
# Weapon effect check (needs testgod): from the main menu start a run as Megabonk survivor N, leave the drop pod, give
# the weapons in BepInEx/RiskOfBonk/testweapons with F11 (WEAPONS, default 3), spawn SPAWNS monsters from testspawn
# (default 4) with F10, wait for them to come close, then slow motion (F7) and SHOTS screenshots (default 6) half a
# second apart; quit back to the main menu.
# Usage: fxtest.sh N outPrefix
set -e
cd "$(dirname "$0")"
I=$(cygpath -m "$(pwd)")
ps() { powershell -ExecutionPolicy Bypass -File "$I/$1" "${@:2}"; }
out() { cygpath -w "$2_$1.png"; }
ps click.ps1 -X 255 -Y 554; sleep 26
bash pick.sh "$1" "$2_select.png" 4 >/dev/null
ps click.ps1 -X 1030 -Y 868; sleep 32
ps key.ps1 -Key e; sleep 6
for i in $(seq 1 "${WEAPONS:-3}"); do ps key.ps1 -Key F11; sleep 1; done
for i in $(seq 1 "${SPAWNS:-4}"); do ps key.ps1 -Key F10; sleep 1; done
sleep "${WAIT:-6}"
ps key.ps1 -Key F7; sleep 0.5
for i in $(seq 1 "${SHOTS:-6}"); do ps shot.ps1 -Out "$(out $i "$2")" >/dev/null; sleep 0.5; done
ps key.ps1 -Key F7
ps key.ps1 -Key escape
sleep 2
ps click.ps1 -X 960 -Y 655; sleep 2
ps click.ps1 -X 811 -Y 604; sleep 25
