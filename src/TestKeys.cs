using System.IO;
using Path = System.IO.Path;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace RiskOfBonk
{
    /// <summary>
    /// Test keys, only when the file BepInEx/RiskOfBonk/testgod exists, and only for the host:
    /// F1 puts you in front of the next chest, barrel, shrine or printer, F2 opens the nearest chest (paid for you), F3 kills every monster, F4 puts a long wall 2.5 m to your right (for wall climbing), F5 toggles the Megabonk look, F6 puts you in a Megabonk shrine (rarest kind first), F7 toggles slow motion (for screenshots of effects), F8 gives the player team a level, F9 puts you at the Teleporter and activates it, F10 spawns the master named in
    /// BepInEx/RiskOfBonk/testspawn (e.g. BrotherMaster) 15 m in front of you as a monster, F11 gives the next weapon from testweapons, F12 goes to the next stage.
    /// </summary>
    public sealed class TestKeys : MonoBehaviour
    {
        public static bool On;

        bool assetsChecked;

        // Fight numbers for balance checks: logged every 15 s of a run.
        static float dealt, taken, nextReport;
        static int kills, deaths;
        static bool hooked;

        static void Hook()
        {
            hooked = true;
            GlobalEventManager.onServerDamageDealt += r =>
            {
                if (!On || r == null || r.damageInfo == null) return;
                var victim = r.victimBody;
                if (victim && victim.isPlayerControlled) taken += r.damageDealt;
                else if (r.attackerBody && r.attackerBody.isPlayerControlled) dealt += r.damageDealt;
            };
            GlobalEventManager.onCharacterDeathGlobal += r =>
            {
                if (!On || r == null) return;
                if (r.victimBody && r.victimBody.isPlayerControlled) deaths++;
                else if (r.attackerBody && r.attackerBody.isPlayerControlled) kills++;
            };
            Run.onRunStartGlobal += _ => { dealt = taken = 0f; kills = deaths = 0; nextReport = 0f; };
        }

        static void Report()
        {
            var run = Run.instance;
            if (!run || run.GetRunStopwatch() < nextReport) return;
            nextReport = run.GetRunStopwatch() + 15f;
            var body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            int monsters = TeamComponent.GetTeamMembers(TeamIndex.Monster).Count;
            float t = Mathf.Max(1f, run.GetRunStopwatch());
            Log.Info($"fight: {t:0}s {(body ? body.GetDisplayName() : "dead")} lv {(body ? body.level : 0):0} hp {(body && body.healthComponent ? body.healthComponent.combinedHealth : 0):0}/{(body && body.healthComponent ? body.healthComponent.fullCombinedHealth : 0):0}, " +
                     $"dealt {dealt:0} ({dealt / t:0.#}/s), taken {taken:0} ({taken / t:0.#}/s), kills {kills}, deaths {deaths}, monsters alive {monsters}, difficulty {run.difficultyCoefficient:0.##}");
        }

        void Update()
        {
            if (On && !hooked) Hook();
            if (On && UnityEngine.Networking.NetworkServer.active) Report();
            if (On && !assetsChecked && RoR2.UI.MainMenu.MainMenuController.instance)
            {
                // Load every weapon asset once, so a wrong address shows up in the log right away.
                assetsChecked = true;
                foreach (var w in RiskOfBonk.Generated.Sheets.Weapons) { WeaponFire.Prefab(w.Prefab); WeaponFire.Prefab(w.Effect); }
                Log.Info("test: weapon assets checked");
            }
            if (!On || !Run.instance || !NetworkServer.active) return;
            if (Input.GetKeyDown(KeyCode.F1)) GoToInteractable();
            if (Input.GetKeyDown(KeyCode.F2)) OpenChest();
            if (Input.GetKeyDown(KeyCode.F3)) KillMonsters();
            if (Input.GetKeyDown(KeyCode.F4)) TestWall();
            if (Input.GetKeyDown(KeyCode.F5)) StageLook.Toggle();
            if (Input.GetKeyDown(KeyCode.F6)) GoToShrine();
            if (Input.GetKeyDown(KeyCode.F7)) Time.timeScale = Time.timeScale < 1f ? 1f : 0.15f;
            if (Input.GetKeyDown(KeyCode.F8)) GiveLevel();
            if (Input.GetKeyDown(KeyCode.F9)) GoToTeleporter();
            if (Input.GetKeyDown(KeyCode.F10)) Spawn();
            if (Input.GetKeyDown(KeyCode.F11)) GiveWeapon();
            if (Input.GetKeyDown(KeyCode.F12) && Run.instance.nextStageScene) Run.instance.AdvanceStage(Run.instance.nextStageScene);
        }

        static void GiveLevel()
        {
            var tm = TeamManager.instance;
            if (!tm) return;
            ulong need = tm.GetTeamNextLevelExperience(TeamIndex.Player) - tm.GetTeamCurrentLevelExperience(TeamIndex.Player);
            tm.GiveTeamExperience(TeamIndex.Player, need + 1);
        }

        /// <summary>F2: the nearest closed item chest opens for the local player (its price is paid for them).</summary>
        static void OpenChest()
        {
            var body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            var interactor = body ? body.GetComponent<Interactor>() : null;
            if (!interactor || !body.master) return;
            PurchaseInteraction best = null;
            float bestD = float.MaxValue;
            foreach (var pi in Object.FindObjectsOfType<PurchaseInteraction>())
            {
                if (!pi.available || !pi.GetComponent<ChestBehavior>() || !pi.name.Contains("Chest")) continue;
                float d = (pi.transform.position - body.corePosition).sqrMagnitude;
                if (d < bestD) { bestD = d; best = pi; }
            }
            if (!best) { Log.Info("test: no chest"); return; }
            body.master.GiveMoney((uint)best.cost + 1);
            interactor.AttemptInteraction(best.gameObject);
            Log.Info($"test: opened {best.name}");
        }

        static int interactableIndex;

        /// <summary>F1: stand 6 m in front of the next chest, barrel, shrine or printer (in turn), looking at it.</summary>
        static void GoToInteractable()
        {
            var body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            if (!body || !body.inputBank) return;
            var list = new System.Collections.Generic.List<GameObject>();
            foreach (var pi in Object.FindObjectsOfType<PurchaseInteraction>()) list.Add(pi.gameObject);
            foreach (var bi in Object.FindObjectsOfType<BarrelInteraction>()) list.Add(bi.gameObject);
            if (list.Count == 0) { Log.Info("test: no interactables"); return; }
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name) != 0 ? string.CompareOrdinal(a.name, b.name) : a.transform.position.x.CompareTo(b.transform.position.x));
            var go = list[interactableIndex++ % list.Count];
            var fwd = body.inputBank.aimDirection; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            TeleportHelper.TeleportBody(body, go.transform.position - fwd.normalized * 6f + Vector3.Cross(Vector3.up, fwd.normalized) * 2.5f + Vector3.up * 1.5f);
            Log.Info($"test: at {go.name} ({interactableIndex}/{list.Count})");
        }

        static void KillMonsters()
        {
            foreach (var tc in new System.Collections.Generic.List<TeamComponent>(TeamComponent.GetTeamMembers(TeamIndex.Monster)))
                if (tc && tc.body && tc.body.healthComponent) tc.body.healthComponent.Suicide();
        }

        static void TestWall()
        {
            var body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            if (!body || !body.inputBank) return;
            var fwd = body.inputBank.aimDirection; fwd.y = 0f; fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "RiskOfBonkTestWall";
            wall.layer = LayerIndex.world.intVal;
            wall.transform.position = body.footPosition + right * 3f + fwd * 25f + Vector3.up * 6f;
            wall.transform.rotation = Quaternion.LookRotation(fwd);
            wall.transform.localScale = new Vector3(1f, 14f, 60f);
        }

        static int shrineIndex;

        static void GoToShrine()
        {
            var body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            var pos = Shrines.Position(shrineIndex++);
            if (body && pos.HasValue) TeleportHelper.TeleportBody(body, pos.Value + Vector3.up * 1.5f);
            shrineIndex = 0; // the shrine just visited is used up soon; the next press goes to the next rarest kind
        }

        static void GoToTeleporter()
        {
            var tp = TeleporterInteraction.instance;
            var body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            if (!tp || !body) return;
            TeleportHelper.TeleportBody(body, tp.transform.position + Vector3.up * 2f + tp.transform.forward * 4f);
            var interactor = body.GetComponent<Interactor>();
            if (interactor) interactor.AttemptInteraction(tp.gameObject);
        }

        /// <summary>F11: give the next weapon listed (comma separated ids) in BepInEx/RiskOfBonk/testweapons; once all are owned, level up the first.</summary>
        static void GiveWeapon()
        {
            var body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            string file = Path.Combine(Plugin.DataDir, "testweapons");
            if (!body || !body.inventory || !File.Exists(file)) return;
            foreach (var id in File.ReadAllText(file).Split(','))
            {
                int w = Weapons.Index(id.Trim());
                if (w < 0 || Weapons.Level(body, w) > 0) continue;
                body.inventory.GiveItemPermanent(Weapons.Items[w], 1);
                Log.Info("testweapons: gave " + id.Trim());
                return;
            }
            // All owned: level up the first listed one (the start weapon counts as owned).
            int first = Weapons.Index(File.ReadAllText(file).Split(',')[0].Trim());
            if (first < 0) return;
            body.inventory.GiveItemPermanent(Weapons.Items[first], 1);
            var row = RiskOfBonk.Generated.Sheets.Weapons[first];
            Log.Info($"testweapons: {row.Id} now level {Weapons.Level(body, first)} ({Weapons.Describe(row, Weapons.Level(body, first) - 1, 1)})");
        }

        static int spawnIndex;

        static void Spawn()
        {
            var body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            string file = Path.Combine(Plugin.DataDir, "testspawn");
            if (!body || !File.Exists(file)) return;
            // One master name, or a comma separated list: each press spawns the next, side by side.
            var names = File.ReadAllText(file).Split(',');
            int k = spawnIndex++ % names.Length;
            string name = names[k].Trim();
            var prefab = MasterCatalog.FindMasterPrefab(name);
            if (!prefab) { Log.Warn("testspawn: no master " + name); return; }
            var aim = body.inputBank ? body.inputBank.aimDirection : body.transform.forward;
            aim.y = 0f;
            var side = Vector3.Cross(Vector3.up, aim.normalized) * (names.Length > 1 ? (k - (names.Length - 1) * 0.5f) * 5f : 0f);
            var pos = body.footPosition + aim.normalized * 15f + side + Vector3.up;
            var master = new MasterSummon { masterPrefab = prefab, position = pos, rotation = Quaternion.LookRotation(-aim), teamIndexOverride = TeamIndex.Monster, ignoreTeamMemberLimit = true }.Perform();
            Log.Info($"testspawn: {name} -> {(master ? master.name : "failed")}");
        }
    }
}
