using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RiskOfBonk
{
    /// <summary>
    /// Turns a Megabonk sound (an FMOD FSB5 bank with one Vorbis sample) into a plain Ogg Vorbis file Unity can play.
    /// FSB5 leaves out Vorbis's setup header (FMOD keeps its own copies, chosen by CRC); those come from the player's own
    /// Megabonk install, out of the FMOD inside its UnityPlayer.dll. Same method as python-fsb5 (MIT, Simon Pinfold).
    /// </summary>
    public static class FsbVorbis
    {
        static Dictionary<uint, byte[]> table;

        /// <summary>FMOD's Vorbis setup headers by CRC-32, from a 64-bit DLL that contains FMOD.</summary>
        public static Dictionary<uint, byte[]> LoadHeaders(string dllPath)
        {
            if (table != null) return table;
            var file = File.ReadAllBytes(dllPath);
            // PE sections, to read the DLL as it sits in memory (virtual addresses).
            int pe = BitConverter.ToInt32(file, 0x3C);
            int nSections = BitConverter.ToUInt16(file, pe + 6);
            int optSize = BitConverter.ToUInt16(file, pe + 20);
            int opt = pe + 24;
            ulong imageBase = BitConverter.ToUInt64(file, opt + 24);
            uint imageSize = BitConverter.ToUInt32(file, opt + 56);
            var img = new byte[imageSize];
            for (int s = 0; s < nSections; s++)
            {
                int h = opt + optSize + s * 40;
                uint va = BitConverter.ToUInt32(file, h + 12), rawSize = BitConverter.ToUInt32(file, h + 16), raw = BitConverter.ToUInt32(file, h + 20);
                if (raw == 0 || rawSize == 0) continue;
                Buffer.BlockCopy(file, (int)raw, img, (int)va, (int)Math.Min(rawSize, Math.Min((uint)file.Length - raw, imageSize - va)));
            }
            // Every "\x05vorbis" header, then the 8-byte pointers to them: FMOD's table of {ptr, len, crc, basePtr, ofs, len2}.
            var heads = new HashSet<ulong>();
            var magic = new byte[] { 5, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' };
            for (int i = IndexOf(img, magic, 0); i >= 0; i = IndexOf(img, magic, i + 7)) heads.Add(imageBase + (ulong)i);
            var refs = new SortedSet<int>();
            for (int i = 0; i + 8 <= img.Length; i += 8)
                if (heads.Contains(BitConverter.ToUInt64(img, i))) refs.Add(i);
            int start = -1;
            foreach (int r in refs)
                if (!refs.Contains(r - 32) && (refs.Contains(r + 32) || refs.Contains(r + 64))) { start = r; break; }
            var result = new Dictionary<uint, byte[]>();
            for (int e = start; e >= 0 && e + 32 <= img.Length; e += 32)
            {
                ulong buf1 = BitConverter.ToUInt64(img, e), buf2 = BitConverter.ToUInt64(img, e + 16);
                uint len1 = BitConverter.ToUInt32(img, e + 8), crc = BitConverter.ToUInt32(img, e + 12);
                uint ofs = BitConverter.ToUInt32(img, e + 24), len2 = BitConverter.ToUInt32(img, e + 28);
                if (crc == 0 || len1 == 0 || buf1 == 0 || len1 > 8192 || len2 > 8192) break;
                var hdr = new byte[len1];
                Buffer.BlockCopy(img, (int)((buf2 != 0 ? buf2 : buf1) - imageBase), hdr, 0, (int)len1);
                if (buf2 != 0) Buffer.BlockCopy(img, (int)(buf1 - imageBase), hdr, (int)ofs, (int)len2);
                result[crc] = hdr;
            }
            return table = result;
        }

        static int IndexOf(byte[] hay, byte[] needle, int from)
        {
            for (int i = from; i <= hay.Length - needle.Length; i++)
            {
                if (hay[i] != needle[0]) continue;
                int k = 1;
                while (k < needle.Length && hay[i + k] == needle[k]) k++;
                if (k == needle.Length) return i;
            }
            return -1;
        }

        static readonly int[] Rates = { 0, 8000, 11000, 11025, 16000, 22050, 24000, 32000, 44100, 48000 };

        /// <summary>The Ogg Vorbis file for an FSB5 bank's first sample, or null when it is not Vorbis or its header is unknown.</summary>
        public static byte[] ToOgg(byte[] fsb, Dictionary<uint, byte[]> headers)
        {
            if (fsb.Length < 0x40 || fsb[0] != 'F' || fsb[1] != 'S' || fsb[2] != 'B' || fsb[3] != '5') return null;
            uint ver = BitConverter.ToUInt32(fsb, 4), shdr = BitConverter.ToUInt32(fsb, 12), names = BitConverter.ToUInt32(fsb, 16);
            uint dataSize = BitConverter.ToUInt32(fsb, 20), mode = BitConverter.ToUInt32(fsb, 24);
            if (mode != 15) return null; // Vorbis
            int hdrLen = ver == 1 ? 0x3C : 0x40, off = hdrLen;
            ulong raw = BitConverter.ToUInt64(fsb, off); off += 8;
            bool next = (raw & 1) != 0;
            int rate = Rates[Math.Min(9, (int)((raw >> 1) & 0xF))], channels = (int)((raw >> 5) & 1) + 1;
            long dataOff = (long)((raw >> 6) & 0x0FFFFFFF) * 16;
            long samples = (long)(raw >> 34);
            uint crc = 0;
            while (next)
            {
                uint c = BitConverter.ToUInt32(fsb, off); off += 4;
                next = (c & 1) != 0;
                int size = (int)((c >> 1) & 0xFFFFFF), type = (int)((c >> 25) & 0x7F);
                if (type == 11) crc = BitConverter.ToUInt32(fsb, off);
                else if (type == 2) channels = fsb[off];
                else if (type == 4) rate = (int)BitConverter.ToUInt32(fsb, off);
                off += size;
            }
            if (!headers.TryGetValue(crc, out var setup)) return null;
            long dataStart = hdrLen + shdr + names + dataOff, dataEnd = hdrLen + shdr + names + dataSize;

            var ms = new MemoryStream();
            var id = new MemoryStream();
            id.Write(new byte[] { 1, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' }, 0, 7);
            id.Write(BitConverter.GetBytes(0), 0, 4);
            id.WriteByte((byte)channels);
            id.Write(BitConverter.GetBytes(rate), 0, 4);
            id.Write(new byte[12], 0, 12);
            id.WriteByte((11 << 4) | 8); // FMOD's block sizes: 256 and 2048
            id.WriteByte(1);
            var vendor = Encoding.ASCII.GetBytes("RiskOfBonk");
            var com = new MemoryStream();
            com.Write(new byte[] { 3, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' }, 0, 7);
            com.Write(BitConverter.GetBytes(vendor.Length), 0, 4);
            com.Write(vendor, 0, vendor.Length);
            com.Write(BitConverter.GetBytes(0), 0, 4);
            com.WriteByte(1);
            Page(ms, new[] { id.ToArray() }, 0, 0, 2);
            Page(ms, new[] { com.ToArray(), setup }, 0, 1, 0);

            var longBlock = ModeBlockFlags(setup);
            int modeBits = ILog(longBlock.Count - 1);
            var packets = new List<(byte[] p, long g)>();
            long granule = 0; int prev = 0;
            for (long p = dataStart; p + 2 <= dataEnd;)
            {
                int n = BitConverter.ToUInt16(fsb, (int)p); p += 2;
                if (n == 0 || p + n > dataEnd) break;
                var pk = new byte[n];
                Buffer.BlockCopy(fsb, (int)p, pk, 0, n); p += n;
                // Audio packet: bit 0 is 0, then the mode number (LSB first).
                int m = 0;
                for (int b = 0; b < modeBits; b++) m |= ((pk[(1 + b) >> 3] >> ((1 + b) & 7)) & 1) << b;
                int bs = m < longBlock.Count && longBlock[m] ? 2048 : 256;
                if (prev != 0) granule += (bs + prev) / 4;
                prev = bs;
                packets.Add((pk, granule));
            }
            for (int i = 0; i < packets.Count; i++)
            {
                bool last = i == packets.Count - 1;
                Page(ms, new[] { packets[i].p }, last ? Math.Min(packets[i].g, samples) : packets[i].g, 2 + i, (byte)(last ? 4 : 0));
            }
            return ms.ToArray();
        }

        static int ILog(int v) { int r = 0; while (v > 0) { r++; v >>= 1; } return r; }

        /// <summary>Each Vorbis mode's block flag, read backwards from the end of the setup header.</summary>
        static List<bool> ModeBlockFlags(byte[] setup)
        {
            int total = setup.Length * 8;
            bool Bit(int i) => ((setup[i >> 3] >> (i & 7)) & 1) != 0;
            int end = total - 1;
            while (end > 0 && !Bit(end)) end--; // the framing flag
            var flags = new List<bool>();
            List<bool> found = null;
            for (int pos = end; pos - 41 >= 6;)
            {
                int s = pos - 41;
                bool zero = true;
                for (int k = 1; k <= 32 && zero; k++) if (Bit(s + k)) zero = false;
                if (!zero) break;
                flags.Insert(0, Bit(s));
                pos = s;
                int count = 0;
                for (int k = 0; k < 6; k++) if (Bit(pos - 6 + k)) count |= 1 << k;
                if (count + 1 == flags.Count) found = new List<bool>(flags);
            }
            return found ?? new List<bool> { false, true };
        }

        static uint[] crcTable;

        static void Page(Stream o, byte[][] packets, long granule, int seq, byte flags)
        {
            var lacing = new List<byte>();
            int bodyLen = 0;
            foreach (var p in packets)
            {
                int n = p.Length; bodyLen += n;
                while (n >= 255) { lacing.Add(255); n -= 255; }
                lacing.Add((byte)n);
            }
            var page = new byte[27 + lacing.Count + bodyLen];
            Encoding.ASCII.GetBytes("OggS").CopyTo(page, 0);
            page[4] = 0; page[5] = flags;
            BitConverter.GetBytes(granule).CopyTo(page, 6);
            BitConverter.GetBytes(1).CopyTo(page, 14);
            BitConverter.GetBytes(seq).CopyTo(page, 18);
            page[26] = (byte)lacing.Count;
            lacing.CopyTo(page, 27);
            int at = 27 + lacing.Count;
            foreach (var p in packets) { p.CopyTo(page, at); at += p.Length; }
            if (crcTable == null)
            {
                crcTable = new uint[256];
                for (uint i = 0; i < 256; i++)
                {
                    uint r = i << 24;
                    for (int k = 0; k < 8; k++) r = (r & 0x80000000) != 0 ? (r << 1) ^ 0x04C11DB7 : r << 1;
                    crcTable[i] = r;
                }
            }
            uint crc = 0;
            foreach (byte b in page) crc = (crc << 8) ^ crcTable[((crc >> 24) ^ b) & 0xFF];
            BitConverter.GetBytes(crc).CopyTo(page, 22);
            o.Write(page, 0, page.Length);
        }
    }
}
