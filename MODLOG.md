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
    Now: Megabonk logo, yellow buttons, chunky text. Cosmetic leftovers: "NEW" shine washes out labels; the
    "RISK OF BONK" title is not visible; the Rain style button is yellow instead of blue.
  - Rig, in progress (still looks wrong in game): newer RoR2 loads skinned meshes lazily via
    SkinDefParams.meshReplacements[].meshAddress, so the fit now uses the body mesh's bind pose (found through the
    skin, picked by vertex count) and reuses its bindposes; bones whose bind-pose length disagrees with the real bone
    are ignored. Original renderers are forced off (forceRenderingOff) since lazy skins re-enable them.
    yawOffsetDeg is 180 for all rows (Megabonk models face the other way).
  - Debug: create BepInEx/RiskOfBonk/debug to dump each fit (model-space OBJ + bones) and the in-game baked mesh.
    The dumps show the remaining bug: some Megabonk raw models (e.g. Chad) come out lying along Z, because
    MegabonkLibrary.Walk drops the root transform's rotation. Next fix: keep a root rotation that tilts the up axis.
- Melty: not done yet (needs a fresh Publish prompt from the user).
