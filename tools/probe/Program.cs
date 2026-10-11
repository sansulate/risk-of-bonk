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
