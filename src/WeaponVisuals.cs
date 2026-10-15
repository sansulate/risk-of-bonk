using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RiskOfBonk.Generated;
using RoR2;
using RoR2.Projectile;
using UnityEngine;

namespace RiskOfBonk
{
    /// <summary>
    /// Megabonk's look and sound for Megabonk weapons. Each attack plays the weapon's Megabonk sound (mbSound) on every
    /// machine, and a projectile fired by a Megabonk weapon shows the weapon's Megabonk model (mbProjectile: the bone,
    /// banana, axe, dice, rocket, flask, mine, dagger) instead of the Risk of Rain 2 projectile's look, spinning like in
    /// Megabonk. The projectile itself (flight, hits) stays the Risk of Rain 2 one, so nothing new is networked: every
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

        /// <summary>Builds the Megabonk projectile models while the content loads (Megabonk's files are open then).</summary>
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
        }

        static void Dress(GameObject ghost, WeaponRow w)
        {
            if (!looks.TryGetValue(w.MbProjectile, out var look)) return;
            foreach (var r in ghost.GetComponentsInChildren<Renderer>(true))
                if (!(r is TrailRenderer)) r.enabled = false;
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
                if (!lastFired.TryGetValue(__instance.owner, out var w) || string.IsNullOrEmpty(w.MbProjectile)) return;
                var prefab = WeaponFire.Prefab(w.Prefab);
                // Only the weapon's own projectile (not, say, an item's missile from the same body).
                if (!prefab || __instance.gameObject.name.Replace("(Clone)", "").Trim() != prefab.name) return;
                try { Dress(__instance.ghost.gameObject, w); } catch (System.Exception e) { Log.Warn($"projectile look {w.Id}: {e.Message}"); }
            }
        }

        public static void Init(Harmony harmony) => harmony.PatchAll(typeof(Patches));
    }
}
