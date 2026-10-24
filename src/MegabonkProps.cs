using System;
using System.Collections.Generic;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace RiskOfBonk
{
    /// <summary>A Megabonk primitive collider in its model's space (the space FindModel builds the model in).</summary>
    public sealed class RawCollider
    {
        /// <summary>0 sphere, 1 capsule, 2 box.</summary>
        public int Kind;
        public float[] Center = new float[3];
        /// <summary>The capsule's axis (unit) or the box's size; unused for spheres.</summary>
        public float[] Axis = { 0, 1, 0 }, Size = new float[3];
        public float Radius, Height;
    }

    /// <summary>A Megabonk map prop (tree, rock, bush, log, ruin): its merged model and its primitive colliders.</summary>
    public sealed class RawProp
    {
        public string Name;
        public RawModel Model;
        public readonly List<RawCollider> Colliders = new List<RawCollider>();
        /// <summary>Whether the prefab collides through a mesh collider (buildings and ruins).</summary>
        public bool MeshCollider;
        /// <summary>The prefab root's own scale (FindModel builds the model without it).</summary>
        public float RootScale = 1f;
    }

    public sealed partial class MegabonkLibrary
    {
        const int BoxColliderId = 65, SphereColliderId = 135, CapsuleColliderId = 136, MeshColliderId = 64;

        /// <summary>A prop prefab by its exact name: model (FindModel) and colliders, or null.</summary>
        public RawProp ReadProp(string name)
        {
            foreach (var e in Entries)
            {
                if (e.Type != GameObjectId || e.Name != name) continue;
                AssetTypeValueField go;
                try { go = am.GetBaseField(e.File, e.Info); } catch { continue; }
                var tf = Component(e.File, go, TransformId);
                if (tf.baseField == null || tf.baseField["m_Father.m_PathID"].AsLong != 0) continue;
                var prop = new RawProp { Name = name, Model = FindModel(new[] { name }) };
                if (prop.Model == null || prop.Model.VertexCount == 0) return null;
                try
                {
                    var t = tf.baseField;
                    // The same root convention as BuildModel: placement dropped, tilt and scale kept.
                    var root = Matrix.TRS(new float[3], Tilt(Q(t["m_LocalRotation"])), V3(t["m_LocalScale"]));
                    prop.RootScale = V3(t["m_LocalScale"])[1];
                    PropColliders(prop, tf, go, root, 0);
                }
                catch (Exception ex) { log($"prop {name} colliders: {ex.Message}"); }
                return prop;
            }
            return null;
        }

        void PropColliders(RawProp prop, AssetExternal tf, AssetTypeValueField go, Matrix world, int depth)
        {
            if (!go["m_IsActive"].AsBool || depth > 16) return;
            float sx = Len(world.MultiplyVector(1, 0, 0)), sy = Len(world.MultiplyVector(0, 1, 0)), sz = Len(world.MultiplyVector(0, 0, 1));
            foreach (var c in go["m_Component.Array"].Children)
            {
                var ext = am.GetExtAsset(tf.file, c["component"], true);
                if (ext.info == null) continue;
                int type = ext.info.TypeId;
                if (type == MeshColliderId) { prop.MeshCollider = true; continue; }
                if (type != SphereColliderId && type != CapsuleColliderId && type != BoxColliderId) continue;
                var b = am.GetExtAsset(tf.file, c["component"]).baseField;
                if (b == null || b["m_Enabled"].AsByte == 0 || b["m_IsTrigger"].AsBool) continue;
                var center = V3(b["m_Center"]);
                var rc = new RawCollider { Center = world.MultiplyPoint(center[0], center[1], center[2]) };
                if (type == SphereColliderId)
                {
                    rc.Kind = 0;
                    rc.Radius = b["m_Radius"].AsFloat * Math.Max(sx, Math.Max(sy, sz));
                }
                else if (type == CapsuleColliderId)
                {
                    rc.Kind = 1;
                    int dir = b["m_Direction"].AsInt;
                    var axis = world.MultiplyVector(dir == 0 ? 1 : 0, dir == 1 ? 1 : 0, dir == 2 ? 1 : 0);
                    float along = Len(axis);
                    rc.Axis = along > 1e-6f ? new[] { axis[0] / along, axis[1] / along, axis[2] / along } : new float[] { 0, 1, 0 };
                    float across = dir == 0 ? Math.Max(sy, sz) : dir == 1 ? Math.Max(sx, sz) : Math.Max(sx, sy);
                    rc.Radius = b["m_Radius"].AsFloat * across;
                    rc.Height = b["m_Height"].AsFloat * along;
                }
                else
                {
                    rc.Kind = 2;
                    var s = V3(b["m_Size"]);
                    rc.Size = new[] { s[0] * sx, s[1] * sy, s[2] * sz };
                }
                prop.Colliders.Add(rc);
            }
            foreach (var child in tf.baseField["m_Children.Array"].Children)
            {
                var ct = am.GetExtAsset(tf.file, child);
                if (ct.baseField == null) continue;
                var cgo = am.GetExtAsset(ct.file, ct.baseField["m_GameObject"]);
                if (cgo.baseField == null) continue;
                var local = Matrix.TRS(V3(ct.baseField["m_LocalPosition"]), Q(ct.baseField["m_LocalRotation"]), V3(ct.baseField["m_LocalScale"]));
                PropColliders(prop, ct, cgo.baseField, world * local, depth + 1);
            }
        }

        static float Len(float[] v) => (float)Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);

        /// <summary>A texture by its exact name (and Unity TextureFormat number, -1 for any; stages share names), or null.</summary>
        public RawTexture ExactTexture(string name, int format = -1)
        {
            foreach (var e in Entries)
            {
                if (e.Type != Texture2DId || e.Name != name) continue;
                try
                {
                    var bf = am.GetBaseField(e.File, e.Info);
                    if (format >= 0 && bf["m_TextureFormat"].AsInt != format) continue; // before ReadTexture: its cache keys by name and size
                    var t = ReadTexture(e.File, bf);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }
    }
}
