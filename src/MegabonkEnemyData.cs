using System;
using System.Collections.Generic;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace RiskOfBonk
{
    /// <summary>
    /// A Megabonk enemy's look from its EnemyData: the baked walk animation (AnimatedMeshScriptableObject: one mesh per
    /// frame at AnimationFPS), its material's texture, and how its renderer sits on the enemy (offset, rotation, scale).
    /// </summary>
    public sealed class RawEnemy
    {
        public string Name;
        public RawTexture Texture;
        public float[] Color = { 1, 1, 1, 1 };
        public float[] Offset = new float[3], Rotation = new float[3];
        public float Scale = 1f, Speed, ColliderRadius;
        public bool Flying;
        public int Hp, Fps = 12;
        public readonly List<RawModel> Frames = new List<RawModel>();
    }

    public sealed partial class MegabonkLibrary
    {
        const int MonoBehaviourId = 114;

        sealed class RawReader
        {
            readonly byte[] b; public int o;
            public RawReader(byte[] b) { this.b = b; }
            public int I() { int v = BitConverter.ToInt32(b, o); o += 4; return v; }
            public long L() { long v = BitConverter.ToInt64(b, o); o += 8; return v; }
            public float F() { float v = BitConverter.ToSingle(b, o); o += 4; return v; }
            public bool Bo() { bool v = b[o] != 0; o += 4; return v; }
            public string S() { int n = I(); string s = Encoding.UTF8.GetString(b, o, n); o += n; o = (o + 3) & ~3; return s; }
            public long P() { I(); return L(); } // a pptr: file id (same file here), path id
        }

        byte[] RawBytes(AssetsFileInstance f, AssetFileInfo info)
        {
            lock (f.file.Reader)
            {
                var r = f.file.Reader;
                r.Position = info.GetAbsoluteByteOffset(f.file);
                return r.ReadBytes((int)info.ByteSize);
            }
        }

        static string RawName(byte[] b)
        {
            int n = BitConverter.ToInt32(b, 28);
            return n >= 0 && n < 200 && 32 + n <= b.Length ? Encoding.UTF8.GetString(b, 32, n) : "";
        }

        /// <summary>Megabonk EnemyData assets by asset name (e.g. "GoblinWeak"), with their animation frames, in one pass.</summary>
        public Dictionary<string, RawEnemy> ReadEnemies(ICollection<string> names)
        {
            var found = new Dictionary<string, RawEnemy>();
            foreach (var f in files)
                foreach (var info in f.file.GetAssetsOfType(MonoBehaviourId))
                {
                    if (found.Count == names.Count) return found;
                    if (info.ByteSize < 40 || info.ByteSize > 4096) continue;
                    byte[] b;
                    try { b = RawBytes(f, info); } catch { continue; }
                    string name = RawName(b);
                    if (!names.Contains(name) || found.ContainsKey(name)) continue;
                    // Animations share their enemy's name ("Golem"): only EnemyData counts.
                    try
                    {
                        var bf = am.GetBaseField(f, info, AssetReadFlags.SkipMonoBehaviourFields);
                        var script = am.GetExtAsset(f, bf["m_Script"]);
                        if (script.baseField == null || script.baseField["m_ClassName"].AsString != "EnemyData") continue;
                    }
                    catch { continue; }
                    try { found[name] = ParseEnemy(f, b, name); }
                    catch (Exception e) { log($"enemy {name}: {e.Message}"); }
                }
            return found;
        }

        RawEnemy ParseEnemy(AssetsFileInstance f, byte[] b, string name)
        {
            var r = new RawReader(b);
            r.P(); r.Bo(); r.P(); r.S(); // m_GameObject, m_Enabled, m_Script, m_Name
            var e = new RawEnemy { Name = name };
            r.I(); // enemyName
            long material = r.P(), animation = r.P();
            e.Offset = new[] { r.F(), r.F(), r.F() };
            e.Rotation = new[] { r.F(), r.F(), r.F() };
            e.Scale = r.F();
            e.ColliderRadius = r.F(); r.F(); r.F(); r.F(); r.F(); // overrideHeight, colliderCenter
            e.Hp = r.I(); r.I(); r.I(); r.Bo(); r.F(); r.Bo(); // damage, shield, isPoison, knockbackResistance, nukeProtection
            r.F(); e.Speed = r.F(); e.Flying = r.Bo();
            // The material's texture and colour.
            var mi = f.file.GetAssetInfo(material);
            if (mi != null)
            {
                var mat = new AssetExternal { file = f, info = mi, baseField = am.GetBaseField(f, mi) };
                e.Texture = MainTexture(mat);
                e.Color = MainColor(mat.baseField);
            }
            // The baked animation: AnimationFPS, then [name, meshes] per animation; the first one is the walk.
            var ai = f.file.GetAssetInfo(animation);
            if (ai == null) return e;
            var a = new RawReader(RawBytes(f, ai));
            a.P(); a.Bo(); a.P(); a.S();
            e.Fps = Math.Max(1, a.I());
            int clips = a.I();
            for (int c = 0; c < clips && c < 1; c++)
            {
                a.S();
                int n = a.I();
                for (int k = 0; k < n && k < 64; k++)
                {
                    long pid = a.P();
                    var mesh = f.file.GetAssetInfo(pid);
                    if (mesh == null) continue;
                    var model = new RawModel { SourceName = name + "_" + k };
                    model.Bones.Add(new RawBone { Name = name, World = Matrix.Scale(new float[] { 1, 1, 1 }) });
                    AppendMesh(model, am.GetBaseField(f, mesh), f, 0, null, new List<RawTexture> { e.Texture }, new List<float[]> { e.Color });
                    if (model.VertexCount > 0) e.Frames.Add(model);
                }
            }
            return e;
        }
    }
}
