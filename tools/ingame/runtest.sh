#!/usr/bin/env bash
# From the main menu: start a singleplayer run as Megabonk survivor N, leave the drop pod, take screenshots of
# standing, running (W) and attacking (left mouse), then quit back to the main menu.
# Usage: runtest.sh N outPrefix [select]   (select: already on the character select screen)
set -e
cd "$(dirname "$0")"
I=$(cygpath -m "$(pwd)")
ps() { powershell -ExecutionPolicy Bypass -File "$I/$1" "${@:2}"; }
out() { cygpath -w "$2_$1.png"; }
[ "$3" = select ] || { ps click.ps1 -X 255 -Y 554; sleep 26; }
bash pick.sh "$1" "$2_select.png" 4 >/dev/null
ps click.ps1 -X 1030 -Y 868; sleep 32
ps key.ps1 -Key e; sleep 6
ps shot.ps1 -Out "$(out idle "$2")" >/dev/null
ps holdshot.ps1 -Keys W -HoldMs 1500 -Out "$(out run "$2")" >/dev/null
ps holdshot.ps1 -Keys "" -Click -HoldMs 600 -Out "$(out attack "$2")" >/dev/null
powershell -ExecutionPolicy Bypass -Command "Add-Type -Name K -Namespace W -MemberDefinition '[DllImport(\"user32.dll\")] public static extern void keybd_event(byte vk, byte scan, uint f, UIntPtr e);'; [W.K]::keybd_event(0x1B,1,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 80; [W.K]::keybd_event(0x1B,1,2,[UIntPtr]::Zero)"
sleep 2
ps click.ps1 -X 960 -Y 655; sleep 2
ps click.ps1 -X 811 -Y 604; sleep 25
ps shot.ps1 -Out "$(out after "$2")" >/dev/null
