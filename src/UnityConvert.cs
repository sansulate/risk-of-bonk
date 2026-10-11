using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RiskOfBonk
{
    /// <summary>Turns raw Megabonk data into Unity objects (main thread only).</summary>
    public static class UnityConvert
    {
        static readonly Dictionary<RawTexture, Texture2D> textures = new Dictionary<RawTexture, Texture2D>();

        public static Texture2D Texture(RawTexture raw, MegabonkLibrary lib = null)
        {
            if (raw == null) return null;
            if (textures.TryGetValue(raw, out var t)) return t;
            try
            {
                var fmt = (TextureFormat)raw.Format;
                bool mips = raw.MipCount > 1;
                t = new Texture2D(raw.Width, raw.Height, fmt, mips) { name = "MB_" + raw.Name, wrapMode = TextureWrapMode.Repeat };
                try { t.LoadRawTextureData(raw.Data); }
                catch
                {
                    // Mip count mismatch: retry without mips using only the first level.
                    UnityEngine.Object.Destroy(t);
                    t = new Texture2D(raw.Width, raw.Height, fmt, false) { name = "MB_" + raw.Name };
                    int size = LevelSize(fmt, raw.Width, raw.Height);
                    if (size <= 0 || size > raw.Data.Length) throw;
                    var first = new byte[size];
                    Buffer.BlockCopy(raw.Data, 0, first, 0, size);
                    t.LoadRawTextureData(first);
                }
                t.Apply(false, false);
            }
            catch (Exception e)
            {
                Log.Warn($"Megabonk texture {raw.Name} ({raw.Width}x{raw.Height}, format {raw.Format}) could not be loaded: {e.Message}");
                t = null;
            }
            if (t != null && lib != null && lib.SpriteRects.TryGetValue(raw, out var r)) t = Crop(t, r);
            textures[raw] = t;
            return t;
        }

        static int LevelSize(TextureFormat f, int w, int h)
        {
            switch (f)
            {
                case TextureFormat.DXT1: case TextureFormat.BC4: return Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * 8;
                case TextureFormat.DXT5: case TextureFormat.BC5: case TextureFormat.BC7: case TextureFormat.BC6H:
                    return Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * 16;
                case TextureFormat.RGBA32: case TextureFormat.ARGB32: case TextureFormat.BGRA32: return w * h * 4;
                case TextureFormat.RGB24: return w * h * 3;
                case TextureFormat.Alpha8: case TextureFormat.R8: return w * h;
            }
            return -1;
        }

        /// <summary>Cuts a sprite out of its atlas through the GPU, so compressed atlases work too.</summary>
        static Texture2D Crop(Texture2D atlas, float[] r)
        {
            int x = (int)r[0], y = (int)r[1], w = Mathf.Max(1, (int)r[2]), h = Mathf.Max(1, (int)r[3]);
            if (x == 0 && y == 0 && w == atlas.width && h == atlas.height) return atlas;
            var rt = RenderTexture.GetTemporary(atlas.width, atlas.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(atlas, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var outT = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = atlas.name + "_crop" };
            outT.ReadPixels(new Rect(x, y, w, h), 0, 0);
            outT.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return outT;
        }

        public static Mesh Mesh(RawModel m)
        {
            int n = m.VertexCount;
            var pos = new Vector3[n];
            var nor = new Vector3[n];
            var uv = new Vector2[n];
            var col = new Color[n];
            for (int i = 0; i < n; i++)
            {
                pos[i] = new Vector3(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);
                nor[i] = new Vector3(m.Normals[i * 3], m.Normals[i * 3 + 1], m.Normals[i * 3 + 2]);
                uv[i] = new Vector2(m.UVs[i * 2], m.UVs[i * 2 + 1]);
                col[i] = new Color(m.Colors[i * 4], m.Colors[i * 4 + 1], m.Colors[i * 4 + 2], m.Colors[i * 4 + 3]);
            }
            var mesh = new Mesh { name = "MB_" + m.SourceName, indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = pos;
            mesh.normals = nor;
            mesh.uv = uv;
            mesh.colors = col;
            mesh.subMeshCount = m.SubMeshes.Count;
            for (int s = 0; s < m.SubMeshes.Count; s++) mesh.SetTriangles(m.SubMeshes[s].Triangles, s);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
