#!/usr/bin/env bash
# Rebuild the plugin, copy it into the BepInEx install and (re)start Risk of Rain 2 with BepInEx.
# The install is BONK_BEPINEX, else the game folder's own BepInEx (as Melty sets it up), else the r2modman "Default" profile.
# Waits until the mod has registered its survivors, then prints the mod's log lines.
set -e
cd "$(dirname "$0")/../.."
GAME="${RISKOFRAIN2:-/c/Program Files (x86)/Steam/steamapps/common/Risk of Rain 2}"
PROF="$(bash tools/ingame/bepinex.sh)"
python tools/sheets.py generate | tail -1
OUT=$(dotnet build src/RiskOfBonk.csproj -c Release -nologo -v q -p:BepInExCore="$PROF/core" -p:RoR2Managed="$(cygpath -m "$GAME/Risk of Rain 2_Data/Managed")" 2>&1); echo "$OUT" | grep -E "error|Build succeeded" | sort -u | head; echo "$OUT" | grep -q " error " && exit 1
taskkill //IM "Risk of Rain 2.exe" //F >/dev/null 2>&1 && sleep 5 || true
mkdir -p "$PROF/plugins/RiskOfBonk"
cp src/bin/Release/netstandard2.1/RiskOfBonk.dll lib/AssetsTools.NET.dll lib/NVorbis.dll lib/Newtonsoft.Json.dll lib/classdata.tpk README.md THIRD_PARTY_NOTICES.md LICENSE "$PROF/plugins/RiskOfBonk/"
rm -f "$PROF/RiskOfBonk/debug/"* "$PROF/LogOutput.log" 2>/dev/null || true
if [ "$PROF" -ef "$GAME/BepInEx" ]; then "/c/Program Files (x86)/Steam/steam.exe" -applaunch 632360 &
else "/c/Program Files (x86)/Steam/steam.exe" -applaunch 632360 --doorstop-enabled true --doorstop-target-assembly "$(cygpath -w "$PROF/core/BepInEx.Preloader.dll")" & fi
sleep 15
for i in $(seq 1 60); do grep -q "Main menu: Megabonk style" "$PROF/LogOutput.log" 2>/dev/null && break; sleep 3; done
grep -E "Risk of Bonk\]|Exception" "$PROF/LogOutput.log" | grep -vE "RoR2BepInExPack|without a usable bind pose" | cut -c1-240
