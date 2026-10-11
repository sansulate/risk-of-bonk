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
        public const string Version = "0.1.0";

        public static readonly Dictionary<string, string> Tokens = new Dictionary<string, string>();
        public static string DataDir;

        void Awake()
        {
            Log.Source = Logger;
            string pluginDir = Path.GetDirectoryName(Info.Location);
            DataDir = Path.Combine(Paths.BepInExRootPath, "RiskOfBonk");
            Directory.CreateDirectory(DataDir);

            ConfigEntry<string> folder = Config.Bind("Megabonk", "MegabonkFolder", "",
                "Your Megabonk install folder. Leave empty: Melty passes it, or it is found in your Steam libraries.");

            MegabonkLoader.Start(folder.Value, Paths.GameRootPath, Path.Combine(pluginDir, "classdata.tpk"),
                Path.Combine(DataDir, "megabonk_inventory.txt"));

            ContentManager.collectContentPackProviders += add => add(new MegabonkSurvivors());
            StageMode.Init();
            new Harmony(Guid).PatchAll(typeof(Patches));
            gameObject.AddComponent<StageMode.LobbyBeacon>();
            Log.Info($"{Name} {Version} loaded");
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
                Log.Info("Main menu: Megabonk style applied");
            }

            [HarmonyPostfix, HarmonyPatch(typeof(CharacterSelectController), "Awake")]
            static void CharacterSelect(CharacterSelectController __instance)
            {
                __instance.gameObject.AddComponent<MenuStyle.Styler>();
                __instance.gameObject.AddComponent<MenuStyle.StyleButton>().controller = __instance;
                Log.Info("Character select: Megabonk style + Bonk/Rain style button");
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
