using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Accessibility;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using UnityEngine.TestTools;
using TMPro;

public sealed class AccessibilitySmokeTests
{
    [Test]
    public void SystemChineseFontUsesDynamicAtlasAndEmbeddedFallback()
    {
        TMP_FontAsset font = GetSystemChineseFont();
        Assert.That(font, Is.Not.Null, "必须能创建系统中文字体或动态中文后备字体。");
        Assert.That(font.faceInfo.pointSize, Is.GreaterThanOrEqualTo(90));
        Assert.That(font.atlasPadding, Is.GreaterThanOrEqualTo(9));
        Assert.That(font.atlasPopulationMode, Is.Not.EqualTo(AtlasPopulationMode.Static),
            "不得再次把大型静态中文图集打入 APK。");
        Assert.That(font.material.shader.name, Is.EqualTo("TextMeshPro/Mobile/Distance Field"),
            "Android 中文字体必须使用移动端 SDF 材质。");
        Assert.That(
            font.material.GetFloat(ShaderUtilities.ID_OutlineWidth),
            Is.GreaterThanOrEqualTo(0.1f),
            "浅色正文必须有稳定的深色描边以保证复杂背景上的局部对比度。");

        TMP_FontAsset fallback = Resources.Load<TMP_FontAsset>(
            "Front/ChineseSubset/FactoryEscapeChineseFallback SDF");
        Assert.That(fallback, Is.Not.Null, "必须保留最小动态中文后备字体。");
        Assert.That(fallback.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Dynamic));
        Assert.That(fallback.sourceFontFile, Is.Not.Null);
        Assert.That(font.HasCharacters("抓住未尽的余晖小明车间继续游戏存档退出"), Is.True,
            "当前字体链必须保留已经生成的关键中文字符。");
        Assert.That(fallback.sourceFontFile.HasCharacter('接'), Is.True,
            "动态后备字体源必须覆盖项目中文字符。");
        Assert.That(fallback.sourceFontFile.HasCharacter('口'), Is.True,
            "动态后备字体源必须覆盖项目中文字符。");
    }

    [Test]
    public void AndroidDoesNotForceDesktopRenderingResolution()
    {
        string source = File.ReadAllText(Path.Combine(
            Application.dataPath, "Scripts", "GameManager", "GameManager.cs"))
            .Replace("\r\n", "\n");
        StringAssert.Contains(
            "#if UNITY_STANDALONE\n        Screen.SetResolution(1920, 1080, true);\n#endif",
            source,
            "固定 1920×1080 只能保留给桌面平台，Android 必须使用原生渲染尺寸。");
    }

    [Test]
    public void AndroidBuildAllowsOnlyPortraitOrientation()
    {
        string source = File.ReadAllText(Path.Combine(
            Application.dataPath, "Editor", "FactoryEscapeAutomation.cs"));
        StringAssert.Contains(
            "PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;", source);
        StringAssert.Contains("PlayerSettings.allowedAutorotateToPortrait = true;", source);
        StringAssert.Contains("PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;", source);
        StringAssert.Contains("PlayerSettings.allowedAutorotateToLandscapeLeft = false;", source);
        StringAssert.Contains("PlayerSettings.allowedAutorotateToLandscapeRight = false;", source);
    }

    [UnityTest]
    public IEnumerator PortraitPresentationShowsReadableMainMenuRows()
    {
        DestroyExistingGameManager();
        yield return SceneManager.LoadSceneAsync("LoadScene", LoadSceneMode.Single);
        for (int frame = 0; frame < 240 &&
             !SceneManager.GetSceneByName("MainMenu").isLoaded; frame++)
        {
            yield return null;
        }
        Assert.That(SceneManager.GetSceneByName("MainMenu").isLoaded, Is.True,
            "竖屏主菜单测试必须等待真正的主菜单场景加载完成。 ");
        if (SceneManager.GetSceneByName("GameScene").isLoaded)
        {
            yield return SceneManager.UnloadSceneAsync("GameScene");
        }
        yield return new WaitForSecondsRealtime(0.6f);

        MonoBehaviour presentation = Resources.FindObjectsOfTypeAll<MonoBehaviour>()
            .FirstOrDefault(component => component.GetType().Name == "PortraitTextPresentation");
        Assert.That(presentation, Is.Not.Null, "竖屏文字呈现层必须随应用启动。 ");
        System.Reflection.PropertyInfo entryCountProperty =
            presentation.GetType().GetProperty("EntryCount");
        System.Reflection.PropertyInfo rootProperty =
            presentation.GetType().GetProperty("PresentationRoot");
        Assert.That(entryCountProperty, Is.Not.Null);
        Assert.That(rootProperty, Is.Not.Null);
        for (int frame = 0; frame < 120; frame++)
        {
            Transform currentRoot = (Transform)rootProperty.GetValue(presentation);
            bool mainMenuReady = (int)entryCountProperty.GetValue(presentation) == 3 &&
                                 currentRoot.GetComponentsInChildren<TMP_Text>(false)
                                     .Any(text => text.text == "抓住未尽的余晖");
            if (mainMenuReady)
            {
                break;
            }

            yield return null;
        }

        string presentationSource = File.ReadAllText(Path.Combine(
            Application.dataPath, "Scripts", "Accessibility", "PortraitTextPresentation.cs"));
        StringAssert.Contains("new Vector2(1080f, 2400f)", presentationSource);
        Transform presentationRoot = (Transform)rootProperty.GetValue(presentation);
        TMP_Text[] texts = presentationRoot.GetComponentsInChildren<TMP_Text>(false);
        Assert.That((int)entryCountProperty.GetValue(presentation), Is.EqualTo(3),
            $"游戏目录应显示子游戏、选项和退出应用。当前文字：{string.Join("|", texts.Select(text => text.text))}");
        Assert.That(texts.Any(text => text.text == "选择游戏"), Is.True);
        Assert.That(texts.Any(text => text.text == "抓住未尽的余晖"), Is.True);
        Assert.That(texts.Any(text => text.text == "退出应用"), Is.True);
        Assert.That(texts.All(text => text.fontSize >= 38f), Is.True,
            "竖屏可见文字不得小于 38。 ");
        Assert.That(texts.All(text => text.font != null), Is.True,
            "竖屏可见文字必须使用可渲染中文的 TMP 字体。 ");
        Assert.That(texts.All(text => text.fontSharedMaterial != text.font.material), Is.True,
            "竖屏文字必须使用独立材质，避免原界面动画把标题或正文改成透明。 ");

        presentation.GetType().GetMethod("SetArtwork").Invoke(presentation, new object[] { null });
        RectTransform artworkBackground = presentationRoot.Find(
            "SafeArea/ArtworkBackground") as RectTransform;
        RectTransform contentPanel = presentationRoot.Find(
            "SafeArea/ContentPanel") as RectTransform;
        Assert.That(artworkBackground.gameObject.activeSelf, Is.False,
            "无图片剧情不得保留空白图片区域。 ");
        Assert.That(contentPanel.anchorMax.y, Is.EqualTo(0.92f).Within(0.001f),
            "无图片剧情的正文区域应扩展到标题下方。 ");
    }

    [UnityTest]
    public IEnumerator MainMenuVisibleTextUsesReadablePresentation()
    {
        DestroyExistingGameManager();
        yield return SceneManager.LoadSceneAsync("LoadScene", LoadSceneMode.Single);
        for (int frame = 0; frame < 90 && FindGameComponent("GameMenu") == null; frame++)
        {
            yield return null;
        }

        TMP_FontAsset font = GetSystemChineseFont();
        TMP_Text[] visibleTexts = Resources.FindObjectsOfTypeAll<TMP_Text>()
            .Where(text => text.gameObject.scene.isLoaded && text.gameObject.activeInHierarchy &&
                           text.color.a > 0.001f)
            .ToArray();
        Assert.That(visibleTexts, Is.Not.Empty, "主菜单必须有明眼人可见的文字。");
        foreach (TMP_Text text in visibleTexts)
        {
            Assert.That(text.font, Is.EqualTo(font), $"{text.name} 没有使用高清主字体。");
            Assert.That(text.color.r, Is.EqualTo(242f / 255f).Within(0.001f));
            Assert.That(text.color.g, Is.EqualTo(1f).Within(0.001f));
            Assert.That(text.color.b, Is.EqualTo(1f).Within(0.001f));
            if (text is TextMeshProUGUI)
            {
                Assert.That(text.fontSize, Is.GreaterThanOrEqualTo(32f));
                text.ForceMeshUpdate(true);
                Assert.That(text.isTextOverflowing, Is.False, $"主菜单文字发生溢出：{text.text}");
            }
        }

    }

    [Test]
    public void ChatHistoryWritesOnlyToPersistentDataPath()
    {
        string source = File.ReadAllText(Path.Combine(
            Application.dataPath, "Scripts", "NodeComponent", "AI", "writeAndLoadHistory.cs"));
        StringAssert.Contains("Application.persistentDataPath", source);
        StringAssert.DoesNotContain("Application.dataPath +", source);
    }

    [Test]
    public void AIRequestHasTimeoutAndCompletesEveryFailurePath()
    {
        string source = File.ReadAllText(Path.Combine(
            Application.dataPath, "Scripts", "NodeComponent", "AI", "tongyi_AI.cs"));
        StringAssert.Contains("request.timeout = 15", source);
        StringAssert.Contains("CompleteRequestFailure(bot", source);
        StringAssert.Contains("reply_is_finished = true", source);
    }

    [Test]
    public void AIFinalResultLeavesOnlyTheExplicitContinueAction()
    {
        string source = File.ReadAllText(Path.Combine(
            Application.dataPath, "Scripts", "Accessibility", "FactoryEscapeAccessibility.cs"));
        StringAssert.Contains("bool resultReady = aiResult != null || level1Result != null", source);
        StringAssert.Contains("if (!resultReady && ai != null && dialogSystem.textFinished)", source);
        StringAssert.Contains("if (ai != null && button == ai.send_button)", source);
        StringAssert.Contains("AddAction(items, \"ai-result-continue\", \"继续剧情\"", source);
        StringAssert.Contains("QueueRefresh();", source);
    }

    [Test]
    public void NodeActionsExposeExplicitResultsAndFailureDirection()
    {
        Type accessibilityType = Type.GetType("FactoryEscapeAccessibility, Assembly-CSharp");
        MethodInfo buildResult = accessibilityType?.GetMethod(
            "BuildNodeActionResult", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(buildResult, Is.Not.Null);

        Assert.That(buildResult.Invoke(null, new object[]
        {
            "按下可以启动传送带", string.Empty, new List<string> { "撬棍" }
        }), Is.EqualTo("传送带已启动，发现撬棍。"));
        Assert.That(buildResult.Invoke(null, new object[]
        {
            "应急操作面板", "螺丝刀", new List<string> { "传送带开启按钮" }
        }), Is.EqualTo("已使用螺丝刀，发现传送带开启按钮。"));

        string source = File.ReadAllText(Path.Combine(
            Application.dataPath, "Scripts", "Accessibility", "FactoryEscapeAccessibility.cs"));
        StringAssert.Contains("破解未成功。机房和主管办公室已解锁，请前往新区域继续调查。", source);
        StringAssert.Contains("AddStaticText(items, \"node-action-result\", nodeActionResult);", source);
        StringAssert.Contains("SendAnnouncement(pendingAnnouncement)", source,
            "操作结果必须主动通知读屏，同时保留可见文字。 ");
    }

    [Test]
    public void NodeScopeProvidesReturnAndRestoresTheRoomFocus()
    {
        string source = File.ReadAllText(Path.Combine(
            Application.dataPath, "Scripts", "Accessibility", "FactoryEscapeAccessibility.cs"));
        StringAssert.Contains("activeGameNodes.Where(node => node != scopeRoot)", source,
            "房间范围必须包含当前节点图中已经显现的多级和独立线索节点。");
        StringAssert.Contains("AddAction(items, \"node-overview-back\", \"返回节点总览\"", source);
        StringAssert.Contains("nodeScopeRootId = string.Empty;", source);
        StringAssert.Contains("$\"node-{rootNode.GetInstanceID()}\"", source,
            "返回总览后必须把焦点恢复到刚退出的房间。 ");
    }

    [Test]
    public void AISubmitButtonHasChineseVisibleTextAndObjectName()
    {
        string prefab = File.ReadAllText(Path.Combine(
            Application.dataPath, "Prefabs", "UI", "AIDialogPanel.prefab"));
        StringAssert.DoesNotContain("m_Name: Button (Legacy)", prefab);
        StringAssert.Contains("m_Name: 提交", prefab);
        StringAssert.Contains("m_Text: \"\\u63D0\\u4EA4\"", prefab);
    }

    [Test]
    public void AITextEntryDoesNotRebuildTheAccessibilityHierarchyPerCharacter()
    {
        string accessibilitySource = File.ReadAllText(Path.Combine(
            Application.dataPath, "Scripts", "Accessibility", "FactoryEscapeAccessibility.cs"));
        string aiSource = File.ReadAllText(Path.Combine(
            Application.dataPath, "Scripts", "NodeComponent", "AI", "tongyi_AI.cs"));
        StringAssert.Contains("item.role == AccessibilityRole.TextField ? string.Empty : item.value",
            accessibilitySource);
        StringAssert.DoesNotContain("!string.IsNullOrWhiteSpace(chat_input_field.text)",
            aiSource.Substring(aiSource.IndexOf("public bool CanSend", StringComparison.Ordinal), 150));
    }

    [Test]
    public void AIDialogUsesThreeOfflineChoicesWithoutTextEntry()
    {
        string accessibilitySource = File.ReadAllText(Path.Combine(
            Application.dataPath, "Scripts", "Accessibility", "FactoryEscapeAccessibility.cs"));
        int aiSectionStart = accessibilitySource.IndexOf(
            "if (dialogSystem != null && dialogSystem.AIDialogPanel.gameObject.activeInHierarchy)",
            StringComparison.Ordinal);
        int aiSectionEnd = accessibilitySource.IndexOf(
            "if (uiManager != null && uiManager.textNodeUI", aiSectionStart,
            StringComparison.Ordinal);
        string aiSection = accessibilitySource.Substring(
            aiSectionStart, aiSectionEnd - aiSectionStart);
        StringAssert.Contains("if (!resultReady && ai != null && dialogSystem.textFinished)", aiSection);
        StringAssert.Contains("请选择一句话安抚823。", aiSection);
        StringAssert.Contains("你已经做得很好了，我会陪着你。", aiSection);
        StringAssert.Contains("先慢慢来，我们换个角度继续。", aiSection);
        StringAssert.Contains("现在没时间焦虑，快点破解。", aiSection);
        StringAssert.Contains("SubmitPresetResponse", aiSection);
        StringAssert.DoesNotContain("GetComponentsInChildren<InputField>", aiSection,
            "823 对话页不得再暴露自由输入焦点。");

        string aiSource = File.ReadAllText(Path.Combine(
            Application.dataPath, "Scripts", "NodeComponent", "AI", "tongyi_AI.cs"));
        int presetStart = aiSource.IndexOf(
            "public bool SubmitPresetResponse", StringComparison.Ordinal);
        Assert.That(presetStart, Is.GreaterThanOrEqualTo(0));
        int presetEnd = aiSource.IndexOf("\n    public ", presetStart + 1,
            StringComparison.Ordinal);
        string presetMethod = aiSource.Substring(presetStart, presetEnd - presetStart);
        StringAssert.Contains("StaticEventHandler.CallCommit", presetMethod);
        StringAssert.DoesNotContain("PostMessage", presetMethod,
            "固定选项必须完全离线，不得尝试联网后再回退。");

        string[] aiGraphs = Directory.GetFiles(
            Path.Combine(Application.dataPath, "ScriptableObjectAssets", "NodeGraph"),
            "*.asset")
            .Where(path => File.ReadAllText(path).Contains("submissionTimes:"))
            .ToArray();
        Assert.That(aiGraphs, Is.Not.Empty);
        foreach (string graph in aiGraphs)
        {
            string[] submissionLines = File.ReadAllLines(graph)
                .Where(line => line.TrimStart().StartsWith("submissionTimes:",
                    StringComparison.Ordinal)).ToArray();
            Assert.That(submissionLines.All(line => line.Trim() == "submissionTimes: 3"), Is.True,
                $"{Path.GetFileName(graph)} 的 823 对话必须统一为三轮。");
        }
    }

    [Test]
    public void AccessibleNewGameRequiresTheDistinctConfirmationAction()
    {
        string accessibilitySource = File.ReadAllText(Path.Combine(
            Application.dataPath, "Scripts", "Accessibility", "FactoryEscapeAccessibility.cs"));
        StringAssert.Contains("gameMenu.RequestStartGameForAccessibility()", accessibilitySource);
        StringAssert.DoesNotContain("gameMenu.StartGame();", accessibilitySource);
        StringAssert.Contains("main-new-game-confirm", accessibilitySource);
        StringAssert.Contains("main-new-game-cancel", accessibilitySource);
    }

    [Test]
    public void AIDialogLogIncludesTheSpeakerName()
    {
        string dialogSource = File.ReadAllText(Path.Combine(
            Application.dataPath, "Scripts", "Dialog", "DialogSystem.cs"));
        string aiSource = File.ReadAllText(Path.Combine(
            Application.dataPath, "Scripts", "NodeComponent", "AI", "tongyi_AI.cs"));
        StringAssert.Contains("AddAIDialogLogCell(AINameText.text, textToPlay)", dialogSource);
        StringAssert.Contains("$\"{speaker}：{dialogText}\"", dialogSource);
        StringAssert.Contains("AddAIDialogLogCell(\"小明\", content)", aiSource);
    }

    [Test]
    public void ScenesDoNotSerializeAIKeys()
    {
        foreach (string scenePath in Directory.GetFiles(
                     Path.Combine(Application.dataPath, "Scenes"), "*.unity"))
        {
            string[] serializedKeys = File.ReadAllLines(scenePath)
                .Where(line => line.TrimStart().StartsWith("Apikey:", StringComparison.Ordinal))
                .ToArray();
            Assert.That(serializedKeys.All(line => line.Trim() == "Apikey:"), Is.True,
                $"场景不得保存 AI 凭据：{Path.GetFileName(scenePath)}");
        }
    }

    [Test]
    public void SaveProfilesCannotEscapeGameDataDirectory()
    {
        Type saveManagerType = Type.GetType("SaveManager, Assembly-CSharp");
        Type nodeStateType = Type.GetType("NodeState, Assembly-CSharp");
        MethodInfo exists = saveManagerType?.GetMethod("Exists", BindingFlags.Public | BindingFlags.Static);
        MethodInfo tryLoad = saveManagerType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "TryLoad")
            .MakeGenericMethod(nodeStateType);
        MethodInfo delete = saveManagerType?.GetMethod("Delete", BindingFlags.Public | BindingFlags.Static);

        Assert.That((bool)exists.Invoke(null, new object[] { "../outside" }), Is.False);
        object[] loadArguments = { "../outside", null };
        Assert.That((bool)tryLoad.Invoke(null, loadArguments), Is.False);
        Assert.DoesNotThrow(() => delete.Invoke(null, new object[] { "../outside" }));
    }

    [Test]
    public void SaveAndQuitButtonUsesAccessibleLabelAndSceneAction()
    {
        string gameScene = File.ReadAllText(
            Path.Combine(Application.dataPath, "Scenes", "GameScene.unity"));
        StringAssert.Contains("m_Name: SaveAndQuit", gameScene);
        StringAssert.Contains("m_MethodName: SaveAndQuit", gameScene);
        StringAssert.DoesNotContain("m_MethodName: OpenPauseMenu", gameScene);

        Type accessibilityType = Type.GetType(
            "FactoryEscapeAccessibility, Assembly-CSharp");
        MethodInfo getButtonLabel = accessibilityType?.GetMethod(
            "GetButtonLabel", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(getButtonLabel, Is.Not.Null);

        var buttonObject = new GameObject(
            "SaveAndQuit", typeof(RectTransform), typeof(Image), typeof(Button));
        try
        {
            string label = (string)getButtonLabel.Invoke(
                null, new object[] { buttonObject.GetComponent<Button>() });
            Assert.That(label, Is.EqualTo("存档并退出"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(buttonObject);
        }
    }

    [Test]
    public void GameProgressSaveCanBeReplacedAndLoadedFromDisk()
    {
        string profileName = $"__GameProgressTest_{Guid.NewGuid():N}";
        Type progressType = Type.GetType("GameProgressState, Assembly-CSharp");
        Type profileType = Type.GetType("SaveProfile`1, Assembly-CSharp")?
            .MakeGenericType(progressType);
        Type saveManagerType = Type.GetType("SaveManager, Assembly-CSharp");

        Assert.That(progressType, Is.Not.Null);
        Assert.That(profileType, Is.Not.Null);
        Assert.That(saveManagerType, Is.Not.Null);

        MethodInfo saveOrReplace = saveManagerType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "SaveOrReplace")
            .MakeGenericMethod(progressType);
        MethodInfo tryLoad = saveManagerType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "TryLoad")
            .MakeGenericMethod(progressType);
        MethodInfo delete = saveManagerType.GetMethod(
            "Delete", BindingFlags.Public | BindingFlags.Static);

        try
        {
            object firstState = Activator.CreateInstance(progressType);
            progressType.GetField("version").SetValue(firstState, 1);
            progressType.GetField("levelIndex").SetValue(firstState, 2);
            progressType.GetField("graphIndex").SetValue(firstState, 1);
            progressType.GetField("enterNodeGraphTimes").SetValue(
                firstState, new List<int> { 1, 3 });
            progressType.GetField("nodeIdsInGraph").SetValue(firstState,
                new List<List<string>>
                {
                    new List<string> { "node-a" },
                    new List<string> { "node-b", "node-c" }
                });
            progressType.GetField("currentAnxiety").SetValue(firstState, 12.5f);
            progressType.GetField("maxAnxiety").SetValue(firstState, 40f);
            progressType.GetField("rate").SetValue(firstState, 0.5f);
            progressType.GetField("previousChapterBot").SetValue(firstState, 823);
            progressType.GetField("level1GetResultTimes").SetValue(firstState, 4);

            object profile = Activator.CreateInstance(
                profileType, new[] { profileName, firstState });
            saveOrReplace.Invoke(null, new[] { profile });

            object[] firstLoadArguments = { profileName, null };
            Assert.That((bool)tryLoad.Invoke(null, firstLoadArguments), Is.True);
            object firstLoad = profileType.GetField("saveData")
                .GetValue(firstLoadArguments[1]);
            Assert.That(progressType.GetField("levelIndex").GetValue(firstLoad),
                Is.EqualTo(2));
            Assert.That(((List<List<string>>)progressType.GetField("nodeIdsInGraph")
                    .GetValue(firstLoad))[1],
                Is.EqualTo(new[] { "node-b", "node-c" }));

            progressType.GetField("levelIndex").SetValue(firstState, 5);
            progressType.GetField("currentAnxiety").SetValue(firstState, 7f);
            saveOrReplace.Invoke(null, new[] { profile });

            object[] secondLoadArguments = { profileName, null };
            Assert.That((bool)tryLoad.Invoke(null, secondLoadArguments), Is.True);
            object secondLoad = profileType.GetField("saveData")
                .GetValue(secondLoadArguments[1]);
            Assert.That(progressType.GetField("levelIndex").GetValue(secondLoad),
                Is.EqualTo(5));
            Assert.That(progressType.GetField("currentAnxiety").GetValue(secondLoad),
                Is.EqualTo(7f));
        }
        finally
        {
            delete.Invoke(null, new object[] { profileName });
        }
    }

    [Test]
    public void AllSerializedStoryFramesHaveSpecificDescriptions()
    {
        Type accessibilityType = Type.GetType("FactoryEscapeAccessibility, Assembly-CSharp");
        MethodInfo getDescription = accessibilityType?.GetMethod(
            "GetCutSceneVisualDescription", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.That(getDescription, Is.Not.Null, "应能读取过场画面说明映射。");
        string[] serializedExtensions = { ".asset", ".prefab", ".unity" };
        string[] animationStates = Directory
            .EnumerateFiles(Application.dataPath, "*.*", SearchOption.AllDirectories)
            .Where(path => serializedExtensions.Contains(Path.GetExtension(path)))
            .SelectMany(File.ReadLines)
            .Select(line => Regex.Match(line, @"^\s*-?\s*animationStateName:\s*(\S+)\s*$"))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .OrderBy(state => state)
            .ToArray();

        Assert.That(animationStates, Has.Length.EqualTo(51), "过场状态清点数量发生变化时应重新核对画面说明。");
        foreach (string animationState in animationStates)
        {
            string description = (string)getDescription.Invoke(
                null, new object[] { animationState });
            Assert.That(
                description,
                Is.Not.EqualTo("这一段剧情的详细画面说明尚未补充。"),
                $"过场状态 {animationState} 必须提供具体画面说明。");
        }

        Assert.That(
            getDescription.Invoke(null, new object[] { "L1_A1" }),
            Is.EqualTo("昏暗的蓝绿色工厂车间内，橙发的823在左侧抬起握拳的手，面向右侧以深色背影出现的小明。"),
            "当前过场必须提供具体画面说明，不能回退为未补充提示。");
    }

    [Test]
    public void VisualMechanicsExposeExplicitAccessibleActions()
    {
        string[] mechanicTypes =
        {
            "AngleLocked", "CipherLocked", "QuickClick", "QTE", "Controllable",
            "Probe", "Synthesizer", "SyntheticPicture", "Moving", "Controll",
            "ControlToResult", "AILocked", "Level1AILock", "Synthesize", "TimerToResult"
        };

        foreach (string typeName in mechanicTypes)
        {
            Type type = Type.GetType($"{typeName}, Assembly-CSharp");
            Assert.That(type, Is.Not.Null, $"未找到视觉玩法组件 {typeName}。");
            Assert.That(
                type.GetInterface("IAccessibleNodeAction"),
                Is.Not.Null,
                $"{typeName} 必须提供显式无障碍动作。");
        }
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        AssistiveSupport.screenReaderStatusOverride =
            AssistiveSupport.ScreenReaderStatusOverride.OSDriven;
        yield return null;
    }

    [UnityTest]
    public IEnumerator SavedProgressRestoresAfterMemoryResetAndNewGameClearsIt()
    {
        AssistiveSupport.screenReaderStatusOverride =
            AssistiveSupport.ScreenReaderStatusOverride.ForceEnabled;
        DestroyExistingGameManager();
        SceneManager.LoadScene("LoadScene");

        object gameManager = null;
        object gameMenu = null;
        for (int frame = 0; frame < 180; frame++)
        {
            gameManager = FindGameComponent("GameManager");
            gameMenu = FindGameComponent("GameMenu");
            if (gameManager != null && gameMenu != null)
            {
                break;
            }

            yield return null;
        }

        Assert.That(gameManager, Is.Not.Null, "入口场景应创建游戏状态管理器。");
        Assert.That(gameMenu, Is.Not.Null, "入口场景应载入主菜单。");
        gameMenu.GetType().GetMethod("StartGame").Invoke(gameMenu, null);

        object nodeMapBuilder = null;
        float timeout = Time.realtimeSinceStartup + 8f;
        while (Time.realtimeSinceStartup < timeout)
        {
            nodeMapBuilder = FindGameComponent("NodeMapBuilder");
            if (SceneManager.GetSceneByName("GameScene").isLoaded &&
                nodeMapBuilder != null &&
                ((System.Collections.IDictionary)nodeMapBuilder.GetType()
                    .GetField("nodeHasCreated").GetValue(nodeMapBuilder)).Count > 0 &&
                gameManager.GetType().GetField("gameState").GetValue(gameManager)
                    .ToString() == "Playing")
            {
                break;
            }

            yield return null;
        }

        Assert.That(SceneManager.GetSceneByName("GameScene").isLoaded, Is.True);
        Assert.That(nodeMapBuilder, Is.Not.Null);

        object uiManager = FindGameComponent("UIManager");
        object videoManager = FindGameComponent("VideoManager");
        object dialogSystem = FindGameComponent("DialogSystem");
        Assert.That(uiManager, Is.Not.Null);
        uiManager.GetType().GetField("UIShow").SetValue(uiManager, false);
        ((Transform)videoManager.GetType().GetField("cutSceneUIPanel").GetValue(videoManager))
            .gameObject.SetActive(false);
        ((GameObject)dialogSystem.GetType().GetField("dialogPanel").GetValue(dialogSystem))
            .SetActive(false);
        ((Transform)dialogSystem.GetType().GetField("AIDialogPanel").GetValue(dialogSystem))
            .gameObject.SetActive(false);

        Type managerType = gameManager.GetType();
        MethodInfo saveProgress = managerType.GetMethod(
            "TrySaveCurrentProgress", BindingFlags.Instance | BindingFlags.NonPublic);
        object[] saveArguments = { null };
        Assert.That((bool)saveProgress.Invoke(gameManager, saveArguments), Is.True,
            saveArguments[0] as string);

        Type saveManagerType = Type.GetType("SaveManager, Assembly-CSharp");
        MethodInfo saveExists = saveManagerType.GetMethod(
            "Exists", BindingFlags.Static | BindingFlags.Public);
        Assert.That((bool)saveExists.Invoke(null, new object[] { "GameProgress" }), Is.True,
            "保存后应生成跨进程总进度文件。");

        FieldInfo levelIndex = managerType.GetField("levelIndex");
        FieldInfo graphIndex = managerType.GetField(
            "graphIndex", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo enterTimes = managerType.GetField(
            "enterNodeGraphTimesList", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo nodeIds = managerType.GetField(
            "nodeIdsInGraph", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo currentAnxiety = managerType.GetField("currentAnxiety");

        int expectedLevel = (int)levelIndex.GetValue(gameManager);
        int expectedGraph = (int)graphIndex.GetValue(gameManager);
        float expectedAnxiety = (float)currentAnxiety.GetValue(gameManager);
        var savedNodeIds = (IList)nodeIds.GetValue(gameManager);
        var currentGraphNodeIds = (IList)savedNodeIds[expectedGraph];
        int expectedNodeCount = currentGraphNodeIds.Count;
        string expectedFirstNode = (string)currentGraphNodeIds[0];

        levelIndex.SetValue(gameManager, -1);
        graphIndex.SetValue(gameManager, 0);
        enterTimes.SetValue(gameManager, Activator.CreateInstance(enterTimes.FieldType));
        nodeIds.SetValue(gameManager, Activator.CreateInstance(nodeIds.FieldType));
        currentAnxiety.SetValue(gameManager, -1f);

        object[] loadArguments = { null };
        bool loaded = (bool)managerType.GetMethod("TryLoadSavedGame")
            .Invoke(gameManager, loadArguments);
        Assert.That(loaded, Is.True, loadArguments[0] as string);
        Assert.That(levelIndex.GetValue(gameManager), Is.EqualTo(expectedLevel));
        Assert.That(graphIndex.GetValue(gameManager), Is.EqualTo(expectedGraph));
        Assert.That(currentAnxiety.GetValue(gameManager), Is.EqualTo(expectedAnxiety));

        var restoredNodeIds = (IList)nodeIds.GetValue(gameManager);
        var restoredCurrentGraph = (IList)restoredNodeIds[expectedGraph];
        Assert.That(restoredCurrentGraph.Count, Is.EqualTo(expectedNodeCount));
        Assert.That(restoredCurrentGraph[0], Is.EqualTo(expectedFirstNode));

        managerType.GetMethod("StartNewGame").Invoke(gameManager, null);
        Assert.That((bool)saveExists.Invoke(null, new object[] { "GameProgress" }), Is.False,
            "开始游戏应清除旧的跨进程存档。");
        Assert.That(levelIndex.GetValue(gameManager), Is.EqualTo(0));
    }

    [UnityTest]
    public IEnumerator MainMenuExposesCatalogThenSelectedGameActions()
    {
        AssistiveSupport.screenReaderStatusOverride =
            AssistiveSupport.ScreenReaderStatusOverride.ForceEnabled;
        DestroyExistingGameManager();
        SceneManager.LoadScene("LoadScene");

        for (int frame = 0; frame < 120 &&
             !SceneManager.GetSceneByName("MainMenu").isLoaded; frame++)
        {
            yield return null;
        }
        Type.GetType("FactoryEscapeAccessibility, Assembly-CSharp")?
            .GetMethod("RefreshScreen", BindingFlags.Static | BindingFlags.Public)?
            .Invoke(null, new object[] { string.Empty });

        for (int frame = 0; frame < 120; frame++)
        {
            AccessibilityHierarchy current = AssistiveSupport.activeHierarchy;
            if (current != null &&
                current.rootNodes.Select(node => node.label).SequenceEqual(
                    new[] { "抓住未尽的余晖", "选项", "退出应用" }))
            {
                break;
            }

            yield return null;
        }

        AccessibilityHierarchy hierarchy = AssistiveSupport.activeHierarchy;
        Assert.That(hierarchy, Is.Not.Null, "启用读屏后应创建无障碍层级。");
        Assert.That(
            hierarchy.rootNodes.Select(node => node.label),
            Is.EqualTo(new[] { "抓住未尽的余晖", "选项", "退出应用" }),
            "入口应先暴露子游戏目录，再进入具体游戏的开始和继续操作。");
        Assert.That(
            hierarchy.rootNodes.All(node => node.role == AccessibilityRole.Button),
            Is.True,
            "主菜单的所有焦点都必须是可操作菜单项。");
        Assert.That(
            hierarchy.rootNodes.All(node => node.frameGetter != null),
            Is.True,
            "竖屏视觉行会因布局与滚动移动，无障碍焦点框必须实时读取视觉坐标。");

        string[] removedLabels = { "主菜单", "主选单", "务必联网进行游戏", "将此节点长按拖至“游戏”" };
        Assert.That(
            hierarchy.rootNodes.Any(node => removedLabels.Contains(node.label)),
            Is.False,
            "读屏顺序中不应保留标题、树状入口或拖拽提示。");

        FieldInfo invokedField = typeof(AccessibilityNode).GetField(
            "invoked", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(invokedField, Is.Not.Null, "无法检查菜单项的激活动作。");
        Assert.That(
            hierarchy.rootNodes.All(node => invokedField.GetValue(node) is Func<bool>),
            Is.True,
            "每个主菜单项都必须绑定双击激活动作。");

        string[] forbiddenHints = { "Enter", "Return", "Space", "回车", "空格" };
        foreach (AccessibilityNode node in hierarchy.rootNodes)
        {
            string spokenText = $"{node.label} {node.hint}";
            Assert.That(
                forbiddenHints.Any(hint => spokenText.Contains(hint)),
                Is.False,
                $"节点“{node.label}”不应重复播报激活方式。");
        }

        AccessibilityNode optionsNode = hierarchy.rootNodes.Single(node => node.label == "选项");
        Assert.That(((Func<bool>)invokedField.GetValue(optionsNode)).Invoke(), Is.True);

        for (int frame = 0; frame < 30; frame++)
        {
            if (AssistiveSupport.activeHierarchy.rootNodes.Any(node => node.label == "音乐音量"))
            {
                break;
            }

            yield return null;
        }

        Assert.That(
            AssistiveSupport.activeHierarchy.rootNodes.Select(node => node.label),
            Is.EqualTo(new[] { "音乐音量", "音效音量", "返回游戏列表" }),
            "激活选项后应进入可滑动操作的选项菜单。");

        AccessibilityNode backNode = AssistiveSupport.activeHierarchy.rootNodes
            .Single(node => node.label == "返回游戏列表");
        Assert.That(((Func<bool>)invokedField.GetValue(backNode)).Invoke(), Is.True);
        for (int frame = 0; frame < 30; frame++)
        {
            if (AssistiveSupport.activeHierarchy.rootNodes.Any(node => node.label == "抓住未尽的余晖"))
            {
                break;
            }

            yield return null;
        }

        AccessibilityNode gameNode = AssistiveSupport.activeHierarchy.rootNodes
            .Single(node => node.label == "抓住未尽的余晖");
        Assert.That(((Func<bool>)invokedField.GetValue(gameNode)).Invoke(), Is.True);
        for (int frame = 0; frame < 30; frame++)
        {
            if (AssistiveSupport.activeHierarchy.rootNodes.Any(node => node.label == "开始游戏"))
            {
                break;
            }

            yield return null;
        }

        Assert.That(
            AssistiveSupport.activeHierarchy.rootNodes.Select(node => node.label),
            Is.EqualTo(new[] { "开始游戏", "继续游戏", "返回游戏列表" }),
            "选择子游戏后才应显示该游戏的开始、继续和返回操作。");

        AccessibilityNode startNode = AssistiveSupport.activeHierarchy.rootNodes
            .Single(node => node.label == "开始游戏");
        Assert.That(((Func<bool>)invokedField.GetValue(startNode)).Invoke(), Is.True);
        float timeout = Time.realtimeSinceStartup + 5f;
        while (Time.realtimeSinceStartup < timeout)
        {
            if (SceneManager.GetSceneByName("GameScene").isLoaded)
            {
                break;
            }

            yield return null;
        }

        Assert.That(
            SceneManager.GetSceneByName("GameScene").isLoaded,
            Is.True,
            "双击开始游戏后应进入游戏场景。");

        string introDescription = "画面描述：开场蒙太奇。余华《活着》的引文之后，雨中的城市人群佩戴XR眼镜生活，一名乘客在车内操作悬浮界面。";
        for (int frame = 0; frame < 120; frame++)
        {
            if (AssistiveSupport.activeHierarchy.rootNodes.Any(node => node.label == introDescription))
            {
                break;
            }

            yield return null;
        }

        Assert.That(
            AssistiveSupport.activeHierarchy.rootNodes.Select(node => node.label),
            Is.EqualTo(new[] { introDescription, "继续剧情" }),
            "没有台词的开场页应先读画面描述，再提供一次继续。");

        AccessibilityNode introContinue = AssistiveSupport.activeHierarchy.rootNodes.Last();
        Assert.That(((Func<bool>)invokedField.GetValue(introContinue)).Invoke(), Is.True);

        string firstImageDescription = "画面描述：雨中手腕上的智能手表特写，屏幕显示2044年10月18日星期五15点38分。";
        string[] firstPage =
        {
            firstImageDescription,
            "这所极度强调时间与效率的高中",
            "今天罕见地推迟了几分钟放学",
            "依然繁重的课业催得你内心焦灼",
            "就像往常一样",
            "沿着XR眼镜的可视化路线规划，加快脚步回家",
            "继续剧情"
        };
        for (int frame = 0; frame < 60; frame++)
        {
            if (AssistiveSupport.activeHierarchy.rootNodes.Any(node =>
                    node.label == firstImageDescription))
            {
                break;
            }

            yield return null;
        }

        Assert.That(
            AssistiveSupport.activeHierarchy.rootNodes.Select(node => node.label),
            Is.EqualTo(firstPage),
            "同一幅画的所有台词应排在画面描述之后，末尾只有一次继续。");
        Assert.That(
            AssistiveSupport.activeHierarchy.rootNodes.Take(firstPage.Length - 1)
                .All(node => node.role == AccessibilityRole.StaticText),
            Is.True,
            "画面描述和台词只能浏览，不应变成操作按钮。");

        float holdPageUntil = Time.realtimeSinceStartup + 0.5f;
        while (Time.realtimeSinceStartup < holdPageUntil)
        {
            yield return null;
        }
        Assert.That(
            AssistiveSupport.activeHierarchy.rootNodes.First().label,
            Is.EqualTo(firstImageDescription),
            "读屏模式应停留在当前画面，不能在用户阅读时按动画计时自动推进。");

        AccessibilityNode firstPageContinue = AssistiveSupport.activeHierarchy.rootNodes.Last();
        Assert.That(((Func<bool>)invokedField.GetValue(firstPageContinue)).Invoke(), Is.True);
        string secondImageDescription = "画面描述：俯视雨中的街道，行人沿不同方向匆忙赶路。";
        for (int frame = 0; frame < 60; frame++)
        {
            if (AssistiveSupport.activeHierarchy.rootNodes.Any(node =>
                    node.label == secondImageDescription))
            {
                break;
            }

            yield return null;
        }

        Assert.That(
            AssistiveSupport.activeHierarchy.rootNodes.First().label,
            Is.EqualTo(secondImageDescription),
            "激活页末继续后应一次进入下一幅画，并从新画面描述开始。");

        object videoManager = FindGameComponent("VideoManager");
        object dialogSystem = FindGameComponent("DialogSystem");
        object uiManager = FindGameComponent("UIManager");
        object accessibility = FindGameComponent("FactoryEscapeAccessibility");
        Assert.That(uiManager, Is.Not.Null, "游戏场景应存在节点提示文本区。");

        ((Transform)videoManager.GetType().GetField("cutSceneUIPanel").GetValue(videoManager))
            .gameObject.SetActive(false);
        ((GameObject)dialogSystem.GetType().GetField("dialogPanel").GetValue(dialogSystem))
            .SetActive(false);
        ((Transform)dialogSystem.GetType().GetField("AIDialogPanel").GetValue(dialogSystem))
            .gameObject.SetActive(false);
        uiManager.GetType().GetField("UIShow").SetValue(uiManager, false);
        uiManager.GetType().GetMethod("DisplayNodeText").Invoke(
            uiManager, new object[] { "测试页面提示" });
        accessibility.GetType().GetMethod(
            "QueueRefresh", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(
            accessibility, null);

        for (int frame = 0; frame < 30; frame++)
        {
            if (AssistiveSupport.activeHierarchy.rootNodes.Any(node => node.label == "测试页面提示") &&
                AssistiveSupport.activeHierarchy.rootNodes.Any(node => node.label == "存档并退出"))
            {
                break;
            }

            yield return null;
        }

        Assert.That(
            AssistiveSupport.activeHierarchy.rootNodes.Any(node =>
                node.label == "测试页面提示" && node.role == AccessibilityRole.StaticText),
            Is.True,
            "节点页面的可见提示文字也应进入读屏顺序。");
        Assert.That(
            AssistiveSupport.activeHierarchy.rootNodes.Any(node => node.label == "游戏"),
            Is.False,
            "游戏场景不应保留没有内容对应的空标题焦点。");
        AccessibilityNode[] saveAndQuitNodes = AssistiveSupport.activeHierarchy.rootNodes
            .Where(node => node.label == "存档并退出").ToArray();
        Assert.That(saveAndQuitNodes, Has.Length.EqualTo(1),
            $"节点页面应只提供一个存档并退出焦点。实际焦点：{string.Join("、", AssistiveSupport.activeHierarchy.rootNodes.Select(node => node.label))}");
        Assert.That(saveAndQuitNodes[0].role, Is.EqualTo(AccessibilityRole.Button),
            "存档并退出必须是可显式激活的按钮。");
        Assert.That(invokedField.GetValue(saveAndQuitNodes[0]), Is.Not.Null,
            "存档并退出必须绑定显式激活动作。");
        Assert.That(AssistiveSupport.activeHierarchy.rootNodes.Last().label,
            Is.EqualTo("存档并退出"),
            "存档并退出应固定在节点调查焦点列表末尾，确保顺序滑动可以到达。");
        Assert.That(
            AssistiveSupport.activeHierarchy.rootNodes.Any(node => node.label == "暂停"),
            Is.False,
            "节点页面不应再暴露暂停按钮。");

        Type dialogType = dialogSystem.GetType();
        var names = (List<string>)dialogType.GetField(
            "nameList", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialogSystem);
        var texts = (List<string>)dialogType.GetField(
            "textList", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialogSystem);
        names.Clear();
        names.AddRange(new[] { "小明", "823" });
        texts.Clear();
        texts.AddRange(new[] { "第一句", "第二句" });
        ((GameObject)dialogType.GetField("dialogPanel").GetValue(dialogSystem)).SetActive(true);
        accessibility.GetType().GetMethod(
            "QueueRefresh", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(
            accessibility, null);

        string dialogDescription = "画面描述：人物对话画面，小明、823参与交谈。";
        for (int frame = 0; frame < 30; frame++)
        {
            if (AssistiveSupport.activeHierarchy.rootNodes.Any(node => node.label == dialogDescription))
            {
                break;
            }

            yield return null;
        }

        Assert.That(
            AssistiveSupport.activeHierarchy.rootNodes.Select(node => node.label),
            Is.EqualTo(new[] { dialogDescription, "小明：第一句", "823：第二句", "继续剧情" }),
            "普通对话也应先读人物画面，再读整组台词，末尾只继续一次。");

        AccessibilityNode dialogContinue = AssistiveSupport.activeHierarchy.rootNodes.Last();
        Assert.That(((Func<bool>)invokedField.GetValue(dialogContinue)).Invoke(), Is.True);
        Assert.That(
            ((GameObject)dialogType.GetField("dialogPanel").GetValue(dialogSystem)).activeSelf,
            Is.False,
            "激活对话页末继续后应一次结束整组对话。");
    }

    [UnityTest]
    public IEnumerator SecondaryWorkshopRoomsExposeInvestigationFocusOnArrival()
    {
        AssistiveSupport.screenReaderStatusOverride =
            AssistiveSupport.ScreenReaderStatusOverride.ForceEnabled;
        DestroyExistingGameManager();
        SceneManager.LoadScene("LoadScene");

        object gameManager = null;
        object gameMenu = null;
        for (int frame = 0; frame < 180; frame++)
        {
            gameManager = FindGameComponent("GameManager");
            gameMenu = FindGameComponent("GameMenu");
            if (gameManager != null && gameMenu != null)
            {
                break;
            }

            yield return null;
        }

        gameMenu.GetType().GetMethod("StartGame").Invoke(gameMenu, null);
        object builder = null;
        float timeout = Time.realtimeSinceStartup + 8f;
        while (Time.realtimeSinceStartup < timeout)
        {
            builder = FindGameComponent("NodeMapBuilder");
            bool mapReady = builder != null &&
                            ((System.Collections.IDictionary)builder.GetType()
                                .GetField("nodeHasCreated").GetValue(builder)).Count > 0;
            if (SceneManager.GetSceneByName("GameScene").isLoaded && mapReady &&
                gameManager.GetType().GetField("gameState").GetValue(gameManager)
                    .ToString() == "Playing")
            {
                break;
            }

            yield return null;
        }

        Assert.That(builder, Is.Not.Null);
        object uiManager = FindGameComponent("UIManager");
        object videoManager = FindGameComponent("VideoManager");
        object dialogSystem = FindGameComponent("DialogSystem");
        object accessibility = FindGameComponent("FactoryEscapeAccessibility");
        ((Transform)videoManager.GetType().GetField("cutSceneUIPanel").GetValue(videoManager))
            .gameObject.SetActive(false);
        ((GameObject)dialogSystem.GetType().GetField("dialogPanel").GetValue(dialogSystem))
            .SetActive(false);
        ((Transform)dialogSystem.GetType().GetField("AIDialogPanel").GetValue(dialogSystem))
            .gameObject.SetActive(false);
        uiManager.GetType().GetField("UIShow").SetValue(uiManager, false);

        Type managerType = gameManager.GetType();
        IList levels = (IList)managerType.GetField("nodeLevelSOs").GetValue(gameManager);
        object workshopLevel = levels[1];
        managerType.GetField("levelIndex").SetValue(gameManager, 1);
        managerType.GetMethod("InitializeReference",
                BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(gameManager, new[] { workshopLevel });
        IList graphs = (IList)workshopLevel.GetType().GetField("levelGraphs")
            .GetValue(workshopLevel);

        string[][] expectedRoomFocus =
        {
            new[] { "机房", "可以旋转的白板", "管理员工作台", "服务器机房", "锁上的门" },
            new[] { "主管办公室", "茶几", "沙发旁的柜子", "主管工位" }
        };

        for (int graphIndex = 1; graphIndex <= 2; graphIndex++)
        {
            builder.GetType().GetMethod("DeleteNodeMap").Invoke(builder, null);
            builder.GetType().GetMethod("GenerateNodeMap").Invoke(
                builder, new[] { graphs[graphIndex], (object)0 });
            for (int frame = 0; frame < 5; frame++)
            {
                yield return null;
            }

            accessibility.GetType().GetMethod(
                    "QueueRefresh", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(accessibility, null);
            for (int frame = 0; frame < 30; frame++)
            {
                string[] labels = AssistiveSupport.activeHierarchy.rootNodes
                    .Select(node => node.label).ToArray();
                if (expectedRoomFocus[graphIndex - 1].All(labels.Contains))
                {
                    break;
                }

                yield return null;
            }

            string[] actualLabels = AssistiveSupport.activeHierarchy.rootNodes
                .Select(node => node.label).ToArray();
            Assert.That(expectedRoomFocus[graphIndex - 1].All(actualLabels.Contains), Is.True,
                $"进入{expectedRoomFocus[graphIndex - 1][0]}后应直接出现首批调查焦点。实际焦点：{string.Join("、", actualLabels)}");
        }
    }

    [UnityTest]
    public IEnumerator EveryReachableItemInLaterLevelsReceivesAccessibilityFocus()
    {
        AssistiveSupport.screenReaderStatusOverride =
            AssistiveSupport.ScreenReaderStatusOverride.ForceEnabled;
        DestroyExistingGameManager();
        SceneManager.LoadScene("LoadScene");

        object gameManager = null;
        object gameMenu = null;
        for (int frame = 0; frame < 180; frame++)
        {
            gameManager = FindGameComponent("GameManager");
            gameMenu = FindGameComponent("GameMenu");
            if (gameManager != null && gameMenu != null)
            {
                break;
            }

            yield return null;
        }

        gameMenu.GetType().GetMethod("StartGame").Invoke(gameMenu, null);
        object builder = null;
        float timeout = Time.realtimeSinceStartup + 8f;
        while (Time.realtimeSinceStartup < timeout)
        {
            builder = FindGameComponent("NodeMapBuilder");
            bool mapReady = builder != null &&
                            ((System.Collections.IDictionary)builder.GetType()
                                .GetField("nodeHasCreated").GetValue(builder)).Count > 0;
            if (SceneManager.GetSceneByName("GameScene").isLoaded && mapReady &&
                gameManager.GetType().GetField("gameState").GetValue(gameManager)
                    .ToString() == "Playing")
            {
                break;
            }

            yield return null;
        }

        object uiManager = FindGameComponent("UIManager");
        object videoManager = FindGameComponent("VideoManager");
        object dialogSystem = FindGameComponent("DialogSystem");
        object accessibility = FindGameComponent("FactoryEscapeAccessibility");
        Type managerType = gameManager.GetType();
        Type accessibilityType = accessibility.GetType();
        MethodInfo getNodeLabel = accessibilityType.GetMethod(
            "GetNodeLabel", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo enterNodeScope = accessibilityType.GetMethod(
            "EnterNodeScope", BindingFlags.Static | BindingFlags.Public);
        IList levels = (IList)managerType.GetField("nodeLevelSOs").GetValue(gameManager);

        for (int levelIndex = 1; levelIndex < levels.Count; levelIndex++)
        {
            object level = levels[levelIndex];
            managerType.GetField("levelIndex").SetValue(gameManager, levelIndex);
            managerType.GetMethod("InitializeReference",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(gameManager, new[] { level });
            IList graphs = (IList)level.GetType().GetField("levelGraphs").GetValue(level);

            for (int graphIndex = 0; graphIndex < graphs.Count; graphIndex++)
            {
                builder.GetType().GetMethod("DeleteNodeMap").Invoke(builder, null);
                yield return null;
                builder.GetType().GetMethod("GenerateNodeMap").Invoke(
                    builder, new[] { graphs[graphIndex], (object)1 });
                yield return null;
                yield return null;

                ((Transform)videoManager.GetType().GetField("cutSceneUIPanel")
                    .GetValue(videoManager)).gameObject.SetActive(false);
                ((GameObject)dialogSystem.GetType().GetField("dialogPanel")
                    .GetValue(dialogSystem)).SetActive(false);
                ((Transform)dialogSystem.GetType().GetField("AIDialogPanel")
                    .GetValue(dialogSystem)).gameObject.SetActive(false);
                uiManager.GetType().GetField("UIShow").SetValue(uiManager, false);

                var nodes = ((System.Collections.IDictionary)builder.GetType()
                        .GetField("nodeHasCreated").GetValue(builder)).Values
                    .OfType<Component>().ToArray();
                Component entrance = null;
                foreach (Component node in nodes)
                {
                    node.gameObject.SetActive(true);
                    object nodeType = node.GetType().GetField("nodeType").GetValue(node);
                    if ((bool)nodeType.GetType().GetField("isEntrance").GetValue(nodeType))
                    {
                        entrance = node;
                    }
                }

                Assert.That(entrance, Is.Not.Null,
                    $"后续关卡 {levelIndex} 的节点图 {graphIndex} 必须有入口节点。");
                enterNodeScope.Invoke(null, new object[] { entrance });
                accessibilityType.GetMethod(
                        "QueueRefresh", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(accessibility, null);
                yield return null;
                yield return null;

                string[] actualLabels = AssistiveSupport.activeHierarchy.rootNodes
                    .Select(node => node.label).ToArray();
                string graphName = graphs[graphIndex].GetType().GetField("graphName")
                    .GetValue(graphs[graphIndex]) as string;
                foreach (IGrouping<string, Component> expectedGroup in nodes
                             .Where(node => node != entrance)
                             .Where(node =>
                             {
                                 object nodeType = node.GetType().GetField("nodeType").GetValue(node);
                                 return !(bool)nodeType.GetType().GetField("isExit").GetValue(nodeType);
                             })
                             .GroupBy(node => (string)getNodeLabel.Invoke(null, new object[] { node })))
                {
                    int actualCount = actualLabels.Count(label => label == expectedGroup.Key);
                    Assert.That(actualCount, Is.GreaterThanOrEqualTo(expectedGroup.Count()),
                        $"{graphName} 中的“{expectedGroup.Key}”应有 {expectedGroup.Count()} 个焦点，实际 {actualCount} 个。");
                }
            }
        }

        builder.GetType().GetMethod("DeleteNodeMap").Invoke(builder, null);
        if (SceneManager.GetSceneByName("GameScene").isLoaded)
        {
            yield return SceneManager.UnloadSceneAsync("GameScene");
        }
    }

    [Test]
    public void TextStoryModelSupportsOptionalArtworkLinearPagesAndChoices()
    {
        Type gameType = Type.GetType("TextAdventureGameSO, Assembly-CSharp");
        Type storyType = Type.GetType("TextAdventureStory, Assembly-CSharp");
        Type chapterType = Type.GetType("TextAdventureChapter, Assembly-CSharp");
        Type pageType = Type.GetType("TextAdventurePage, Assembly-CSharp");
        Type lineType = Type.GetType("TextAdventureLine, Assembly-CSharp");
        Type choiceType = Type.GetType("TextAdventureChoice, Assembly-CSharp");
        Type validatorType = Type.GetType("TextAdventureGameValidator, Assembly-CSharp");
        Type sessionType = Type.GetType("TextAdventureSession, Assembly-CSharp");
        Type catalogType = Type.GetType("TextAdventureCatalogSO, Assembly-CSharp");
        Type catalogValidatorType = Type.GetType("TextAdventureCatalogValidator, Assembly-CSharp");
        Assert.That(new[] { gameType, storyType, chapterType, pageType, lineType,
            choiceType, validatorType, sessionType, catalogType, catalogValidatorType }
            .All(type => type != null), Is.True);

        var game = ScriptableObject.CreateInstance(gameType);
        ScriptableObject secondGame = null;
        ScriptableObject catalog = null;
        try
        {
            gameType.GetField("schemaVersion").SetValue(game, 1);
            gameType.GetField("gameId").SetValue(game, "template-test");
            gameType.GetField("displayName").SetValue(game, "模板测试");
            gameType.GetField("protagonistName").SetValue(game, "测试者");
            gameType.GetField("mode").SetValue(game,
                Enum.Parse(gameType.GetField("mode").FieldType, "TextStory"));

            object story = Activator.CreateInstance(storyType);
            storyType.GetField("firstPageId").SetValue(story, "intro");
            IList chapters = (IList)Activator.CreateInstance(
                storyType.GetField("chapters").FieldType);
            object chapter = Activator.CreateInstance(chapterType);
            chapterType.GetField("id").SetValue(chapter, "chapter1");
            chapterType.GetField("title").SetValue(chapter, "第一章");
            chapters.Add(chapter);
            storyType.GetField("chapters").SetValue(story, chapters);

            IList pages = (IList)Activator.CreateInstance(storyType.GetField("pages").FieldType);
            object intro = CreateTextStoryPage(pageType, lineType,
                "intro", "开场", "没有图片的文字场景。", "choice");
            object choicePage = CreateTextStoryPage(pageType, lineType,
                "choice", "选择", "主角需要做出决定。", string.Empty);
            IList choices = (IList)pageType.GetField("choices").GetValue(choicePage);
            object choice = Activator.CreateInstance(choiceType);
            choiceType.GetField("id").SetValue(choice, "go");
            choiceType.GetField("label").SetValue(choice, "继续前进");
            choiceType.GetField("nextPageId").SetValue(choice, "ending");
            choices.Add(choice);
            object ending = CreateTextStoryPage(pageType, lineType,
                "ending", "结局", "道路尽头亮起一盏灯。", string.Empty);
            pages.Add(intro);
            pages.Add(choicePage);
            pages.Add(ending);
            storyType.GetField("pages").SetValue(story, pages);
            gameType.GetField("story").SetValue(game, story);

            IList errors = (IList)validatorType.GetMethod("Validate").Invoke(null, new object[] { game });
            Assert.That(errors.Count, Is.EqualTo(0), string.Join("；", errors.Cast<string>()));
            Assert.That(pageType.GetField("artwork").GetValue(intro), Is.Null,
                "没有图片时仍应是有效剧情页。");

            object session = Activator.CreateInstance(sessionType, new object[] { game });
            object[] startArguments = { null };
            Assert.That((bool)sessionType.GetMethod("Start").Invoke(session, startArguments), Is.True);
            object[] continueArguments = { null };
            Assert.That((bool)sessionType.GetMethod("Continue").Invoke(session, continueArguments), Is.True);
            object[] choiceArguments = { "go", null };
            Assert.That((bool)sessionType.GetMethod("Choose").Invoke(session, choiceArguments), Is.True);
            object currentPage = sessionType.GetProperty("CurrentPage").GetValue(session);
            Assert.That(pageType.GetField("id").GetValue(currentPage), Is.EqualTo("ending"));

            secondGame = UnityEngine.Object.Instantiate(game);
            gameType.GetField("gameId").SetValue(secondGame, "template-test-2");
            gameType.GetField("displayName").SetValue(secondGame, "第二个子游戏");
            catalog = ScriptableObject.CreateInstance(catalogType);
            IList games = (IList)catalogType.GetField("games").GetValue(catalog);
            games.Add(game);
            games.Add(secondGame);
            IList catalogErrors = (IList)catalogValidatorType.GetMethod("Validate")
                .Invoke(null, new object[] { catalog });
            Assert.That(catalogErrors.Count, Is.EqualTo(0),
                string.Join("；", catalogErrors.Cast<string>()));
            string firstSave = (string)gameType.GetProperty("SaveProfileName").GetValue(game);
            string secondSave = (string)gameType.GetProperty("SaveProfileName").GetValue(secondGame);
            Assert.That(firstSave, Is.Not.EqualTo(secondSave),
                "同一 App 内的不同子游戏必须使用不同存档。");
        }
        finally
        {
            if (catalog != null)
            {
                UnityEngine.Object.DestroyImmediate(catalog);
            }
            if (secondGame != null)
            {
                UnityEngine.Object.DestroyImmediate(secondGame);
            }
            UnityEngine.Object.DestroyImmediate(game);
        }
    }

    private static object CreateTextStoryPage(
        Type pageType,
        Type lineType,
        string id,
        string title,
        string description,
        string nextPageId)
    {
        object page = Activator.CreateInstance(pageType);
        pageType.GetField("id").SetValue(page, id);
        pageType.GetField("chapterId").SetValue(page, "chapter1");
        pageType.GetField("title").SetValue(page, title);
        pageType.GetField("visualDescription").SetValue(page, description);
        pageType.GetField("nextPageId").SetValue(page, nextPageId);

        IList lines = (IList)Activator.CreateInstance(pageType.GetField("lines").FieldType);
        object line = Activator.CreateInstance(lineType);
        lineType.GetField("speaker").SetValue(line, "旁白");
        lineType.GetField("text").SetValue(line, "这是一段可以被看见和读出的文字。");
        lines.Add(line);
        pageType.GetField("lines").SetValue(page, lines);
        pageType.GetField("choices").SetValue(page,
            Activator.CreateInstance(pageType.GetField("choices").FieldType));
        return page;
    }

    private static object FindGameComponent(string typeName)
    {
        Type type = Type.GetType($"{typeName}, Assembly-CSharp");
        Assert.That(type, Is.Not.Null, $"未找到游戏组件类型 {typeName}。");
        return Resources.FindObjectsOfTypeAll(type)
            .OfType<Component>()
            .FirstOrDefault(component => component.gameObject.scene.isLoaded);
    }

    private static TMP_FontAsset GetSystemChineseFont()
    {
        Type providerType = Type.GetType("SystemChineseFontProvider, Assembly-CSharp");
        Assert.That(providerType, Is.Not.Null, "未找到系统中文字体提供器。");
        PropertyInfo fontProperty = providerType.GetProperty(
            "CurrentFont",
            BindingFlags.Public | BindingFlags.Static);
        Assert.That(fontProperty, Is.Not.Null);
        return fontProperty.GetValue(null) as TMP_FontAsset;
    }

    private static void DestroyExistingGameManager()
    {
        object gameManager = FindGameComponent("GameManager");
        if (gameManager is Component component)
        {
            UnityEngine.Object.DestroyImmediate(component.gameObject);
        }
    }
}
