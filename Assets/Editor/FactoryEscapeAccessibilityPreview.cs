using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Accessibility;

[InitializeOnLoad]
public static class FactoryEscapeAccessibilityPreview
{
    private const string StartScenePath = "Assets/Scenes/LoadScene.unity";
    private static double nextCheckTime;
    private static double previewEnabledAt;
    private static bool previewWasEnabled;
    private static bool launchAttempted;

    static FactoryEscapeAccessibilityPreview()
    {
        EditorApplication.update += WatchPreviewMarker;
    }

    [MenuItem("Tools/无障碍/启动流程预览器")]
    public static void StartPreview()
    {
        Directory.CreateDirectory(GetPreviewDirectory());
        File.WriteAllText(GetPreviewPath("enabled"), "1");
        launchAttempted = false;
        WatchPreviewMarker();
    }

    [MenuItem("Tools/无障碍/停止流程预览器")]
    public static void StopPreview()
    {
        string markerPath = GetPreviewPath("enabled");
        if (File.Exists(markerPath))
        {
            File.Delete(markerPath);
        }

        StopPlayModeIfNeeded();
    }

    private static void WatchPreviewMarker()
    {
        if (EditorApplication.timeSinceStartup < nextCheckTime)
        {
            return;
        }

        nextCheckTime = EditorApplication.timeSinceStartup + 0.25d;
        bool enabled = File.Exists(GetPreviewPath("enabled"));
        if (!enabled)
        {
            if (previewWasEnabled)
            {
                StopPlayModeIfNeeded();
            }

            previewWasEnabled = false;
            launchAttempted = false;
            return;
        }

        if (!previewWasEnabled)
        {
            previewEnabledAt = EditorApplication.timeSinceStartup;
        }
        previewWasEnabled = true;
        AssistiveSupport.screenReaderStatusOverride =
            AssistiveSupport.ScreenReaderStatusOverride.ForceEnabled;

        if (!launchAttempted && EditorApplication.timeSinceStartup - previewEnabledAt >= 1d)
        {
            launchAttempted = true;
            LaunchPreviewer();
        }

        if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode &&
            !EditorApplication.isCompiling)
        {
            EditorSceneManager.playModeStartScene =
                AssetDatabase.LoadAssetAtPath<SceneAsset>(StartScenePath);
            EditorApplication.EnterPlaymode();
        }
    }

    private static void StopPlayModeIfNeeded()
    {
        AssistiveSupport.screenReaderStatusOverride =
            AssistiveSupport.ScreenReaderStatusOverride.OSDriven;
        if (EditorApplication.isPlaying)
        {
            EditorApplication.ExitPlaymode();
        }
    }

    private static void LaunchPreviewer()
    {
        if (File.Exists(GetPreviewPath("unified-editor")))
        {
            return;
        }

        string assemblyPath = Path.Combine(
            GetProjectRoot(), "Tools", "AccessibilityPreviewer", "bin", "Release",
            "net8.0-windows", "AccessibilityPreviewer.dll");
        if (!File.Exists(assemblyPath))
        {
            UnityEngine.Debug.LogError(
                "无障碍流程预览器尚未构建。请运行项目根目录的“启动无障碍预览器.cmd”。");
            return;
        }

        string clientPidPath = GetPreviewPath("client.pid");
        if (File.Exists(clientPidPath) && int.TryParse(File.ReadAllText(clientPidPath), out int clientPid))
        {
            try
            {
                Process.GetProcessById(clientPid);
                return;
            }
            catch (ArgumentException)
            {
                File.Delete(clientPidPath);
            }
        }

        string dotnetPath = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles),
            "dotnet", "dotnet.exe");
        Process.Start(new ProcessStartInfo
        {
            FileName = dotnetPath,
            Arguments = $"\"{assemblyPath}\" --project-root \"{GetProjectRoot()}\"",
            UseShellExecute = true,
            WorkingDirectory = GetProjectRoot()
        });
    }

    private static string GetPreviewDirectory()
    {
        return Path.Combine(GetProjectRoot(), "Temp", "AccessibilityPreview");
    }

    private static string GetPreviewPath(string fileName)
    {
        return Path.Combine(GetPreviewDirectory(), fileName);
    }

    private static string GetProjectRoot()
    {
        return Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
    }
}
