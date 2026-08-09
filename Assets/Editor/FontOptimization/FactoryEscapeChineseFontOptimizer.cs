using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

[InitializeOnLoad]
public static class FactoryEscapeChineseFontOptimizer
{
    private const string RequestPath = "Library/FactoryEscapeChineseFontOptimization.request";
    private const string ResultPath = "Library/FactoryEscapeChineseFontOptimization.result";
    private const string CharacterSetPath = "Assets/Editor/FontOptimization/FactoryEscapeChineseCharacterSet.txt";
    private const string SourceFontPath = "Assets/Resources/Front/ChineseSubset/FactoryEscapeChineseSubset.otf";
    private const string FontAssetPath = "Assets/Resources/Front/ChineseSubset/FactoryEscapeChineseSubset SDF.asset";

    private const string SourceHanFontGuid = "e552070ed1ea96b4db6885a9e9f17e35";
    private const string SourceHanFontAssetGuid = "8f979eb6be14ead429d5a87a4d395bd5";
    private const long SourceHanMaterialLocalId = -1120416614473179194;
    private const string SimYouFontGuid = "02b096461799f0848859d884b2d9f0d1";
    private const string SimYouFontAssetGuid = "9d766e093c3b9334eaac07c6ef6abeaa";
    private const string AaFontGuid = "2d4a280ee918d7b42a23b22d2952774f";
    private const string AaFontAssetGuid = "c64d717d321e51c4e8c253cdcc35ee8b";
    private const string ZhanKuFontGuid = "3a5d503963ae87f4ebe1b4f4f05e2ab5";

    private static readonly string[] LegacyGuids =
    {
        SourceHanFontGuid,
        SourceHanFontAssetGuid,
        SimYouFontGuid,
        SimYouFontAssetGuid,
        AaFontGuid,
        AaFontAssetGuid,
        ZhanKuFontGuid
    };

    static FactoryEscapeChineseFontOptimizer()
    {
        EditorApplication.delayCall += RunPendingRequest;
    }

    [MenuItem("Tools/Factory Escape/生成通用中文字体")]
    public static void GenerateFromMenu()
    {
        GenerateAndReplace();
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
            string result = GenerateAndReplace();
            File.WriteAllText(ResultPath, "SUCCESS\n" + result, new UTF8Encoding(false));
        }
        catch (Exception exception)
        {
            File.WriteAllText(ResultPath, "ERROR\n" + exception, new UTF8Encoding(false));
            Debug.LogException(exception);
        }
    }

    private static string GenerateAndReplace()
    {
        for (int index = 0; index < EditorSceneManager.sceneCount; index++)
        {
            if (EditorSceneManager.GetSceneAt(index).isDirty)
            {
                throw new InvalidOperationException("检测到未保存的场景，已停止字体替换。请先保存场景后重试。");
            }
        }

        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath) != null)
        {
            throw new InvalidOperationException($"目标字体资产已经存在：{FontAssetPath}");
        }

        AssetDatabase.ImportAsset(CharacterSetPath, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(SourceFontPath, ImportAssetOptions.ForceSynchronousImport);

        TextAsset characterSetAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(CharacterSetPath);
        Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
        if (characterSetAsset == null || sourceFont == null)
        {
            throw new InvalidOperationException("字符表或子集字体尚未导入 Unity。请等待资源导入完成后重试。");
        }

        string characters = characterSetAsset.text;
        TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
            sourceFont,
            48,
            5,
            GlyphRenderMode.SDFAA,
            2048,
            2048,
            AtlasPopulationMode.Dynamic,
            true);
        if (fontAsset == null)
        {
            throw new InvalidOperationException("TMP 字体资产创建失败。");
        }

        fontAsset.name = "FactoryEscapeChineseSubset SDF";
        fontAsset.atlasTextures[0].name = "FactoryEscapeChineseSubset Atlas";
        fontAsset.material.name = "FactoryEscapeChineseSubset Material";
        AssetDatabase.CreateAsset(fontAsset, FontAssetPath);
        AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);
        AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);

        if (!fontAsset.TryAddCharacters(characters, out string missingCharacters, true) ||
            !string.IsNullOrEmpty(missingCharacters))
        {
            throw new InvalidOperationException(
                $"字体图集缺少 {missingCharacters?.Length ?? 0} 个字符：{missingCharacters}");
        }

        fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;
        EditorUtility.SetDirty(fontAsset);
        AssetDatabase.SaveAssets();

        string fontAssetGuid = AssetDatabase.AssetPathToGUID(FontAssetPath);
        string sourceFontGuid = AssetDatabase.AssetPathToGUID(SourceFontPath);
        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                fontAsset.material,
                out string materialGuid,
                out long materialLocalId) ||
            materialGuid != fontAssetGuid)
        {
            throw new InvalidOperationException("无法读取新字体材质的本地文件 ID。");
        }

        int updatedFiles = ReplaceSerializedReferences(
            fontAssetGuid,
            sourceFontGuid,
            materialLocalId);
        SetDefaultTmpFont(fontAssetGuid);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        TMP_FontAsset reloadedFontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        var missingAfterReload = new List<char>();
        if (reloadedFontAsset == null ||
            !reloadedFontAsset.HasCharacters(characters, out missingAfterReload) ||
            missingAfterReload.Count > 0)
        {
            throw new InvalidOperationException(
                $"字体重新导入后的字符覆盖验证失败，缺少 {missingAfterReload?.Count ?? 0} 个字符。");
        }

        string[] remainingReferences = FindLegacyReferences();
        if (remainingReferences.Length > 0)
        {
            throw new InvalidOperationException(
                "仍存在旧字体引用：\n" + string.Join("\n", remainingReferences));
        }

        int atlasCount = reloadedFontAsset.atlasTextures.Count(texture => texture != null);
        string result =
            $"characters={characters.Length}\n" +
            $"atlasTextures={atlasCount}\n" +
            $"updatedFiles={updatedFiles}\n" +
            $"fontAsset={FontAssetPath}\n" +
            $"fontAssetGuid={fontAssetGuid}\n" +
            $"materialLocalId={materialLocalId}";
        Debug.Log("通用中文字体生成完成：\n" + result);
        return result;
    }

    private static int ReplaceSerializedReferences(
        string fontAssetGuid,
        string sourceFontGuid,
        long materialLocalId)
    {
        int updatedFiles = 0;
        string oldMaterialReference =
            $"fileID: {SourceHanMaterialLocalId}, guid: {SourceHanFontAssetGuid}";
        string newMaterialReference =
            $"fileID: {materialLocalId}, guid: {fontAssetGuid}";

        foreach (string assetPath in GetSerializedAssetPaths())
        {
            string content = File.ReadAllText(assetPath);
            string updated = content.Replace(oldMaterialReference, newMaterialReference);
            updated = updated.Replace(SourceHanFontAssetGuid, fontAssetGuid);
            updated = updated.Replace(SimYouFontAssetGuid, fontAssetGuid);
            updated = updated.Replace(AaFontAssetGuid, fontAssetGuid);
            updated = updated.Replace(SourceHanFontGuid, sourceFontGuid);
            updated = updated.Replace(SimYouFontGuid, sourceFontGuid);
            updated = updated.Replace(AaFontGuid, sourceFontGuid);
            updated = updated.Replace(ZhanKuFontGuid, sourceFontGuid);
            if (updated == content)
            {
                continue;
            }

            File.WriteAllText(assetPath, updated, new UTF8Encoding(false));
            updatedFiles++;
        }

        return updatedFiles;
    }

    private static void SetDefaultTmpFont(string fontAssetGuid)
    {
        const string settingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        string content = File.ReadAllText(settingsPath);
        string updated = Regex.Replace(
            content,
            @"(?m)^  m_defaultFontAsset: .*?$",
            $"  m_defaultFontAsset: {{fileID: 11400000, guid: {fontAssetGuid}, type: 2}}",
            RegexOptions.CultureInvariant);
        if (updated == content)
        {
            throw new InvalidOperationException("TMP 默认字体引用没有找到或没有发生变化。");
        }

        File.WriteAllText(settingsPath, updated, new UTF8Encoding(false));
    }

    private static IEnumerable<string> GetSerializedAssetPaths()
    {
        return AssetDatabase.GetAllAssetPaths()
            .Where(path => path.StartsWith("Assets/", StringComparison.Ordinal))
            .Where(path => !path.StartsWith("Assets/Resources/Front/", StringComparison.Ordinal))
            .Where(path =>
                path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase));
    }

    private static string[] FindLegacyReferences()
    {
        var references = new List<string>();
        foreach (string assetPath in GetSerializedAssetPaths())
        {
            string content = File.ReadAllText(assetPath);
            foreach (string guid in LegacyGuids)
            {
                if (content.Contains(guid, StringComparison.Ordinal))
                {
                    references.Add($"{assetPath}: {guid}");
                }
            }
        }

        return references.ToArray();
    }
}
