#!/usr/bin/env bash
# Movement check (needs the testmove file, which logs speed and height every 0.1 s): from the main menu start a run as
# Megabonk survivor N, then run, let go, slide, bunny hop and jump in place; quit back to the main menu.
# Usage: movetest.sh N outPrefix
set -e
cd "$(dirname "$0")"
I=$(cygpath -m "$(pwd)")
ps() { powershell -ExecutionPolicy Bypass -File "$I/$1" "${@:2}"; }
ps click.ps1 -X 255 -Y 554; sleep 26
bash pick.sh "$1" "$2_select.png" 4 >/dev/null
[ "$STYLE" = bonk ] && { ps click.ps1 -X 1030 -Y 939; sleep 2; }
ps click.ps1 -X 1030 -Y 868; sleep 32
ps key.ps1 -Key e; sleep 6
LOG="$APPDATA/r2modmanPlus-local/RiskOfRain2/profiles/Default/BepInEx/LogOutput.log"
step() { echo "STEP $1 $(date +%T.%N | cut -c1-12)" >> "$2_steps.txt"; }
: > "$2_steps.txt"
step run "$2"; ps hold.ps1 -Vks 0x57 -Ms 2500
step release "$2"; sleep 1.2
step run2 "$2"; ps hold.ps1 -Vks 0x57 -Ms 1500
step slide "$2"; ps hold.ps1 -Vks 0x57,0xA2 -Ms 1500
step bhop "$2"; ps hold.ps1 -Vks 0x57 -Ms 3500 -TapVk 0x20 -TapEveryMs 750
step stand "$2"; sleep 1.5
step jump "$2"; ps hold.ps1 -Vks 0x20 -Ms 100; sleep 1.5
step done "$2"
ps shot.ps1 -Out "$(cygpath -w "$2_end.png")" >/dev/null
powershell -ExecutionPolicy Bypass -Command "Add-Type -Name K -Namespace W -MemberDefinition '[DllImport(\"user32.dll\")] public static extern void keybd_event(byte vk, byte scan, uint f, UIntPtr e);'; [W.K]::keybd_event(0x1B,1,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 80; [W.K]::keybd_event(0x1B,1,2,[UIntPtr]::Zero)"
sleep 2
ps click.ps1 -X 960 -Y 655; sleep 2
ps click.ps1 -X 811 -Y 604; sleep 25
