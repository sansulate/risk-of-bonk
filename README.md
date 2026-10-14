# Risk of Bonk

A Risk of Rain 2 × Megabonk mashup. You play **Risk of Rain 2** (the host game, online co-op intact), and **Megabonk**
brings its characters and its look into it, read from your own Megabonk install every time the game starts.

## What you get (v0.2, "characters, menus and the Megabonk game feel")

- **Megabonk characters as Risk of Rain 2 survivors.** Each one uses its real Megabonk model and textures (loaded from
  your Megabonk folder, never copied or shipped) fitted onto a Risk of Rain 2 survivor's skeleton (for its animations)
  and fights with Megabonk weapons (below). The full list is in `sheets/characters.json`.
- **Megabonk-style main menu.** A "RISK OF BONK" title with Megabonk's logo when it is found, chunky outlined text,
  dark panels and bright buttons. Every Risk of Rain 2 option still works, including online multiplayer.
- **Megabonk-style character select** with both rosters in one grid: the Risk of Rain 2 survivors and the Megabonk
  characters. Difficulty, artifacts and loadouts are unchanged.
- **Bonk style / Rain style button** on the character select screen. The host picks it and every player in the lobby
  sees the host's choice. *Rain style* keeps Risk of Rain 2's stages. *Bonk style* gives every normal stage a new
  Megabonk-style random map: low-poly hills with flat tops and steps, groves of trees, rocks and walls round the edge,
  coloured to fit the stage (grass, snow, sand, autumn, dusk) under the stage's own sky. The Teleporter, chests,
  shrines and monsters all use the new map; every player gets the same map. Hidden realms and the final stages stay
  as they are. Both styles also get a light Megabonk colour grade (more saturated and punchier, on top of each stage's
  own grading; setting MegabonkLook, numbers in `sheets/tuning.json`).
- **Megabonk movement.** You always run at full speed (no sprint toggle), and the **Sprint key slides**: a slide
  gives a burst of speed and keeps building speed down slopes. Momentum fades instead of stopping dead, bunny hops
  keep it, and air strafing steers without slowing you down. Jumps are a little higher. In the air, **hold Jump next to
  a wall to wallrun** along it; let go to kick off. Numbers in `sheets/tuning.json`.
- **Megabonk XP.** Kills drop XP gems on the ground; walk near them and they fly to you. A Megabonk XP bar runs
  across the top of the screen, pulses as gems land and bursts into "LEVEL UP!" when you level.
- **Megabonk level-up menu.** Every level-up pops up three cards, picked with **1, 2 or 3**: Megabonk tomes (Damage,
  Cooldown, Precision, HP, Regen, Armor, Agility, Shield, Evasion, Luck) with rarities from Common to Legendary, and
  for Megabonk characters also weapons (on a gamepad: X, Y, B). The game does not pause (co-op keeps going): you are **invincible for 10
  seconds** while the menu is open, shown as a countdown that turns red and shakes near the end; picking ends it.
- **Megabonk weapons (Megabonk characters only).** Each Megabonk character starts with its own Megabonk weapon on
  left mouse and nothing else. Weapons you pick go on right mouse, then Shift, then R: four weapons per run. Picking a
  weapon you own levels it up. Equipment (Q) works as usual. Risk of Rain 2 survivors keep their own movesets.
  `sheets/weapons.json` (25 weapons), start weapons in `sheets/characters.json`.
- **Megabonk shrines** on every stage, next to Risk of Rain 2's own shrines: **Charge Shrines** (stand in the golden
  circle until it is charged and every player gets an upgrade pick), a **Magnet Shrine** (blue: every XP gem on the
  map flies to you), a **Moai Shrine** (lilac: everyone gets a random tome), a **Greed Shrine** (green: more gold and
  more monsters for the rest of the stage), a **Challenge Shrine** (red: a wave of champions; beat it and everyone
  gets an upgrade pick) and a **Cursed Shrine** (purple: two upgrade picks for everyone now, but more monsters and more
  elites for the rest of the stage).
- **Megabonk hordes.** More monsters per wave, more often, each with less health.
- **Megabonk boss event at the Teleporter.** Instead of the vanilla boss, a Megabonk boss (Ancient Ent, Ghost Lord)
  arrives with a Megabonk swarm; the Teleporter finishes when it dies, with the normal boss rewards.
- **Megabonk final bosses.** Mithrix becomes the **Ghost King** and False Son the **Shadow King**: Megabonk models and
  names on their own fights, and like Megabonk's main bosses they bring a swarm: while a final boss (or Voidling) is
  alive, extra monsters keep coming. `sheets/bosses.json`.
- **Endless:** Risk of Rain 2's looping stays, so a run goes on as long as you survive.
- **Multiplayer:** Risk of Rain 2's own online co-op, up to 4 players. Every player needs the mod and both games.

Settings (`BepInEx/config/com.sansulate.riskofbonk.cfg`, section Gameplay): MegabonkMovement, MegabonkHordes,
MegabonkBossEvent, MegabonkXp, MegabonkShrines, MegabonkLook.

## Planned next

Megabonk chests alongside Risk of Rain 2's, Megabonk-styled
stage visuals, and Megabonk bosses for Voidling and
the other final bosses.

## Requirements

- Risk of Rain 2 with BepInEx 5 (Melty installs it).
- Megabonk installed. Melty passes its folder (`megabonk=` in `%LOCALAPPDATA%/RiskOfBonk/melty.properties`, or `RISKOFBONK_MEGABONK`). Without Melty, the mod looks in your Steam
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

The zip holds `RiskOfBonk/` and `RiskofThunder-RoR2BepInExPack/` (RoR2BepInExPack, unchanged, MIT), both for
`BepInEx/plugins/`. It holds no files from either game.

## Credits

- Risk of Rain 2 by Hopoo Games / Gearbox. Megabonk by vedinad. This mod ships no files from either game.
- AssetsTools.NET and classdata.tpk by nesrak1 (MIT); RoR2BepInExPack by Risk of Thunder (MIT) with Newtonsoft.Json by
  James Newton-King (MIT). See `THIRD_PARTY_NOTICES.md`.
- Built with AI assistance (Claude Code) for sansulate.

## Melty

- Listed on Melty as "Risk of Bonk" (MIT, remixes allowed). Online co-op: up to 4 players through Risk of Rain 2's
  Steam lobbies. `MeltyLink` writes `Melty join address: <lobby>.steamlobby` to `%LOCALAPPDATA%/RiskOfBonk/melty.log`
  when you own a lobby, and joins the lobby in `join=` of `melty.properties` at the main menu.
- Test mode (`MeltyTest`, `MeltyTestRun`): while Melty's test file exists, the mod starts a run as a Megabonk survivor,
  fires its weapon, levels up and picks a card, and reports each check.

## License

MIT, see `LICENSE`.
