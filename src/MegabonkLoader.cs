using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace RiskOfBonk
{
    /// <summary>Finds the player's own Megabonk install and opens it on a worker thread.</summary>
    public static class MegabonkLoader
    {
        public const string EnvVar = "RISKOFBONK_MEGABONK";
        public static volatile bool Done;
        public static MegabonkLibrary Library;
        public static string Error = "";
        public static string GameDir;

        public static void Start(string configuredDir, string ror2Dir, string tpkPath, string inventoryPath)
        {
            GameDir = Locate(configuredDir, ror2Dir);
            if (GameDir == null)
            {
                Error = $"Set {EnvVar} or the MegabonkFolder config to your Megabonk folder.";
                Done = true;
                return;
            }
            Log.Info("Megabonk folder: " + GameDir);
            new Thread(() =>
            {
                try
                {
                    var lib = new MegabonkLibrary(Log.Info);
                    lib.Open(GameDir, tpkPath);
                    try { lib.WriteInventory(inventoryPath); } catch (Exception e) { Log.Warn("inventory: " + e.Message); }
                    Library = lib;
                }
                catch (Exception e) { Error = e.ToString(); }
                finally { Done = true; }
            }) { IsBackground = true, Name = "RiskOfBonk Megabonk reader" }.Start();
        }

        static bool IsMegabonk(string dir) =>
            !string.IsNullOrEmpty(dir) && Directory.Exists(dir) && Directory.GetDirectories(dir, "*_Data").Any(d => File.Exists(Path.Combine(d, "globalgamemanagers")))
            && (File.Exists(Path.Combine(dir, "Megabonk.exe")) || Directory.Exists(Path.Combine(dir, "Megabonk_Data")));

        static string Locate(string configured, string ror2Dir)
        {
            foreach (var c in new[] { Environment.GetEnvironmentVariable(EnvVar), configured })
                if (IsMegabonk(c?.Trim().Trim('"'))) return c.Trim().Trim('"');

            // Steam libraries, starting from the library Risk of Rain 2 is installed in.
            var libs = new List<string>();
            try
            {
                var common = Directory.GetParent(ror2Dir);              // steamapps/common
                var steamapps = common?.Parent;                         // steamapps
                if (steamapps != null)
                {
                    libs.Add(steamapps.Parent.FullName);
                    foreach (var vdf in new[] { Path.Combine(steamapps.FullName, "libraryfolders.vdf") })
                        if (File.Exists(vdf))
                            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                                libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
                }
            }
            catch { }
            libs.Add(@"C:\Program Files (x86)\Steam");
            foreach (var l in libs.Distinct())
            {
                var d = Path.Combine(l, "steamapps", "common", "Megabonk");
                if (IsMegabonk(d)) return d;
            }
            return null;
        }
    }
}
