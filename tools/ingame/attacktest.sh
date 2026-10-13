#!/usr/bin/env bash
# From the main menu: start a singleplayer run as Megabonk survivor N, leave the drop pod and hold the primary attack
# twice (facing two directions), taking a screenshot mid-attack each time; then quit back to the main menu.
# With the testgod file the mod logs every weapon attack ("weapon <id> ..."), so check the log afterwards too.
# Usage: [SLOW=1] attacktest.sh N outPrefix   (SLOW: slow motion while attacking, needs testgod)
set -e
cd "$(dirname "$0")"
I=$(cygpath -m "$(pwd)")
ps() { powershell -ExecutionPolicy Bypass -File "$I/$1" "${@:2}"; }
out() { cygpath -w "$2_$1.png"; }
ps click.ps1 -X 255 -Y 554; sleep 26
bash pick.sh "$1" "$2_select.png" 4 >/dev/null
ps click.ps1 -X 1030 -Y 868; sleep 32
ps key.ps1 -Key e; sleep 5
ps hold.ps1 -Vks 0x57 -Ms 1500
[ -n "$SLOW" ] && ps hold.ps1 -Vks 0x76 -Ms 80  # F7: slow motion (testgod), to catch effects mid-swing
ps holdshot.ps1 -Keys "" -Click -HoldMs 1500 -Out "$(out a1 "$2")" >/dev/null
ps look.ps1 -Dx 900
ps holdshot.ps1 -Keys "" -Click -HoldMs 1500 -Out "$(out a2 "$2")" >/dev/null
[ -n "$SLOW" ] && ps hold.ps1 -Vks 0x76 -Ms 80
powershell -ExecutionPolicy Bypass -Command "Add-Type -Name K -Namespace W -MemberDefinition '[DllImport(\"user32.dll\")] public static extern void keybd_event(byte vk, byte scan, uint f, UIntPtr e);'; [W.K]::keybd_event(0x1B,1,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 80; [W.K]::keybd_event(0x1B,1,2,[UIntPtr]::Zero)"
sleep 2
ps click.ps1 -X 960 -Y 655; sleep 2
ps click.ps1 -X 811 -Y 604; sleep 25
