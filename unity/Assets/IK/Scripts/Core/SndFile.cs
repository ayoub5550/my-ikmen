using System;
using System.Collections.Generic;
using System.IO;

namespace IK.Core {
    /// <summary>A single wave of a .snd file: the raw RIFF bytes plus the decoded PCM.</summary>
    public class SndEntry {
        public int Group, Number;
        public byte[] Wav;              // the embedded RIFF/WAVE file
        public int Channels, SampleRate, Bits;
        public float[] Samples;         // interleaved, -1..1
        public int SampleCount => Channels > 0 && Samples != null ? Samples.Length / Channels : 0;
        public float Duration => SampleRate > 0 ? (float)SampleCount / SampleRate : 0f;
    }

    /// <summary>
    /// Elecbyte .snd reader (Ikemen GO `engine/ikemen-go/src/sound.go: LoadSndFiltered`):
    /// a 16-byte header followed by a linked list of subheaders, each holding a complete
    /// RIFF/WAVE file. The PCM is decoded here (8/16/24/32-bit and 32-bit float) so the
    /// Unity side only has to hand the floats to an AudioClip.
    /// </summary>
    public class SndFile {
        public int Version, Version2;
        public readonly List<SndEntry> Entries = new List<SndEntry>();
        readonly Dictionary<long, SndEntry> byKey = new Dictionary<long, SndEntry>();

        public SndEntry Get(int group, int number) =>
            byKey.TryGetValue(((long)group << 32) | (uint)number, out var e) ? e : null;

        public static SndFile Load(byte[] data, bool decodePcm = true) {
            if (data == null || data.Length < 16) throw new InvalidDataException("SND file too short");
            for (int i = 0; i < 11; i++)
                if (data[i] != "ElecbyteSnd"[i]) throw new InvalidDataException("Unrecognized SND file, invalid header");

            var snd = new SndFile { Version = data[12] | (data[13] << 8), Version2 = data[14] | (data[15] << 8) };
            int numberOfSounds = (int)U32(data, 16);
            int offset = (int)U32(data, 20);

            for (int i = 0; i < numberOfSounds; i++) {
                if (offset <= 0 || offset + 16 > data.Length) break;
                int next = (int)U32(data, offset);
                int length = (int)U32(data, offset + 4);
                int group = (int)U32(data, offset + 8);
                int number = (int)U32(data, offset + 12);
                if (length > 0 && offset + 16 + length <= data.Length) {
                    var wav = new byte[length];
                    Array.Copy(data, offset + 16, wav, 0, length);
                    var entry = new SndEntry { Group = group, Number = number, Wav = wav };
                    if (decodePcm) {
                        try { DecodeWav(entry); } catch (Exception) { entry.Samples = null; }
                    }
                    long key = ((long)group << 32) | (uint)number;
                    snd.Entries.Add(entry);
                    if (!snd.byKey.ContainsKey(key)) snd.byKey[key] = entry;
                }
                offset = next;
            }
            return snd;
        }

        /// <summary>Decodes a RIFF/WAVE (PCM or IEEE float) into interleaved floats.</summary>
        public static void DecodeWav(SndEntry e) {
            var d = e.Wav;
            if (d == null || d.Length < 44) throw new InvalidDataException("WAV too short");
            if (d[0] != 'R' || d[1] != 'I' || d[2] != 'F' || d[3] != 'F' ||
                d[8] != 'W' || d[9] != 'A' || d[10] != 'V' || d[11] != 'E')
                throw new InvalidDataException("not a RIFF/WAVE");

            int p = 12;
            int format = 1, dataOfs = -1, dataLen = 0;
            while (p + 8 <= d.Length) {
                string id = "" + (char)d[p] + (char)d[p + 1] + (char)d[p + 2] + (char)d[p + 3];
                int size = (int)U32(d, p + 4);
                int body = p + 8;
                if (size < 0 || body > d.Length) break;
                if (id == "fmt ") {
                    format = d[body] | (d[body + 1] << 8);
                    e.Channels = d[body + 2] | (d[body + 3] << 8);
                    e.SampleRate = (int)U32(d, body + 4);
                    e.Bits = d[body + 14] | (d[body + 15] << 8);
                    if (format == 0xFFFE && size >= 26) format = d[body + 24] | (d[body + 25] << 8);
                } else if (id == "data") {
                    dataOfs = body;
                    dataLen = Math.Min(size, d.Length - body);
                }
                p = body + size + (size & 1);
            }
            if (dataOfs < 0 || e.Channels <= 0 || e.Bits <= 0) throw new InvalidDataException("WAV without fmt/data");

            int bytesPerSample = e.Bits / 8;
            int count = dataLen / bytesPerSample;
            var samples = new float[count];
            for (int i = 0; i < count; i++) {
                int o = dataOfs + i * bytesPerSample;
                switch (e.Bits) {
                    case 8: samples[i] = (d[o] - 128) / 128f; break;
                    case 16: samples[i] = (short)(d[o] | (d[o + 1] << 8)) / 32768f; break;
                    case 24: {
                        int v = d[o] | (d[o + 1] << 8) | (d[o + 2] << 16);
                        if ((v & 0x800000) != 0) v |= unchecked((int)0xff000000);
                        samples[i] = v / 8388608f;
                        break;
                    }
                    case 32:
                        if (format == 3) samples[i] = BitConverter.ToSingle(d, o);
                        else samples[i] = (int)U32(d, o) / 2147483648f;
                        break;
                    default: throw new NotSupportedException("WAV bit depth " + e.Bits);
                }
            }
            e.Samples = samples;
        }

        static uint U32(byte[] d, int o) =>
            o + 3 < d.Length ? (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24)) : 0u;
    }
}
