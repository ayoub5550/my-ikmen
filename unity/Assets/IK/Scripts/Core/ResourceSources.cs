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
    /// Textures are RGBA32 with point filtering (MUGEN art is pixel art) and the pivot is
    /// placed on the MUGEN axis, so a sprite drawn at the character position lands exactly
    /// where the engine would draw it. Results are cached per (sprite, palette).
    /// </summary>
    public class MugenAssetCache : IDisposable {
        readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        readonly Dictionary<long, AudioClip> clips = new Dictionary<long, AudioClip>();
        readonly List<Texture2D> textures = new List<Texture2D>();

        public int SpriteCount => sprites.Count;

        /// <summary>Unity sprite for an SFF sprite, with its axis as the pivot.</summary>
        public Sprite SpriteFor(SffFile sff, SffSprite s, uint[] paletteOverride = null) {
            if (s == null || s.IsBlank) return null;
            var pal = paletteOverride ?? sff.PaletteFor(s);
            string key = s.Index + ":" + (pal != null ? pal.GetHashCode() : 0);
            if (sprites.TryGetValue(key, out var cached)) return cached;

            var tex = TextureFor(s, pal);
            // pivot in normalised texture space: MUGEN axis (X,Y) counted from the top-left
            var pivot = new Vector2(s.Width > 0 ? (float)s.X / s.Width : 0.5f,
                                    s.Height > 0 ? 1f - (float)s.Y / s.Height : 0.5f);
            var sprite = Sprite.Create(tex, new Rect(0, 0, s.Width, s.Height), pivot, 1f, 0,
                                       SpriteMeshType.FullRect);
            sprite.name = s.Group + "," + s.Number;
            sprites[key] = sprite;
            return sprite;
        }

        /// <summary>RGBA32 texture; index 0 and palette alpha give transparency.</summary>
        public Texture2D TextureFor(SffSprite s, uint[] pal) {
            var tex = new Texture2D(s.Width, s.Height, TextureFormat.RGBA32, false) {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "mugen " + s.Group + "," + s.Number
            };
            var px = new Color32[s.Width * s.Height];
            if (s.Raw) {
                int bpp = s.ColorDepth == 24 ? 3 : 4;
                for (int y = 0; y < s.Height; y++) {
                    int srcRow = y * s.Width * bpp;
                    int dstRow = (s.Height - 1 - y) * s.Width;     // Unity textures are bottom-up
                    for (int x = 0; x < s.Width; x++) {
                        int o = srcRow + x * bpp;
                        byte r = o < s.Pixels.Length ? s.Pixels[o] : (byte)0;
                        byte g = o + 1 < s.Pixels.Length ? s.Pixels[o + 1] : (byte)0;
                        byte b = o + 2 < s.Pixels.Length ? s.Pixels[o + 2] : (byte)0;
                        byte a = bpp == 4 ? (o + 3 < s.Pixels.Length ? s.Pixels[o + 3] : (byte)255) : (byte)255;
                        px[dstRow + x] = new Color32(r, g, b, a);
                    }
                }
            } else {
                for (int y = 0; y < s.Height; y++) {
                    int srcRow = y * s.Width;
                    int dstRow = (s.Height - 1 - y) * s.Width;
                    for (int x = 0; x < s.Width; x++) {
                        byte idx = srcRow + x < s.Pixels.Length ? s.Pixels[srcRow + x] : (byte)0;
                        uint c = pal != null && idx < pal.Length ? pal[idx] : 0u;
                        byte a = (byte)(c >> 24);
                        if (idx == 0) a = 0;                        // MUGEN: index 0 is transparent
                        px[dstRow + x] = new Color32((byte)(c & 0xff), (byte)((c >> 8) & 0xff),
                                                     (byte)((c >> 16) & 0xff), a);
                    }
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            textures.Add(tex);
            return tex;
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

        public void Dispose() {
            foreach (var t in textures) if (t != null) UnityEngine.Object.Destroy(t);
            textures.Clear();
            sprites.Clear();
            clips.Clear();
        }
    }
}
