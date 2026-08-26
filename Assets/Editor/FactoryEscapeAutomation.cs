using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class FactoryEscapeAutomation
{
    private const string ReadableFontAssetPath =
        "Assets/Resources/Front/ChineseSubset/FactoryEscapeChineseFallback SDF.asset";
    private static readonly Color ReadableTextColor = new Color32(242, 255, 255, 255);

    public static void BuildAndroid()
    {
        BuildAndroidPlayer("Builds/Android/FactoryEscape.apk", AndroidArchitecture.ARM64);
    }

    public static void BuildAndroidEmulator()
    {
        BuildAndroidPlayer("Builds/Android/FactoryEscape-Emulator.apk", AndroidArchitecture.X86_64);
    }

    private static void BuildAndroidPlayer(string outputPath, AndroidArchitecture architecture)
    {
        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

        AndroidArchitecture previousArchitecture = PlayerSettings.Android.targetArchitectures;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.Android.targetArchitectures = architecture;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);

        BuildReport report;
        try
        {
            report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            });
        }
        finally
        {
            PlayerSettings.Android.targetArchitectures = previousArchitecture;
        }

        Debug.Log($"Android build: {report.summary.result}; scenes={scenes.Length}; " +
                  $"bytes={report.summary.totalSize}; output={outputPath}");
        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new InvalidOperationException($"Android build failed: {report.summary.result}");
        }
    }

    public static void CaptureReadabilityFrame()
    {
        TMP_FontAsset font = SystemChineseFontProvider.CurrentFont;
        if (font == null || font.atlasPopulationMode == AtlasPopulationMode.Static)
        {
            throw new InvalidOperationException("系统中文字体或动态后备字体不可用。");
        }

        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
        Camera camera = UnityEngine.Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate.gameObject.scene == scene && candidate.enabled);
        if (camera == null)
        {
            throw new InvalidOperationException("主菜单没有可用于清晰度截图的相机。");
        }

        Canvas[] canvases = UnityEngine.Object.FindObjectsByType<Canvas>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None)
            .Where(canvas => canvas.gameObject.scene == scene &&
                             canvas.renderMode != RenderMode.WorldSpace)
            .ToArray();
        var canvasStates = canvases.Select(canvas => new
        {
            canvas,
            canvas.renderMode,
            canvas.worldCamera,
            canvas.planeDistance
        }).ToArray();

        const int width = 1920;
        const int height = 1080;
        string outputDirectory = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName,
            "Logs",
            "Readability");
        string outputPath = Path.Combine(outputDirectory, "MainMenu-MobileSDF.png");
        Directory.CreateDirectory(outputDirectory);

        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        var frame = new Texture2D(width, height, TextureFormat.RGB24, false);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;
        try
        {
            foreach (Canvas canvas in canvases)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = Mathf.Max(camera.nearClipPlane + 0.1f, 1f);
            }

            camera.targetTexture = target;
            Canvas.ForceUpdateCanvases();
            foreach (TMP_Text text in UnityEngine.Object.FindObjectsByType<TMP_Text>(
                         FindObjectsInactive.Exclude,
                         FindObjectsSortMode.None))
            {
                if (text.gameObject.scene == scene)
                {
                    text.ForceMeshUpdate(true);
                }
            }

            camera.Render();
            RenderTexture.active = target;
            frame.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            frame.Apply();

            int brightPixels = frame.GetPixels32().Count(pixel =>
                pixel.r >= 180 && pixel.g >= 180 && pixel.b >= 180);
            if (brightPixels < 1000)
            {
                throw new InvalidOperationException(
                    $"清晰度截图没有捕获到足够的可见文字像素：{brightPixels}");
            }

            File.WriteAllBytes(outputPath, frame.EncodeToPNG());
            Debug.Log($"Readability frame captured: brightPixels={brightPixels}; output={outputPath}");
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            foreach (var state in canvasStates)
            {
                state.canvas.renderMode = state.renderMode;
                state.canvas.worldCamera = state.worldCamera;
                state.canvas.planeDistance = state.planeDistance;
            }

            UnityEngine.Object.DestroyImmediate(frame);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    public static void AuditAccessibility()
    {
        var lines = new List<string>();
        foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes.Where(scene => scene.enabled))
        {
            Scene scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Single);
            lines.Add($"SCENE {scene.path}");
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                AddInteractiveObjects(root.transform, lines);
            }
        }

        lines.Add("PREFABS");
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            int before = lines.Count;
            AddInteractiveObjects(prefab.transform, lines);
            if (lines.Count > before)
            {
                lines.Insert(before, $"PREFAB {path}");
            }
        }

        Directory.CreateDirectory("Logs");
        File.WriteAllLines("Logs/AccessibilityAudit.txt", lines);
        Debug.Log($"Accessibility audit: entries={lines.Count}; output=Logs/AccessibilityAudit.txt");
    }

    public static void ApplyReadableAndroidPresentation()
    {
        TMP_FontAsset readableFont =
            AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ReadableFontAssetPath);
        if (readableFont == null)
        {
            throw new InvalidOperationException($"找不到高清中文字体：{ReadableFontAssetPath}");
        }

        int updatedScenes = 0;
        int updatedPrefabs = 0;
        int updatedTexts = 0;
        int updatedCanvases = 0;
        foreach (EditorBuildSettingsScene buildScene in
                 EditorBuildSettings.scenes.Where(scene => scene.enabled))
        {
            Scene scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Single);
            bool changed = false;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                changed |= ApplyReadableSettings(
                    root,
                    readableFont,
                    ref updatedTexts,
                    ref updatedCanvases);
            }

            if (changed)
            {
                EditorSceneManager.SaveScene(scene);
                updatedScenes++;
            }
        }

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (!ApplyReadableSettings(
                        root,
                        readableFont,
                        ref updatedTexts,
                        ref updatedCanvases))
                {
                    continue;
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
                updatedPrefabs++;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log(
            $"Readable Android presentation applied: scenes={updatedScenes}; " +
            $"prefabs={updatedPrefabs}; texts={updatedTexts}; canvases={updatedCanvases}");
    }

    private static bool ApplyReadableSettings(
        GameObject root,
        TMP_FontAsset readableFont,
        ref int updatedTexts,
        ref int updatedCanvases)
    {
        bool changed = false;
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            bool textChanged = false;
            if (text.font != readableFont)
            {
                text.font = readableFont;
                textChanged = true;
            }

            if (text.fontSharedMaterial != readableFont.material)
            {
                text.fontSharedMaterial = readableFont.material;
                textChanged = true;
            }

            if (text.color.a > 0.001f)
            {
                Color targetColor = ReadableTextColor;
                targetColor.a = text.color.a;
                if (text.color != targetColor)
                {
                    text.color = targetColor;
                    textChanged = true;
                }
            }

            if (text is TextMeshProUGUI)
            {
                if (text.fontSize < 32f)
                {
                    text.fontSize = 32f;
                    textChanged = true;
                }

                if (text.enableAutoSizing && text.fontSizeMin < 28f)
                {
                    text.fontSizeMin = 28f;
                    textChanged = true;
                }

                if (text.enableAutoSizing && text.fontSizeMax < 32f)
                {
                    text.fontSizeMax = 32f;
                    textChanged = true;
                }
            }

            if (!textChanged)
            {
                continue;
            }

            EditorUtility.SetDirty(text);
            updatedTexts++;
            changed = true;
        }

        foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
        {
            if (canvas.renderMode == RenderMode.WorldSpace)
            {
                continue;
            }

            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            bool canvasChanged = !canvas.pixelPerfect;
            canvas.pixelPerfect = true;
            if (scaler != null)
            {
                canvasChanged |= scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize ||
                                 scaler.referenceResolution != new Vector2(1080f, 2400f) ||
                                 scaler.screenMatchMode != CanvasScaler.ScreenMatchMode.MatchWidthOrHeight ||
                                 !Mathf.Approximately(scaler.matchWidthOrHeight, 0f);
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080f, 2400f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0f;
                EditorUtility.SetDirty(scaler);
            }

            if (!canvasChanged)
            {
                continue;
            }

            EditorUtility.SetDirty(canvas);
            updatedCanvases++;
            changed = true;
        }

        return changed;
    }

    private static void AddInteractiveObjects(Transform root, ICollection<string> lines)
    {
        foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
        {
            Component[] components = transform.GetComponents<Component>();
            string[] interactions = components
                .Where(component => component is Selectable ||
                                    component is IPointerClickHandler ||
                                    component is IPointerDownHandler ||
                                    component is IPointerUpHandler ||
                                    component is IBeginDragHandler ||
                                    component is IDragHandler ||
                                    component is IEndDragHandler ||
                                    component is IScrollHandler)
                .Select(component => component.GetType().Name)
                .Distinct()
                .ToArray();
            if (interactions.Length == 0)
            {
                continue;
            }

            string label = transform.GetComponentInChildren<TMP_Text>(true)?.text ?? string.Empty;
            lines.Add($"{GetPath(transform)} | active={transform.gameObject.activeInHierarchy} | " +
                      $"label={label.Replace('\n', ' ')} | interactions={string.Join(",", interactions)}");
        }
    }

    private static string GetPath(Transform transform)
    {
        var names = new Stack<string>();
        for (Transform current = transform; current != null; current = current.parent)
        {
            names.Push(current.name);
        }

        return string.Join("/", names);
    }
}
