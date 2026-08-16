using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class FactoryEscapeAssetSizeOptimizer
{
    private const string RequestPath = "Temp/ApplyAssetSizeOptimization.request";
    private const string ResultPath = "Temp/ApplyAssetSizeOptimization.result";

    private static readonly string[] RuntimeArtworkFolders =
    {
        "Assets/Resources/Art/BackGround",
        "Assets/Resources/Art/CutScene",
        "Assets/Resources/Animator/CutScene",
        "Assets/Resources/Art/Character",
        "Assets/Resources/Art/UI",
        "Assets/Resources/Art/Node"
    };

    static FactoryEscapeAssetSizeOptimizer()
    {
        EditorApplication.delayCall += RunPendingRequest;
    }

    [MenuItem("Tools/Factory Escape/应用 Android 基础图片尺寸")]
    public static void ApplyFromMenu()
    {
        Debug.Log(Apply());
    }

    private static void RunPendingRequest()
    {
        if (!File.Exists(RequestPath))
        {
            return;
        }

        File.Delete(RequestPath);
        try
        {
            string result = Apply();
            File.WriteAllText(ResultPath, "SUCCESS\n" + result, new UTF8Encoding(false));
        }
        catch (Exception exception)
        {
            File.WriteAllText(ResultPath, "ERROR\n" + exception, new UTF8Encoding(false));
            Debug.LogException(exception);
        }
    }

    public static string Apply()
    {
        string[] texturePaths = RuntimeArtworkFolders
            .Where(AssetDatabase.IsValidFolder)
            .SelectMany(folder => AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => !string.IsNullOrEmpty(path))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var changedPaths = new List<string>();
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string path in texturePaths)
            {
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                {
                    continue;
                }

                int maxSize = GetMaxSize(path);
                TextureImporterFormat format = GetAndroidFormat(path);
                bool changed = importer.maxTextureSize != maxSize ||
                               importer.mipmapEnabled ||
                               importer.textureCompression != TextureImporterCompression.Compressed;

                importer.maxTextureSize = maxSize;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.crunchedCompression = false;

                TextureImporterPlatformSettings android =
                    importer.GetPlatformTextureSettings("Android");
                changed |= !android.overridden ||
                           android.maxTextureSize != maxSize ||
                           android.format != format ||
                           android.textureCompression != TextureImporterCompression.Compressed;
                android.name = "Android";
                android.overridden = true;
                android.maxTextureSize = maxSize;
                android.format = format;
                android.textureCompression = TextureImporterCompression.Compressed;
                android.compressionQuality = 50;
                importer.SetPlatformTextureSettings(android);

                if (changed && AssetDatabase.WriteImportSettingsIfDirty(path))
                {
                    changedPaths.Add(path);
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        foreach (string path in changedPaths)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        AssetDatabase.SaveAssets();

        string result = $"Android 图片优化完成：扫描 {texturePaths.Length}，更新 {changedPaths.Count}；" +
                        "背景/剧情最长边 1024，人物/UI 512，小图标 256。";
        Debug.Log(result);
        return result;
    }

    private static int GetMaxSize(string path)
    {
        string normalized = path.Replace('\\', '/');
        if (normalized.Contains("/Art/UI/ButtonUI/") ||
            normalized.Contains("/Art/Node/QTE/"))
        {
            return 256;
        }

        if (normalized.Contains("/Art/Character/") ||
            normalized.Contains("/Art/UI/") ||
            normalized.Contains("/Art/Node/"))
        {
            return 512;
        }

        return 1024;
    }

    private static TextureImporterFormat GetAndroidFormat(string path)
    {
        string normalized = path.Replace('\\', '/');
        return normalized.Contains("/Art/BackGround/") ||
               normalized.Contains("/Art/CutScene/") ||
               normalized.Contains("/Animator/CutScene/")
            ? TextureImporterFormat.ASTC_8x8
            : TextureImporterFormat.ASTC_6x6;
    }
}
