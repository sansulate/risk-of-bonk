using System.IO;
using Path = System.IO.Path;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace RiskOfBonk
{
    /// <summary>
    /// Test keys, only when the file BepInEx/RiskOfBonk/testgod exists, and only for the host:
    /// F8 gives the player team a level, F9 puts you at the Teleporter and activates it, F10 spawns the master named in
    /// BepInEx/RiskOfBonk/testspawn (e.g. BrotherMaster) 15 m in front of you as a monster, F11 gives the next weapon from testweapons.
    /// </summary>
    public sealed class TestKeys : MonoBehaviour
    {
        public static bool On;

        void Update()
        {
            if (!On || !Run.instance || !NetworkServer.active) return;
            if (Input.GetKeyDown(KeyCode.F8)) GiveLevel();
            if (Input.GetKeyDown(KeyCode.F9)) GoToTeleporter();
            if (Input.GetKeyDown(KeyCode.F10)) Spawn();
            if (Input.GetKeyDown(KeyCode.F11)) GiveWeapon();
        }

        static void GiveLevel()
        {
            var tm = TeamManager.instance;
            if (!tm) return;
            ulong need = tm.GetTeamNextLevelExperience(TeamIndex.Player) - tm.GetTeamCurrentLevelExperience(TeamIndex.Player);
            tm.GiveTeamExperience(TeamIndex.Player, need + 1);
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

        /// <summary>F11: give the next weapon listed (comma separated ids) in BepInEx/RiskOfBonk/testweapons.</summary>
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
        }

        static void Spawn()
        {
            var body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            string file = Path.Combine(Plugin.DataDir, "testspawn");
            if (!body || !File.Exists(file)) return;
            string name = File.ReadAllText(file).Trim();
            var prefab = MasterCatalog.FindMasterPrefab(name);
            if (!prefab) { Log.Warn("testspawn: no master " + name); return; }
            var aim = body.inputBank ? body.inputBank.aimDirection : body.transform.forward;
            aim.y = 0f;
            var pos = body.footPosition + aim.normalized * 15f + Vector3.up;
            var master = new MasterSummon { masterPrefab = prefab, position = pos, rotation = Quaternion.LookRotation(-aim), teamIndexOverride = TeamIndex.Monster, ignoreTeamMemberLimit = true }.Perform();
            Log.Info($"testspawn: {name} -> {(master ? master.name : "failed")}");
        }
    }
}
