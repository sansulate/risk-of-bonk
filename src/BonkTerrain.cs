using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using RoR2;
using RoR2.Navigation;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace RiskOfBonk
{
    /// <summary>
    /// "Bonk style": Megabonk-like random terrain on Risk of Rain 2's normal stages. When the host picked Bonk style, every
    /// player builds the same map from the run's synced unique id and the stage number: rolling low-poly hills with
    /// terraces and walls around the edge, flat-shaded in a palette that fits the stage (grass, snow, sand, dusk), with
    /// low-poly trees and rocks. The stage's own ground is hidden (its sky, fog and light stay), its fixed interactables
    /// are set down on the new ground, and the host bakes new ground and air node graphs so the Teleporter, chests,
    /// shrines and monsters all use the new map.
    /// </summary>
    public static class BonkTerrain
    {
        public static bool Active { get; private set; }
        public static Vector3 Center { get; private set; }

        const float Size = 380f;          // whole map, walls included
        const int Cells = 126;            // 3 m cells
        const float Playable = 0.8f;      // fraction of the half size inside the walls
        static readonly int WorldLayer = LayerIndex.world.intVal;

        static float half => Size / 2f;
        static float seedX, seedZ;

        // ---------------- height ----------------

        static float Fbm(float x, float z)
        {
            float sum = 0f, amp = 1f, freq = 1f, norm = 0f;
            for (int o = 0; o < 4; o++)
            {
                sum += amp * Mathf.PerlinNoise(seedX + x * freq, seedZ + z * freq);
                norm += amp;
                amp *= 0.5f;
                freq *= 2.03f;
            }
            return sum / norm;
        }

        /// <summary>Height above the map's base at local (x, z), centre = (0, 0).</summary>
        public static float Height(float x, float z)
        {
            float n = Fbm(x * 0.0045f, z * 0.0045f);
            float h = (n - 0.45f) * 42f;
            // Megabonk's maps have flat tops and steps between them.
            h = Mathf.Lerp(h, Mathf.Round(h / 7f) * 7f, 0.55f);
            // A flat clearing where the players start.
            float c = Mathf.Sqrt(x * x + z * z);
            h *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(14f, 40f, c));
            // Walls round the edge.
            float d = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) / half;
            h += Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Playable + 0.02f, 0.97f, d)) * 75f;
            return h;
        }

        // ---------------- palette ----------------

        // 0 grass light, 1 grass dark, 2 dirt, 3 rock, 4 rock dark, 5 wall top, 6 trunk, 7 leaves
        static Color32[] Palette(string scene)
        {
            Color32 C(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }
            scene = scene ?? "";
            if (scene.Contains("snow") || scene.Contains("frozen"))
                return new[] { C("#e8f1ff"), C("#c9daf2"), C("#9fb3cf"), C("#7b8ba8"), C("#5d6a85"), C("#ffffff"), C("#5a4636"), C("#2f6b5a") };
            if (scene.Contains("goolake") || scene.Contains("desert") || scene.Contains("ancientloft"))
                return new[] { C("#f2d58a"), C("#e3bd6a"), C("#c99a52"), C("#b07a45"), C("#8a5a35"), C("#f7e3b0"), C("#6b4a2e"), C("#7fa34a") };
            if (scene.Contains("wispgraveyard") || scene.Contains("shipgraveyard") || scene.Contains("rootjungle"))
                return new[] { C("#e39a3c"), C("#c9782c"), C("#8f5a33"), C("#77685e"), C("#57493f"), C("#f2c46b"), C("#4d3526"), C("#d9572b") };
            if (scene.Contains("dampcave") || scene.Contains("skymeadow") || scene.Contains("sulfur") || scene.Contains("void"))
                return new[] { C("#6f6ad1"), C("#5650b0"), C("#45407f"), C("#3a3a5c"), C("#2a2a44"), C("#a59cff"), C("#3b2e4f"), C("#b45ad1") };
            return new[] { C("#7bd34a"), C("#5db63a"), C("#9a7448"), C("#8d8f99"), C("#6c6e78"), C("#c9ef8e"), C("#6b4a2e"), C("#3f9e3a") };
        }

        static float[,] heights;
        static readonly List<Vector2> treeSpots = new List<Vector2>();

        /// <summary>The terrain mesh's height at local (x, z), from its grid (fast, matches the mesh).</summary>
        static float GridHeight(float x, float z)
        {
            float cell = Size / Cells;
            float fx = Mathf.Clamp((x + half) / cell, 0f, Cells - 0.001f), fz = Mathf.Clamp((z + half) / cell, 0f, Cells - 0.001f);
            int i = (int)fx, j = (int)fz;
            float tx = fx - i, tz = fz - j;
            return Mathf.Lerp(Mathf.Lerp(heights[i, j], heights[i + 1, j], tx), Mathf.Lerp(heights[i, j + 1], heights[i + 1, j + 1], tx), tz);
        }

        static Material material;
        static Texture2D paletteTex;

        static Material Mat(Color32[] palette)
        {
            if (!paletteTex)
            {
                paletteTex = new Texture2D(8, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                material = new Material(Addressables.LoadAssetAsync<Shader>("RoR2/Base/Shaders/HGStandard.shader").WaitForCompletion());
                material.SetTexture("_MainTex", paletteTex);
                material.SetColor("_Color", Color.white);
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
            }
            paletteTex.SetPixels32(palette);
            paletteTex.Apply();
            return material;
        }

        static Vector2 Uv(int index) => new Vector2((index + 0.5f) / 8f, 0.5f);

        // ---------------- meshes ----------------

        /// <summary>Flat-shaded triangles; each gets its palette colour from its height and steepness.</summary>
        static Mesh TerrainMesh()
        {
            float cell = Size / Cells;
            var h = heights = new float[Cells + 1, Cells + 1];
            for (int i = 0; i <= Cells; i++)
                for (int j = 0; j <= Cells; j++)
                    h[i, j] = Height(-half + i * cell, -half + j * cell);
            var verts = new List<Vector3>(Cells * Cells * 6);
            var uvs = new List<Vector2>(Cells * Cells * 6);
            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                var n = Vector3.Cross(b - a, c - a).normalized;
                float y = (a.y + b.y + c.y) / 3f;
                float steep = 1f - n.y;
                var mid = (a + b + c) / 3f;
                int col;
                if (y > 40f) col = 5;
                else if (steep > 0.35f) col = Mathf.PerlinNoise(mid.x * 0.05f, mid.z * 0.05f) > 0.5f ? 3 : 4;
                else if (steep > 0.18f) col = 2;
                else col = Mathf.PerlinNoise(seedX + mid.x * 0.02f, seedZ + mid.z * 0.02f) > 0.55f ? 1 : 0;
                var uv = Uv(col);
                verts.Add(a); verts.Add(b); verts.Add(c);
                uvs.Add(uv); uvs.Add(uv); uvs.Add(uv);
            }
            for (int i = 0; i < Cells; i++)
                for (int j = 0; j < Cells; j++)
                {
                    float x0 = -half + i * cell, z0 = -half + j * cell;
                    var p00 = new Vector3(x0, h[i, j], z0);
                    var p10 = new Vector3(x0 + cell, h[i + 1, j], z0);
                    var p01 = new Vector3(x0, h[i, j + 1], z0 + cell);
                    var p11 = new Vector3(x0 + cell, h[i + 1, j + 1], z0 + cell);
                    // Alternate the diagonal for the low-poly look.
                    if (((i + j) & 1) == 0) { Tri(p00, p01, p11); Tri(p00, p11, p10); }
                    else { Tri(p00, p01, p10); Tri(p10, p01, p11); }
                }
            var tris = new int[verts.Count];
            for (int t = 0; t < tris.Length; t++) tris[t] = t;
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32, name = "BonkTerrain" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Adds a flat-shaded n-sided cone or prism (radius r0 at y0, r1 at y1) to the lists.</summary>
        static void Frustum(List<Vector3> v, List<Vector2> uv, int sides, float r0, float r1, float y0, float y1, int col, float twist)
        {
            for (int s = 0; s < sides; s++)
            {
                float a0 = twist + Mathf.PI * 2f * s / sides, a1 = twist + Mathf.PI * 2f * (s + 1) / sides;
                var b0 = new Vector3(Mathf.Sin(a0) * r0, y0, Mathf.Cos(a0) * r0);
                var b1 = new Vector3(Mathf.Sin(a1) * r0, y0, Mathf.Cos(a1) * r0);
                var t0 = new Vector3(Mathf.Sin(a0) * r1, y1, Mathf.Cos(a0) * r1);
                var t1 = new Vector3(Mathf.Sin(a1) * r1, y1, Mathf.Cos(a1) * r1);
                v.Add(b0); v.Add(t0); v.Add(b1);
                v.Add(b1); v.Add(t0); v.Add(t1);
                v.Add(new Vector3(0, y0, 0)); v.Add(b0); v.Add(b1); // bottom cap
                for (int k = 0; k < 9; k++) uv.Add(Uv(col));
            }
        }

        static Mesh Build(List<Vector3> v, List<Vector2> uv, string name)
        {
            var tris = new int[v.Count];
            for (int t = 0; t < tris.Length; t++) tris[t] = t;
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static Mesh PineMesh()
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>();
            Frustum(v, uv, 6, 0.45f, 0.35f, -0.5f, 2.2f, 6, 0f);
            Frustum(v, uv, 7, 2.6f, 0f, 1.8f, 5.2f, 7, 0.2f);
            Frustum(v, uv, 7, 2.0f, 0f, 3.6f, 6.6f, 7, 0.6f);
            Frustum(v, uv, 7, 1.4f, 0f, 5.2f, 8.0f, 7, 1.0f);
            return Build(v, uv, "BonkPine");
        }

        static Mesh RoundTreeMesh()
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>();
            Frustum(v, uv, 6, 0.5f, 0.35f, -0.5f, 3.0f, 6, 0f);
            Frustum(v, uv, 8, 1.6f, 2.8f, 2.6f, 4.4f, 7, 0.3f);
            Frustum(v, uv, 8, 2.8f, 1.2f, 4.4f, 6.8f, 7, 0.3f);
            return Build(v, uv, "BonkTree");
        }

        static Mesh RockMesh(System.Random rng)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>();
            float r = 1f;
            Frustum(v, uv, 6, r, r * 0.9f, -0.6f, 0.7f, 3, (float)rng.NextDouble());
            Frustum(v, uv, 6, r * 0.9f, r * 0.25f, 0.7f, 1.5f, 4, (float)rng.NextDouble());
            return Build(v, uv, "BonkRock");
        }

        // ---------------- building the stage ----------------

        static bool Eligible()
        {
            var def = SceneCatalog.GetSceneDefForCurrentScene();
            return def && def.sceneType == SceneType.Stage && def.stageOrder >= 1 && def.stageOrder <= 5 && SceneInfo.instance;
        }

        static ulong Seed()
        {
            var id = Run.instance.GetUniqueId().ToByteArray();
            ulong s = System.BitConverter.ToUInt64(id, 0) ^ System.BitConverter.ToUInt64(id, 8);
            s ^= (ulong)(Run.instance.stageClearCount + 1) * 0x9E3779B97F4A7C15UL;
            foreach (char ch in SceneManager.GetActiveScene().name) s = s * 31 + ch;
            return s;
        }

        static bool KeepRenderer(Component c) =>
            c is ParticleSystemRenderer || c.GetComponentInParent<NetworkIdentity>() || c.GetComponentInParent<IInteractable>() != null
            || c.GetComponentInParent<SpawnPoint>();

        /// <summary>Hides the stage's own ground and props; its sky, light, fog and networked things stay.</summary>
        static void HideStage(GameObject[] roots)
        {
            int renderers = 0, colliders = 0;
            foreach (var root in roots)
            {
                if (root.name.StartsWith("RiskOfBonk")) continue;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    if (r.enabled && !KeepRenderer(r)) { r.enabled = false; renderers++; }
                foreach (var t in root.GetComponentsInChildren<Terrain>(true)) t.enabled = false;
                foreach (var c in root.GetComponentsInChildren<Collider>(true))
                {
                    if (!c.enabled || c.isTrigger && !c.GetComponent<MapZone>()) continue;
                    if (c.GetComponentInParent<NetworkIdentity>() || c.GetComponentInParent<IInteractable>() != null) continue;
                    c.enabled = false;
                    colliders++;
                }
            }
            Log.Info($"Bonk style: hid {renderers} renderers and {colliders} colliders of the stage");
        }

        /// <summary>Fixed interactables of the stage (newt altars and the like) are set down on the new ground.</summary>
        static void SettleInteractables(GameObject[] roots)
        {
            float lim = half * Playable - 10f;
            foreach (var root in roots)
                foreach (var it in root.GetComponentsInChildren<IInteractable>(true))
                {
                    var tr = ((Component)it).transform;
                    var local = tr.position - Center;
                    local.x = Mathf.Clamp(local.x, -lim, lim);
                    local.z = Mathf.Clamp(local.z, -lim, lim);
                    tr.position = Center + new Vector3(local.x, Height(local.x, local.z), local.z);
                }
        }

        static GameObject terrainRoot;

        static void BuildTerrain()
        {
            var sw = Stopwatch.StartNew();
            var rng = new System.Random((int)(Seed() & 0x7fffffff) ^ (int)(Seed() >> 33));
            seedX = (float)rng.NextDouble() * 1000f;
            seedZ = (float)rng.NextDouble() * 1000f;
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();

            // Centre the new map on the players' spawn so the stage's sky and fog sit right.
            var spawn = SpawnPoint.readOnlyInstancesList.Count > 0 ? SpawnPoint.readOnlyInstancesList[0].transform.position : Vector3.zero;
            Center = spawn - Vector3.up * 1f;

            treeSpots.Clear();
            HideStage(roots);
            foreach (var zone in Object.FindObjectsOfType<MapZone>()) zone.enabled = false;

            terrainRoot = new GameObject("RiskOfBonkTerrain");
            SceneManager.MoveGameObjectToScene(terrainRoot, scene);
            terrainRoot.transform.position = Center;
            terrainRoot.layer = WorldLayer;
            var mat = Mat(Palette(scene.name.ToLowerInvariant()));
            var mesh = TerrainMesh();
            terrainRoot.AddComponent<MeshFilter>().sharedMesh = mesh;
            terrainRoot.AddComponent<MeshRenderer>().sharedMaterial = mat;
            terrainRoot.AddComponent<MeshCollider>().sharedMesh = mesh;

            // Trees in groves, rocks scattered; none in the start clearing.
            var pine = PineMesh();
            var round = RoundTreeMesh();
            var rocks = new[] { RockMesh(rng), RockMesh(rng), RockMesh(rng) };
            float lim = half * Playable - 6f;
            int trees = 0, stones = 0;
            for (int n = 0; n < 900 && (trees < 140 || stones < 70); n++)
            {
                float x = ((float)rng.NextDouble() * 2f - 1f) * lim, z = ((float)rng.NextDouble() * 2f - 1f) * lim;
                if (x * x + z * z < 30f * 30f) continue;
                bool grove = Mathf.PerlinNoise(seedX + x * 0.012f, seedZ + z * 0.012f) > 0.52f;
                bool tree = grove && trees < 140;
                if (!tree && (stones >= 70 || rng.NextDouble() > 0.35)) continue;
                float y = Height(x, z);
                // Not on steep ground.
                float dx = Height(x + 1.5f, z) - y, dz = Height(x, z + 1.5f) - y;
                if (Mathf.Abs(dx) > 1.2f || Mathf.Abs(dz) > 1.2f) continue;
                var go = new GameObject(tree ? "BonkTree" : "BonkRock");
                go.layer = WorldLayer;
                go.transform.SetParent(terrainRoot.transform, false);
                go.transform.localPosition = new Vector3(x, y, z);
                go.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                float s = tree ? 0.8f + (float)rng.NextDouble() * 0.7f : 1.2f + (float)rng.NextDouble() * 2.8f;
                go.transform.localScale = new Vector3(s, tree ? s : s * (0.6f + (float)rng.NextDouble() * 0.5f), s);
                var m = tree ? (rng.NextDouble() < 0.6 ? pine : round) : rocks[rng.Next(rocks.Length)];
                go.AddComponent<MeshFilter>().sharedMesh = m;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                if (tree)
                {
                    var cap = go.AddComponent<CapsuleCollider>();
                    cap.radius = 0.5f; cap.height = 4f; cap.center = new Vector3(0, 1.5f, 0);
                    treeSpots.Add(new Vector2(x, z));
                    trees++;
                }
                else { go.AddComponent<MeshCollider>().sharedMesh = m; stones++; }
            }

            SettleInteractables(roots);
            // Players start in the clearing.
            int i = 0;
            foreach (var sp in SpawnPoint.readOnlyInstancesList)
            {
                float a = i++ * 2.4f;
                var local = new Vector3(Mathf.Sin(a) * 4f, 0f, Mathf.Cos(a) * 4f);
                sp.transform.position = Center + local + Vector3.up * (Height(local.x, local.z) + 0.5f);
            }
            Physics.SyncTransforms();
            Active = true;
            Log.Info($"Bonk style: terrain built in {sw.ElapsedMilliseconds} ms ({trees} trees, {stones} rocks), seed {seedX:0.0}/{seedZ:0.0}");
        }

        /// <summary>
        /// Host: new ground and air node graphs on the new terrain. The editor's bake tests every link with physics (over
        /// 20 s for this map); the terrain is a known heightfield, so links, line of sight and Teleporter spots are worked
        /// out from the heights instead.
        /// </summary>
        static void BakeGraphs()
        {
            var sw = Stopwatch.StartNew();
            var info = SceneInfo.instance;
            float lim = half * Playable - 4f;
            const int AllHulls = (int)(HullMask.Human | HullMask.Golem | HullMask.BeetleQueen);

            bool Clear(Vector3 a, Vector3 b, float above, float step = 4f)
            {
                int steps = Mathf.CeilToInt(Vector3.Distance(a, b) / step);
                for (int k = 1; k < steps; k++)
                {
                    var p = Vector3.Lerp(a, b, (float)k / steps);
                    if (GridHeight(p.x, p.z) > p.y - above) return false;
                }
                return true;
            }

            NodeGraph Bake(string name, List<Vector3> spots, float linkRange, bool ground, out GameObject go)
            {
                go = new GameObject(name);
                var nodes = new List<MapNode>(spots.Count);
                foreach (var spot in spots)
                {
                    var n = new GameObject("MapNode");
                    n.transform.SetParent(go.transform, false);
                    n.transform.position = Center + spot;
                    var node = n.AddComponent<MapNode>();
                    node.flags = NodeFlags.NoCeiling;
                    nodes.Add(node);
                }
                for (int a = 0; a < spots.Count; a++)
                {
                    for (int b = 0; b < spots.Count; b++)
                    {
                        if (a == b) continue;
                        var d = spots[b] - spots[a];
                        float flat = new Vector2(d.x, d.z).magnitude;
                        if (flat > linkRange) continue;
                        // Ground: walkable if the slope is under 45 degrees and the ground in between has no bump.
                        bool ok = ground ? Mathf.Abs(d.y) <= flat && Clear(spots[a] + Vector3.up * 2f, spots[b] + Vector3.up * 2f, 0f)
                            : Clear(spots[a], spots[b], 3f);
                        if (ok) nodes[a].links.Add(new MapNode.Link { nodeB = nodes[b], distanceScore = d.magnitude, hullMask = AllHulls });
                    }
                    if (ground)
                    {
                        // Teleporter spots: ground within -3..+7 m all the way round at 15 m (what MapNode.TestTeleporterOK checks).
                        bool tp = true;
                        for (int k = 0; k < 20 && tp; k++)
                        {
                            float ang = Mathf.PI * 2f * k / 20f;
                            float dy = GridHeight(spots[a].x + Mathf.Sin(ang) * 15f, spots[a].z + Mathf.Cos(ang) * 15f) - spots[a].y;
                            tp = dy > -3f && dy < 7f;
                        }
                        if (tp) nodes[a].flags |= NodeFlags.TeleporterOK;
                    }
                }
                // Line of sight, once per pair, up to 120 m (farther counts as out of sight).
                var masks = new List<SerializableBitArray>(spots.Count);
                for (int a = 0; a < spots.Count; a++) masks.Add(new SerializableBitArray(spots.Count));
                for (int a = 0; a < spots.Count; a++)
                {
                    masks[a][a] = true;
                    for (int b = a + 1; b < spots.Count; b++)
                    {
                        if ((spots[b] - spots[a]).sqrMagnitude > 120f * 120f) continue;
                        if (Clear(spots[a] + Vector3.up, spots[b] + Vector3.up, 0f, 8f)) { masks[a][b] = true; masks[b][a] = true; }
                    }
                }
                var graph = ScriptableObject.CreateInstance<NodeGraph>();
                graph.SetNodes(nodes.AsReadOnly(), masks.AsReadOnly());
                go.SetActive(false);
                return graph;
            }

            var groundSpots = new List<Vector3>();
            for (float x = -lim; x <= lim; x += 10f)
                for (float z = -lim; z <= lim; z += 10f)
                {
                    float y = GridHeight(x, z);
                    if (y > 30f) continue;
                    float dx = GridHeight(x + 2f, z) - GridHeight(x - 2f, z), dz = GridHeight(x, z + 2f) - GridHeight(x, z - 2f);
                    if (Mathf.Abs(dx) > 2.8f || Mathf.Abs(dz) > 2.8f) continue; // steeper than ~35 degrees
                    bool tree = false;
                    foreach (var t in treeSpots) if ((t - new Vector2(x, z)).sqrMagnitude < 9f) { tree = true; break; }
                    if (!tree) groundSpots.Add(new Vector3(x, y, z));
                }
            var airSpots = new List<Vector3>();
            for (float x = -lim; x <= lim; x += 20f)
                for (float z = -lim; z <= lim; z += 20f)
                {
                    float y = GridHeight(x, z);
                    if (y > 30f) continue;
                    airSpots.Add(new Vector3(x, y + 12f, z));
                    airSpots.Add(new Vector3(x, y + 26f, z));
                }
            var groundGraph = Bake("RiskOfBonkGroundNodes", groundSpots, 14.5f, true, out var groundGo);
            var airGraph = Bake("RiskOfBonkAirNodes", airSpots, 29f, false, out var airGo);

            info.groundNodes = groundGraph;
            info.airNodes = airGraph;
            info.groundNodeGroup = groundGo.AddComponent<MapNodeGroup>();
            info.airNodeGroup = airGo.AddComponent<MapNodeGroup>();
            Log.Info($"Bonk style: node graphs built in {sw.ElapsedMilliseconds} ms ({groundSpots.Count} ground, {airSpots.Count} air)");
        }

        /// <summary>Anyone who falls off the map comes back to the clearing.</summary>
        public sealed class FallGuard : MonoBehaviour
        {
            float next;
            void Update()
            {
                if (!Active || Time.time < next) return;
                next = Time.time + 0.5f;
                foreach (var b in CharacterBody.readOnlyInstancesList)
                {
                    if (!b || !b.hasEffectiveAuthority) continue;
                    var p = b.footPosition - Center;
                    bool outside = p.y < -60f || Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z)) > half + 20f;
                    if (outside) TeleportHelper.TeleportBody(b, Center + Vector3.up * (Height(0f, 0f) + 3f));
                }
            }
        }

        public static void Init(Harmony harmony, GameObject host)
        {
            SceneManager.activeSceneChanged += (a, b) => Active = false;
            host.AddComponent<FallGuard>();
            harmony.PatchAll(typeof(Patches));
        }

        [HarmonyPatch]
        static class Patches
        {
            // Before the host's SceneDirector places the Teleporter and interactables; every player runs it.
            [HarmonyPrefix, HarmonyPatch(typeof(SceneDirector), "Start")]
            static void Build()
            {
                if (Active || !StageMode.IsBonk || !Run.instance || !Eligible()) return;
                try
                {
                    BuildTerrain();
                    if (NetworkServer.active) BakeGraphs();
                }
                catch (System.Exception e) { Log.Error($"Bonk style terrain: {e}"); }
            }
        }
    }
}
