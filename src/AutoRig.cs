using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RiskOfBonk
{
    /// <summary>
    /// Fits a Megabonk model onto a Risk of Rain 2 survivor's skeleton at runtime, so the Megabonk character plays the
    /// survivor's own animations. Each vertex is bound to its two nearest bone segments (inverse-distance weights).
    /// </summary>
    public static class AutoRig
    {
        public static SkinnedMeshRenderer Fit(Transform modelRoot, Transform[] bones, Mesh source, Material[] materials, float yawDeg, string name)
        {
            bones = bones.Where(b => b != null).Distinct().ToArray();
            var local = bones.Select(b => modelRoot.InverseTransformPoint(b.position)).ToArray();

            // Skeleton extents in model space, feet to head. Skins also hold helper bones (IK targets, aim pivots, hook
            // chains) that can sit far from the body, so measure from body bones only and prefer the head and feet.
            var body = Enumerable.Range(0, bones.Length).Where(i => !IsHelper(bones[i].name)).ToArray();
            if (body.Length < 3) body = Enumerable.Range(0, bones.Length).ToArray();
            var heads = body.Where(i => Named(bones[i], "head")).ToArray();
            var feet = body.Where(i => Named(bones[i], "foot", "toe", "ankle")).ToArray();
            float minY = (feet.Length > 0 ? feet : body).Min(i => local[i].y);
            float maxY = heads.Length > 0 ? heads.Max(i => local[i].y) : body.Max(i => local[i].y);
            float skeletonHeight = Mathf.Max(0.3f, maxY - minY);
            var centre = new Vector3(body.Average(i => local[i].x), 0, body.Average(i => local[i].z));
            var top = body.OrderByDescending(i => local[i].y).Take(3).Select(i => $"{bones[i].name}@{local[i].y:0.00}");
            Log.Info($"{name} on {modelRoot.name}: {bones.Length} bones ({body.Length} body), height {skeletonHeight:0.00} " +
                     $"[{minY:0.00}..{maxY:0.00}], head {(heads.Length > 0 ? bones[heads[0]].name : "-")}, top {string.Join(" ", top)}, scale {modelRoot.lossyScale}");

            // Megabonk model extents.
            var rot = Quaternion.Euler(0, yawDeg, 0);
            var verts = source.vertices.Select(v => rot * v).ToArray();
            var b = new Bounds(verts[0], Vector3.zero);
            foreach (var v in verts) b.Encapsulate(v);
            // A little taller than the joints so the head clears the head bone.
            float scale = skeletonHeight * 1.12f / Mathf.Max(0.01f, b.size.y);
            var offset = new Vector3(centre.x, minY - 0.04f * skeletonHeight, centre.z) - new Vector3(b.center.x, b.min.y, b.center.z) * scale;
            for (int i = 0; i < verts.Length; i++) verts[i] = verts[i] * scale + offset;

            // Bone segments: each body bone to each of its child bones; leaves are points. Helper bones get no vertices.
            var isBody = new HashSet<int>(body);
            var index = new Dictionary<Transform, int>();
            for (int i = 0; i < bones.Length; i++) index[bones[i]] = i;
            var segA = new List<Vector3>(); var segB = new List<Vector3>(); var segBone = new List<int>();
            for (int i = 0; i < bones.Length; i++)
            {
                if (!isBody.Contains(i)) continue;
                bool child = false;
                foreach (Transform c in bones[i])
                    if (index.TryGetValue(c, out int ci))
                    {
                        segA.Add(local[i]); segB.Add(local[ci]); segBone.Add(i); child = true;
                    }
                if (!child) { segA.Add(local[i]); segB.Add(local[i]); segBone.Add(i); }
            }

            var weights = new BoneWeight[verts.Length];
            for (int v = 0; v < verts.Length; v++)
            {
                int b0 = 0, b1 = 0; float d0 = float.MaxValue, d1 = float.MaxValue;
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

            var mesh = Object.Instantiate(source);
            mesh.name = name + "_rigged";
            mesh.vertices = verts;
            mesh.normals = source.normals.Select(n => rot * n).ToArray();
            mesh.boneWeights = weights;

            var go = new GameObject(name);
            go.transform.SetParent(modelRoot, false);
            go.layer = modelRoot.gameObject.layer;
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            mesh.bindposes = bones.Select(bt => bt.worldToLocalMatrix * go.transform.localToWorldMatrix).ToArray();
            mesh.RecalculateBounds();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = bones[0];
            smr.sharedMaterials = materials;
            smr.updateWhenOffscreen = true;
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

        static float DistSq(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude < 1e-8f ? 0 : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return (a + ab * t - p).sqrMagnitude;
        }
    }
}
