#!/usr/bin/env bash
# Prints the BepInEx folder the test tools use: BONK_BEPINEX, else the game folder's own BepInEx, else r2modman's "Default" profile.
GAME="${RISKOFRAIN2:-/c/Program Files (x86)/Steam/steamapps/common/Risk of Rain 2}"
if [ -n "$BONK_BEPINEX" ]; then echo "$BONK_BEPINEX"
elif [ -f "$GAME/BepInEx/core/BepInEx.Preloader.dll" ]; then echo "$GAME/BepInEx"
else echo "$APPDATA/r2modmanPlus-local/RiskOfRain2/profiles/Default/BepInEx"; fi
