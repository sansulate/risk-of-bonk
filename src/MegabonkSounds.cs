using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using Path = System.IO.Path;

namespace RiskOfBonk
{
    public sealed partial class MegabonkLibrary
    {
        const int AudioClipId = 83;

        /// <summary>A Megabonk sound's FMOD sound bank (FSB5) bytes, by AudioClip name, straight from its .resS file.</summary>
        public byte[] ReadSound(string name)
        {
            foreach (var f in files)
                foreach (var info in f.file.GetAssetsOfType(AudioClipId))
                {
                    var bf = am.GetBaseField(f, info);
                    if (bf["m_Name"].AsString != name) continue;
                    var res = bf["m_Resource"];
                    string src = res["m_Source"].AsString;
                    long offset = (long)res["m_Offset"].AsULong, size = (long)res["m_Size"].AsULong;
                    if (string.IsNullOrEmpty(src) || size <= 0) return null;
                    string full = Path.Combine(Path.GetDirectoryName(f.path) ?? DataDir, Path.GetFileName(src));
                    if (!File.Exists(full)) full = Path.Combine(DataDir, Path.GetFileName(src));
                    var buf = new byte[size];
                    using (var fs = File.OpenRead(full))
                    {
                        fs.Position = offset;
                        int read = 0;
                        while (read < buf.Length) { int n = fs.Read(buf, read, buf.Length - read); if (n <= 0) break; read += n; }
                    }
                    return buf;
                }
            return null;
        }
    }

    /// <summary>
    /// Megabonk's own sounds (weapon attacks, jump, landing, slide), read from the player's Megabonk install. Risk of Rain 2
    /// switches Unity's audio off (its sounds go through Wwise), so they are decoded here (FSB5 to Ogg Vorbis to PCM,
    /// with NVorbis) and played through Windows' own audio output (waveOut, also there under Proton), at Risk of Rain 2's
    /// master and sound effect volume, quieter with distance. Names: sheets/weapons.json (mbSound), sheets/sounds.json.
    /// </summary>
    public sealed class MegabonkSounds : MonoBehaviour
    {
        sealed class Pcm { public short[] data; public int rate, channels; }

        static readonly Dictionary<string, Pcm> sounds = new Dictionary<string, Pcm>();
        static MegabonkSounds instance;

        void Awake() => instance = this;

        /// <summary>Loads every named sound once Megabonk's files are open (rebuilt as .ogg files under the data folder, once).</summary>
        public static void LoadAll(IEnumerable<string> names)
        {
            if (instance == null) return;
            instance.StartCoroutine(instance.Load(names.Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList()));
        }

        IEnumerator Load(List<string> names)
        {
            while (!MegabonkLoader.Done) yield return null;
            var lib = MegabonkLoader.Library;
            if (lib == null) yield break;
            string dir = Path.Combine(Plugin.DataDir, "sounds");
            Directory.CreateDirectory(dir);
            bool done = false;
            int ok = 0;
            new System.Threading.Thread(() =>
            {
                try
                {
                    Dictionary<uint, byte[]> headers = null;
                    foreach (var name in names)
                    {
                        string file = Path.Combine(dir, name + ".ogg");
                        if (!File.Exists(file))
                        {
                            headers ??= FsbVorbis.LoadHeaders(Path.Combine(MegabonkLoader.GameDir, "UnityPlayer.dll"));
                            byte[] fsb;
                            lock (lib) fsb = lib.ReadSound(name);
                            var ogg = fsb != null ? FsbVorbis.ToOgg(fsb, headers) : null;
                            if (ogg == null) { Log.Warn($"Megabonk sound {name}: {(fsb == null ? "not found" : "not a known Vorbis sound")}"); continue; }
                            File.WriteAllBytes(file, ogg);
                        }
                        var pcm = Decode(file);
                        if (pcm == null) { Log.Warn($"Megabonk sound {name}: could not decode"); continue; }
                        lock (sounds) sounds[name] = pcm;
                        ok++;
                    }
                }
                catch (Exception e) { Log.Warn("Megabonk sounds: " + e.Message); }
                finally { done = true; }
            }) { IsBackground = true, Name = "RiskOfBonk sounds" }.Start();
            while (!done) yield return null;
            Log.Info($"Megabonk sounds ready: {ok}/{names.Count}");
        }

        static Dictionary<uint, byte[]> vorbisHeaders;
        static readonly object convertLock = new object();

        /// <summary>A Megabonk sound as an .ogg file under the data folder (converted from Megabonk's FSB5 once), or null.</summary>
        public static string OggFile(string name)
        {
            var lib = MegabonkLoader.Library;
            string dir = Path.Combine(Plugin.DataDir, "sounds");
            string file = Path.Combine(dir, name + ".ogg");
            if (File.Exists(file)) return file;
            if (lib == null) return null;
            lock (convertLock)
            {
                if (File.Exists(file)) return file;
                Directory.CreateDirectory(dir);
                vorbisHeaders ??= FsbVorbis.LoadHeaders(Path.Combine(MegabonkLoader.GameDir, "UnityPlayer.dll"));
                byte[] fsb;
                lock (lib) fsb = lib.ReadSound(name);
                var ogg = fsb != null ? FsbVorbis.ToOgg(fsb, vorbisHeaders) : null;
                if (ogg == null) { Log.Warn($"Megabonk sound {name}: {(fsb == null ? "not found" : "not a known Vorbis sound")}"); return null; }
                File.WriteAllBytes(file, ogg);
                return file;
            }
        }

        static Pcm Decode(string file)
        {
            using (var vr = new NVorbis.VorbisReader(file))
            {
                var all = new List<short>();
                var buf = new float[4096 * vr.Channels];
                int n;
                while ((n = vr.ReadSamples(buf, 0, buf.Length)) > 0)
                    for (int i = 0; i < n; i++) all.Add((short)Mathf.Clamp(buf[i] * 32767f, -32768f, 32767f));
                return all.Count > 0 ? new Pcm { data = all.ToArray(), rate = vr.SampleRate, channels = vr.Channels } : null;
            }
        }

        static float SettingsVolume()
        {
            float v = 1f;
            foreach (var cv in new[] { "volume_master", "volume_sfx" })
            {
                var c = RoR2.Console.instance ? RoR2.Console.instance.FindConVar(cv) : null;
                if (c != null && float.TryParse(c.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f)) v *= f / 100f;
            }
            return v;
        }

        /// <summary>Plays a Megabonk sound heard from a point. False when it is not loaded (the caller can fall back).</summary>
        public static bool Play(string name, Vector3 at, float volume = 1f)
        {
            if (string.IsNullOrEmpty(name)) return false;
            Pcm pcm;
            lock (sounds) if (!sounds.TryGetValue(name, out pcm)) return false;
            var cam = Camera.main;
            float dist = cam ? Vector3.Distance(cam.transform.position, at) : 0f;
            float v = volume * SettingsVolume() * Generated.Tune.SoundVolume * Mathf.Clamp01(1f - Mathf.Max(0f, dist - 12f) / 70f);
            if (v < 0.01f) return true;
            Voice.Start(pcm, v);
            if (BonkMovement.LogSpeed) Log.Info($"sound: {name} at volume {v:F2}");
            return true;
        }

        void Update() => Voice.Tick();

        // Every audio device is closed while the game is still whole (OnApplicationQuit comes before teardown): a
        // waveOut device left open into the game's exit crashed it in ntdll (c0000005) or left its process hanging.
        void OnApplicationQuit() => Voice.CloseAll();

        void OnDestroy() => Voice.CloseAll();

        /// <summary>A waveOut device playing one buffer; a few stay open per format and are reused, and each is closed
        /// again after a few silent seconds, so no device stays open while nothing plays.</summary>
        sealed class Voice
        {
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

            const uint Done = 1, Prepared = 2;
            const int MaxVoices = 12;
            const float IdleClose = 3f;
            static readonly List<Voice> voices = new List<Voice>();
            static bool broken, closed;

            IntPtr handle, hdr, data;
            int rate, channels, capacity;
            bool busy;
            float idleSince;

            static int HdrSize => Marshal.SizeOf(typeof(WaveHdr));

            public static void Start(Pcm pcm, float volume)
            {
                if (broken || closed) return;
                try
                {
                    var v = voices.FirstOrDefault(x => !x.busy && x.rate == pcm.rate && x.channels == pcm.channels);
                    if (v == null)
                    {
                        if (voices.Count >= MaxVoices) return; // too many at once: skip this one
                        v = new Voice { rate = pcm.rate, channels = pcm.channels };
                        var fmt = new WaveFormat { tag = 1, channels = (ushort)pcm.channels, rate = (uint)pcm.rate, bits = 16, align = (ushort)(pcm.channels * 2), bytesPerSec = (uint)(pcm.rate * pcm.channels * 2) };
                        if (waveOutOpen(out v.handle, 0xFFFFFFFF, ref fmt, IntPtr.Zero, IntPtr.Zero, 0) != 0) { broken = true; Log.Warn("Megabonk sounds: no audio output (waveOutOpen failed)"); return; }
                        v.hdr = Marshal.AllocHGlobal(HdrSize);
                        voices.Add(v);
                    }
                    int bytes = pcm.data.Length * 2;
                    if (v.capacity < bytes)
                    {
                        if (v.data != IntPtr.Zero) Marshal.FreeHGlobal(v.data);
                        v.data = Marshal.AllocHGlobal(bytes);
                        v.capacity = bytes;
                    }
                    var scaled = new short[pcm.data.Length];
                    for (int i = 0; i < scaled.Length; i++) scaled[i] = (short)(pcm.data[i] * volume);
                    Marshal.Copy(scaled, 0, v.data, scaled.Length);
                    Marshal.StructureToPtr(new WaveHdr { data = v.data, length = (uint)bytes }, v.hdr, false);
                    if (waveOutPrepareHeader(v.handle, v.hdr, HdrSize) != 0) { v.Close(); return; }
                    v.busy = true;
                    if (waveOutWrite(v.handle, v.hdr, HdrSize) != 0) v.Close();
                }
                catch (Exception e) { broken = true; Log.Warn("Megabonk sounds: " + e.Message); }
            }

            public static void Tick()
            {
                for (int i = voices.Count - 1; i >= 0; i--)
                {
                    var v = voices[i];
                    if (v.busy)
                    {
                        var h = (WaveHdr)Marshal.PtrToStructure(v.hdr, typeof(WaveHdr));
                        if ((h.flags & Done) == 0) continue;
                        waveOutUnprepareHeader(v.handle, v.hdr, HdrSize);
                        v.busy = false;
                        v.idleSince = Time.unscaledTime;
                    }
                    else if (Time.unscaledTime - v.idleSince > IdleClose) v.Close();
                }
            }

            /// <summary>Stops and closes this device, then frees its buffers (only once the device has let go of them).</summary>
            void Close()
            {
                voices.Remove(this);
                busy = false;
                try
                {
                    if (handle != IntPtr.Zero)
                    {
                        waveOutReset(handle);
                        var h = (WaveHdr)Marshal.PtrToStructure(hdr, typeof(WaveHdr));
                        if ((h.flags & Prepared) != 0) waveOutUnprepareHeader(handle, hdr, HdrSize);
                        if (waveOutClose(handle) != 0) return; // the device still holds the buffers: leave them allocated
                        handle = IntPtr.Zero;
                    }
                    if (hdr != IntPtr.Zero) { Marshal.FreeHGlobal(hdr); hdr = IntPtr.Zero; }
                    if (data != IntPtr.Zero) { Marshal.FreeHGlobal(data); data = IntPtr.Zero; capacity = 0; }
                }
                catch { }
            }

            public static void CloseAll()
            {
                closed = true;
                foreach (var v in voices.ToArray()) v.Close();
                voices.Clear();
            }
        }
    }
}
