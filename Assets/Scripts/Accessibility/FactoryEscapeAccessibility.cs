using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Accessibility;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class FactoryEscapeAccessibility : MonoBehaviour
{
    private enum MainMenuPage
    {
        Root,
        Options,
        MusicVolume,
        SfxVolume
    }

    private sealed class Item
    {
        public string key;
        public string label;
        public string value;
        public AccessibilityRole role;
        public AccessibilityState state;
        public Func<bool> activate;
        public Action<string> setValue;
    }

    private static FactoryEscapeAccessibility instance;
    private readonly AccessibilityHierarchy hierarchy = new AccessibilityHierarchy();
    private string currentSignature = string.Empty;
    private string lastNodeContext = string.Empty;
    private string pendingFocusKey = string.Empty;
    private string modalReturnFocusKey = string.Empty;
    private MainMenuPage mainMenuPage;
    private float nextRefreshTime;
    private bool announceNextRefresh;

#if UNITY_EDITOR
    [Serializable]
    private sealed class PreviewState
    {
        public long revision;
        public string focusKey;
        public string status;
        public List<PreviewItem> items;
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

    [Serializable]
    private sealed class PreviewCommand
    {
        public long id;
        public string action;
        public string key;
        public string value;
    }

    private long previewRevision;
    private float nextPreviewCommandPollTime;
    private string previewStatus = "预览已连接";
    private bool previewConfiguredRunInBackground;
    private bool previewPreviousRunInBackground;
    private bool previewConfiguredAudioVolume;
    private float previewPreviousAudioVolume;
    private bool previewConfiguredScreenReader;
    private AssistiveSupport.ScreenReaderStatusOverride previewPreviousScreenReader;
#endif

    public static bool ReduceMotion => AssistiveSupport.isScreenReaderEnabled;

    public static void RefreshScreen(string focusKey = "")
    {
        if (instance == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(focusKey))
        {
            instance.pendingFocusKey = focusKey;
        }
        instance.QueueRefresh();
    }

    public static void UpdateAIStatus(bool structureChanged)
    {
        if (instance == null)
        {
            return;
        }

        if (structureChanged)
        {
            RefreshScreen("ai-dialog");
            return;
        }

        List<Item> items = instance.CollectItems();
        Item status = items.FirstOrDefault(item => item.key == "ai-status");
        AccessibilityNode statusNode = instance.hierarchy.rootNodes
            .FirstOrDefault(node => node.label.StartsWith("焦虑水平", StringComparison.Ordinal));
        if (status == null || statusNode == null)
        {
            RefreshScreen("ai-dialog");
            return;
        }

        statusNode.label = status.label;
        statusNode.value = status.value;
        statusNode.state = status.state;
        if (instance.announceNextRefresh)
        {
            return;
        }

        instance.currentSignature = BuildSignature(items);
#if UNITY_EDITOR
        instance.WritePreviewState(items, string.Empty, instance.previewStatus);
#endif
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (instance != null)
        {
            return;
        }

        var manager = new GameObject(nameof(FactoryEscapeAccessibility));
        instance = manager.AddComponent<FactoryEscapeAccessibility>();
        DontDestroyOnLoad(manager);
    }

    private void OnEnable()
    {
        instance = this;
#if UNITY_EDITOR
        if (File.Exists(GetPreviewPath("enabled")))
        {
            EnableEditorPreviewOverrides();
        }
#endif
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        AssistiveSupport.screenReaderStatusChanged += OnScreenReaderStatusChanged;
        StartCoroutine(RefreshAfterLayout(true));
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        AssistiveSupport.screenReaderStatusChanged -= OnScreenReaderStatusChanged;
        if (AssistiveSupport.activeHierarchy == hierarchy)
        {
            AssistiveSupport.activeHierarchy = null;
        }

#if UNITY_EDITOR
        DisableEditorPreviewOverrides();

        WritePreviewState(new List<Item>(), string.Empty, "Unity 播放模式已停止");
#endif
    }

    private void Update()
    {
#if UNITY_EDITOR
        UpdateEditorPreview();
#endif

        if (!AssistiveSupport.isScreenReaderEnabled || Time.unscaledTime < nextRefreshTime)
        {
            return;
        }

        nextRefreshTime = Time.unscaledTime + 0.5f;
        List<Item> items = CollectItems();
        string signature = BuildSignature(items);
        if (signature != currentSignature)
        {
            bool notify = announceNextRefresh;
            announceNextRefresh = false;
            Rebuild(items, notify);
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "MainMenu")
        {
            mainMenuPage = MainMenuPage.Root;
        }

        StartCoroutine(RefreshAfterLayout(true));
    }

    private void OnSceneUnloaded(Scene scene)
    {
        StartCoroutine(RefreshAfterLayout(true));
    }

    private void OnScreenReaderStatusChanged(bool enabled)
    {
        if (enabled)
        {
            StartCoroutine(RefreshAfterLayout(true));
        }
        else
        {
            hierarchy.Clear();
            currentSignature = string.Empty;
            AssistiveSupport.activeHierarchy = null;
        }
    }

    private IEnumerator RefreshAfterLayout(bool screenChanged)
    {
        yield return new WaitForEndOfFrame();
        if (AssistiveSupport.isScreenReaderEnabled)
        {
            Rebuild(CollectItems(), screenChanged);
        }
    }

    private void Rebuild(List<Item> items, bool notify)
    {
        AssistiveSupport.activeHierarchy = null;
        hierarchy.Clear();

        float rowHeight = Screen.height / Mathf.Max(1f, items.Count);
        AccessibilityNode focusTarget = null;
        for (int index = 0; index < items.Count; index++)
        {
            Item item = items[index];
            AccessibilityNode node = hierarchy.AddNode(item.label);
            node.value = item.value;
            node.role = item.role;
            node.state = item.state;
            node.frame = new Rect(0, index * rowHeight, Screen.width, rowHeight);
            if (item.activate != null)
            {
                node.invoked += item.activate;
            }

            if (item.key == pendingFocusKey)
            {
                focusTarget = node;
            }
        }

        currentSignature = BuildSignature(items);
        AssistiveSupport.activeHierarchy = hierarchy;
        if (notify && hierarchy.rootNodes.Count > 0)
        {
            AssistiveSupport.notificationDispatcher.SendScreenChanged(focusTarget ?? hierarchy.rootNodes[0]);
        }

#if UNITY_EDITOR
        string previewFocusKey = focusTarget == null ? string.Empty : pendingFocusKey;
        WritePreviewState(items, previewFocusKey, previewStatus);
#endif

        pendingFocusKey = string.Empty;
    }

#if UNITY_EDITOR
    private void UpdateEditorPreview()
    {
        if (!File.Exists(GetPreviewPath("enabled")))
        {
            DisableEditorPreviewOverrides();
            return;
        }

        EnableEditorPreviewOverrides();
        AudioListener.volume = 0f;

        if (AssistiveSupport.screenReaderStatusOverride !=
            AssistiveSupport.ScreenReaderStatusOverride.ForceEnabled)
        {
            AssistiveSupport.screenReaderStatusOverride =
                AssistiveSupport.ScreenReaderStatusOverride.ForceEnabled;
        }

        if (Time.unscaledTime < nextPreviewCommandPollTime)
        {
            return;
        }

        nextPreviewCommandPollTime = Time.unscaledTime + 0.1f;
        string previewDirectory = Path.GetDirectoryName(GetPreviewPath("state.json"));
        string[] commandPaths = Directory.Exists(previewDirectory)
            ? Directory.GetFiles(previewDirectory, "command-*.json")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray()
            : Array.Empty<string>();
        string legacyCommandPath = GetPreviewPath("command.json");
        if (File.Exists(legacyCommandPath))
        {
            commandPaths = new[] { legacyCommandPath }.Concat(commandPaths).ToArray();
        }

        if (commandPaths.Length == 0)
        {
            return;
        }

        foreach (string commandPath in commandPaths)
        {
            try
            {
                PreviewCommand command = JsonUtility.FromJson<PreviewCommand>(
                    File.ReadAllText(commandPath, Encoding.UTF8));
                File.Delete(commandPath);
                ExecutePreviewCommand(command);
            }
            catch (Exception exception)
            {
                previewStatus = $"预览命令失败：{exception.Message}";
                QueueRefresh();
            }
        }
    }

    private void ExecutePreviewCommand(PreviewCommand command)
    {
        List<Item> items = CollectItems();
        Item target = null;
        if (command != null && (command.action == "activate" || command.action == "set-text"))
        {
            target = items.FirstOrDefault(item => item.key == command.key);
        }
        else if (command != null && command.action == "back")
        {
            target = items.LastOrDefault(item => item.activate != null &&
                (item.label.StartsWith("返回", StringComparison.Ordinal) ||
                 item.label.StartsWith("关闭", StringComparison.Ordinal)));
            if (target == null && GetCurrentScreenName() == "暂停菜单")
            {
                target = items.FirstOrDefault(item => item.activate != null &&
                    item.label.Contains("继续", StringComparison.Ordinal));
            }
        }
        else if (command != null && command.action == "refresh")
        {
            previewStatus = "页面已刷新";
            QueueRefresh();
            return;
        }

        if (command != null && command.action == "set-text")
        {
            if (target?.setValue == null)
            {
                previewStatus = "当前项目不可输入";
            }
            else
            {
                target.setValue(command.value ?? string.Empty);
                pendingFocusKey = target.key;
                previewStatus = "输入内容已更新";
            }

            QueueRefresh();
            return;
        }

        if (target?.activate == null)
        {
            previewStatus = command != null && command.action == "back"
                ? "当前页面没有返回操作"
                : "当前项目不可激活";
            QueueRefresh();
            return;
        }

        bool activated = target.activate();
        previewStatus = activated ? $"已激活：{target.label}" : $"激活失败：{target.label}";
        QueueRefresh();
    }

    private void WritePreviewState(List<Item> items, string focusKey, string status)
    {
        if (!File.Exists(GetPreviewPath("enabled")))
        {
            return;
        }

        var state = new PreviewState
        {
            revision = ++previewRevision,
            focusKey = focusKey,
            status = status,
            items = items.Select(item => new PreviewItem
            {
                key = item.key,
                label = item.label,
                value = item.value,
                role = item.role.ToString(),
                state = item.state.ToString(),
                actionable = item.activate != null,
                editable = item.setValue != null
            }).ToList()
        };

        string statePath = GetPreviewPath("state.json");
        WriteTextAtomically(statePath, JsonUtility.ToJson(state, true));
    }

    private static string GetPreviewPath(string fileName)
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
            ?? Application.dataPath;
        return Path.Combine(projectRoot, "Temp", "AccessibilityPreview", fileName);
    }

    private void EnableEditorPreviewOverrides()
    {
        if (!previewConfiguredRunInBackground)
        {
            previewPreviousRunInBackground = Application.runInBackground;
            previewConfiguredRunInBackground = true;
            Application.runInBackground = true;
        }

        if (!previewConfiguredAudioVolume)
        {
            previewPreviousAudioVolume = AudioListener.volume;
            previewConfiguredAudioVolume = true;
            AudioListener.volume = 0f;
        }

        if (!previewConfiguredScreenReader)
        {
            previewPreviousScreenReader = AssistiveSupport.screenReaderStatusOverride;
            previewConfiguredScreenReader = true;
        }
    }

    private void DisableEditorPreviewOverrides()
    {
        if (previewConfiguredRunInBackground)
        {
            Application.runInBackground = previewPreviousRunInBackground;
            previewConfiguredRunInBackground = false;
        }

        if (previewConfiguredAudioVolume)
        {
            AudioListener.volume = previewPreviousAudioVolume;
            previewConfiguredAudioVolume = false;
        }

        if (previewConfiguredScreenReader)
        {
            AssistiveSupport.screenReaderStatusOverride = previewPreviousScreenReader;
            previewConfiguredScreenReader = false;
        }
    }

    private static void WriteTextAtomically(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(temporaryPath, path);
    }
#endif

    private List<Item> CollectItems()
    {
        if (IsSceneLoaded("MainMenu") && !IsSceneLoaded("GameScene"))
        {
            return CollectMainMenuItems();
        }

        var items = new List<Item>();
        UIManager uiManager = FindFirstObjectByType<UIManager>();
        if (!IsSceneLoaded("GameScene"))
        {
            items.Add(new Item
            {
                key = "screen",
                label = GetCurrentScreenName(),
                role = AccessibilityRole.Header
            });
        }

        VideoManager videoManager = FindFirstObjectByType<VideoManager>();
        if (videoManager != null && videoManager.cutSceneUIPanel.gameObject.activeInHierarchy)
        {
            AddStaticText(items, "cutscene-image",
                $"画面描述：{GetCutSceneVisualDescription(videoManager.AccessibilityAnimationState)}");
            List<string> lines = videoManager.GetAccessibilityTextLines();
            for (int index = 0; index < lines.Count; index++)
            {
                AddStaticText(items, $"cutscene-line-{index}", lines[index]);
            }

            AddAction(items, "cutscene-continue", "继续剧情", () =>
            {
                bool advanced = videoManager.AdvancePageForAccessibility();
                QueueRefresh();
                return advanced;
            });
            return items;
        }

        DialogSystem dialogSystem = FindFirstObjectByType<DialogSystem>();
        if (dialogSystem != null && dialogSystem.dialogPanel.activeInHierarchy)
        {
            AddStaticText(items, "dialog-image",
                $"画面描述：{dialogSystem.GetAccessibilityVisualDescription()}");
            List<string> lines = dialogSystem.GetAccessibilityTextLines();
            for (int index = 0; index < lines.Count; index++)
            {
                AddStaticText(items, $"dialog-line-{index}", lines[index]);
            }

            AddAction(items, "dialog-continue", "继续剧情", () =>
            {
                bool advanced = dialogSystem.AdvancePageForAccessibility();
                QueueRefresh();
                return advanced;
            });
            return items;
        }

        if (dialogSystem != null && dialogSystem.AIDialogPanel.gameObject.activeInHierarchy)
        {
            if (uiManager != null && uiManager.AIDialogLog != null &&
                uiManager.AIDialogLog.gameObject.activeInHierarchy)
            {
                TMP_Text[] logLines = uiManager.AIDialogLog.GetComponentsInChildren<TMP_Text>(false)
                    .Where(text => !string.IsNullOrWhiteSpace(text.text))
                    .ToArray();
                for (int index = 0; index < logLines.Length; index++)
                {
                    AddStaticText(items, $"ai-log-{index}", logLines[index].text);
                }

                AddAction(items, "ai-log-close", "关闭对话记录", () =>
                {
                    uiManager.DisplayAndCloseAILog();
                    QueueRefresh();
                    return true;
                });
                return items;
            }

            AddStaticText(items, "ai-speaker", dialogSystem.AINameText.text);
            AddStaticText(items, "ai-dialog", dialogSystem.textFinished
                ? dialogSystem.AIDialogText.text
                : "正在显示回复");
            tongyi_AI ai = tongyi_AI.instance;
            GameManager gameManager = GameManager.Instance;
            AILocked aiResult = FindObjectsByType<AILocked>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .FirstOrDefault(component => component.AccessibilityResultReady);
            Level1AILock level1Result = FindObjectsByType<Level1AILock>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .FirstOrDefault(component => component.AccessibilityResultReady);
            bool resultReady = aiResult != null || level1Result != null;
            if (ai != null && gameManager != null)
            {
                int anxietyPercentage = gameManager.maxAnxiety <= 0f
                    ? 0
                    : Mathf.RoundToInt(gameManager.currentAnxiety / gameManager.maxAnxiety * 100f);
                AddStaticText(items, "ai-status",
                    $"焦虑水平{anxietyPercentage}%，剩余提交{Mathf.Max(0, ai.SubmitTimer)}次");
            }

            if (!resultReady)
            {
                foreach (InputField input in dialogSystem.AIDialogPanel.GetComponentsInChildren<InputField>(false))
                {
                    items.Add(new Item
                    {
                        key = $"input-{input.GetInstanceID()}",
                        label = GetInputLabel(input),
                        value = input.text,
                        role = AccessibilityRole.TextField,
                        activate = () =>
                        {
                            input.ActivateInputField();
                            return true;
                        },
                        setValue = value => input.text = value
                    });
                }
            }

            foreach (Button button in dialogSystem.AIDialogPanel.GetComponentsInChildren<Button>(false))
            {
                if (resultReady && ai != null && button == ai.send_button)
                {
                    continue;
                }

                bool canActivate = button.IsInteractable() &&
                                   (ai == null || button != ai.send_button || ai.CanSend);
                items.Add(new Item
                {
                    key = $"button-{button.GetInstanceID()}",
                    label = GetButtonLabel(button),
                    role = AccessibilityRole.Button,
                    state = canActivate ? AccessibilityState.None : AccessibilityState.Disabled,
                    activate = () =>
                    {
                        if (!canActivate)
                        {
                            return false;
                        }

                        button.onClick.Invoke();
                        QueueRefresh();
                        return true;
                    }
                });
            }

            if (aiResult != null)
            {
                AddAction(items, "ai-result-continue", "继续剧情", () =>
                {
                    bool completed = aiResult.CompleteAccessibilityResult();
                    QueueRefresh();
                    return completed;
                });
            }
            else if (level1Result != null)
            {
                AddAction(items, "ai-result-continue", "继续剧情", () =>
                {
                    bool completed = level1Result.CompleteAccessibilityResult();
                    QueueRefresh();
                    return completed;
                });
            }

            return items;
        }

        if (uiManager != null && uiManager.textNodeUI != null &&
            uiManager.textNodeUI.gameObject.activeInHierarchy)
        {
            AddStaticText(items, "document", uiManager.scrollViewContent == null
                ? string.Empty
                : uiManager.scrollViewContent.GetComponent<TMP_Text>()?.text);
            AddAction(items, "document-close", "关闭文本", () =>
            {
                uiManager.CloseTextNodeUI();
                pendingFocusKey = modalReturnFocusKey;
                QueueRefresh();
                return true;
            });
            return items;
        }

        if (uiManager != null && uiManager.graphNodeUI != null &&
            uiManager.graphNodeUI.gameObject.activeInHierarchy)
        {
            AddStaticText(items, "image-context", string.IsNullOrWhiteSpace(lastNodeContext) ? "图片线索" : lastNodeContext);
            AddAction(items, "image-close", "关闭图片", () =>
            {
                uiManager.CloseGraph();
                pendingFocusKey = modalReturnFocusKey;
                QueueRefresh();
                return true;
            });
            return items;
        }

        Node[] gameNodes = FindObjectsByType<Node>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(node => node.gameObject.activeInHierarchy)
            .OrderBy(node => node.rect.y)
            .ThenBy(node => node.rect.x)
            .ThenBy(node => node.id)
            .ToArray();

        if (uiManager != null && uiManager.nodeTextForShow != null)
        {
            AddStaticText(items, "node-prompt",
                uiManager.nodeTextForShow.GetComponent<TMP_Text>()?.text);
        }

        foreach (Node gameNode in gameNodes)
        {
            if (gameNode.nodeType != null && gameNode.nodeType.isExit)
            {
                continue;
            }

            string label = GetNodeLabel(gameNode);
            string nodeKey = $"node-{gameNode.GetInstanceID()}";
            items.Add(new Item
            {
                key = nodeKey,
                label = label,
                value = gameNode.isSelected ? "已选择" : string.Empty,
                role = AccessibilityRole.Button,
                state = gameNode.isPopping ? AccessibilityState.Disabled : AccessibilityState.None,
                activate = () =>
                {
                    modalReturnFocusKey = nodeKey;
                    string prompt = gameNode.nodeTextForShow?.Trim();
                    if (!string.IsNullOrWhiteSpace(prompt))
                    {
                        lastNodeContext = prompt;
                        uiManager?.DisplayNodeText(prompt);
                        pendingFocusKey = "node-prompt";
                    }

                    bool activated = gameNode.ActivateForAccessibility();
                    QueueRefresh();
                    return activated;
                }
            });
        }

        Button[] visibleButtons = FindObjectsByType<Button>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(button => button.isActiveAndEnabled && button.interactable &&
                             button.gameObject.activeInHierarchy)
            .ToArray();

        foreach (Button button in visibleButtons.Where(button =>
                     GetButtonLabel(button) != "存档并退出"))
        {
            string label = GetButtonLabel(button);
            string buttonKey = $"button-{button.GetInstanceID()}";
            items.Add(new Item
            {
                key = buttonKey,
                label = label,
                role = AccessibilityRole.Button,
                activate = () =>
                {
                    if (!button.IsInteractable())
                    {
                        return false;
                    }

                    modalReturnFocusKey = buttonKey;
                    button.onClick.Invoke();
                    QueueRefresh();
                    return true;
                }
            });
        }

        foreach (InputField input in FindObjectsByType<InputField>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                     .Where(input => input.isActiveAndEnabled && input.interactable && input.gameObject.activeInHierarchy))
        {
            items.Add(new Item
            {
                key = $"input-{input.GetInstanceID()}",
                label = GetInputLabel(input),
                value = input.text,
                role = AccessibilityRole.TextField,
                activate = () =>
                {
                    input.ActivateInputField();
                    return true;
                },
                setValue = value => input.text = value
            });
        }

        if (uiManager != null && !uiManager.UIShow)
        {
            items.Add(new Item
            {
                key = "save-and-quit",
                label = "存档并退出",
                role = AccessibilityRole.Button,
                activate = () =>
                {
                    uiManager.SaveAndQuit();
                    return true;
                }
            });
        }

        return items;
    }

    private List<Item> CollectMainMenuItems()
    {
        var items = new List<Item>();
        GameMenu gameMenu = FindFirstObjectByType<GameMenu>();
        if (gameMenu == null)
        {
            return items;
        }

        if (gameMenu.AwaitingNewGameConfirmation)
        {
            AddStaticText(items, "main-new-game-warning", "已有存档，重新开始将删除原存档");
            AddAction(items, "main-new-game-confirm", "确认重新开始", () =>
            {
                bool confirmed = gameMenu.ConfirmStartGame();
                QueueRefresh();
                return confirmed;
            });
            AddAction(items, "main-new-game-cancel", "保留存档", () =>
            {
                bool cancelled = gameMenu.CancelStartGame();
                pendingFocusKey = "main-start";
                QueueRefresh();
                return cancelled;
            });
            return items;
        }

        switch (mainMenuPage)
        {
            case MainMenuPage.Options:
                AddAction(items, "main-music-volume", "音乐音量",
                    () => OpenMainMenuPage(MainMenuPage.MusicVolume));
                AddAction(items, "main-sfx-volume", "音效音量",
                    () => OpenMainMenuPage(MainMenuPage.SfxVolume));
                AddAction(items, "main-options-back", "返回主菜单",
                    () => OpenMainMenuPage(MainMenuPage.Root, "main-options"));
                break;

            case MainMenuPage.MusicVolume:
                AddVolumeItems(items, gameMenu, true);
                AddAction(items, "main-music-back", "返回选项",
                    () => OpenMainMenuPage(MainMenuPage.Options, "main-music-volume"));
                break;

            case MainMenuPage.SfxVolume:
                AddVolumeItems(items, gameMenu, false);
                AddAction(items, "main-sfx-back", "返回选项",
                    () => OpenMainMenuPage(MainMenuPage.Options, "main-sfx-volume"));
                break;

            default:
                AddAction(items, "main-start", "开始游戏", () =>
                {
                    bool requested = gameMenu.RequestStartGameForAccessibility();
                    if (gameMenu.AwaitingNewGameConfirmation)
                    {
                        pendingFocusKey = "main-new-game-confirm";
                    }
                    QueueRefresh();
                    return requested;
                });
                AddAction(items, "main-continue", "继续游戏", () => ContinueFromMain(gameMenu));
                AddAction(items, "main-options", "选项",
                    () => OpenMainMenuPage(MainMenuPage.Options));
                AddAction(items, "main-quit", "退出游戏", () =>
                {
                    gameMenu.QuitGame();
                    return true;
                });
                break;
        }

        return items;
    }

    private void AddVolumeItems(ICollection<Item> items, GameMenu gameMenu, bool music)
    {
        float currentVolume = music
            ? soundManager.Instance.currentMusicVolume
            : soundManager.Instance.currentSFXVolume;
        int[] percentages = { 0, 25, 50, 75, 100 };
        foreach (int percentage in percentages)
        {
            int capturedPercentage = percentage;
            string key = $"main-{(music ? "music" : "sfx")}-{percentage}";
            items.Add(new Item
            {
                key = key,
                label = $"{percentage}%",
                value = Mathf.Approximately(currentVolume, percentage / 100f) ? "已选择" : string.Empty,
                role = AccessibilityRole.Button,
                activate = () =>
                {
                    float volume = capturedPercentage / 100f;
                    if (music)
                    {
                        gameMenu.ChangeMusicVolume(volume);
                    }
                    else
                    {
                        gameMenu.ChangeSFXVolume(volume);
                    }

                    pendingFocusKey = key;
                    QueueRefresh();
                    return true;
                }
            });
        }
    }

    private bool ContinueFromMain(GameMenu gameMenu)
    {
        return gameMenu.TryContinueFromMain(null);
    }

    private bool OpenMainMenuPage(MainMenuPage page, string focusKey = "")
    {
        mainMenuPage = page;
        pendingFocusKey = focusKey;
        QueueRefresh();
        return true;
    }

    private void QueueRefresh()
    {
        announceNextRefresh = true;
        currentSignature = string.Empty;
        nextRefreshTime = 0f;
    }

    private static bool IsSceneLoaded(string sceneName)
    {
        Scene scene = SceneManager.GetSceneByName(sceneName);
        return scene.IsValid() && scene.isLoaded;
    }

    private static string GetCutSceneVisualDescription(string animationState)
    {
        return animationState switch
        {
            "Intro1" => "开场蒙太奇。余华《活着》的引文之后，雨中的城市人群佩戴XR眼镜生活，一名乘客在车内操作悬浮界面。",
            "L0_A1" => "雨中手腕上的智能手表特写，屏幕显示2044年10月18日星期五15点38分。",
            "L0_A2" => "俯视雨中的街道，行人沿不同方向匆忙赶路。",
            "L0_A3" => "雨中街道，一位母亲牵着孩子训斥，周围行人匆匆经过。",
            "L0_A4" => "戴着XR眼镜和兜帽的小明突然抬头，神情惊讶。",
            "L0_A5" => "雨中路口发生车祸，两辆汽车燃烧，现场已经封锁。",
            "L0_A6" => "雨中的未来城市街道，XR导航路线改道并指向一条偏僻巷子。",
            "L0_A7" => "昏暗的废弃工厂内，一副发出微弱黄光的XR眼镜被遗落在设备旁。",
            "L0_A8" => "小明走进工厂车间，前方是大型机械设备。",
            "L0_A9" => "小明戴着XR眼镜的面部特写，镜片闪过黄色光线。",
            "L0_A10_1" => "小明背着书包站在昏暗走廊，身后出口仍亮着光。",
            "L0_A10_2" or "L0_A10_3" => "工厂走廊陷入黑暗，小明身后的出口已经关闭。",
            "L0_Video" => "小明启动捡到的XR眼镜。系统画面闪烁，一道女性轮廓逐渐变成橙发紫眼的823。",
            "L1_A1" => "昏暗的蓝绿色工厂车间内，橙发的823在左侧抬起握拳的手，面向右侧以深色背影出现的小明。",
            "L1_A2" => "昏暗车间里，小明站在控制台前，眉头紧皱并攥紧右拳，正面对823发怒。",
            "L1_A3" => "橙发紫眼的823面部特写；她神情严肃地转向小明，身后可见小明的深色轮廓。",
            "L1_A4" => "823和小明站在控制设备旁，823的眼睛亮起红光；画面随后切成黑底红色故障波形。",
            "L1_A5" => "控制台屏幕显示红色警报界面，中央横贯一条紫白色干扰带。",
            "L1_A6" => "红色警报屏幕逐渐淡出，画面转为全黑。",
            "L2_A1" => "工厂走廊里，小明回头望向身后半透明的823投影。",
            "L2_A2" => "823的半透明近景，她一手托着下巴，低头思索。",
            "L2_A3" => "小明正面特写，脸庞一半处在阴影中，神情沉重。",
            "L2_A4" => "明亮窗户前的办公室回忆中，小明站在左侧，面对右侧的校长。",
            "L2_A5_1" => "同一间办公室里，小明仍站在左侧，校长转身背对他望向窗户。",
            "L2_A5_2" => "办公室回忆变成灰白色，小明站在左侧，校长背对他站在窗前。",
            "L2_A6" => "灰白办公室回忆前，823的半透明投影双臂交叉、闭眼思考。",
            "L2_A7" => "823投影猛然抬头示警，画面叠出小明紧张的正面特写。",
            "L3_A1" => "昏暗车间里，小明和823从后方望向悬在空中的机械臂，机械臂亮着红光。",
            "L3_A2" => "亮着红光的机械臂从车间上方快速俯冲下来。",
            "L3_A3" => "机械臂撞向平台并迸出火花；小明腾空躲避，823在右下方低身闪避。",
            "L3_A4" => "小明背对画面，朝前方发出白光的出口奔去，823的投影紧跟在他右侧。",
            "L3_A5" => "小明和823奔向出口的画面逐渐淡出为全黑。",
            "L6_A5" => "823侧脸特写，她抬头望向前方的青色屏幕，神情坚定。",
            "L6_A8" => "橙色背景的家庭回忆中，小明的父母并肩站在房门口看向他。",
            "L6_A9" => "俯视家庭书桌，年幼的小明坐在亮着的屏幕前，父母分别站在两侧，母亲把手放在他头上。",
            "L6_A10" => "年幼的小明戴着XR眼镜坐在书桌前，一位家长把手放在他头上，另一位站在身后。",
            "L6_A11" => "家庭回忆淡去，画面回到工厂；823出现在设备旁，抬手担心地望向小明。",
            "L6_A12" => "工厂内，823靠近小明，抬手擦向他戴着XR眼镜的脸；两人之间闪过一道白色光晕。",
            "L6_Video" => "中控室的多块屏幕显示相互连接的数据节点；画面闪过红色故障波形，随后823站在屏幕前，数据界面逐渐关闭。",
            "TrueEnd1" => "粉紫色落日下的工厂屋顶，小明坐在近处，823坐在对面望向他，远处是城市天际线。",
            "TrueEnd2" => "小明和823背对画面并肩坐在屋顶边缘，一起望着落日与城市。",
            "TrueEnd3" => "落日屋顶上，小明和823仍并肩坐着；小明转头望向823，暖色光线横贯画面。",
            "FakeEnd1" => "灰白天空下的工厂屋顶，823与戴着XR眼镜的小明面对面站着。",
            "FakeEnd2" => "屋顶近景中，823独自面对小明，身后可见低矮围墙与远处的太阳。",
            "TheEnd" => "黑底片尾依次显示鼓励文字、制作人员职务与名单，最后出现游戏标题“抓住未尽的余晖”。",
            "C1" => "黑色标题画面显示：第一章，难以逾越的高山。",
            "C2" => "黑色标题画面显示：第二章，时间飞逝而去。",
            "C3" => "黑色标题画面显示：第三章，腹背受敌之时。",
            "C4" => "黑色标题画面显示：第四章，抓住未尽的余晖。",
            "over" => "失败画面，游戏在此结束。",
            _ => "这一段剧情的详细画面说明尚未补充。"
        };
    }

    private static void AddStaticText(ICollection<Item> items, string key, string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            items.Add(new Item { key = key, label = text.Trim(), role = AccessibilityRole.StaticText });
        }
    }

    private static void AddAction(ICollection<Item> items, string key, string label, Func<bool> action)
    {
        items.Add(new Item { key = key, label = label, role = AccessibilityRole.Button, activate = action });
    }

    private static string BuildSignature(IEnumerable<Item> items)
    {
        return string.Join("|", items.Select(item =>
            $"{item.key}:{item.label}:{(item.role == AccessibilityRole.TextField ? string.Empty : item.value)}:{item.state}"));
    }

    private static string GetCurrentScreenName()
    {
        for (int index = SceneManager.sceneCount - 1; index >= 0; index--)
        {
            Scene scene = SceneManager.GetSceneAt(index);
            if (!scene.isLoaded || scene.name == "LoadScene")
            {
                continue;
            }

            switch (scene.name)
            {
                case "MainMenu": return "主菜单";
                case "GameScene": return "游戏";
                case "PauseMenu": return "暂停菜单";
                case "FailMenu": return "失败菜单";
                default: return scene.name;
            }
        }

        return "载入中";
    }

    private static string GetNodeLabel(Node node)
    {
        QTE qte = node.GetComponent<QTE>();
        if (qte != null)
        {
            return qte.AccessibilityLabel;
        }

        if (node.GetComponent<Moving>() != null || node.GetComponent<Controll>() != null)
        {
            return "进入下一阶段";
        }

        if (node.GetComponent<TimerToResult>() != null)
        {
            return "等待救援";
        }

        if (node.GetComponent<ControlToResult>() != null)
        {
            return "追赶小明";
        }

        Cipher cipher = node.GetComponent<Cipher>();
        if (cipher != null)
        {
            return $"密码第{cipher.index + 1}位，{cipher.value}";
        }

        string label = node.GetComponentInChildren<TMP_Text>(true)?.text;
        if (string.IsNullOrWhiteSpace(label))
        {
            label = node.nodeTextForShow;
        }

        return string.IsNullOrWhiteSpace(label) ? "未命名操作" : label.Trim();
    }

    private static string GetButtonLabel(Button button)
    {
        tongyi_AI ai = button.GetComponentInParent<tongyi_AI>(true);
        if (ai != null && ai.send_button == button)
        {
            return "提交";
        }

        string label = button.GetComponentInChildren<TMP_Text>(true)?.text;
        if (!string.IsNullOrWhiteSpace(label))
        {
            return label.Trim();
        }

        switch (button.name.ToLowerInvariant())
        {
            case "saveandquit": return "存档并退出";
            case "pause": return "暂停";
            case "close": return "关闭";
            case "log": return "对话记录";
            default: return button.name;
        }
    }

    private static string GetInputLabel(InputField input)
    {
        Text placeholder = input.placeholder as Text;
        return placeholder == null || string.IsNullOrWhiteSpace(placeholder.text) ? "输入内容" : placeholder.text.Trim();
    }
}
