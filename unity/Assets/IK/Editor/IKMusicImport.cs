using UnityEditor;
using UnityEngine;

namespace IK.EditorTools {
    /// <summary>
    /// dev.6: the background music (Resources/music/*.ogg) stays Vorbis-compressed in memory
    /// instead of Unity's default decompress-on-load (an 80 s stereo track would be ~14 MB of
    /// PCM each on the phone).
    /// </summary>
    public class IKMusicImport : AssetPostprocessor {
        void OnPreprocessAudio() {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/music/")) return;
            var imp = (AudioImporter)assetImporter;
            var s = imp.defaultSampleSettings;
            s.loadType = AudioClipLoadType.CompressedInMemory;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.5f;
            s.preloadAudioData = false;
            imp.defaultSampleSettings = s;
            imp.loadInBackground = true;
        }
    }
}
