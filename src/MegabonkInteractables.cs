using System.Collections.Generic;
using System.Linq;
using RoR2;
using UnityEngine;
using UnityEngine.Rendering;

namespace RiskOfBonk
{
    /// <summary>
    /// Megabonk's chests, pots and shrines on Bonk style stages: Risk of Rain 2's chests wear Megabonk's Chest (and its
    /// open chest once bought), money barrels become Megabonk's pots, equipment barrels its barrel, printers its
    /// microwave, the Teleporter its boss portal arch, and the shrines its shrine statues (Chance: Balance, Blood: Greed, Combat: Challenge, Mountain: Cursed,
    /// Woods and Cleansing: Moai, Order: Balance). The Risk of Rain 2 object (its collider, price and interaction) stays;
    /// only its renderers are switched off, and its highlight outlines the Megabonk model. The Megabonk shrines
    /// (Shrines.cs) use the statues everywhere. Purely visual and local, so nothing is networked.
    /// </summary>
    public static class MegabonkInteractables
    {
        // Megabonk prefab names (with their scale on top of Megabonk's own).
        public const string Chest = "Chest", OpenChest = "OpenChest", PotSmall = "PotSmall", PotBig = "PotBig", Barrel = "Barrel", Microwave = "Microwave",
            BossSpawner = "BossSpawner";
        static readonly string[] ShrineModels = { "ChargeShrine", "MagnetShrine", "ShrineMaoi", "GreedShrine", "ChallengeShrine", "CursedShrine", "ShrineBalance", "ShrineGreed" };

        public static void Prepare(MegabonkLibrary lib)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var names = new[] { Chest, OpenChest, PotSmall, PotBig, Barrel, Microwave, BossSpawner }.Concat(ShrineModels);
            foreach (var n in names)
                if (!BonkTerrain.rawProps.ContainsKey(n))
                    lock (lib) BonkTerrain.rawProps[n] = lib.ReadProp(n);
            foreach (var n in names)
                if (BonkTerrain.rawProps.TryGetValue(n, out var p) && p != null) Clean(p.Model);
            if (TestKeys.On)
                foreach (var n in names)
                    if (BonkTerrain.rawProps.TryGetValue(n, out var p) && p != null)
                    {
                        var m = p.Model; float y0 = float.MaxValue, y1 = float.MinValue, x0 = float.MaxValue, x1 = float.MinValue;
                        foreach (var sm in m.SubMeshes) foreach (int k in sm.Triangles) { int i = k * 3; y0 = System.Math.Min(y0, m.Positions[i + 1]); y1 = System.Math.Max(y1, m.Positions[i + 1]); x0 = System.Math.Min(x0, m.Positions[i]); x1 = System.Math.Max(x1, m.Positions[i]); }
                        Log.Info($"  {n}: root scale {p.RootScale:0.###}, y {y0:0.##}..{y1:0.##} x {x0:0.##}..{x1:0.##}, submeshes: {string.Join(", ", m.SubMeshes.Select(sm => (sm.Texture?.Name ?? "no texture") + " " + string.Join("/", sm.Color.Select(c => c.ToString("0.##")))))}");
                    }
            Log.Info($"Megabonk chests, pots and shrines read in {sw.ElapsedMilliseconds} ms: " +
                     string.Join(", ", names.Select(n => BonkTerrain.rawProps.TryGetValue(n, out var p) && p != null ? $"{n} ({p.Model.VertexCount} verts)" : n + " missing")));
        }

        // Parts of the prefabs that are not the object: the encounter ring on the ground, the floating "!" and the chest's
        // shadow decal (Megabonk draws those separately), and the boss portal's 30 m skirt under the ground.
        static readonly HashSet<string> notTheObject = new HashSet<string> { "psd_Encounter", "icon_exclamation", "psd_Chest", "psd_ChestOpen", "BossPortalNormalBottom" };

        /// <summary>Drops the decals and markers, then stands what is left on the ground at the origin.</summary>
        static void Clean(RawModel m)
        {
            m.SubMeshes.RemoveAll(sm => sm.Texture != null && notTheObject.Contains(sm.Texture.Name));
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, z0 = float.MaxValue, z1 = float.MinValue;
            foreach (var sm in m.SubMeshes)
                foreach (int i in sm.Triangles)
                {
                    float x = m.Positions[i * 3], y = m.Positions[i * 3 + 1], z = m.Positions[i * 3 + 2];
                    if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (z < z0) z0 = z; if (z > z1) z1 = z;
                }
            if (y0 == float.MaxValue) return;
            float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
            for (int i = 0; i < m.Positions.Count; i += 3) { m.Positions[i] -= cx; m.Positions[i + 1] -= y0; m.Positions[i + 2] -= cz; }
        }

        /// <summary>Megabonk's model as a child of parent (feet at its origin), or null when it was not read.</summary>
        public static GameObject Model(string name, Transform parent, float scale)
        {
            if (!BonkTerrain.PropModel(name, out var mesh, out var mats)) return null;
            var go = new GameObject("Megabonk_" + name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.On;
            return go;
        }

        /// <summary>Which Megabonk model a Risk of Rain 2 interactable wears, by its object name, and how big.</summary>
        static string Pick(GameObject go, out float scale)
        {
            string n = go.name;
            scale = 1f;
            // The Teleporter: Megabonk's boss portal (a 13 m stone arch on a 25 m base), at 60%.
            if (go.GetComponent<TeleporterInteraction>()) { scale = 0.6f; return BossSpawner; }
            if (n.Contains("Lunar") || n.Contains("Void") || n.Contains("Scrapper")) return null;
            if (n.StartsWith("Barrel1") || go.GetComponent<BarrelInteraction>())
            {
                // Pots: mostly small ones, a big one now and then (fixed per spot, so every player sees the same).
                var p = go.transform.position;
                int h = Mathf.FloorToInt(p.x * 7f + p.z * 13f);
                return (h & 3) == 0 ? PotBig : PotSmall;
            }
            if (n.Contains("EquipmentBarrel")) return Barrel;
            if (n.Contains("Chest")) { scale = n.Contains("GoldChest") ? 1.6f : n.Contains("Chest2") ? 1.25f : 1f; return Chest; }
            if (n.Contains("Duplicator")) return Microwave;
            if (n.Contains("ShrineChance") || n.Contains("ShrineRestack")) return "ShrineBalance";
            if (n.Contains("ShrineBlood")) return "GreedShrine";
            if (n.Contains("ShrineCombat")) return "ChallengeShrine";
            if (n.Contains("ShrineBoss")) return "CursedShrine";
            if (n.Contains("ShrineHealing") || n.Contains("ShrineCleanse")) return "ShrineMaoi";
            return null;
        }

        /// <summary>The Megabonk look on one interactable: hides its own renderers and follows its state.</summary>
        sealed class Wearer : MonoBehaviour
        {
            public string model;
            public float scale = 1f;
            GameObject mb;
            PurchaseInteraction purchase;
            BarrelInteraction barrel;
            bool teleporter;
            List<Renderer> hidden = new List<Renderer>();
            bool opened;
            float nextHide;

            void Start()
            {
                purchase = GetComponent<PurchaseInteraction>();
                barrel = GetComponent<BarrelInteraction>();
                teleporter = GetComponent<TeleporterInteraction>();
                mb = Model(model, transform, scale);
                if (!mb) { Destroy(this); return; }
                // Megabonk models stand upright on the ground, whatever the stage tilted the interactable to.
                mb.transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                // The portal's stone platform has no collider: sink it so players stand on its top, not in it.
                if (teleporter) mb.transform.position -= Vector3.up * 1.4f * scale;
                Hide();
                var highlight = GetComponent<Highlight>();
                if (highlight) highlight.targetRenderer = mb.GetComponent<MeshRenderer>();
            }

            void Hide()
            {
                var root = GetComponent<ModelLocator>() is ModelLocator ml && ml.modelTransform ? ml.modelTransform : transform;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (mb && r.transform.IsChildOf(mb.transform)) continue;
                    if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                    if (r.GetComponent<TMPro.TMP_Text>() || r.GetComponentInParent<CostHologramContent>()) continue; // keep the price
                    if (teleporter && !TeleporterBody(r)) continue; // its charging range, beam and glow stay
                    if (!r.forceRenderingOff) { r.forceRenderingOff = true; hidden.Add(r); }
                }
            }

            // The Teleporter's own model parts (logged in a test run): base, prongs, beacon, crystals and its decal.
            static bool TeleporterBody(Renderer r)
            {
                string n = r.name;
                return n.StartsWith("Teleporter") || n.Contains("Prong") || n.Contains("Crystal") || n == "Decal" || n.Contains("BeaconBlocker");
            }

            void Update()
            {
                if (!mb) return;
                if (Time.time >= nextHide) { nextHide = Time.time + 1f; Hide(); } // parts swapped in later (opened lids)
                bool nowOpen = barrel ? barrel.Networkopened : purchase && !purchase.available && model == Chest;
                if (nowOpen == opened) return;
                opened = nowOpen;
                if (teleporter) return;
                if (barrel)
                {
                    // A pot breaks: Megabonk's pots shatter and are gone.
                    mb.SetActive(false);
                    return;
                }
                // A bought chest: Megabonk's open chest takes its place (when it was read).
                var open = Model(OpenChest, transform, scale);
                if (open)
                {
                    open.transform.rotation = mb.transform.rotation;
                    Destroy(mb);
                    mb = open;
                }
            }

            void OnDestroy()
            {
                if (mb) Destroy(mb);
                foreach (var r in hidden) if (r) r.forceRenderingOff = false;
            }
        }

        /// <summary>Puts the Megabonk looks on new interactables while a Bonk style stage is up.</summary>
        public sealed class Scanner : MonoBehaviour
        {
            float next;

            void Update()
            {
                if (Time.time < next) return;
                next = Time.time + 1f;
                if (!BonkTerrain.Active || !Run.instance) return;
                foreach (var pi in Object.FindObjectsOfType<PurchaseInteraction>()) Dress(pi.gameObject);
                foreach (var bi in Object.FindObjectsOfType<BarrelInteraction>()) Dress(bi.gameObject);
                if (TeleporterInteraction.instance) Dress(TeleporterInteraction.instance.gameObject);
            }

            static void Dress(GameObject go)
            {
                if (!go || go.GetComponent<Wearer>()) return;
                string model = Pick(go, out float scale);
                if (model == null) return;
                var w = go.AddComponent<Wearer>();
                w.model = model;
                w.scale = scale;
            }
        }
    }
}
