#!/usr/bin/env bash
# Stage title check: pick survivor N, start (NOTOGGLE keeps the style), screenshot at 5, 7 and 9 s into the stage,
# F12 (needs testgod) and the same on the next stage, quit.  Usage: titletest.sh N outPrefix
set -e
cd "$(dirname "$0")"
I=$(cygpath -m "$(pwd)")
ps() { powershell -ExecutionPolicy Bypass -File "$I/$1" "${@:2}"; }
ps click.ps1 -X 255 -Y 554; sleep 26
bash pick.sh "$1" "$2_select.png" 4 >/dev/null
[ -n "$NOTOGGLE" ] || { ps click.ps1 -X 1030 -Y 940; sleep 2; }
ps click.ps1 -X 1030 -Y 868
for s in 1 2; do
  sleep ${WAIT:-20}
  for t in 1 2 3; do ps shot.ps1 -Out "$(cygpath -w "$2_s${s}_$t.png")" >/dev/null; sleep 2; done
  if [ $s -lt 2 ]; then sleep 5; ps key.ps1 -Key F12; fi
done
ps key.ps1 -Key escape; sleep 2
ps click.ps1 -X 960 -Y 655; sleep 2
ps click.ps1 -X 811 -Y 604; sleep 25
