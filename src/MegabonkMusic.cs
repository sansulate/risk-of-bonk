using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using RoR2;
using UnityEngine;

namespace RiskOfBonk
{
    /// <summary>
    /// Megabonk's soundtrack, from the player's install: its Menu track on the main menu, and on Bonk style stages the map's
    /// tracks (Megabonk's MusicTrack pools: Forest 1-4 on the forest stages, Desert 1-4 on the deserts, its other tracks on the
    /// Graveyard) with the map's boss track (ForestBoss / DesertBoss / GraveyardBoss) while the Teleporter boss is up. Risk of Rain 2's own music is muted
    /// meanwhile and comes back elsewhere (Rain style). Played like the Megabonk sounds (Ogg Vorbis decoded with NVorbis,
    /// out through Windows' waveOut), streamed and looped on a thread of its own, at the game's master x music volume.
    /// </summary>
    public sealed class MegabonkMusic : MonoBehaviour
    {
        static readonly string[] Forest = { "Forest1_Loop", "Forest2_Loop", "Forest3_Loop", "Forest4_Loop" };
        static readonly string[] Desert = { "Desert1_Loop", "Desert2_Loop", "Desert3_Loop", "Desert4_Loop" };
        static readonly string[] Graveyard = { "Other1_Loop", "Other2_Loop", "Other4_Loop", "Other5_Loop", "Other6_Loop", "Other7_Loop", "Other8Loop", "Other9_Loop" };
        const string Menu = "Menu", ForestBoss = "ForestBoss_Loop", DesertBoss = "DesertBoss_Loop", GraveyardBoss = "GraveyardBoss_Loop";

        public static bool Enabled = true;

        static volatile string wanted;      // track the main thread wants (null: none)
        static volatile float volume;       // settings volume, set by the main thread
        static volatile bool quitting;
        static Thread player;
        static bool muted;
        string stageTrack;
        int stageFor = -1;
        float nextCheck;

        void Start()
        {
            player = new Thread(PlayLoop) { IsBackground = true, Name = "RiskOfBonk music" };
            player.Start();
            // Every track is converted from Megabonk's sound bank the first time it is needed, which can take a while; do
            // them all once in the background (after the game has loaded), so a new stage's music starts at once.
            new Thread(() =>
            {
                Thread.Sleep(20000);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int n = 0;
                foreach (var t in new[] { Menu }.Concat(Forest).Concat(Desert).Concat(Graveyard).Concat(new[] { ForestBoss, DesertBoss, GraveyardBoss }))
                {
                    if (quitting) return;
                    try { if (MegabonkSounds.OggFile(t) != null) n++; } catch (Exception e) { Log.Warn($"music {t}: {e.Message}"); }
                }
                Log.Info($"music: {n} Megabonk tracks ready in {sw.ElapsedMilliseconds} ms");
            }) { IsBackground = true, Name = "RiskOfBonk music prepare", Priority = System.Threading.ThreadPriority.BelowNormal }.Start();
        }

        void Update()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.5f;
            volume = Settings();
            string track = Enabled ? Pick() : null;
            wanted = track;
            // Risk of Rain 2's music off while Megabonk's plays.
            bool mute = track != null;
            if (mute != muted)
            {
                muted = mute;
                try { AkSoundEngine.SetRTPCValue("Volume_MSX", mute ? 0f : ConVar("volume_msx")); } catch (Exception e) { Log.Warn("music: " + e.Message); }
            }
        }

        string Pick()
        {
            if (RoR2.UI.MainMenu.MainMenuController.instance) return Menu;
            if (!Run.instance || !BonkTerrain.Active) return null;
            string map = BonkTerrain.MapName ?? "";
            bool graveyard = map == "Graveyard", desert = map.StartsWith("Desert");
            var tp = TeleporterInteraction.instance;
            if (tp && tp.isCharging && tp.bossGroup && tp.bossGroup.combatSquad && tp.bossGroup.combatSquad.memberCount > 0)
                return graveyard ? GraveyardBoss : desert ? DesertBoss : ForestBoss;
            int stage = Run.instance.stageClearCount;
            if (stageFor != stage || stageTrack == null)
            {
                stageFor = stage;
                var pool = graveyard ? Graveyard : desert ? Desert : Forest;
                // Forest 1-3 and Desert 1-3 play their own track, like Megabonk's stages; the Graveyard picks from its pool.
                int tier = map.Length > 0 && char.IsDigit(map[map.Length - 1]) ? map[map.Length - 1] - '1' : -1;
                stageTrack = tier >= 0 && tier < pool.Length ? pool[tier] : pool[new System.Random(stage * 7919 + (int)(Run.instance.seed & 0xffff)).Next(pool.Length)];
            }
            return stageTrack;
        }

        static float ConVar(string name)
        {
            var c = RoR2.Console.instance ? RoR2.Console.instance.FindConVar(name) : null;
            return c != null && float.TryParse(c.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : 100f;
        }

        static float Settings() => ConVar("volume_master") / 100f * ConVar("volume_msx") / 100f * Generated.Tune.MusicVolume;

        void OnApplicationQuit() => Stop();
        void OnDestroy() => Stop();

        static void Stop()
        {
            quitting = true;
            try { player?.Join(1500); } catch { }
        }

        // ---------------- player thread ----------------

        [StructLayout(LayoutKind.Sequential)]
        struct WaveFormat { public ushort tag, channels; public uint rate, bytesPerSec; public ushort align, bits, extra; }

        [StructLayout(LayoutKind.Sequential)]
        struct WaveHdr { public IntPtr data; public uint length, recorded; public IntPtr user; public uint flags, loops; public IntPtr next, reserved; }

        [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr h, uint device, ref WaveFormat fmt, IntPtr cb, IntPtr inst, uint flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr h);

        const int Buffers = 4, BufferMs = 200;
        const uint Done = 1, Prepared = 2;

        sealed class Stream
        {
            public NVorbis.VorbisReader reader;
            public IntPtr handle;
            public IntPtr[] hdrs = new IntPtr[Buffers], data = new IntPtr[Buffers];
            public int samplesPerBuffer;
            public string name;
            public float fade;     // 0..1
            public bool stopping;
        }

        static void PlayLoop()
        {
            Stream cur = null;
            var failed = new HashSet<string>();
            while (!quitting)
            {
                try
                {
                    string want = wanted;
                    if (cur != null && cur.name != want) cur.stopping = true;
                    if (cur != null && cur.stopping && cur.fade <= 0f) { Close(cur); cur = null; }
                    if (cur == null && want != null && !failed.Contains(want))
                    {
                        cur = Open(want);
                        if (cur == null) failed.Add(want);
                        else Log.Info($"music: {want}");
                    }
                    if (cur != null) Feed(cur);
                }
                catch (Exception e) { Log.Warn("music: " + e.Message); if (cur != null) { Close(cur); cur = null; } Thread.Sleep(1000); }
                Thread.Sleep(30);
            }
            if (cur != null) Close(cur);
        }

        static Stream Open(string name)
        {
            string file = MegabonkSounds.OggFile(name);
            if (file == null) return null;
            var s = new Stream { name = name, reader = new NVorbis.VorbisReader(file) };
            int ch = s.reader.Channels, rate = s.reader.SampleRate;
            var fmt = new WaveFormat { tag = 1, channels = (ushort)ch, rate = (uint)rate, bits = 16, align = (ushort)(ch * 2), bytesPerSec = (uint)(rate * ch * 2) };
            if (waveOutOpen(out s.handle, 0xFFFFFFFF, ref fmt, IntPtr.Zero, IntPtr.Zero, 0) != 0) { s.reader.Dispose(); return null; }
            s.samplesPerBuffer = rate * ch * BufferMs / 1000;
            int hs = Marshal.SizeOf(typeof(WaveHdr));
            for (int i = 0; i < Buffers; i++)
            {
                s.hdrs[i] = Marshal.AllocHGlobal(hs);
                Marshal.StructureToPtr(new WaveHdr { flags = Done }, s.hdrs[i], false);
                s.data[i] = Marshal.AllocHGlobal(s.samplesPerBuffer * 2);
            }
            return s;
        }

        static readonly float[] floats = new float[48000 * 2];
        static readonly short[] shorts = new short[48000 * 2];

        static void Feed(Stream s)
        {
            int hs = Marshal.SizeOf(typeof(WaveHdr));
            for (int i = 0; i < Buffers; i++)
            {
                var h = (WaveHdr)Marshal.PtrToStructure(s.hdrs[i], typeof(WaveHdr));
                if ((h.flags & Done) == 0) continue; // still playing
                if ((h.flags & Prepared) != 0) waveOutUnprepareHeader(s.handle, s.hdrs[i], hs);
                int n = 0, rewinds = 0;
                while (n < s.samplesPerBuffer && rewinds < 2)
                {
                    int got = s.reader.ReadSamples(floats, n, s.samplesPerBuffer - n);
                    if (got <= 0) { s.reader.DecodedPosition = 0; rewinds++; continue; } // the end: loop
                    n += got;
                }
                if (n == 0) throw new Exception(s.name + " has no samples");
                // Fade in on start, out when switching tracks (about 0.8 s).
                float step = (float)BufferMs / 800f;
                s.fade = Mathf.Clamp01(s.fade + (s.stopping ? -step : step));
                float v = volume * s.fade;
                for (int k = 0; k < n; k++) shorts[k] = (short)Mathf.Clamp(floats[k] * v * 32767f, -32768f, 32767f);
                Marshal.Copy(shorts, 0, s.data[i], n);
                Marshal.StructureToPtr(new WaveHdr { data = s.data[i], length = (uint)(n * 2) }, s.hdrs[i], false);
                if (waveOutPrepareHeader(s.handle, s.hdrs[i], hs) != 0) throw new Exception("waveOutPrepareHeader failed");
                if (waveOutWrite(s.handle, s.hdrs[i], hs) != 0) throw new Exception("waveOutWrite failed");
            }
        }

        static void Close(Stream s)
        {
            int hs = Marshal.SizeOf(typeof(WaveHdr));
            try
            {
                waveOutReset(s.handle);
                for (int i = 0; i < Buffers; i++)
                {
                    var h = (WaveHdr)Marshal.PtrToStructure(s.hdrs[i], typeof(WaveHdr));
                    if ((h.flags & Prepared) != 0) waveOutUnprepareHeader(s.handle, s.hdrs[i], hs);
                }
                if (waveOutClose(s.handle) == 0)
                    for (int i = 0; i < Buffers; i++) { Marshal.FreeHGlobal(s.hdrs[i]); Marshal.FreeHGlobal(s.data[i]); }
            }
            catch { }
            try { s.reader.Dispose(); } catch { }
        }
    }
}
