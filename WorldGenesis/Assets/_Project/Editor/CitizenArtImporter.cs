using UnityEditor;
using UnityEngine;

namespace WorldGenesis.Editor
{
    // The original sheets retain their 192px cells; do not rescale their UV grid.
    public sealed class CitizenArtImporter : AssetPostprocessor
    {
        // Import settings are part of the runtime sheet contract. Bumping the version
        // reimports existing sheets as well as those first encountered by this importer.
        public override uint GetVersion() { return 2; }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/_Project/Art/Units/Resources/TinySwordsCC0/")) return;
            TextureImporter importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            // textureType Default does not reset textureShape. These sheets previously
            // had Cube metadata, which makes Resources.Load<Texture2D> return null.
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.isReadable = true;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
        }
    }
}
