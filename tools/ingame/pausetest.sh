#!/usr/bin/env bash
# Level-up pause check (needs testgod): pick Megabonk survivor N, start (NOTOGGLE to keep the current style), F8 for a
# level, screenshots 2 s apart while the menu is open (the run timer must not move), pick card 1, screenshots 2 s apart
# (timer moves again), quit.  Usage: pausetest.sh N outPrefix
set -e
cd "$(dirname "$0")"
I=$(cygpath -m "$(pwd)")
ps() { powershell -ExecutionPolicy Bypass -File "$I/$1" "${@:2}"; }
out() { cygpath -w "$2_$1.png"; }
ps click.ps1 -X 255 -Y 554; sleep 26
bash pick.sh "$1" "$2_select.png" 4 >/dev/null
[ -n "$NOTOGGLE" ] || { ps click.ps1 -X 1030 -Y 940; sleep 2; }
ps click.ps1 -X 1030 -Y 868; sleep 40
ps key.ps1 -Key F8; sleep 2
ps shot.ps1 -Out "$(out open1 "$2")" >/dev/null; sleep 2
ps shot.ps1 -Out "$(out open2 "$2")" >/dev/null
ps key.ps1 -Key 1; sleep 2
ps shot.ps1 -Out "$(out after1 "$2")" >/dev/null; sleep 2
ps shot.ps1 -Out "$(out after2 "$2")" >/dev/null
ps key.ps1 -Key escape; sleep 2
ps click.ps1 -X 960 -Y 655; sleep 2
ps click.ps1 -X 811 -Y 604; sleep 25
