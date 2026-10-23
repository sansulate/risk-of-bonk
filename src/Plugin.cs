using System;
using System.Collections.Generic;
using System.IO;
using Path = System.IO.Path;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using RoR2;
using RoR2.ContentManagement;
using RoR2.UI;
using RoR2.UI.MainMenu;

namespace RiskOfBonk
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.sansulate.riskofbonk";
        public const string Name = "Risk of Bonk";
        public const string Version = "0.2.6";

        public static readonly Dictionary<string, string> Tokens = new Dictionary<string, string>();
        public static string DataDir;
        public static bool MainMenuReached;

        void Awake()
        {
            Log.Source = Logger;
            string pluginDir = Path.GetDirectoryName(Info.Location);
            DataDir = Path.Combine(Paths.BepInExRootPath, "RiskOfBonk");
            Directory.CreateDirectory(DataDir);
            Log.Info($"{Name} {Version} starting");
            UnityEngine.Application.quitting += ExitWatchdog;

            // Each part starts on its own: one that fails on a player's PC (another game build, a missing method)
            // is logged and switched off, and the rest of the mod still loads.
            int failed = 0;
            void Part(string what, Action start)
            {
                try { start(); }
                catch (Exception e) { failed++; Log.Error($"{what} could not start and is off this session: {e}"); }
            }

            ConfigEntry<string> folder = Config.Bind("Megabonk", "MegabonkFolder", "",
                "Your Megabonk install folder. Leave empty: Melty passes it, or it is found in your Steam libraries.");
            ConfigEntry<bool> movement = Config.Bind("Gameplay", "MegabonkMovement", true,
                "Megabonk movement: always sprinting, the Sprint key slides, momentum, bunny hops and air strafing (your own character).");
            ConfigEntry<bool> hordes = Config.Bind("Gameplay", "MegabonkHordes", true,
                "Megabonk horde pacing: more, weaker monsters. Applies when you host; every player should use the same setting.");
            ConfigEntry<bool> bosses = Config.Bind("Gameplay", "MegabonkBossEvent", true,
                "Teleporter boss event: a Megabonk boss and swarm instead of the vanilla boss; a Megabonk swarm in the final boss fights too. Applies when you host.");
            ConfigEntry<bool> xp = Config.Bind("Gameplay", "MegabonkXp", true,
                "Megabonk XP: kills drop XP gems you collect by walking near them, shown in a Megabonk XP bar. Applies when you host.");
            ConfigEntry<bool> shrines = Config.Bind("Gameplay", "MegabonkShrines", true,
                "Megabonk Charge Shrines (stand inside to charge, everyone gets an upgrade) and Magnet Shrines (pull every XP gem) on each stage, next to Risk of Rain 2's shrines. Applies when you host.");
            ConfigEntry<bool> look = Config.Bind("Gameplay", "MegabonkLook", true,
                "Megabonk's look on stages: brighter, more saturated colours and lighter fog. Only changes your own screen.");

            Part("Melty test mode", MeltyTest.Start);
            Part("Megabonk reader", () => MegabonkLoader.Start(folder.Value, Paths.GameRootPath, Path.Combine(pluginDir, "classdata.tpk"),
                Path.Combine(DataDir, "megabonk_inventory.txt")));
            Part("Megabonk characters", () => ContentManager.collectContentPackProviders += add => add(new MegabonkSurvivors()));
            Part("Bonk/Rain style", StageMode.Init);
            var harmony = new Harmony(Guid);
            Part("Menus", () => harmony.PatchAll(typeof(Patches)));
            Part("Megabonk movement", () => { BonkMovement.Enabled = movement.Value; BonkMovement.Init(harmony); });
            if (hordes.Value) Part("Megabonk hordes", () => Horde.Init(harmony));
            Part("Upgrades", () => Upgrades.Init(harmony));
            Part("Megabonk passives", () => Passives.Init(harmony));
            if (bosses.Value) Part("Megabonk bosses", () => { MegabonkBosses.Init(harmony); gameObject.AddComponent<FinalSwarm>(); });
            Part("Test keys", () => { TestKeys.On = File.Exists(Path.Combine(DataDir, "testgod")); gameObject.AddComponent<TestKeys>(); });
            Part("Level-up menu", () => gameObject.AddComponent<UpgradePanel>());
            Part("Megabonk XP", () => { XpGems.Init(harmony, gameObject, xp.Value); gameObject.AddComponent<XpBar>(); });
            Part("Megabonk shrines", () => Shrines.Init(gameObject, shrines.Value));
            Part("Lobby style sync", () => gameObject.AddComponent<StageMode.LobbyBeacon>());
            Part("Melty join links", () => gameObject.AddComponent<MeltyLink>());
            Part("Megabonk sounds", () =>
            {
                gameObject.AddComponent<MegabonkSounds>();
                MegabonkSounds.LoadAll(System.Linq.Enumerable.Concat(System.Linq.Enumerable.Select(RiskOfBonk.Generated.Sheets.Weapons, w => w.MbSound),
                    System.Linq.Enumerable.Select(RiskOfBonk.Generated.Sheets.Sounds, s => s.MbSound)));
            });
            Part("Megabonk projectiles", () => WeaponVisuals.Init(harmony));
            Part("Melty test run", () => gameObject.AddComponent<MeltyTestRun>());
            Part("Bonk style terrain", () => BonkTerrain.Init(harmony, gameObject));
            if (look.Value) Part("Megabonk look", StageLook.Init);
            Log.Info($"{Name} {Version} loaded" + (failed > 0 ? $" ({failed} part(s) off, see the errors above)" : ""));
        }

        // A game whose exit hangs (its window gone, the process left behind) keeps the next Play from starting, so
        // once the game is closing it gets 30 seconds to finish before the process ends itself.
        static void ExitWatchdog()
        {
            new System.Threading.Thread(() =>
            {
                System.Threading.Thread.Sleep(30000);
                try { System.Diagnostics.Process.GetCurrentProcess().Kill(); } catch { }
            }) { IsBackground = true, Name = "RiskOfBonk exit watchdog" }.Start();
        }

        static class Patches
        {
            [HarmonyPostfix, HarmonyPatch(typeof(Language), nameof(Language.GetLocalizedStringByToken))]
            static void Token(string token, ref string __result)
            {
                if (token != null && Tokens.TryGetValue(token, out var s)) __result = s;
            }

            [HarmonyPostfix, HarmonyPatch(typeof(MainMenuController), "Start")]
            static void MainMenu(MainMenuController __instance)
            {
                var s = __instance.gameObject.AddComponent<MenuStyle.Styler>();
                s.title = true;
                MainMenuReached = true;
                Log.Info("Main menu: Megabonk style applied");
            }

            [HarmonyPostfix, HarmonyPatch(typeof(CharacterSelectController), "Awake")]
            static void CharacterSelect(CharacterSelectController __instance)
            {
                __instance.gameObject.AddComponent<MenuStyle.Styler>();
                __instance.gameObject.AddComponent<MenuStyle.StyleButton>().controller = __instance;
                Log.Info("Character select: Megabonk style + Bonk/Rain style button");
            }

            // Test runs only (host side): the file BepInEx/RiskOfBonk/testgod makes player bodies unkillable, and
            // testnospawn stops monster spawns, so a test character is not buried under enemies at the drop pod.
            static bool TestGodOn => File.Exists(Path.Combine(DataDir, "testgod"));
            static bool NoSpawnOn => File.Exists(Path.Combine(DataDir, "testnospawn"));

            [HarmonyPostfix, HarmonyPatch(typeof(CharacterBody), "Start")]
            static void TestGod(CharacterBody __instance)
            {
                if (!__instance.isPlayerControlled || !UnityEngine.Networking.NetworkServer.active || !__instance.healthComponent) return;
                if (!TestGodOn) return;
                __instance.healthComponent.godMode = true;
                Log.Info($"testgod: {__instance.name} is unkillable");
            }

            [HarmonyPrefix, HarmonyPatch(typeof(CombatDirector), "AttemptSpawnOnTarget")]
            static bool TestGodNoSpawns(ref bool __result)
            {
                if (!NoSpawnOn) return true;
                __result = false;
                return false;
            }

            [HarmonyPrefix, HarmonyPatch(typeof(SceneDirector), "PopulateScene")]
            static void TestGodNoStageMonsters(SceneDirector __instance)
            {
                if (NoSpawnOn) Traverse.Create(__instance).Field("monsterCredit").SetValue(0);
            }
        }
    }

    public static class Log
    {
        public static ManualLogSource Source;
        public static void Info(string m) => Source?.LogInfo(m);
        public static void Warn(string m) => Source?.LogWarning(m);
        public static void Error(string m) => Source?.LogError(m);
    }
}
