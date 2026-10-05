#if UNITY_EDITOR
using UnityEditor;

namespace IK.EditorTools {
    /// <summary>Imports the Fist Forge icon as a plain, uncompressed, readable Texture2D (launcher icon + title logo).</summary>
    public class IKIconImport : AssetPostprocessor {
        void OnPreprocessTexture() {
            if (!assetPath.EndsWith("brand/fist-forge-icon.png")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.textureShape = TextureImporterShape.Texture2D;
            ti.mipmapEnabled = false;
            ti.isReadable = true;
            ti.alphaIsTransparency = true;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.maxTextureSize = 1024;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
        }
    }
}
#endif
