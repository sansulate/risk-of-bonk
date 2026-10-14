#!/usr/bin/env bash
# Builds the BepInEx plugin and packs the release zip (dist/RiskOfBonk-<version>.zip).
# Needs: .NET SDK, python3, and BEPINEX_CORE pointing at BepInEx 5's core folder (BepInEx.dll, 0Harmony.dll).
set -euo pipefail
cd "$(dirname "$0")"
: "${BEPINEX_CORE:?set BEPINEX_CORE to a BepInEx 5 core folder}"
python3 tools/sheets.py generate
# RISKOFRAIN2=<game folder> compiles against the installed game's own assemblies (publicized at build time);
# without it the older reference assemblies from nuget are used.
EXTRA=()
[ -n "${RISKOFRAIN2:-}" ] && EXTRA=(-p:RoR2Managed="$RISKOFRAIN2/Risk of Rain 2_Data/Managed")
dotnet build src/RiskOfBonk.csproj -c Release -nologo -v q -p:BepInExCore="$BEPINEX_CORE" "${EXTRA[@]}"
VER=$(grep -oP '(?<=<Version>)[^<]+' src/RiskOfBonk.csproj)
STAGE=dist/stage/RiskOfBonk
rm -rf dist/stage && mkdir -p "$STAGE"
cp src/bin/Release/netstandard2.1/RiskOfBonk.dll "$STAGE/"
cp lib/AssetsTools.NET.dll lib/classdata.tpk lib/Newtonsoft.Json.dll "$STAGE/"
cp README.md THIRD_PARTY_NOTICES.md LICENSE "$STAGE/"
(cd dist/stage && rm -f "../RiskOfBonk-$VER.zip" && zip -qr "../RiskOfBonk-$VER.zip" RiskOfBonk)
echo "dist/RiskOfBonk-$VER.zip"
