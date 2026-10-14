using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using Path = System.IO.Path;

namespace RiskOfBonk
{
    /// <summary>
    /// Melty's test runs (only while %LOCALAPPDATA%/Melty/test/risk-of-bonk-3.json exists): plays the mod with no one at
    /// the keyboard. From the main menu it starts a single-player run as a Megabonk survivor (picked by the run's seed;
    /// even runs in Bonk style), fires its weapon, levels up and picks a card, reporting each step to MeltyTest.
    /// </summary>
    public sealed class MeltyTestRun : MonoBehaviour
    {
        static string TestFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Melty", "test", "risk-of-bonk-3.json");

        int seed, run = 1;

        void Start()
        {
            if (!File.Exists(TestFile)) { enabled = false; return; }
            try
            {
                string json = File.ReadAllText(TestFile);
                var m = Regex.Match(json, "\"seed\"\\s*:\\s*(-?\\d+)");
                if (m.Success) seed = (int)(long.Parse(m.Groups[1].Value) & 0x7fffffff);
                m = Regex.Match(json, "\"run\"\\s*:\\s*(\\d+)");
                if (m.Success) run = int.Parse(m.Groups[1].Value);
            }
            catch (Exception e) { Log.Warn("Melty test: " + e.Message); }
            Log.Info($"Melty test run {run}, seed {seed}");
            StartCoroutine(Play());
        }

        static IEnumerator WaitFor(Func<bool> done, float seconds)
        {
            float until = Time.unscaledTime + seconds;
            while (!done() && Time.unscaledTime < until) yield return null;
        }

        static void Report(string name, bool ok, string why)
        {
            MeltyTest.Report(name, ok, why);
            Log.Info($"Melty test: {(ok ? "pass" : "fail")} {name}{(ok || why == null ? "" : ": " + why)}");
        }

        IEnumerator Play()
        {
            // 1. Megabonk's characters read from the player's Megabonk install.
            yield return WaitFor(() => MegabonkLoader.Done && FindObjectOfType<RoR2.UI.MainMenu.MainMenuController>() && LocalUserManager.isAnyUserSignedIn, 240f);
            var survivors = SurvivorCatalog.allSurvivorDefs.Where(s => s && s.bodyPrefab && s.bodyPrefab.GetComponent<Weapons.WeaponLoadout>()).ToList();
            bool loaded = MegabonkLoader.Library != null && survivors.Count > 0;
            Report("megabonk-loaded", loaded, loaded ? null : (MegabonkLoader.Error != "" ? MegabonkLoader.Error : "no Megabonk survivors registered"));
            if (!loaded) yield break;
            yield return new WaitForSecondsRealtime(3f);

            // 2. A single-player run as a Megabonk survivor.
            RoR2.Console.instance.SubmitCmd(null, "host 0");
            yield return WaitFor(() => PreGameController.instance && LocalUserManager.GetFirstLocalUser()?.currentNetworkUser, 60f);
            var pregame = PreGameController.instance;
            var user = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;
            if (!pregame || !user) { Report("run-started", false, "character select did not open"); yield break; }
            yield return new WaitForSecondsRealtime(2f);
            var survivor = survivors[seed % survivors.Count];
            user.SetSurvivorPreferenceClient(survivor);
            if (run % 2 == 0 && !StageMode.IsBonk) StageMode.Toggle();
            yield return new WaitForSecondsRealtime(1f);
            Log.Info($"Melty test: {survivor.cachedName}, {StageMode.Row.Label}");
            pregame.StartLaunch();
            yield return WaitFor(() => Run.instance && LocalUserManager.GetFirstLocalUser()?.cachedBody, 120f);
            var body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            bool started = body && Weapons.IsMegabonk(body);
            Report("run-started", started, started ? null : body ? "the body is not a Megabonk survivor: " + body.name : "no player body within 2 minutes");
            if (!started) yield break;
            if (NetworkServer.active && body.healthComponent) body.healthComponent.godMode = true; // nobody is playing
            yield return new WaitForSecondsRealtime(3f);

            // 3. The start weapon on left mouse.
            int before = WeaponFire.Fired;
            float until = Time.unscaledTime + 8f;
            while (WeaponFire.Fired == before && Time.unscaledTime < until)
            {
                body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
                if (body && body.skillLocator && body.skillLocator.primary) body.skillLocator.primary.ExecuteIfReady();
                yield return new WaitForSecondsRealtime(0.2f);
            }
            string weapon = body && body.skillLocator && body.skillLocator.primary ? body.skillLocator.primary.skillDef?.skillName : "none";
            Report("weapon-fires", WeaponFire.Fired > before, WeaponFire.Fired > before ? null : "the start weapon (" + weapon + ") never attacked");

            // 4. A level-up opens the Megabonk card menu.
            var master = LocalUserManager.GetFirstLocalUser()?.cachedMaster;
            var tm = TeamManager.instance;
            if (tm && NetworkServer.active)
            {
                ulong need = tm.GetTeamNextLevelExperience(TeamIndex.Player) - tm.GetTeamCurrentLevelExperience(TeamIndex.Player);
                tm.GiveTeamExperience(TeamIndex.Player, need + 1);
            }
            yield return WaitFor(() => UpgradePanel.Open, 10f);
            Report("levelup-menu", UpgradePanel.Open, UpgradePanel.Open ? null : "no card menu within 10 s of a level-up");
            if (!UpgradePanel.Open || !master) { Report("upgrade-picked", false, "no menu to pick from"); yield break; }

            // 5. Picking the first card.
            int picks = master.inventory.GetItemCountPermanent(Upgrades.PicksItem);
            yield return new WaitForSecondsRealtime(1.5f);
            UpgradePanel.TestPick = 0;
            yield return WaitFor(() => master.inventory.GetItemCountPermanent(Upgrades.PicksItem) > picks, 10f);
            bool picked = master.inventory.GetItemCountPermanent(Upgrades.PicksItem) > picks;
            Report("upgrade-picked", picked, picked ? null : "the pick never reached the inventory");
            // The run goes on (Melty closes the game when the run's minutes are up).
        }
    }
}
