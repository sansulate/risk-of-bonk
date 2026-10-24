#!/usr/bin/env bash
# Bonk style terrain check: from the main menu pick Megabonk survivor N, switch the lobby to Bonk style, start, leave
# the drop pod, then screenshots turning around (4 directions, looking a little down), and STAGES-1 more stages with
# F12 (needs testgod); quit back to the main menu. The log shows "Bonk style:" lines.
# Usage: terraintest.sh N outPrefix
set -e
cd "$(dirname "$0")"
I=$(cygpath -m "$(pwd)")
ps() { powershell -ExecutionPolicy Bypass -File "$I/$1" "${@:2}"; }
ps click.ps1 -X 255 -Y 554; sleep 26
bash pick.sh "$1" "$2_select.png" 4 >/dev/null
[ -n "$NOTOGGLE" ] || { ps click.ps1 -X 1030 -Y 940; sleep 2; }
ps shot.ps1 -Out "$(cygpath -w "$2_lobby.png")" >/dev/null
ps click.ps1 -X 1030 -Y 868; sleep 40
ps key.ps1 -Key e; sleep 6
for s in $(seq 1 "${STAGES:-1}"); do
  ps look.ps1 -Dy 150 >/dev/null
  for d in 1 2 3 4; do ps shot.ps1 -Out "$(cygpath -w "$2_s${s}_$d.png")" >/dev/null; ps look.ps1 -Dx 900 >/dev/null; sleep 1; done
  ps look.ps1 -Dy -150 >/dev/null
  ps holdshot.ps1 -Keys W -HoldMs 4000 -Out "$(cygpath -w "$2_s${s}_run.png")" >/dev/null
  if [ "$s" -lt "${STAGES:-1}" ]; then ps key.ps1 -Key F12; sleep 30; fi
done
ps key.ps1 -Key escape; sleep 2
ps click.ps1 -X 960 -Y 655; sleep 2
ps click.ps1 -X 811 -Y 604; sleep 25
