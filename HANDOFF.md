# Handoff: continue on the PC that has both games

The design and v1 scope are settled (see MODLOG.md); do not re-ask the user about them.

1. Build: install the .NET SDK if it's missing. Set `BEPINEX_CORE` to a BepInEx 5 core folder (BepInEx 5.4.23.x from its
   GitHub releases), then run `build.sh`, or the same steps by hand on Windows, to get `dist/RiskOfBonk-0.1.0.zip`.
2. Copy the zip's `RiskOfBonk/` folder into Risk of Rain 2's `BepInEx/plugins/` and launch the game. Read
   `BepInEx/LogOutput.log` for "Megabonk folder:", "Megabonk survivors ready: ..." and any warnings.
3. Open `BepInEx/RiskOfBonk/megabonk_inventory.txt`. Fix `mbNameHints` and `yawOffsetDeg` in `sheets/characters.json`,
   and the logo hints in `sheets/menu_style.json`, to Megabonk's real asset names. Set `mbAssetsVerified: true` per row
   once it looks right in game. Repeat until `python tools/sheets.py preflight` shows 0 unverified cells.
4. Drive the game with universal-modder (`um win`): check the menus, play a stage as a Megabonk character, and capture a
   real screenshot of the game window only.
5. Test co-op with two Risk of Rain 2 copies (host plus join) and check that the Bonk/Rain choice syncs.
6. Melty: ask the user to paste a fresh Melty "Publish" prompt (it carries their token; never save it anywhere), then
   follow it: game_info, search_mashups, inspect_package, validate_recipe, one_click_check (Megabonk as companion, its
   folder passed in the env var `RISKOFBONK_MEGABONK`), create_mod, upload, screenshot, test_release. Ask for credits,
   license and remix permission, and ask before publishing.
7. Commit and push to `claude/ecstatic-shannon-g82oak`.
