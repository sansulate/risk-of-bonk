using System.Linq;
using RiskOfBonk.Generated;
using RoR2;
using RoR2.Networking;
using UnityEngine;
using UnityEngine.Networking;

namespace RiskOfBonk
{
    /// <summary>
    /// The "Bonk style / Rain style" choice from the character select screen (sheets/stage_modes.json).
    /// The host decides; every client in the lobby gets the host's choice over the game's own network connection.
    /// </summary>
    public static class StageMode
    {
        public const short MsgType = 0x5B0B;
        public static int Current; // index into Sheets.StageModes (0 = Rain style)
        public static event System.Action Changed;

        public static ModeRow Row => Sheets.StageModes[Mathf.Clamp(Current, 0, Sheets.StageModes.Length - 1)];
        public static bool IsBonk => Row.Id == "bonk";

        sealed class ModeMessage : MessageBase
        {
            public byte mode;
            public override void Serialize(NetworkWriter w) => w.Write(mode);
            public override void Deserialize(NetworkReader r) => mode = r.ReadByte();
        }

        public static void Init()
        {
            NetworkManagerSystem.onStartServerGlobal += () =>
                NetworkServer.RegisterHandler(MsgType, _ => { /* clients may not change the mode; host only */ });
            NetworkManagerSystem.onStartClientGlobal += client =>
            {
                MegabonkSurvivors.RegisterNetworkPrefabs();
                client.RegisterHandler(MsgType, msg =>
                {
                    var m = msg.ReadMessage<ModeMessage>();
                    if (NetworkServer.active) return; // host already knows
                    Set(m.mode, false);
                });
            };
        }

        public static bool CanChange => !NetworkClient.active || NetworkServer.active;

        public static void Toggle()
        {
            if (!CanChange) return;
            Set((Current + 1) % Sheets.StageModes.Length, true);
        }

        static void Set(int mode, bool broadcast)
        {
            bool changed = mode != Current;
            Current = mode;
            if (broadcast) Broadcast();
            if (changed) { Log.Info("Stage style: " + Row.Label); Changed?.Invoke(); }
        }

        public static void Broadcast()
        {
            if (NetworkServer.active) NetworkServer.SendToAll(MsgType, new ModeMessage { mode = (byte)Current });
        }

        /// <summary>Host re-sends the choice while in the lobby so players who join late get it too.</summary>
        public sealed class LobbyBeacon : MonoBehaviour
        {
            float next;
            void Update()
            {
                if (Time.unscaledTime < next) return;
                next = Time.unscaledTime + 2f;
                if (PreGameController.instance != null) Broadcast();
            }
        }
    }
}
