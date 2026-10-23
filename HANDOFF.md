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
- Still open toward "play like Megabonk": weapon visuals are Risk of Rain 2 effects (Megabonk's are particles);
  projectile speed/size/bounce/duration upgrades and character passives other than WallClimb are not applied;
  Megabonk's per-character health/armor/etc. stat modifiers (tools/mbdata/characters.py) are not applied; co-op of
  the per-slot weapon state machines not tested with two players.
- Melty publishing: only with the user's go-ahead (they paste a fresh Publish prompt; never save its token).
