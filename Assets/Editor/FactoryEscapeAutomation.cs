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
    public static void BuildAndroid()
    {
        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();
        const string outputPath = "Builds/Android/FactoryEscape.apk";
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.Android,
            options = BuildOptions.None
        });

        Debug.Log($"Android build: {report.summary.result}; scenes={scenes.Length}; " +
                  $"bytes={report.summary.totalSize}; output={outputPath}");
        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new InvalidOperationException($"Android build failed: {report.summary.result}");
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
