using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace RiskOfBonk
{
    /// <summary>
    /// Fit diagnostics, only when the folder BepInEx/RiskOfBonk/debug exists: writes each fitted Megabonk mesh and the
    /// skeleton it was fitted to as OBJ files (model space), and once in game the animated result.
    /// </summary>
    public static class RigDebug
    {
        public static string Dir => Path.Combine(Plugin.DataDir, "debug");
        public static bool On => Plugin.DataDir != null && Directory.Exists(Dir);

        public static void WriteFit(string name, Vector3[] verts, int[] tris, Vector3[] bones, Matrix4x4 toModel)
        {
            if (!On) return;
            var sb = new StringBuilder();
            foreach (var v in verts) Line(sb, "v", toModel.MultiplyPoint3x4(v));
            for (int i = 0; i + 2 < tris.Length; i += 3) sb.Append($"f {tris[i] + 1} {tris[i + 1] + 1} {tris[i + 2] + 1}\n");
            File.WriteAllText(Path.Combine(Dir, name + "_fit.obj"), sb.ToString());
            sb.Clear();
            foreach (var b in bones) Line(sb, "v", b);
            File.WriteAllText(Path.Combine(Dir, name + "_bones.obj"), sb.ToString());
        }

        public static void WriteSkeleton(string name, System.Collections.Generic.IEnumerable<string> lines)
        {
            if (On) File.WriteAllLines(Path.Combine(Dir, name + "_skeleton.txt"), lines);
        }

        static void Line(StringBuilder sb, string tag, Vector3 v) =>
            sb.Append(tag).Append(' ').Append(v.x.ToString(CultureInfo.InvariantCulture)).Append(' ')
              .Append(v.y.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(v.z.ToString(CultureInfo.InvariantCulture)).Append('\n');

        /// <summary>Bakes the animated mesh a few seconds after the body spawns (bodies only, not displays).</summary>
        public sealed class Bake : MonoBehaviour
        {
            public Transform modelRoot;
            float at;
            void OnEnable() => at = Time.time + 6f;
            void Update()
            {
                if (at <= 0 || Time.time < at || !On) return;
                at = 0;
                var smr = GetComponent<SkinnedMeshRenderer>();
                var m = new Mesh();
                smr.BakeMesh(m);
                var sb = new StringBuilder();
                var toModel = modelRoot.worldToLocalMatrix * transform.localToWorldMatrix;
                foreach (var v in m.vertices) Line(sb, "v", toModel.MultiplyPoint3x4(v));
                var t = m.triangles;
                for (int i = 0; i + 2 < t.Length; i += 3) sb.Append($"f {t[i] + 1} {t[i + 1] + 1} {t[i + 2] + 1}\n");
                File.WriteAllText(Path.Combine(Dir, name + "_" + transform.root.name.Replace("(Clone)", "") + "_ingame.obj"), sb.ToString());
                sb.Clear();
                foreach (var b in smr.bones) if (b != null) Line(sb, "v", modelRoot.InverseTransformPoint(b.position));
                File.WriteAllText(Path.Combine(Dir, name + "_" + transform.root.name.Replace("(Clone)", "") + "_ingame_bones.obj"), sb.ToString());
                Log.Info($"debug: baked {name}");
                foreach (var r in modelRoot.GetComponentsInChildren<Renderer>(true))
                    if (r.enabled && !r.forceRenderingOff && r.gameObject.activeInHierarchy)
                        Log.Info($"debug: {name} visible renderer {r.GetType().Name} '{r.name}' under '{r.transform.parent?.name}' " +
                                 $"shader {(r.sharedMaterial != null ? r.sharedMaterial.shader.name : "none")} bounds {r.bounds.size}");
            }
        }
    }
}
