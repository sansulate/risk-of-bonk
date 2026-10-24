#!/usr/bin/env bash
# Megabonk enemy look check (needs testgod): a Bonk style run as survivor N, F10 spawns SPAWNS monsters from
# testspawn in front, then screenshots (slow motion) of whatever comes; STAGES more stages with F12.
# Usage: enemytest.sh N outPrefix
set -e
cd "$(dirname "$0")"
I=$(cygpath -m "$(pwd)")
ps() { powershell -ExecutionPolicy Bypass -File "$I/$1" "${@:2}"; }
ps click.ps1 -X 255 -Y 554; sleep 26
bash pick.sh "$1" "$2_select.png" 4 >/dev/null
[ -n "$NOTOGGLE" ] || { ps click.ps1 -X 1030 -Y 940; sleep 2; }
ps click.ps1 -X 1030 -Y 868; sleep 40
ps key.ps1 -Key e; sleep 4
for s in $(seq 1 "${STAGES:-1}"); do
  for i in $(seq 1 "${SPAWNS:-3}"); do ps key.ps1 -Key F10; sleep 0.5; done
  sleep "${WAIT:-12}"
  ps key.ps1 -Key F7
  for i in 1 2 3; do ps shot.ps1 -Out "$(cygpath -w "$2_s${s}_$i.png")" >/dev/null; ps look.ps1 -Dx 700 >/dev/null; done
  ps key.ps1 -Key F7
  if [ "$s" -lt "${STAGES:-1}" ]; then ps key.ps1 -Key F12; sleep 35; fi
done
ps key.ps1 -Key escape; sleep 2
ps click.ps1 -X 960 -Y 655; sleep 2
ps click.ps1 -X 811 -Y 604; sleep 25
