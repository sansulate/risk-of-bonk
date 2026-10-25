# Risk of Bonk

A Risk of Rain 2 × Megabonk mashup. You play **Risk of Rain 2** (the host game, online co-op intact), and **Megabonk**
brings its characters and its look into it, read from your own Megabonk install every time the game starts.

## What you get (v0.2.7)

- **20 Megabonk characters as Risk of Rain 2 survivors.** Each one uses its real Megabonk model, textures and
  animations (idle, run, jump, fall, slide; loaded from your Megabonk folder, never copied or shipped), its own
  Megabonk stats and passive (Megachad's Flex, Fox's RNG Blessing, Calcium's Speed Demon, Monke's wall climb, ...),
  and its Megabonk start weapon. The full list is in `sheets/characters.json`.
- **Megabonk weapons (Megabonk characters only)**, with Megabonk's own numbers: they **fire by themselves** at the
  nearest enemies, up to four at once, with Megabonk's own projectiles, effects and sounds. Picking a weapon you own
  levels it up with Megabonk's upgrades (damage, more projectiles, crit, bounces, size, speed). Damage is balanced
  against Risk of Rain 2's survivors. `sheets/weapons.json`.
- **Megabonk-style menus.** A "RISK OF BONK" title with Megabonk's logo and soundtrack, and a character select with
  both rosters in one grid (Megabonk passives shown). Every Risk of Rain 2 option still works.
- **Bonk style / Rain style** (the host picks it on the character select screen; everyone sees the host's choice).
  *Rain style* keeps Risk of Rain 2's stages. *Bonk style* plays Megabonk's own maps, generated the way Megabonk makes
  them: Forest 1, Desert 1, Forest 2, Desert 2, Graveyard, Forest 3, Desert 3, then looping. Each has Megabonk's trees,
  rocks, ruins and houses, its fog, lighting, music and ambience, Megabonk's enemies (Risk of Rain 2's monsters wear
  goblin, slime, skeleton, mummy and scorpion looks), its chests, pots and shrine statues, and its **Boss Portal** in
  place of the Teleporter. You start on the map with no drop pod, and the stage title names the Megabonk map. Both
  styles get a light Megabonk colour grade.
- **Megabonk movement**, measured from Megabonk's own code: always full speed, the **Sprint key slides** (or crouches
  when slow), momentum, bunny hops, air strafing, Megabonk's jump and gravity, model tilt on slopes, and Megabonk's
  fall damage (only big drops hurt).
- **Megabonk XP and level-ups.** Kills drop XP gems that fly to you (wider pickup for some characters). Every level-up
  pops up three cards, picked with **1, 2 or 3** (gamepad X, Y, B): Megabonk tomes (Damage, Cooldown, Precision, HP,
  Regen, Armor, Agility, Shield, Evasion, Luck) and, for Megabonk characters, weapons. Playing alone the game
  **pauses** while you choose, like Megabonk; in co-op you are invincible for 10 seconds instead. Chests can hold a
  bonus pick for Megabonk characters.
- **Megabonk HUD**: health bar, weapons (or Risk of Rain 2 skills with cooldowns) and tomes top left, and an XP bar
  across the top.
- **Megabonk shrines** on every stage: Charge, Magnet, Moai, Greed, Challenge and Cursed Shrines.
- **Megabonk hordes** that grow through the stage like Megabonk's, a **Megabonk boss event** at the Teleporter (Ancient
  Ent, Ghost Lord with a swarm and boss music), and **Megabonk final bosses**: Mithrix becomes the Ghost King and
  False Son the Shadow King, with swarms during final boss fights.
- **Endless:** Risk of Rain 2's looping stays, so a run goes on as long as you survive.
- **Multiplayer:** Risk of Rain 2's own online co-op through Steam lobbies, up to 4 players. Every player needs the mod
  and both games.

Settings (`BepInEx/config/com.sansulate.riskofbonk.cfg`, section Gameplay): MegabonkMovement, MegabonkHordes,
MegabonkBossEvent, MegabonkXp, MegabonkShrines, MegabonkLook, MegabonkMusic, MegabonkHud, MegabonkLevelUpPause.

## Planned next

Megabonk's Hell and Dungeon maps, Megabonk's duration upgrades, and more co-op testing.

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

The zip holds `RiskOfBonk/` for `BepInEx/plugins/`: the plugin DLL, AssetsTools.NET.dll, classdata.tpk and
Newtonsoft.Json.dll (Risk of Rain 2's code refers to it but the game does not ship it). It holds no files from either game.

## Credits

- Risk of Rain 2 by Hopoo Games / Gearbox. Megabonk by vedinad. This mod ships no files from either game.
- AssetsTools.NET and classdata.tpk by nesrak1 (MIT); Newtonsoft.Json by James Newton-King (MIT); NVorbis by Andrew Ward
  (MIT); the FSB5-to-Ogg method of python-fsb5 by Simon Pinfold (MIT). See
  `THIRD_PARTY_NOTICES.md`.
- Built with AI assistance (Claude Code) for sansulate.

## Melty

- Listed on Melty as "Risk of Bonk" (MIT, remixes allowed). Online co-op: up to 4 players through Risk of Rain 2's
  Steam lobbies. `MeltyLink` writes `Melty join address: <lobby>.steamlobby` to `%LOCALAPPDATA%/RiskOfBonk/melty.log`
  when you own a lobby, and joins the lobby in `join=` of `melty.properties` at the main menu.
- Test mode (`MeltyTest`, `MeltyTestRun`): while Melty's test file exists, the mod starts a run as a Megabonk survivor,
  fires its weapon, levels up and picks a card, and reports each check.

## License

MIT, see `LICENSE`.
