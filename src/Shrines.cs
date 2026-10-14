using System.Collections.Generic;
using RiskOfBonk.Generated;
using RoR2;
using RoR2.Navigation;
using RoR2.Networking;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace RiskOfBonk
{
    /// <summary>
    /// Megabonk shrines on Risk of Rain 2 stages, next to the game's own shrines. Charge Shrine: stand in its circle until
    /// it is charged (leaving pauses it and slowly drains it) and every player gets an upgrade pick. Magnet Shrine: stand
    /// in it briefly and every XP gem on the map flies to you. Moai Shrine: every player gets a random tome. Greed Shrine:
    /// more gold and more monsters for the rest of the stage. Challenge Shrine: elite champions come; kill them all and
    /// every player gets an upgrade pick. The host places them on the stage's ground nodes and runs
    /// the charging; every player sees them through messages (sent again every few seconds, so a player whose stage
    /// loaded late still gets them).
    /// </summary>
    public static class Shrines
    {
        public const short MsgType = 0x5B0F;
        const byte OpUpsert = 0, OpRemove = 1;
        public const byte KindCharge = 0, KindMagnet = 1, KindMoai = 2, KindGreed = 3, KindChallenge = 4;
        static readonly string[] kindNames = { "Charge", "Magnet", "Moai", "Greed", "Challenge" };

        sealed class ShrineMessage : MessageBase
        {
            public byte op, kind, progress;
            public uint id;
            public Vector3 pos;
            public bool used;
            public override void Serialize(NetworkWriter w)
            {
                w.Write(op); w.WritePackedUInt32(id);
                if (op == OpUpsert) { w.Write(kind); w.Write(pos); w.Write(progress); }
                else w.Write(used);
            }
            public override void Deserialize(NetworkReader r)
            {
                op = r.ReadByte(); id = r.ReadPackedUInt32();
                if (op == OpUpsert) { kind = r.ReadByte(); pos = r.ReadVector3(); progress = r.ReadByte(); }
                else used = r.ReadBoolean();
            }
        }

        static float Seconds(byte kind) => kind == KindCharge ? Tune.ChargeShrineSeconds : kind == KindMagnet ? Tune.MagnetShrineSeconds : Tune.ShrineUseSeconds;
        static float Radius(byte kind) => kind == KindCharge ? Tune.ChargeShrineRadius : 3f;

        // ---------------- host ----------------

        sealed class Shrine { public uint id; public byte kind; public Vector3 pos; public float progress; public byte sent; }
        static readonly List<Shrine> shrines = new List<Shrine>();
        static uint nextId = 1;

        static void Send(ShrineMessage m) { if (NetworkServer.active) NetworkServer.SendToAll(MsgType, m); }
        static void SendState(Shrine s)
        {
            s.sent = (byte)Mathf.RoundToInt(s.progress * 255f);
            Send(new ShrineMessage { op = OpUpsert, id = s.id, kind = s.kind, pos = s.pos, progress = s.sent });
        }

        /// <summary>Places the shrines on a normal stage (one with a Teleporter), spread out and away from the Teleporter.</summary>
        static void Place(SceneDirector director)
        {
            shrines.Clear();
            if (!Run.instance || !TeleporterInteraction.instance || !SceneInfo.instance || !SceneInfo.instance.groundNodes) return;
            var graph = SceneInfo.instance.groundNodes;
            var nodes = graph.GetActiveNodesForHullMaskWithFlagConditions(HullMask.Golem, NodeFlags.None, NodeFlags.NoShrineSpawn);
            if (nodes.Count == 0) return;
            var rng = new Xoroshiro128Plus(Run.instance.stageRng.nextUlong);
            var taken = new List<Vector3> { TeleporterInteraction.instance.transform.position };
            void Add(byte kind, int count)
            {
                for (int n = 0; n < count; n++)
                    for (int attempt = 0; attempt < 40; attempt++)
                    {
                        if (!graph.GetNodePosition(nodes[rng.RangeInt(0, nodes.Count)], out var p)) continue;
                        bool clear = true;
                        foreach (var t in taken) if ((t - p).sqrMagnitude < 30f * 30f) { clear = false; break; }
                        if (!clear) continue;
                        if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var hit, 10f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                            p = hit.point;
                        taken.Add(p);
                        shrines.Add(new Shrine { id = nextId++, kind = kind, pos = p });
                        break;
                    }
            }
            Add(KindCharge, (int)Tune.ChargeShrinesPerStage);
            Add(KindMagnet, (int)Tune.MagnetShrinesPerStage);
            Add(KindMoai, (int)Tune.MoaiShrinesPerStage);
            Add(KindGreed, (int)Tune.GreedShrinesPerStage);
            Add(KindChallenge, (int)Tune.ChallengeShrinesPerStage);
            Log.Info($"Megabonk shrines placed: {shrines.Count}");
        }

        /// <summary>Host, test keys: where the n-th remaining shrine is, rarest kind first (cycling), or null.</summary>
        public static Vector3? Position(int n)
        {
            if (shrines.Count == 0) return null;
            var order = new List<Shrine>(shrines);
            order.Sort((x, y) => y.kind.CompareTo(x.kind));
            return order[((n % order.Count) + order.Count) % order.Count].pos;
        }

        static void Complete(Shrine s, CharacterBody by)
        {
            if (s.kind == KindCharge)
            {
                int picks = Mathf.Max(1, (int)Tune.ChargeShrinePicks);
                foreach (var pc in PlayerCharacterMasterController.instances)
                    if (pc && pc.master && pc.master.inventory) pc.master.inventory.GiveItemPermanent(Upgrades.BonusPicksItem, picks);
                Chat.SendBroadcastChat(new Chat.SimpleChatMessage { baseToken = "<style=cShrine>A Charge Shrine is charged: everyone gets an upgrade.</style>" });
            }
            else if (s.kind == KindMagnet) { if (by) XpGems.MagnetTo(by); }
            else if (s.kind == KindMoai) Moai();
            else if (s.kind == KindGreed) Greed();
            else if (s.kind == KindChallenge) Challenge(s.pos);
            Log.Info($"{kindNames[s.kind]} Shrine used{(by ? " by " + by.GetUserName() : "")}");
            Send(new ShrineMessage { op = OpRemove, id = s.id, used = true });
        }

        static readonly System.Random rng = new System.Random();

        static void Say(string text) => Chat.SendBroadcastChat(new Chat.SimpleChatMessage { baseToken = "<style=cShrine>" + text + "</style>" });

        /// <summary>Moai: every player gets a random tome at a rolled rarity.</summary>
        static void Moai()
        {
            foreach (var pc in PlayerCharacterMasterController.instances)
            {
                var inv = pc && pc.master ? pc.master.inventory : null;
                if (!inv || Upgrades.TomeItems.Length == 0) continue;
                int u = rng.Next(Upgrades.TomeItems.Length), r = Upgrades.RollRarity(rng, inv);
                inv.GiveItemPermanent(Upgrades.TomeItems[u], Sheets.Rarities[r].Points);
                Say($"Moai Shrine: {(pc.networkUser ? pc.networkUser.userName : "someone")} gets {Sheets.Rarities[r].Label} {Sheets.Upgrades[u].DisplayName}.");
                Log.Info($"Moai Shrine: {Sheets.Rarities[r].Label} {Sheets.Upgrades[u].DisplayName} (+{Sheets.Rarities[r].Points})");
            }
        }

        /// <summary>Greed: this stage's monster directors pay more gold and spend more.</summary>
        static void Greed()
        {
            foreach (var d in CombatDirector.instancesList)
            {
                if (!d || d.teamIndex != TeamIndex.Monster) continue;
                d.goldRewardCoefficient *= Tune.GreedGold;
                d.creditMultiplier *= Tune.GreedMonsters;
            }
            Say("Greed Shrine: more gold, and more monsters, for the rest of this stage.");
        }

        sealed class Trial { public GameObject director; public readonly List<CharacterMaster> masters = new List<CharacterMaster>(); public float started; }
        static readonly List<Trial> trials = new List<Trial>();

        /// <summary>Challenge: one wave of elite champions from the stage's own monsters (like a Shrine of Combat).</summary>
        static void Challenge(Vector3 pos)
        {
            var go = new GameObject("RiskOfBonkChallenge");
            go.transform.position = pos;
            go.SetActive(false);
            var director = go.AddComponent<CombatDirector>();
            director.customName = "RiskOfBonkChallenge";
            director.teamIndex = TeamIndex.Monster;
            director.moneyWaveIntervals = new RangeFloat[0];
            director.creditMultiplier = 0f;
            director.shouldSpawnOneWave = true;
            director.targetPlayers = true;
            director.eliteBias = 0.5f; // cheaper elites, so most of the wave is elite
            director.fallBackToStageMonsterCards = true;
            director.onSpawnedServer = new CombatDirector.OnSpawnedServer();
            var trial = new Trial { director = go, started = Time.time };
            director.onSpawnedServer.AddListener(spawned => { var m = spawned ? spawned.GetComponent<CharacterMaster>() : null; if (m) trial.masters.Add(m); });
            go.SetActive(true);
            float credit = Tune.ChallengeCredit * (Run.instance ? Run.instance.difficultyCoefficient : 1f);
            var card = Champion(credit) ?? director.SelectMonsterCardForCombatShrine(credit);
            if (card != null) director.StartManualMonsterWave(credit, card);
            Log.Info($"Challenge Shrine: {(card != null ? card.GetSpawnCard().name : "no monster fits")} for {credit:0} credits");
            trials.Add(trial);
            Say("Challenge Shrine: a challenge is coming. Beat it for an upgrade!");
        }

        /// <summary>A champion (big monster) from the stage's own monsters that the credit can pay for, if there is one.</summary>
        static DirectorCard Champion(float credit)
        {
            var selection = ClassicStageInfo.instance ? ClassicStageInfo.instance.monsterSelection : null;
            if (selection == null) return null;
            var options = new List<DirectorCard>();
            for (int i = 0; i < selection.Count; i++)
            {
                var card = selection.GetChoice(i).value;
                var master = card?.GetSpawnCard() ? card.GetSpawnCard().prefab.GetComponent<CharacterMaster>() : null;
                var body = master && master.bodyPrefab ? master.bodyPrefab.GetComponent<CharacterBody>() : null;
                if (body && body.isChampion && card.IsAvailable() && card.cost <= credit) options.Add(card);
            }
            return options.Count > 0 ? options[rng.Next(options.Count)] : null;
        }

        static void TickTrials()
        {
            for (int i = trials.Count - 1; i >= 0; i--)
            {
                var t = trials[i];
                bool anyAlive = false;
                foreach (var m in t.masters) if (m && !m.IsDeadAndOutOfLivesServer()) { anyAlive = true; break; }
                bool spawned = t.masters.Count > 0;
                // Won when everything that came is dead; if nothing could spawn, the reward comes anyway.
                if (anyAlive || (!spawned && Time.time - t.started < 10f) || (spawned && Time.time - t.started < 3f)) continue;
                trials.RemoveAt(i);
                if (t.director) Object.Destroy(t.director);
                foreach (var pc in PlayerCharacterMasterController.instances)
                    if (pc && pc.master && pc.master.inventory) pc.master.inventory.GiveItemPermanent(Upgrades.BonusPicksItem, 1);
                Say($"Challenge beaten ({t.masters.Count} elites): everyone gets an upgrade.");
                Log.Info($"Challenge Shrine beaten: {t.masters.Count} monsters");
            }
        }

        /// <summary>Host tick: charge shrines with a living player inside; drain the others slowly.</summary>
        public sealed class Server : MonoBehaviour
        {
            float nextBroadcast;

            void FixedUpdate()
            {
                if (!NetworkServer.active) return;
                if (trials.Count > 0) TickTrials();
                if (shrines.Count == 0) return;
                float dt = Time.fixedDeltaTime;
                for (int i = shrines.Count - 1; i >= 0; i--)
                {
                    var s = shrines[i];
                    CharacterBody inside = null;
                    float r = Radius(s.kind);
                    foreach (var pc in PlayerCharacterMasterController.instances)
                    {
                        var body = pc && pc.master ? pc.master.GetBody() : null;
                        if (!body || !body.healthComponent || !body.healthComponent.alive) continue;
                        var d = body.footPosition - s.pos;
                        if (Mathf.Abs(d.y) < 4f && new Vector2(d.x, d.z).sqrMagnitude <= r * r) { inside = body; break; }
                    }
                    float seconds = Mathf.Max(0.1f, Seconds(s.kind));
                    s.progress = Mathf.Clamp01(s.progress + (inside ? dt / seconds : -dt / (seconds * 4f)));
                    if (s.progress >= 1f)
                    {
                        shrines.RemoveAt(i);
                        Complete(s, inside);
                        continue;
                    }
                    if (Mathf.Abs(Mathf.RoundToInt(s.progress * 255f) - s.sent) >= 6) SendState(s);
                }
                if (Time.time >= nextBroadcast)
                {
                    nextBroadcast = Time.time + 2f;
                    foreach (var s in shrines) SendState(s);
                }
            }
        }

        // ---------------- every machine: visuals ----------------

        sealed class Visual
        {
            public GameObject go;
            public Transform crystal, fillTr;
            public MeshFilter fill;
            public byte kind;
            public float progress, shown, usedAt = -1f, phase;
        }

        const float CrystalHeight = 4.5f; // over a player standing in the middle
        static readonly Dictionary<uint, Visual> visuals = new Dictionary<uint, Visual>();
        static readonly Color[] kindColors =
        {
            new Color(1f, 0.78f, 0.15f),  // charge: gold
            new Color(0.35f, 0.55f, 1f),  // magnet: blue
            new Color(0.7f, 0.6f, 0.9f),  // moai: lilac stone
            new Color(0.3f, 0.95f, 0.35f), // greed: green
            new Color(1f, 0.25f, 0.2f),   // challenge: red
        };
        static readonly Dictionary<Color, Material> mats = new Dictionary<Color, Material>();

        static Material Mat(Color c, float power)
        {
            var key = new Color(c.r, c.g, c.b, power);
            if (mats.TryGetValue(key, out var m) && m) return m;
            var shader = Addressables.LoadAssetAsync<Shader>("RoR2/Base/Shaders/HGStandard.shader").WaitForCompletion();
            m = new Material(shader);
            m.SetColor("_Color", c);
            if (m.HasProperty("_EmColor")) m.SetColor("_EmColor", c);
            if (m.HasProperty("_EmPower")) m.SetFloat("_EmPower", power);
            mats[key] = m;
            return m;
        }

        /// <summary>A flat ring (annulus) lying on the ground, the first <paramref name="fraction"/> of the way round.</summary>
        static Mesh Ring(float inner, float outer, float fraction, Mesh mesh = null)
        {
            const int Segments = 64;
            int seg = Mathf.Max(1, Mathf.CeilToInt(Segments * Mathf.Clamp01(fraction)));
            var verts = new Vector3[(seg + 1) * 2];
            var tris = new int[seg * 12];
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.PI * 2f * Mathf.Clamp01(fraction) * i / seg;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                verts[i * 2] = dir * inner;
                verts[i * 2 + 1] = dir * outer;
            }
            for (int i = 0; i < seg; i++)
            {
                int v = i * 2, t = i * 12;
                // Both faces, so it shows from below on slopes too.
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                tris[t + 3] = v + 1; tris[t + 4] = v + 3; tris[t + 5] = v + 2;
                tris[t + 6] = v; tris[t + 7] = v + 2; tris[t + 8] = v + 1;
                tris[t + 9] = v + 1; tris[t + 10] = v + 2; tris[t + 11] = v + 3;
            }
            if (mesh == null) mesh = new Mesh();
            mesh.Clear();
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static GameObject Part(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        static Visual Create(byte kind, Vector3 pos)
        {
            var color = kindColors[Mathf.Min(kind, kindColors.Length - 1)];
            float r = Radius(kind);
            var go = new GameObject("RiskOfBonk" + kindNames[Mathf.Min(kind, kindNames.Length - 1)] + "Shrine");
            go.transform.position = pos + Vector3.up * 0.08f;
            Part(go.transform, "Ring", Ring(r - 0.25f, r, 1f), Mat(color, 1.2f));
            Part(go.transform, "Base", Ring(r - 0.95f, r - 0.35f, 1f), Mat(color * 0.3f, 0.1f));
            var fill = Part(go.transform, "Fill", Ring(r - 0.95f, r - 0.35f, 0.001f), Mat(color, 2f));
            fill.transform.localPosition = Vector3.up * 0.03f;
            // A big spinning crystal over the middle, like Megabonk's shrines; a magnet shrine's is smaller and blue.
            var crystal = Part(go.transform, "Crystal", XpGems.GemMesh(), Mat(color, 0.8f));
            crystal.transform.localPosition = Vector3.up * CrystalHeight;
            crystal.transform.localScale = Vector3.one * (kind == KindCharge ? 2.6f : 1.8f);
            return new Visual { go = go, crystal = crystal.transform, fill = fill.GetComponent<MeshFilter>(), fillTr = fill.transform, kind = kind, phase = Random.value * 6f };
        }

        static void OnMessage(NetworkMessage msg)
        {
            var m = msg.ReadMessage<ShrineMessage>();
            if (m.op == OpUpsert)
            {
                if (!visuals.TryGetValue(m.id, out var v) || !v.go) visuals[m.id] = v = Create(m.kind, m.pos);
                v.progress = m.progress / 255f;
            }
            else if (visuals.TryGetValue(m.id, out var v))
            {
                if (m.used && v.go)
                {
                    v.usedAt = Time.time;
                    v.progress = 1f;
                    Util.PlaySound("Play_UI_shrineActivate", v.go);
                }
                else { if (v.go) Object.Destroy(v.go); visuals.Remove(m.id); }
            }
        }

        public sealed class Client : MonoBehaviour
        {
            readonly List<uint> done = new List<uint>();

            void Update()
            {
                if (visuals.Count == 0) return;
                float now = Time.time;
                done.Clear();
                foreach (var kv in visuals)
                {
                    var v = kv.Value;
                    if (!v.go) { done.Add(kv.Key); continue; }
                    float r = Radius(v.kind);
                    v.crystal.Rotate(0f, (60f + 300f * v.progress) * Time.deltaTime, 0f, Space.World);
                    v.crystal.localPosition = Vector3.up * (CrystalHeight + 0.25f * Mathf.Sin(now * 2f + v.phase));
                    if (Mathf.Abs(v.shown - v.progress) > 0.004f)
                    {
                        v.shown = Mathf.MoveTowards(v.shown, v.progress, Time.deltaTime * 2f);
                        Ring(r - 0.95f, r - 0.35f, Mathf.Max(0.001f, v.shown), v.fill.sharedMesh);
                    }
                    if (v.usedAt >= 0f)
                    {
                        // Used: the crystal shoots up and the whole shrine shrinks away.
                        float t = (now - v.usedAt) / 0.8f;
                        v.crystal.localPosition += Vector3.up * (t * 8f);
                        v.go.transform.localScale = Vector3.one * Mathf.Max(0.01f, 1f + 0.3f * Mathf.Sin(Mathf.Min(t, 0.5f) * Mathf.PI) - Mathf.Max(0f, t - 0.5f) * 2f);
                        if (t >= 1f) { Object.Destroy(v.go); done.Add(kv.Key); }
                    }
                }
                foreach (var id in done) visuals.Remove(id);
            }
        }

        static void Clear()
        {
            shrines.Clear();
            trials.Clear();
            foreach (var v in visuals.Values) if (v.go) Object.Destroy(v.go);
            visuals.Clear();
        }

        /// <summary>Every player shows the host's shrines; only a host with the setting on places them.</summary>
        public static void Init(GameObject host, bool hostShrines)
        {
            NetworkManagerSystem.onStartClientGlobal += client => client.RegisterHandler(MsgType, OnMessage);
            SceneManager.activeSceneChanged += (a, b) => Clear();
            host.AddComponent<Client>();
            if (!hostShrines) return;
            host.AddComponent<Server>();
            SceneDirector.onPostPopulateSceneServer += Place;
        }
    }
}
