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
    public void ReadableChineseFontKeepsQualityContrastAndGb2312Fallback()
    {
        TMP_FontAsset font = Resources.Load<TMP_FontAsset>(
            "Front/ChineseSubset/FactoryEscapeChineseReadable SDF");
        Assert.That(font, Is.Not.Null, "必须打入高清中文主字体。");
        Assert.That(font.faceInfo.pointSize, Is.GreaterThanOrEqualTo(90));
        Assert.That(font.atlasPadding, Is.GreaterThanOrEqualTo(9));
        Assert.That(font.atlasRenderMode, Is.EqualTo(GlyphRenderMode.SDF32));
        Assert.That(font.material.shader.name, Is.EqualTo("TextMeshPro/Mobile/Distance Field"),
            "Android 中文字体必须使用已在真机验证可绘制完整字形的移动端 SDF 材质。");
        Assert.That(
            font.material.GetFloat(ShaderUtilities.ID_OutlineWidth),
            Is.GreaterThanOrEqualTo(0.1f),
            "浅色正文必须有稳定的深色描边以保证复杂背景上的局部对比度。");

        string requiredCharacters = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Editor",
            "FontOptimization",
            "FactoryEscapeRequiredCharacterSet.txt"));
        Assert.That(font.HasCharacters(requiredCharacters, out List<char> missing), Is.True,
            $"高清主字体缺少 {missing?.Count ?? 0} 个项目字符。");
        Assert.That(font.fallbackFontAssetTable, Has.Count.EqualTo(1));
        Assert.That(
            font.fallbackFontAssetTable[0].material.shader.name,
            Is.EqualTo("TextMeshPro/Mobile/Distance Field"),
            "动态中文后备字体必须与主字体使用相同的 Android 兼容材质。");
        Assert.That(
            font.fallbackFontAssetTable[0].atlasPopulationMode,
            Is.EqualTo(AtlasPopulationMode.Dynamic),
            "GB2312 后备字体应按需生成，不能再次挤入低精度静态主图集。");
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
    public void AndroidBuildAllowsOnlyLandscapeOrientations()
    {
        string source = File.ReadAllText(Path.Combine(
            Application.dataPath, "Editor", "FactoryEscapeAutomation.cs"));
        StringAssert.Contains(
            "PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;", source);
        StringAssert.Contains("PlayerSettings.allowedAutorotateToPortrait = false;", source);
        StringAssert.Contains("PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;", source);
        StringAssert.Contains("PlayerSettings.allowedAutorotateToLandscapeLeft = true;", source);
        StringAssert.Contains("PlayerSettings.allowedAutorotateToLandscapeRight = true;", source);
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

        TMP_FontAsset font = Resources.Load<TMP_FontAsset>(
            "Front/ChineseSubset/FactoryEscapeChineseReadable SDF");
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
        StringAssert.Contains("if (!resultReady)", source);
        StringAssert.Contains("if (resultReady && ai != null && button == ai.send_button)", source);
        StringAssert.Contains("AddAction(items, \"ai-result-continue\", \"继续剧情\"", source);
        StringAssert.Contains("QueueRefresh();", source);
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
    public IEnumerator MainMenuExposesOnlyFlatAccessibleActions()
    {
        AssistiveSupport.screenReaderStatusOverride =
            AssistiveSupport.ScreenReaderStatusOverride.ForceEnabled;
        DestroyExistingGameManager();
        SceneManager.LoadScene("LoadScene");

        for (int frame = 0; frame < 120; frame++)
        {
            AccessibilityHierarchy current = AssistiveSupport.activeHierarchy;
            if (current != null &&
                current.rootNodes.Select(node => node.label).SequenceEqual(
                    new[] { "开始游戏", "继续游戏", "选项", "退出游戏" }))
            {
                break;
            }

            yield return null;
        }

        AccessibilityHierarchy hierarchy = AssistiveSupport.activeHierarchy;
        Assert.That(hierarchy, Is.Not.Null, "启用读屏后应创建无障碍层级。");
        Assert.That(
            hierarchy.rootNodes.Select(node => node.label),
            Is.EqualTo(new[] { "开始游戏", "继续游戏", "选项", "退出游戏" }),
            "主菜单应只暴露四个可直接激活的菜单项。");
        Assert.That(
            hierarchy.rootNodes.All(node => node.role == AccessibilityRole.Button),
            Is.True,
            "主菜单的所有焦点都必须是可操作菜单项。");

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
            Is.EqualTo(new[] { "音乐音量", "音效音量", "返回主菜单" }),
            "激活选项后应进入可滑动操作的选项菜单。");

        AccessibilityNode backNode = AssistiveSupport.activeHierarchy.rootNodes
            .Single(node => node.label == "返回主菜单");
        Assert.That(((Func<bool>)invokedField.GetValue(backNode)).Invoke(), Is.True);
        for (int frame = 0; frame < 30; frame++)
        {
            if (AssistiveSupport.activeHierarchy.rootNodes.Any(node => node.label == "开始游戏"))
            {
                break;
            }

            yield return null;
        }

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

        string dialogDescription =
            "画面描述：人物对话画面，小明、823参与交谈，当前说话者的立绘高亮。";
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

    private static object FindGameComponent(string typeName)
    {
        Type type = Type.GetType($"{typeName}, Assembly-CSharp");
        Assert.That(type, Is.Not.Null, $"未找到游戏组件类型 {typeName}。");
        return Resources.FindObjectsOfTypeAll(type)
            .OfType<Component>()
            .FirstOrDefault(component => component.gameObject.scene.isLoaded);
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
