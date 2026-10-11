# Risk of Bonk

A Risk of Rain 2 × Megabonk mashup. You play **Risk of Rain 2** (the host game, online co-op intact), and **Megabonk**
brings its characters and its look into it, read from your own Megabonk install every time the game starts.

## What you get (v0.1, "characters + menus")

- **Megabonk characters as Risk of Rain 2 survivors.** Each one uses its real Megabonk model and textures (loaded from
  your Megabonk folder, never copied or shipped) fitted onto a Risk of Rain 2 survivor's skeleton, and plays that
  survivor's moveset: Megachad punches with Loader's gauntlets, Fox throws Huntress's glaives, and so on. The full
  list is in `sheets/characters.json`.
- **Megabonk-style main menu.** A "RISK OF BONK" title with Megabonk's logo when it is found, chunky outlined text,
  dark panels and bright buttons. Every Risk of Rain 2 option still works, including online multiplayer.
- **Megabonk-style character select** with both rosters in one grid: the Risk of Rain 2 survivors and the Megabonk
  characters. Difficulty, artifacts and loadouts are unchanged.
- **Bonk style / Rain style button** on the character select screen. The host picks it and every player in the lobby
  sees the host's choice. *Rain style* keeps Risk of Rain 2's stages. *Bonk style* will add Megabonk-style random
  terrain generation in the next update; in v0.1 it is only the setting, and the stages stay the same.
- **Multiplayer:** Risk of Rain 2's own online co-op, up to 4 players. Every player needs the mod and both games.

## Planned next (update 2+)

Megabonk movement and physics controls, Megabonk's level-up upgrade system and weapons/tomes, horde pacing, the
Megabonk boss event at the Teleporter, Megabonk final bosses replacing Mithrix and the other final bosses, Bonk style
terrain generation, Megabonk shrines alongside Risk of Rain 2's shrines and chests, and endless looping.

## Requirements

- Risk of Rain 2 with BepInEx 5 (Melty installs it).
- Megabonk installed. Melty passes its folder in `RISKOFBONK_MEGABONK`. Without Melty, the mod looks in your Steam
  libraries, or you can set `MegabonkFolder` in `BepInEx/config/com.sansulate.riskofbonk.cfg`.

## How it works

- `sheets/*.json` are the design source of truth: characters, moveset bodies, menu style, stage modes, game hooks.
  `python3 tools/sheets.py preflight` lists unfilled cells, unverified cells and broken references between sheets;
  `generate` writes `src/Generated/Sheets.g.cs`.
- `MegabonkLibrary` reads Megabonk's Unity data files with AssetsTools.NET, using built-in Unity types only (models,
  materials, textures, sprites). Megabonk is IL2CPP, so no Megabonk code is loaded.
- `AutoRig` binds each Megabonk model to its moveset survivor's bones, so it plays that survivor's animations.
- `MegabonkSurvivors` clones the moveset survivor (network-registered with a stable asset id), swaps in the model and
  registers a SurvivorDef.
- On first start the mod writes `BepInEx/RiskOfBonk/megabonk_inventory.txt`, a list of Megabonk's model and texture
  names, which is used to check the `mbNameHints` in the character sheet.

## Build

```
BEPINEX_CORE=/path/to/BepInEx/core ./build.sh   # -> dist/RiskOfBonk-<version>.zip
```

The zip holds `RiskOfBonk/` for `BepInEx/plugins/`: the plugin DLL, AssetsTools.NET.dll and classdata.tpk. It holds no
files from either game.

## Credits

- Risk of Rain 2 by Hopoo Games / Gearbox. Megabonk by vedinad. This mod ships no files from either game.
- AssetsTools.NET and classdata.tpk by nesrak1 (MIT), see `THIRD_PARTY_NOTICES.md`.
- Built with AI assistance (Claude Code) for sansulate.
