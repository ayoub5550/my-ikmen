using System;
using System.Collections.Generic;
using UnityEngine;

namespace IK.Core {
    /// <summary>
    /// Reads character files from <c>Resources</c>. On Android the APK has no readable file
    /// system for assets, so every MUGEN file ships as a <c>TextAsset</c>: the extension is
    /// replaced by an underscore (kfm.sff -> chars/kfm/kfm_sff.bytes) because Unity strips
    /// the last extension from the asset name and would otherwise make "kfm.sff" ambiguous.
    /// </summary>
    public class ResourcesSource : IResourceSource {
        readonly string root;
        public ResourcesSource(string root) { this.root = root.TrimEnd('/'); }

        public static string AssetName(string fileName) {
            var name = fileName.Replace('\\', '/');
            int slash = name.LastIndexOf('/');
            if (slash >= 0) name = name.Substring(slash + 1);
            return name.Replace('.', '_').ToLowerInvariant();
        }

        public byte[] Read(string fileName) {
            if (string.IsNullOrEmpty(fileName)) return null;
            var path = root + "/" + AssetName(fileName);
            var asset = Resources.Load<TextAsset>(path);
            return asset != null ? asset.bytes : null;
        }
    }

    /// <summary>Reads character files from a folder on disk (editor tools and EditMode tests).</summary>
    public class FileSource : IResourceSource {
        readonly string folder;
        public FileSource(string folder) { this.folder = folder; }

        public byte[] Read(string fileName) {
            if (string.IsNullOrEmpty(fileName)) return null;
            var path = System.IO.Path.Combine(folder, fileName);
            return System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null;
        }
    }

    /// <summary>
    /// Turns decoded SFF sprites into Unity sprites, and SND waves into AudioClips.
    /// Textures are RGBA32 and the pivot is placed on the MUGEN axis, so a sprite drawn at the
    /// character position lands exactly where the engine would draw it. Results are cached per
    /// (sprite, palette).
    ///
    /// dev.7: keys are numbers (no per-draw string), textures are cached too (the parallax path
    /// used to create a new texture every frame), the CPU copy is released after upload, the
    /// colour of transparent texels is bled from their opaque neighbours so bilinear / crisp
    /// filtering has no dark fringes, and the filter follows Options → Video for every live cache.
    /// </summary>
    public class MugenAssetCache : IDisposable {
        readonly Dictionary<long, Sprite> sprites = new Dictionary<long, Sprite>();
        readonly Dictionary<long, Texture2D> texByKey = new Dictionary<long, Texture2D>();
        readonly Dictionary<long, AudioClip> clips = new Dictionary<long, AudioClip>();
        readonly List<Texture2D> textures = new List<Texture2D>();

        static readonly List<WeakReference<MugenAssetCache>> live = new List<WeakReference<MugenAssetCache>>();
        static FilterMode filter = FilterMode.Point;

        /// <summary>Texture filter of every MUGEN sprite (Point = sharp, Bilinear = smooth / crisp).</summary>
        public static FilterMode Filter {
            get => filter;
            set {
                if (filter == value) return;
                filter = value;
                for (int i = live.Count - 1; i >= 0; i--) {
                    if (!live[i].TryGetTarget(out var c)) { live.RemoveAt(i); continue; }
                    foreach (var t in c.textures) if (t != null) t.filterMode = value;
                }
            }
        }

        /// <summary>Textures created since start-up (benchmark / tests).</summary>
        public static int TexturesCreated { get; private set; }

        public MugenAssetCache() {
            for (int i = live.Count - 1; i >= 0; i--) if (!live[i].TryGetTarget(out _)) live.RemoveAt(i);
            live.Add(new WeakReference<MugenAssetCache>(this));
        }

        public int SpriteCount => sprites.Count;
        public int TextureCount => textures.Count;

        static long Key(SffSprite s, uint[] pal) =>
            ((long)s.Index << 32) | (uint)(pal != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(pal) : 0);

        /// <summary>Unity sprite for an SFF sprite, with its axis as the pivot.</summary>
        public Sprite SpriteFor(SffFile sff, SffSprite s, uint[] paletteOverride = null) {
            if (s == null || s.IsBlank) return null;
            var pal = paletteOverride ?? sff.PaletteFor(s);
            long key = Key(s, pal);
            if (sprites.TryGetValue(key, out var cached)) return cached;

            var tex = CachedTexture(s, pal, key);
            // pivot in normalised texture space: MUGEN axis (X,Y) counted from the top-left
            var pivot = new Vector2(s.Width > 0 ? (float)s.X / s.Width : 0.5f,
                                    s.Height > 0 ? 1f - (float)s.Y / s.Height : 0.5f);
            var sprite = Sprite.Create(tex, new Rect(0, 0, s.Width, s.Height), pivot, 1f, 0,
                                       SpriteMeshType.FullRect);
            sprite.name = "mugen";
            sprites[key] = sprite;
            return sprite;
        }

        /// <summary>Cached texture for a sprite + palette (parallax backgrounds).</summary>
        public Texture2D CachedTextureFor(SffSprite s, uint[] pal) {
            if (s == null || s.IsBlank) return null;
            return CachedTexture(s, pal, Key(s, pal));
        }

        Texture2D CachedTexture(SffSprite s, uint[] pal, long key) {
            if (texByKey.TryGetValue(key, out var t) && t != null) return t;
            t = TextureFor(s, pal);
            texByKey[key] = t;
            return t;
        }

        /// <summary>
        /// Builds the textures of the given frames now (fight load), so a move never stalls the
        /// first time it is drawn. Returns the number of sprites created.
        /// </summary>
        public int Prewarm(SffFile sff, IEnumerable<long> groupNumbers, uint[] pal) {
            if (sff == null || groupNumbers == null) return 0;
            int made = 0;
            foreach (var gn in groupNumbers) {
                var spr = sff.Get((int)(gn >> 16), (int)(gn & 0xffff));
                if (spr == null || spr.IsBlank) continue;
                var p = spr.Raw || spr.OwnPalette != null ? null : pal;
                int before = sprites.Count;
                SpriteFor(sff, spr, p);
                if (sprites.Count > before) made++;
            }
            return made;
        }

        /// <summary>RGBA32 texture; index 0 and palette alpha give transparency.</summary>
        public Texture2D TextureFor(SffSprite s, uint[] pal) {
            var tex = new Texture2D(s.Width, s.Height, TextureFormat.RGBA32, false) {
                filterMode = filter,
                wrapMode = TextureWrapMode.Clamp,
                name = "mugen " + s.Group + "," + s.Number
            };
            var px = Pixels(s, pal);
            tex.SetPixels32(px);
            tex.Apply(false, true);                 // upload and drop the CPU copy
            textures.Add(tex);
            TexturesCreated++;
            return tex;
        }

        /// <summary>The decoded RGBA pixels, bottom-up, with transparent texels bled.</summary>
        public static Color32[] Pixels(SffSprite s, uint[] pal) {
            var px = new Color32[s.Width * s.Height];
            int w = s.Width, h = s.Height;
            var src = s.Pixels;
            if (s.Raw) {
                int bpp = s.ColorDepth == 24 ? 3 : 4;
                for (int y = 0; y < h; y++) {
                    int srcRow = y * w * bpp;
                    int dstRow = (h - 1 - y) * w;      // Unity textures are bottom-up
                    for (int x = 0; x < w; x++) {
                        int o = srcRow + x * bpp;
                        byte r = o < src.Length ? src[o] : (byte)0;
                        byte g = o + 1 < src.Length ? src[o + 1] : (byte)0;
                        byte b = o + 2 < src.Length ? src[o + 2] : (byte)0;
                        byte a = bpp == 4 ? (o + 3 < src.Length ? src[o + 3] : (byte)255) : (byte)255;
                        px[dstRow + x] = new Color32(r, g, b, a);
                    }
                }
            } else {
                int palLen = pal != null ? pal.Length : 0;
                for (int y = 0; y < h; y++) {
                    int srcRow = y * w;
                    int dstRow = (h - 1 - y) * w;
                    for (int x = 0; x < w; x++) {
                        int si = srcRow + x;
                        byte idx = si < src.Length ? src[si] : (byte)0;
                        uint c = idx < palLen ? pal[idx] : 0u;
                        byte a = idx == 0 ? (byte)0 : (byte)(c >> 24);   // MUGEN: index 0 is transparent
                        px[dstRow + x] = new Color32((byte)(c & 0xff), (byte)((c >> 8) & 0xff),
                                                     (byte)((c >> 16) & 0xff), a);
                    }
                }
            }
            Bleed(px, w, h);
            return px;
        }

        /// <summary>
        /// Gives every fully transparent texel the average colour of its opaque 8-neighbours
        /// (alpha stays 0), so filtering across a sprite edge blends towards the sprite colour
        /// instead of black. Invisible with point filtering.
        /// </summary>
        public static void Bleed(Color32[] px, int w, int h) {
            for (int y = 0; y < h; y++) {
                for (int x = 0; x < w; x++) {
                    int i = y * w + x;
                    if (px[i].a != 0) continue;
                    int r = 0, g = 0, b = 0, n = 0;
                    for (int dy = -1; dy <= 1; dy++) {
                        int yy = y + dy;
                        if (yy < 0 || yy >= h) continue;
                        int row = yy * w;
                        for (int dx = -1; dx <= 1; dx++) {
                            int xx = x + dx;
                            if (xx < 0 || xx >= w || (dx == 0 && dy == 0)) continue;
                            var c = px[row + xx];
                            if (c.a < 128) continue;
                            r += c.r; g += c.g; b += c.b; n++;
                        }
                    }
                    px[i] = n > 0 ? new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 0) : new Color32(0, 0, 0, 0);
                }
            }
        }

        /// <summary>AudioClip for a .snd entry (decoded PCM, no streaming).</summary>
        public AudioClip ClipFor(SndEntry e) {
            if (e == null || e.Samples == null || e.Channels <= 0 || e.SampleRate <= 0) return null;
            long key = ((long)e.Group << 32) | (uint)e.Number;
            if (clips.TryGetValue(key, out var cached)) return cached;
            var clip = AudioClip.Create("snd " + e.Group + "," + e.Number, e.SampleCount, e.Channels,
                                        e.SampleRate, false);
            clip.SetData(e.Samples, 0);
            clips[key] = clip;
            return clip;
        }

        static void Kill(UnityEngine.Object o) {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }

        public void Dispose() {
            foreach (var sp in sprites.Values) Kill(sp);
            foreach (var t in textures) Kill(t);
            textures.Clear();
            texByKey.Clear();
            sprites.Clear();
            clips.Clear();
        }
    }
}
