using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

public sealed class SystemChineseFontProvider : MonoBehaviour
{
    private const string EmbeddedFallbackPath =
        "Front/ChineseSubset/FactoryEscapeChineseFallback SDF";
    private const string RequiredProbe = "抓住未尽的余晖小明车间螺丝刀继续游戏存档退出";

    private static readonly string[] PreferredFontPathTokens =
    {
        "NotoSansCJK",
        "NotoSansSC",
        "DroidSansFallback",
        "SourceHanSans",
        "msyh",
        "simhei"
    };

    private static SystemChineseFontProvider instance;
    private static TMP_FontAsset currentFont;
    private static TMP_FontAsset embeddedFallback;
    private float nextRefreshTime;

    public static TMP_FontAsset CurrentFont => GetOrCreateFont();
    public static bool UsingSystemFont { get; private set; }
    public static string SelectedFamilyName { get; private set; } = string.Empty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeBeforeSceneLoad()
    {
        if (instance != null)
        {
            return;
        }

        var host = new GameObject(nameof(SystemChineseFontProvider));
        DontDestroyOnLoad(host);
        instance = host.AddComponent<SystemChineseFontProvider>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        TMP_Settings.defaultFontAsset = GetOrCreateFont();
        SceneManager.sceneLoaded += OnSceneLoaded;
        ApplyToLoadedText();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            instance = null;
        }
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime)
        {
            return;
        }

        nextRefreshTime = Time.unscaledTime + 1f;
        ApplyToLoadedText();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyToLoadedText();
    }

    public static TMP_FontAsset GetOrCreateFont()
    {
        if (currentFont != null)
        {
            return currentFont;
        }

        embeddedFallback = Resources.Load<TMP_FontAsset>(EmbeddedFallbackPath);
        foreach (string fontPath in GetCandidateFontPaths())
        {
            foreach (int faceIndex in GetPreferredFaceIndices(fontPath))
            {
                TMP_FontAsset systemAsset = TMP_FontAsset.CreateFontAsset(
                    fontPath,
                    faceIndex,
                    90,
                    9,
                    GlyphRenderMode.SDFAA,
                    1024,
                    1024);
                if (systemAsset == null ||
                    !systemAsset.TryAddCharacters(RequiredProbe, out string missing) ||
                    !string.IsNullOrEmpty(missing))
                {
                    if (systemAsset != null)
                    {
                        Destroy(systemAsset);
                    }
                    continue;
                }

                currentFont = systemAsset;
                currentFont.name = "Factory Escape System Chinese";
                if (embeddedFallback != null)
                {
                    currentFont.fallbackFontAssetTable = new List<TMP_FontAsset>
                    {
                        embeddedFallback
                    };
                }
                ConfigureReadableMaterial(currentFont);
                UsingSystemFont = true;
                SelectedFamilyName = currentFont.faceInfo.familyName;
                Debug.Log($"System Chinese font enabled: {SelectedFamilyName}");
                return currentFont;
            }
        }

        currentFont = embeddedFallback;
        UsingSystemFont = false;
        SelectedFamilyName = currentFont != null ? currentFont.faceInfo.familyName : string.Empty;
        if (currentFont != null)
        {
            ConfigureReadableMaterial(currentFont);
            Debug.LogWarning("No compatible OS Chinese font was found; using embedded fallback.");
        }
        else
        {
            Debug.LogError("No compatible OS Chinese font or embedded fallback is available.");
        }
        return currentFont;
    }

    public static void ApplyToLoadedText()
    {
        TMP_FontAsset font = GetOrCreateFont();
        if (font == null)
        {
            return;
        }

        foreach (TMP_Text text in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if (text == null || !text.gameObject.scene.IsValid() ||
                !text.gameObject.scene.isLoaded || text.font == font)
            {
                continue;
            }

            text.font = font;
            text.fontSharedMaterial = font.material;
            text.SetAllDirty();
        }
    }

    private static IEnumerable<string> GetCandidateFontPaths()
    {
        return (Font.GetPathsToOSFonts() ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(GetFontPathPriority)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static int GetFontPathPriority(string path)
    {
        for (int index = 0; index < PreferredFontPathTokens.Length; index++)
        {
            if (path.IndexOf(
                    PreferredFontPathTokens[index],
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return PreferredFontPathTokens.Length - index;
            }
        }

        return 0;
    }

    private static IEnumerable<int> GetPreferredFaceIndices(string path)
    {
        return path.IndexOf("NotoSansCJK", StringComparison.OrdinalIgnoreCase) >= 0
            ? new[] { 2, 0, 1, 3, 4 }
            : new[] { 0 };
    }

    private static void ConfigureReadableMaterial(TMP_FontAsset font)
    {
        if (font.material == null)
        {
            return;
        }

        font.material.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.12f);
        font.material.SetFloat(ShaderUtilities.ID_OutlineSoftness, 0f);
        font.material.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
    }
}
