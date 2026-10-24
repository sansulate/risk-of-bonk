# Handoff

State (2026-10-23, 0.2.6 + later commits): see MODLOG.md updates 16-22. The design and v1 scope are settled; do not
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
- Bonk style is now Megabonk's own map generator, lighting, props, enemies and music (MODLOG 26-28); chests can
  give weapon/tome picks (MODLOG 25). Test scripts: tools/ingame/terraintest.sh, stucktest.sh, enemytest.sh,
  chesttest.sh (F2 opens the nearest chest).
- Still open: projectile speed/bounce/duration upgrades; co-op with two players (per-slot weapon machines, enemy
  looks and music are local and deterministic, untested together); Megabonk's desert/hell maps (noise-mesh
  MapGenerator, not tile mazes) are not used; enemy looks only checked for slimes (Lemurians) in game; effect scales
  (mbFxScale) want tuning by eye; Megabonk's chests, shrines and pots still look like Risk of Rain 2's.
- Melty publishing: only with the user's go-ahead (they paste a fresh Publish prompt; never save its token).
