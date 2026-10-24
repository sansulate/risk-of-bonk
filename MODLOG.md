# MODLOG: Risk of Bonk

## Design (agreed with the user)
- Host: Risk of Rain 2 (primary, BepInEx 5). Megabonk is a required companion, read from the player's own install.
- Online co-op is RoR2's own (up to 4 players).
- Full vision: Megabonk movement/physics controls, Megabonk characters with RoR2 movesets, Megabonk hordes and upgrade
  system, the Megabonk boss event at the Teleporter, Megabonk final bosses instead of Mithrix and the others, endless
  looping, Megabonk-style menus, a Bonk style (random terrain) / Rain style (RoR2 stages) toggle, and all shrines,
  chests and runes from both games.
- v1 scope picked by the user: **characters + menus first**.

## Route
- RoR2: BepInEx 5 plugin + HarmonyX, with no R2API dependency (content pack, prefab cloning, network ids and tokens done directly).
- Megabonk: Unity IL2CPP. Its built-in Unity assets are read with AssetsTools.NET plus classdata.tpk at runtime; no
  code is loaded.
- Models are auto-rigged onto the moveset survivor's bones at runtime (nearest bone segment, 2 weights).

## Versions
- Compiled against RiskOfRain2.GameLibs 1.3.1.275-r.0 (nuget.org) and UnityEngine.Modules 2021.3.33.
  The bepinex.dev feed (newer GameLibs) is blocked from the build container. The members the mod uses are older stable
  APIs; SkinDef fields are set through reflection because newer builds moved them into SkinDefParams.
- BepInEx 5.4.23.2 (reference only). AssetsTools.NET 3.0.5. classdata.tpk from UABEA v8.

## Status
- Builds: yes (`./build.sh`; on Windows the same steps by hand, Git Bash has no `zip`). Preflight: 0 errors,
  21 unverified cells (mbAssetsVerified per character + logo; set true only after each looks right in game).
- First run on the PC with both games (2026-10-11; RoR2 Unity 2021.3.45, BepInEx 5.4.21 via r2modman profile
  "Default"; Megabonk Unity 2023.2.22f1):
  - Loads, finds Megabonk (32 asset files, 12130 named assets). Megabonk-style main menu and character select apply.
  - All 20 Megabonk survivors register, each with its real Megabonk character-select icon as portrait.
    Model names from the real inventory: Megachad=Chad, Fox=FoxWizard, Sir Oofie=Knight, Tony McZoom=ModelHoverman,
    Spaceman=Astronaut, Vlad=BloodMage, CL4NK=RobotCowboy, Bush=Sniperman, Sir Chadwell=CorruptedKnight
    (sharedassets1 has a_skin_sirChadwell*; Corrupt* skins). Portraits are exact Texture2D names (`mbPortrait`).
    Logo: MegabonkLogo.
  - Fixed: newer RoR2 leaves RendererInfo.defaultMaterial empty (defaultMaterialAddress) and rejects
    Shader.Find("Standard"); materials now come from the address, a renderer, or HGStandard via Addressables.
  - Fixed: 20 extra survivors overflowed the grid onto the Overview/Skills tabs; the grid now adds columns and
    shrinks icons to keep the vanilla height. The cloned Bonk/Rain button no longer carries Ready's gamepad glyph
    or shortcut.
  - Character-select display models look right (seen: Calcium on Railgunner, Amog on Engineer).
  - Fixed: menu styling threw on not-yet-initialized TMP labels (outlineWidth), so the main menu stayed vanilla.
    Now: Megabonk logo, yellow buttons, chunky text.
  - Rig (2026-10-11): retargeting, not skinning by distance. The reader keeps each Megabonk model's own skeleton and
    skin weights (all 20 are skinned rigs: Hips, Leg/Knee/Foot|Toes, Torso, Neck/Head, Shoulder/Arm|Forearm|Elbow,
    Hand|Wrist|Wirst, with .L/.R). When a matched mesh object's bones live beside it (FBX armature), the model is
    built from the nearest ancestor holding them, still showing only that object's renderers. FBX roots keep their
    tilt (the -90 deg X axis conversion) and drop their yaw.
    In game (Retarget.cs): the Megabonk bones become transforms under the survivor model, posed like the survivor's
    bind pose (chest->head, upper arm->forearm->hand, thigh->shin->foot directions), and MegabonkRig copies each
    survivor bone's rotation away from its bind pose onto the mapped Megabonk bone every LateUpdate; the hips also
    follow the pelvis offset (scaled by leg length; legless Vlad ignores the vertical bob). Proportions stay
    Megabonk's. Survivor names: pelvis|hip|base, chest, head, upper_arm, lower_arm|forearm, hand, thigh, calf, foot;
    REX: front legs as legs, PlatformBase/HeadBase, its one arm. Every survivor (REX included) now retargets; the
    nearest-bone fit remains as fallback.
    Facing: every Megabonk rig faces +Z with .L at -X, like RoR2, so yawOffsetDeg is 0 (180 was turning them
    around); the yaw is derived from the rig and the sheet value is added on top.
    CharacterModel draws a renderer with one material, so multi-part models (CL4NK's revolvers, Ogre's axe,
    Calcium's skateboard) showed only their first part: each part is now its own renderer + RendererInfo.
    Newer builds need SkinDefParams on our SkinDef and give effect renderers (Engineer's sprint jets, REX's roots,
    Huntress/Railgunner/Acrid effects) their materials by address through the default skin: those are now assigned
    from the old skin (they were magenta). Sir Oofie uses mbTexture Knight_Default (its model carries the corrupted
    knight's material). Display prefabs get a soft fill light (character select is dark red).
    Checked in game: character select for all 20; runs (idle, run, attack) as Megachad, Monke, Ogre, CL4NK, Bush,
    Vlad. Acrid's moveset starts outside a drop pod, so an idle test character can die to the first enemy.
  - Debug: create BepInEx/RiskOfBonk/debug to dump each fit (model-space OBJ + skeleton names) and, 6 s after a
    model appears, the baked mesh and the list of visible renderers.
  - Test tools: tools/probe (offline: what the mod reads from Megabonk per character), tools/ingame (redeploy.sh
    rebuilds and relaunches the r2modman profile; sheet.ps1 makes a character select contact sheet; runtest.sh plays
    a short run as one survivor; screenshots go to .work/, which git ignores).
  - Menu leftovers fixed (2026-10-11): RoR2's label material (TextMeshPro/Distance Field) has a pink underlay and the
    outline keyword off, which washed labels out; each label now gets its own material with the outline on and a dark
    underlay. The "RISK OF BONK" title shows (yellow, black outline). The Rain style button keeps its blue color
    (the menu styler no longer re-tints it).
- Second PC (2026-10-13; RoR2 1.5.0#1210, 1366x768): set up from scratch. .NET 8 SDK and Python 3.12 via winget;
  BepInExPack 5.4.2122 + RoR2BepInExPack 1.46.0 from Thunderstore laid out like an r2modman "Default" profile
  (winhttp.dll + doorstop_config.ini with enabled=false in the game folder, so plain Steam launches stay vanilla).
  RoR2BepInExPack must sit in plugins/RiskofThunder-RoR2BepInExPack: the preloader deletes a plain
  plugins/RoR2BepInExPack folder, and without its Newtonsoft.Json the game's own types fail to load our plugin.
  Same results as the first PC: 20/20 survivors, Megabonk-style menu and character select.
  - Test tools now work at any window size: click.ps1 takes 1920x1080 reference coordinates and scales them to the
    game's client area, focuses the game (Alt tap + SetForegroundWindow) and sends real mouse moves (RoR2 reads raw
    input, SetCursorPos alone is ignored). sheet.ps1 scales its crops.
  - testgod: the file BepInEx/RiskOfBonk/testgod makes player bodies unkillable and stops monster spawns (host), so
    run tests are not buried at the drop pod.
  - Verified (mbAssetsVerified/verified true): logo; Megachad, Monke, Ogre, CL4NK, Bush, Vlad (runs on the first PC),
    Fox and Calcium (runs here). All 20 look right in this PC's character select contact sheet. Still to run-test
    (12 cells): Sir Oofie, Ninja, Tony McZoom, Robinette, Bandit, Birdo, Noelle, Spaceman, Athena, Dicehead, Amog,
    Sir Chadwell. This PC has 7.9 GB RAM and the game takes ~3 GB; a long batch got stopped for low memory, so run
    a few characters per game launch.
- Update 2 (2026-10-13), toward the full vision (user asked to continue with it):
  - Megabonk's metadata (IL2CPP strings, read only) shows a Dani/Karlson-style controller: slide (slideForce,
    slideCounterMovement, slideThresholdSpeed), maxSlopeAngle, inertiaFriction, jump buffer, extra jumps, hold-to-wallrun;
    actions move/jump/interact/slide/aim, no sprint. BonkMovement: auto-sprint, Sprint key (rawSprintInput) = Slide,
    momentum model in a CharacterMotor.PreMove postfix (horizontal only; RoR2 keeps gravity), bhop in
    ApplyJumpVelocity. Measured in game: sprint 10.2 m/s, slide start 12.8, bhop chain ~13. Wallrun not done.
  - Upgrades: tomes as hidden ItemDefs (inventory sync = network sync), picks counter item, pending = team level - 1 -
    picks; client sends message 0x5B0C, host validates and gives items. Checked: HP Tome math (352 -> 388 at level 5),
    shield, agility (walk 11.4 = 7 x 1.45 x 1.12), Megabonk tome icons by exact Texture2D name.
  - Hordes: monster count went 5 -> 31 in ~2 min standing still (vanilla ~10-15).
  - Bosses: Megabonk keeps most enemies outside scene renderers (Golem textures exist, mesh not found); usable skinned
    rigs: Ghost (humanoid, 41 bones, t_ghost_* variants), Ent, ShadyGuy. Teleporter: Ancient Ent (Ent on TitanBody,
    retargeted 13 bones), Ghost Lord (Ghost on VagrantBody, nearest-bone fit). Final: Ghost King on Brother bodies,
    Shadow King on FalseSon bodies (Retarget now also reads L_/R_ prefixes, Hips, SpineEnd). Checked in game: boss
    event spawns, boss bar names, vanilla boss suppressed, swarm; Mithrix as Ghost King.
    Note: RigDebug's in-game bake uses BakeMesh (applies model scale) while bones are dumped unscaled; compare world
    bounds, not the bake against the bones.
  - Test switches in BepInEx/RiskOfBonk/: testgod (unkillable; enables F8 level, F9 teleporter, F10 spawn testspawn
    master), testnospawn, testmove (speed log), testboss (force a boss id), debug.
  - Not done yet: Bonk style terrain, Megabonk weapons, Megabonk shrines/chests, stage visuals, final-fight swarm,
    Voidling, wallrun, controller support for upgrade picks.
- Update 3 (2026-10-13), user request: Megabonk weapons only for Megabonk survivors; each starts with one weapon on
  primary (its own, characters.json startWeapon, a best guess: verify against Megabonk), picked weapons fill
  secondary/utility/special in pick order (4 per run), equipment untouched; Megabonk XP with an animated XP bar; the
  level-up menu gives 10 s invincibility with a countdown (red + shaking in the last 30%) instead of pausing.
  - Build: compiling against the installed game's assemblies now (-p:RoR2Managed=..., publicized at build time by
    BepInEx.AssemblyPublicizer.MSBuild); redeploy.sh does it, build.sh with RISKOFRAIN2=<game folder>. The nuget
    GameLibs 1.3.1 path is pre-SotS (DamageType vs DamageTypeCombo etc.) and no longer compiles the weapon code.
  - Weapons: 25 rows (Megabonk icons exist for all); one EntityState (WeaponFire) for every weapon, kind bullet /
    projectile / melee / aura / lightning built from RoR2 attacks (tracers, MageFirebolt, Sawmerang, EngiMine,
    MissileProjectile, Merc slashes, Jellyfish/Vagrant novas, Ukulele lightning). Megabonk bodies get their own
    SkillFamilies (start weapon + empty "?" slots); the WeaponLoadout component sets SkillOverrides from the
    inventory's itemAcquisitionOrder, so every machine agrees. LightningOrb needs bouncedObjects set.
  - XP: ExperienceManager.AwardExperience prefix -> host gems (raycast to ground), messages 0x5B0E, pickup radius 7 m,
    team XP on arrival; xp_multiplier 1.5. Checked: gems drop, fly in, level up; bar fills/flashes.
  - Menu: client sends 0x5B0D on open, host adds HiddenInvincibility (once per pick), cleared on pick. Checked:
    countdown red at 2.9/2.7 s, buff icon shows, "Not invincible" after.
  - Checked in game as Megachad: M1 Aura, M2/Shift/R "?" until weapons are picked; Shotgun pick went to right mouse;
    Firestaff/Lightning Staff/Sword on M2/Shift/R fire without errors; weapon-upgrade cards once 4 weapons are held.
  - Test tools: F11 gives the next weapon from BepInEx/RiskOfBonk/testweapons; shot.ps1 caches its helper DLL in
    %TEMP% (~10 s per shot instead of ~50 s during a run).
- Update 4 (2026-10-14): run-tested the remaining Megabonk survivors' start weapons (Sir Oofie, Ninja, Tony McZoom,
  Robinette, Bandit, Birdo, Noelle, Spaceman, Athena, Dicehead, Amog, Sir Chadwell) and fixed what they showed:
  - Weapons fired every frame while held: the state's minimum interrupt priority equalled the skill's, so the held
    skill restarted itself. Now PrioritySkill until the attack's interval is over, then Any (checked: 4 attacks in 2 s
    for a 0.5 s weapon, was ~90/s).
  - 20 of the weapon addresses were wrong for game 1.5.0 (prefabs moved, e.g. RoR2/Base/Merc/MercSwordSlash.prefab, not
    .../Characters/Merc/...), so most projectiles never spawned and swings/novas had no effect. Sheet fixed; WeaponFire
    now falls back to the game's address with the same file name and logs it. With testgod the mod loads every weapon
    asset at the main menu and logs any it can't find. Note: catalog.json's plain-text strings are bundle paths, not
    addresses; don't grep it to check an address.
  - Dice: Mithrix's lunar shards replaced by Engineer's bouncing grenades.
  - Melee weapons play Mercenary's GroundLight1/2 swing where the body's animator has it (checked: Sir Oofie swings),
    with the slash on the blade's muzzle; otherwise the slash spawns in front of the body via EffectManager.
  - Survivor descriptions: no RoR2 kit names anymore; they name the start weapon and explain the slot order.
  - Test tools: attacktest.sh (pick, leave the pod, hold attack twice, screenshots; SLOW=1 uses F7 slow motion),
    F7 toggles slow motion (testgod), every weapon attack is logged with testgod. pick.sh no longer needs Python.
- Update 5 (2026-10-14): Megabonk shrines next to RoR2's. Shrines.cs: host places tuning.charge_shrines_per_stage (4)
  Charge Shrines and magnet_shrines_per_stage (1) Magnet Shrines on stages with a Teleporter (SceneDirector
  .onPostPopulateSceneServer; ground nodes for HullMask.Golem without NoShrineSpawn, 30 m apart and from the
  Teleporter, stage RNG). No network prefabs: message 0x5B0F upserts (kind, position, progress) and removes; the host
  re-sends every shrine every 2 s, so clients whose scene loaded late still get them. Charging: a living player's feet
  inside the circle (radius 5 m, |dy| < 4) adds dt/5 s, empty drains at a quarter of that. Charged: every player's
  master gets the hidden "bonus picks" item (pending picks = level - 1 + bonus - picks); the panel title says "Shrine
  charged!" once level picks are used. Magnet: XpGems.MagnetTo sends every lying gem to that player. Visuals: flat
  emissive rings (ring, dim base, fill arc that grows with progress) and a spinning crystal 4.5 m up; used shrines
  pop and shrink away. Checked in game: 5 shrines placed, charge fills, "Shrine charged!" menu with invincibility
  countdown, chat line, x2 when two are charged; Magnet Shrine used (log), gem pull not observed with gems lying
  (they were all collected already). Test key F6 = into the next shrine (testgod).
- Update 6 (2026-10-14): Megabonk swarm in the final boss fights. FinalSwarm.cs (host, with MegabonkBossEvent): every
  second checks for a living non-player body named in a final boss row's replaces (Brother*, FalseSon*) or Voidling's
  MiniVoidRaidCrabBodyPhase1-3; while one lives, a runtime CombatDirector (created inactive so its fields are set
  before Awake; creditMultiplier 0, fallBackToStageMonsterCards) gets tuning.final_swarm_credit (12/s) x
  sqrt(difficulty) and spends it on the stage's monsters (the horde patch applies to it). Removed with credit zeroed
  when the boss is gone. Checked: F10 BrotherMaster on stage 1 -> "swarm on", beetles stream in. Not checked yet: on
  the moon itself, swarm off after the kill.
- Update 7 (2026-10-14): Bonk style terrain. BonkTerrain.cs, SceneDirector.Start prefix on every machine (before the
  host places the Teleporter and interactables), only with StageMode.IsBonk on SceneType.Stage scenes with stageOrder
  1-5. Seed from the run's synced unique id (Run.GetUniqueId) + stageClearCount + scene name, so every player builds
  the same map with no message. Map: 380 m square, 3 m cells, 4-octave Perlin, terraced (lerp to 7 m steps), flat
  clearing at the spawn, walls in the outer 20%; flat-shaded triangles coloured from an 8-colour palette texture by
  height/steepness, palette by scene name (snow/frozen, goolake/desert, wispgraveyard/shipgraveyard/rootjungle,
  dampcave/skymeadow/sulfur/void, else grass). 140 low-poly trees (pine and round, capsule colliders) in Perlin groves
  and 70 rocks. The stage's renderers and colliders are switched off except particles and anything networked or
  interactable (sky, light, fog stay); MapZones off, with a FallGuard that puts bodies back in the clearing; fixed
  interactables set down on the new ground; spawn points moved to the clearing. Host: ground (10 m grid) and air
  (20 m grid at +12/+26 m) NodeGraphs built from the heightfield: links under 45 degrees with no bump in between,
  TeleporterOK like MapNode.TestTeleporterOK (ground within -3..+7 m at 15 m all round), line of sight per pair up to
  120 m. MapNodeGroup.Bake works at runtime but its physics tests took 23 s for this map; the heightfield version takes
  0.5 s. Checked in game: terrain, trees, rocks, walls, monsters and chests on it, Teleporter placed on it and the
  Megabonk boss event there. Not checked yet: a client seeing the same map, later stages/palettes.
- Update 8 (2026-10-14): Megabonk look on stages (StageLook.cs, setting MegabonkLook, tuning system "look"). On
  every Stage scene each PostProcessVolume's own ColorGrading copy (volume.profile) gets saturation +35, contrast +15,
  post exposure +0.35 (LDR brightness +12) added to the stage's values. Lessons: a separate global volume replaces the
  stage's values instead of adding (stages came out darker); a volume destroyed with a scene stays in RoR2's
  PostProcessLayer volume list (NullReferenceException in PPVolumeInfo every frame), so the fallback volume (stages
  without grading) is one DontDestroyOnLoad object switched on and off; lighter RampFog made stages darker (their fog
  carries much of the light), so look_fog is 1 (unchanged). Stages checked grade in HDR (lakes, foggyswamp,
  sulfurpools, dampcavesimple). Result is subtle (A/B with the F5 test key); values are for the user to tune.
  Test key F5 toggles the look; F12 goes to the next stage. Bonk style checked over stages 1-4 (new map each stage,
  5 shrines each, palettes).
- Update 9 (2026-10-14): gamepad picks in the level-up menu: X / Y / B (JoystickButton2/3/1, Unity's legacy input)
  pick the left / middle / right card; cards show "1 / X" etc. Keyboard checked; gamepad not checked (no controller
  on this PC).
- Update 10 (2026-10-14): wallrun (BonkMovement air branch). In the air with Jump held, a raycast left/right of the
  horizontal velocity (capsule radius + 0.7 m, world layer, |normal.y| < 0.35) finds a wall; with at least
  wallrun_min_speed x walk along it, velocity is projected along the wall (speed kept, slight push into it) and the
  fall is capped at wallrun_fall, for up to wallrun_time per landing. Releasing Jump mid-run kicks off the wall and up
  (wallrun_kick). Checked with test key F4 (a 60 m test wall beside you) + testmove log: two runs of 1.42 s at
  10.2 m/s, ended by the time limit.
- Update 11 (2026-10-14): more Megabonk shrines (names from Megabonk's files: ChargeShrine, MagnetShrine, ShrineMaoi,
  GreedShrine, ChallengeShrine, CursedShrine). New kinds in Shrines.cs, 1 each per stage, stand 1.5 s
  (shrine_use_seconds): Moai = every player a random tome at a rolled rarity (luck counts); Greed = every monster
  CombatDirector on the stage gets goldRewardCoefficient x1.5 and creditMultiplier x1.5; Challenge = a runtime
  CombatDirector (one wave, eliteBias 0.5) with challenge_credit (400) x difficulty, preferring a champion card from
  ClassicStageInfo.monsterSelection, else SelectMonsterCardForCombatShrine; spawned masters tracked through
  onSpawnedServer (no CombatSquad: it is a NetworkBehaviour); all dead -> everyone a bonus pick (nothing spawned in
  10 s -> reward anyway). Checked in game: chat lines for all three, Moai gave "Common Armor Tome (+1)", Greed used,
  Challenge sent 5 Golems and paid out once they died (test key F3 kills every monster). F6 now visits the rarest
  shrine kind first.
  Cursed Shrine (our reading of Megabonk's curse, unverified against Megabonk): everyone cursed_shrine_picks (2) bonus
  picks now; the stage's monster directors creditMultiplier x1.3 and eliteBias x0.5 (cheaper elites). Checked: chat
  line, "Shrine charged! x2" menu.
- Update 12 (2026-10-14): Melty release 0.2.0. Sheets: every cell verified by best judgment (user's call), with start
  weapons checked against Megabonk guides: Tony McZoom = Wireless Dagger, Spaceman = Black Hole, Athena = Aegis,
  Amog = Poison Flask (new weapons: wireless_dagger = Ceremonial Dagger projectile, black_hole = Primordial Cube
  GravSphere, aegis = melee bash with slow; icons BluetoothDagger/BlackHole/Aegis from Megabonk's textures).
  MeltyLink: Steam lobby address in melty.log, join= from melty.properties; megabonk= there is the Megabonk folder.
  MeltyTest (Melty's code, unchanged) + MeltyTestRun drive a run with no one playing. Only Newtonsoft.Json.dll is bundled (in RiskOfBonk/): the whole RoR2BepInExPack
  logged Debug hook lines with "Exception" in their names, which Melty's test read as errors, and other mashups that
  ship it would conflict with ours.
  MIT license.
- Update 13 (2026-10-15): Megabonk movement measured from Megabonk itself. Il2CppDumper on GameAssembly.dll gave
  PlayerMovement / PlayerMovementValues; their constructors and methods (disassembled with capstone) hold the numbers:
  moveSpeed force 2700 x mass at 50 Hz (54 m/s per s), maxRunSpeed 10, counterMovement 0.25, slowDownSpeed 0.01 / 0.1
  above max speed, slideForce 200 (4 m/s), slideCounterMovement 0.1 (5.4 m/s per s), slideThresholdSpeed 8, slide
  cooldown 25 ticks, justLanded 15 ticks, air input 0.45, JumpHeight stat 7 (impulse), Physics gravity -19.62
  (PhysicsManager in globalgamemanagers), minWallClimbSpeed 10. All in sheets/tuning.json (system movement);
  BonkMovement rebuilt from them (gravityScale per step for Megabonk's gravity). Checked with tools/ingame/movetest.sh
  on a Bonk style map (testmove log every 0.1 s): 0 to 10 m/s in about 0.1 s, stop in about 0.25 s after the 0.3 s
  landing grace, jump apex 1.26 m, slide boost 4 m/s and downhill gain, bunny hops keep 10 m/s.
- Update 14 (2026-10-15): Megabonk's own animations. Megabonk has no Avatar assets: its character clips are generic
  (Transform curves keyed by CRC-32 path hashes), not humanoid. MegabonkAnimations.cs reads each character's
  AnimatorController (the Animator on its prefab in sharedassets1) and decodes its clips (streamed cubic segments,
  dense samples, constants) into 30 fps tracks; MegabonkAnimator plays them on the rebuilt Megabonk skeleton (the
  survivor's poses no longer drive it) with roles from sheets/animations.json: idle, run (speed-scaled), jump (once),
  fall, slide. All 20 characters have 5 clips (mbClips in characters.json). No attack clips exist in Megabonk;
  slides use the rail Grind pose (Bush has a real Sliding clip). Spawned bodies set themselves up in Awake (only
  serialized fields survive Instantiate). Checked in game: Sir Oofie switches Idle/Run/Jump/Falling/Grind as he
  moves (anim log with testmove) and shows Megabonk's run and idle poses.
- Update 15 (2026-10-15): Megabonk weapon sounds and projectile models. Sounds: Megabonk's AudioClips are FSB5
  Vorbis in .resS; FsbVorbis rebuilds them as Ogg Vorbis (python-fsb5's method) with the Vorbis setup headers read from
  the FMOD table inside Megabonk's own UnityPlayer.dll (64-bit entries {ptr, len, crc, basePtr, ofs, len2}), NVorbis
  decodes them, and since Risk of Rain 2 disables Unity audio ("Audio system is disabled"; it uses Wwise) they play
  through winmm waveOut (a pool of 12 voices), at volume_master x volume_sfx x sound_volume, fading with distance.
  25/25 load (mbSound per weapon in weapons.json; jump, land, slide in sounds.json). Projectiles: weapons with a
  Megabonk projectile model (bone, banana, axe, dice, rocket, flask, mine, dagger) dress the RoR2 projectile's ghost
  on every machine (the body's last fired weapon picks the look), spinning (projSpin). Checked: Calcium throws
  Megabonk bones, Monke bananas, with BoneThrow / AxeThrow logged. Megabonk's slashes, fire, lightning, aura and
  tornado are particle effects with no mesh, so those weapons keep Risk of Rain 2's effects.

- Update 16 (2026-10-23): 0.2.5, fixes for players' reports on 0.2.4. (1) 7 players crashed in ntdll.dll (c0000005)
  while the game was closing (their logs end with Wwise's shutdown and the run report save): MegabonkSounds' waveOut
  devices stayed open into the process exit. Devices now close after 3 s of silence (buffers freed once closed), and
  all of them in OnApplicationQuit, before Unity's teardown. (2) "Game never appears after Play": the logs were the
  previous session's, consistent with that session's exit hanging and its process blocking the next start; besides
  (1), Plugin starts a 30 s exit watchdog on Application.quitting. (3) "Loader runs but never loads its code": Plugin
  Awake now starts each part on its own (a failing part is logged and switched off, the rest loads), and
  MegabonkLoader always sets Done, so the content pack can't wait forever on a failed Megabonk search.
- Third PC setup (2026-10-23, the first PC again): one install only, the game folder's own BepInEx 5.4.23.5 (set up by
  Melty: winhttp.dll + doorstop enabled, plugins/RiskOfBonk only), so a plain Steam launch plays the mod. The old 0.1
  copy in the r2modman "Default" profile (with the user's other mods) was removed. Test tools find the install with
  tools/ingame/bepinex.sh (BONK_BEPINEX, else the game folder's BepInEx, else r2modman's Default profile);
  redeploy.sh copies the whole plugin folder and clears the old log first. This PC's character grid has 11 cells
  before Megachad: GRID_START=11 for pick.sh / runtest.sh / movetest.sh.
- Update 17 (2026-10-23): 0.2.6, Megabonk's own weapon numbers and automatic attacks. Il2CppDumper 6.7.46 works on
  Megabonk's GameAssembly (metadata v29); a scratch AssetsTools reader dumped the 30 WeaponData assets in
  sharedassets1 (raw layout: UnlockableBase fields, two LocalizedStrings, then WeaponData's fields in declaration
  order, bools padded to 4). weapons.json now carries mbDamage, mbProjectiles, mbBurstTime, mbMinBurstInterval,
  mbEndCooldown, mbRange, mbCritChance, mbProc, mbOnlyClose. WeaponUtility.GetWeaponCooldown (disassembled):
  cycle = endCooldown / attackSpeed + shots x max(burstTime / shots, minBurstInterval) / attackSpeed; shots follow
  each other at that interval. EnemyTargeting.GetEnemy: shot n goes to the n-th closest enemy in spawnProjectileRange.
  WeaponFire now times the whole cycle (burst + wait) and fires its shots at targets (none in range: where you aim;
  the Revolver, onlySpawnWhenCloseEnemies, holds fire); damage = mbDamage x tuning weapon_damage_scale (0.4);
  shotgun pellets share one cone's damage. Weapons fire by themselves (Weapons.AutoFire, authority, not in a vehicle),
  each slot on its own networked EntityStateMachine MBWeapon0..3, so all four run at once; weapon skills are not
  combat skills. The aura's nova effect shows at most once a second. Checked as CL4NK with Revolver, Sword,
  Firestaff and Aura: 2.5 s revolver cycles with 6 shots 0.2 s apart, all four firing together, kills and XP.
  Test tools: weapontest.sh (F11 weapons, no attack keys), key.ps1 takes F1..F12/escape/enter. The character grid
  differs per Steam profile (11 or 18 cells before Megachad; GRID_START).
- Update 18 (2026-10-23): Megabonk characters' own movement, from their CharacterData (tools/mbdata/characters.py):
  start weapons corrected (Bandit: Dexecutioner, Birdo: Tornado); mbMoveSpeed (added to the move speed multiplier,
  e.g. Tony McZoom +30%), mbJumpHeight (added to the 7 m/s jump: PlayerMovement.GetJumpForce = JumpHeight stat x mass),
  mbExtraJumps (Birdo +1; every Megabonk survivor gets baseJumpCount 1 + that). Megabonk has no wallrun for everyone:
  wall climbing is the WallClimb passive (Monke only). CanWallClimb: passive, not tagged Ignore, input towards the
  wall within wallrunAttachAngle 60, holding Wallrun (Jump unless a Wallrun key is bound); WallClimbing sets the
  upward speed to max(minWallClimbSpeed 10, maxSpeed x 1.5) and pushes 4 m/s into the wall; wallClimbCooldown 0.4 s.
  The old hold-Jump wallrun (and its four guessed tuning rows) is gone. extraGravity (11) is a plain AddForce on a
  60 kg rigidbody (0.18 m/s^2), so it is left out. Checked: Monke climbs the F4 test wall at 15 m/s; Monke's jump
  launches at 9 m/s. Offline Megabonk readers are in tools/mbdata (README).
- Update 19 (2026-10-23): crouch, from Megabonk's CheckInput/StartCrouch: the Slide key slides only from
  slideThresholdSpeed (8 m/s) up; slower it crouches, capping speed at GetMaxSpeed x 0.5 (tuning crouch_speed), and a
  slide that slows to that becomes a crouch (both show the slide pose). Checked with movetest (now holds W straight
  into the slide; hold.ps1 -AddVk/-AddAfterMs): Megachad 9.5 -> 13.45 m/s on the slide, fading to a 4.75 m/s crouch.
- Update 20 (2026-10-23): Megabonk's model tilt, from PlayerRenderer.Update: a ray down (51 m) finds the ground's
  normal; smoothingMultiplier = 1 - clamp01((distance - 1) / 3) x 0.95 lets it count fully near the ground and barely
  in the air; the model's up turns towards it at 14/s; wall climbing leans into the wall (wall normal + up).
  MegabonkAnimator tilts the Megabonk skeleton root about the feet that way after posing. PlayerRenderer's animator
  only has moving / grounded / jumping / grinding (no slide or attack state), which the clip roles already cover.
  Checked: Monke runs and stands on stage 1 slopes, no errors.
- Update 21 (2026-10-23): weapon levels use Megabonk's UpgradeData (tools/mbdata/upgrades.py): each weapon's list of
  stat upgrades (e.g. Revolver: damage +2.5, projectiles +1, crit +10%, bounces, crit damage, projectile speed). The
  ones this mod can apply (damage as flat Megabonk damage, projectiles as more shots per burst, size as melee/aura
  reach, crit chance) are mbUpgrades in weapons.json; each level above 1 applies the next in order, cycling (Megabonk
  rolls a random few per level; a fixed order needs no network message). Replaces +25% damage per level; level-up
  cards and the chat line say what the levels add. F11 levels the first testweapons entry once all are owned.
  Checked: CL4NK's revolver 28 -> 42 damage (lv 2), 7 shots (lv 3), 56 damage (lv 5).
- Update 22 (2026-10-23): air deceleration, from MovementTick: in the air, each look-relative axis without input gets
  a push against its velocity of airDeceleration (0.003) x moveSpeed x 0.02, about 16%/s (tuning air_drag 0.162); the
  port fades the part of the speed you are not steering along. Checked with movetest as Tony McZoom: run 13 m/s
  (mbMoveSpeed +30%), bunny hops hold 13 with W held, 12.9 -> 12.3 m/s in 0.4 s of air with no input.
- Update 23 (2026-10-24): Megabonk passives and character stats (src/Passives.cs). Numbers come from each
  PassiveAbility class (constructor constants, OnLevelup's stat and per-level value) and the PassiveAbilities_en string
  table; "per level" is Risk of Rain 2's level. characters.json gains mbStats (the rest of CharacterData's
  statModifiers: MaxHealth/Shield on Megabonk's base 100, Armor as a damage fraction -> RoR2 armor 100f/(1-f), Evasion,
  CritChance, DamageMultiplier, Lifesteal, Luck, Gold/Xp multipliers, SizeMultiplier, FallDamageReduction, Thorns,
  Difficulty) and mbPassiveName/mbPassiveText (shown on the character select screen). Passives: Flex (blocks a hit,
  10 m knockback blast, +2.5% damage per flex, cooldown 12 s - 0.2/level, min 4), RNG Blessing (+1.5% luck/level),
  Speed Demon (+0.85% speed per second up to +150%, a hit removes 1.5/(4 + level/10); damage from speed), Reinforced
  (+1% armor/level), Monke Grip (+2 HP/level), Shadowstep (+0.5% evasion/level, evading executes a non-boss attacker),
  Zoombonk (0.6% charge per metre moved; a full charge zaps enemies within 6 m of the next hit for 3x damage),
  Stonks (+1% gold/level, damage from gold in Megabonk's tiers), Flowstate (+1% attack speed/level), Float (hold Jump
  while falling: sink at 2 m/s; +1% airborne damage/level), Enduring (+0.75% size/level, +1.5% damage per slowed
  enemy), Quantum (+1% XP/level on gems you collect), Vampire (+1% lifesteal/level), Lock In (damage up to +100% at
  low HP, +2 thorns/level), Warrior (+1.5% damage/level), Crit Happens (+1% crit/level), Gamba (a random tome point
  per level-up), Plague (poisoned enemies burst, poisoning enemies within 4 m + 0.05/level; +1% poison damage/level;
  the Poison Flask now poisons), Bullseye (marks a random enemy every 5 s; hitting it makes it explode; +1% crit
  damage/level), Curse (+1% difficulty/level: monster directors earn that much more credit). Checked with
  tools/ingame/passivetest.sh: Megachad flexes on cooldown, Bush marks/explosions, Dicehead 5 rolls in 5 levels,
  Calcium speed +0.6 falling on hits, Tony zaps after running, Amog plague bursts, Ninja executed a Blind Pest.
- Update 24 (2026-10-24): Megabonk's own weapon effects (src/MegabonkFx.cs, MegabonkFxData.cs). The Megabonk reader
  copies effect prefabs (particle systems with all their modules, mesh/trail/line renderers, materials, textures,
  meshes); MegabonkFx rebuilds them as Unity particle systems at runtime. Each weapon's prefabs come from its
  WeaponData.attack -> WeaponAttack (prefabProjectile / prefabMuzzle / prefabHit) or its constant attack (CombatAura,
  IceAura, DragonsBreath, LaserBeamAttack); weapons.json mbFxMuzzle/mbFxShot/mbFxMode/mbFxHit/mbFxScale/mbFxSpeed say
  which and how they are placed (fly, burst, beam, slash, strike, ghost, aura, stream). Retro Arsenal shaders map to
  RoR2's Cloud Remap with the remap off (vertex colour on, vertex alpha off, as RoR2's own particle materials do);
  _ExtraGlow >= 2 adds light (SrcAlpha/One), the rest blends. Risk of Rain 2 tracers, hit sparks, swings and novas are
  left out where Megabonk's effect exists. Checked: 36 prefabs read, Megachad's aura ring, revolver/sword/shotgun
  effects in game. First pass: per-weapon scales (mbFxScale) still want tuning by eye.
- Update 25 (2026-10-24): Megabonk weapons and tomes from chests (src/Chests.cs): an item chest opened by a Megabonk
  survivor holds, with tuning chest_upgrade_chance (0.5), a bonus pick instead of its item (the level-up menu, as
  for Charge Shrines; banner now "Bonus upgrade!"). Equipment barrels, lunar pods and void cradles are unchanged.
  Test key F2 opens the nearest chest (paid). Checked with tools/ingame/chesttest.sh: 5 of 6 chests gave picks.
- Update 26 (2026-10-24): Bonk style terrain rebuilt on Megabonk's own map generation (BonkTerrain.cs, MegabonkProps.cs,
  tools/mbdata/stages.py). Read from Megabonk: MapParameters (Forest1 15 tiles x 40 m = 600 m, Forest2 15 x 44,
  Forest3 16 x 48, Graveyard 19 x 40; height step = half a tile; volatility 0.8 / 1), StageData (randomMapObjects:
  prefab, amount, checkRadius, scale range; flatTilePrefabs dressing a quarter of the tiles; tile materials; fog,
  ambience and light colours). Generation like ProceduralTileGeneration + MazeHeightGenerator.GenerateHeightMe: a random
  depth-first spanning tree over the grid from the middle tile; a tile continuing straight from its parent becomes a
  ramp with chance volatility (keeping its up/down direction), turns stay level; different heights meet as cliffs; a
  ring of higher tiles walls the map. Tops use Megabonk's 128 px tile textures (one per tile, point filtered; Forest 2/3
  use its blue night grass), cliffs FillTop/FillMiddle per step. Megabonk's own trees, logs, bushes, rocks, boulders,
  ruins and tile dressings are placed by its rules (e.g. Forest 1: 300 Tree0, 40 Log0, 100 Bush0, 150 rocks, 100 Bush1,
  8 Boulders). Stages go Forest 1, 2, 3, then Graveyard. Props collide like Megabonk's (its capsule/sphere colliders),
  pebbles and bushes are walk-through, big rocks get a sunk sphere: in a 40 s run into scenery the longest stop was 1 s.
  Lighting on Bonk maps: Megabonk's exponential fog (density 0.007, the stage's fog colour) instead of the stage's ramp
  fog, its sun and ambience colours (a shadowless fill light stands in for the flat ambience under deferred shading), a
  gradient sky in the fog colour, the stage's colour grading and vignette off. Node graphs use a spatial grid (0.5 s).
- Update 27 (2026-10-24): Megabonk's soundtrack (src/MegabonkMusic.cs, config Gameplay/MegabonkMusic, tuning
  music_volume): Menu on the main menu and lobby; on Bonk style stages Forest1/2/3_Loop for Forest 1-3 (Megabonk's
  forest MusicTrack pool, then random), its Other tracks on the Graveyard, and ForestBoss_Loop / GraveyardBoss_Loop
  while the Teleporter boss squad lives. Streamed from the .ogg (MegabonkSounds.OggFile converts FSB5 once) on its own
  thread through waveOut, looped, 0.8 s fades, master x music volume; Risk of Rain 2's music is muted through the
  Volume_MSX RTPC meanwhile and restored after. Checked: Menu -> Forest1_Loop -> ForestBoss_Loop (F9) -> Menu.
- Update 28 (2026-10-24): Megabonk's enemies on Bonk style stages (src/MegabonkEnemies.cs, MegabonkEnemyData.cs,
  tools/mbdata/enemies.py). Megabonk enemies are baked vertex animations (EnemyData.animation ->
  AnimatedMeshScriptableObject: AnimationFPS and one Mesh per frame) with a material, renderer offset/rotation/scale,
  hp, speed, isFlying. The reader takes EnemyData by name (script class checked: animations share the names) and
  every frame mesh. Each regular monster (not champions or bosses) wears one from the map's pool by weight (base
  health < 150 light, < 700 medium, else heavy; flyers fly): Forest GoblinWeak/Slime, GoblinStrong/Golem/BoomerSpider,
  GoblinTank, Bee; Graveyard Skeleton/Zombie/Ghoul, SkeletonArmored, Ghost/GhostPurple/Head. The pick hashes the
  monster's name and the stage, so every machine agrees. The Risk of Rain 2 renderers are forced off; the Megabonk mesh
  follows the feet and facing, plays its walk frames at its FPS x (speed / Megabonk speed), keeps Megabonk's size
  stretched at most 2x towards the monster's hurtbox, tints elites, and shrinks away on death. Checked: Lemurians
  appear as Megabonk slimes.
- Update 29 (2026-10-24): balance between Megabonk and Risk of Rain 2 survivors (src/Balance.cs, tuning balance_*).
  Both games' scaling are compared as power = damage per second in base-damage units x enemies each hit reaches.
  Levels, items and tomes grow both kinds of survivor through the same stats, so only the Megabonk weapons are
  compared: a Risk of Rain 2 kit is worth balance_reference_power (5, Commando's Double Tap plus its other skills);
  a Megabonk survivor's raw power is the sum of its weapons' Megabonk damage x shots / attack cycle x crit x area
  (melee 2, aura 3, projectile 1.3, lightning 2, piercing bullets 2), from WeaponFire's own formulas. Megabonk survivors
  take weapon picks where Risk of Rain 2 survivors take tomes, so each weapon level may add balance_pick_value (5%) of
  the reference. Weapon damage is pulled to that allowance (strength 0.6, weak loadouts raised at most 15%): Megachad's
  aura x0.5 (aura counted as reaching 2 enemies; at 0.24 it needed 7 ticks for a Lemurian where Megabonk's aura kills
  a goblin in 1-2), Ninja's katana x0.67, a lone revolver unchanged (2 shots per Lemurian, as per goblin in Megabonk). Megabonk survivor bodies now use Commando's base stats
  (110 health +33/level, 12 damage, no armor) instead of their moveset body's (Loader/Acrid 160 health, 20 armor),
  with the character's Megabonk health modifier on top: Megachad at level 5 291 health instead of 403.
- Update 30 (2026-10-24): Megabonk's chests, pots and shrines on Bonk style stages (src/MegabonkInteractables.cs):
  chests wear Megabonk's Chest (OpenChest once bought), money barrels its PotSmall/PotBig, equipment barrels its Barrel,
  printers its Microwave, shrines its statues (Chance/Order: Balance, Blood: Greed, Combat: Challenge, Mountain: Cursed,
  Woods/Cleansing: Moai). The prefabs' encounter ring, floating "!" and chest shadow decal are dropped and the model
  stood on the ground. The Risk of Rain 2 object stays underneath (renderers off, highlight on the Megabonk model). The
  Megabonk shrines (Shrines.cs) show their statues on every stage. Test key F1 visits the next interactable.
- Update 31 (2026-10-24): Megabonk's HUD (src/MegabonkHud.cs, config Gameplay/MegabonkHud): top left under the money,
  a red health bar (shield and barrier over it), the four weapons with their levels (Risk of Rain 2 survivors: their four
  skills with a cooldown sweep, seconds left and charges), and every owned tome with its level; slots pop gold on a
  level up. Risk of Rain 2's own skill icons are hidden while it shows. Checked with tools/ingame/hudtest.sh as Megachad
  and Commando.
- Update 32 (2026-10-24): smoother HUD. With the Megabonk HUD up, Risk of Rain 2's health bar, level/experience
  display and sprint icon are hidden too (the Megabonk health bar and XP bar show them; buffs, money, items and
  equipment stay). With Megabonk movement, Risk of Rain 2's "Sprint to get around the map!" hint is dismissed (it
  waited for a manual sprint that never comes). Megabonk's BossSpawner was tried on the Teleporter and dropped: the
  prefab is its whole 43 m portal set piece and does not read as a Teleporter.
- Update 33 (2026-10-24): balance test tools: testmortal (with testgod: test keys on, damage real) and "fight:" log
  lines every 15 s in test runs (health, damage dealt/taken per second, kills, deaths, monsters alive, difficulty);
  tools/ingame/survivetest.sh runs a survivor in circles taking the first level-up card. A circling bot is a poor
  player (Megachad died at 54 s, the aura never stays on enemies), so it only shows trends.
- Update 34 (2026-10-24): Bonk style stages start like Megabonk: no drop pod (Stage.usePod off on the host), you stand
  on the map at once. The stage's own particle effects (waterfalls, falling leaves, dust) are hidden with its geometry;
  they hung in mid-air over the Bonk map. Test key F10 takes a comma separated list in testspawn (each press spawns the
  next, side by side). Checked: Imp, Beetle, Golem, Beetle Guard wear sword goblin, slime, golem and spider looks.
- Update 35 (2026-10-24): balance comparison under the same play (test file testbot: aims at the nearest monster and
  holds primary, secondary and special) with tools/ingame/survivetest.sh, 4 minutes of Bonk style stage 1, Rainstorm:
  Commando 22.9 damage/s, 74 kills, no death; CL4NK (revolver) 19.2/s, 51 kills, no death; Megachad (aura) 8.7/s,
  31 kills, no death (the circling bot rarely stays inside the aura). Megabonk survivors are now at or a little under
  a Risk of Rain 2 survivor. Fixes: characters with a negative Megabonk health modifier (Tony, CL4NK) spawned with the
  unmodified health and kept it until hurt (now capped), and Megabonk's Shield modifier is flat on the base like
  MaxHealth (CL4NK 44 health + 55 shield = 99, Megabonk's 90; was 22 shield).
- Update 36 (2026-10-24): Megabonk's horde density (Horde.Target, tuning horde_target_*, horde_density,
  horde_fill_credit). From Megabonk's StageSummoner.GetNumTargetEnemies: it keeps 5 + 200 x (stage time /
  stageDuration) enemies alive (MapData Forest stageDuration 600 s), times 1 + 0.7 x Difficulty + 0.4 x its stage
  multiplier; credits per second (1 + 0.5 x minutes); a summon every 2 s. Here the target is 35% of Megabonk's (Risk of
  Rain 2 monsters are tougher and heavier to run), x (1 + 0.4 x stages cleared), capped by horde_limit 70, and while
  fewer monsters are alive the stage's directors earn 3 credits per missing monster per second. Checked with the
  Commando test bot: 8 -> 33 monsters alive over 4.5 minutes (was 5-10 throughout), 91 kills, health dipping to 103/154.
  Fix: Risk of Rain 2 heals a body by the rise in max health inside RecalculateStats, before this mod's health bonuses
  (Megabonk character health, HP Tomes); Balance now heals the rest of the rise after every bonus (and caps health above
  the maximum). Megachad started at 110/132 and HP Tome owners healed less on level-ups; now 132/132.
- Update 37 (2026-10-24): the Teleporter on Bonk style stages is Megabonk's boss portal (BossSpawner: a 13 m stone
  arch with a skull on a 25 m stone platform), at 60%, sunk by its platform height so players stand on top of it (it
  has no collider). Its 30 m underground skirt (BossPortalNormalBottom) is dropped; the first try stood the model on
  the skirt's bottom, 30 m in the air. The Teleporter's base, prongs, beacon and crystals are hidden; its charging
  range, beam and glow stay. tools/probe `prop <name> <dir>` writes a prop's submeshes as OBJs with their extents.
- Update 38 (2026-10-24): all of Megabonk's weapon upgrade kinds except duration (tools/mbdata/upgrades.py ->
  weapons.json mbUpgrades, in Megabonk's order): critdamage (that weapon's crit multiplier + v), bounces (src/Bounces.cs:
  when a bullet or projectile reaches its target it jumps on to the nearest enemy not hit yet within 15 m, once per
  bounce, with the weapon's own Megabonk shot effect; the owner deals the hop's damage as a bullet; lightning gets more
  chain bounces), speed (projectile speed x(1+v), at most +100%), knockback (force x(1+v)). One crit roll per shot.
  Balance counts crit damage and bounces (+0.5 enemies each). Checked: CL4NK's revolver to level 7 (+50% damage,
  +1 projectile, +10% crit, +20% crit damage, +1 bounce, +20% projectile speed), bounces of 8-13 m onto Beetles and a
  Bison. Known: with the balance pull a non-damage level can lower the per-bullet number slightly while total power
  rises (about 10% per level, like a Damage Tome point).
- Update 39 (2026-10-24): weapon effects pass with tools/ingame/fxtest.sh over every weapon (groups of three on Sir
  Oofie, slow motion). Fixes: Wireless Dagger had no icon (Megabonk's texture is "Bluetooth Knife"; BluetoothDagger is
  only its mesh); Poison Flask (0.6 -> 0.4), Mine (0.6 -> 0.35) and Dice (0.5 -> 0.35) hit effects filled the screen
  when they burst next to the player (additive clouds; mines are dropped at your feet). The rest (Space Noodle beam,
  Sniper/Bow shots, Firestaff/Dragon's Breath fire, swords' slashes, Blood Magic, Black Hole, Chunkers, Tornado) read
  well at their scales.
- Update 40 (2026-10-24): health bonuses fixed for real. Risk of Rain 2 sets health inside RecalculateStats from its
  own maximum, before this mod's postfixes add Megabonk character health and HP Tomes: it clamps health to that lower
  maximum on every recalculation (buffs, items, levels), so bonus max health was mostly unusable (Megachad sat at
  110/132, HP Tome points barely helped anyone). Balance now notes health and maximum before RecalculateStats and, after
  every postfix, sets health to what it was plus the whole rise of the final maximum (capped by it); shield likewise.
  Checked: Megachad 132/132 through a fight. Megabonk's fall damage under Megabonk movement (PlayerHealth.OnPlayerLanded):
  nothing below a landing speed of 30 + jump height (about a 35 m drop), else 10 + 80 x (speed - that) / (140 + jump
  height) % of max health, never while sliding; Risk of Rain 2's started at jump power + 20 = 27 m/s, so every 20 m cliff
  of a Bonk map hurt. No fall damage for 4 s after spawning on a Bonk stage (no pod: you appear above the ground).
  Bonk style stages: Risk of Rain 2's ranged monsters (wisps, Clay Templars, Alloy Vultures, Solus Probes, Hermit Crabs,
  Blind Pests, ...) spawn at bonk_ranged_weight (0.35) of their weight; Megabonk's hordes are melee chasers.
- Update 41 (2026-10-24): close-range weapons counted as reaching 1.5 enemies (was aura 2, melee 2): Risk of Rain 2's
  monsters often fight from range (Lemurians spit fireballs before they bite), and a stand-and-fight test bot as
  Megachad took 144 damage in 10 s while its aura dealt 9 per second (the aura itself kills four Beetles within seconds
  of them arriving). Aura x0.59, Katana x0.79 at level 1. survivetest.sh: MOVEMS/PAUSEMS for stand-and-fight runs.
- Update 42 (2026-10-24): Megabonk's Desert on Bonk style stages (BonkTerrain noise maps). Read from Megabonk:
  StageDesert1-3 (tools/mbdata/stages.py now finds noise-map stages: an empty MapParameters reference, then the prop
  rules), NoiseData (Desert 1/2: scale 100, 2 octaves, persistence 0.4, lacunarity 3; Desert 3: scale 40, 0.5),
  TerrainDesert (heightMultiplier 11, a 4-key height curve), MapGenerator (mapChunkSize 241, the mesh scaled 4 x 8 x 4 x
  StageData.proceduralMapScale 1 / 1.1 / 1.2), Noise.GenerateNoiseMap (octaves of Mathf.PerlinNoise at seeded offsets,
  normalised; its blend warp is off for the desert) and the triplanar ground (SandNew on top, SandRockNew where steep;
  Desert 3 the _2 textures). Desert 1 is 960 m across with dunes up to 50 m. Props by its rules: Landmark1-4 rock
  formations, desert houses, seven rocks, Cactus1/2, logs, DesertTree1/2, 300 DesertBush; houses and landmarks collide.
  Fog, light and ambience per stage (Desert 2 blue night, Desert 3 red). Invisible walls at the edges; node graphs every
  12 m (about 1.1 s). Stage order: Forest 1, Desert 1, Forest 2, Desert 2, Graveyard, Forest 3, Desert 3. Desert enemy
  looks: Mummy, Scorpion, Tumblebone; dusty Skeleton, big Scorpion, MummyTank; MummyAncient, armored dusty Skeleton;
  Wisp flying. Music: Desert1-3_Loop and DesertBoss_Loop. All music tracks are now converted in the background 20 s after
  start (20 tracks in under a second), so a stage's first track no longer starts late.
- Update 43 (2026-10-24): fixed the screen burning to white after many stages in one game session. StageLook read
  each post-processing volume's grading as its base and added Megabonk's boost; some volumes outlive their stage, so
  every stage load added +0.35 exposure, +35 saturation and +15 contrast again. Original values are now noted once per
  grading. Checked: nine stage loads over two runs, no whiteout (was mostly white after about a dozen loads). Music:
  the track choice is keyed on the map too (a quick stage change could keep the previous map's track).
- Update 44 (2026-10-24): playing alone, the game pauses while the level-up menu is open, like Megabonk's
  LevelupScreen (config Gameplay/MegabonkLevelUpPause, default on); the countdown line reads "Paused". With other
  players it keeps the invincibility countdown. Never touches the time scale under Risk of Rain 2's own pause menu, and
  restores it afterwards if a pick was made while that menu was open. Checked with tools/ingame/pausetest.sh: the run
  timer stays at 00:38.04 for 2 s with the menu open, then runs on after the pick.
- Update 45 (2026-10-24): Megabonk characters' PickupRange stat now widens their XP gem pickup radius (metres on top of
  tuning xp_pickup_radius 7: Fox +4, Bush +6, Calcium/Ninja/Tony/Robinette/CL4NK +3, the casters +5).
