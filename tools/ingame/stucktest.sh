#!/usr/bin/env bash
# Getting-stuck check on a Bonk style map (needs testgod and testmove): start a Bonk style run as survivor N, leave the
# pod, then hold W for SECONDS (default 40) turning a little every 2 s, so the run goes into trees, rocks and walls.
# The log's "move:" lines show the speed; stuck = holding W at under 1 m/s for long.
# Usage: stucktest.sh N outPrefix
set -e
cd "$(dirname "$0")"
I=$(cygpath -m "$(pwd)")
ps() { powershell -ExecutionPolicy Bypass -File "$I/$1" "${@:2}"; }
ps click.ps1 -X 255 -Y 554; sleep 26
bash pick.sh "$1" "$2_select.png" 4 >/dev/null
[ -n "$NOTOGGLE" ] || { ps click.ps1 -X 1030 -Y 940; sleep 2; }
ps click.ps1 -X 1030 -Y 868; sleep 40
ps key.ps1 -Key e; sleep 6
for i in $(seq 1 $(( ${SECONDS_:-40} / 2 ))); do
  ps hold.ps1 -Vks 0x57 -Ms 2000 >/dev/null 2>&1 &
  sleep 0.3; ps look.ps1 -Dx $(( (RANDOM % 600) - 300 )) -Steps 5 >/dev/null; wait
done
ps shot.ps1 -Out "$(cygpath -w "$2_end.png")" >/dev/null
ps key.ps1 -Key escape; sleep 2
ps click.ps1 -X 960 -Y 655; sleep 2
ps click.ps1 -X 811 -Y 604; sleep 25
