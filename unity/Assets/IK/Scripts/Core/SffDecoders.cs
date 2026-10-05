namespace IK.Core {
    /// <summary>
    /// The four sprite compressions used by SFF, ported 1:1 from Ikemen GO
    /// (`engine/ikemen-go/src/image.go`: RlePcxDecode, Rle8Decode, Rle5Decode, Lz5Decode).
    /// They are deliberately literal ports — including the "stop advancing on the last
    /// byte" quirk — because real character files rely on that tolerance for truncated
    /// streams. `tools/sff_dump.py` is the independent reference the tests compare against.
    /// </summary>
    public static class SffDecoders {
        /// <summary>PCX run-length (SFF v1). <paramref name="bpl"/> is the PCX bytes-per-line.</summary>
        public static byte[] RlePcx(byte[] rle, int width, int height, int bpl) {
            if (rle == null || rle.Length == 0 || bpl <= 0) {
                var copy = new byte[width * height];
                if (rle != null) System.Array.Copy(rle, copy, System.Math.Min(rle.Length, copy.Length));
                return copy;
            }
            var p = new byte[width * height];
            int i = 0, j = 0, k = 0, w = width;
            while (j < p.Length) {
                int n = 1;
                byte d = rle[i];
                if (i < rle.Length - 1) i++;
                if (d >= 0xc0) {
                    n = d & 0x3f;
                    d = rle[i];
                    if (i < rle.Length - 1) i++;
                }
                for (; n > 0; n--) {
                    if (k < w && j < p.Length) p[j++] = d;
                    k++;
                    if (k == bpl) { k = 0; n = 1; }
                }
            }
            return p;
        }

        /// <summary>SFF v2 format 2.</summary>
        public static byte[] Rle8(byte[] rle, int width, int height) {
            var p = new byte[width * height];
            if (rle == null || rle.Length == 0) return p;
            int i = 0, j = 0;
            while (j < p.Length) {
                int n = 1;
                byte d = rle[i];
                if (i < rle.Length - 1) i++;
                if ((d & 0xc0) == 0x40) {
                    n = d & 0x3f;
                    d = rle[i];
                    if (i < rle.Length - 1) i++;
                }
                for (; n > 0; n--) if (j < p.Length) p[j++] = d;
            }
            return p;
        }

        /// <summary>SFF v2 format 3 (5-bit colour runs).</summary>
        public static byte[] Rle5(byte[] rle, int width, int height) {
            var p = new byte[width * height];
            if (rle == null || rle.Length == 0) return p;
            int i = 0, j = 0;
            while (j < p.Length) {
                int rl = rle[i];
                if (i < rle.Length - 1) i++;
                int dl = rle[i] & 0x7f;
                byte c = 0;
                if ((rle[i] >> 7) != 0) {
                    if (i < rle.Length - 1) i++;
                    c = rle[i];
                }
                if (i < rle.Length - 1) i++;
                while (true) {
                    if (j < p.Length) p[j++] = c;
                    rl--;
                    if (rl < 0) {
                        dl--;
                        if (dl < 0) break;
                        c = (byte)(rle[i] & 0x1f);
                        rl = rle[i] >> 5;
                        if (i < rle.Length - 1) i++;
                    }
                }
            }
            return p;
        }

        /// <summary>SFF v2 format 4 (LZ5).</summary>
        public static byte[] Lz5(byte[] rle, int width, int height) {
            var p = new byte[width * height];
            if (rle == null || rle.Length == 0) return p;
            int i = 0, j = 0, n = 0;
            byte ct = rle[i], rb = 0;
            int cts = 0, rbc = 0;
            if (i < rle.Length - 1) i++;
            while (j < p.Length) {
                int d = rle[i];
                if (i < rle.Length - 1) i++;
                if ((ct & (byte)(1 << cts)) != 0) {
                    if ((d & 0x3f) == 0) {
                        d = ((d << 2) | rle[i]) + 1;
                        if (i < rle.Length - 1) i++;
                        n = rle[i] + 2;
                        if (i < rle.Length - 1) i++;
                    } else {
                        rb |= (byte)((d & 0xc0) >> rbc);
                        rbc += 2;
                        n = d & 0x3f;
                        if (rbc < 8) {
                            d = rle[i] + 1;
                            if (i < rle.Length - 1) i++;
                        } else {
                            d = rb + 1;
                            rb = 0; rbc = 0;
                        }
                    }
                    while (true) {
                        if (j < p.Length) { p[j] = j - d >= 0 ? p[j - d] : (byte)0; j++; }
                        n--;
                        if (n < 0) break;
                    }
                } else {
                    if ((d & 0xe0) == 0) {
                        n = rle[i] + 8;
                        if (i < rle.Length - 1) i++;
                    } else {
                        n = d >> 5;
                        d &= 0x1f;
                    }
                    for (; n > 0; n--) if (j < p.Length) p[j++] = (byte)d;
                }
                cts++;
                if (cts >= 8) {
                    ct = rle[i]; cts = 0;
                    if (i < rle.Length - 1) i++;
                }
            }
            return p;
        }
    }
}
