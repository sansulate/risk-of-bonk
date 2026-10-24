using System.Collections.Generic;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using RiskOfBonk;

// Reads each characters.json model from Megabonk the way the mod does, prints its extents and skeleton, and writes
// <id>.obj (mesh) and <id>_bones.txt (bone name, parent, model-space position, used-by-weights flag).
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
string mb = Environment.GetEnvironmentVariable("RISKOFBONK_MEGABONK") ?? @"C:\Program Files (x86)\Steam\steamapps\common\Megabonk";
if (args.Length > 0 && args[0] == "find")
{
    var lib3 = new MegabonkLibrary(s => { });
    lib3.Open(mb, Path.Combine(repo, "lib", "classdata.tpk"));
    foreach (var n in args.Skip(1))
    {
        var m = lib3.FindModel(new[] { n });
        if (m == null) { Console.WriteLine($"{n,-30} none"); continue; }
        float[] mn = { 1e9f, 1e9f, 1e9f }, mx = { -1e9f, -1e9f, -1e9f };
        for (int i = 0; i < m.Positions.Count; i++) { int k = i % 3; mn[k] = Math.Min(mn[k], m.Positions[i]); mx[k] = Math.Max(mx[k], m.Positions[i]); }
        Console.WriteLine($"{n,-30} -> {m.SourceName,-28} v={m.VertexCount,5} parts={m.SubMeshes.Count} size {mx[0]-mn[0]:0.00}x{mx[1]-mn[1]:0.00}x{mx[2]-mn[2]:0.00} tex=[{string.Join(",", m.SubMeshes.Select(s => s.Texture?.Name ?? "-"))}]");
    }
    return;
}
if (args.Length > 1 && args[0] == "prop")
{
    // prop <name> <outDir>: the prop's model as one OBJ per submesh (named by texture), with its extents.
    var libp = new MegabonkLibrary(s => Console.WriteLine("  " + s));
    libp.Open(mb, Path.Combine(repo, "lib", "classdata.tpk"));
    var prop = libp.ReadProp(args[1]);
    if (prop == null) { Console.WriteLine("none"); return; }
    var m = prop.Model;
    string dir = args.Length > 2 ? args[2] : ".";
    Directory.CreateDirectory(dir);
    for (int si = 0; si < m.SubMeshes.Count; si++)
    {
        var sm = m.SubMeshes[si];
        var used = sm.Triangles.Distinct().ToList();
        float[] mn = { 1e9f, 1e9f, 1e9f }, mx = { -1e9f, -1e9f, -1e9f };
        var sb = new StringBuilder();
        foreach (int v in used)
        {
            for (int k = 0; k < 3; k++) { mn[k] = Math.Min(mn[k], m.Positions[v * 3 + k]); mx[k] = Math.Max(mx[k], m.Positions[v * 3 + k]); }
            sb.AppendLine($"v {m.Positions[v * 3]} {m.Positions[v * 3 + 1]} {m.Positions[v * 3 + 2]}");
        }
        string tex = sm.Texture?.Name ?? "none";
        File.WriteAllText(Path.Combine(dir, $"{args[1]}_{si}_{tex}.obj"), sb.ToString());
        Console.WriteLine($"{si} {tex,-28} verts {used.Count,5} x {mn[0]:0.0}..{mx[0]:0.0} y {mn[1]:0.0}..{mx[1]:0.0} z {mn[2]:0.0}..{mx[2]:0.0}");
    }
    return;
}
if (args.Length > 0 && args[0] == "anim")
{
    var lib2 = new MegabonkLibrary(s => Console.WriteLine("  " + s));
    lib2.Open(mb, Path.Combine(repo, "lib", "classdata.tpk"));
    var doc2 = JsonDocument.Parse(File.ReadAllText(Path.Combine(repo, "sheets", "characters.json")));
    foreach (var row in doc2.RootElement.GetProperty("rows").EnumerateArray())
    {
        string id = row.GetProperty("id").GetString();
        if (args.Length > 1 && !args.Skip(1).Contains(id)) continue;
        var m = lib2.FindModel(row.GetProperty("mbNameHints").EnumerateArray().Select(e => e.GetString()).ToArray());
        if (m == null) continue;
        // every suffix path of every bone
        var paths = new Dictionary<uint, string>();
        for (int i = 0; i < m.Bones.Count; i++)
        {
            var parts = new List<string>();
            for (int b = i; b >= 0; b = m.Bones[b].Parent)
            {
                parts.Insert(0, m.Bones[b].Name);
                paths[MegabonkLibrary.PathHash(string.Join("/", parts))] = string.Join("/", parts);
            }
        }
        var clips = lib2.FindClips(m);
        foreach (var c in clips)
        {
            int matched = c.Tracks.Count(t => paths.ContainsKey(t.PathHash));
            var rot = c.Tracks.FirstOrDefault(t => t.Kind == 2);
            string q = rot == null ? "-" : string.Join(" ", Enumerable.Range(0, 3).Select(fr => { int o = fr * (c.Frames / 3) * 4; var v = rot.Values; return $"|q|={Math.Sqrt(v[o]*v[o]+v[o+1]*v[o+1]+v[o+2]*v[o+2]+v[o+3]*v[o+3]):0.000}"; }));
            Console.WriteLine($"{id,-12} {c.Name,-14} len {c.Length:0.00}s loop {c.Loop} tracks {c.Tracks.Count} matched {matched} kinds {string.Join("", c.Tracks.Select(t => t.Kind))} {q}");
            foreach (var t in c.Tracks.Where(t => !paths.ContainsKey(t.PathHash)).Take(3)) Console.WriteLine($"    unmatched {t.PathHash} kind {t.Kind}");
        }
    }
    return;
}
string outDir = args.Length > 0 ? args[0] : "out";
var only = args.Skip(1).ToHashSet();
Directory.CreateDirectory(outDir);
var lib = new MegabonkLibrary(s => { if (!s.StartsWith("skip")) Console.WriteLine("  " + s); });
lib.Open(mb, Path.Combine(repo, "lib", "classdata.tpk"));
var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(repo, "sheets", "characters.json")));
foreach (var row in doc.RootElement.GetProperty("rows").EnumerateArray())
{
    string id = row.GetProperty("id").GetString();
    if (only.Count > 0 && !only.Contains(id)) continue;
    var hints = row.GetProperty("mbNameHints").EnumerateArray().Select(e => e.GetString()).ToArray();
    var m = lib.FindModel(hints);
    if (m == null) { Console.WriteLine($"{id}: none"); continue; }
    float[] mn = { 1e9f, 1e9f, 1e9f }, mx = { -1e9f, -1e9f, -1e9f };
    for (int i = 0; i < m.Positions.Count; i++) { int k = i % 3; mn[k] = Math.Min(mn[k], m.Positions[i]); mx[k] = Math.Max(mx[k], m.Positions[i]); }
    var used = new bool[m.Bones.Count];
    for (int i = 0; i < m.BoneIndices.Count; i++) if (m.BoneWeights[i] > 0) used[m.BoneIndices[i]] = true;
    Console.WriteLine($"{id,-13} {m.SourceName,-18} v={m.VertexCount,5} skinned={m.SkinnedVertices,5} bones={m.Bones.Count,3} used={used.Count(u => u),3} " +
                      $"X {mn[0]:0.00}..{mx[0]:0.00} Y {mn[1]:0.00}..{mx[1]:0.00} Z {mn[2]:0.00}..{mx[2]:0.00} parts={m.SubMeshes.Count} tex=[{string.Join(",", m.SubMeshes.Select(s => s.Texture?.Name ?? "-"))}]");
    foreach (var sm in m.SubMeshes)
    {
        var vs = sm.Triangles.Distinct().ToArray();
        double ca = vs.Average(v => m.Colors[v * 4 + 3]), cr = vs.Average(v => m.Colors[v * 4]);
        Console.WriteLine($"    part tex={sm.Texture?.Name ?? "-"} fmt={sm.Texture?.Format} {sm.Texture?.Width}x{sm.Texture?.Height} color=({string.Join(",", sm.Color.Select(c => c.ToString("0.00")))}) vtx={vs.Length} vcolR={cr:0.00} vcolA={ca:0.00}");
    }
    var sb = new StringBuilder();
    for (int i = 0; i < m.VertexCount; i++) sb.Append($"v {m.Positions[i * 3]} {m.Positions[i * 3 + 1]} {m.Positions[i * 3 + 2]}\n");
    foreach (var sm in m.SubMeshes) for (int i = 0; i + 2 < sm.Triangles.Length; i += 3) sb.Append($"f {sm.Triangles[i] + 1} {sm.Triangles[i + 1] + 1} {sm.Triangles[i + 2] + 1}\n");
    File.WriteAllText(Path.Combine(outDir, id + ".obj"), sb.ToString());
    sb.Clear();
    for (int i = 0; i < m.Bones.Count; i++)
    {
        var w = m.Bones[i].World.M;
        sb.Append($"{i}\t{m.Bones[i].Name}\t{m.Bones[i].Parent}\t{w[3]:0.000} {w[7]:0.000} {w[11]:0.000}\t{(used[i] ? "used" : "")}\n");
    }
    File.WriteAllText(Path.Combine(outDir, id + "_bones.txt"), sb.ToString());
}
