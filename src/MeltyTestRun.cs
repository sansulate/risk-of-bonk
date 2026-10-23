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
        DateTime until = DateTime.MaxValue;
        bool quitting;

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
                m = Regex.Match(json, "\"until\"\\s*:\\s*\"([^\"]+)\"");
                if (m.Success && DateTime.TryParse(m.Groups[1].Value, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var u)) until = u;
            }
            catch (Exception e) { Log.Warn("Melty test: " + e.Message); }
            Log.Info($"Melty test run {run}, seed {seed}");
            StartCoroutine(Play());
        }

        void Update()
        {
            // Closing the game at the run's end could hang on this PC (the window gone, the process left behind), so a
            // test run ends itself a few seconds early, once its checks are in; a stuck exit is cut short.
            if (quitting || DateTime.UtcNow < until.AddSeconds(-8)) return;
            quitting = true;
            Log.Info("Melty test: run time is up, closing the game");
            new System.Threading.Thread(() =>
            {
                System.Threading.Thread.Sleep(15000);
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            }) { IsBackground = true }.Start();
            Application.Quit();
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
            // The main menu's own Start (the catalogs are filled by then), and the Megabonk survivors in SurvivorCatalog.
            System.Collections.Generic.List<SurvivorDef> survivors = null;
            yield return WaitFor(() =>
            {
                if (!MegabonkLoader.Done || !Plugin.MainMenuReached || !LocalUserManager.isAnyUserSignedIn) return false;
                survivors = SurvivorCatalog.allSurvivorDefs.Where(s => s && s.bodyPrefab && s.bodyPrefab.GetComponent<Weapons.WeaponLoadout>()).ToList();
                return survivors.Count > 0 || MegabonkLoader.Library == null;
            }, 300f);
            survivors = survivors ?? new System.Collections.Generic.List<SurvivorDef>();
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
            // Characters built on an expansion survivor (Void Fiend, Railgunner) become Commando on a PC without that
            // expansion, so the test picks from the ones every copy of the game can play.
            var playable = survivors.Where(s => !s.bodyPrefab.GetComponent<RoR2.ExpansionManagement.ExpansionRequirementComponent>()).ToList();
            if (playable.Count > 0) survivors = playable;
            var survivor = survivors[seed % survivors.Count];
            // The character select screen can put the profile's last survivor back after this pick, so the profile gets
            // the pick too and it is sent again until the lobby has it.
            var bodyIndex = BodyCatalog.FindBodyIndex(survivor.bodyPrefab);
            try { LocalUserManager.GetFirstLocalUser()?.userProfile?.SetSurvivorPreference(survivor); } catch (Exception e) { Log.Warn("Melty test: " + e.Message); }
            for (float pickUntil = Time.unscaledTime + 15f; Time.unscaledTime < pickUntil; )
            {
                user = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;
                if (!user) break;
                if (user.bodyIndexPreference == bodyIndex) break;
                user.SetSurvivorPreferenceClient(survivor);
                yield return new WaitForSecondsRealtime(0.5f);
            }
            yield return new WaitForSecondsRealtime(1f);
            user = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;
            if (user && user.bodyIndexPreference != bodyIndex) user.SetSurvivorPreferenceClient(survivor);
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
