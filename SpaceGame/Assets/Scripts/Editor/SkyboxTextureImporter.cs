using UnityEditor;
using UnityEngine;

// Auto-applies correct import settings for textures under Textures/Skybox/ and Textures/Moon/.
class SkyboxTextureImporter : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (assetPath.Contains("Textures/Skybox"))
        {
            var importer = (TextureImporter)assetImporter;
            importer.textureShape       = TextureImporterShape.Texture2D;
            importer.wrapMode           = TextureWrapMode.Clamp;
            importer.maxTextureSize     = 8192;
            importer.sRGBTexture        = true;
            importer.mipmapEnabled      = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }
        else if (assetPath.Contains("Textures/Moon"))
        {
            var importer = (TextureImporter)assetImporter;
            importer.textureShape  = TextureImporterShape.Texture2D;
            importer.wrapMode      = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;

            bool isDisplacement = assetPath.Contains("Displacement");
            importer.sRGBTexture        = !isDisplacement;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            if (isDisplacement)
                importer.textureType = TextureImporterType.SingleChannel;
        }
    }
}
