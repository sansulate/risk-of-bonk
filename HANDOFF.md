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
- Still open: projectile speed/bounce/duration upgrades; co-op of the per-slot weapon state machines with two players;
  Bonk style terrain (BonkTerrain.cs) wanted by the user: bigger maps at Megabonk's size and scale, Megabonk-like
  generation, nicer look, and no getting stuck on trees/rocks (Megabonk's generator: MapGenerationController,
  MapGenerator, ProceduralTiles namespace, MazeHeightGenerator in the Il2CppDumper dump) - not started.
- Melty publishing: only with the user's go-ahead (they paste a fresh Publish prompt; never save its token).
