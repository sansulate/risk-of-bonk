using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace RiskOfBonk
{
    /// <summary>One bone's curves in a Megabonk clip, sampled at a fixed rate. Kind: 1 position, 2 rotation, 3 scale, 4 euler.</summary>
    public sealed class RawTrack
    {
        public uint PathHash;
        public int Kind;
        public float[] Values; // frames x (3 or 4)
        public int Width => Kind == 2 ? 4 : 3;
    }

    /// <summary>A Megabonk AnimationClip (generic), decoded from its packed curves and sampled.</summary>
    public sealed class RawClip
    {
        public string Name;
        public float Length, SampleRate;
        public bool Loop;
        public int Frames;
        public readonly List<RawTrack> Tracks = new List<RawTrack>();
    }

    public sealed partial class MegabonkLibrary
    {
        const int AnimatorId = 95, AnimatorControllerId = 91, AnimationClipId = 74;

        /// <summary>
        /// The clips of the AnimatorController that animates a Megabonk model: the Animator on one of the model's game
        /// objects (by name, Megabonk's own character prefabs first).
        /// </summary>
        public List<RawClip> FindClips(RawModel model, float sampleRate = 30f)
        {
            var names = new HashSet<string>(model.Bones.Select(b => b.Name));
            AssetExternal controller = default;
            foreach (var f in files.OrderBy(f => f.name.StartsWith("sharedassets1") ? 0 : 1))
            {
                foreach (var info in f.file.GetAssetsOfType(AnimatorId))
                {
                    var bf = am.GetBaseField(f, info);
                    var go = am.GetExtAsset(f, bf["m_GameObject"]);
                    string n = go.baseField?["m_Name"].AsString;
                    if (n == null || !names.Contains(n)) continue;
                    var c = am.GetExtAsset(f, bf["m_Controller"]);
                    if (c.baseField == null || c.info.TypeId != AnimatorControllerId) continue;
                    controller = c;
                    break;
                }
                if (controller.baseField != null) break;
            }
            var clips = new List<RawClip>();
            if (controller.baseField == null) return clips;
            var seen = new HashSet<long>();
            foreach (var p in controller.baseField["m_AnimationClips.Array"].Children)
            {
                var clip = am.GetExtAsset(controller.file, p);
                if (clip.baseField == null || !seen.Add(clip.info.PathId)) continue;
                try { clips.Add(DecodeClip(clip.baseField, sampleRate)); }
                catch (Exception e) { log($"clip {clip.baseField["m_Name"].AsString}: {e.Message}"); }
            }
            log($"{model.SourceName}: controller {controller.baseField["m_Name"].AsString}, clips {string.Join(", ", clips.Select(c => c.Name))}");
            return clips;
        }

        /// <summary>Unity's path hash of a bone path ("Armature/Hips/Spine"): CRC-32 of its UTF-8 bytes.</summary>
        public static uint PathHash(string path)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in Encoding.UTF8.GetBytes(path))
            {
                crc ^= b;
                for (int k = 0; k < 8; k++) crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
            }
            return ~crc;
        }

        static RawClip DecodeClip(AssetTypeValueField bf, float sampleRate)
        {
            var mc = bf["m_MuscleClip"];
            var clip = mc["m_Clip.data"];
            float stop = mc["m_StopTime"].AsFloat, start = mc["m_StartTime"].AsFloat;
            var raw = new RawClip
            {
                Name = bf["m_Name"].AsString,
                Length = Math.Max(1f / sampleRate, stop - start),
                SampleRate = sampleRate,
                Loop = mc["m_LoopTime"].AsBool,
            };
            raw.Frames = Math.Max(2, (int)Math.Round(raw.Length * sampleRate) + 1);

            // Streamed curves: frames of keys, each key the cubic segment of one curve from that time on.
            var streamed = clip["m_StreamedClip"];
            int nStreamed = streamed["curveCount"].AsInt;
            var words = streamed["data.Array"].Children.Select(c => c.AsUInt).ToArray();
            var bytes = new byte[words.Length * 4];
            Buffer.BlockCopy(words, 0, bytes, 0, bytes.Length);
            var segs = new List<(float t, float a, float b, float c, float d)>[nStreamed];
            for (int i = 0; i < nStreamed; i++) segs[i] = new List<(float, float, float, float, float)>();
            int pos = 0;
            while (pos + 8 <= bytes.Length)
            {
                float t = BitConverter.ToSingle(bytes, pos);
                int keys = BitConverter.ToInt32(bytes, pos + 4);
                pos += 8;
                for (int k = 0; k < keys && pos + 20 <= bytes.Length; k++, pos += 20)
                {
                    int ci = BitConverter.ToInt32(bytes, pos);
                    if (ci < 0 || ci >= nStreamed) continue;
                    segs[ci].Add((t, BitConverter.ToSingle(bytes, pos + 4), BitConverter.ToSingle(bytes, pos + 8),
                                  BitConverter.ToSingle(bytes, pos + 12), BitConverter.ToSingle(bytes, pos + 16)));
                }
            }
            float Streamed(int ci, float t)
            {
                var list = segs[ci];
                if (list.Count == 0) return 0f;
                int pick = -1;
                for (int i = 0; i < list.Count; i++)
                {
                    if (float.IsInfinity(list[i].t) || float.IsNaN(list[i].t) || Math.Abs(list[i].t) > 1e6f) continue;
                    if (list[i].t <= t + 1e-5f) pick = i; else break;
                }
                if (pick < 0)
                {
                    // Before the first key: the first real key's value.
                    var first = list.FirstOrDefault(s => Math.Abs(s.t) <= 1e6f);
                    return first.d;
                }
                var s0 = list[pick];
                float x = t - s0.t;
                return ((s0.a * x + s0.b) * x + s0.c) * x + s0.d;
            }

            // Dense curves: one sample per frame.
            var dense = clip["m_DenseClip"];
            int nDense = (int)dense["m_CurveCount"].AsUInt, denseFrames = dense["m_FrameCount"].AsInt;
            float denseRate = dense["m_SampleRate"].AsFloat, denseBegin = dense["m_BeginTime"].AsFloat;
            var denseData = dense["m_SampleArray.Array"].Children.Select(c => c.AsFloat).ToArray();
            float Dense(int ci, float t)
            {
                if (denseFrames <= 0 || nDense == 0) return 0f;
                float f = (t - denseBegin) * denseRate;
                int i0 = Math.Max(0, Math.Min(denseFrames - 1, (int)Math.Floor(f)));
                int i1 = Math.Min(denseFrames - 1, i0 + 1);
                float w = Math.Max(0f, Math.Min(1f, f - i0));
                return denseData[i0 * nDense + ci] * (1 - w) + denseData[i1 * nDense + ci] * w;
            }

            var constant = clip["m_ConstantClip.data.Array"].Children.Select(c => c.AsFloat).ToArray();
            float Curve(int ci, float t) =>
                ci < nStreamed ? Streamed(ci, t) : ci < nStreamed + nDense ? Dense(ci - nStreamed, t) : constant[ci - nStreamed - nDense];
            int total = nStreamed + nDense + constant.Length;

            // Bindings, in curve order: a position, scale or euler binding takes 3 curves, a rotation 4.
            int cur = 0;
            foreach (var b in bf["m_ClipBindingConstant.genericBindings.Array"].Children)
            {
                int kind = (int)b["attribute"].AsUInt, typeId = b["typeID"].AsInt;
                bool pptr = b["isPPtrCurve"].AsByte != 0;
                int width = typeId == TransformId && !pptr ? (kind == 2 ? 4 : kind >= 1 && kind <= 4 ? 3 : 1) : 1;
                if (pptr) continue;
                if (typeId == TransformId && kind >= 1 && kind <= 4 && cur + width <= total)
                {
                    var tr = new RawTrack { PathHash = b["path"].AsUInt, Kind = kind, Values = new float[raw.Frames * width] };
                    for (int fr = 0; fr < raw.Frames; fr++)
                    {
                        float t = start + Math.Min(raw.Length, fr / sampleRate);
                        for (int k = 0; k < width; k++) tr.Values[fr * width + k] = Curve(cur + k, t);
                    }
                    raw.Tracks.Add(tr);
                }
                cur += width;
            }
            return raw;
        }
    }
}
