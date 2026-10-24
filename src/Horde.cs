using HarmonyLib;
using RiskOfBonk.Generated;
using RoR2;
using UnityEngine;

namespace RiskOfBonk
{
    /// <summary>
    /// Megabonk horde pacing (sheets/tuning.json, system "hordes"): monster directors get more credit, bigger and faster
    /// waves of cheap monsters, a higher team limit, and non-champion monsters have less health. Like Megabonk's
    /// StageSummoner, a target number of monsters alive ramps up over each stage and the directors fill up to it.
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

            // Megabonk keeps a target number of enemies alive that ramps over the stage (StageSummoner.GetNumTargetEnemies);
            // below it, the stage's directors earn extra credit per missing monster.
            [HarmonyPrefix, HarmonyPatch(typeof(CombatDirector), "FixedUpdate")]
            static void Fill(CombatDirector __instance)
            {
                if (!UnityEngine.Networking.NetworkServer.active || !__instance.enabled || __instance.teamIndex != TeamIndex.Monster || __instance.combatSquad) return;
                int missing = Target() - TeamComponent.GetTeamMembers(TeamIndex.Monster).Count;
                if (missing > 0) __instance.monsterCredit += Tune.HordeFillCredit * missing * Time.fixedDeltaTime / Mathf.Max(1, Directors());
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

        /// <summary>Megabonk's target of enemies alive at this point of the stage, scaled to Risk of Rain 2 monsters.</summary>
        public static int Target()
        {
            if (!Run.instance || !Stage.instance) return 0;
            float t = Run.instance.GetRunStopwatch() - Stage.instance.entryStopwatchValue;
            float megabonk = (Tune.HordeTargetStart + Tune.HordeTargetRamp * Mathf.Clamp01(t / Tune.HordeStageSeconds))
                             * (1f + Tune.HordeTargetStageGrowth * Run.instance.stageClearCount);
            return Mathf.Min((int)Tune.HordeLimit, Mathf.RoundToInt(megabonk * Tune.HordeDensity));
        }

        static float directorsAt = -1f;
        static int directors;

        /// <summary>Active monster directors without a boss squad (they share the extra credit).</summary>
        static int Directors()
        {
            if (Time.time - directorsAt < 2f && directorsAt >= 0f) return directors;
            directorsAt = Time.time;
            directors = 0;
            foreach (var d in CombatDirector.instancesList)
                if (d && d.enabled && d.teamIndex == TeamIndex.Monster && !d.combatSquad) directors++;
            return directors;
        }

        public static void Init(Harmony harmony) => harmony.PatchAll(typeof(Patches));
    }
}
