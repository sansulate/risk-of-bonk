using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RiskOfBonk.Generated;
using RoR2;
using RoR2.Projectile;
using UnityEngine;

namespace RiskOfBonk
{
    /// <summary>
    /// Megabonk's look and sound for Megabonk weapons. Each attack plays the weapon's Megabonk sound (mbSound) and its
    /// Megabonk effects (mbFx*, MegabonkFx) on every machine: muzzle flashes, bullets and arrows flying to the target,
    /// slashes, strikes, beams, auras and flame streams. A projectile fired by a Megabonk weapon shows the weapon's Megabonk
    /// model (mbProjectile: the bone, banana, axe, dice, rocket, flask, mine, dagger), spinning like in Megabonk, and its
    /// Megabonk trail or projectile effect, instead of the Risk of Rain 2 projectile's look, and its Megabonk hit effect
    /// where it ends. The projectile itself (flight, hits) stays the Risk of Rain 2 one, so nothing new is networked: every
    /// machine dresses its own copy of the projectile from the last weapon that body fired.
    /// </summary>
    public static class WeaponVisuals
    {
        sealed class Look { public Mesh mesh; public Material[] mats; }

        static readonly Dictionary<string, Look> looks = new Dictionary<string, Look>();
        static readonly ConditionalWeakTable<GameObject, WeaponRow> lastFired = new ConditionalWeakTable<GameObject, WeaponRow>();
        internal static Material Template;

        /// <summary>Called by WeaponFire on every machine when a body attacks with a weapon.</summary>
        public static void Fired(GameObject body, WeaponRow weapon, Vector3 at)
        {
            if (!body) return;
            lastFired.Remove(body);
            lastFired.Add(body, weapon);
            if (!MegabonkSounds.Play(weapon.MbSound, at) && !string.IsNullOrEmpty(weapon.Sound)) Util.PlaySound(weapon.Sound, body);
        }

        /// <summary>Whether this weapon's attack shows Megabonk's effect (so the Risk of Rain 2 tracer or swing is left out).</summary>
        public static bool HasShot(WeaponRow w) => MegabonkFx.Has(w.MbFxShot);

        /// <summary>
        /// Every machine, every shot: the weapon's Megabonk effects. aim is where the shot goes (towards the target when
        /// there is one), reach the attack's reach (melee/aura radius or range), size its size multiplier.
        /// </summary>
        public static void Attack(CharacterBody body, WeaponRow w, Ray aim, HurtBox target, float reach, float size)
        {
            if (!body || string.IsNullOrEmpty(w.MbFxMode)) return;
            float scale = w.MbFxScale * size;
            var dir = aim.direction.sqrMagnitude > 1e-6f ? aim.direction.normalized : body.transform.forward;
            var flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude < 1e-4f) flat = body.transform.forward;
            var look = Util.QuaternionSafeLookRotation(dir);
            var muzzle = body.corePosition + dir * Mathf.Max(0.5f, body.radius);
            Vector3 end = target ? target.transform.position : End(muzzle, dir, reach);
            if (!string.IsNullOrEmpty(w.MbFxMuzzle)) MegabonkFx.Spawn(w.MbFxMuzzle, muzzle, look, w.MbFxScale);
            switch (w.MbFxMode)
            {
                case "fly":
                    MegabonkFx.Fly(w.MbFxShot, muzzle, end, w.MbFxSpeed, scale, w.MbFxHit, w.MbFxScale);
                    break;
                case "burst":
                    MegabonkFx.Spawn(w.MbFxShot, muzzle, look, scale);
                    if (target) MegabonkFx.Spawn(w.MbFxHit, end, look, w.MbFxScale);
                    break;
                case "beam":
                    MegabonkFx.Beam(w.MbFxShot, muzzle, end, w.MbFxScale);
                    MegabonkFx.Spawn(w.MbFxHit, end, look, w.MbFxScale);
                    break;
                case "slash":
                    MegabonkFx.Spawn(w.MbFxShot, body.corePosition, Util.QuaternionSafeLookRotation(flat), scale);
                    break;
                case "strike":
                    MegabonkFx.Spawn(w.MbFxShot, Ground(end), Util.QuaternionSafeLookRotation(flat), scale);
                    break;
                case "aura":
                    // Kept on the body and sized so its ring matches the damage radius.
                    MegabonkFx.Keep(body.gameObject, w.MbFxShot, Vector3.up * 0.2f + body.transform.InverseTransformVector(body.footPosition - body.transform.position),
                        Quaternion.identity, reach / MegabonkFx.Radius(w.MbFxShot) / Mathf.Max(0.01f, body.transform.lossyScale.x));
                    break;
                case "stream":
                    MegabonkFx.Keep(body.gameObject, w.MbFxShot, body.transform.InverseTransformPoint(muzzle), look, scale / Mathf.Max(0.01f, body.transform.lossyScale.x));
                    break;
            }
        }

        static Vector3 End(Vector3 from, Vector3 dir, float range)
        {
            range = Mathf.Clamp(range, 3f, 120f);
            return Physics.Raycast(from, dir, out var hit, range, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) ? hit.point : from + dir * range;
        }

        static Vector3 Ground(Vector3 p) =>
            Physics.Raycast(p + Vector3.up, Vector3.down, out var hit, 6f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) ? hit.point : p;

        /// <summary>Builds the Megabonk projectile models and reads the effects while the content loads (Megabonk's files are open then).</summary>
        public static void Prepare(MegabonkLibrary lib)
        {
            foreach (var w in Sheets.Weapons)
            {
                if (string.IsNullOrEmpty(w.MbProjectile) || looks.ContainsKey(w.MbProjectile)) continue;
                RawModel raw;
                lock (lib) raw = lib.FindModel(new[] { w.MbProjectile });
                if (raw == null || raw.VertexCount == 0) { Log.Warn($"Megabonk projectile model {w.MbProjectile} not found"); continue; }
                var mesh = UnityConvert.Mesh(raw);
                var mats = new Material[raw.SubMeshes.Count];
                for (int i = 0; i < mats.Length; i++)
                {
                    var sm = raw.SubMeshes[i];
                    var m = Template ? new Material(Template) : new Material(Shader.Find("Standard"));
                    m.name = "MB_" + w.MbProjectile;
                    if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", UnityConvert.Texture(sm.Texture, lib));
                    if (m.HasProperty("_Color")) m.SetColor("_Color", new Color(sm.Color[0], sm.Color[1], sm.Color[2], 1f));
                    foreach (var p in new[] { "_NormalTex", "_BumpMap", "_EmTex", "_FresnelRamp", "_FlowHeightmap" })
                        if (m.HasProperty(p)) m.SetTexture(p, null);
                    if (m.HasProperty("_EmPower")) m.SetFloat("_EmPower", 0f);
                    mats[i] = m;
                }
                looks[w.MbProjectile] = new Look { mesh = mesh, mats = mats };
            }
            Log.Info($"Megabonk projectile models ready: {string.Join(", ", looks.Keys)}");
            MegabonkFx.Prepare(lib, Sheets.Weapons.SelectMany(w => new[] { w.MbFxMuzzle, w.MbFxShot, w.MbFxHit }));
        }

        static void Dress(GameObject ghost, WeaponRow w)
        {
            // A pooled ghost may still wear an earlier look.
            foreach (Transform c in ghost.transform)
                if (c.name == "MB_Projectile" || c.name.StartsWith("MB_Fx")) Object.Destroy(c.gameObject);
            bool model = looks.TryGetValue(w.MbProjectile ?? "", out var look);
            bool fx = w.MbFxMode == "ghost" && MegabonkFx.Has(w.MbFxShot);
            // Dragon's Breath's fireballs only carry its damage: the flame stream on the body is the look.
            if (!model && !fx && w.MbFxMode != "stream" && !(w.MbFxMode == "ghost" && MegabonkFx.Has(w.MbFxHit))) return;
            foreach (var r in ghost.GetComponentsInChildren<Renderer>(true))
                if (!(r is TrailRenderer)) r.enabled = false;
            if (model)
            {
                var go = new GameObject("MB_Projectile");
                go.transform.SetParent(ghost.transform, false);
                go.layer = ghost.layer;
                var spin = go.AddComponent<Spin>();
                spin.degreesPerSecond = w.ProjSpin;
                var body = new GameObject("Model");
                body.transform.SetParent(go.transform, false);
                body.layer = ghost.layer;
                body.transform.localScale = Vector3.one * w.ProjScale;
                body.transform.localPosition = -look.mesh.bounds.center * w.ProjScale;
                body.AddComponent<MeshFilter>().sharedMesh = look.mesh;
                var mr = body.AddComponent<MeshRenderer>();
                mr.sharedMaterials = look.mats;
            }
            if (fx)
            {
                var t = MegabonkFx.Template(w.MbFxShot);
                var go = Object.Instantiate(t, ghost.transform);
                go.name = "MB_Fx";
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = t.transform.localRotation;
                go.transform.localScale = t.transform.localScale * w.MbFxScale;
                go.SetActive(true);
            }
            if (MegabonkFx.Has(w.MbFxHit))
            {
                var hit = new GameObject("MB_FxHit").AddComponent<HitOnEnd>();
                hit.transform.SetParent(ghost.transform, false);
                hit.name0 = w.MbFxHit;
                hit.scale = w.MbFxScale;
            }
        }

        /// <summary>Plays the weapon's Megabonk hit effect where its projectile ends.</summary>
        sealed class HitOnEnd : MonoBehaviour
        {
            public string name0;
            public float scale;
            void OnDisable()
            {
                if (!Run.instance || !gameObject.scene.isLoaded || string.IsNullOrEmpty(name0)) return;
                MegabonkFx.Spawn(name0, transform.position, Quaternion.identity, scale);
                name0 = null;
            }
        }

        sealed class Spin : MonoBehaviour
        {
            public float degreesPerSecond;
            void Update() { if (degreesPerSecond != 0f) transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f, Space.Self); }
        }

        [HarmonyPatch]
        static class Patches
        {
            [HarmonyPostfix, HarmonyPatch(typeof(ProjectileController), "Start")]
            static void Ghost(ProjectileController __instance)
            {
                if (!__instance || !__instance.ghost || !__instance.owner) return;
                if (!lastFired.TryGetValue(__instance.owner, out var w)) return;
                var prefab = WeaponFire.Prefab(w.Prefab);
                // Only the weapon's own projectile (not, say, an item's missile from the same body).
                if (!prefab || __instance.gameObject.name.Replace("(Clone)", "").Trim() != prefab.name) return;
                try { Dress(__instance.ghost.gameObject, w); } catch (System.Exception e) { Log.Warn($"projectile look {w.Id}: {e.Message}"); }
            }
        }

        public static void Init(Harmony harmony, GameObject host)
        {
            harmony.PatchAll(typeof(Patches));
            host.AddComponent<MegabonkFx.Scroller>();
        }
    }
}
