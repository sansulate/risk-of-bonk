using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using RoR2;
using RoR2.Navigation;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.SceneManagement;

namespace RiskOfBonk
{
    /// <summary>
    /// "Bonk style": Megabonk's own map generation on Risk of Rain 2's normal stages. Every player builds the same map
    /// from the run's synced unique id and the stage number, the way Megabonk's ProceduralTileGeneration does: a maze
    /// (a random spanning tree over an N x N grid of square tiles, MapParameters size/scale) whose heights change along
    /// straight corridors (MazeHeightGenerator: a straight step changes height with chance "volatility", keeping its
    /// direction up or down), so the map is flat plateaus joined by ramps with cliffs between different heights; tile
    /// tops and cliff sides use Megabonk's tile textures, and Megabonk's own trees, logs, bushes, rocks, boulders and
    /// ruins are scattered by its StageData.randomMapObjects rules (amounts, scales, spacing) plus its pre-made tile
    /// dressings on a quarter of the tiles. The Desert is Megabonk's other generator (MapGenerator): a Perlin-noise
    /// heightmap of dunes shaped by its height curve, with its triplanar sand. Stages take Megabonk's maps in turn:
    /// Forest 1, Desert 1, Forest 2, Desert 2, Graveyard, Forest 3, Desert 3, and round again.
    /// The stage's own ground is hidden (its sky stays), its fixed interactables are set down on the new ground, and the
    /// host bakes ground and air node graphs from the tile heights so the Teleporter, chests and monsters use the map.
    /// Props only collide where Megabonk's do (tree trunks, logs, boulders, big rocks), with smooth primitive colliders.
    /// </summary>
    public static class BonkTerrain
    {
        public static bool Active { get; private set; }
        public static Vector3 Center { get; private set; }
        /// <summary>The Megabonk map of the current Bonk style stage ("Forest 1".."Forest 3", "Desert 1".."Desert 3", "Graveyard").</summary>
        public static string MapName => Active && profile != null ? profile.name : null;
        static readonly int WorldLayer = LayerIndex.world.intVal;

        // ---------------- Megabonk's stages (StageData + MapParameters, tools/mbdata/stages.py) ----------------

        sealed class PropRule
        {
            public string[] prefabs;
            public int amount;
            public float checkRadius, scaleMin, scaleMax, upOffset;
            public bool align;
            public PropRule(int amount, float r, float s0, float s1, params string[] prefabs) { this.amount = amount; checkRadius = r; scaleMin = s0; scaleMax = s1; this.prefabs = prefabs; }
        }

        sealed class Profile
        {
            public string name;
            public int size;          // tiles per side
            public float tile;        // scaledTileWidth (m)
            public float volatility;
            public string[] tops;     // flatMaterials' textures, one picked per tile
            public string fillTop = "FillTop", fillMiddle = "FillMiddle";
            public int fillFormat = 10;
            public Color fog;
            public float fogDensity = 0.007f;
            public Color light = new Color(1f, 0.957f, 0.839f), ambience = new Color(0.745f, 0.745f, 0.745f);
            public PropRule[] props;
            public string[] tilePrefabs;
            public float populateTiles = 0.25f;
            public float Step => tile / 2f; // scaledTileHeight
            // Noise-mesh maps (Desert): Megabonk's MapGenerator (NoiseData, TerrainData, StageData.proceduralMapScale).
            public bool noise;
            public float noiseScale, persistence, lacunarity, heightMultiplier, mapScale = 1f;
            public int octaves;
            public Keyframe[] curve;
            public string sand, sandSide;
            public int sandFormat = 4, sideFormat = 10;
        }

        static readonly string[] Rocks = { "rock_medium_01", "rock_medium_02", "rock_medium_03", "rock_small_01", "rock_small_02", "rock_small_05" };
        static readonly string[] DarkRocks = { "rock_medium_01Dark", "rock_medium_02Dark", "rock_medium_03Dark", "rock_small_01Dark", "rock_small_02Dark", "rock_small_05Dark" };
        static readonly string[] ForestTiles = Enumerable.Range(0, 9).Select(i => "Forest0_Prefab" + i).ToArray();

        static readonly Profile[] Profiles =
        {
            new Profile
            {
                name = "Forest 1", size = 15, tile = 40, volatility = 0.8f,
                tops = new[] { "TopGrass12", "TopGrass2", "TopGrass3", "TopGrass4", "TopGrass5", "TopGrass6", "TopGrass7", "TopGrass8", "TopGrass9", "TopGrass11" },
                fog = new Color(0f, 0.48f, 0.62f),
                props = new[]
                {
                    new PropRule(300, 1f, 0.75f, 2.5f, "Tree0"), new PropRule(40, 2f, 0.75f, 1.5f, "Log0"), new PropRule(100, 0.5f, 0.75f, 1.25f, "Bush0"),
                    new PropRule(150, 0.5f, 1f, 7f, Rocks), new PropRule(100, 0.5f, 1f, 2f, "Bush1") { align = true },
                    new PropRule(8, 0.5f, 0.75f, 2f, "Boulder") { upOffset = 4f, align = true },
                },
                tilePrefabs = ForestTiles,
            },
            new Profile
            {
                name = "Forest 2", size = 15, tile = 44, volatility = 0.8f, tops = new[] { "Forest1_TopGrass" }, fog = new Color(0.38f, 0.28f, 0.62f), ambience = new Color(0.7f, 0.66f, 0.89f),
                props = new[]
                {
                    new PropRule(300, 1f, 1f, 4f, "Tree0Forest2", "Tree1Forest2", "Tree2"), new PropRule(50, 2f, 0.75f, 3f, "Log0"),
                    new PropRule(150, 0.5f, 1f, 7f, Rocks), new PropRule(300, 0.5f, 0.75f, 3.5f, "Bush") { align = true },
                    new PropRule(8, 0.5f, 0.75f, 2f, "Boulder") { upOffset = 4f, align = true },
                },
                tilePrefabs = ForestTiles,
            },
            new Profile
            {
                name = "Forest 3", size = 16, tile = 48, volatility = 0.8f, tops = new[] { "Forest1_TopGrass" }, fog = new Color(0.3f, 0.08f, 0.08f), ambience = new Color(0.85f, 0.65f, 0.65f),
                props = new[]
                {
                    new PropRule(300, 1f, 1f, 5f, "DeadTree0", "DeadTree1", "DeadTree2"), new PropRule(50, 2f, 0.75f, 3f, "Log0Dead"),
                    new PropRule(150, 0.5f, 1f, 8f, DarkRocks), new PropRule(300, 0.5f, 0.75f, 3.5f, "BushRed") { align = true },
                    new PropRule(8, 0.5f, 0.75f, 2f, "Boulder") { upOffset = 4f, align = true },
                },
                tilePrefabs = ForestTiles,
            },
            new Profile
            {
                name = "Graveyard", size = 19, tile = 40, volatility = 1f, tops = new[] { "TopGrass1" }, fillFormat = 4, fog = new Color(0.49f, 0.42f, 0.62f), fogDensity = 0.0075f, light = new Color(0.945f, 0.843f, 1f),
                props = new[]
                {
                    new PropRule(450, 1f, 0.75f, 2.5f, "DeadTree0", "DeadTree1", "DeadTree2"), new PropRule(60, 2f, 0.75f, 1.5f, "Log0"),
                    new PropRule(250, 0.5f, 1f, 7f, Rocks), new PropRule(15, 0.5f, 0.75f, 2f, "Boulder") { upOffset = 4f, align = true },
                    new PropRule(25, 2f, 0.9f, 1.15f, "Lightpost", "Brazier") { align = true }, new PropRule(150, 0.5f, 1f, 2f, "Bush1") { align = true },
                    new PropRule(50, 2f, 1f, 1f, "Fence", "Fence_Broken", "Arch") { align = true }, new PropRule(25, 2f, 1f, 1f, "Barrel") { align = true },
                },
                tilePrefabs = Enumerable.Range(0, 12).Select(i => "Graveyard_Tile" + i).ToArray(),
            },
        };

        // Megabonk's TerrainDesert height curve (keyframes: time, value, in and out tangent).
        static readonly Keyframe[] DesertCurve =
        {
            new Keyframe(0.0013f, 0.001f, 1.8899f, 1.8899f), new Keyframe(0.0718f, 0.1013f, 0.1141f, 0.1141f),
            new Keyframe(0.3345f, 0.1109f, 0.118f, 0.118f), new Keyframe(0.8312f, 0.5727f, -0.0059f, -0.0059f),
        };
        static readonly string[] DesertRocks = { "Rock_4_Small", "Rock_10_Small", "Rock_11_Small", "Rock_12_Small", "Rock_13_Small", "Rock_14_Small", "Rock_15_Small" };
        static readonly string[] Landmarks = { "Ladnmark1", "Ladnmark2", "Landmark3", "Landmark4" };

        static Profile Desert(string name, float mapScale, float noiseScale, float persistence, Color fog, Color ambience, Color light, float landmarkMax, int landmarks, float rockMax, int rocks, int trees, string sand, string side, int sideFormat) => new Profile
        {
            name = name, noise = true, mapScale = mapScale, noiseScale = noiseScale, octaves = 2, persistence = persistence, lacunarity = 3f,
            heightMultiplier = 11f, curve = DesertCurve, sand = sand, sandSide = side, sideFormat = sideFormat,
            fog = fog, fogDensity = 0.006f, ambience = ambience, light = light, tops = new string[0], tilePrefabs = new string[0],
            props = new[]
            {
                new PropRule(landmarks, 0.5f, landmarkMax * 0.45f, landmarkMax, Landmarks),
                new PropRule(15, 0.5f, 1f, 1.25f, "TilePrefabHouse", "TilePrefabHouse2") { align = true },
                new PropRule(rocks, 1f, 0.75f, rockMax, DesertRocks) { align = true },
                new PropRule(120, 1f, 0.8f, 4f, "Cactus1", "Cactus2"),
                new PropRule(60, 1f, 1f, 4f, "Log0 1", "Log2") { align = true },
                new PropRule(trees, 1f, 0.5f, 2.5f, "DesertTree1", "DesertTree2"),
                new PropRule(300, 0.5f, 0.75f, 1.5f, "DesertBush"),
            },
        };

        static readonly Profile[] Deserts =
        {
            // StageDesert1-3: fog, ambience and light colours, map scale, NoiseDesert1 (scale 100, persistence 0.4) or
            // NoiseDesert3 (scale 40, 0.5), and their prop rules.
            Desert("Desert 1", 1f, 100f, 0.4f, new Color(0.93f, 0.82f, 0.59f), new Color(0.75f, 0.75f, 0.75f), new Color(1f, 0.96f, 0.84f), 3f, 8, 6f, 60, 15, "SandNew", "SandRockNew", 10),
            Desert("Desert 2", 1.1f, 100f, 0.4f, new Color(0.42f, 0.62f, 0.94f), new Color(0.47f, 0.56f, 0.95f), new Color(0.36f, 0.64f, 1f), 5f, 8, 6f, 60, 15, "SandNew", "SandRockNew", 10),
            Desert("Desert 3", 1.2f, 40f, 0.5f, new Color(0.58f, 0.19f, 0.19f), new Color(0.82f, 0.63f, 0.62f), new Color(0.88f, 0.74f, 0.72f) * 0.75f, 6f, 14, 8f, 100, 50, "SandNew_2", "SandRockNew_2", 4),
        };

        // Stage order: Megabonk's maps in turn, each map's tiers in order.
        static readonly Profile[] Order = { Profiles[0], Deserts[0], Profiles[1], Deserts[1], Profiles[3], Profiles[2], Deserts[2] };

        static Profile ProfileFor(int stage) => Order[stage % Order.Length];

        // ---------------- Megabonk assets (read while the content loads) ----------------

        internal static readonly Dictionary<string, RawProp> rawProps = new Dictionary<string, RawProp>();
        static readonly Dictionary<string, RawTexture> rawTextures = new Dictionary<string, RawTexture>();

        /// <summary>Reads Megabonk's map props and tile textures (Megabonk's files are open while the content loads).</summary>
        public static void Prepare(MegabonkLibrary lib)
        {
            var sw = Stopwatch.StartNew();
            foreach (var p in Profiles.Concat(Deserts))
            {
                if (p.noise)
                {
                    foreach (var name in p.props.SelectMany(r => r.prefabs))
                        if (!rawProps.ContainsKey(name)) lock (lib) rawProps[name] = lib.ReadProp(name);
                    foreach (var (t, f) in new[] { (p.sand, p.sandFormat), (p.sandSide, p.sideFormat) })
                        if (!rawTextures.ContainsKey(t + f)) lock (lib) rawTextures[t + f] = lib.ExactTexture(t, f);
                    continue;
                }
                foreach (var name in p.props.SelectMany(r => r.prefabs).Concat(p.tilePrefabs))
                    if (!rawProps.ContainsKey(name)) lock (lib) rawProps[name] = lib.ReadProp(name);
                foreach (var t in p.tops.Concat(new[] { p.fillTop, p.fillMiddle }))
                {
                    string key = t + (t.StartsWith("Fill") ? p.fillFormat.ToString() : "");
                    if (rawTextures.ContainsKey(key)) continue;
                    lock (lib) rawTextures[key] = lib.ExactTexture(t, t.StartsWith("Fill") ? p.fillFormat : -1);
                }
            }
            var missing = rawProps.Where(k => k.Value == null).Select(k => k.Key).Concat(rawTextures.Where(k => k.Value == null).Select(k => k.Key)).ToList();
            Log.Info($"Bonk style: {rawProps.Count(k => k.Value != null)} Megabonk props, {rawTextures.Count(k => k.Value != null)} tile textures read in {sw.ElapsedMilliseconds} ms"
                     + (missing.Count > 0 ? "; missing: " + string.Join(", ", missing) : ""));
        }

        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static readonly Dictionary<string, Mesh> propMeshes = new Dictionary<string, Mesh>();
        static readonly Dictionary<string, Material[]> propMats = new Dictionary<string, Material[]>();
        static Shader standard;

        static Material Lit(Texture tex, Color color, string name)
        {
            if (materials.TryGetValue(name, out var m)) return m;
            if (!standard) standard = Addressables.LoadAssetAsync<Shader>("RoR2/Base/Shaders/HGStandard.shader").WaitForCompletion();
            m = new Material(standard) { name = "Bonk_" + name };
            if (tex) m.SetTexture("_MainTex", tex);
            m.SetColor("_Color", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
            if (m.HasProperty("_SpecularStrength")) m.SetFloat("_SpecularStrength", 0f);
            materials[name] = m;
            return m;
        }

        static Material TileMat(string key, Color tint)
        {
            rawTextures.TryGetValue(key, out var raw);
            var tex = UnityConvert.Texture(raw);
            if (tex) { tex.filterMode = FilterMode.Point; tex.wrapMode = TextureWrapMode.Repeat; }
            return Lit(tex, tex ? tint : new Color(0.4f, 0.7f, 0.3f), "tile_" + key);
        }

        internal static bool PropModel(string name, out Mesh mesh, out Material[] mats)
        {
            mesh = null; mats = null;
            if (!rawProps.TryGetValue(name, out var p) || p == null) return false;
            if (!propMeshes.TryGetValue(name, out mesh))
            {
                propMeshes[name] = mesh = UnityConvert.Mesh(p.Model);
                propMats[name] = p.Model.SubMeshes.Select((s, i) =>
                {
                    var tex = UnityConvert.Texture(s.Texture);
                    if (tex) tex.filterMode = s.Texture.Filter == 0 ? FilterMode.Point : FilterMode.Bilinear;
                    return Lit(tex, new Color(s.Color[0], s.Color[1], s.Color[2]), name + "_" + i + "_" + (s.Texture?.Name ?? "none"));
                }).ToArray();
            }
            mats = propMats[name];
            return true;
        }

        // ---------------- the maze ----------------

        sealed class Cell
        {
            public int x, z, level, yDir, parentX = -1, parentZ = -1;
            public bool slope;   // a ramp from (level - yDir) at the parent's side to level at the far side
            public int dirX, dirZ;
            public bool inMaze;
            public int top;      // the tile's top texture
        }

        static Profile profile;
        static Cell[,] cells;
        static int N;            // tiles per side, edge ring included
        static float tile, step, half;
        static int minLevel, maxLevel, edgeLevel;

        /// <summary>Megabonk's Maze.Generate: a random depth-first spanning tree over the grid.</summary>
        static void Maze(System.Random rng, int size, int startX, int startZ)
        {
            var stack = new Stack<Cell>();
            var start = cells[startX, startZ];
            start.inMaze = true;
            stack.Push(start);
            var order = new List<Cell> { start };
            int[] dx = { 0, 1, 0, -1 }, dz = { 1, 0, -1, 0 };
            while (stack.Count > 0)
            {
                var c = stack.Peek();
                var options = new List<int>();
                for (int d = 0; d < 4; d++)
                {
                    int nx = c.x + dx[d], nz = c.z + dz[d];
                    if (nx >= 1 && nz >= 1 && nx <= size && nz <= size && !cells[nx, nz].inMaze) options.Add(d);
                }
                if (options.Count == 0) { stack.Pop(); continue; }
                int pick = options[rng.Next(options.Count)];
                var n = cells[c.x + dx[pick], c.z + dz[pick]];
                n.inMaze = true;
                n.parentX = c.x; n.parentZ = c.z;
                n.dirX = dx[pick]; n.dirZ = dz[pick];
                stack.Push(n);
                order.Add(n);
            }
            // MazeHeightGenerator.GenerateHeightMe: walking the tree from the start, a tile that continues straight on from its
            // parent changes height with chance volatility (and keeps going the same way while it can); turns stay level.
            foreach (var c in order.Skip(1))
            {
                var p = cells[c.parentX, c.parentZ];
                bool straight = p.parentX >= 0 && p.dirX == c.dirX && p.dirZ == c.dirZ;
                if (straight && rng.NextDouble() < profile.volatility)
                {
                    int dir = p.yDir != 0 ? p.yDir : (rng.NextDouble() < 0.5 ? 1 : -1);
                    // Megabonk biases heights towards centerHeightTarget; here the map stays within five levels.
                    if (p.level + dir > 3 || p.level + dir < -2) dir = -dir;
                    c.yDir = dir;
                    c.slope = true;
                    c.level = p.level + dir;
                }
                else
                {
                    c.yDir = straight ? p.yDir : 0;
                    c.level = p.level;
                }
            }
        }

        static void BuildCells(System.Random rng)
        {
            int size = profile.size;
            N = size + 2;
            tile = profile.tile;
            step = profile.Step;
            half = N * tile / 2f;
            cells = new Cell[N, N];
            for (int x = 0; x < N; x++)
                for (int z = 0; z < N; z++)
                    cells[x, z] = new Cell { x = x, z = z, top = rng.Next(profile.tops.Length) };
            int mid = 1 + size / 2;
            Maze(rng, size, mid, mid);
            minLevel = int.MaxValue; maxLevel = int.MinValue;
            for (int x = 1; x <= size; x++)
                for (int z = 1; z <= size; z++) { minLevel = Mathf.Min(minLevel, cells[x, z].level); maxLevel = Mathf.Max(maxLevel, cells[x, z].level); }
            // Map edge: a ring of high walls (Megabonk's MapEdgeFillType Walls).
            edgeLevel = maxLevel + 2;
            for (int x = 0; x < N; x++)
                for (int z = 0; z < N; z++)
                    if (x == 0 || z == 0 || x == N - 1 || z == N - 1) cells[x, z].level = edgeLevel;
        }

        static Cell CellAt(float x, float z)
        {
            int i = Mathf.Clamp(Mathf.FloorToInt((x + half) / tile), 0, N - 1), j = Mathf.Clamp(Mathf.FloorToInt((z + half) / tile), 0, N - 1);
            return cells[i, j];
        }

        /// <summary>Height of a tile's top at local (x, z): flat, or a ramp along the tile's direction.</summary>
        static float TileHeight(Cell c, float x, float z)
        {
            if (!c.slope) return c.level * step;
            float x0 = -half + c.x * tile, z0 = -half + c.z * tile;
            float u = (x - x0) / tile, v = (z - z0) / tile;
            // t: 0 at the side facing the parent, 1 at the far side.
            float t = c.dirX > 0 ? u : c.dirX < 0 ? 1f - u : c.dirZ > 0 ? v : 1f - v;
            return (c.level - c.yDir * (1f - Mathf.Clamp01(t))) * step;
        }

        /// <summary>Ground height at local (x, z) (exact, matches the mesh).</summary>
        public static float Height(float x, float z) => profile != null && profile.noise ? (noiseH == null ? 0f : NoiseHeight(x, z)) : cells == null ? 0f : TileHeight(CellAt(x, z), x, z);

        /// <summary>The ground's up direction at local (x, z).</summary>
        static Vector3 GroundNormal(float x, float z)
        {
            if (profile.noise)
            {
                float dx = Height(x + 1f, z) - Height(x - 1f, z), dz = Height(x, z + 1f) - Height(x, z - 1f);
                return new Vector3(-dx, 2f, -dz).normalized;
            }
            var c = CellAt(x, z);
            if (!c.slope) return Vector3.up;
            var up = Vector3.Cross(new Vector3(c.dirZ, 0, -c.dirX), new Vector3(c.dirX, c.yDir * step / tile, c.dirZ)).normalized;
            return Vector3.Dot(up, Vector3.up) < 0f ? -up : up;
        }

        // ---------------- noise-mesh maps (Megabonk's MapGenerator: the Desert) ----------------

        const int NoiseVerts = 241;  // MapGenerator.mapChunkSize
        static float[,] noiseH;      // vertex heights (m)
        static float spacing;        // metres between vertices: the mesh's localScale, 4 x StageData.proceduralMapScale
        static float lowest, highest;

        /// <summary>
        /// MapGenerator.GenerateMap: Noise.GenerateNoiseMap (octaves of Perlin noise from seeded offsets, amplitude x
        /// persistence and frequency x lacunarity per octave, normalised to 0..1), then MeshGenerator's heights:
        /// heightCurve(h) x heightMultiplier, with the mesh scaled 4 x 8 x 4 by the stage's map scale.
        /// </summary>
        static void BuildNoise(System.Random rng)
        {
            int n = NoiseVerts;
            spacing = 4f * profile.mapScale;
            half = (n - 1) * spacing / 2f;
            tile = 40f; step = 20f;
            var curve = new AnimationCurve(profile.curve);
            var offsets = new Vector2[profile.octaves];
            for (int o = 0; o < offsets.Length; o++) offsets[o] = new Vector2(rng.Next(-10000, 10000), rng.Next(-10000, 10000));
            var raw = new float[n, n];
            float min = float.MaxValue, max = float.MinValue, hw = n / 2f, scale = Mathf.Max(0.0001f, profile.noiseScale);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float amp = 1f, freq = 1f, h = 0f;
                    for (int o = 0; o < offsets.Length; o++)
                    {
                        float sx = (x - hw) / scale * freq + offsets[o].x, sy = (y - hw) / scale * freq + offsets[o].y;
                        h += (Mathf.PerlinNoise(sx, sy) * 2f - 1f) * amp;
                        amp *= profile.persistence;
                        freq *= profile.lacunarity;
                    }
                    raw[x, y] = h;
                    if (h < min) min = h;
                    if (h > max) max = h;
                }
            noiseH = new float[n, n];
            float hs = profile.heightMultiplier * 8f * profile.mapScale;
            lowest = float.MaxValue; highest = float.MinValue;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float v = curve.Evaluate(Mathf.InverseLerp(min, max, raw[x, y])) * hs;
                    noiseH[x, y] = v;
                    lowest = Mathf.Min(lowest, v); highest = Mathf.Max(highest, v);
                }
            minLevel = Mathf.FloorToInt(lowest / step); maxLevel = Mathf.CeilToInt(highest / step);
        }

        /// <summary>Height on the noise mesh, on the same two triangles per quad as the mesh.</summary>
        static float NoiseHeight(float x, float z)
        {
            int n = NoiseVerts;
            float gx = Mathf.Clamp((x + half) / spacing, 0f, n - 1.001f), gz = Mathf.Clamp((z + half) / spacing, 0f, n - 1.001f);
            int i = (int)gx, j = (int)gz;
            float fx = gx - i, fz = gz - j;
            float h00 = noiseH[i, j], h10 = noiseH[i + 1, j], h01 = noiseH[i, j + 1], h11 = noiseH[i + 1, j + 1];
            return fz > fx ? h00 + fz * (h01 - h00) + fx * (h11 - h01) : h00 + fx * (h10 - h00) + fz * (h11 - h10);
        }

        const int SubSand = 0, SubSandSide = 1;

        /// <summary>The noise map's mesh: Megabonk's triplanar sand (its top texture on gentle ground, the rock side texture where steep).</summary>
        static Mesh NoiseMesh(out int[] subIds)
        {
            int n = NoiseVerts;
            const float texMetres = 8f; // Megabonk's triplanar sand repeats every few metres
            var v = new Vector3[n * n];
            var uv = new Vector2[n * n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float x = -half + i * spacing, z = -half + j * spacing;
                    v[j * n + i] = new Vector3(x, noiseH[i, j], z);
                    uv[j * n + i] = new Vector2(x / texMetres, z / texMetres);
                }
            var sand = new List<int>((n - 1) * (n - 1) * 6);
            var side = new List<int>();
            void Tri(int a, int b, int c)
            {
                var nrm = Vector3.Cross(v[b] - v[a], v[c] - v[a]).normalized;
                var list = Mathf.Abs(nrm.y) < 0.8f ? side : sand;
                list.Add(a); list.Add(b); list.Add(c);
            }
            for (int j = 0; j < n - 1; j++)
                for (int i = 0; i < n - 1; i++)
                {
                    int p00 = j * n + i, p10 = p00 + 1, p01 = p00 + n, p11 = p01 + 1;
                    Tri(p00, p01, p11);
                    Tri(p00, p11, p10);
                }
            var m = new Mesh { indexFormat = IndexFormat.UInt32, name = "BonkNoiseTerrain" };
            m.vertices = v;
            m.uv = uv;
            m.subMeshCount = 2;
            m.SetTriangles(sand, SubSand);
            m.SetTriangles(side, SubSandSide);
            m.RecalculateNormals();
            m.RecalculateBounds();
            subIds = new[] { SubSand, SubSandSide };
            return m;
        }

        /// <summary>Invisible walls along the noise map's edges (Megabonk's map edge), so nobody walks off it.</summary>
        static void EdgeWalls()
        {
            float h = highest - lowest + 80f, y = (lowest + highest) / 2f + 20f;
            var walls = new[]
            {
                (new Vector3(half + 2f, y, 0f), new Vector3(4f, h, half * 2f + 8f)), (new Vector3(-half - 2f, y, 0f), new Vector3(4f, h, half * 2f + 8f)),
                (new Vector3(0f, y, half + 2f), new Vector3(half * 2f + 8f, h, 4f)), (new Vector3(0f, y, -half - 2f), new Vector3(half * 2f + 8f, h, 4f)),
            };
            foreach (var (pos, size) in walls)
            {
                var go = new GameObject("BonkEdge") { layer = WorldLayer };
                go.transform.SetParent(terrainRoot.transform, false);
                go.transform.localPosition = pos;
                go.AddComponent<BoxCollider>().size = size;
            }
        }

        static float GridHeight(float x, float z) => Height(x, z);

        // ---------------- meshes ----------------

        sealed class MeshBuilder
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly Dictionary<int, List<int>> subs = new Dictionary<int, List<int>>();
            public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
                if (!subs.TryGetValue(sub, out var t)) subs[sub] = t = new List<int>();
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
                t.Add(i); t.Add(i + 2); t.Add(i + 3);
            }
            public Mesh Build(out int[] subIds)
            {
                var m = new Mesh { indexFormat = IndexFormat.UInt32, name = "BonkTerrain" };
                m.SetVertices(v);
                m.SetUVs(0, uv);
                subIds = subs.Keys.OrderBy(k => k).ToArray();
                m.subMeshCount = subIds.Length;
                for (int s = 0; s < subIds.Length; s++) m.SetTriangles(subs[subIds[s]], s);
                m.RecalculateNormals();
                m.RecalculateBounds();
                return m;
            }
        }

        const int SubFillTop = 1000, SubFillMiddle = 1001;

        /// <summary>Tile tops (one texture across each tile, like Megabonk's tile prefabs) and the cliff walls between tiles.</summary>
        static Mesh TerrainMesh(out int[] subIds)
        {
            var b = new MeshBuilder();
            float floor = (minLevel - 1) * step;
            for (int x = 0; x < N; x++)
                for (int z = 0; z < N; z++)
                {
                    var c = cells[x, z];
                    float x0 = -half + x * tile, z0 = -half + z * tile, x1 = x0 + tile, z1 = z0 + tile;
                    var p00 = new Vector3(x0, TileHeight(c, x0, z0), z0);
                    var p10 = new Vector3(x1, TileHeight(c, x1 - 0.001f, z0), z0);
                    var p11 = new Vector3(x1, TileHeight(c, x1 - 0.001f, z1 - 0.001f), z1);
                    var p01 = new Vector3(x0, TileHeight(c, x0, z1 - 0.001f), z1);
                    // Clockwise seen from above: the top faces up.
                    b.Quad(c.top, p00, p01, p11, p10, new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
                    // Walls on the four sides, from this tile's edge down to the neighbour's (or the floor), facing out.
                    Wall(b, c, x0, z0, x1, z0, 0, -1, floor); // south
                    Wall(b, c, x1, z0, x1, z1, 1, 0, floor);  // east
                    Wall(b, c, x1, z1, x0, z1, 0, 1, floor);  // north
                    Wall(b, c, x0, z1, x0, z0, -1, 0, floor); // west
                }
            return b.Build(out subIds);
        }

        static void Wall(MeshBuilder b, Cell c, float ax, float az, float bx, float bz, int nx, int nz, float floor)
        {
            int ox = c.x + nx, oz = c.z + nz;
            Cell n = ox >= 0 && oz >= 0 && ox < N && oz < N ? cells[ox, oz] : null;
            // Edge points, nudged into this tile and into the neighbour.
            Vector3 inA = new Vector3(ax - nx * 0.001f + (bx - ax) * 0.0005f, 0, az - nz * 0.001f + (bz - az) * 0.0005f);
            Vector3 inB = new Vector3(bx - nx * 0.001f - (bx - ax) * 0.0005f, 0, bz - nz * 0.001f - (bz - az) * 0.0005f);
            float ta = TileHeight(c, inA.x, inA.z), tb = TileHeight(c, inB.x, inB.z);
            float ba = n != null ? TileHeight(n, inA.x + nx * 0.002f, inA.z + nz * 0.002f) : floor;
            float bb = n != null ? TileHeight(n, inB.x + nx * 0.002f, inB.z + nz * 0.002f) : floor;
            ba = Mathf.Max(floor, Mathf.Min(ba, ta));
            bb = Mathf.Max(floor, Mathf.Min(bb, tb));
            if (ta - ba < 0.01f && tb - bb < 0.01f) return;
            // Megabonk's fill tiles: one texture per tile width and per tile height step; FillTop (with the grass lip)
            // on the step under the top, FillMiddle further down.
            float len = Vector2.Distance(new Vector2(ax, az), new Vector2(bx, bz)) / tile;
            float top = Mathf.Max(ta, tb), lipA = ta - step, lipB = tb - step;
            var A = new Vector3(ax, 0, az); var B = new Vector3(bx, 0, bz);
            // Upper band: from the top down one step (not below the neighbour).
            float ua = Mathf.Max(ba, lipA), ub = Mathf.Max(bb, lipB);
            b.Quad(SubFillTop, A + Vector3.up * ua, A + Vector3.up * ta, B + Vector3.up * tb, B + Vector3.up * ub,
                new Vector2(0, (ua - lipA) / step), new Vector2(0, 1), new Vector2(len, 1), new Vector2(len, (ub - lipB) / step));
            // Lower band.
            if (ua - ba > 0.01f || ub - bb > 0.01f)
                b.Quad(SubFillMiddle, A + Vector3.up * ba, A + Vector3.up * ua, B + Vector3.up * ub, B + Vector3.up * bb,
                    new Vector2(0, ba / step), new Vector2(0, ua / step), new Vector2(len, ub / step), new Vector2(len, bb / step));
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

        // The stage's own particle effects (waterfalls, falling leaves, dust, rain) go too: they hang over the old
        // geometry, in mid-air over the Bonk map.
        static bool KeepRenderer(Component c) =>
            c.GetComponentInParent<NetworkIdentity>() || c.GetComponentInParent<IInteractable>() != null || c.GetComponentInParent<SpawnPoint>();

        /// <summary>Hides the stage's own ground and props; its sky, light, fog and networked things stay.</summary>
        static void HideStage(GameObject[] roots)
        {
            int renderers = 0, colliders = 0;
            foreach (var root in roots)
            {
                if (root.name.StartsWith("RiskOfBonk")) continue;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    if (r.enabled && !KeepRenderer(r))
                    {
                        r.enabled = false;
                        renderers++;
                        if (r is ParticleSystemRenderer && r.GetComponent<ParticleSystem>() is ParticleSystem ps) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
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

        static float Inner => profile != null && profile.noise ? half - 6f : half - tile; // inside the edge walls

        /// <summary>Fixed interactables of the stage (newt altars and the like) are set down on the new ground.</summary>
        static void SettleInteractables(GameObject[] roots, System.Random rng)
        {
            float lim = Inner - 6f;
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
        static readonly List<Vector3> blockers = new List<Vector3>(); // (x, z, radius) of colliding props, for the node graphs

        static void BuildTerrain()
        {
            var sw = Stopwatch.StartNew();
            ulong seed = Seed();
            var rng = new System.Random((int)(seed & 0x7fffffff) ^ (int)(seed >> 33));
            profile = ProfileFor(Run.instance.stageClearCount);
            if (profile.noise) BuildNoise(rng); else BuildCells(rng);
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();

            // Centre the new map on the players' spawn so the stage's sky and fog sit right.
            var spawn = SpawnPoint.readOnlyInstancesList.Count > 0 ? SpawnPoint.readOnlyInstancesList[0].transform.position : Vector3.zero;
            Center = spawn - Vector3.up * 1f;

            blockers.Clear();
            HideStage(roots);
            foreach (var zone in Object.FindObjectsOfType<MapZone>()) zone.enabled = false;

            terrainRoot = new GameObject("RiskOfBonkTerrain");
            SceneManager.MoveGameObjectToScene(terrainRoot, scene);
            terrainRoot.transform.position = Center;
            terrainRoot.layer = WorldLayer;
            int[] subIds;
            var mesh = profile.noise ? NoiseMesh(out subIds) : TerrainMesh(out subIds);
            var fillKey = profile.fillFormat.ToString();
            var mats = profile.noise
                ? new[] { TileMat(profile.sand + profile.sandFormat, new Color(0.9f, 0.86f, 0.78f)), TileMat(profile.sandSide + profile.sideFormat, new Color(0.8f, 0.78f, 0.74f)) }
                : subIds.Select(s => s == SubFillTop ? TileMat(profile.fillTop + fillKey, new Color(0.77f, 0.77f, 0.77f))
                    : s == SubFillMiddle ? TileMat(profile.fillMiddle + fillKey, new Color(0.77f, 0.77f, 0.77f))
                    : TileMat(profile.tops[s], Color.white)).ToArray();
            terrainRoot.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = terrainRoot.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            terrainRoot.AddComponent<MeshCollider>().sharedMesh = mesh;
            if (profile.noise) EdgeWalls();
            Physics.SyncTransforms();

            int dressed = profile.noise ? 0 : DressTiles(rng);
            int props = 0;
            foreach (var rule in profile.props) props += Scatter(rule, rng);

            SettleInteractables(roots, rng);
            // Players start on the first tile of the maze, in the middle.
            int i = 0;
            foreach (var sp in SpawnPoint.readOnlyInstancesList)
            {
                float a = i++ * 2.4f;
                var local = new Vector3(Mathf.Sin(a) * 4f, 0f, Mathf.Cos(a) * 4f);
                sp.transform.position = Center + local + Vector3.up * (Height(local.x, local.z) + 0.5f);
            }
            Physics.SyncTransforms();
            try { Sky(); } catch (System.Exception e) { Log.Warn($"Bonk style sky: {e.Message}"); }
            Active = true;
            Log.Info(profile.noise
                ? $"Bonk style: Megabonk {profile.name} map, noise mesh {NoiseVerts}x{NoiseVerts} at {spacing} m ({half * 2f:0} m), heights {lowest:0.#}..{highest:0.#} m, {props} props, built in {sw.ElapsedMilliseconds} ms"
                : $"Bonk style: Megabonk {profile.name} map, {profile.size}x{profile.size} tiles of {tile} m ({profile.size * tile} m), levels {minLevel}..{maxLevel} x {step} m, " +
                  $"{props} props, {dressed} dressed tiles, built in {sw.ElapsedMilliseconds} ms");
        }

        /// <summary>
        /// Megabonk's StageData lighting on this screen: its fog colour (the distance fog and the sky take it), its sun colour
        /// and ambience, and a neutral colour grade instead of the stage's own tint (Megabonk's look adds saturation on top).
        /// </summary>
        static void Sky()
        {
            var fog = profile.fog;
            int volumes = 0;
            foreach (var volume in Object.FindObjectsOfType<PostProcessVolume>())
            {
                if (!volume || !volume.sharedProfile || volume.gameObject.scene != SceneManager.GetActiveScene()) continue;
                var p = volume.profile;
                // Megabonk's fog is Unity's exponential distance fog (StageData.fogIntensity, fogColor), not the stage's ramp fog.
                if (p.TryGetSettings<RampFog>(out var rf))
                {
                    if (TestKeys.On) Log.Info($"Bonk style: ramp fog on {volume.name}: intensity {rf.fogIntensity.value} power {rf.fogPower.value} zero {rf.fogZero.value} one {rf.fogOne.value} " +
                        $"height {rf.fogHeightStart.value}..{rf.fogHeightEnd.value} x{rf.fogHeightIntensity.value} start {rf.fogColorStart.value} mid {rf.fogColorMid.value} end {rf.fogColorEnd.value} sky {rf.skyboxStrength.value} weight {volume.weight} global {volume.isGlobal}");
                    rf.enabled.Override(false);
                }
                // The stage's own mood (night, haze, tints) goes: Megabonk's maps are graded only by its light and fog.
                if (p.TryGetSettings<ColorGrading>(out var cg)) cg.enabled.Override(false);
                if (p.TryGetSettings<Vignette>(out var vig)) vig.enabled.Override(false);
                volumes++;
            }
            // Daylight like Megabonk's maps: one sun from high up in the stage's light colour, the flat ambience instead of the
            // stage's baked light probes (night stages would leave everything dark), and a sky fading into the fog colour.
            bool sun = false;
            foreach (var l in Object.FindObjectsOfType<Light>())
                if (l.type == LightType.Directional && l.gameObject.scene == SceneManager.GetActiveScene())
                {
                    if (sun) { l.enabled = false; continue; }
                    sun = true;
                    l.color = profile.light;
                    l.intensity = 1.25f;
                    l.shadows = LightShadows.Soft;
                    l.shadowStrength = 0.55f; // Megabonk's toon shading keeps shade readable
                    l.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
                }
            if (sun)
            {
                // Megabonk's flat ambience lights every face the sun misses; Risk of Rain 2's deferred shading takes its ambient
                // from the stage's own probes, so a soft shadowless fill light from the other side stands in for it.
                var fill = new GameObject("BonkFillLight").AddComponent<Light>();
                SceneManager.MoveGameObjectToScene(fill.gameObject, SceneManager.GetActiveScene());
                fill.type = LightType.Directional;
                fill.color = profile.ambience;
                fill.intensity = 0.55f;
                fill.shadows = LightShadows.None;
                fill.transform.rotation = Quaternion.Euler(25f, 145f, 0f);
            }
            LightmapSettings.lightProbes = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = profile.ambience;
            RenderSettings.ambientEquatorColor = profile.ambience * 0.9f;
            RenderSettings.ambientGroundColor = profile.ambience * 0.7f;
            RenderSettings.ambientLight = profile.ambience;
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(profile.ambience * 0.85f);
            RenderSettings.ambientProbe = sh;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = profile.fogDensity;
            RenderSettings.fogColor = fog;
            var old = RenderSettings.skybox;
            if (old && old.shader.name == "Skybox/Cubemap")
            {
                var m = new Material(old.shader) { name = "BonkSky" };
                m.SetTexture("_Tex", SkyCube(fog));
                m.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f)); // the cubemap shader doubles its tint
                m.SetFloat("_Exposure", 1f);
                RenderSettings.skybox = m;
            }
            foreach (var cam in Object.FindObjectsOfType<Camera>()) if (TestKeys.On) Log.Info($"Bonk style: camera {cam.name} clear {cam.clearFlags} bg {cam.backgroundColor} far {cam.farClipPlane}");
            Log.Info($"Bonk style: Megabonk {profile.name} lighting on {volumes} volumes");
        }

        static Cubemap skyCube;
        static Color skyFor;

        /// <summary>A plain sky: the fog colour at the horizon (and below), a clearer blue overhead.</summary>
        static Cubemap SkyCube(Color fog)
        {
            if (skyCube && skyFor == fog) return skyCube;
            const int n = 32;
            if (!skyCube) skyCube = new Cubemap(n, TextureFormat.RGBA32, false) { name = "BonkSkyCube" };
            skyFor = fog;
            var zenith = Color.Lerp(fog, new Color(0.45f, 0.72f, 1f), 0.55f);
            var horizon = Color.Lerp(fog, Color.white, 0.25f);
            var px = new Color[n * n];
            foreach (CubemapFace face in new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ })
            {
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                        Vector3 d;
                        switch (face)
                        {
                            case CubemapFace.PositiveX: d = new Vector3(1, -v, -u); break;
                            case CubemapFace.NegativeX: d = new Vector3(-1, -v, u); break;
                            case CubemapFace.PositiveY: d = new Vector3(u, 1, v); break;
                            case CubemapFace.NegativeY: d = new Vector3(u, -1, -v); break;
                            case CubemapFace.PositiveZ: d = new Vector3(u, -v, 1); break;
                            default: d = new Vector3(-u, -v, -1); break;
                        }
                        float up = d.normalized.y;
                        px[y * n + x] = up <= 0f ? horizon : Color.Lerp(horizon, zenith, Mathf.Pow(up, 0.6f));
                    }
                skyCube.SetPixels(px, face);
            }
            skyCube.Apply();
            return skyCube;
        }

        static bool NearSpawn(float x, float z) => x * x + z * z < (tile * 0.6f) * (tile * 0.6f);

        /// <summary>Megabonk's GenerateTileObjects: a quarter of the flat tiles get one of the stage's pre-made dressings.</summary>
        static int DressTiles(System.Random rng)
        {
            int n = 0;
            for (int x = 1; x < N - 1; x++)
                for (int z = 1; z < N - 1; z++)
                {
                    var c = cells[x, z];
                    if (c.slope || rng.NextDouble() >= profile.populateTiles) continue;
                    float cx = -half + (x + 0.5f) * tile, cz = -half + (z + 0.5f) * tile;
                    if (NearSpawn(cx, cz)) continue;
                    string name = profile.tilePrefabs[rng.Next(profile.tilePrefabs.Length)];
                    if (!PropModel(name, out var mesh, out var mats)) continue;
                    var go = new GameObject("BonkTile_" + name) { layer = WorldLayer };
                    go.transform.SetParent(terrainRoot.transform, false);
                    go.transform.localPosition = new Vector3(cx, c.level * step, cz);
                    go.transform.localRotation = Quaternion.Euler(0f, 90f * rng.Next(4), 0f);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterials = mats;
                    go.AddComponent<MeshCollider>().sharedMesh = mesh; // ruins and castles: walk on and around them
                    n++;
                }
            return n;
        }

        static readonly List<Vector4> placed = new List<Vector4>();

        /// <summary>Megabonk's RandomObjectPlacer: amount objects at random spots on the map, at least checkRadius x scale apart.</summary>
        static int Scatter(PropRule rule, System.Random rng)
        {
            int n = 0;
            float lim = Inner - 2f;
            for (int tries = 0; tries < rule.amount * 6 && n < rule.amount; tries++)
            {
                float x = ((float)rng.NextDouble() * 2f - 1f) * lim, z = ((float)rng.NextDouble() * 2f - 1f) * lim;
                string name = rule.prefabs[rng.Next(rule.prefabs.Length)];
                float s = Mathf.Lerp(rule.scaleMin, rule.scaleMax, (float)rng.NextDouble());
                float yaw = (float)rng.NextDouble() * 360f;
                if (NearSpawn(x, z)) continue;
                float r = Mathf.Max(0.5f, rule.checkRadius * s);
                bool free = true;
                foreach (var p in placed) if ((new Vector2(p.x - x, p.z - z)).sqrMagnitude < (p.w + r) * (p.w + r) * 0.25f) { free = false; break; }
                if (!free) continue;
                if (!PropModel(name, out var mesh, out var mats)) continue;
                float y = Height(x, z);
                var go = new GameObject("BonkProp_" + name) { layer = WorldLayer };
                go.transform.SetParent(terrainRoot.transform, false);
                // (Megabonk drops its boulders from upOffset metres up to roll down; here they rest on the ground.)
                go.transform.localPosition = new Vector3(x, y, z);
                var up = rule.align ? GroundNormal(x, z) : Vector3.up;
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0f, yaw, 0f);
                go.transform.localScale = Vector3.one * s;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = mats;
                float blockRadius = AddColliders(go, rawProps[name], mesh, s);
                if (blockRadius > 0f) blockers.Add(new Vector3(x, z, blockRadius));
                placed.Add(new Vector4(x, y, z, r));
                n++;
            }
            return n;
        }

        /// <summary>
        /// Colliders like Megabonk's (a trunk capsule, a boulder sphere), smooth primitives so nobody snags on them.
        /// Props without a collider in Megabonk (bushes, small rocks) stay walk-through; rocks get a sphere only when big
        /// enough to stand in the way, sunk into the ground so you slide around and over their edge. Returns the radius
        /// that blocks monsters' paths (0: none).
        /// </summary>
        static float AddColliders(GameObject go, RawProp p, Mesh mesh, float s)
        {
            if (p.Colliders.Count > 0)
            {
                float block = 0f;
                foreach (var c in p.Colliders)
                {
                    var center = new Vector3(c.Center[0], c.Center[1], c.Center[2]);
                    switch (c.Kind)
                    {
                        case 0:
                            var sph = go.AddComponent<SphereCollider>(); sph.center = center; sph.radius = c.Radius;
                            block = Mathf.Max(block, c.Radius * s);
                            break;
                        case 1:
                            var cap = go.AddComponent<CapsuleCollider>(); cap.center = center; cap.radius = c.Radius; cap.height = c.Height;
                            var axis = new Vector3(c.Axis[0], c.Axis[1], c.Axis[2]);
                            float ax = Mathf.Abs(axis.x), ay = Mathf.Abs(axis.y), az = Mathf.Abs(axis.z);
                            cap.direction = ay >= ax && ay >= az ? 1 : ax >= az ? 0 : 2;
                            block = Mathf.Max(block, c.Radius * s);
                            break;
                        default:
                            var box = go.AddComponent<BoxCollider>(); box.center = center; box.size = new Vector3(c.Size[0], c.Size[1], c.Size[2]);
                            block = Mathf.Max(block, Mathf.Max(c.Size[0], c.Size[2]) * 0.5f * s);
                            break;
                    }
                }
                return block;
            }
            if (p.Name.StartsWith("rock_") || p.Name.StartsWith("Rock"))
            {
                var b = mesh.bounds;
                float r = Mathf.Max(b.extents.x, b.extents.z) * s;
                if (r < 1.2f) return 0f; // pebbles: walk through
                var sph = go.AddComponent<SphereCollider>();
                sph.radius = Mathf.Max(b.extents.x, b.extents.z) * 0.9f;
                sph.center = new Vector3(b.center.x, b.min.y + b.extents.y * 0.6f - sph.radius * 0.35f, b.center.z);
                return r;
            }
            // Buildings and big rock formations: walk on and around them (fences, arches, ruins, desert houses and landmarks).
            if (p.MeshCollider && (p.Name.StartsWith("Fence") || p.Name == "Arch" || p.Name.StartsWith("Ruins") || p.Name.StartsWith("TilePrefabHouse")
                                   || p.Name.StartsWith("Landmark") || p.Name.StartsWith("Ladnmark")))
            {
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                return Mathf.Max(mesh.bounds.extents.x, mesh.bounds.extents.z) * s;
            }
            return 0f;
        }

        /// <summary>
        /// Host: new ground and air node graphs on the new terrain. The editor's bake tests every link with physics; the
        /// terrain is a known heightfield, so links, line of sight and Teleporter spots are worked out from the heights,
        /// with neighbours found through a grid so the big maps bake quickly.
        /// </summary>
        static void BakeGraphs()
        {
            var sw = Stopwatch.StartNew();
            var info = SceneInfo.instance;
            float lim = Inner - 4f;
            const int AllHulls = (int)(HullMask.Human | HullMask.Golem | HullMask.BeetleQueen);

            bool Clear(Vector3 a, Vector3 b, float above, float stepLen = 3f)
            {
                int steps = Mathf.CeilToInt(Vector3.Distance(a, b) / stepLen);
                for (int k = 1; k < steps; k++)
                {
                    var p = Vector3.Lerp(a, b, (float)k / steps);
                    if (GridHeight(p.x, p.z) > p.y - above) return false;
                }
                return true;
            }

            NodeGraph Bake(string name, List<Vector3> spots, float spacing, float linkRange, bool ground, out GameObject go)
            {
                go = new GameObject(name);
                var nodes = new List<MapNode>(spots.Count);
                var grid = new Dictionary<(int, int), List<int>>();
                for (int k = 0; k < spots.Count; k++)
                {
                    var n = new GameObject("MapNode");
                    n.transform.SetParent(go.transform, false);
                    n.transform.position = Center + spots[k];
                    var node = n.AddComponent<MapNode>();
                    node.flags = NodeFlags.NoCeiling;
                    nodes.Add(node);
                    var key = (Mathf.FloorToInt(spots[k].x / spacing), Mathf.FloorToInt(spots[k].z / spacing));
                    if (!grid.TryGetValue(key, out var l)) grid[key] = l = new List<int>();
                    l.Add(k);
                }
                IEnumerable<int> Near(Vector3 p, float range)
                {
                    int r = Mathf.CeilToInt(range / spacing), cx = Mathf.FloorToInt(p.x / spacing), cz = Mathf.FloorToInt(p.z / spacing);
                    for (int i = -r; i <= r; i++)
                        for (int j = -r; j <= r; j++)
                            if (grid.TryGetValue((cx + i, cz + j), out var l)) foreach (var k in l) yield return k;
                }
                for (int a = 0; a < spots.Count; a++)
                {
                    foreach (int b in Near(spots[a], linkRange))
                    {
                        if (a == b) continue;
                        var d = spots[b] - spots[a];
                        float flat = new Vector2(d.x, d.z).magnitude;
                        if (flat > linkRange) continue;
                        // Ground: walkable if the slope is under 45 degrees and the ground in between has no bump (cliffs block).
                        bool ok = ground ? Mathf.Abs(d.y) <= flat && Clear(spots[a] + Vector3.up * 2f, spots[b] + Vector3.up * 2f, 0f)
                            : Clear(spots[a], spots[b], 3f, 6f);
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
                // Line of sight, once per pair, up to 100 m (farther counts as out of sight).
                var masks = new List<SerializableBitArray>(spots.Count);
                for (int a = 0; a < spots.Count; a++) masks.Add(new SerializableBitArray(spots.Count));
                for (int a = 0; a < spots.Count; a++)
                {
                    masks[a][a] = true;
                    foreach (int b in Near(spots[a], 100f))
                    {
                        if (b <= a || (spots[b] - spots[a]).sqrMagnitude > 100f * 100f) continue;
                        if (Clear(spots[a] + Vector3.up, spots[b] + Vector3.up, 0f, 8f)) { masks[a][b] = true; masks[b][a] = true; }
                    }
                }
                var graph = ScriptableObject.CreateInstance<NodeGraph>();
                graph.SetNodes(nodes.AsReadOnly(), masks.AsReadOnly());
                go.SetActive(false);
                return graph;
            }

            bool Blocked(float x, float z, float margin)
            {
                foreach (var b in blockers) if ((new Vector2(b.x - x, b.y - z)).sqrMagnitude < (b.z + margin) * (b.z + margin)) return true;
                return false;
            }

            // Ground nodes every 10 m (12 m on the big noise maps, so their bake stays quick).
            float gs = profile.noise ? 12f : 10f;
            var groundSpots = new List<Vector3>();
            for (float x = -lim; x <= lim; x += gs)
                for (float z = -lim; z <= lim; z += gs)
                {
                    float y = GridHeight(x, z);
                    // Not on a cliff edge: the ground 2 m around must be about as high.
                    bool edge = false;
                    foreach (var o in new[] { new Vector2(2, 0), new Vector2(-2, 0), new Vector2(0, 2), new Vector2(0, -2) })
                        if (Mathf.Abs(GridHeight(x + o.x, z + o.y) - y) > 1.6f) { edge = true; break; }
                    if (edge || Blocked(x, z, 1.5f)) continue;
                    groundSpots.Add(new Vector3(x, y, z));
                }
            var airSpots = new List<Vector3>();
            for (float x = -lim; x <= lim; x += 20f)
                for (float z = -lim; z <= lim; z += 20f)
                {
                    float y = GridHeight(x, z);
                    airSpots.Add(new Vector3(x, y + 12f, z));
                    airSpots.Add(new Vector3(x, y + 26f, z));
                }
            var groundGraph = Bake("RiskOfBonkGroundNodes", groundSpots, gs, gs * 1.45f, true, out var groundGo);
            var airGraph = Bake("RiskOfBonkAirNodes", airSpots, 20f, 29f, false, out var airGo);

            info.groundNodes = groundGraph;
            info.airNodes = airGraph;
            info.groundNodeGroup = groundGo.AddComponent<MapNodeGroup>();
            info.airNodeGroup = airGo.AddComponent<MapNodeGroup>();
            Log.Info($"Bonk style: node graphs built in {sw.ElapsedMilliseconds} ms ({groundSpots.Count} ground, {airSpots.Count} air)");
        }

        /// <summary>Ends the spawn's fall damage immunity after a few seconds.</summary>
        public sealed class SoftLandingTimer : MonoBehaviour
        {
            float until;
            void Start() => until = Time.time + 4f;
            void Update()
            {
                if (Time.time < until) return;
                var b = GetComponent<CharacterBody>();
                if (b) b.bodyFlags &= ~CharacterBody.BodyFlags.IgnoreFallDamage;
                Destroy(this);
            }
        }

        /// <summary>Anyone who falls off the map comes back to the start.</summary>
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
                    float floorY = profile != null && profile.noise ? lowest - 30f : (minLevel - 3) * step;
                    bool outside = p.y < floorY || Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z)) > half + 20f;
                    if (outside) TeleportHelper.TeleportBody(b, Center + Vector3.up * (Height(0f, 0f) + 3f));
                }
            }
        }

        public static void Init(Harmony harmony, GameObject host)
        {
            SceneManager.activeSceneChanged += (a, b) => { Active = false; placed.Clear(); noiseH = null; };
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
                    placed.Clear();
                    BuildTerrain();
                    if (NetworkServer.active) BakeGraphs();
                }
                catch (System.Exception e) { Log.Error($"Bonk style terrain: {e}"); }
            }

            // The stage's title card names the Megabonk map instead of the Risk of Rain 2 stage it replaced.
            [HarmonyPostfix, HarmonyPatch(typeof(RoR2.UI.AssignStageToken), "Start")]
            static void Title(RoR2.UI.AssignStageToken __instance)
            {
                if (TestKeys.On) Log.Info($"stage title: active {Active}, map {MapName}");
                if (!Active || profile == null) return;
                if (__instance.titleText) __instance.titleText.text = profile.name;
                if (__instance.subtitleText) __instance.subtitleText.text = profile.noise ? "Megabonk's dunes" : profile.name == "Graveyard" ? "Megabonk's crypts" : "Megabonk's woods";
            }

            // Megabonk drops you straight onto the map: no drop pod on Bonk style stages (host side).
            [HarmonyPrefix, HarmonyPatch(typeof(Stage), nameof(Stage.RespawnCharacter))]
            static void NoPod(Stage __instance)
            {
                if (StageMode.IsBonk && __instance.usePod) __instance.usePod = false;
            }

            // Without the pod a player appears a little above the ground and took fall damage on landing: no fall damage
            // for the first seconds on a Bonk style stage.
            [HarmonyPostfix, HarmonyPatch(typeof(CharacterBody), "Start")]
            static void SoftLanding(CharacterBody __instance)
            {
                if (!StageMode.IsBonk || !__instance.isPlayerControlled || (__instance.bodyFlags & CharacterBody.BodyFlags.IgnoreFallDamage) != 0) return;
                __instance.bodyFlags |= CharacterBody.BodyFlags.IgnoreFallDamage;
                __instance.gameObject.AddComponent<SoftLandingTimer>();
            }
        }
    }
}
