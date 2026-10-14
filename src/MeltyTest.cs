// Melty's test mode (C#). While Melty tests a release it writes %LOCALAPPDATA%\Melty\test\risk-of-bonk-3.json; its "checks" names the file to append one line per check to, verdict first: "pass <name>" or "fail <name>: why". Report every name in recipe test.checks (and list only names you report). Never quit the game in a test run: Melty closes it when the run's minutes are up. Without the file (a player's game), nothing happens. Call MeltyTest.Start() from your BaseUnityPlugin's Awake(), then MeltyTest.Report(...) as each check happens.
using System;
using System.IO;
using System.Text.RegularExpressions;

public static class MeltyTest
{
    static string checksFile;

    public static void Start()
    {
        string test = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Melty", "test", "risk-of-bonk-3.json");
        if (!File.Exists(test)) return; // not a test run
        Match match = Regex.Match(File.ReadAllText(test), "\"checks\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        if (match.Success) checksFile = Regex.Unescape(match.Groups[1].Value);
        Report("mod-loads", true, null);
    }

    public static void Report(string name, bool passed, string why)
    {
        if (checksFile == null) return;
        try { File.AppendAllText(checksFile, (passed ? "pass " : "fail ") + name + (passed || why == null ? "" : ": " + why) + Environment.NewLine); }
        catch (Exception) { /* nothing more to do: the run says the line never came */ }
    }
}
