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
    private const string RequiredCharacterSetPath = "Assets/Editor/FontOptimization/FactoryEscapeRequiredCharacterSet.txt";
    private const string SourceFontPath = "Assets/Resources/Front/ChineseSubset/FactoryEscapeChineseSubset.otf";
    private const string LegacySubsetFontAssetPath = "Assets/Resources/Front/ChineseSubset/FactoryEscapeChineseSubset SDF.asset";
    private const string FontAssetPath = "Assets/Resources/Front/ChineseSubset/FactoryEscapeChineseReadable SDF.asset";
    private const string FallbackFontAssetPath = "Assets/Resources/Front/ChineseSubset/FactoryEscapeChineseFallback SDF.asset";
    private const string LegacySubsetFontAssetGuid = "fca67701ba9a2794999ccd875ebb6d3d";
    private const long LegacySubsetMaterialLocalId = 4357687372513342661;

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
        ZhanKuFontGuid,
        LegacySubsetFontAssetGuid
    };

    static FactoryEscapeChineseFontOptimizer()
    {
        EditorApplication.delayCall += RunPendingRequest;
    }

    [MenuItem("Tools/Factory Escape/生成高清中文字体")]
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

        TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        TMP_FontAsset fallbackFontAsset =
            AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FallbackFontAssetPath);
        if ((fontAsset == null) != (fallbackFontAsset == null))
        {
            throw new InvalidOperationException("高清主字体与后备字体不完整，无法安全继续。");
        }

        AssetDatabase.ImportAsset(CharacterSetPath, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(RequiredCharacterSetPath, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(SourceFontPath, ImportAssetOptions.ForceSynchronousImport);

        TextAsset characterSetAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(CharacterSetPath);
        TextAsset requiredCharacterSetAsset =
            AssetDatabase.LoadAssetAtPath<TextAsset>(RequiredCharacterSetPath);
        Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
        if (characterSetAsset == null || requiredCharacterSetAsset == null || sourceFont == null)
        {
            throw new InvalidOperationException("字符表或子集字体尚未导入 Unity。请等待资源导入完成后重试。");
        }

        string allCharacters = characterSetAsset.text;
        string requiredCharacters = requiredCharacterSetAsset.text;
        if (fontAsset == null)
        {
            fallbackFontAsset = CreateFontAsset(
                sourceFont,
                FallbackFontAssetPath,
                "FactoryEscapeChineseFallback",
                GlyphRenderMode.SDFAA_HINTED,
                AtlasPopulationMode.Dynamic);
            fontAsset = CreateFontAsset(
                sourceFont,
                FontAssetPath,
                "FactoryEscapeChineseReadable",
                GlyphRenderMode.SDF32,
                AtlasPopulationMode.Dynamic);
            fontAsset.fallbackFontAssetTable = new List<TMP_FontAsset> { fallbackFontAsset };

            if (!fontAsset.TryAddCharacters(
                    requiredCharacters,
                    out string missingCharacters,
                    true) ||
                !string.IsNullOrEmpty(missingCharacters))
            {
                throw new InvalidOperationException(
                    $"高清主字体缺少 {missingCharacters?.Length ?? 0} 个字符：{missingCharacters}");
            }

            fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;
        }

        ConfigureReadableMaterial(fontAsset);
        ConfigureReadableMaterial(fallbackFontAsset);
        EditorUtility.SetDirty(fontAsset);
        EditorUtility.SetDirty(fallbackFontAsset);
        AssetDatabase.SaveAssets();

        string fontAssetGuid = AssetDatabase.AssetPathToGUID(FontAssetPath);
        string sourceFontGuid = AssetDatabase.AssetPathToGUID(SourceFontPath);
        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                fontAsset.material,
                out string materialGuid,
                out long materialLocalId) ||
            materialGuid != fontAssetGuid)
        {
            throw new InvalidOperationException("无法读取高清字体材质的本地文件 ID。");
        }

        int updatedFiles = ReplaceSerializedReferences(
            fontAssetGuid,
            sourceFontGuid,
            materialLocalId);
        SetDefaultTmpFont(fontAssetGuid);
        if (AssetDatabase.LoadMainAssetAtPath(LegacySubsetFontAssetPath) != null &&
            !AssetDatabase.DeleteAsset(LegacySubsetFontAssetPath))
        {
            throw new InvalidOperationException($"无法删除旧的低精度字体：{LegacySubsetFontAssetPath}");
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        TMP_FontAsset reloadedFontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        TMP_FontAsset reloadedFallbackFontAsset =
            AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FallbackFontAssetPath);
        var missingAfterReload = new List<char>();
        if (reloadedFontAsset == null ||
            !reloadedFontAsset.HasCharacters(requiredCharacters, out missingAfterReload) ||
            missingAfterReload.Count > 0)
        {
            throw new InvalidOperationException(
                $"高清主字体重新导入后缺少 {missingAfterReload?.Count ?? 0} 个字符。");
        }

        if (reloadedFallbackFontAsset == null ||
            reloadedFallbackFontAsset.atlasPopulationMode != AtlasPopulationMode.Dynamic ||
            reloadedFallbackFontAsset.sourceFontFile == null)
        {
            throw new InvalidOperationException("GB2312 动态后备字体配置无效。");
        }

        string[] remainingReferences = FindLegacyReferences();
        if (remainingReferences.Length > 0)
        {
            throw new InvalidOperationException(
                "仍存在旧字体引用：\n" + string.Join("\n", remainingReferences));
        }

        int atlasCount = reloadedFontAsset.atlasTextures.Count(texture => texture != null);
        string result =
            $"requiredCharacters={requiredCharacters.Length}\n" +
            $"fallbackCharacters={allCharacters.Length}\n" +
            $"atlasTextures={atlasCount}\n" +
            $"updatedFiles={updatedFiles}\n" +
            $"fontAsset={FontAssetPath}\n" +
            $"fontAssetGuid={fontAssetGuid}\n" +
            $"materialLocalId={materialLocalId}";
        Debug.Log("高清中文字体生成完成：\n" + result);
        return result;
    }

    private static TMP_FontAsset CreateFontAsset(
        Font sourceFont,
        string assetPath,
        string assetName,
        GlyphRenderMode renderMode,
        AtlasPopulationMode populationMode)
    {
        TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
            sourceFont,
            90,
            9,
            renderMode,
            2048,
            2048,
            populationMode,
            true);
        if (fontAsset == null)
        {
            throw new InvalidOperationException($"TMP 字体资产创建失败：{assetPath}");
        }

        fontAsset.name = assetName + " SDF";
        fontAsset.atlasTextures[0].name = assetName + " Atlas";
        fontAsset.material.name = assetName + " Material";
        AssetDatabase.CreateAsset(fontAsset, assetPath);
        AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);
        AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
        return fontAsset;
    }

    private static void ConfigureReadableMaterial(TMP_FontAsset fontAsset)
    {
        Shader shader = Shader.Find("TextMeshPro/Mobile/Distance Field");
        if (shader == null)
        {
            throw new InvalidOperationException("找不到 TextMeshPro/Mobile/Distance Field shader。");
        }

        Material material = fontAsset.material;
        material.shader = shader;
        material.SetTexture(ShaderUtilities.ID_MainTex, fontAsset.atlasTextures[0]);
        material.SetColor(ShaderUtilities.ID_FaceColor, Color.white);
        material.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
        material.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.12f);
        material.SetFloat(ShaderUtilities.ID_OutlineSoftness, 0f);
        EditorUtility.SetDirty(material);
    }

    private static int ReplaceSerializedReferences(
        string fontAssetGuid,
        string sourceFontGuid,
        long materialLocalId)
    {
        int updatedFiles = 0;
        string oldMaterialReference =
            $"fileID: {SourceHanMaterialLocalId}, guid: {SourceHanFontAssetGuid}";
        string legacySubsetMaterialReference =
            $"fileID: {LegacySubsetMaterialLocalId}, guid: {LegacySubsetFontAssetGuid}";
        string newMaterialReference =
            $"fileID: {materialLocalId}, guid: {fontAssetGuid}";

        foreach (string assetPath in GetSerializedAssetPaths())
        {
            string content = File.ReadAllText(assetPath);
            string updated = content.Replace(oldMaterialReference, newMaterialReference);
            updated = updated.Replace(legacySubsetMaterialReference, newMaterialReference);
            updated = updated.Replace(SourceHanFontAssetGuid, fontAssetGuid);
            updated = updated.Replace(SimYouFontAssetGuid, fontAssetGuid);
            updated = updated.Replace(AaFontAssetGuid, fontAssetGuid);
            updated = updated.Replace(LegacySubsetFontAssetGuid, fontAssetGuid);
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
        string expectedReference =
            $"  m_defaultFontAsset: {{fileID: 11400000, guid: {fontAssetGuid}, type: 2}}";
        if (content.Contains(expectedReference, StringComparison.Ordinal))
        {
            return;
        }

        string updated = Regex.Replace(
            content,
            @"(?m)^  m_defaultFontAsset: .*?$",
            expectedReference,
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
