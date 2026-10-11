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
- Melty: not done yet (needs a fresh Publish prompt from the user).
