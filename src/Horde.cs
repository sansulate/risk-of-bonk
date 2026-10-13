using HarmonyLib;
using RiskOfBonk.Generated;
using RoR2;
using UnityEngine;

namespace RiskOfBonk
{
    /// <summary>
    /// Megabonk horde pacing (sheets/tuning.json, system "hordes"): monster directors get more credit, bigger and faster
    /// waves of cheap monsters, a higher team limit, and non-champion monsters have less health.
    /// Boss squads (the Teleporter boss) are left alone. Every player runs the same sheet values, so the health change,
    /// which each client computes for itself, stays consistent.
    /// </summary>
    public static class Horde
    {
        [HarmonyPatch]
        static class Patches
        {
            [HarmonyPrefix, HarmonyPatch(typeof(CombatDirector), "Awake")]
            static void Director(CombatDirector __instance)
            {
                if (__instance.teamIndex != TeamIndex.Monster || __instance.combatSquad) return;
                __instance.creditMultiplier *= Tune.HordeCredit;
                __instance.skipSpawnIfTooCheap = false;
                __instance.maximumNumberToSpawnBeforeSkipping = Mathf.Max(__instance.maximumNumberToSpawnBeforeSkipping, (int)Tune.HordeWaveSize);
                __instance.minRerollSpawnInterval *= Tune.HordeInterval;
                __instance.maxRerollSpawnInterval *= Tune.HordeInterval;
            }

            [HarmonyPostfix, HarmonyPatch(typeof(Run), "Start")]
            static void TeamLimit()
            {
                var def = TeamCatalog.GetTeamDef(TeamIndex.Monster);
                if (def != null) def.softCharacterLimit = Mathf.Max(def.softCharacterLimit, (int)Tune.HordeLimit);
            }

            [HarmonyPrefix, HarmonyPatch(typeof(CharacterBody), "Start")]
            static void Weaker(CharacterBody __instance)
            {
                if (__instance.isPlayerControlled || __instance.isChampion) return;
                if (!__instance.teamComponent || __instance.teamComponent.teamIndex != TeamIndex.Monster) return;
                __instance.baseMaxHealth *= Tune.HordeHealth;
                __instance.levelMaxHealth *= Tune.HordeHealth;
            }
        }

        public static void Init(Harmony harmony) => harmony.PatchAll(typeof(Patches));
    }
}
