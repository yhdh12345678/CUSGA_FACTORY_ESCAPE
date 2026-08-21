#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed class DarkRoomAccessibilityPreviewBridge : MonoBehaviour
{
    private const float CommandPollInterval = 0.05f;
    private const float StateWriteInterval = 0.25f;

    private string previewDirectory;
    private DarkRoomApp app;
    private float nextCommandPoll;
    private float nextStateWrite;
    private long revision;
    private string status = string.Empty;
    private string focusKey = string.Empty;
    private string lastSignature = string.Empty;
    private bool stateDirty = true;
    private bool previousRunInBackground;

    private void Awake()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        previewDirectory = Path.Combine(projectRoot, "Temp", "AccessibilityPreview");
        previousRunInBackground = Application.runInBackground;
        Application.runInBackground = true;
    }

    private void Update()
    {
        if (!File.Exists(Path.Combine(previewDirectory, "enabled")))
        {
            Destroy(this);
            return;
        }

        AttachToApp();
        float now = Time.unscaledTime;
        if (now >= nextCommandPoll)
        {
            nextCommandPoll = now + CommandPollInterval;
            ProcessCommands();
        }
        if (now >= nextStateWrite)
        {
            nextStateWrite = now + StateWriteInterval;
            WriteStateIfChanged();
        }
    }

    private void AttachToApp()
    {
        if (app == DarkRoomApp.Instance) return;
        if (app != null) app.AccessibilityRefreshRequested -= OnAccessibilityRefreshRequested;
        app = DarkRoomApp.Instance;
        if (app != null)
        {
            app.AccessibilityRefreshRequested += OnAccessibilityRefreshRequested;
            stateDirty = true;
        }
    }

    private void ProcessCommands()
    {
        if (!Directory.Exists(previewDirectory)) return;
        foreach (string commandPath in Directory.GetFiles(previewDirectory, "command-*.json")
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            try
            {
                PreviewCommand command = JsonUtility.FromJson<PreviewCommand>(File.ReadAllText(commandPath));
                if (command != null)
                {
                    Execute(command);
                    WriteStateIfChanged();
                }
                File.Delete(commandPath);
            }
            catch (IOException)
            {
            }
            catch (Exception exception)
            {
                status = $"预览指令失败：{exception.Message}";
                stateDirty = true;
                TryDelete(commandPath);
            }
        }
    }

    private void Execute(PreviewCommand command)
    {
        if (app == null)
        {
            status = "暗黑房间尚未就绪。";
            stateDirty = true;
            return;
        }

        switch (command.action)
        {
            case "activate":
                focusKey = command.key ?? string.Empty;
                if (!app.Activate(focusKey))
                {
                    status = "当前项目不可激活。";
                    stateDirty = true;
                }
                break;
            case "back":
                if (!app.HandleKeyboardCommand(KeyCode.Escape))
                {
                    status = "当前页面没有返回操作。";
                    stateDirty = true;
                }
                break;
            case "refresh":
                app.HandleKeyboardCommand(KeyCode.F5);
                break;
            case "set-text":
                status = "当前页面没有可编辑文本。";
                stateDirty = true;
                break;
            case "toggle-panel":
            case "gesture-start":
            case "gesture-end":
            case "gesture-double-tap":
                status = "当前页面没有手势面板。";
                stateDirty = true;
                break;
        }
    }

    private void OnAccessibilityRefreshRequested(string requestedFocusKey, string message, bool screenChanged)
    {
        if (!string.IsNullOrWhiteSpace(requestedFocusKey)) focusKey = requestedFocusKey;
        if (!string.IsNullOrWhiteSpace(message)) status = message;
        stateDirty = true;
    }

    private void WriteStateIfChanged()
    {
        if (app == null) return;
        PreviewState state = BuildState();
        string signature = JsonUtility.ToJson(state);
        if (!stateDirty && signature == lastSignature) return;
        state.revision = ++revision;
        WriteAtomically(Path.Combine(previewDirectory, "state.json"), JsonUtility.ToJson(state, true));
        lastSignature = signature;
        stateDirty = false;
    }

    private PreviewState BuildState()
    {
        IReadOnlyList<DarkRoomAccessibleItem> accessibleItems = app.GetAccessibleItems();
        string pageLabel = accessibleItems.FirstOrDefault(item => item.IsHeader)?.Label ??
                           app.CurrentPage.ToString();
        return new PreviewState
        {
            projectLabel = "暗黑房间",
            pageLabel = pageLabel,
            focusKey = ResolveFocusKey(accessibleItems),
            status = status,
            interactionMode = "Keyboard",
            gesturePanelAvailable = false,
            items = accessibleItems.Select(ToPreviewItem).ToArray()
        };
    }

    private string ResolveFocusKey(IReadOnlyList<DarkRoomAccessibleItem> items)
    {
        if (!string.IsNullOrWhiteSpace(focusKey) && items.Any(item => item.Key == focusKey))
        {
            return focusKey;
        }
        return items.FirstOrDefault(item => item.IsButton && !item.IsDisabled)?.Key ??
               items.FirstOrDefault()?.Key ?? string.Empty;
    }

    private static PreviewItem ToPreviewItem(DarkRoomAccessibleItem item)
    {
        return new PreviewItem
        {
            key = item.Key ?? string.Empty,
            label = item.Label ?? string.Empty,
            value = item.Value ?? string.Empty,
            role = item.IsHeader ? "Header" : item.IsButton ? "Button" : "Text",
            state = item.IsDisabled ? "Disabled" : "None",
            actionable = item.IsButton && !item.IsDisabled,
            editable = false
        };
    }

    private void OnDestroy()
    {
        if (app != null) app.AccessibilityRefreshRequested -= OnAccessibilityRefreshRequested;
        Application.runInBackground = previousRunInBackground;
    }

    private static void WriteAtomically(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, content);
        if (File.Exists(path)) File.Replace(temporaryPath, path, null);
        else File.Move(temporaryPath, path);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    [Serializable]
    private sealed class PreviewCommand
    {
        public long id;
        public string action;
        public string key;
        public string value;
    }

    [Serializable]
    private sealed class PreviewState
    {
        public long revision;
        public string projectLabel;
        public string pageLabel;
        public string focusKey;
        public string status;
        public string interactionMode;
        public bool gesturePanelAvailable;
        public PreviewItem[] items;
    }

    [Serializable]
    private sealed class PreviewItem
    {
        public string key;
        public string label;
        public string value;
        public string role;
        public string state;
        public bool actionable;
        public bool editable;
    }
}
#endif
