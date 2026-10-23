#!/usr/bin/env bash
# Weapon check (needs testgod): from the main menu start a run as Megabonk survivor N, leave the drop pod, give the
# weapons listed in BepInEx/RiskOfBonk/testweapons with F11 (WEAPONS, default 3), then stand still for SECONDS (default 15)
# without pressing any attack key, with screenshots; quit back to the main menu. The log shows each weapon's attacks.
# Usage: weapontest.sh N outPrefix
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
ps holdshot.ps1 -Keys W -HoldMs 1500 -Out "$(out run "$2")" >/dev/null
sleep $(( ${SECONDS_:-15} / 2 ))
ps shot.ps1 -Out "$(out mid "$2")" >/dev/null
sleep $(( ${SECONDS_:-15} / 2 ))
ps shot.ps1 -Out "$(out end "$2")" >/dev/null
ps key.ps1 -Key escape
sleep 2
ps click.ps1 -X 960 -Y 655; sleep 2
ps click.ps1 -X 811 -Y 604; sleep 25
