using System;
using System.IO;
using System.IO.Compression;

namespace IK.Core {
    /// <summary>
    /// Minimal PNG decoder for the SFF v2 PNG sprite formats (10 = 8-bit paletted,
    /// 11 = 24-bit, 12 = 32-bit). Unity's <c>Texture2D.LoadImage</c> cannot be used for
    /// format 10 because it throws the palette away, and a palette-indexed sprite is what
    /// MUGEN palette swapping needs. Supports colour types 0/2/3/6 at bit depth 8, plus
    /// 1/2/4-bit palettes; interlaced PNGs are rejected (MUGEN never writes them).
    /// </summary>
    public static class PngReader {
        public class Image {
            public int Width, Height;
            public byte[] Indices;      // colour type 3: one byte per pixel
            public uint[] Palette;      // colour type 3: packed 0xAABBGGRR
            public byte[] Rgba;         // other colour types: 4 bytes per pixel
            public bool IsIndexed => Indices != null;
        }

        public static bool TryDecode(byte[] data, int offset, int length, out Image image, out string error) {
            image = null;
            error = null;
            try {
                image = Decode(data, offset, length);
                return true;
            } catch (Exception e) {
                error = e.Message;
                return false;
            }
        }

        public static Image Decode(byte[] data, int offset, int length) {
            if (data == null || length < 8) throw new InvalidDataException("PNG too short");
            int p = offset;
            if (data[p] != 0x89 || data[p + 1] != 'P' || data[p + 2] != 'N' || data[p + 3] != 'G')
                throw new InvalidDataException("not a PNG");
            p += 8;
            int end = offset + length;

            int width = 0, height = 0, bitDepth = 0, colorType = 0;
            uint[] palette = null;
            byte[] trns = null;
            var idat = new MemoryStream();

            while (p + 8 <= end) {
                int len = ReadInt32(data, p); p += 4;
                string type = "" + (char)data[p] + (char)data[p + 1] + (char)data[p + 2] + (char)data[p + 3];
                p += 4;
                if (len < 0 || p + len > end) throw new InvalidDataException("truncated PNG chunk " + type);
                switch (type) {
                    case "IHDR":
                        width = ReadInt32(data, p);
                        height = ReadInt32(data, p + 4);
                        bitDepth = data[p + 8];
                        colorType = data[p + 9];
                        if (data[p + 12] != 0) throw new NotSupportedException("interlaced PNG");
                        break;
                    case "PLTE": {
                        int n = len / 3;
                        palette = new uint[n];
                        for (int i = 0; i < n; i++)
                            palette[i] = 0xff000000u | ((uint)data[p + i * 3 + 2] << 16) |
                                         ((uint)data[p + i * 3 + 1] << 8) | data[p + i * 3];
                        break;
                    }
                    case "tRNS":
                        trns = new byte[len];
                        Array.Copy(data, p, trns, 0, len);
                        break;
                    case "IDAT":
                        idat.Write(data, p, len);
                        break;
                }
                p += len + 4;   // skip the CRC
                if (type == "IEND") break;
            }

            if (width <= 0 || height <= 0) throw new InvalidDataException("PNG without IHDR");
            byte[] raw = Inflate(idat.ToArray());

            int channels;
            switch (colorType) {
                case 0: channels = 1; break;
                case 2: channels = 3; break;
                case 3: channels = 1; break;
                case 4: channels = 2; break;
                case 6: channels = 4; break;
                default: throw new NotSupportedException("PNG colour type " + colorType);
            }
            if (colorType != 3 && bitDepth != 8) throw new NotSupportedException("PNG bit depth " + bitDepth);
            if (colorType == 3 && bitDepth != 1 && bitDepth != 2 && bitDepth != 4 && bitDepth != 8)
                throw new NotSupportedException("PNG palette bit depth " + bitDepth);

            int bpp = Math.Max(1, channels * bitDepth / 8);
            int stride = (width * channels * bitDepth + 7) / 8;
            var lines = Unfilter(raw, width, height, stride, bpp);

            var img = new Image { Width = width, Height = height };
            if (colorType == 3) {
                if (palette == null) throw new InvalidDataException("paletted PNG without PLTE");
                if (trns != null)
                    for (int i = 0; i < trns.Length && i < palette.Length; i++)
                        palette[i] = (palette[i] & 0x00ffffffu) | ((uint)trns[i] << 24);
                img.Palette = palette;
                img.Indices = new byte[width * height];
                for (int y = 0; y < height; y++) {
                    int src = y * stride;
                    for (int x = 0; x < width; x++) {
                        byte v;
                        if (bitDepth == 8) v = lines[src + x];
                        else {
                            int perByte = 8 / bitDepth;
                            byte b = lines[src + x / perByte];
                            int shift = 8 - bitDepth * (x % perByte + 1);
                            v = (byte)((b >> shift) & ((1 << bitDepth) - 1));
                        }
                        img.Indices[y * width + x] = v;
                    }
                }
                return img;
            }

            img.Rgba = new byte[width * height * 4];
            for (int y = 0; y < height; y++) {
                int src = y * stride;
                for (int x = 0; x < width; x++) {
                    int o = (y * width + x) * 4;
                    switch (colorType) {
                        case 0: {
                            byte g = lines[src + x];
                            img.Rgba[o] = g; img.Rgba[o + 1] = g; img.Rgba[o + 2] = g; img.Rgba[o + 3] = 255;
                            break;
                        }
                        case 2:
                            img.Rgba[o] = lines[src + x * 3];
                            img.Rgba[o + 1] = lines[src + x * 3 + 1];
                            img.Rgba[o + 2] = lines[src + x * 3 + 2];
                            img.Rgba[o + 3] = 255;
                            break;
                        case 4: {
                            byte g = lines[src + x * 2];
                            img.Rgba[o] = g; img.Rgba[o + 1] = g; img.Rgba[o + 2] = g;
                            img.Rgba[o + 3] = lines[src + x * 2 + 1];
                            break;
                        }
                        default:
                            img.Rgba[o] = lines[src + x * 4];
                            img.Rgba[o + 1] = lines[src + x * 4 + 1];
                            img.Rgba[o + 2] = lines[src + x * 4 + 2];
                            img.Rgba[o + 3] = lines[src + x * 4 + 3];
                            break;
                    }
                }
            }
            return img;
        }

        /// <summary>zlib stream (2-byte header + raw deflate + adler32) to bytes.</summary>
        static byte[] Inflate(byte[] zlib) {
            if (zlib.Length < 2) return new byte[0];
            using (var input = new MemoryStream(zlib, 2, zlib.Length - 2))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream()) {
                deflate.CopyTo(output);
                return output.ToArray();
            }
        }

        /// <summary>Reverses the PNG per-scanline filters, returning height*stride bytes.</summary>
        static byte[] Unfilter(byte[] raw, int width, int height, int stride, int bpp) {
            var outBuf = new byte[height * stride];
            int src = 0;
            for (int y = 0; y < height; y++) {
                if (src >= raw.Length) break;
                int filter = raw[src++];
                int dst = y * stride;
                int prev = dst - stride;
                for (int x = 0; x < stride; x++) {
                    byte cur = src < raw.Length ? raw[src++] : (byte)0;
                    byte a = x >= bpp ? outBuf[dst + x - bpp] : (byte)0;
                    byte b = y > 0 ? outBuf[prev + x] : (byte)0;
                    byte c = (y > 0 && x >= bpp) ? outBuf[prev + x - bpp] : (byte)0;
                    int v;
                    switch (filter) {
                        case 0: v = cur; break;
                        case 1: v = cur + a; break;
                        case 2: v = cur + b; break;
                        case 3: v = cur + ((a + b) >> 1); break;
                        case 4: v = cur + Paeth(a, b, c); break;
                        default: v = cur; break;
                    }
                    outBuf[dst + x] = (byte)v;
                }
            }
            return outBuf;
        }

        static int Paeth(byte a, byte b, byte c) {
            int pp = a + b - c;
            int pa = Math.Abs(pp - a), pb = Math.Abs(pp - b), pc = Math.Abs(pp - c);
            if (pa <= pb && pa <= pc) return a;
            return pb <= pc ? b : c;
        }

        static int ReadInt32(byte[] d, int p) =>
            (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3];
    }
}
