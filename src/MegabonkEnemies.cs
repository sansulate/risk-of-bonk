using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;

namespace RiskOfBonk
{
    /// <summary>
    /// Megabonk's enemies on Bonk style stages: every regular monster (not bosses) wears a Megabonk enemy from the map's
    /// pool (Forest: goblins, slimes, golems, boomer spiders, bees; Desert: mummies, scorpions, tumblebones, dusty skeletons,
    /// wisps; Graveyard: skeletons, zombies, ghouls, ghosts, heads),
    /// light monsters a light enemy, heavy ones a heavy one, flyers a flyer. Megabonk's enemies are baked vertex
    /// animations (AnimatedMeshScriptableObject: one mesh per frame), so the look is exactly Megabonk's: its walk frames
    /// play at its AnimationFPS, faster or slower with the monster's speed. The Risk of Rain 2 monster (its AI, attacks
    /// and hitbox) stays underneath, only its renderers are switched off. Every machine picks the same enemy from the
    /// monster's name and the stage, so nothing is networked.
    /// </summary>
    public static class MegabonkEnemies
    {
        sealed class Pool { public string[] light, medium, heavy, flying; }

        static readonly Pool ForestPool = new Pool
        {
            light = new[] { "GoblinWeak", "Slime" },
            medium = new[] { "GoblinStrong", "Golem", "BoomerSpider" },
            heavy = new[] { "GoblinTank" },
            flying = new[] { "Bee" },
        };

        static readonly Pool GraveyardPool = new Pool
        {
            light = new[] { "Skeleton", "Zombie", "Ghoul" },
            medium = new[] { "SkeletonArmored" },
            heavy = new[] { "SkeletonArmored" },
            flying = new[] { "Ghost", "GhostPurple", "Head" },
        };

        static readonly Pool DesertPool = new Pool
        {
            light = new[] { "Mummy", "Scorpion", "Tumblebone" },
            medium = new[] { "SkeletonDusty", "ScorpionBig", "MummyTank" },
            heavy = new[] { "MummyAncient", "SkeletonArmoredDusty" },
            flying = new[] { "Wisp" },
        };

        static Dictionary<string, RawEnemy> raws = new Dictionary<string, RawEnemy>();

        /// <summary>Reads the enemies (Megabonk's files are open while the content loads).</summary>
        public static void Prepare(MegabonkLibrary lib)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var names = new HashSet<string>(new[] { ForestPool, GraveyardPool, DesertPool }.SelectMany(p => p.light.Concat(p.medium).Concat(p.heavy).Concat(p.flying)));
            lock (lib) raws = lib.ReadEnemies(names);
            Log.Info($"Megabonk enemies read in {sw.ElapsedMilliseconds} ms: {string.Join(", ", raws.Select(r => $"{r.Key} ({r.Value?.Frames.Count ?? 0} frames)"))}"
                     + (raws.Count < names.Count ? "; missing: " + string.Join(", ", names.Where(n => !raws.ContainsKey(n))) : ""));
        }

        sealed class Look { public Mesh[] frames; public Material material; public RawEnemy raw; public float height; }
        static readonly Dictionary<string, Look> looks = new Dictionary<string, Look>();
        static Shader standard;

        static Look LookOf(string name)
        {
            if (looks.TryGetValue(name, out var l)) return l;
            if (raws.TryGetValue(name, out var raw) && raw != null && raw.Frames.Count > 0)
            {
                if (!standard) standard = Addressables.LoadAssetAsync<Shader>("RoR2/Base/Shaders/HGStandard.shader").WaitForCompletion();
                var mat = new Material(standard) { name = "MBEnemy_" + name };
                var tex = UnityConvert.Texture(raw.Texture);
                if (tex) { tex.filterMode = raw.Texture.Filter == 0 ? FilterMode.Point : FilterMode.Bilinear; mat.SetTexture("_MainTex", tex); }
                mat.SetColor("_Color", new Color(raw.Color[0], raw.Color[1], raw.Color[2], 1f));
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
                var frames = raw.Frames.Select(UnityConvert.Mesh).ToArray();
                l = new Look { frames = frames, material = mat, raw = raw, height = frames[0].bounds.size.y };
            }
            looks[name] = l;
            return l;
        }

        static string Pick(CharacterBody body)
        {
            string map = BonkTerrain.MapName ?? "";
            var pool = map == "Graveyard" ? GraveyardPool : map.StartsWith("Desert") ? DesertPool : ForestPool;
            string[] options;
            if (body.isFlying) options = pool.flying;
            else if (body.baseMaxHealth < 150f) options = pool.light;
            else if (body.baseMaxHealth < 700f) options = pool.medium;
            else options = pool.heavy;
            // The same on every machine: from the monster's name and the stage.
            string n = body.name.Replace("(Clone)", "");
            int h = Run.instance ? Run.instance.stageClearCount * 31 : 0;
            foreach (char c in n) h = h * 31 + c;
            return options[(h & 0x7fffffff) % options.Length];
        }

        /// <summary>The Megabonk look on one monster: its walk frames, following the monster's feet and facing.</summary>
        sealed class Wearer : MonoBehaviour
        {
            public Look look;
            CharacterBody body;
            Transform model, mb;
            MeshFilter filter;
            float frame, dead = -1f, baseScale;

            void Start()
            {
                body = GetComponent<CharacterBody>();
                model = body.modelLocator ? body.modelLocator.modelTransform : null;
                Hide();
                var go = new GameObject("MegabonkEnemy");
                mb = go.transform;
                filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = look.frames[0];
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = look.material;
                mr.shadowCastingMode = ShadowCastingMode.On;
                if (body.isElite)
                {
                    // Elites keep a tint of their elite colour.
                    var eq = body.inventory ? EquipmentCatalog.GetEquipmentDef(body.inventory.currentEquipmentIndex) : null;
                    var elite = eq ? eq.passiveBuffDef?.eliteDef : null;
                    if (elite)
                    {
                        var block = new MaterialPropertyBlock();
                        block.SetColor("_Color", Color.Lerp(Color.white, elite.color, 0.6f));
                        mr.SetPropertyBlock(block);
                    }
                }
                // Megabonk's own size, grown or shrunk a little towards the Risk of Rain 2 monster's (its hitbox).
                float rorHeight = body.mainHurtBox && body.mainHurtBox.collider ? body.mainHurtBox.collider.bounds.size.y : body.radius * 2f;
                float mbHeight = Mathf.Max(0.1f, look.height * look.raw.Scale);
                baseScale = look.raw.Scale * Mathf.Clamp(rorHeight / mbHeight, 0.85f, 2f);
                frame = Random.value * look.frames.Length;
            }

            void Hide()
            {
                if (!model) return;
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                    if (!(r is ParticleSystemRenderer) && !(r is TrailRenderer) && !(r is LineRenderer)) r.forceRenderingOff = true;
            }

            void LateUpdate()
            {
                if (!mb) return;
                if (!body || !body.healthComponent) { Destroy(mb.gameObject); Destroy(this); return; }
                // Megabonk enemies shrink away when they die.
                if (!body.healthComponent.alive && dead < 0f) dead = Time.time;
                float shrink = dead < 0f ? 1f : Mathf.Clamp01(1f - (Time.time - dead) / 0.3f);
                if (shrink <= 0f) { mb.gameObject.SetActive(false); return; }
                var feet = body.footPosition;
                var fwd = body.characterDirection ? body.characterDirection.forward : model ? model.forward : transform.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
                var rot = Quaternion.LookRotation(fwd) * Quaternion.Euler(look.raw.Rotation[0], look.raw.Rotation[1], look.raw.Rotation[2]);
                float s = baseScale * shrink;
                mb.SetPositionAndRotation(feet + rot * new Vector3(look.raw.Offset[0], look.raw.Offset[1], look.raw.Offset[2]) * s, rot);
                mb.localScale = Vector3.one * s;
                // Walk frames at Megabonk's rate, scaled by how fast the monster goes compared with the Megabonk enemy.
                float speed = body.characterMotor ? new Vector2(body.characterMotor.velocity.x, body.characterMotor.velocity.z).magnitude
                    : body.rigidbody ? body.rigidbody.velocity.magnitude : 0f;
                float rate = look.raw.Flying ? 1f : Mathf.Clamp(speed / Mathf.Max(1f, look.raw.Speed), 0f, 2f);
                frame = (frame + Time.deltaTime * look.raw.Fps * rate) % look.frames.Length;
                filter.sharedMesh = look.frames[(int)frame];
                if (Time.frameCount % 30 == 0) Hide(); // models swapped in later (elite effects, death states) stay hidden too
            }

            void OnDestroy() { if (mb) Destroy(mb.gameObject); }
        }

        [HarmonyPatch]
        static class Patches
        {
            [HarmonyPostfix, HarmonyPatch(typeof(CharacterBody), "Start")]
            static void Spawned(CharacterBody __instance)
            {
                var b = __instance;
                if (!BonkTerrain.Active || !b || b.isPlayerControlled || b.isChampion || b.isBoss) return;
                if (!b.teamComponent || b.teamComponent.teamIndex != TeamIndex.Monster || b.name.StartsWith("MB")) return;
                try
                {
                    var look = LookOf(Pick(b));
                    if (look == null) return;
                    b.gameObject.AddComponent<Wearer>().look = look;
                }
                catch (System.Exception e) { Log.Warn($"Megabonk enemy look on {b.name}: {e.Message}"); }
            }
        }

        /// <summary>Risk of Rain 2 monsters that fight from range (bodies); on Bonk style stages they spawn less often.</summary>
        static readonly HashSet<string> Ranged = new HashSet<string>
        {
            "WispBody", "GreaterWispBody", "ClayBruiserBody", "ClayGrenadierBody", "VultureBody", "RoboBallMiniBody", "LunarGolemBody",
            "LunarWispBody", "BellBody", "HermitCrabBody", "FlyingVerminBody", "VoidJailerBody", "NullifierBody", "VoidBarnacleBody",
            "ChildBody", "ScorchlingBody", "MinorConstructBody",
        };

        static void Weigh(ClassicStageInfo info)
        {
            var sel = info ? info.monsterSelection : null;
            if (sel == null || !BonkTerrain.Active && !StageMode.IsBonk) return;
            var lowered = new List<string>();
            for (int i = 0; i < sel.Count; i++)
            {
                var choice = sel.GetChoice(i);
                var master = choice.value?.spawnCard ? choice.value.spawnCard.prefab : null;
                var body = master && master.GetComponent<CharacterMaster>() is CharacterMaster cm && cm.bodyPrefab ? cm.bodyPrefab.name : null;
                if (body == null || !Ranged.Contains(body)) continue;
                sel.ModifyChoiceWeight(i, choice.weight * Generated.Tune.BonkRangedWeight);
                lowered.Add(body);
            }
            if (lowered.Count > 0) Log.Info($"Bonk style: ranged monsters spawn less often ({string.Join(", ", lowered)})");
        }

        [HarmonyPatch]
        static class SpawnPatches
        {
            [HarmonyPostfix, HarmonyPatch(typeof(ClassicStageInfo), nameof(ClassicStageInfo.RebuildCards))]
            static void Rebuilt(ClassicStageInfo __instance) => Weigh(__instance);
        }

        public static void Init(Harmony harmony)
        {
            harmony.PatchAll(typeof(Patches));
            harmony.PatchAll(typeof(SpawnPatches));
        }
    }
}
