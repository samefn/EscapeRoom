using UnityEditor;
using UnityEngine;

namespace EscapeRoom.EditorTools
{
    /// <summary>
    /// Ajusta solo las imágenes del tema Night Terror (Assets/Resources/NightTerror) cada vez que se importan:
    /// sin compresión ni mipmaps (los marcos de 9 trozos se ven nítidos) y sin reescalar a potencia de dos.
    /// </summary>
    public class UiAssetImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Resources/NightTerror/")) return;
            var imp = (TextureImporter)assetImporter;
            bool tile = System.IO.Path.GetFileNameWithoutExtension(assetPath) == "noise";
            imp.textureType = TextureImporterType.Default;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.mipmapEnabled = false;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.alphaIsTransparency = true;
            imp.wrapMode = tile ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            imp.filterMode = FilterMode.Bilinear;
            imp.maxTextureSize = 2048;
        }
    }
}
