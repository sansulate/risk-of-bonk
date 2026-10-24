using System;
using System.Collections.Generic;
using System.Linq;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace RiskOfBonk
{
    /// <summary>A copy of a serialized Unity field tree (a particle system's modules), readable without AssetsTools.</summary>
    public sealed class RawNode
    {
        public string Name;
        public double Num;
        public string Str;
        public List<RawNode> Kids;

        public RawNode Kid(string name)
        {
            if (Kids == null) return null;
            foreach (var k in Kids) if (k.Name == name) return k;
            return null;
        }

        /// <summary>The node at a dotted path ("InitialModule.startLifetime.scalar"), or null.</summary>
        public RawNode At(string path)
        {
            var n = this;
            foreach (var part in path.Split('.'))
            {
                n = n?.Kid(part);
                if (n == null) return null;
            }
            return n;
        }

        public float F(string path, float def = 0f) { var n = At(path); return n == null ? def : (float)n.Num; }
        public int I(string path, int def = 0) { var n = At(path); return n == null ? def : (int)n.Num; }
        public bool B(string path, bool def = false) { var n = At(path); return n == null ? def : n.Num != 0; }
    }

    /// <summary>An effect material: the texture(s) and the numbers its Megabonk shader uses.</summary>
    public sealed class RawFxMaterial
    {
        public string Name;
        /// <summary>glow (Retro Arsenal BasicGlow), beam (scrolling texture), disintegrate (ramp), sprite, toon (MK Toon), other.</summary>
        public string Kind;
        public RawTexture Texture, Texture2, Ramp;
        public readonly Dictionary<string, float> Floats = new Dictionary<string, float>();
        public readonly Dictionary<string, float[]> Colors = new Dictionary<string, float[]>();
        public float Float(string n, float def) => Floats.TryGetValue(n, out var v) ? v : def;
        public float[] Color(string n) => Colors.TryGetValue(n, out var v) ? v : new float[] { 1, 1, 1, 1 };
    }

    /// <summary>One GameObject of a Megabonk effect prefab: its placement, particle system, and mesh or trail renderer.</summary>
    public sealed class RawFx
    {
        public string Name;
        public bool Active = true;
        public float[] Pos = { 0, 0, 0 }, Rot = { 0, 0, 0, 1 }, Scale = { 1, 1, 1 };
        /// <summary>The ParticleSystem's serialized modules, or null.</summary>
        public RawNode Particles;
        /// <summary>The ParticleSystemRenderer's serialized fields (render mode, alignment, pivot, ...).</summary>
        public RawNode ParticleRenderer;
        public RawFxMaterial ParticleMaterial, ParticleTrailMaterial;
        /// <summary>The particles' mesh in mesh render mode: a Megabonk mesh, or "Quad" for Unity's built-in quad.</summary>
        public RawModel ParticleMesh;
        public string ParticleMeshName;
        /// <summary>A plain MeshRenderer's mesh (with its texture on the sub-meshes), or null.</summary>
        public RawModel Mesh;
        public RawFxMaterial MeshMaterial;
        /// <summary>A TrailRenderer's fields, or null.</summary>
        public RawNode Trail;
        public RawFxMaterial TrailMaterial;
        /// <summary>A LineRenderer's fields (a beam), or null.</summary>
        public RawNode Line;
        public RawFxMaterial LineMaterial;
        public readonly List<RawFx> Children = new List<RawFx>();
    }

    public sealed partial class MegabonkLibrary
    {
        const int ParticleSystemId = 198, ParticleSystemRendererId = 199, TrailRendererId = 96, LineRendererId = 120;

        /// <summary>The root prefab GameObject with this exact name (one without a parent), as an effect tree; null if none.</summary>
        public RawFx ReadFx(string name)
        {
            foreach (var e in Entries)
            {
                if (e.Type != GameObjectId || e.Name != name) continue;
                AssetTypeValueField go;
                try { go = am.GetBaseField(e.File, e.Info); } catch { continue; }
                var tf = Component(e.File, go, TransformId);
                if (tf.baseField == null || tf.baseField["m_Father.m_PathID"].AsLong != 0) continue;
                try { return ReadFxNode(e.File, go, tf, 0); }
                catch (Exception ex) { log($"effect {name}: {ex.Message}"); return null; }
            }
            return null;
        }

        RawFx ReadFxNode(AssetsFileInstance file, AssetTypeValueField go, AssetExternal tf, int depth)
        {
            var t = tf.baseField;
            var fx = new RawFx
            {
                Name = go["m_Name"].AsString,
                Active = go["m_IsActive"].AsBool,
                Pos = V3(t["m_LocalPosition"]),
                Rot = Q(t["m_LocalRotation"]),
                Scale = V3(t["m_LocalScale"]),
            };
            var ps = Component(file, go, ParticleSystemId);
            if (ps.baseField != null)
            {
                fx.Particles = Copy(ps.baseField, "ParticleSystem", 0);
                var pr = Component(file, go, ParticleSystemRendererId);
                if (pr.baseField != null && pr.baseField["m_Enabled"].AsByte != 0)
                {
                    fx.ParticleRenderer = Copy(pr.baseField, "Renderer", 0, skip: new[] { "m_Materials", "m_Mesh", "m_Mesh1", "m_Mesh2", "m_Mesh3", "m_GameObject", "m_StaticBatchInfo", "m_LightProbeVolumeOverride", "m_ProbeAnchor", "m_MaskInteraction" });
                    var mats = pr.baseField["m_Materials.Array"].Children;
                    if (mats.Count > 0) fx.ParticleMaterial = ReadFxMaterial(pr.file, mats[0]);
                    if (mats.Count > 1) fx.ParticleTrailMaterial = ReadFxMaterial(pr.file, mats[1]);
                    var meshPtr = pr.baseField["m_Mesh"];
                    if (meshPtr["m_PathID"].AsLong != 0) (fx.ParticleMesh, fx.ParticleMeshName) = ReadFxMesh(pr.file, meshPtr);
                }
                else fx.Particles = null; // no renderer: nothing to see
            }
            var mr = Component(file, go, MeshRendererId);
            var mf = Component(file, go, MeshFilterId);
            if (mr.baseField != null && mf.baseField != null && mr.baseField["m_Enabled"].AsByte != 0)
            {
                var mats = mr.baseField["m_Materials.Array"].Children;
                if (mats.Count > 0) fx.MeshMaterial = ReadFxMaterial(mr.file, mats[0]);
                (fx.Mesh, _) = ReadFxMesh(mf.file, mf.baseField["m_Mesh"], fx.MeshMaterial?.Texture);
            }
            var tr = Component(file, go, TrailRendererId);
            if (tr.baseField != null && tr.baseField["m_Enabled"].AsByte != 0)
            {
                fx.Trail = Copy(tr.baseField, "Trail", 0, skip: new[] { "m_Materials", "m_GameObject", "m_StaticBatchInfo", "m_LightProbeVolumeOverride", "m_ProbeAnchor" });
                var mats = tr.baseField["m_Materials.Array"].Children;
                if (mats.Count > 0) fx.TrailMaterial = ReadFxMaterial(tr.file, mats[0]);
            }
            var lr = Component(file, go, LineRendererId);
            if (lr.baseField != null)
            {
                fx.Line = Copy(lr.baseField, "Line", 0, skip: new[] { "m_Materials", "m_GameObject", "m_StaticBatchInfo", "m_LightProbeVolumeOverride", "m_ProbeAnchor" });
                var mats = lr.baseField["m_Materials.Array"].Children;
                if (mats.Count > 0) fx.LineMaterial = ReadFxMaterial(lr.file, mats[0]);
            }
            if (depth < 8)
                foreach (var child in t["m_Children.Array"].Children)
                {
                    var c = am.GetExtAsset(tf.file, child);
                    if (c.baseField == null) continue;
                    var cgo = am.GetExtAsset(c.file, c.baseField["m_GameObject"]);
                    if (cgo.baseField != null) fx.Children.Add(ReadFxNode(cgo.file, cgo.baseField, c, depth + 1));
                }
            return fx;
        }

        static RawNode Copy(AssetTypeValueField f, string name, int depth, string[] skip = null)
        {
            var n = new RawNode { Name = name };
            if (f.Value != null && f.Value.ValueType != AssetValueType.Array && f.Value.ValueType != AssetValueType.ByteArray)
            {
                switch (f.Value.ValueType)
                {
                    case AssetValueType.String: n.Str = f.AsString; break;
                    case AssetValueType.Bool: n.Num = f.AsBool ? 1 : 0; break;
                    case AssetValueType.Float: n.Num = f.AsFloat; break;
                    case AssetValueType.Double: n.Num = f.AsDouble; break;
                    case AssetValueType.UInt64: n.Num = f.AsULong; break;
                    case AssetValueType.UInt32: case AssetValueType.UInt16: case AssetValueType.UInt8: n.Num = f.AsUInt; break;
                    default: n.Num = f.AsLong; break;
                }
                return n;
            }
            if (depth > 12 || f.Children == null) return n;
            n.Kids = new List<RawNode>(f.Children.Count);
            foreach (var c in f.Children)
            {
                if (skip != null && Array.IndexOf(skip, c.FieldName) >= 0) continue;
                n.Kids.Add(Copy(c, c.FieldName, depth + 1));
            }
            return n;
        }

        readonly Dictionary<(string, long), RawFxMaterial> fxMaterials = new Dictionary<(string, long), RawFxMaterial>();

        RawFxMaterial ReadFxMaterial(AssetsFileInstance file, AssetTypeValueField ptr)
        {
            if (ptr["m_PathID"].AsLong == 0) return null;
            var m = am.GetExtAsset(file, ptr);
            if (m.baseField == null) return null;
            var key = (m.file.path, m.info.PathId);
            if (fxMaterials.TryGetValue(key, out var cached)) return cached;
            var mat = new RawFxMaterial { Name = m.baseField["m_Name"].AsString };
            var props = m.baseField["m_SavedProperties"];
            var texs = new Dictionary<string, AssetTypeValueField>();
            foreach (var te in props["m_TexEnvs.Array"].Children)
                if (te["second.m_Texture.m_PathID"].AsLong != 0) texs[te["first"].AsString] = te["second.m_Texture"];
            foreach (var c in props["m_Floats.Array"].Children) mat.Floats[c["first"].AsString] = c["second"].AsFloat;
            foreach (var c in props["m_Colors.Array"].Children)
                mat.Colors[c["first"].AsString] = new[] { c["second.r"].AsFloat, c["second.g"].AsFloat, c["second.b"].AsFloat, c["second.a"].AsFloat };
            RawTexture Tex(string prop)
            {
                if (!texs.TryGetValue(prop, out var tp)) return null;
                var tx = am.GetExtAsset(m.file, tp);
                return tx.baseField != null && tx.info.TypeId == Texture2DId ? ReadTexture(tx.file, tx.baseField) : null;
            }
            // The shader is told apart by the properties its materials carry (reading the shader asset itself is slow).
            if (texs.ContainsKey("_TextureSample"))
            {
                mat.Texture = Tex("_TextureSample");
                mat.Texture2 = Tex("_TextureSample2");
                mat.Ramp = Tex("_Ramp");
                mat.Kind = mat.Ramp != null ? "disintegrate" : mat.Colors.ContainsKey("_ScrollSpeed") ? "beam" : "glow";
            }
            else if (texs.ContainsKey("_AlbedoMap")) { mat.Texture = Tex("_AlbedoMap"); mat.Kind = "toon"; }
            else if (mat.Colors.ContainsKey("_RendererColor")) { mat.Texture = Tex("_MainTex"); mat.Kind = "sprite"; }
            else { mat.Texture = Tex("_MainTex"); mat.Kind = "other"; }
            fxMaterials[key] = mat;
            return mat;
        }

        // Unity's built-in meshes live in "unity default resources" under these path ids.
        static readonly Dictionary<long, string> BuiltinMeshes = new Dictionary<long, string>
        {
            { 10202, "Cube" }, { 10206, "Cylinder" }, { 10207, "Sphere" }, { 10208, "Capsule" }, { 10209, "Plane" }, { 10210, "Quad" },
        };

        (RawModel, string) ReadFxMesh(AssetsFileInstance file, AssetTypeValueField ptr, RawTexture texture = null)
        {
            long pid = ptr["m_PathID"].AsLong;
            if (pid == 0) return (null, null);
            AssetExternal mesh;
            try { mesh = am.GetExtAsset(file, ptr); } catch { mesh = default; }
            if (mesh.baseField == null) return (null, BuiltinMeshes.TryGetValue(pid, out var b) ? b : null);
            string name = mesh.baseField["m_Name"].AsString;
            var model = new RawModel { SourceName = name };
            model.Bones.Add(new RawBone { Name = name, World = Matrix.Scale(new float[] { 1, 1, 1 }) });
            AppendMesh(model, mesh.baseField, mesh.file, 0, null, new List<RawTexture> { texture }, new List<float[]> { new float[] { 1, 1, 1, 1 } });
            return (model.VertexCount > 0 ? model : null, name);
        }
    }
}
