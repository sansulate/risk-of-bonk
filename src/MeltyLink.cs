using System;
using System.IO;
using RoR2;
using UnityEngine;
using Path = System.IO.Path;
using PlatformID = RoR2.PlatformID;

namespace RiskOfBonk
{
    /// <summary>
    /// Melty's join links, through Risk of Rain 2's own Steam lobbies (no ports to forward). Hosting: once this player
    /// owns a lobby (the Multiplayer menu opens one), "Melty join address: &lt;lobby id&gt;.steamlobby" goes into
    /// %LOCALAPPDATA%/RiskOfBonk/melty.log. Joining: Melty writes join=&lt;that address&gt; in melty.properties; at the
    /// main menu the game joins that lobby (as Steam's +connect_lobby does) and the log says "Melty joined: ...".
    /// </summary>
    public sealed class MeltyLink : MonoBehaviour
    {
        static string dir, logPath;

        static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RiskOfBonk");

        /// <summary>A key=value line from %LOCALAPPDATA%/RiskOfBonk/melty.properties (Melty writes join= and megabonk=).</summary>
        public static string Property(string key)
        {
            try
            {
                string props = Path.Combine(Folder, "melty.properties");
                if (!File.Exists(props)) return null;
                foreach (var raw in File.ReadAllLines(props))
                {
                    var line = raw.Trim();
                    if (line.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) return line.Substring(key.Length + 1).Trim();
                }
            }
            catch (Exception e) { Log.Warn("melty.properties: " + e.Message); }
            return null;
        }
        string join;
        ulong joinId;
        bool joinSent, joinDone;
        float joinStarted;
        string hostedId;

        static void Write(string line)
        {
            try { File.AppendAllText(logPath, line + Environment.NewLine); }
            catch (Exception e) { Log.Warn("melty.log: " + e.Message); }
        }

        void Awake()
        {
            try
            {
                dir = Folder;
                Directory.CreateDirectory(dir);
                logPath = Path.Combine(dir, "melty.log");
                File.WriteAllText(logPath, "");
                join = Property("join");
                if (string.IsNullOrEmpty(join)) return;
                string id = join.EndsWith(".steamlobby", StringComparison.OrdinalIgnoreCase) ? join.Substring(0, join.Length - 11) : join;
                if (!ulong.TryParse(id, out joinId))
                {
                    Write("Melty join failed: not a Steam lobby address: " + join);
                    join = null;
                    return;
                }
                Log.Info("Melty: joining " + join);
            }
            catch (Exception e) { Log.Warn("Melty link: " + e.Message); }
        }

        void Update()
        {
            var lobbies = PlatformSystems.lobbyManager;
            if (lobbies == null) return;
            try
            {
                if (join != null && !joinDone) Join(lobbies);
                // Hosting: a lobby this player owns is the game friends join.
                string id = lobbies.isInLobby && lobbies.ownsLobby ? lobbies.GetLobbyID() : null;
                if (!string.IsNullOrEmpty(id) && id != "0" && id != hostedId)
                {
                    hostedId = id;
                    Write("Melty join address: " + id + ".steamlobby");
                    Log.Info("Melty: hosting lobby " + id);
                }
            }
            catch (Exception e) { Log.Warn("Melty link: " + e.Message); join = null; }
        }

        void Join(LobbyManager lobbies)
        {
            if (!joinSent)
            {
                // Wait for the main menu and a signed-in profile, like a Steam invite does.
                if (!LocalUserManager.isAnyUserSignedIn || !FindObjectOfType<RoR2.UI.MainMenu.MainMenuController>()) return;
                joinSent = true;
                joinStarted = Time.unscaledTime;
                lobbies.onLobbyJoined += ok =>
                {
                    if (joinDone) return;
                    joinDone = true;
                    Write(ok ? "Melty joined: " + join : "Melty join failed: the lobby could not be joined (full, closed or private)");
                };
                if (lobbies is SteamworksLobbyManager steam) steam.pendingSteamworksLobbyId = new PlatformID(joinId);
                else lobbies.JoinLobby(new PlatformID(joinId));
                Log.Info("Melty: join sent for lobby " + joinId);
                return;
            }
            if (lobbies.isInLobby && !lobbies.ownsLobby && lobbies.GetLobbyID() == joinId.ToString())
            {
                joinDone = true;
                Write("Melty joined: " + join);
            }
            else if (Time.unscaledTime - joinStarted > 60f)
            {
                joinDone = true;
                Write("Melty join failed: no answer from the lobby within a minute");
            }
        }
    }
}
