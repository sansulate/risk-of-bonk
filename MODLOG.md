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
- Melty: not done yet (needs a fresh Publish prompt from the user).
