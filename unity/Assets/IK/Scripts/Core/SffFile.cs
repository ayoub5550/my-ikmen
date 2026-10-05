using System;
using System.Collections.Generic;
using System.IO;

namespace IK.Core {
    /// <summary>One sprite of an SFF file, decoded to 8-bit indices or to raw RGBA.</summary>
    public class SffSprite {
        public int Index;
        public ushort Group, Number;
        public int Width, Height;
        public short X, Y;              // MUGEN axis offset
        public int Format = -1;         // SFF v2 format byte; -1 for a v1 PCX sprite
        public int PaletteIndex = -1;   // index into SffFile.Palettes
        public int Link;

        // dev.8: SFF v2 sprites can be decoded lazily (SffFile.Load(lazy: true)): the 9 MB
        // motif SFF only decodes the sprites a screen draws, so the title background shows at
        // once instead of after decoding every sprite (≈6 s on the emulator).
        int colorDepth = 8;
        byte[] pixels;
        bool raw;
        uint[] ownPalette;
        internal Action Decode;
        void Ensure() {
            if (Decode == null) return;
            lock (this) {
                var d = Decode;
                if (d == null) return;
                Decode = null;
                try { d(); } catch (Exception e) { pixels = null; SffFile.LastDecodeError = ToString() + ": " + e.Message; }
            }
        }
        public int ColorDepth { get { Ensure(); return colorDepth; } set { colorDepth = value; } }
        public byte[] Pixels { get { Ensure(); return pixels; } set { pixels = value; } }      // palette indices, or RGBA/RGB bytes when Raw
        public bool Raw { get { Ensure(); return raw; } set { raw = value; } }
        public uint[] OwnPalette { get { Ensure(); return ownPalette; } set { ownPalette = value; } }   // v1 sprites and paletted PNGs carry their own palette
        /// <summary>True until a lazily loaded sprite has been decoded.</summary>
        public bool Pending => Decode != null;

        public bool IsBlank => Width <= 0 || Height <= 0 || Pixels == null || Pixels.Length == 0;
        public override string ToString() => $"{Group},{Number} {Width}x{Height} fmt={Format}";
    }

    /// <summary>
    /// SFF v1 and v2 reader (Elecbyte sprite archive), ported from Ikemen GO
    /// `engine/ikemen-go/src/image.go` (SffHeader.Read, Sprite.read, Sprite.readV2,
    /// Sff.loadPalettes, Sff.ReadPalette).
    ///
    /// Everything is decoded in memory from a byte[]: on Android the files come out of
    /// Resources as TextAssets, in the editor and in tests straight from disk, so there is
    /// no file handle and no platform-specific IO anywhere in the loader.
    ///
    /// Ground truth for the decoders is `tools/sff_dump.py`, an independent Python
    /// implementation; `SffReaderTests` compares every sprite of KFM against its output.
    /// </summary>
    public class SffFile {
        public int VersionHigh, VersionLo1, VersionLo2, VersionLo3;
        public readonly List<SffSprite> Sprites = new List<SffSprite>();
        public readonly List<uint[]> Palettes = new List<uint[]>();
        /// <summary>(group,number) of a palette -> index into <see cref="Palettes"/>.</summary>
        public readonly Dictionary<int, int> PaletteTable = new Dictionary<int, int>();
        readonly Dictionary<int, SffSprite> byKey = new Dictionary<int, SffSprite>();

        /// <summary>Last error of a lazy sprite decode (shown by the device probe).</summary>
        public static string LastDecodeError;

        public static int Key(int group, int number) => (group << 16) | (number & 0xffff);

        public SffSprite Get(int group, int number) =>
            byKey.TryGetValue(Key(group, number), out var s) ? s : null;

        public uint[] PaletteFor(SffSprite s) {
            if (s == null) return null;
            if (s.OwnPalette != null) return s.OwnPalette;
            if (s.PaletteIndex >= 0 && s.PaletteIndex < Palettes.Count) return Palettes[s.PaletteIndex];
            return Palettes.Count > 0 ? Palettes[0] : null;
        }

        /// <summary>
        /// Reads a whole SFF. <paramref name="isCharacter"/> enables the legacy handling of a
        /// character's 0,0 sprite in SFF v1 (its palette sits at the end of the block).
        /// </summary>
        public static SffFile Load(byte[] data, bool isCharacter = true, bool lazy = false) {
            if (data == null || data.Length < 36) throw new InvalidDataException("SFF file too short");
            for (int i = 0; i < 11; i++)
                if (data[i] != "ElecbyteSpr"[i]) throw new InvalidDataException("Unrecognized SFF file, invalid header");

            var sff = new SffFile {
                VersionLo3 = data[12], VersionLo2 = data[13], VersionLo1 = data[14], VersionHigh = data[15]
            };
            var r = new ByteReader(data);
            if (sff.VersionHigh == 1) {
                int numberOfSprites = (int)r.U32(20);
                int first = (int)r.U32(24);
                sff.ReadV1(r, numberOfSprites, first, isCharacter);
            } else if (sff.VersionHigh == 2) {
                int first = (int)r.U32(36);
                int numberOfSprites = (int)r.U32(40);
                int firstPalette = (int)r.U32(44);
                int numberOfPalettes = (int)r.U32(48);
                uint lofs = r.U32(52);
                uint tofs = r.U32(60);
                sff.ReadPalettesV2(r, numberOfPalettes, firstPalette, lofs);
                sff.ReadV2(r, numberOfSprites, first, lofs, tofs, lazy);
            } else {
                throw new InvalidDataException("Unrecognized SFF version " + sff.VersionHigh);
            }

            foreach (var s in sff.Sprites) {
                int k = Key(s.Group, s.Number);
                if (!sff.byKey.ContainsKey(k)) sff.byKey[k] = s;     // duplicates: first wins, as in Ikemen
            }
            return sff;
        }

        // ------------------------------------------------------------------ SFF v1

        void ReadV1(ByteReader r, int count, int first, bool isCharacter) {
            int shofs = first;
            SffSprite prev = null;
            for (int i = 0; i < count; i++) {
                if (shofs <= 0 || shofs + 19 > r.Length) break;
                uint next = r.U32(shofs);
                uint size = r.U32(shofs + 4);
                var s = new SffSprite {
                    Index = i,
                    X = r.I16(shofs + 8),
                    Y = r.I16(shofs + 10),
                    Group = r.U16(shofs + 12),
                    Number = r.U16(shofs + 14),
                    Link = r.U16(shofs + 16),
                    Format = -1
                };
                byte ps = r.U8(shofs + 18);
                Sprites.Add(s);

                if (size == 0) {
                    if (s.Link < i) {
                        var src = Sprites[s.Link];
                        s.Width = src.Width; s.Height = src.Height;
                        s.Pixels = src.Pixels; s.OwnPalette = src.OwnPalette;
                        s.Raw = src.Raw;
                    }
                    shofs = (int)next;
                    continue;
                }

                int offset = shofs + 32;
                // PCX header
                byte encoding = r.U8(offset + 2), bpp = r.U8(offset + 3);
                if (bpp != 8) throw new InvalidDataException("Invalid PCX color depth: " + bpp);
                int x0 = r.U16(offset + 4), y0 = r.U16(offset + 6), x1 = r.U16(offset + 8), y1 = r.U16(offset + 10);
                int bpl = r.U16(offset + 66);
                s.Width = x1 - x0 + 1;
                s.Height = y1 - y0 + 1;
                int rleBpl = encoding == 1 ? bpl : 0;
                int pcxDataStart = offset + 128;
                bool paletteSame = ps != 0 && prev != null;

                byte[] px;
                int paletteOffset;
                bool paletteHasMarker;
                bool isCharFirstSprite = isCharacter && (prev == null || (s.Group == 0 && s.Number == 0));
                if (isCharFirstSprite) {
                    int dataSize = next > (uint)offset ? (int)(next - offset) : (int)size;
                    px = r.Slice(pcxDataStart, Math.Max(0, dataSize - 128));
                    paletteOffset = offset + dataSize - 768;
                    paletteHasMarker = false;
                } else {
                    int blockEnd = next > (uint)offset ? (int)next : offset + (int)size;
                    if (paletteSame) {
                        paletteOffset = blockEnd;
                    } else {
                        paletteOffset = -1;
                        for (int pos = blockEnd - 769; pos >= pcxDataStart; pos--) {
                            if (pos < r.Length && r.U8(pos) == 0x0C) { paletteOffset = pos; break; }
                        }
                        if (paletteOffset < 0) paletteOffset = blockEnd - 769;
                    }
                    px = r.Slice(pcxDataStart, Math.Max(0, paletteOffset - pcxDataStart));
                    paletteHasMarker = true;
                }

                if (paletteSame) {
                    s.OwnPalette = prev.OwnPalette;
                } else {
                    s.OwnPalette = ReadPaletteRgb(r, paletteOffset + (paletteHasMarker ? 1 : 0));
                }
                s.Pixels = SffDecoders.RlePcx(px, s.Width, s.Height, rleBpl);
                prev = s;
                shofs = (int)next;
            }
        }

        /// <summary>256 RGB triples; index 0 is transparent, as everywhere in MUGEN.</summary>
        static uint[] ReadPaletteRgb(ByteReader r, int offset) {
            var pal = new uint[256];
            for (int i = 0; i < 256; i++) {
                int o = offset + i * 3;
                byte red = o + 2 < r.Length ? r.U8(o) : (byte)0;
                byte green = o + 2 < r.Length ? r.U8(o + 1) : (byte)0;
                byte blue = o + 2 < r.Length ? r.U8(o + 2) : (byte)0;
                uint alpha = i == 0 ? 0u : 255u;
                pal[i] = (alpha << 24) | ((uint)blue << 16) | ((uint)green << 8) | red;
            }
            return pal;
        }

        // ------------------------------------------------------------------ SFF v2

        void ReadPalettesV2(ByteReader r, int count, int firstHeader, uint lofs) {
            var unique = new Dictionary<int, int>();
            for (int i = 0; i < count; i++) {
                int h = firstHeader + i * 16;
                if (h + 16 > r.Length) break;
                ushort g = r.U16(h), n = r.U16(h + 2);
                ushort link = r.U16(h + 6);
                uint ofs = r.U32(h + 8), size = r.U32(h + 12);
                uint[] pal;
                int idx;
                int key = Key(g, n);
                if (unique.TryGetValue(key, out var old)) {
                    idx = old;
                    pal = idx < Palettes.Count ? Palettes[idx] : new uint[256];
                } else if (size == 0) {
                    idx = link;
                    pal = idx < Palettes.Count ? Palettes[idx] : new uint[256];
                } else {
                    pal = ReadPaletteV2(r, (int)(lofs + ofs), size);
                    idx = i;
                }
                unique[key] = idx;
                Palettes.Add(pal);
                PaletteTable[key] = idx;
            }
        }

        uint[] ReadPaletteV2(ByteReader r, int offset, uint size) {
            int rawCount = (int)(size / 4);
            int depth = 1;
            while (depth < rawCount) depth *= 2;
            depth = Math.Max(16, Math.Min(256, depth));
            var pal = new uint[depth];
            for (int i = 0; i < depth; i++) {
                byte red = 0, green = 0, blue = 0, alpha = 0;
                if (i < rawCount && offset + i * 4 + 3 < r.Length) {
                    red = r.U8(offset + i * 4);
                    green = r.U8(offset + i * 4 + 1);
                    blue = r.U8(offset + i * 4 + 2);
                    alpha = r.U8(offset + i * 4 + 3);
                }
                if (VersionLo2 == 0) alpha = i == 0 ? (byte)0 : (byte)255;   // SFF 2.0.0.0
                pal[i] = ((uint)alpha << 24) | ((uint)blue << 16) | ((uint)green << 8) | red;
            }
            return pal;
        }

        void ReadV2(ByteReader r, int count, int first, uint lofs, uint tofs, bool lazy) {
            int shofs = first;
            for (int i = 0; i < count; i++) {
                if (shofs + 28 > r.Length) break;
                var s = new SffSprite {
                    Index = i,
                    Group = r.U16(shofs),
                    Number = r.U16(shofs + 2),
                    Width = r.U16(shofs + 4),
                    Height = r.U16(shofs + 6),
                    X = r.I16(shofs + 8),
                    Y = r.I16(shofs + 10),
                    Link = r.U16(shofs + 12),
                    Format = r.U8(shofs + 14),
                    ColorDepth = r.U8(shofs + 15),
                    PaletteIndex = r.U16(shofs + 24)
                };
                uint dataOfs = r.U32(shofs + 16);
                uint dataSize = r.U32(shofs + 20);
                ushort flags = r.U16(shofs + 26);
                dataOfs += (flags & 1) == 0 ? lofs : tofs;
                Sprites.Add(s);
                shofs += 28;

                if (dataSize == 0) {
                    if (s.Link < i) {
                        var src = Sprites[s.Link];
                        s.Width = src.Width; s.Height = src.Height;
                        s.PaletteIndex = src.PaletteIndex;
                        if (lazy && src.Pending) {
                            var ls = s;
                            s.Decode = () => {
                                ls.Pixels = src.Pixels; ls.Raw = src.Raw; ls.ColorDepth = src.ColorDepth;
                                ls.OwnPalette = src.OwnPalette; ls.Width = src.Width; ls.Height = src.Height;
                            };
                        } else {
                            s.Pixels = src.Pixels; s.Raw = src.Raw;
                            s.ColorDepth = src.ColorDepth;
                            s.OwnPalette = src.OwnPalette;
                        }
                    }
                    continue;
                }

                if (lazy) {
                    var ls = s; uint o = dataOfs, n = dataSize;
                    s.Decode = () => DecodeV2(r, ls, o, n);
                } else {
                    DecodeV2(r, s, dataOfs, dataSize);
                }
            }
        }

        void DecodeV2(ByteReader r, SffSprite s, uint dataOfs, uint dataSize) {
                switch (s.Format) {
                    case 0:
                        s.Pixels = r.Slice((int)dataOfs, (int)dataSize);
                        if (s.ColorDepth == 24 || s.ColorDepth == 32) s.Raw = true;
                        else if (s.ColorDepth != 8) throw new InvalidDataException("Unknown color depth " + s.ColorDepth);
                        break;
                    case 2:
                        s.Pixels = SffDecoders.Rle8(r.Slice((int)dataOfs + 4, (int)dataSize - 4), s.Width, s.Height);
                        break;
                    case 3:
                        s.Pixels = SffDecoders.Rle5(r.Slice((int)dataOfs + 4, (int)dataSize - 4), s.Width, s.Height);
                        break;
                    case 4:
                        s.Pixels = SffDecoders.Lz5(r.Slice((int)dataOfs + 4, (int)dataSize - 4), s.Width, s.Height);
                        break;
                    case 10:
                    case 11:
                    case 12: {
                        var img = PngReader.Decode(r.Data, (int)dataOfs + 4, (int)dataSize - 4);
                        if (img.IsIndexed) {
                            s.Pixels = img.Indices;
                            if (s.PaletteIndex < 0 || s.PaletteIndex >= Palettes.Count) s.OwnPalette = img.Palette;
                        } else {
                            s.Raw = true;
                            s.ColorDepth = 32;
                            s.Pixels = img.Rgba;
                        }
                        s.Width = img.Width;
                        s.Height = img.Height;
                        break;
                    }
                    default:
                        throw new InvalidDataException("Unknown sprite format " + s.Format);
                }
        }

        /// <summary>Little-endian reader over a byte[] with clamped slices.</summary>
        class ByteReader {
            public readonly byte[] Data;
            public ByteReader(byte[] d) { Data = d; }
            public int Length => Data.Length;
            public byte U8(int o) => (uint)o < (uint)Data.Length ? Data[o] : (byte)0;
            public ushort U16(int o) => (ushort)(U8(o) | (U8(o + 1) << 8));
            public short I16(int o) => (short)U16(o);
            public uint U32(int o) => (uint)(U8(o) | (U8(o + 1) << 8) | (U8(o + 2) << 16) | (U8(o + 3) << 24));
            public byte[] Slice(int offset, int length) {
                if (length <= 0 || offset >= Data.Length) return new byte[Math.Max(0, length)];
                int n = Math.Min(length, Data.Length - Math.Max(0, offset));
                var b = new byte[length];
                Array.Copy(Data, Math.Max(0, offset), b, 0, n);
                return b;
            }
        }
    }
}
