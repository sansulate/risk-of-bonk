#!/usr/bin/env bash
# Teleporter look check (needs testgod): pick Megabonk survivor N, Bonk style, start, leave the pod, F9 (to the
# Teleporter, activates it), back off and screenshot it from a few places, F3 to clear monsters, quit.
# Usage: tptest.sh N outPrefix
set -e
cd "$(dirname "$0")"
I=$(cygpath -m "$(pwd)")
ps() { powershell -ExecutionPolicy Bypass -File "$I/$1" "${@:2}"; }
out() { cygpath -w "$2_$1.png"; }
ps click.ps1 -X 255 -Y 554; sleep 26
bash pick.sh "$1" "$2_select.png" 4 >/dev/null
[ -n "$NOTOGGLE" ] || { ps click.ps1 -X 1030 -Y 940; sleep 2; }
ps click.ps1 -X 1030 -Y 868; sleep 40
ps key.ps1 -Key e; sleep 6
ps key.ps1 -Key F9; sleep 3
ps shot.ps1 -Out "$(out tp0 "$2")" >/dev/null
ps hold.ps1 -Vks 0x53 -Ms 1500 >/dev/null 2>&1 || true
ps shot.ps1 -Out "$(out tp1 "$2")" >/dev/null
ps look.ps1 -Dy -200 >/dev/null; sleep 1
ps shot.ps1 -Out "$(out tp2 "$2")" >/dev/null
ps look.ps1 -Dy 200 >/dev/null
ps hold.ps1 -Vks 0x53 -Ms 2500 >/dev/null 2>&1 || true
ps shot.ps1 -Out "$(out tp3 "$2")" >/dev/null
ps key.ps1 -Key escape; sleep 2
ps click.ps1 -X 960 -Y 655; sleep 2
ps click.ps1 -X 811 -Y 604; sleep 25
