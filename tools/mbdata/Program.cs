using System.Globalization;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
// usage: mbread <megabonkDataDir> <tpk> <dummyDllDir> <ClassName[,Class2]> [nameFilter]   (RAW=1: int/float words)
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
bool raw = Environment.GetEnvironmentVariable("RAW") == "1";
var dataDir = args[0];
var am = new AssetsManager();
am.LoadClassPackage(args[1]);
am.MonoTempGenerator = new MonoCecilTempGenerator(args[2]);
var classes = args[3].Split(',').ToHashSet();
string filter = args.Length > 4 ? args[4] : null;
var files = Directory.GetFiles(dataDir).Where(f => { var n = Path.GetFileName(f); return n.EndsWith(".assets") || n.StartsWith("level") && !n.Contains('.') || n == "globalgamemanagers"; }).ToList();
AssetsFileInstance cur = null;
foreach (var path in files)
{
    AssetsFileInstance inst;
    try { inst = am.LoadAssetsFile(path, true); } catch { continue; }
    cur = inst;
    if (am.ClassDatabase == null) am.LoadClassDatabaseFromPackage(inst.file.Metadata.UnityVersion);
    foreach (var info in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
    {
        string cls;
        string goName = "";
        try
        {
            var baseField = am.GetBaseField(inst, info, AssetReadFlags.SkipMonoBehaviourFields);
            var script = am.GetExtAsset(inst, baseField["m_Script"]);
            if (script.baseField == null) continue;
            cls = script.baseField["m_ClassName"].AsString;
            if (!classes.Contains(cls)) continue;
            try { var go = am.GetExtAsset(inst, baseField["m_GameObject"]); if (go.baseField != null) goName = go.baseField["m_Name"].AsString; } catch { }
        }
        catch { continue; }
        if (raw)
        {
            var r = inst.file.Reader; r.Position = info.GetAbsoluteByteOffset(inst.file);
            var bytes = r.ReadBytes((int)info.ByteSize);
            int nl = BitConverter.ToInt32(bytes, 28); string nm = nl >= 0 && nl < 200 ? Encoding.UTF8.GetString(bytes, 32, nl) : "?";
            if (filter != null && !(nm + " " + goName).Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            var bin = Environment.GetEnvironmentVariable("BIN");
            if (bin != null) { Directory.CreateDirectory(bin); File.WriteAllBytes(Path.Combine(bin, $"{cls}__{(nm.Length > 0 ? nm : goName)}__{Path.GetFileName(path)}_{info.PathId}.bin"), bytes); continue; }
            Console.WriteLine($"=== RAW {cls} '{nm}' go '{goName}' {bytes.Length} bytes");
            for (int o = 0; o + 4 <= bytes.Length; o += 4)
            {
                var s = new StringBuilder();
                for (int k = 0; k < 4; k++) { char ch = (char)bytes[o + k]; s.Append(ch >= 32 && ch < 127 ? ch : '.'); }
                Console.WriteLine($"{o,5}: {BitConverter.ToInt32(bytes, o),12} {BitConverter.ToSingle(bytes, o),14:G6}  {s}");
            }
            continue;
        }
        AssetTypeValueField full;
        try { full = am.GetBaseField(inst, info); } catch (Exception e) { Console.WriteLine($"! {cls} {e.Message}"); continue; }
        string name = full["m_Name"].IsDummy ? "" : full["m_Name"].AsString;
        if (filter != null && !(name + " " + goName).Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
        Console.WriteLine($"=== {cls} '{name}' go '{goName}' ({Path.GetFileName(path)} #{info.PathId})");
        Dump(full, 0);
    }
}

void Dump(AssetTypeValueField f, int depth)
{
    foreach (var c in f.Children)
    {
        string n = c.FieldName;
        if (depth == 0 && (n == "m_GameObject" || n == "m_Enabled" || n == "m_Script" || n == "m_Name" || n == "m_EditorHideFlags" || n == "m_EditorClassIdentifier")) continue;
        var pad = new string(' ', depth * 2 + 2);
        if (c.TypeName.StartsWith("PPtr"))
        {
            long pid = c["m_PathID"].AsLong; int fid = c["m_FileID"].AsInt;
            string target = "";
            if (pid != 0) try { var ext = am.GetExtAsset(cur, c); if (ext.baseField != null && !ext.baseField["m_Name"].IsDummy) target = ext.baseField["m_Name"].AsString; } catch { }
            Console.WriteLine($"{pad}{n} -> {(pid == 0 ? "null" : target + $" ({fid}:{pid})")}");
        }
        else if (c.Value != null && c.Value.ValueType != AssetValueType.Array && c.Value.ValueType != AssetValueType.ByteArray)
            Console.WriteLine($"{pad}{n} = {c.Value.AsString}");
        else if (c.Children.Count > 0 && depth < 6)
        {
            if (c.TypeName == "Array" || c.TypeName == "vector") { Console.WriteLine($"{pad}{n} [{c.Children.Count}]"); if (c.Children.Count <= 40) Dump(c, depth + 1); }
            else { Console.WriteLine($"{pad}{n}:"); Dump(c, depth + 1); }
        }
    }
}
