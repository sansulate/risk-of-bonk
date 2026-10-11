using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace RiskOfBonk
{
    /// <summary>Raw texture pixels exactly as Megabonk stores them (format is Unity's TextureFormat number).</summary>
    public sealed class RawTexture
    {
        public string Name;
        public int Width, Height, Format, MipCount;
        public byte[] Data;
    }

    /// <summary>One sub-mesh of a Megabonk model, already moved into the model's root space.</summary>
    public sealed class RawSubMesh
    {
        public int[] Triangles;
        public RawTexture Texture;
        public float[] Color = { 1, 1, 1, 1 };
    }

    /// <summary>A whole Megabonk model: every mesh renderer under one GameObject merged into one vertex list.</summary>
    public sealed class RawModel
    {
        public string SourceName;
        public readonly List<float> Positions = new List<float>(); // xyz
        public readonly List<float> Normals = new List<float>();   // xyz
        public readonly List<float> UVs = new List<float>();       // uv
        public readonly List<float> Colors = new List<float>();    // rgba
        public readonly List<RawSubMesh> SubMeshes = new List<RawSubMesh>();
        public int VertexCount => Positions.Count / 3;
    }

    /// <summary>
    /// Reads the player's own Megabonk install (an IL2CPP Unity game) with AssetsTools.NET. Only built-in Unity types are
    /// read (GameObject, Transform, MeshRenderer/SkinnedMeshRenderer, Mesh, Material, Texture2D, Sprite), which need no
    /// game code. Nothing is copied to disk: data lives in memory for the session.
    /// Runs on a worker thread; it never touches UnityEngine.
    /// </summary>
    public sealed class MegabonkLibrary
    {
        const int GameObjectId = 1, TransformId = 4, MeshRendererId = 23, MeshFilterId = 33, MeshId = 43,
                  MaterialId = 21, Texture2DId = 28, SkinnedMeshRendererId = 137, SpriteId = 213, RectTransformId = 224;

        public sealed class Entry
        {
            public AssetsFileInstance File;
            public AssetFileInfo Info;
            public string Name;
            public int Type;
        }

        readonly AssetsManager am = new AssetsManager();
        readonly List<AssetsFileInstance> files = new List<AssetsFileInstance>();
        readonly Dictionary<AssetsFileInstance, BundleFileInstance> bundleOf = new Dictionary<AssetsFileInstance, BundleFileInstance>();
        public readonly List<Entry> Entries = new List<Entry>();
        public string DataDir { get; private set; }
        public string UnityVersion { get; private set; }
        readonly Action<string> log;

        public MegabonkLibrary(Action<string> log) { this.log = log; }

        public void Open(string gameDir, string tpkPath)
        {
            DataDir = Directory.GetDirectories(gameDir, "*_Data").FirstOrDefault(d => File.Exists(Path.Combine(d, "globalgamemanagers")))
                      ?? throw new DirectoryNotFoundException("No Unity *_Data folder with globalgamemanagers in " + gameDir);
            am.UseTemplateFieldCache = true;
            am.UseQuickLookup = true;
            am.LoadClassPackage(tpkPath);

            var paths = new List<string> { Path.Combine(DataDir, "globalgamemanagers") };
            paths.AddRange(Directory.GetFiles(DataDir, "level*").Where(p => !p.EndsWith(".resS")));
            paths.AddRange(Directory.GetFiles(DataDir, "*.assets"));
            foreach (var p in paths)
            {
                try { files.Add(am.LoadAssetsFile(p, false)); }
                catch (Exception e) { log($"skip {Path.GetFileName(p)}: {e.Message}"); }
            }
            // Addressables / AssetBundles, if the game ships content that way.
            string aa = Path.Combine(DataDir, "StreamingAssets");
            if (Directory.Exists(aa))
                foreach (var b in Directory.GetFiles(aa, "*.bundle", SearchOption.AllDirectories))
                {
                    try
                    {
                        var bun = am.LoadBundleFile(b, true);
                        for (int i = 0; i < bun.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
                        {
                            string n = bun.file.GetFileName(i);
                            if (n.EndsWith(".resS") || n.EndsWith(".resource")) continue;
                            var inst = am.LoadAssetsFileFromBundle(bun, i, false);
                            if (inst != null) { files.Add(inst); bundleOf[inst] = bun; }
                        }
                    }
                    catch (Exception e) { log($"skip bundle {Path.GetFileName(b)}: {e.Message}"); }
                }
            if (files.Count == 0) throw new IOException("No readable Megabonk asset files in " + DataDir);
            UnityVersion = files[0].file.Metadata.UnityVersion;
            am.LoadClassDatabaseFromPackage(UnityVersion);

            int[] wanted = { GameObjectId, MeshId, Texture2DId, SpriteId };
            foreach (var f in files)
                foreach (var info in f.file.AssetInfos)
                {
                    int t = info.TypeId;
                    if (Array.IndexOf(wanted, t) < 0) continue;
                    string name;
                    try { name = AssetHelper.GetAssetNameFast(f.file, am.ClassDatabase, info); } catch { continue; }
                    Entries.Add(new Entry { File = f, Info = info, Name = name ?? "", Type = t });
                }
            log($"Megabonk {UnityVersion}: {files.Count} asset files, {Entries.Count} named models/meshes/textures/sprites");
        }

        public void WriteInventory(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Megabonk asset names (Unity {UnityVersion}) - used to check sheets/characters.json mbNameHints");
            foreach (var g in Entries.GroupBy(e => e.Type).OrderBy(g => g.Key))
            {
                sb.AppendLine($"## type {g.Key} ({TypeName(g.Key)}): {g.Count()}");
                foreach (var n in g.Select(e => e.Name).Where(n => n.Length > 0).Distinct().OrderBy(n => n)) sb.AppendLine(n);
            }
            File.WriteAllText(path, sb.ToString());
        }

        static string TypeName(int t) => t switch { 1 => "GameObject", 43 => "Mesh", 28 => "Texture2D", 213 => "Sprite", _ => t.ToString() };

        static int Score(string name, string[] hints)
        {
            int best = 0;
            string n = name.Replace(" ", "").Replace("_", "").ToLowerInvariant();
            foreach (var h0 in hints)
            {
                string h = h0.Replace(" ", "").Replace("_", "").ToLowerInvariant();
                if (n == h) best = Math.Max(best, 100);
                else if (n.StartsWith(h)) best = Math.Max(best, 60);
                else if (n.Contains(h)) best = Math.Max(best, 30);
            }
            return best;
        }

        // ---------------- models ----------------

        /// <summary>Best GameObject matching the hints whose hierarchy holds mesh renderers (most vertices wins ties).</summary>
        public RawModel FindModel(string[] hints)
        {
            var candidates = Entries.Where(e => e.Type == GameObjectId)
                .Select(e => (e, s: Score(e.Name, hints))).Where(x => x.s > 0)
                .OrderByDescending(x => x.s).Take(40).ToList();
            RawModel best = null; int bestScore = -1;
            foreach (var (e, s) in candidates)
            {
                RawModel m;
                try { m = BuildModel(e); } catch (Exception ex) { log($"model {e.Name}: {ex.Message}"); continue; }
                if (m == null || m.VertexCount == 0) continue;
                // Prefer name score, then size; ignore tiny props (icons, pickups) when a fuller body exists.
                int score = s * 100000 + Math.Min(m.VertexCount, 99999);
                if (score > bestScore) { best = m; bestScore = score; }
            }
            if (best == null)
            {
                // No GameObject: fall back to a bare mesh with the best name.
                var me = Entries.Where(e => e.Type == MeshId).Select(e => (e, s: Score(e.Name, hints))).Where(x => x.s > 0)
                    .OrderByDescending(x => x.s).FirstOrDefault().e;
                if (me != null)
                {
                    best = new RawModel { SourceName = me.Name };
                    AppendMesh(best, am.GetBaseField(me.File, me.Info), me.File, Matrix.Identity, new List<RawTexture>(), new List<float[]>());
                    var tex = FindTexture(hints);
                    foreach (var sm in best.SubMeshes) sm.Texture = tex;
                }
            }
            return best;
        }

        RawModel BuildModel(Entry go)
        {
            var model = new RawModel { SourceName = go.Name };
            var goField = am.GetBaseField(go.File, go.Info);
            var rootTf = Component(go.File, goField, TransformId);
            if (rootTf.baseField == null) rootTf = Component(go.File, goField, RectTransformId);
            if (rootTf.baseField == null) return null;
            Walk(rootTf, Matrix.Identity, model, 0);
            return model;
        }

        AssetExternal Component(AssetsFileInstance file, AssetTypeValueField goField, int typeId)
        {
            foreach (var c in goField["m_Component.Array"].Children)
            {
                var ext = am.GetExtAsset(file, c["component"], true);
                if (ext.info != null && ext.info.TypeId == typeId)
                    return am.GetExtAsset(file, c["component"]);
            }
            return default;
        }

        void Walk(AssetExternal tf, Matrix parent, RawModel model, int depth)
        {
            if (depth > 64 || model.VertexCount > 200000) return;
            var t = tf.baseField;
            var local = Matrix.TRS(V3(t["m_LocalPosition"]), Q(t["m_LocalRotation"]), V3(t["m_LocalScale"]));
            // The root's own placement in its scene is ignored: the model is rebuilt around its origin. Its tilt is
            // kept though: imported FBX roots often carry the -90 deg X axis conversion, without which the model lies along Z.
            var world = depth == 0 ? Matrix.TRS(new float[3], Tilt(Q(t["m_LocalRotation"])), V3(t["m_LocalScale"])) : parent * local;
            var goExt = am.GetExtAsset(tf.file, t["m_GameObject"]);
            if (goExt.baseField != null && goExt.baseField["m_IsActive"].AsBool)
            {
                var goField = goExt.baseField;
                var smr = Component(goExt.file, goField, SkinnedMeshRendererId);
                if (smr.baseField != null)
                {
                    var mesh = am.GetExtAsset(smr.file, smr.baseField["m_Mesh"]);
                    if (mesh.baseField != null) AppendRenderer(model, mesh, smr, world);
                }
                else
                {
                    var mr = Component(goExt.file, goField, MeshRendererId);
                    var mf = Component(goExt.file, goField, MeshFilterId);
                    if (mr.baseField != null && mf.baseField != null)
                    {
                        var mesh = am.GetExtAsset(mf.file, mf.baseField["m_Mesh"]);
                        if (mesh.baseField != null) AppendRenderer(model, mesh, mr, world);
                    }
                }
            }
            foreach (var child in t["m_Children.Array"].Children)
            {
                var c = am.GetExtAsset(tf.file, child);
                if (c.baseField != null) Walk(c, world, model, depth + 1);
            }
        }

        void AppendRenderer(RawModel model, AssetExternal mesh, AssetExternal renderer, Matrix world)
        {
            if (!renderer.baseField["m_Enabled"].AsBool) return;
            var texs = new List<RawTexture>();
            var colors = new List<float[]>();
            foreach (var m in renderer.baseField["m_Materials.Array"].Children)
            {
                var mat = am.GetExtAsset(renderer.file, m);
                texs.Add(mat.baseField != null ? MainTexture(mat) : null);
                colors.Add(mat.baseField != null ? MainColor(mat.baseField) : new float[] { 1, 1, 1, 1 });
            }
            AppendMesh(model, mesh.baseField, mesh.file, world, texs, colors);
        }

        static readonly string[] TexPrefs = { "_MainTex", "_BaseMap", "_BaseColorMap", "_Albedo", "_MainTexture", "_Texture", "_Diffuse", "_ColorMap" };

        RawTexture MainTexture(AssetExternal mat)
        {
            var envs = mat.baseField["m_SavedProperties.m_TexEnvs.Array"].Children;
            AssetTypeValueField pick = null;
            foreach (var pref in TexPrefs)
            {
                pick = envs.FirstOrDefault(e => e["first"].AsString == pref && e["second.m_Texture.m_PathID"].AsLong != 0);
                if (pick != null) break;
            }
            pick ??= envs.FirstOrDefault(e => e["second.m_Texture.m_PathID"].AsLong != 0 &&
                                              !e["first"].AsString.ToLowerInvariant().Contains("normal") &&
                                              !e["first"].AsString.ToLowerInvariant().Contains("bump"));
            if (pick == null) return null;
            var tex = am.GetExtAsset(mat.file, pick["second.m_Texture"]);
            return tex.baseField != null && tex.info.TypeId == Texture2DId ? ReadTexture(tex.file, tex.baseField) : null;
        }

        static float[] MainColor(AssetTypeValueField mat)
        {
            foreach (var c in mat["m_SavedProperties.m_Colors.Array"].Children)
            {
                string n = c["first"].AsString;
                if (n == "_Color" || n == "_BaseColor" || n == "_MainColor")
                    return new[] { c["second.r"].AsFloat, c["second.g"].AsFloat, c["second.b"].AsFloat, c["second.a"].AsFloat };
            }
            return new float[] { 1, 1, 1, 1 };
        }

        // Unity 2019+ VertexFormat sizes.
        static readonly int[] FormatSize = { 4, 2, 1, 1, 2, 2, 1, 1, 2, 2, 4, 4 };

        void AppendMesh(RawModel model, AssetTypeValueField mesh, AssetsFileInstance file, Matrix world, List<RawTexture> texs, List<float[]> colors)
        {
            if (!mesh["m_MeshCompression"].IsDummy && mesh["m_MeshCompression"].AsByte != 0)
            {
                log($"mesh {mesh["m_Name"].AsString}: compressed meshes are not supported, skipped");
                return;
            }
            var vd = mesh["m_VertexData"];
            int vcount = (int)vd["m_VertexCount"].AsUInt;
            byte[] data = vd["m_DataSize"].AsByteArray;
            if ((data == null || data.Length == 0) && !mesh["m_StreamData"].IsDummy)
                data = ReadStream(file, mesh["m_StreamData"]);
            if (data == null || data.Length == 0 || vcount == 0) return;

            var channels = vd["m_Channels.Array"].Children
                .Select(c => (stream: c["stream"].AsByte, offset: c["offset"].AsByte, format: c["format"].AsByte, dim: c["dimension"].AsByte & 0xF))
                .ToList();
            int streams = channels.Count == 0 ? 0 : channels.Max(c => c.stream) + 1;
            var strides = new int[streams];
            for (int s = 0; s < streams; s++)
                strides[s] = channels.Where(c => c.stream == s && c.dim > 0).Select(c => c.offset + FormatSize[c.format] * c.dim).DefaultIfEmpty(0).Max();
            var starts = new int[streams];
            int pos = 0;
            for (int s = 0; s < streams; s++)
            {
                starts[s] = pos;
                pos += strides[s] * vcount;
                pos = (pos + 15) & ~15;
            }

            float[] Read(int ch, int v, int comp)
            {
                var c = channels[ch];
                var r = new float[comp];
                if (c.dim == 0) return null;
                int at = starts[c.stream] + v * strides[c.stream] + c.offset;
                for (int k = 0; k < Math.Min(comp, c.dim); k++)
                    r[k] = ReadValue(data, at + k * FormatSize[c.format], c.format);
                return r;
            }

            int baseIndex = model.VertexCount;
            var normalMatrix = world.InverseTransposeLinear();
            for (int v = 0; v < vcount; v++)
            {
                var p = Read(0, v, 3);
                var w = world.MultiplyPoint(p[0], p[1], p[2]);
                model.Positions.AddRange(w);
                var n = channels.Count > 1 ? Read(1, v, 3) : null;
                if (n != null) model.Normals.AddRange(Normalize(normalMatrix.MultiplyVector(n[0], n[1], n[2])));
                else model.Normals.AddRange(new float[] { 0, 1, 0 });
                var uv = channels.Count > 4 ? Read(4, v, 2) : null;
                model.UVs.AddRange(uv ?? new float[] { 0, 0 });
                var col = channels.Count > 3 ? Read(3, v, 4) : null;
                model.Colors.AddRange(col ?? new float[] { 1, 1, 1, 1 });
            }

            byte[] ib = mesh["m_IndexBuffer.Array"].IsDummy ? mesh["m_IndexBuffer"].AsByteArray : mesh["m_IndexBuffer.Array"].AsByteArray;
            bool u32 = !mesh["m_IndexFormat"].IsDummy && mesh["m_IndexFormat"].AsInt == 1;
            int si = 0;
            foreach (var sm in mesh["m_SubMeshes.Array"].Children)
            {
                int first = (int)sm["firstByte"].AsUInt, count = (int)sm["indexCount"].AsUInt, topo = sm["topology"].AsInt;
                int baseVertex = sm["baseVertex"].IsDummy ? 0 : (int)sm["baseVertex"].AsUInt;
                if (topo != 0) { si++; continue; } // triangles only
                var tris = new int[count];
                for (int i = 0; i < count; i++)
                {
                    int idx = u32 ? BitConverter.ToInt32(ib, first + i * 4) : BitConverter.ToUInt16(ib, first + i * 2);
                    tris[i] = baseIndex + baseVertex + idx;
                }
                if (world.Determinant() < 0)
                    for (int i = 0; i + 2 < tris.Length; i += 3) (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
                int mi = Math.Min(si, Math.Max(0, texs.Count - 1));
                model.SubMeshes.Add(new RawSubMesh
                {
                    Triangles = tris,
                    Texture = texs.Count > 0 ? texs[mi] : null,
                    Color = colors.Count > 0 ? colors[mi] : new float[] { 1, 1, 1, 1 },
                });
                si++;
            }
        }

        static float[] Normalize(float[] v)
        {
            float l = (float)Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            return l < 1e-6f ? new float[] { 0, 1, 0 } : new[] { v[0] / l, v[1] / l, v[2] / l };
        }

        static float ReadValue(byte[] d, int at, int format)
        {
            switch (format)
            {
                case 0: return BitConverter.ToSingle(d, at);
                case 1: return Half(BitConverter.ToUInt16(d, at));
                case 2: return d[at] / 255f;
                case 3: return Math.Max((sbyte)d[at] / 127f, -1f);
                case 4: return BitConverter.ToUInt16(d, at) / 65535f;
                case 5: return Math.Max(BitConverter.ToInt16(d, at) / 32767f, -1f);
                case 6: return d[at];
                case 7: return (sbyte)d[at];
                case 8: return BitConverter.ToUInt16(d, at);
                case 9: return BitConverter.ToInt16(d, at);
                case 10: return BitConverter.ToUInt32(d, at);
                case 11: return BitConverter.ToInt32(d, at);
            }
            return 0;
        }

        static float Half(ushort h)
        {
            int s = (h >> 15) & 1, e = (h >> 10) & 0x1F, m = h & 0x3FF;
            float v = e == 0 ? m / 1024f * (float)Math.Pow(2, -14) : e == 31 ? float.PositiveInfinity : (1 + m / 1024f) * (float)Math.Pow(2, e - 15);
            return s == 1 ? -v : v;
        }

        // ---------------- textures ----------------

        public RawTexture FindTexture(string[] hints, string[] extra = null)
        {
            var pool = Entries.Where(e => e.Type == Texture2DId || e.Type == SpriteId)
                .Select(e => (e, s: Score(e.Name, hints) + (extra != null && Score(e.Name, extra) > 0 ? 50 : 0)))
                .Where(x => x.s > 0 && (extra == null || Score(x.e.Name, extra) > 0))
                .OrderByDescending(x => x.s).Take(10);
            foreach (var (e, _) in pool)
            {
                try
                {
                    var bf = am.GetBaseField(e.File, e.Info);
                    if (e.Type == Texture2DId) return ReadTexture(e.File, bf);
                    // Sprite: the whole atlas texture plus the sprite rect, cropped later on the main thread.
                    var tex = am.GetExtAsset(e.File, bf["m_RD.texture"]);
                    if (tex.baseField == null) continue;
                    var t = ReadTexture(tex.file, tex.baseField);
                    if (t == null) continue;
                    var r = bf["m_RD.textureRect"];
                    t.Name = e.Name;
                    SpriteRects[t] = new[] { r["x"].AsFloat, r["y"].AsFloat, r["width"].AsFloat, r["height"].AsFloat };
                    return t;
                }
                catch (Exception ex) { log($"texture {e.Name}: {ex.Message}"); }
            }
            return null;
        }

        /// <summary>Sprite crop rects (x, y, w, h in pixels) for textures returned from sprites.</summary>
        public readonly Dictionary<RawTexture, float[]> SpriteRects = new Dictionary<RawTexture, float[]>();
        readonly Dictionary<(AssetsFileInstance, long), RawTexture> texCache = new Dictionary<(AssetsFileInstance, long), RawTexture>();

        RawTexture ReadTexture(AssetsFileInstance file, AssetTypeValueField t)
        {
            long key = t["m_Name"].AsString.GetHashCode() ^ (t["m_Width"].AsInt << 16) ^ t["m_Height"].AsInt;
            if (texCache.TryGetValue((file, key), out var cached)) return cached;
            byte[] data = t["image data"].AsByteArray;
            if ((data == null || data.Length == 0) && !t["m_StreamData"].IsDummy) data = ReadStream(file, t["m_StreamData"]);
            if (data == null || data.Length == 0) return null;
            var raw = new RawTexture
            {
                Name = t["m_Name"].AsString,
                Width = t["m_Width"].AsInt,
                Height = t["m_Height"].AsInt,
                Format = t["m_TextureFormat"].AsInt,
                MipCount = t["m_MipCount"].IsDummy ? 1 : t["m_MipCount"].AsInt,
                Data = data,
            };
            texCache[(file, key)] = raw;
            return raw;
        }

        byte[] ReadStream(AssetsFileInstance file, AssetTypeValueField sd)
        {
            long offset = sd["offset"].IsDummy ? 0 : (long)(sd["offset"].Value.ValueType == AssetValueType.UInt64 ? (long)sd["offset"].AsULong : sd["offset"].AsUInt);
            long size = sd["size"].AsUInt;
            string path = sd["path"].AsString;
            if (size <= 0 || string.IsNullOrEmpty(path)) return null;
            var buf = new byte[size];
            if (path.StartsWith("archive:/") && bundleOf.TryGetValue(file, out var bun))
            {
                int idx = bun.file.GetFileIndex(Path.GetFileName(path));
                if (idx < 0) return null;
                bun.file.GetFileRange(idx, out long start, out long _);
                lock (bun.file.DataReader)
                {
                    bun.file.DataReader.Position = start + offset;
                    bun.file.DataReader.BaseStream.Read(buf, 0, buf.Length);
                }
                return buf;
            }
            string full = Path.Combine(Path.GetDirectoryName(file.path) ?? DataDir, Path.GetFileName(path));
            if (!File.Exists(full)) full = Path.Combine(DataDir, Path.GetFileName(path));
            using (var fs = File.OpenRead(full))
            {
                fs.Position = offset;
                int read = 0;
                while (read < buf.Length) { int n = fs.Read(buf, read, buf.Length - read); if (n <= 0) break; read += n; }
            }
            return buf;
        }

        /// <summary>The swing part of a rotation: what is left after removing its twist (yaw) around the up axis.</summary>
        static float[] Tilt(float[] q)
        {
            float tl = (float)Math.Sqrt(q[1] * q[1] + q[3] * q[3]);
            if (tl < 1e-4f) return q; // a pure half-turn tilt: no yaw to remove
            float ty = q[1] / tl, tw = q[3] / tl;
            // swing = q * inverse(twist), twist = (0, ty, 0, tw)
            return new[]
            {
                q[0] * tw + q[2] * ty,
                q[1] * tw - q[3] * ty,
                q[2] * tw - q[0] * ty,
                q[3] * tw + q[1] * ty,
            };
        }

        static float[] V3(AssetTypeValueField f) => new[] { f["x"].AsFloat, f["y"].AsFloat, f["z"].AsFloat };
        static float[] Q(AssetTypeValueField f) => new[] { f["x"].AsFloat, f["y"].AsFloat, f["z"].AsFloat, f["w"].AsFloat };

        public void Close()
        {
            am.UnloadAll(true);
        }
    }

    /// <summary>Minimal 4x4 matrix so the worker thread never calls into UnityEngine.</summary>
    public struct Matrix
    {
        public float[] M; // row-major 16

        public static Matrix Identity => new Matrix { M = new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 } };

        public static Matrix Scale(float[] s) => new Matrix { M = new float[] { s[0], 0, 0, 0, 0, s[1], 0, 0, 0, 0, s[2], 0, 0, 0, 0, 1 } };

        public static Matrix TRS(float[] t, float[] q, float[] s)
        {
            float x = q[0], y = q[1], z = q[2], w = q[3];
            float xx = x * x, yy = y * y, zz = z * z, xy = x * y, xz = x * z, yz = y * z, wx = w * x, wy = w * y, wz = w * z;
            return new Matrix
            {
                M = new[]
                {
                    (1 - 2 * (yy + zz)) * s[0], 2 * (xy - wz) * s[1], 2 * (xz + wy) * s[2], t[0],
                    2 * (xy + wz) * s[0], (1 - 2 * (xx + zz)) * s[1], 2 * (yz - wx) * s[2], t[1],
                    2 * (xz - wy) * s[0], 2 * (yz + wx) * s[1], (1 - 2 * (xx + yy)) * s[2], t[2],
                    0, 0, 0, 1,
                }
            };
        }

        public static Matrix operator *(Matrix a, Matrix b)
        {
            var r = new float[16];
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                {
                    float s = 0;
                    for (int k = 0; k < 4; k++) s += a.M[i * 4 + k] * b.M[k * 4 + j];
                    r[i * 4 + j] = s;
                }
            return new Matrix { M = r };
        }

        public float[] MultiplyPoint(float x, float y, float z) => new[]
        {
            M[0] * x + M[1] * y + M[2] * z + M[3],
            M[4] * x + M[5] * y + M[6] * z + M[7],
            M[8] * x + M[9] * y + M[10] * z + M[11],
        };

        public float[] MultiplyVector(float x, float y, float z) => new[]
        {
            M[0] * x + M[1] * y + M[2] * z,
            M[4] * x + M[5] * y + M[6] * z,
            M[8] * x + M[9] * y + M[10] * z,
        };

        public float Determinant() =>
            M[0] * (M[5] * M[10] - M[6] * M[9]) - M[1] * (M[4] * M[10] - M[6] * M[8]) + M[2] * (M[4] * M[9] - M[5] * M[8]);

        /// <summary>Inverse-transpose of the upper 3x3 (for normals), as a matrix with no translation.</summary>
        public Matrix InverseTransposeLinear()
        {
            float a = M[0], b = M[1], c = M[2], d = M[4], e = M[5], f = M[6], g = M[8], h = M[9], i = M[10];
            float det = Determinant();
            if (Math.Abs(det) < 1e-12f) return Identity;
            float k = 1f / det;
            // cofactor matrix / det == inverse transpose
            return new Matrix
            {
                M = new[]
                {
                    (e * i - f * h) * k, -(d * i - f * g) * k, (d * h - e * g) * k, 0,
                    -(b * i - c * h) * k, (a * i - c * g) * k, -(a * h - b * g) * k, 0,
                    (b * f - c * e) * k, -(a * f - c * d) * k, (a * e - b * d) * k, 0,
                    0, 0, 0, 1,
                }
            };
        }
    }
}
