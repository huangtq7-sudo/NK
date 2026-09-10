using System;
using UnityEditor;
using UnityEngine;

namespace Naraka.EditorTools
{
    /// <summary>
    /// 为全屏UI背景图强制统一导入设置。该目录只存放1:1显示的整屏底图，
    /// 因此设置是强制的而不是仅首次导入：任何机器重新导入都得到相同结果。
    /// 需要不同设置的贴图不要放进这个目录。
    /// </summary>
    public sealed class UiBackgroundTextureImporter : AssetPostprocessor
    {
        public const string BackgroundDirectory = "Assets/Game/Art/UI/Backgrounds/";
        private const int MaximumTextureSize = 2048;

        [MenuItem("NARAKA/Setup/Reimport UI Backgrounds")]
        public static void ReimportAll()
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { BackgroundDirectory.TrimEnd('/') });
            foreach (var guid in guids)
            {
                AssetDatabase.ImportAsset(
                    AssetDatabase.GUIDToAssetPath(guid),
                    ImportAssetOptions.ForceUpdate);
            }

            Debug.Log($"已重新导入{guids.Length}张UI背景图。");
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(BackgroundDirectory, StringComparison.Ordinal))
            {
                return;
            }

            // 整屏背景按1:1显示，不需要Sprite边框、Mipmap或Alpha通道。
            // 天空与云雾有大面积平滑渐变，压缩必须用高质量档以避免色带。
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = MaximumTextureSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.compressionQuality = (int)TextureCompressionQuality.Best;
        }
    }
}
