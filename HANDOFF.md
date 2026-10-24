# Handoff

State (2026-10-24, 0.2.6 + later commits): see MODLOG.md updates 16-36. The design and v1 scope are settled; do not
re-ask the user about them.

- Install on the PC with both games: one copy only, in the game folder's own BepInEx (plain Steam launch plays the mod).
  `bash tools/ingame/redeploy.sh` builds, copies the plugin folder there and relaunches (needs the .NET 8 SDK and
  Python). Test switches are files in `BepInEx/RiskOfBonk/` (testgod, testmove, testweapons, ...): create them for a
  test session and delete them afterwards (testgod makes the player unkillable).
- Character grid: GRID_START (cells before Megachad) is 18 with every DLC survivor showing, 11 without.
- Megabonk ground truth: tools/mbdata (README): Il2CppDumper output + raw asset readers + dis.py. Weapons, characters,
  weapon upgrades, movement, crouch, wall climb, model tilt and air deceleration now come from it.
- Passives and character stats: src/Passives.cs (MODLOG 23). Weapon effects: Megabonk's own particle prefabs rebuilt by
  src/MegabonkFx.cs (MODLOG 24); first pass, mbFxScale per weapon needs tuning by eye (tools/ingame/fxtest.sh with F7
  slow motion), and only revolver/sword/shotgun/aura were looked at in game.
- Bonk style is now Megabonk's own map generator, lighting, props, enemies and music (MODLOG 26-28), its chests, pots
  and shrine statues (MODLOG 30), no drop pod and no leftover stage particles (MODLOG 34); chests can give weapon/tome
  picks (MODLOG 25). Megabonk HUD (top left: health, weapons or RoR2 skills with cooldowns, tomes) replaces RoR2's
  skill icons, health bar and level display (MODLOG 31-32).
- Balance (MODLOG 29, 33, 35, 36): src/Balance.cs scales Megabonk weapon damage to a Risk of Rain 2 kit's power
  (tuning balance_*), Megabonk survivors use Commando's base stats; hordes follow Megabonk's StageSummoner target
  (tuning horde_target_*, horde_density). Compare survivors with tools/ingame/survivetest.sh and the test files
  testgod + testmortal + testbot (bot aims and fires for RoR2 survivors); "fight:" lines every 15 s. The circling bot
  plays close-range survivors (Megachad's aura) badly: it dies at about 55 s where Commando and CL4NK survive 4 min.
- Test scripts: tools/ingame/terraintest.sh, stucktest.sh, enemytest.sh (testspawn takes a comma list), chesttest.sh
  (F2 opens the nearest chest), hudtest.sh (F1 visits interactables), survivetest.sh, tptest.sh.
- Still open: projectile speed/bounce/duration upgrades; co-op with two players (per-slot weapon machines, enemy
  looks, interactable looks and music are local and deterministic, untested together); Megabonk's desert/hell maps
  (noise-mesh MapGenerator, not tile mazes) are not used; effect scales (mbFxScale) want tuning by eye; the Teleporter
  keeps its Risk of Rain 2 look (Megabonk's BossSpawner prefab is a 43 m set piece); Risk of Rain 2's ranged monsters
  wear melee Megabonk looks and are hard on close-range survivors.
- Melty publishing: only with the user's go-ahead (they paste a fresh Publish prompt; never save its token).
