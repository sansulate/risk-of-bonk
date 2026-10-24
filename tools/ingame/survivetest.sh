#!/usr/bin/env bash
# Balance check (needs testgod and testmortal: keys on, damage real): pick Megabonk survivor N (CELL=k: grid cell k,
# a Risk of Rain 2 survivor), Bonk style unless NOTOGGLE, start, leave the pod, then for SECONDS (default 180) run in
# wide circles (MOVEMS of running, then PAUSEMS standing still when set: stand and fight), turning, taking the first level-up card whenever one is offered; screenshots every minute; quit.
# The log shows "fight:" lines every 15 s (health, damage dealt/taken per second, kills, deaths, monsters alive).
# Usage: survivetest.sh N outPrefix
set -e
cd "$(dirname "$0")"
I=$(cygpath -m "$(pwd)")
ps() { powershell -ExecutionPolicy Bypass -File "$I/$1" "${@:2}"; }
out() { cygpath -w "$2_$1.png"; }
ps click.ps1 -X 255 -Y 554; sleep 26
if [ -n "$CELL" ]; then GRID_START=0 bash pick.sh "$CELL" "$2_select.png" 4 >/dev/null; else bash pick.sh "$1" "$2_select.png" 4 >/dev/null; fi
[ -n "$NOTOGGLE" ] || { ps click.ps1 -X 1030 -Y 940; sleep 2; }
ps click.ps1 -X 1030 -Y 868; sleep 40
ps key.ps1 -Key e; sleep 4
end=$(( $(date +%s) + ${SECONDS_:-180} )); n=0
while [ "$(date +%s)" -lt "$end" ]; do
  ps hold.ps1 -Vks "0x57,0x44" -Ms ${MOVEMS:-2500} -TapVk 0x31 -TapEveryMs 1200 >/dev/null 2>&1 || true
  if [ -n "$PAUSEMS" ]; then ps key.ps1 -Key 1 >/dev/null 2>&1; sleep $(awk "BEGIN{print $PAUSEMS/1000}"); fi
  ps look.ps1 -Dx 250 >/dev/null 2>&1 || true
  n=$((n+1)); if [ $((n % 20)) -eq 0 ]; then ps shot.ps1 -Out "$(out m$((n/20)) "$2")" >/dev/null; fi
done
ps shot.ps1 -Out "$(out end "$2")" >/dev/null
ps key.ps1 -Key escape; sleep 2
ps click.ps1 -X 960 -Y 655; sleep 2
ps click.ps1 -X 811 -Y 604; sleep 25
