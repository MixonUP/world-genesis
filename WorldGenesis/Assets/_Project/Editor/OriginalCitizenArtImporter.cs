using UnityEditor;
using UnityEngine;

namespace WorldGenesis.Editor
{
    // Independent of the preserved Tiny Swords import contract and GUIDs.
    public sealed class OriginalCitizenArtImporter : AssetPostprocessor
    {
        public override uint GetVersion() { return 1; }
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/_Project/Art/Units/Resources/OriginalCitizens/")) return;
            TextureImporter importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.isReadable = true;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 8192;
        }
    }
}
