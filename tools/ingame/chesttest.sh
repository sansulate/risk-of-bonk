#!/usr/bin/env bash
# Chest check (needs testgod): start a run as Megabonk survivor N, leave the drop pod, open the nearest chests with F2
# (CHESTS, default 6), screenshot, quit. The log shows "chest: ... gave ... an upgrade pick" for chests that held an upgrade.
# Usage: chesttest.sh N outPrefix
set -e
cd "$(dirname "$0")"
I=$(cygpath -m "$(pwd)")
ps() { powershell -ExecutionPolicy Bypass -File "$I/$1" "${@:2}"; }
ps click.ps1 -X 255 -Y 554; sleep 26
bash pick.sh "$1" "$2_select.png" 4 >/dev/null
ps click.ps1 -X 1030 -Y 868; sleep 32
ps key.ps1 -Key e; sleep 6
for i in $(seq 1 "${CHESTS:-6}"); do ps key.ps1 -Key F2; sleep 3; done
ps shot.ps1 -Out "$(cygpath -w "$2_end.png")" >/dev/null
ps key.ps1 -Key escape; sleep 2
ps click.ps1 -X 960 -Y 655; sleep 2
ps click.ps1 -X 811 -Y 604; sleep 25
