using System.Collections.Generic;
using System.Linq;
using RiskOfBonk.Generated;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace RiskOfBonk
{
    /// <summary>
    /// Megabonk's main bosses come with a horde. While a final boss is alive (Mithrix as the Ghost King, False Son as the
    /// Shadow King, Voidling), the host runs an extra monster director that gets tuning.final_swarm_credit credits every
    /// second (scaled with difficulty) and spends them on the stage's own monsters, which get the horde changes too.
    /// The director goes away with the boss.
    /// </summary>
    public sealed class FinalSwarm : MonoBehaviour
    {
        static HashSet<string> bodies;
        CombatDirector director;
        float nextCheck;

        static HashSet<string> Bodies()
        {
            if (bodies != null) return bodies;
            bodies = new HashSet<string>(Sheets.Bosses.Where(b => b.Role == "final").SelectMany(b => b.Replaces));
            foreach (var n in new[] { "MiniVoidRaidCrabBodyPhase1", "MiniVoidRaidCrabBodyPhase2", "MiniVoidRaidCrabBodyPhase3", "VoidRaidCrabBody" })
                bodies.Add(n);
            return bodies;
        }

        static bool FinalBossAlive()
        {
            var names = Bodies();
            foreach (var b in CharacterBody.readOnlyInstancesList)
            {
                if (!b || !b.healthComponent || !b.healthComponent.alive) continue;
                if (b.teamComponent && b.teamComponent.teamIndex == TeamIndex.Player) continue;
                var index = b.bodyIndex;
                if (index != BodyIndex.None && names.Contains(BodyCatalog.GetBodyName(index))) return true;
            }
            return false;
        }

        void FixedUpdate()
        {
            if (!NetworkServer.active || !Run.instance) { Stop(); return; }
            if (director)
            {
                float diff = Mathf.Sqrt(Mathf.Max(1f, Run.instance.compensatedDifficultyCoefficient));
                director.monsterCredit += Tune.FinalSwarmCredit * diff * Time.fixedDeltaTime;
            }
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + 1f;
            bool fight = FinalBossAlive();
            if (fight && !director) Begin();
            else if (!fight && director) Stop();
        }

        void Begin()
        {
            // Set up inactive so the fields are in place before the director's Awake.
            var go = new GameObject("RiskOfBonkFinalSwarm");
            go.SetActive(false);
            director = go.AddComponent<CombatDirector>();
            director.customName = "RiskOfBonkFinalSwarm";
            director.teamIndex = TeamIndex.Monster;
            director.moneyWaveIntervals = new[] { new RangeFloat { min = 1f, max = 1f } };
            director.creditMultiplier = 0f; // only the credit given above, not the normal money waves
            director.expRewardCoefficient = 0.2f;
            director.goldRewardCoefficient = 1f;
            director.targetPlayers = true;
            director.fallBackToStageMonsterCards = true;
            director.onSpawnedServer = new CombatDirector.OnSpawnedServer();
            director.spawnDistanceMultiplier = 0.8f;
            go.SetActive(true);
            Log.Info("final boss fight: Megabonk swarm on");
        }

        void Stop()
        {
            if (!director) return;
            director.monsterCredit = 0f; // don't hand the credit to the stage's other directors
            Destroy(director.gameObject);
            director = null;
            Log.Info("final boss fight over: Megabonk swarm off");
        }
    }
}
