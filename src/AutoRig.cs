using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RiskOfBonk
{
    /// <summary>
    /// Fits a Megabonk model onto a Risk of Rain 2 survivor's skeleton at runtime, so the Megabonk character plays the
    /// survivor's own animations. Each vertex is bound to its two nearest bone segments (inverse-distance weights).
    /// The fit is done in the survivor mesh's bind pose (its rest pose, from the mesh's bindposes), not in whatever
    /// pose the prefab's bones happen to be saved in, and the new mesh reuses those bindposes.
    /// </summary>
    public static class AutoRig
    {
        public static SkinnedMeshRenderer Fit(Transform modelRoot, SkinnedMeshRenderer src, Matrix4x4[] srcBindposes, Transform[] fallbackBones, Mesh source,
            Material[] materials, float yawDeg, string name)
        {
            Transform[] bones;
            Vector3[] local;          // bone positions in model space (Y up, character facing +Z)
            Matrix4x4 toModel;        // new mesh space -> model space
            Matrix4x4[] bindposes = null;
            Transform parent;
            bool bindPose = src != null && srcBindposes != null && src.bones.Length > 0 && srcBindposes.Length == src.bones.Length;
            if (bindPose)
            {
                // Mesh space = the survivor SMR's own space; bone i sits at inverse(bindpose[i]) there.
                bones = src.bones;
                bindposes = srcBindposes;
                toModel = modelRoot.worldToLocalMatrix * src.transform.localToWorldMatrix;
                var m = toModel;
                local = bindposes.Select(bp => m.MultiplyPoint3x4(bp.inverse.MultiplyPoint3x4(Vector3.zero))).ToArray();
                parent = src.transform.parent;
            }
            else
            {
                bones = fallbackBones.Where(b => b != null).Distinct().ToArray();
                toModel = Matrix4x4.identity;
                local = bones.Select(b => modelRoot.InverseTransformPoint(b.position)).ToArray();
                parent = modelRoot;
            }

            // Skeleton extents in model space, feet to head. Skins also hold helper bones (IK targets, aim pivots, hook
            // chains) that can sit far from the body, so measure from body bones only and prefer the head and feet.
            var all = Enumerable.Range(0, bones.Length).Where(i => bones[i] != null).ToArray();
            if (bindPose)
            {
                // Skins list bones their mesh does not use, with meaningless bindposes. Bones only rotate, so a bone's
                // bind-pose distance to its parent must match its real length; drop the ones that do not.
                var pose = bones.Select(t => t != null ? modelRoot.InverseTransformPoint(t.position) : Vector3.zero).ToArray();
                var at = new Dictionary<Transform, int>();
                foreach (int i in all) if (!at.ContainsKey(bones[i])) at[bones[i]] = i;
                var bad = new HashSet<int>();
                foreach (int i in all)
                {
                    if (bones[i].parent == null || !at.TryGetValue(bones[i].parent, out int p)) continue;
                    float real = (pose[i] - pose[p]).magnitude, bind = (local[i] - local[p]).magnitude;
                    if (Mathf.Abs(bind - real) > 0.02f + 0.25f * real) bad.Add(i);
                }
                if (bad.Count > 0) Log.Info($"{name}: {bad.Count} bones without a usable bind pose ignored");
                all = all.Where(i => !bad.Contains(i)).ToArray();
            }
            var body = all.Where(i => !IsHelper(bones[i].name)).ToArray();
            if (body.Length < 3) body = all;
            var heads = body.Where(i => Named(bones[i], "head")).ToArray();
            var feet = body.Where(i => Named(bones[i], "foot", "toe", "ankle")).ToArray();
            float minY = (feet.Length > 0 ? feet : body).Min(i => local[i].y);
            float maxY = heads.Length > 0 ? heads.Max(i => local[i].y) : body.Max(i => local[i].y);
            // Non-humanoid rigs (REX) name things "foot"/"head" that are not feet or head: fall back to all body bones.
            if (maxY - minY < 0.25f * (body.Max(i => local[i].y) - body.Min(i => local[i].y)))
            {
                minY = body.Min(i => local[i].y);
                maxY = body.Max(i => local[i].y);
            }
            float skeletonHeight = Mathf.Max(0.3f, maxY - minY);
            var centre = new Vector3(body.Average(i => local[i].x), 0, body.Average(i => local[i].z));
            Log.Info($"{name} on {modelRoot.name}: {(bindPose ? "bind pose" : "prefab pose")}, {bones.Length} bones ({body.Length} body), " +
                     $"height {skeletonHeight:0.00} [{minY:0.00}..{maxY:0.00}], head {(heads.Length > 0 ? bones[heads[0]].name : "-")}");

            // Megabonk model, scaled and placed on the skeleton in model space.
            var rot = Quaternion.Euler(0, yawDeg, 0);
            var verts = source.vertices.Select(v => rot * v).ToArray();
            var b = new Bounds(verts[0], Vector3.zero);
            foreach (var v in verts) b.Encapsulate(v);
            // A little taller than the joints so the head clears the head bone.
            float scale = skeletonHeight * 1.12f / Mathf.Max(0.01f, b.size.y);
            // Centre on the vertex median, not the bounds: a held prop (sword, cape, staff) skews the bounds sideways.
            float mx = Median(verts.Select(v => v.x)), mz = Median(verts.Select(v => v.z));
            var offset = new Vector3(centre.x, minY - 0.04f * skeletonHeight, centre.z) - new Vector3(mx, b.min.y, mz) * scale;
            for (int i = 0; i < verts.Length; i++) verts[i] = verts[i] * scale + offset;

            // Bone segments: each body bone to each of its child bones; leaves are points. Helper bones get no vertices.
            var isBody = new HashSet<int>(body);
            var index = new Dictionary<Transform, int>();
            foreach (int i in all) if (!index.ContainsKey(bones[i])) index[bones[i]] = i;
            var segA = new List<Vector3>(); var segB = new List<Vector3>(); var segBone = new List<int>();
            foreach (int i in body)
            {
                bool child = false;
                foreach (Transform c in bones[i])
                    if (index.TryGetValue(c, out int ci) && isBody.Contains(ci))
                    {
                        segA.Add(local[i]); segB.Add(local[ci]); segBone.Add(i); child = true;
                    }
                if (!child) { segA.Add(local[i]); segB.Add(local[i]); segBone.Add(i); }
            }

            var weights = new BoneWeight[verts.Length];
            for (int v = 0; v < verts.Length; v++)
            {
                int b0 = segBone[0], b1 = segBone[0]; float d0 = float.MaxValue, d1 = float.MaxValue;
                for (int s = 0; s < segA.Count; s++)
                {
                    float d = DistSq(verts[v], segA[s], segB[s]);
                    int bi = segBone[s];
                    if (d < d0) { if (bi != b0) { d1 = d0; b1 = b0; } d0 = d; b0 = bi; }
                    else if (d < d1 && bi != b0) { d1 = d; b1 = bi; }
                }
                float w0 = 1f / (d0 + 1e-4f), w1 = d1 < float.MaxValue ? 1f / (d1 + 1e-4f) : 0f;
                // Sharpen so limbs do not smear into the torso.
                w0 *= w0; w1 *= w1;
                float sum = w0 + w1;
                weights[v] = new BoneWeight { boneIndex0 = b0, weight0 = w0 / sum, boneIndex1 = b1, weight1 = w1 / sum };
            }

            RigDebug.WriteFit(name + "_" + modelRoot.root.name, verts, source.triangles, all.Select(i => local[i]).ToArray(), Matrix4x4.identity);

            // Back from model space into the mesh space the bindposes expect.
            var toMesh = toModel.inverse;
            var mesh = Object.Instantiate(source);
            mesh.name = name + "_rigged";
            mesh.vertices = verts.Select(v => toMesh.MultiplyPoint3x4(v)).ToArray();
            mesh.normals = source.normals.Select(n => toMesh.MultiplyVector(rot * n).normalized).ToArray();
            mesh.boneWeights = weights;

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            if (bindPose)
            {
                go.transform.localPosition = src.transform.localPosition;
                go.transform.localRotation = src.transform.localRotation;
                go.transform.localScale = src.transform.localScale;
            }
            go.layer = modelRoot.gameObject.layer;
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            mesh.bindposes = bindPose ? bindposes : bones.Select(bt => bt.worldToLocalMatrix * go.transform.localToWorldMatrix).ToArray();
            mesh.RecalculateBounds();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = bindPose && src.rootBone != null ? src.rootBone : bones[all[0]];
            smr.sharedMaterials = materials;
            smr.updateWhenOffscreen = true;
            if (RigDebug.On) go.AddComponent<RigDebug.Bake>().modelRoot = modelRoot;
            return smr;
        }

        static readonly string[] helperWords = { "ik", "target", "pole", "aim", "muzzle", "camera", "pivot", "hook", "chain", "rope", "root", "fx", "hurtbox", "end" };

        static bool IsHelper(string boneName)
        {
            string n = boneName.ToLowerInvariant().Replace(".", "_").Replace(" ", "_");
            // Match word starts so "spine" or "hand" do not trip on "pine"/"and".
            foreach (var part in n.Split('_', '-'))
                foreach (var w in helperWords)
                    if (part.StartsWith(w)) return true;
            return false;
        }

        static bool Named(Transform t, params string[] words)
        {
            string n = t.name.ToLowerInvariant();
            return words.Any(w => n.Contains(w)) && !n.Contains("end");
        }

        static float Median(IEnumerable<float> values)
        {
            var s = values.OrderBy(x => x).ToArray();
            return s.Length == 0 ? 0 : s[s.Length / 2];
        }

        static float DistSq(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude < 1e-8f ? 0 : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return (a + ab * t - p).sqrMagnitude;
        }
    }
}
