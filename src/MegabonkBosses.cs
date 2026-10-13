using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RiskOfBonk.Generated;
using RoR2;
using RoR2.Navigation;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace RiskOfBonk
{
    /// <summary>
    /// The Megabonk boss event at the Teleporter (sheets/bosses.json). Each Megabonk boss is a Risk of Rain 2 boss body
    /// and AI wearing a Megabonk model from the player's install, fitted onto that boss's skeleton like the survivors.
    /// When the Teleporter starts charging, the host spawns one (rotating by stage) into the Teleporter's boss squad
    /// instead of the vanilla boss, and sends a Megabonk swarm with it. The Teleporter finishes once the boss is dead,
    /// with the normal boss rewards.
    /// </summary>
    public static class MegabonkBosses
    {
        sealed class Built
        {
            public BossRow Row;
            public GameObject Master;
            public CharacterSpawnCard Card;
        }

        static readonly List<Built> built = new List<Built>();

        public static void BuildAll(Dictionary<string, string> keys, List<GameObject> bodies, List<GameObject> masters)
        {
            var finals = new List<string>();
            foreach (var row in Sheets.Bosses.Where(r => r.Role == "final"))
            {
                try { finals.AddRange(ReskinFinal(row, keys)); }
                catch (Exception e) { Log.Error($"final boss {row.DisplayName}: {e}"); }
            }
            if (finals.Count > 0) Log.Info($"Megabonk final bosses: {string.Join(", ", finals)}");
            foreach (var row in Sheets.Bosses.Where(r => r.Role == "teleporter"))
            {
                try
                {
                    var b = Build(row, keys);
                    if (b == null) continue;
                    bodies.Add(b.Master.GetComponent<CharacterMaster>().bodyPrefab);
                    masters.Add(b.Master);
                    built.Add(b);
                }
                catch (Exception e) { Log.Error($"boss {row.DisplayName}: {e}"); }
            }
            Log.Info($"Megabonk bosses ready: {string.Join(", ", built.Select(b => b.Row.DisplayName))} ({built.Count}/{Sheets.Bosses.Count(r => r.Role == "teleporter")})");
        }

        static RawModel Model(BossRow row)
        {
            var lib = MegabonkLoader.Library;
            RawModel raw;
            lock (lib) raw = lib.FindModel(row.MbNameHints);
            if (raw == null || raw.VertexCount == 0) return null;
            if (!string.IsNullOrEmpty(row.MbTexture))
            {
                RawTexture tex;
                lock (lib) tex = lib.FindTexture(new[] { row.MbTexture });
                var biggest = raw.SubMeshes.OrderByDescending(sm => sm.Triangles.Length).FirstOrDefault();
                if (tex != null && biggest != null) biggest.Texture = tex;
            }
            return raw;
        }

        /// <summary>
        /// A final boss keeps its own fight; only its look and name become Megabonk's. The game's own body prefabs are
        /// changed in memory for this session (every listed phase body), nothing on disk.
        /// Bodies from expansions the player does not own are simply skipped.
        /// </summary>
        static IEnumerable<string> ReskinFinal(BossRow row, Dictionary<string, string> keys)
        {
            var done = new List<string>();
            string tok = "RISKOFBONK_BOSS_" + row.Id.ToUpperInvariant();
            Plugin.Tokens[tok + "_NAME"] = row.DisplayName;
            Plugin.Tokens[tok + "_SUBTITLE"] = row.Subtitle;
            foreach (var bodyName in row.Replaces)
            {
                if (!keys.TryGetValue(bodyName + ".prefab", out var key)) { Log.Info($"final boss {row.DisplayName}: no {bodyName} in this game; skipped"); continue; }
                var raw = Model(row);
                if (raw == null) { Log.Warn($"final boss {row.DisplayName}: no model matching [{string.Join(", ", row.MbNameHints)}]"); break; }
                var body = Addressables.LoadAssetAsync<GameObject>(key).WaitForCompletion();
                var modelRoot = body ? body.GetComponent<ModelLocator>()?.modelTransform : null;
                if (modelRoot == null) { Log.Warn($"final boss {row.DisplayName}: {bodyName} has no model"); continue; }
                var asRow = new CharacterRow("boss_" + row.Id, row.DisplayName, row.MbNameHints, "", bodyName, "#FFFFFF", 0f, 0f, 0f, row.Subtitle, row.MbTexture);
                MegabonkSurvivors.Reskin(modelRoot, raw, UnityConvert.Mesh(raw), asRow, MegabonkLoader.Library, row.Retarget);
                var cb = body.GetComponent<CharacterBody>();
                cb.baseNameToken = tok + "_NAME";
                cb.subtitleNameToken = tok + "_SUBTITLE";
                done.Add($"{row.DisplayName} on {bodyName}");
            }
            return done;
        }

        static Built Build(BossRow row, Dictionary<string, string> keys)
        {
            var lib = MegabonkLoader.Library;
            RawModel raw;
            lock (lib) raw = lib.FindModel(row.MbNameHints);
            if (raw == null || raw.VertexCount == 0)
            {
                Log.Warn($"boss {row.DisplayName}: no model matching [{string.Join(", ", row.MbNameHints)}]; skipped.");
                return null;
            }
            if (!string.IsNullOrEmpty(row.MbTexture))
            {
                RawTexture tex;
                lock (lib) tex = lib.FindTexture(new[] { row.MbTexture });
                var biggest = raw.SubMeshes.OrderByDescending(sm => sm.Triangles.Length).FirstOrDefault();
                if (tex != null && biggest != null) biggest.Texture = tex;
            }
            if (!keys.TryGetValue(row.RorBody + ".prefab", out var bodyKey)) throw new Exception($"body {row.RorBody} not in Addressables");
            if (!keys.TryGetValue(row.RorMaster + ".prefab", out var masterKey)) throw new Exception($"master {row.RorMaster} not in Addressables");
            var srcBody = Addressables.LoadAssetAsync<GameObject>(bodyKey).WaitForCompletion();
            var srcMaster = Addressables.LoadAssetAsync<GameObject>(masterKey).WaitForCompletion();

            string id = "MBBoss" + MegabonkSurvivors.Pascal(row.Id);
            var body = MegabonkSurvivors.Clone(srcBody, id + "Body", true);
            var master = MegabonkSurvivors.Clone(srcMaster, id + "Master", true);
            master.GetComponent<CharacterMaster>().bodyPrefab = body;

            string tok = "RISKOFBONK_BOSS_" + row.Id.ToUpperInvariant();
            Plugin.Tokens[tok + "_NAME"] = row.DisplayName;
            Plugin.Tokens[tok + "_SUBTITLE"] = row.Subtitle;
            var cb = body.GetComponent<CharacterBody>();
            cb.baseNameToken = tok + "_NAME";
            cb.subtitleNameToken = tok + "_SUBTITLE";

            var modelRoot = body.GetComponent<ModelLocator>()?.modelTransform;
            if (modelRoot == null) throw new Exception("boss body has no model");
            var asRow = new CharacterRow("boss_" + row.Id, row.DisplayName, row.MbNameHints, "", row.RorBody, "#FFFFFF", 0f, 0f, 0f, row.Subtitle, row.MbTexture);
            MegabonkSurvivors.Reskin(modelRoot, raw, UnityConvert.Mesh(raw), asRow, lib, row.Retarget);

            var card = ScriptableObject.CreateInstance<CharacterSpawnCard>();
            card.name = "csc" + id;
            card.prefab = master;
            card.sendOverNetwork = true;
            card.hullSize = HullClassification.BeetleQueen;
            card.nodeGraphType = row.Airborne ? MapNodeGroup.GraphType.Air : MapNodeGroup.GraphType.Ground;
            card.requiredFlags = NodeFlags.None;
            card.forbiddenFlags = NodeFlags.NoCharacterSpawn;
            card.directorCreditCost = 600;
            card.occupyPosition = false;

            MegabonkSurvivors.NetworkPrefabs.Add(body);
            MegabonkSurvivors.NetworkPrefabs.Add(master);
            Log.Info($"boss {row.DisplayName}: Megabonk model '{raw.SourceName}' on {row.RorBody}");
            return new Built { Row = row, Master = master, Card = card };
        }

        public static void Init(Harmony harmony) => harmony.PatchAll(typeof(Patches));

        [HarmonyPatch]
        static class Patches
        {
            [HarmonyPostfix, HarmonyPatch(typeof(TeleporterInteraction.ChargingState), nameof(TeleporterInteraction.ChargingState.OnEnter))]
            static void BossEvent()
            {
                if (!NetworkServer.active || built.Count == 0) return;
                var tp = TeleporterInteraction.instance;
                if (!tp || !tp.bossDirector || !tp.bossDirector.combatSquad || !DirectorCore.instance) return;
                try { Spawn(tp); }
                catch (Exception e) { Log.Error("boss event: " + e); }
            }
        }

        static void Spawn(TeleporterInteraction tp)
        {
            var run = Run.instance;
            var b = built[(run ? run.stageClearCount : 0) % built.Count];
            // Test only: the file BepInEx/RiskOfBonk/testboss holding a boss id picks that boss.
            string forced = System.IO.Path.Combine(Plugin.DataDir, "testboss");
            if (System.IO.File.Exists(forced))
                b = built.FirstOrDefault(x => x.Row.Id == System.IO.File.ReadAllText(forced).Trim()) ?? b;
            var rng = new Xoroshiro128Plus(run ? run.stageRng.nextUlong : (ulong)DateTime.Now.Ticks);
            var rule = new DirectorPlacementRule
            {
                placementMode = DirectorPlacementRule.PlacementMode.Approximate,
                minDistance = 15f,
                maxDistance = 70f,
                spawnOnTarget = tp.transform,
            };
            var req = new DirectorSpawnRequest(b.Card, rule, rng) { teamIndexOverride = TeamIndex.Monster, ignoreTeamMemberLimit = true };
            var go = DirectorCore.instance.TrySpawnObject(req);
            var master = go ? go.GetComponent<CharacterMaster>() : null;
            if (!master)
            {
                Log.Warn($"boss event: could not place {b.Row.DisplayName}; the vanilla boss comes instead.");
                return;
            }
            // Replace the vanilla boss: the boss director (and a companion boss) stop, ours joins their squad.
            tp.bossDirector.enabled = false;
            tp.bossDirector.monsterCredit = 0f;
            if (Traverse.Create(tp).Field("companionBoss").GetValue() is CombatDirector companion && companion) companion.enabled = false; // newer builds
            tp.bossDirector.combatSquad.AddMember(master);
#pragma warning disable CS0618
            if (b.Row.HealthBoost > 0) master.inventory.GiveItem(RoR2Content.Items.BoostHp, b.Row.HealthBoost * Mathf.Max(1, run ? run.participatingPlayerCount : 1));
#pragma warning restore CS0618
            // The Megabonk swarm that comes with the boss.
            if (tp.bonusDirector)
                tp.bonusDirector.monsterCredit += Tune.BossSwarmCredit * (run ? Mathf.Sqrt(run.compensatedDifficultyCoefficient) : 1f);
            Log.Info($"boss event: {b.Row.DisplayName} spawned at the Teleporter");
        }
    }
}
