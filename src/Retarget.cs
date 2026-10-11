using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RiskOfBonk
{
    /// <summary>
    /// Plays a survivor's animations on a Megabonk model through the model's own skeleton and skin weights.
    /// The Megabonk bones are rebuilt as transforms, posed like the survivor's bind pose (arms, legs and neck turned to
    /// the survivor's directions, so an arms-down model matches an arms-out rig), and every frame each mapped Megabonk
    /// bone takes the rotation its survivor bone made away from the bind pose. Megabonk proportions stay as they are.
    /// </summary>
    public static class Retarget
    {
        public enum Role { None, Hips, Chest, Neck, Head, UpperArm, ForeArm, Hand, Thigh, Shin, Foot }

        static readonly string[] skipWords = { "mech", "cannon", "ik", "target", "bender", "grip", "jiggle", "finger", "thumb", "roll",
                                               "control", "helper", "back", "extra", "end", "pad", "exhaust", "tool", "gun", "hood" };

        /// <summary>Role and side (-1 left, 1 right, 0 middle) of a bone, by its name.</summary>
        public static (Role role, int side) Classify(string boneName, bool megabonk)
        {
            string n = boneName.ToLowerInvariant().Replace(" ", "");
            int side = 0;
            foreach (var (suffix, s) in new[] { (".l", -1), ("_l", -1), (".left", -1), (".r", 1), ("_r", 1), (".right", 1) })
                if (n.EndsWith(suffix)) { side = s; n = n.Substring(0, n.Length - suffix.Length); break; }
            // REX walks on four legs: its front pair stands in for legs (the back pair is skipped above).
            n = n.Replace(".front", "");
            foreach (var part in n.Split('.', '_', '-'))
                foreach (var w in skipWords)
                    if (part.StartsWith(w)) return (Role.None, side);
            Role r = Role.None;
            if (megabonk)
                switch (n)
                {
                    case "hips": r = Role.Hips; break;
                    case "torso": r = Role.Chest; break;
                    case "neck": r = Role.Neck; break;
                    case "head": r = Role.Head; break;
                    case "shoulder": r = Role.UpperArm; break;
                    case "arm": case "forearm": case "elbow": r = Role.ForeArm; break;
                    case "hand": case "wrist": case "wirst": r = Role.Hand; break;
                    case "leg": r = Role.Thigh; break;
                    case "knee": r = Role.Shin; break;
                    case "foot": case "toes": r = Role.Foot; break;
                }
            else
                switch (n)
                {
                    case "pelvis": case "hip": r = Role.Hips; break;
                    case "chest": case "platformbase": r = Role.Chest; break;
                    case "neck": r = Role.Neck; break;
                    case "head": case "headbase": r = Role.Head; break;
                    case "upper_arm": case "upperarm": r = Role.UpperArm; break;
                    case "lower_arm": case "lowerarm": case "forearm": r = Role.ForeArm; break;
                    case "hand": r = Role.Hand; break;
                    case "thigh": r = Role.Thigh; break;
                    case "calf": case "shin": r = Role.Shin; break;
                    case "foot": r = Role.Foot; break;
                }
            // Limbs need a side; a lone "Hand" under a left forearm (Void Fiend) is resolved by the caller.
            return (r, side);
        }

        /// <summary>First bone (in hierarchy order) per (role, side).</summary>
        static Dictionary<(Role, int), int> Map(IList<string> names, IList<int> parents, IEnumerable<int> candidates, bool megabonk)
        {
            var map = new Dictionary<(Role, int), int>();
            foreach (int i in candidates)
            {
                var (r, s) = Classify(names[i], megabonk);
                if (r == Role.None) continue;
                if (s == 0 && (r == Role.Hand || r == Role.ForeArm || r == Role.UpperArm))
                {
                    // Side from the nearest sided ancestor.
                    for (int p = parents[i]; p >= 0 && s == 0; p = parents[p]) s = Classify(names[p], megabonk).side;
                }
                if (!map.ContainsKey((r, s))) map[(r, s)] = i;
            }
            return map;
        }

        /// <summary>A body (hips and chest) with at least four limb bones: arms, legs, or REX's one arm and front legs.</summary>
        static bool Humanoid(Dictionary<(Role, int), int> m) =>
            m.ContainsKey((Role.Hips, 0)) && m.ContainsKey((Role.Chest, 0)) &&
            m.Keys.Count(k => k.Item2 != 0 && k.Item1 >= Role.UpperArm) >= 4;

        static Vector3 Pos(Matrix m) => new Vector3(m.M[3], m.M[7], m.M[11]);

        /// <summary>
        /// Which way the Megabonk model faces, as a yaw that turns it to +Z (the survivor's forward): its left limbs
        /// (".L") sit on the character's left. Null when the skeleton does not say.
        /// </summary>
        public static float? FacingYaw(RawModel raw)
        {
            if (raw == null || raw.Bones.Count == 0) return null;
            var map = Map(raw.Bones.Select(b => b.Name).ToList(), raw.Bones.Select(b => b.Parent).ToList(), Enumerable.Range(0, raw.Bones.Count), true);
            Vector3 left = Vector3.zero, right = Vector3.zero; int n = 0;
            foreach (var r in new[] { Role.UpperArm, Role.Thigh })
                if (map.TryGetValue((r, -1), out int l) && map.TryGetValue((r, 1), out int rr))
                {
                    left += Pos(raw.Bones[l].World); right += Pos(raw.Bones[rr].World); n++;
                }
            if (n == 0) return null;
            // Facing +Z means the character's right is +X: forward = cross(left-to-right, up).
            var lr = right - left; lr.y = 0;
            if (lr.sqrMagnitude < 1e-8f) return null;
            var forward = Vector3.Cross(lr, Vector3.up);
            return -Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Builds the retargeted renderer, or returns null when either skeleton is not humanoid enough (the caller then
        /// falls back to the nearest-bone fit). Target bones are given in model space at their bind pose.
        /// </summary>
        public static SkinnedMeshRenderer Build(Transform modelRoot, Transform[] tBones, Vector3[] tPos, Quaternion[] tRot, int[] tValid,
            RawModel raw, Mesh source, Material[] materials, Quaternion yaw, float scale, Vector3 offset, string name)
        {
            if (raw == null || raw.Bones.Count == 0 || raw.BoneIndices.Count != raw.VertexCount * 4) return null;
            var tNames = tBones.Select(b => b != null ? b.name : "").ToList();
            var tIndex = new Dictionary<Transform, int>();
            foreach (int i in tValid) if (!tIndex.ContainsKey(tBones[i])) tIndex[tBones[i]] = i;
            var tParents = tBones.Select(b => b != null && b.parent != null && tIndex.TryGetValue(b.parent, out int p) ? p : -1).ToList();
            var tMap = Map(tNames, tParents, tValid, false);
            if (!tMap.ContainsKey((Role.Hips, 0)))
            {
                // No pelvis/hip: the armature's "base" carries the legs.
                foreach (int i in tValid)
                    if (tNames[i].ToLowerInvariant() == "base") { tMap[(Role.Hips, 0)] = i; break; }
            }
            if (!tMap.ContainsKey((Role.Head, 0)) && tMap.TryGetValue((Role.Neck, 0), out int tn)) tMap[(Role.Head, 0)] = tn;
            // Still no hips (REX): the chest's parent carries the body, or the chest itself.
            if (!tMap.ContainsKey((Role.Hips, 0)) && tMap.TryGetValue((Role.Chest, 0), out int tc))
                tMap[(Role.Hips, 0)] = tParents[tc] >= 0 ? tParents[tc] : tc;

            var mNames = raw.Bones.Select(b => b.Name).ToList();
            var mParents = raw.Bones.Select(b => b.Parent).ToList();
            var mMap = Map(mNames, mParents, Enumerable.Range(0, raw.Bones.Count), true);
            // A Megabonk "Neck" without a "Head" is the head: drive it once, as the head.
            if (!mMap.ContainsKey((Role.Head, 0)) && mMap.TryGetValue((Role.Neck, 0), out int mn)) { mMap[(Role.Head, 0)] = mn; mMap.Remove((Role.Neck, 0)); }
            if (!Humanoid(tMap) || !Humanoid(mMap))
            {
                Log.Info($"{name}: not retargeted (survivor roles {string.Join(",", tMap.Keys)}; Megabonk roles {string.Join(",", mMap.Keys)})");
                return null;
            }

            // Megabonk transforms: the bones the skin uses and their ancestors.
            var keep = new bool[raw.Bones.Count];
            for (int i = 0; i < raw.BoneIndices.Count; i++)
                if (raw.BoneWeights[i] > 0)
                    for (int b = raw.BoneIndices[i]; b >= 0 && !keep[b]; b = raw.Bones[b].Parent) keep[b] = true;
            foreach (var b in mMap.Values) for (int p = b; p >= 0 && !keep[p]; p = raw.Bones[p].Parent) keep[p] = true;

            // Stand the Megabonk hips over the survivor's hips.
            var mHips = yaw * Pos(raw.Bones[mMap[(Role.Hips, 0)]].World) * scale + offset;
            var tHips = tPos[tMap[(Role.Hips, 0)]];
            offset += new Vector3(tHips.x - mHips.x, 0, tHips.z - mHips.z);

            var rig = new GameObject(name + "_skeleton").transform;
            rig.SetParent(modelRoot, false);
            rig.localPosition = offset;
            rig.localRotation = yaw;
            rig.localScale = Vector3.one * scale;
            var mT = new Transform[raw.Bones.Count];
            for (int i = 0; i < raw.Bones.Count; i++)
            {
                if (!keep[i]) continue;
                var t = new GameObject(raw.Bones[i].Name).transform;
                int p = raw.Bones[i].Parent;
                t.SetParent(p >= 0 && mT[p] != null ? mT[p] : rig, false);
                var world = ToUnity(raw.Bones[i].World);
                var local = p >= 0 && mT[p] != null ? ToUnity(raw.Bones[p].World).inverse * world : world;
                t.localPosition = local.GetColumn(3);
                t.localRotation = Rot(local);
                t.localScale = local.lossyScale;
                mT[i] = t;
            }

            // The new renderer sits on the model root; its mesh is in model space, in the Megabonk rest pose.
            var go = new GameObject(name);
            go.transform.SetParent(modelRoot, false);
            go.layer = modelRoot.gameObject.layer;
            var used = Enumerable.Range(0, raw.Bones.Count).Where(i => mT[i] != null).ToArray();
            var slot = new Dictionary<int, int>();
            for (int k = 0; k < used.Length; k++) slot[used[k]] = k;
            var weights = new BoneWeight[raw.VertexCount];
            for (int v = 0; v < raw.VertexCount; v++)
            {
                int B(int k) => slot.TryGetValue(raw.BoneIndices[v * 4 + k], out int s) ? s : 0;
                float W(int k) => raw.BoneWeights[v * 4 + k];
                weights[v] = new BoneWeight
                {
                    boneIndex0 = B(0), weight0 = W(0), boneIndex1 = B(1), weight1 = W(1),
                    boneIndex2 = B(2), weight2 = W(2), boneIndex3 = B(3), weight3 = W(3),
                };
            }
            var mesh = Object.Instantiate(source);
            mesh.name = name + "_retargeted";
            mesh.vertices = source.vertices.Select(v => yaw * v * scale + offset).ToArray();
            mesh.normals = source.normals.Select(n => (yaw * n).normalized).ToArray();
            mesh.boneWeights = weights;
            mesh.bindposes = used.Select(i => mT[i].worldToLocalMatrix * go.transform.localToWorldMatrix).ToArray();
            mesh.RecalculateBounds();

            // Pose the Megabonk skeleton like the survivor's bind pose: limb and neck directions, parent first.
            var model = modelRoot;
            Vector3 MPos(int i) => model.InverseTransformPoint(mT[i].position);
            void Align(Role a, Role b, int side)
            {
                if (!mMap.TryGetValue((a, side), out int ma) || !mMap.TryGetValue((b, side), out int mb)) return;
                if (!tMap.TryGetValue((a, side), out int ta) || !tMap.TryGetValue((b, side), out int tb)) return;
                var from = MPos(mb) - MPos(ma);
                var to = tPos[tb] - tPos[ta];
                if (from.sqrMagnitude < 1e-10f || to.sqrMagnitude < 1e-10f) return;
                var turn = Quaternion.FromToRotation(model.TransformDirection(from), model.TransformDirection(to));
                mT[ma].rotation = turn * mT[ma].rotation;
            }
            Align(Role.Chest, Role.Head, 0);
            foreach (int s in new[] { -1, 1 })
            {
                Align(Role.UpperArm, Role.ForeArm, s);
                Align(Role.ForeArm, Role.Hand, s);
                Align(Role.Thigh, Role.Shin, s);
                Align(Role.Shin, Role.Foot, s);
            }

            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = used.Select(i => mT[i]).ToArray();
            smr.rootBone = rig;
            smr.sharedMaterials = materials;
            smr.updateWhenOffscreen = true;

            // Driver: each mapped Megabonk bone follows its survivor bone's rotation away from the bind pose.
            var pairs = mMap.Where(kv => tMap.ContainsKey(kv.Key)).OrderBy(kv => Depth(mParents, kv.Value)).ToList();
            var drv = go.AddComponent<MegabonkRig>();
            drv.modelRoot = modelRoot;
            drv.megabonk = pairs.Select(kv => mT[kv.Value]).ToArray();
            drv.survivor = pairs.Select(kv => tBones[tMap[kv.Key]]).ToArray();
            drv.survivorBind = pairs.Select(kv => tRot[tMap[kv.Key]]).ToArray();
            drv.megabonkRest = pairs.Select(kv => Quaternion.Inverse(modelRoot.rotation) * mT[kv.Value].rotation).ToArray();
            int hipsPair = pairs.FindIndex(kv => kv.Key == (Role.Hips, 0));
            drv.hips = hipsPair;
            if (hipsPair >= 0)
            {
                drv.hipsRest = MPos(mMap[(Role.Hips, 0)]);
                drv.survivorHipsBind = tPos[tMap[(Role.Hips, 0)]];
                float tFloor = tValid.Min(i => tPos[i].y), mFloor = mesh.bounds.min.y;
                float th = drv.survivorHipsBind.y - tFloor, mh = drv.hipsRest.y - mFloor;
                // Hip motion scales with leg length; a legless model (Vlad floats on a cape) just follows it.
                bool legs = mMap.ContainsKey((Role.Thigh, -1)) && mMap.ContainsKey((Role.Thigh, 1));
                drv.hipsScale = legs && th > 1e-4f ? Mathf.Clamp(mh / th, 0.5f, 2f) : 1f;
                // Without legs the robe would sink through the floor whenever the survivor crouches: float instead.
                drv.hipsVertical = legs ? 1f : 0f;
            }
            Log.Info($"{name} on {modelRoot.name}: retargeted, {pairs.Count} bones driven ({string.Join(" ", pairs.Select(kv => mNames[kv.Value] + ">" + tNames[tMap[kv.Key]]))})");
            if (RigDebug.On) go.AddComponent<RigDebug.Bake>().modelRoot = modelRoot;
            return smr;
        }

        static int Depth(List<int> parents, int i) { int d = 0; for (int p = parents[i]; p >= 0; p = parents[p]) d++; return d; }

        /// <summary>Rotation of a matrix that may carry a positive scale.</summary>
        public static Quaternion Rot(Matrix4x4 m)
        {
            Vector3 f = m.GetColumn(2), u = m.GetColumn(1);
            return f.sqrMagnitude < 1e-12f || u.sqrMagnitude < 1e-12f ? Quaternion.identity : Quaternion.LookRotation(f, u);
        }

        static Matrix4x4 ToUnity(Matrix m)
        {
            var u = new Matrix4x4();
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) u[r, c] = m.M[r * 4 + c];
            return u;
        }
    }

    /// <summary>Copies a survivor skeleton's motion onto the Megabonk skeleton after the survivor's animation ran.</summary>
    public sealed class MegabonkRig : MonoBehaviour
    {
        public Transform modelRoot;
        public Transform[] megabonk, survivor;
        public Quaternion[] survivorBind, megabonkRest; // model space
        public int hips = -1;
        public Vector3 hipsRest, survivorHipsBind;      // model space
        public float hipsScale = 1f, hipsVertical = 1f;

        void LateUpdate()
        {
            if (modelRoot == null) return;
            var root = modelRoot.rotation;
            var inv = Quaternion.Inverse(root);
            for (int i = 0; i < megabonk.Length; i++)
            {
                var s = survivor[i];
                var m = megabonk[i];
                if (s == null || m == null) continue;
                var delta = inv * s.rotation * Quaternion.Inverse(survivorBind[i]);
                m.rotation = root * delta * megabonkRest[i];
                if (i == hips)
                {
                    var moved = modelRoot.InverseTransformPoint(s.position) - survivorHipsBind;
                    moved.y *= hipsVertical;
                    m.position = modelRoot.TransformPoint(hipsRest + moved * hipsScale);
                }
            }
        }
    }
}
