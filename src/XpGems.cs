using System.Collections.Generic;
using HarmonyLib;
using RiskOfBonk.Generated;
using RoR2;
using RoR2.Networking;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace RiskOfBonk
{
    /// <summary>
    /// Megabonk XP: kills drop XP gems on the ground instead of Risk of Rain 2's flying orbs. A gem flies to a player who
    /// comes within tuning.xp_pickup_radius and then counts for the team's level (Risk of Rain 2 shares the level in
    /// co-op, so everyone gets the level-up pick). The host owns the gems; every player sees them through messages
    /// (spawn, collect, remove) on the game's own connection.
    /// </summary>
    public static class XpGems
    {
        public const short MsgType = 0x5B0E;
        const byte OpSpawn = 0, OpCollect = 1, OpRemove = 2;
        const float FlyTime = 0.35f;

        /// <summary>Raised on every machine when a gem reaches its player (for the XP bar's pulse).</summary>
        public static event System.Action<ulong> Collected;

        sealed class GemMessage : MessageBase
        {
            public byte op;
            public uint id;
            public Vector3 pos;
            public uint amount;
            public NetworkInstanceId target;
            public override void Serialize(NetworkWriter w)
            {
                w.Write(op); w.WritePackedUInt32(id);
                if (op == OpSpawn) { w.Write(pos); w.WritePackedUInt32(amount); }
                if (op == OpCollect) w.Write(target);
            }
            public override void Deserialize(NetworkReader r)
            {
                op = r.ReadByte(); id = r.ReadPackedUInt32();
                if (op == OpSpawn) { pos = r.ReadVector3(); amount = r.ReadPackedUInt32(); }
                if (op == OpCollect) target = r.ReadNetworkId();
            }
        }

        // ---------------- host ----------------

        sealed class Gem { public uint id; public Vector3 pos; public ulong amount; public float flyStart = -1f; public CharacterBody collector; }
        static readonly List<Gem> gems = new List<Gem>();
        static uint nextId = 1;

        static void Send(GemMessage m) { if (NetworkServer.active) NetworkServer.SendToAll(MsgType, m); }

        static void Spawn(Vector3 origin, ulong amount)
        {
            if (amount == 0) return;
            // Megabonk gems lie on the ground.
            if (Physics.Raycast(origin + Vector3.up, Vector3.down, out var hit, 300f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                origin = hit.point + Vector3.up * 0.6f;
            origin += new Vector3(Random.Range(-0.8f, 0.8f), 0f, Random.Range(-0.8f, 0.8f));
            if (gems.Count >= (int)Tune.XpGemLimit)
            {
                var old = gems[0];
                gems.RemoveAt(0);
                if (TeamManager.instance) TeamManager.instance.GiveTeamExperience(TeamIndex.Player, old.amount);
                Send(new GemMessage { op = OpRemove, id = old.id });
            }
            var g = new Gem { id = nextId++, pos = origin, amount = amount };
            gems.Add(g);
            Send(new GemMessage { op = OpSpawn, id = g.id, pos = g.pos, amount = (uint)Mathf.Min(amount, uint.MaxValue) });
        }

        /// <summary>Host: every gem on the map flies to this player (Megabonk's magnet).</summary>
        public static void MagnetTo(CharacterBody body)
        {
            if (!NetworkServer.active || !body) return;
            float now = Time.fixedTime;
            int n = 0;
            foreach (var g in gems)
            {
                if (g.flyStart >= 0f) continue;
                n++;
                g.flyStart = now;
                Send(new GemMessage { op = OpCollect, id = g.id, target = body.netId });
            }
            Log.Info($"magnet: {n} XP gems pulled");
        }

        /// <summary>Host tick: gems near a player fly to them and count once they arrive.</summary>
        public sealed class Server : MonoBehaviour
        {
            float nextScan;

            void FixedUpdate()
            {
                if (!NetworkServer.active || gems.Count == 0) return;
                float now = Time.fixedTime;
                for (int i = gems.Count - 1; i >= 0; i--)
                {
                    var g = gems[i];
                    if (g.flyStart >= 0f && now - g.flyStart >= FlyTime)
                    {
                        gems.RemoveAt(i);
                        // Megabonk's XP gain stat (Spaceman, Quantum XP Theory) counts for the gems that player collects.
                        ulong amount = (ulong)Mathf.Max(1f, Mathf.Round(g.amount * Passives.XpMultiplier(g.collector)));
                        if (TeamManager.instance) TeamManager.instance.GiveTeamExperience(TeamIndex.Player, amount);
                    }
                }
                if (now < nextScan) return;
                nextScan = now + 0.1f;
                foreach (var pc in PlayerCharacterMasterController.instances)
                {
                    var body = pc && pc.master ? pc.master.GetBody() : null;
                    if (!body || !body.healthComponent || !body.healthComponent.alive) continue;
                    // Megabonk characters' PickupRange stat (Fox +4, Bush +6, ...) adds metres to the radius.
                    float r = Tune.XpPickupRadius + Passives.PickupRange(body);
                    float r2 = r * r;
                    var p = body.corePosition;
                    foreach (var g in gems)
                    {
                        if (g.flyStart >= 0f || (g.pos - p).sqrMagnitude > r2) continue;
                        g.flyStart = now;
                        g.collector = body;
                        Send(new GemMessage { op = OpCollect, id = g.id, target = body.netId });
                    }
                }
            }
        }

        // ---------------- every machine: visuals ----------------

        sealed class Visual
        {
            public GameObject go;
            public Transform target;
            public Vector3 from;
            public float flyStart = -1f, phase;
            public ulong amount;
        }

        static readonly Dictionary<uint, Visual> visuals = new Dictionary<uint, Visual>();
        static Mesh gemMesh;
        static readonly Dictionary<int, Material> mats = new Dictionary<int, Material>();

        public static Mesh GemMesh()
        {
            if (gemMesh != null) return gemMesh;
            // An octahedron, like Megabonk's XP crystals.
            var v = new[] { new Vector3(0, 0.45f, 0), new Vector3(0, -0.45f, 0), new Vector3(0.28f, 0, 0), new Vector3(-0.28f, 0, 0), new Vector3(0, 0, 0.28f), new Vector3(0, 0, -0.28f) };
            var t = new[] { 0, 4, 2, 0, 2, 5, 0, 5, 3, 0, 3, 4, 1, 2, 4, 1, 5, 2, 1, 3, 5, 1, 4, 3 };
            var verts = new Vector3[t.Length];
            var tris = new int[t.Length];
            for (int i = 0; i < t.Length; i++) { verts[i] = v[t[i]]; tris[i] = i; }
            gemMesh = new Mesh { vertices = verts, triangles = tris };
            gemMesh.RecalculateNormals();
            gemMesh.RecalculateBounds();
            return gemMesh;
        }

        static int Tier(ulong amount) => amount < 8 ? 0 : amount < 40 ? 1 : 2;
        static readonly Color[] tierColors = { new Color(0.25f, 0.65f, 1f), new Color(0.3f, 1f, 0.35f), new Color(1f, 0.25f, 0.35f) };

        static Material Mat(int tier)
        {
            if (mats.TryGetValue(tier, out var m) && m) return m;
            var shader = Addressables.LoadAssetAsync<Shader>("RoR2/Base/Shaders/HGStandard.shader").WaitForCompletion();
            m = new Material(shader);
            var c = tierColors[tier];
            m.SetColor("_Color", c);
            if (m.HasProperty("_EmColor")) m.SetColor("_EmColor", c);
            if (m.HasProperty("_EmPower")) m.SetFloat("_EmPower", 2.5f);
            mats[tier] = m;
            return m;
        }

        static void OnMessage(NetworkMessage msg)
        {
            var m = msg.ReadMessage<GemMessage>();
            switch (m.op)
            {
                case OpSpawn:
                {
                    var go = new GameObject("RiskOfBonkXpGem");
                    go.transform.position = m.pos;
                    int tier = Tier(m.amount);
                    go.transform.localScale = Vector3.one * (0.8f + 0.35f * tier);
                    go.AddComponent<MeshFilter>().sharedMesh = GemMesh();
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = Mat(tier);
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    visuals[m.id] = new Visual { go = go, from = m.pos, phase = Random.value * 6f, amount = m.amount };
                    break;
                }
                case OpCollect:
                    if (visuals.TryGetValue(m.id, out var v))
                    {
                        var target = ClientScene.FindLocalObject(m.target);
                        v.target = target ? target.transform : null;
                        v.from = v.go ? v.go.transform.position : Vector3.zero;
                        v.flyStart = Time.time;
                    }
                    break;
                case OpRemove:
                    if (visuals.TryGetValue(m.id, out var rv)) { if (rv.go) Object.Destroy(rv.go); visuals.Remove(m.id); }
                    break;
            }
        }

        /// <summary>Spins and bobs the gems, flies collected ones to their player.</summary>
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
                    var tr = v.go.transform;
                    tr.Rotate(0f, 120f * Time.deltaTime, 0f, Space.World);
                    if (v.flyStart < 0f)
                    {
                        tr.position = v.from + Vector3.up * (0.15f * Mathf.Sin(now * 3f + v.phase));
                        continue;
                    }
                    float t = Mathf.Clamp01((now - v.flyStart) / FlyTime);
                    var end = v.target ? v.target.position + Vector3.up : v.from;
                    // A little hop up, then into the player.
                    tr.position = Vector3.Lerp(v.from, end, t * t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 1.5f);
                    tr.localScale *= 0.98f;
                    if (t >= 1f) { Object.Destroy(v.go); done.Add(kv.Key); Collected?.Invoke(v.amount); }
                }
                foreach (var id in done) visuals.Remove(id);
            }
        }

        static void Clear()
        {
            gems.Clear();
            foreach (var v in visuals.Values) if (v.go) Object.Destroy(v.go);
            visuals.Clear();
        }

        /// <summary>Every player shows the host's gems; only a host with the setting on turns kill XP into gems.</summary>
        public static void Init(Harmony harmony, GameObject host, bool hostGems)
        {
            NetworkManagerSystem.onStartClientGlobal += client => client.RegisterHandler(MsgType, OnMessage);
            SceneManager.activeSceneChanged += (a, b) => Clear();
            host.AddComponent<Client>();
            if (!hostGems) return;
            host.AddComponent<Server>();
            harmony.PatchAll(typeof(Patches));
        }

        [HarmonyPatch]
        static class Patches
        {
            // Kill XP for players becomes a gem where the monster died.
            [HarmonyPrefix, HarmonyPatch(typeof(ExperienceManager), nameof(ExperienceManager.AwardExperience))]
            static bool Award(Vector3 origin, CharacterBody body, ulong amount)
            {
                if (!NetworkServer.active || !body || !body.master || body.master.teamIndex != TeamIndex.Player) return true;
                Spawn(origin, (ulong)Mathf.Max(1f, amount * Tune.XpMultiplier));
                return false;
            }
        }
    }
}
