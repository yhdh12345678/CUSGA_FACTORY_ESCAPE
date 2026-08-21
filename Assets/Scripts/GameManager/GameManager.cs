using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Accessibility;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[System.Serializable]
public enum GameState
{
    Start,
    Generating,
    Playing,
    Pause,
    Result,
    Fail,
}

public class GameManager : SingletonMonobehaviour<GameManager>
{
    private const int SaveVersion = 2;
    private const int TextStorySaveVersion = 1;
    private const int InkStorySaveVersion = 1;
    private const string ProgressProfileName = "GameProgress";

    [Header("游戏内容")]
    [Tooltip("同一个 App 内可选择的全部子游戏")]
    public TextAdventureCatalogSO gameCatalog;
    [Tooltip("当前选中的子游戏；启动时默认使用目录第一项")]
    public TextAdventureGameSO gameDefinition;
    private TextAdventureSession textStorySession;
    private InkAdventureSession inkStorySession;

    [Header("AI参数")]
    [Tooltip("AI当前焦虑值")]
    public float currentAnxiety;
    [Tooltip("AI的最大焦虑值")]
    public float maxAnxiety;
    [Tooltip("AI解锁成功条件(胜利概率)")]
    public float rate;
    [SerializeField] private int previousChapterBot = 0;

    [Space(10)]
    [Header("弹出动画参数")]
    [Tooltip("弹出动画的持续时间")]
    public float tweenDuring = 0.2f;// 弹出持续时间
    [Tooltip("弹出动画的弹出距离")]
    public float popUpForce = 3;// 弹出距离

    [Space(10)]
    [Header("节点图参数")]
    [Tooltip("所需生成节点图列表")]
    public List<NodeLevelSO> nodeLevelSOs;
    [Tooltip("进入对应索引节点图的次数")]
    private List<int> enterNodeGraphTimesList = new List<int>();
    public int levelIndex = -1;// 关卡索引
    [SerializeField] private int graphIndex = 0;// 节点图索引
    private List<List<string>> nodeIdsInGraph = new List<List<string>>(); // 对应节点图索引的节点ID列表，用于存取节点状态数据
    [SerializeField] public bool isGettingNextLevel = false;
    Coroutine ChangeNodeGraph;
    Coroutine GetNextLevel;
    Coroutine ResultCoroutine;
    Coroutine ChangeScene;
    Coroutine BackGameSceneFromOther;
    private bool saveAndQuitStarted;
    private bool restoredFromSave;
    private bool sceneTransitionInProgress;

    [Space(10)]
    [Header("场景过度")]
    public CanvasGroup canvasGroup;

    [Space(10)]
    [Header("游戏状态参数")]
    public GameState gameState = GameState.Start;
    [HideInInspector] public int level1GetResultTimes = 0;
    [HideInInspector] public bool haveNodeDrag = false;

    [Space(10)]
    [Header("游戏结局过场动画")]
    public List<CutSceneCell> winCutScene;
    public List<CutSceneCell> fakeCutScene;

    override protected void Awake() {
        base.Awake();
        InitializeGameCatalog();
        InitializeGameDefinition();
#if UNITY_STANDALONE
        Screen.SetResolution(1920, 1080, true);
#endif
        SceneManager.LoadScene("MainMenu",LoadSceneMode.Additive);
        StartCoroutine(Fade(1,0,0.8f,Color.black));
        DontDestroyOnLoad(gameObject);
    }
    
    private void OnEnable() {
        StaticEventHandler.OnGetNextNodeLevel += StaticEventHandler_OnGetNextNodeLevel;

        StaticEventHandler.OnGetResult += StaticEventHandler_OnGetResult;
    }

    private void OnDisable() {
        StaticEventHandler.OnGetNextNodeLevel -= StaticEventHandler_OnGetNextNodeLevel;

        StaticEventHandler.OnGetResult -= StaticEventHandler_OnGetResult;
    }

    private void InitializeGameDefinition()
    {
        if (gameDefinition == null)
        {
            return;
        }

        List<string> errors = TextAdventureGameValidator.Validate(gameDefinition);
        if (errors.Count > 0)
        {
            Debug.LogError($"游戏内容校验失败：\n{string.Join("\n", errors)}");
            return;
        }

        if (gameDefinition.mode == TextAdventureGameMode.LegacyNodeAdventure)
        {
            nodeLevelSOs = new List<NodeLevelSO>(gameDefinition.legacyLevels);
            if (gameDefinition.primaryEnding.Count > 0)
            {
                winCutScene = new List<CutSceneCell>(gameDefinition.primaryEnding);
            }
            if (gameDefinition.alternateEnding.Count > 0)
            {
                fakeCutScene = new List<CutSceneCell>(gameDefinition.alternateEnding);
            }
        }

        textStorySession = null;
        inkStorySession = null;
    }

    private void InitializeGameCatalog()
    {
        if (gameCatalog == null)
        {
            return;
        }

        List<string> errors = TextAdventureCatalogValidator.Validate(gameCatalog);
        if (errors.Count > 0)
        {
            Debug.LogError($"子游戏目录校验失败：\n{string.Join("\n", errors)}");
            return;
        }

        if (gameDefinition == null || !gameCatalog.games.Contains(gameDefinition))
        {
            gameDefinition = gameCatalog.games[0];
        }
    }

    public IReadOnlyList<TextAdventureGameSO> AvailableGames => gameCatalog == null
        ? gameDefinition == null
            ? Array.Empty<TextAdventureGameSO>()
            : new[] { gameDefinition }
        : gameCatalog.games;

    public bool SelectGame(TextAdventureGameSO game)
    {
        bool isAvailable = gameCatalog == null
            ? game == gameDefinition
            : gameCatalog.games.Contains(game);
        if (sceneTransitionInProgress || game == null || !isAvailable)
        {
            return false;
        }

        gameDefinition = game;
        gameState = GameState.Start;
        restoredFromSave = false;
        InitializeGameDefinition();
        return true;
    }

    public bool IsTextStoryMode => gameDefinition != null &&
        gameDefinition.mode == TextAdventureGameMode.TextStory;
    public bool IsInkStoryMode => gameDefinition != null &&
        gameDefinition.mode == TextAdventureGameMode.InkStory;
    public bool IsDarkRoomMode => gameDefinition != null &&
        gameDefinition.mode == TextAdventureGameMode.DarkRoom;
    public bool IsNarrativeStoryMode => IsTextStoryMode || IsInkStoryMode;
    public string DisplayName => string.IsNullOrWhiteSpace(gameDefinition?.displayName)
        ? "抓住未尽的余晖"
        : gameDefinition.displayName;
    public TextAdventurePage CurrentTextStoryPage => textStorySession?.CurrentPage;
    public InkAdventurePage CurrentInkStoryPage => inkStorySession?.CurrentPage;
    private int SkyUiLevelIndex => gameDefinition != null &&
        gameDefinition.mode == TextAdventureGameMode.LegacyNodeAdventure
            ? gameDefinition.skyUiLevelIndex
            : 8;
    public string CurrentTextStoryChapterTitle
    {
        get
        {
            string chapterId = CurrentTextStoryPage?.chapterId;
            return gameDefinition?.story?.chapters?.Find(chapter =>
                chapter != null && chapter.id == chapterId)?.title ?? string.Empty;
        }
    }
    private string ActiveProgressProfileName => gameDefinition == null
        ? ProgressProfileName
        : gameDefinition.SaveProfileName;
    public bool HasSavedGame => IsDarkRoomMode
        ? DarkRoomApp.HasSavedGame
        : SaveManager.Exists(ActiveProgressProfileName);
    public bool IsSaveAndQuitStarted => saveAndQuitStarted;
    public bool IsSceneTransitionInProgress => sceneTransitionInProgress;

    private void OnApplicationPause(bool paused)
    {
        if (!paused || saveAndQuitStarted || sceneTransitionInProgress ||
            !SceneManager.GetSceneByName("GameScene").isLoaded)
        {
            return;
        }

        if (TrySaveCurrentProgress(out string errorMessage))
        {
            Announce("进度已自动保存");
        }
        else
        {
            Debug.LogWarning($"Automatic save skipped: {errorMessage}");
        }
    }

    public void StartNewGame()
    {
        if (IsDarkRoomMode)
        {
            DarkRoomApp.DeleteSavedGame();
            gameState = GameState.Start;
            restoredFromSave = false;
            return;
        }

        if (IsInkStoryMode)
        {
            SaveManager.Delete(gameDefinition.SaveProfileName);
            inkStorySession = CreateInkStorySession();
            if (!inkStorySession.Start(out string storyError))
            {
                Debug.LogError(storyError);
                Announce(storyError);
            }
            gameState = GameState.Start;
            restoredFromSave = false;
            return;
        }

        if (IsTextStoryMode)
        {
            SaveManager.Delete(gameDefinition.SaveProfileName);
            textStorySession = new TextAdventureSession(gameDefinition);
            if (!textStorySession.Start(out string storyError))
            {
                Debug.LogError(storyError);
            }
            gameState = GameState.Start;
            restoredFromSave = false;
            return;
        }

        DeleteSavedGame();
        levelIndex = 0;
        graphIndex = 0;
        enterNodeGraphTimesList.Clear();
        nodeIdsInGraph.Clear();
        currentAnxiety = 0;
        maxAnxiety = 0;
        rate = 0;
        previousChapterBot = 0;
        level1GetResultTimes = 0;
        haveNodeDrag = false;
        isGettingNextLevel = false;
        restoredFromSave = false;
    }

    public bool TryLoadSavedGame(out string errorMessage)
    {
        if (IsDarkRoomMode)
        {
            errorMessage = HasSavedGame ? string.Empty : "没有可继续的游戏";
            return HasSavedGame;
        }

        if (IsInkStoryMode)
        {
            return TryLoadInkStory(out errorMessage);
        }

        if (IsTextStoryMode)
        {
            return TryLoadTextStory(out errorMessage);
        }

        IReadOnlyList<SaveProfile<GameProgressState>> candidates =
            SaveManager.LoadCandidates<GameProgressState>(ActiveProgressProfileName);
        if (candidates.Count == 0)
        {
            errorMessage = HasSavedGame ? "存档损坏，无法继续游戏" : "没有可继续的游戏";
            return false;
        }

        string candidateError = "存档损坏，无法继续游戏";
        for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
        {
            SaveProfile<GameProgressState> candidate = candidates[candidateIndex];
            GameProgressState save = candidate.saveData;
            if (!TryValidateSavedGame(save, out candidateError))
            {
                continue;
            }

            try
            {
                RestoreWorkingNodeProfiles(save);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Unable to restore save candidate: {exception.Message}");
                candidateError = "存档内容无法恢复";
                continue;
            }

            if (candidateIndex > 0)
            {
                try
                {
                    SaveManager.SaveOrReplace(
                        new SaveProfile<GameProgressState>(ActiveProgressProfileName, save));
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"Unable to promote recovered save: {exception.Message}");
                }
            }

            ApplySavedGame(save);
            errorMessage = string.Empty;
            return true;
        }

        errorMessage = candidateError;
        return false;
    }

    public bool ContinueTextStory()
    {
        string errorMessage = "剧情尚未准备好";
        if (!IsTextStoryMode || textStorySession == null ||
            !textStorySession.Continue(out errorMessage))
        {
            Announce(errorMessage ?? "剧情尚未准备好");
            return false;
        }

        FactoryEscapeAccessibility.RefreshScreen("story-description");
        return true;
    }

    public bool LaunchDarkRoom()
    {
        if (!IsDarkRoomMode || DarkRoomApp.Instance != null)
        {
            return false;
        }

        soundManager.Instance?.StopMusic();
        FactoryEscapeAccessibility.EnterEmbeddedGame();
        DarkRoomApp.Launch(ReturnFromDarkRoom);
        gameState = GameState.Playing;
        return true;
    }

    private void ReturnFromDarkRoom()
    {
        gameState = GameState.Start;
        FactoryEscapeAccessibility.ExitEmbeddedGame("进度已保存");
        soundManager.Instance?.PlayMusicInFade("Theme");
    }

    public bool ChooseTextStory(string choiceId)
    {
        string errorMessage = "剧情尚未准备好";
        if (!IsTextStoryMode || textStorySession == null ||
            !textStorySession.Choose(choiceId, out errorMessage))
        {
            Announce(errorMessage ?? "剧情尚未准备好");
            return false;
        }

        FactoryEscapeAccessibility.RefreshScreen("story-description");
        return true;
    }

    public bool ChooseInkStory(int choiceIndex)
    {
        string errorMessage = "剧情尚未准备好";
        if (!IsInkStoryMode || inkStorySession == null ||
            !inkStorySession.Choose(choiceIndex, out errorMessage))
        {
            Announce(errorMessage ?? "剧情尚未准备好");
            return false;
        }

        soundManager.Instance?.PlaySFX("Selected");
        FactoryEscapeAccessibility.RefreshScreen("story-description");
        return true;
    }

    private bool TryLoadInkStory(out string errorMessage)
    {
        IReadOnlyList<SaveProfile<InkStoryProgressState>> candidates =
            SaveManager.LoadCandidates<InkStoryProgressState>(gameDefinition.SaveProfileName);
        foreach (SaveProfile<InkStoryProgressState> candidate in candidates)
        {
            InkStoryProgressState save = candidate.saveData;
            if (save == null || save.version != InkStorySaveVersion ||
                save.gameId != gameDefinition.gameId)
            {
                continue;
            }

            InkAdventureSession session = CreateInkStorySession();
            if (!session.Restore(save.storyStateJson, save.page, out errorMessage))
            {
                continue;
            }

            inkStorySession = session;
            gameState = GameState.Playing;
            restoredFromSave = true;
            errorMessage = string.Empty;
            return true;
        }

        errorMessage = HasSavedGame
            ? "存档损坏，无法继续游戏"
            : "没有可继续的游戏";
        return false;
    }

    private bool TryLoadTextStory(out string errorMessage)
    {
        IReadOnlyList<SaveProfile<TextStoryProgressState>> candidates =
            SaveManager.LoadCandidates<TextStoryProgressState>(gameDefinition.SaveProfileName);
        foreach (SaveProfile<TextStoryProgressState> candidate in candidates)
        {
            TextStoryProgressState save = candidate.saveData;
            if (save == null || save.version != TextStorySaveVersion ||
                save.gameId != gameDefinition.gameId)
            {
                continue;
            }

            var session = new TextAdventureSession(gameDefinition);
            if (!session.Restore(save.pageId, out errorMessage))
            {
                continue;
            }

            textStorySession = session;
            gameState = GameState.Playing;
            restoredFromSave = true;
            errorMessage = string.Empty;
            return true;
        }

        errorMessage = HasSavedGame
            ? "存档损坏，无法继续游戏"
            : "没有可继续的游戏";
        return false;
    }

    private bool TryValidateSavedGame(GameProgressState save, out string errorMessage)
    {
        if (save == null || save.version < 1 || save.version > SaveVersion ||
            save.levelIndex < 0 || save.levelIndex >= nodeLevelSOs.Count)
        {
            errorMessage = "存档版本不兼容，无法继续游戏";
            return false;
        }

        if (save.version >= 2 && string.IsNullOrWhiteSpace(save.saveGeneration))
        {
            errorMessage = "存档内容不完整，无法继续游戏";
            return false;
        }

        if (float.IsNaN(save.currentAnxiety) || float.IsInfinity(save.currentAnxiety) ||
            float.IsNaN(save.maxAnxiety) || float.IsInfinity(save.maxAnxiety) ||
            float.IsNaN(save.rate) || float.IsInfinity(save.rate) ||
            save.maxAnxiety <= 0f || save.currentAnxiety < 0f ||
            save.currentAnxiety > save.maxAnxiety || save.rate < 0f || save.rate > 1f ||
            save.previousChapterBot < 0 || save.previousChapterBot > 3 ||
            save.level1GetResultTimes < 0)
        {
            errorMessage = "存档数值异常，无法继续游戏";
            return false;
        }

        int graphCount = nodeLevelSOs[save.levelIndex].levelGraphs.Count;
        if (save.graphIndex < 0 || save.graphIndex >= graphCount ||
            save.enterNodeGraphTimes == null || save.enterNodeGraphTimes.Count != graphCount ||
            save.nodeIdsInGraph == null || save.nodeIdsInGraph.Count != graphCount ||
            save.enterNodeGraphTimes.Any(times => times < 0) ||
            save.enterNodeGraphTimes[save.graphIndex] < 1)
        {
            errorMessage = "存档内容不完整，无法继续游戏";
            return false;
        }

        for (int graph = 0; graph < save.nodeIdsInGraph.Count; graph++)
        {
            List<string> nodeIds = save.nodeIdsInGraph[graph];
            if (nodeIds == null)
            {
                errorMessage = "存档内容不完整，无法继续游戏";
                return false;
            }

            var allowedNodeIds = new HashSet<string>(
                nodeLevelSOs[save.levelIndex].levelGraphs[graph].nodeList
                    .Where(node => node != null && !string.IsNullOrWhiteSpace(node.id))
                    .Select(node => node.id));
            if (nodeIds.Count != nodeIds.Distinct().Count() ||
                (save.enterNodeGraphTimes[graph] == 0 && nodeIds.Count != 0) ||
                (save.enterNodeGraphTimes[graph] > 0 && !allowedNodeIds.SetEquals(nodeIds)))
            {
                errorMessage = "存档内容不完整，无法继续游戏";
                return false;
            }

            foreach (string nodeId in nodeIds)
            {
                if (!allowedNodeIds.Contains(nodeId))
                {
                    errorMessage = "存档内容不完整，无法继续游戏";
                    return false;
                }

                string profileName = GetNodeProfileName(save.saveGeneration, nodeId);
                if (!SaveManager.TryLoad(profileName, out SaveProfile<NodeState> nodeProfile) ||
                    !IsValidNodeState(nodeProfile.saveData, allowedNodeIds))
                {
                    errorMessage = "存档内容不完整，无法继续游戏";
                    return false;
                }
            }
        }

        errorMessage = string.Empty;
        return true;
    }

    private static bool IsValidNodeState(NodeState state, HashSet<string> allowedNodeIds)
    {
        if (state == null || float.IsNaN(state.localPosition.x) || float.IsInfinity(state.localPosition.x) ||
            float.IsNaN(state.localPosition.y) || float.IsInfinity(state.localPosition.y) ||
            state.childNodeID == null || state.childNodeID.Count != state.childNodeID.Distinct().Count())
        {
            return false;
        }

        if (!string.IsNullOrEmpty(state.parentNodeID) && !allowedNodeIds.Contains(state.parentNodeID))
        {
            return false;
        }

        return state.childNodeID.All(allowedNodeIds.Contains);
    }

    private static string GetNodeProfileName(string saveGeneration, string nodeId)
    {
        return string.IsNullOrWhiteSpace(saveGeneration)
            ? nodeId
            : $"{saveGeneration}_{nodeId}";
    }

    private static void RestoreWorkingNodeProfiles(GameProgressState save)
    {
        if (string.IsNullOrWhiteSpace(save.saveGeneration))
        {
            return;
        }

        foreach (List<string> nodeIds in save.nodeIdsInGraph)
        {
            foreach (string nodeId in nodeIds)
            {
                string profileName = GetNodeProfileName(save.saveGeneration, nodeId);
                if (!SaveManager.TryLoad(profileName, out SaveProfile<NodeState> nodeProfile))
                {
                    throw new InvalidDataException($"Missing node profile {nodeId}.");
                }

                SaveManager.SaveOrReplace(new SaveProfile<NodeState>(nodeId, nodeProfile.saveData));
            }
        }
    }

    private void ApplySavedGame(GameProgressState save)
    {
        levelIndex = save.levelIndex;
        graphIndex = save.graphIndex;
        enterNodeGraphTimesList = new List<int>(save.enterNodeGraphTimes);
        nodeIdsInGraph = new List<List<string>>();
        foreach (List<string> nodeIds in save.nodeIdsInGraph)
        {
            nodeIdsInGraph.Add(new List<string>(nodeIds));
        }

        currentAnxiety = save.currentAnxiety;
        maxAnxiety = save.maxAnxiety;
        rate = save.rate;
        previousChapterBot = save.previousChapterBot;
        level1GetResultTimes = save.level1GetResultTimes;
        haveNodeDrag = false;
        isGettingNextLevel = false;
        gameState = GameState.Playing;
        restoredFromSave = true;
    }

    public void StartSaveAndQuit()
    {
        if (!saveAndQuitStarted)
        {
            StartCoroutine(SaveAndQuitCoroutine());
        }
    }

    private IEnumerator SaveAndQuitCoroutine()
    {
        saveAndQuitStarted = true;
        if (!TrySaveCurrentProgress(out string errorMessage))
        {
            Announce(errorMessage);
            UIManager.Instance?.DisplayNodeText(errorMessage);
            saveAndQuitStarted = false;
            yield break;
        }

        const string confirmation = "存档完成，正在退出游戏";
        if (UIManager.Instance != null)
        {
            UIManager.Instance.UIShow = true;
        }
        canvasGroup.interactable = false;
        UIManager.Instance?.DisplayNodeText(confirmation);
        Announce(confirmation);
        yield return new WaitForSecondsRealtime(0.75f);

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private bool TrySaveCurrentProgress(out string errorMessage)
    {
        if (IsInkStoryMode)
        {
            return TrySaveInkStoryProgress(out errorMessage);
        }

        if (IsTextStoryMode)
        {
            return TrySaveTextStoryProgress(out errorMessage);
        }

        string saveGeneration = null;
        List<string> generatedProfiles = new List<string>();
        bool progressCommitted = false;
        try
        {
            if (!SceneManager.GetSceneByName("GameScene").isLoaded ||
                gameState != GameState.Playing || isGettingNextLevel || haveNodeDrag ||
                UIManager.Instance == null || UIManager.Instance.UIShow ||
                levelIndex < 0 || levelIndex >= nodeLevelSOs.Count ||
                graphIndex < 0 || graphIndex >= nodeIdsInGraph.Count ||
                NodeMapBuilder.Instance == null)
            {
                errorMessage = "当前进度暂时无法保存";
                return false;
            }

            NodeMapBuilder.Instance.SaveNodeMap(nodeIdsInGraph[graphIndex]);
            var savedNodeIds = new List<List<string>>();
            foreach (List<string> nodeIds in nodeIdsInGraph)
            {
                savedNodeIds.Add(new List<string>(nodeIds));
            }

            saveGeneration = Guid.NewGuid().ToString("N");
            foreach (List<string> nodeIds in savedNodeIds)
            {
                foreach (string nodeId in nodeIds)
                {
                    if (!SaveManager.TryLoad(nodeId, out SaveProfile<NodeState> nodeProfile))
                    {
                        throw new InvalidDataException($"Missing working node profile {nodeId}.");
                    }

                    string generatedProfile = GetNodeProfileName(saveGeneration, nodeId);
                    SaveManager.SaveOrReplace(
                        new SaveProfile<NodeState>(generatedProfile, nodeProfile.saveData));
                    generatedProfiles.Add(generatedProfile);
                }
            }

            IReadOnlyList<SaveProfile<GameProgressState>> previousCandidates =
                SaveManager.LoadCandidates<GameProgressState>(ActiveProgressProfileName);

            var progress = new GameProgressState
            {
                version = SaveVersion,
                saveGeneration = saveGeneration,
                levelIndex = levelIndex,
                graphIndex = graphIndex,
                enterNodeGraphTimes = new List<int>(enterNodeGraphTimesList),
                nodeIdsInGraph = savedNodeIds,
                currentAnxiety = currentAnxiety,
                maxAnxiety = maxAnxiety,
                rate = rate,
                previousChapterBot = previousChapterBot,
                level1GetResultTimes = level1GetResultTimes
            };
            SaveManager.SaveOrReplace(new SaveProfile<GameProgressState>(ActiveProgressProfileName, progress));
            progressCommitted = true;

            foreach (SaveProfile<GameProgressState> obsoleteCandidate in previousCandidates.Skip(1))
            {
                try
                {
                    DeleteGenerationNodeProfiles(obsoleteCandidate.saveData);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"Unable to clean an obsolete save generation: {exception.Message}");
                }
            }

            errorMessage = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            if (!progressCommitted)
            {
                foreach (string generatedProfile in generatedProfiles)
                {
                    SaveManager.Delete(generatedProfile);
                }
            }

            Debug.LogException(exception);
            errorMessage = "存档失败，游戏没有退出";
            return false;
        }
    }

    private bool TrySaveTextStoryProgress(out string errorMessage)
    {
        if (!SceneManager.GetSceneByName("GameScene").isLoaded ||
            gameState != GameState.Playing || textStorySession?.CurrentPage == null)
        {
            errorMessage = "当前进度暂时无法保存";
            return false;
        }

        try
        {
            var progress = new TextStoryProgressState
            {
                version = TextStorySaveVersion,
                gameId = gameDefinition.gameId,
                pageId = textStorySession.CurrentPage.id
            };
            SaveManager.SaveOrReplace(new SaveProfile<TextStoryProgressState>(
                gameDefinition.SaveProfileName,
                progress));
            errorMessage = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            errorMessage = "存档失败，游戏没有退出";
            return false;
        }
    }

    private bool TrySaveInkStoryProgress(out string errorMessage)
    {
        if (!SceneManager.GetSceneByName("GameScene").isLoaded ||
            gameState != GameState.Playing || inkStorySession?.CurrentPage == null)
        {
            errorMessage = "当前进度暂时无法保存";
            return false;
        }

        try
        {
            var progress = new InkStoryProgressState
            {
                version = InkStorySaveVersion,
                gameId = gameDefinition.gameId,
                storyStateJson = inkStorySession.GetStateJson(),
                page = inkStorySession.CurrentPage
            };
            SaveManager.SaveOrReplace(new SaveProfile<InkStoryProgressState>(
                gameDefinition.SaveProfileName,
                progress));
            errorMessage = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            errorMessage = "存档失败，游戏没有退出";
            return false;
        }
    }

    private void DeleteSavedGame()
    {
        var nodeIdsToDelete = new HashSet<string>();
        foreach (SaveProfile<GameProgressState> saveProfile in
                 SaveManager.LoadCandidates<GameProgressState>(ActiveProgressProfileName))
        {
            GameProgressState progress = saveProfile.saveData;
            if (progress.nodeIdsInGraph == null)
            {
                continue;
            }

            DeleteGenerationNodeProfiles(progress);
            foreach (List<string> nodeIds in progress.nodeIdsInGraph)
            {
                if (nodeIds == null)
                {
                    continue;
                }

                foreach (string nodeId in nodeIds)
                {
                    nodeIdsToDelete.Add(nodeId);
                }
            }
        }

        foreach (List<string> nodeIds in nodeIdsInGraph)
        {
            foreach (string nodeId in nodeIds)
            {
                nodeIdsToDelete.Add(nodeId);
            }
        }

        foreach (string nodeId in nodeIdsToDelete)
        {
            SaveManager.Delete(nodeId);
        }
        SaveManager.Delete(ActiveProgressProfileName);
    }

    private static void DeleteGenerationNodeProfiles(GameProgressState progress)
    {
        if (progress?.nodeIdsInGraph == null || string.IsNullOrWhiteSpace(progress.saveGeneration))
        {
            return;
        }

        foreach (List<string> nodeIds in progress.nodeIdsInGraph)
        {
            if (nodeIds == null)
            {
                continue;
            }

            foreach (string nodeId in nodeIds)
            {
                SaveManager.Delete(GetNodeProfileName(progress.saveGeneration, nodeId));
            }
        }
    }

    private static void Announce(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            AssistiveSupport.notificationDispatcher.SendAnnouncement(message);
        }
    }

    private void StaticEventHandler_OnGetResult(GetResult result)
    {
        if (ResultCoroutine != null)
        {
            StopCoroutine(ResultCoroutine);
        }
        haveNodeDrag = false;
        ResultCoroutine = StartCoroutine(GetResult(result.cutSceneCells));
    }

    IEnumerator GetResult(List<CutSceneCell> cutSceneCells)
    {
        canvasGroup.blocksRaycasts = true;
        UIManager.Instance.UIShow = true;
        yield return StartCoroutine(Fade(0,1,0.8f,Color.black));

        soundManager.Instance.StopMusicInFade();
        soundManager.Instance.PlaySFX("ChangeScene");

        VideoManager.Instance.ShowCutScenes(cutSceneCells);

        canvasGroup.blocksRaycasts = false;
        yield return StartCoroutine(Fade(1,0,0.8f,Color.black));
    }

    private void StaticEventHandler_OnGetNextNodeLevel(GetNextNodeLevel args)
    {
        Debug.Log("触发进入下一关卡事件");
        if (GetNextLevel != null)
        {
            StopCoroutine(GetNextLevel);
        }
        haveNodeDrag = false;
        isGettingNextLevel = true;
        GetNextLevel = StartCoroutine(GetNextNodeLevel());
    }

    IEnumerator GetNextNodeLevel()
    {
        UIManager.Instance.UIShow = true;
        canvasGroup.blocksRaycasts = true;
        yield return StartCoroutine(Fade(0,1,0.8f,Color.black));

        soundManager.Instance.StopMusicInFade();
        soundManager.Instance.PlaySFX("ChangeScene");

        levelIndex++;
        gameState = GameState.Generating;

        UIManager.Instance.UIShow = false; 
        canvasGroup.blocksRaycasts = false;
        yield return StartCoroutine(Fade(1,0,0.8f,Color.black));
        isGettingNextLevel = false;
    }

    private void Update() {
        HandleGameState();
    }

    private void HandleGameState() {
        switch (gameState) {
            case GameState.Start:
                break;
            case GameState.Generating:
                if (IsInkStoryMode)
                {
                    if (inkStorySession == null)
                    {
                        inkStorySession = CreateInkStorySession();
                        if (!inkStorySession.Start(out string errorMessage))
                        {
                            Debug.LogError(errorMessage);
                            Announce(errorMessage);
                            break;
                        }
                    }

                    gameState = GameState.Playing;
                    PlayCommonNarrativeMusic();
                    FactoryEscapeAccessibility.RefreshScreen("story-description");
                    break;
                }

                if (IsTextStoryMode)
                {
                    if (textStorySession == null)
                    {
                        textStorySession = new TextAdventureSession(gameDefinition);
                        if (!textStorySession.Start(out string errorMessage))
                        {
                            Debug.LogError(errorMessage);
                            break;
                        }
                    }

                    gameState = GameState.Playing;
                    PlayCommonNarrativeMusic();
                    FactoryEscapeAccessibility.RefreshScreen("story-description");
                    break;
                }

                if (NodeMapBuilder.Instance == null) return;

                GetGenerateNodeMap();

                gameState = GameState.Playing;
                break;
            case GameState.Playing:
                break;
            case GameState.Pause:
                break;
            case GameState.Result:
                break;
            case GameState.Fail:
                break;
        }
    }

    /// <summary>
    /// 生成节点图并初始化相关参数
    /// </summary>
    private void GetGenerateNodeMap()
    {
        NodeLevelSO currentNodeLevel = nodeLevelSOs[levelIndex];
        NodeGraphSO currentNodeGraph = currentNodeLevel.levelGraphs[graphIndex];

        InitializeReference(currentNodeLevel);
        
        VideoManager.Instance.ShowCutScenes(currentNodeLevel.cutSceneList);
        if (currentNodeLevel.cutSceneList.Count == 0)
        {
            PlayCurrentLevelAudio();
        }

        NodeMapBuilder.Instance.DeleteNodeMap();
        NodeMapBuilder.Instance.GenerateNodeMap(currentNodeGraph,enterNodeGraphTimesList[graphIndex]);
        enterNodeGraphTimesList[graphIndex]++;
    }

    /// <summary>
    /// 初始化相关参数与函数回调
    /// </summary>
    private void InitializeReference(NodeLevelSO currentNodeLevel)
    {
        // 清理数据
        nodeIdsInGraph.Clear();
        enterNodeGraphTimesList.Clear();

        // 初始化存档
        foreach (NodeGraphSO nodeGraph in currentNodeLevel.levelGraphs)
        {
            nodeIdsInGraph.Add(new List<string>());
            enterNodeGraphTimesList.Add(0);
        }   

        // 按钮初始化
        MatchRightAndLeftNodeGraphName(currentNodeLevel);
        BindNodeGraphButtons();

        // 初始化AI
        maxAnxiety = currentNodeLevel.initialAnxietyValue;
        currentAnxiety = maxAnxiety;
        rate = currentNodeLevel.rate;
        if (currentNodeLevel.chapterBot != previousChapterBot) 
        {
            if (currentNodeLevel.chapterBot != 0)
            {
                tongyi_AI.instance.changeRobot(currentNodeLevel.chapterBot);
                Debug.Log($"Change Bot to {currentNodeLevel.chapterBot}");
            }
            previousChapterBot = currentNodeLevel.chapterBot;
        }

        // 初始化天空UI(针对最后一个关卡)
        if (levelIndex == SkyUiLevelIndex)
        {
            UIManager.Instance.SkyUI.gameObject.SetActive(true);
        }
        else
        {
            UIManager.Instance.SkyUI.gameObject.SetActive(false);
        }
    }

#region 左右两侧节点图切换按钮脚本
    /// <summary>
    /// 匹配左右两侧的切换节点图按钮
    /// </summary>
    private void MatchRightAndLeftNodeGraphName(NodeLevelSO currentNodeLevel)
    {
        if (currentNodeLevel.levelGraphs.Count > 2)
        {
            UIManager.Instance.rightNodeGraphButton.gameObject.SetActive(true);
            UIManager.Instance.leftNodeGraphButton.gameObject.SetActive(true);
        }
        else if (currentNodeLevel.levelGraphs.Count == 2)
        {
            UIManager.Instance.rightNodeGraphButton.gameObject.SetActive(true);
            UIManager.Instance.leftNodeGraphButton.gameObject.SetActive(false);
        }
        else
        {
            UIManager.Instance.rightNodeGraphButton.gameObject.SetActive(false);
            UIManager.Instance.leftNodeGraphButton.gameObject.SetActive(false);
        }

        int rightGraphIndex = graphIndex + 1;
        if (rightGraphIndex >= currentNodeLevel.levelGraphs.Count)
        {
            rightGraphIndex = 0;
        }
        UIManager.Instance.rightNodeGraphButton.GetComponentInChildren<TMP_Text>().text = currentNodeLevel.levelGraphs[rightGraphIndex].graphName;

        int leftGraphIndex = graphIndex - 1;
        if (leftGraphIndex < 0)
        {
            leftGraphIndex = currentNodeLevel.levelGraphs.Count - 1;
        }
        UIManager.Instance.leftNodeGraphButton.GetComponentInChildren<TMP_Text>().text = currentNodeLevel.levelGraphs[leftGraphIndex].graphName;
    }

    private void BindNodeGraphButtons()
    {
        Button rightButton = UIManager.Instance.rightNodeGraphButton.GetComponent<Button>();
        Button leftButton = UIManager.Instance.leftNodeGraphButton.GetComponent<Button>();
        rightButton.onClick.RemoveListener(ChangeToRightGraph);
        leftButton.onClick.RemoveListener(ChangeToLeftGraph);
        rightButton.onClick.AddListener(ChangeToRightGraph);
        leftButton.onClick.AddListener(ChangeToLeftGraph);
    }
    
    private void ChangeToRightGraph()
    {
        if (ChangeNodeGraph != null)
        {
            StopCoroutine(ChangeNodeGraph);
        }
        ChangeNodeGraph = StartCoroutine(ChangeToRightGraphCoroutine());
    }

    IEnumerator ChangeToRightGraphCoroutine()
    {
        canvasGroup.blocksRaycasts = true;
        yield return StartCoroutine(Fade(0,1,0.8f,Color.black));

        soundManager.Instance.PlaySFX("ChangeScene");

        GetRightNodeGraph();

        canvasGroup.blocksRaycasts = false;
        yield return StartCoroutine(Fade(1,0,0.8f,Color.black));
    }

    /// <summary>
    /// 向右转换节点图，index++
    /// </summary>
    private void GetRightNodeGraph() {
        NodeLevelSO currentNodeLevel = nodeLevelSOs[levelIndex];
        // 保存当前节点图的节点数据
        NodeMapBuilder.Instance.SaveNodeMap(nodeIdsInGraph[graphIndex]);

        // 节点图索引增加
        graphIndex++;
        if (graphIndex >= currentNodeLevel.levelGraphs.Count) {
            graphIndex = 0;
        }
        
        MatchRightAndLeftNodeGraphName(currentNodeLevel);

        // 生成当前索引的节点图
        NodeMapBuilder.Instance.DeleteNodeMap();
        NodeMapBuilder.Instance.GenerateNodeMap(
            currentNodeLevel.levelGraphs[graphIndex],
            enterNodeGraphTimesList[graphIndex]
            );
        enterNodeGraphTimesList[graphIndex]++;

        // 根据读取的节点状态数据重新载入节点图
        if (enterNodeGraphTimesList[graphIndex] != 1)
            NodeMapBuilder.Instance.LoadNodeMap(nodeIdsInGraph[graphIndex]);
    }

    private void ChangeToLeftGraph()
    {
        if (ChangeNodeGraph != null)
        {
            StopCoroutine(ChangeNodeGraph);
        }

        ChangeNodeGraph = StartCoroutine(ChangeToLeftGraphCoroutine());
    }

    IEnumerator ChangeToLeftGraphCoroutine()
    {
        canvasGroup.blocksRaycasts = true;
        yield return StartCoroutine(Fade(0,1,0.8f,Color.black));

        soundManager.Instance.PlaySFX("ChangeScene");

        GetLeftNodeGraph();

        canvasGroup.blocksRaycasts = false;
        yield return StartCoroutine(Fade(1,0,0.8f,Color.black));
    }

    /// <summary>
    /// 向左转换节点图，Index--
    /// </summary>
    private void GetLeftNodeGraph() {
        NodeLevelSO currentNodeLevel = nodeLevelSOs[levelIndex];
        // 保存当前节点图的节点数据
        NodeMapBuilder.Instance.SaveNodeMap(nodeIdsInGraph[graphIndex]);

        // 节点图索引减少
        graphIndex--;
        if (graphIndex < 0) {
            graphIndex = currentNodeLevel.levelGraphs.Count - 1;
        }

        MatchRightAndLeftNodeGraphName(currentNodeLevel);

        // 生成当前索引的节点图
        NodeMapBuilder.Instance.DeleteNodeMap();
        NodeMapBuilder.Instance.GenerateNodeMap(
            currentNodeLevel.levelGraphs[graphIndex],
            enterNodeGraphTimesList[graphIndex]
            );
        enterNodeGraphTimesList[graphIndex]++;
        
        // 根据读取的节点状态数据重新载入节点图
        if (enterNodeGraphTimesList[graphIndex] != 1)
            NodeMapBuilder.Instance.LoadNodeMap(nodeIdsInGraph[graphIndex]);
    }
#endregion

    /// <summary>
    /// 检查当前的焦虑值是否处在焦虑值比例内
    /// </summary>
    public bool CheckAnxietyValue()
    {
        if (currentAnxiety/maxAnxiety < rate)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    /// <summary>
    /// 淡入淡出
    /// </summary>
    IEnumerator Fade(float startFadeAlpha, float targetFadeAlpha, float fadeSeconds, Color backGround)
    {
        Image image = canvasGroup.GetComponent<Image>();
        image.color = backGround;

        if (FactoryEscapeAccessibility.ReduceMotion)
        {
            canvasGroup.alpha = targetFadeAlpha;
            yield break;
        }

        float time = 0;

        while (time <= fadeSeconds)
        {
            time += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(startFadeAlpha, targetFadeAlpha, time/fadeSeconds);
            yield return null;
        }
    }

    /// <summary>
    /// 切换场景及对应游戏状态
    /// </summary>
    public void StartChangeSceneCoroutine(string unLoadSceneName, string loadSceneName, GameState gameState)
    {
        if (sceneTransitionInProgress)
        {
            return;
        }
        if (!Application.CanStreamedLevelBeLoaded(loadSceneName))
        {
            Debug.LogError($"Scene is not available in the build: {loadSceneName}");
            Announce("场景载入失败，请重试");
            return;
        }
        sceneTransitionInProgress = true;
        ChangeScene = StartCoroutine(ChangeSceneCoroutine(unLoadSceneName,loadSceneName,gameState));
    }

    IEnumerator ChangeSceneCoroutine(string unLoadSceneName, string loadSceneName, GameState gameState)
    {
        try
        {
            canvasGroup.blocksRaycasts = true;
            yield return StartCoroutine(Fade(0,1,0.8f,Color.black));

            AsyncOperation unloadOperation = SceneManager.UnloadSceneAsync(unLoadSceneName);
            AsyncOperation loadOperation = SceneManager.LoadSceneAsync(loadSceneName,LoadSceneMode.Additive);
            while ((unloadOperation != null && !unloadOperation.isDone) ||
                   (loadOperation != null && !loadOperation.isDone))
            {
                yield return null;
            }

            soundManager.Instance.StopMusicInFade();
            soundManager.Instance.PlaySFX("ChangeScene");

            this.gameState = gameState;

            canvasGroup.blocksRaycasts = false;
            yield return StartCoroutine(Fade(1,0,0.8f,Color.black));
        }
        finally
        {
            if (canvasGroup != null)
            {
                canvasGroup.blocksRaycasts = false;
            }
            sceneTransitionInProgress = false;
        }
    }

    /// <summary>
    /// 添加暂停场景
    /// </summary>
    public IEnumerator LoadPauseMenu()
    {
        canvasGroup.blocksRaycasts = true;
        yield return StartCoroutine(Fade(0,1,0.8f,Color.black));

        if (!IsNarrativeStoryMode)
        {
            NodeMapBuilder.Instance.SaveNodeMap(nodeIdsInGraph[graphIndex]);
        }

        AsyncOperation unloadOperation = SceneManager.UnloadSceneAsync("GameScene");
        AsyncOperation loadOperation = SceneManager.LoadSceneAsync("PauseMenu",LoadSceneMode.Additive);
        while ((unloadOperation != null && !unloadOperation.isDone) ||
               (loadOperation != null && !loadOperation.isDone))
        {
            yield return null;
        }

        soundManager.Instance.StopMusicInFade();
        soundManager.Instance.PlaySFX("ChangeScene");
        
        canvasGroup.blocksRaycasts = false;
        yield return StartCoroutine(Fade(1,0,0.8f,Color.black));
    }

    /// <summary>
    /// 载入原游戏
    /// </summary>
    public void ChangeAndLoadGameScene(string fromSceneName)
    {
        if (sceneTransitionInProgress)
        {
            return;
        }
        if (!Application.CanStreamedLevelBeLoaded("GameScene"))
        {
            Debug.LogError("GameScene is not available in the build");
            Announce("游戏场景载入失败，请重试");
            return;
        }
        sceneTransitionInProgress = true;
        BackGameSceneFromOther = StartCoroutine(ChangeLoadGameSceneCoroutine(fromSceneName));
    }

    /// <summary>
    /// 载入原游戏数据
    /// </summary>
    IEnumerator ChangeLoadGameSceneCoroutine(string fromSceneName)
    {
        try
        {
            canvasGroup.blocksRaycasts = true;
            yield return StartCoroutine(Fade(0,1,0.8f,Color.black));

            AsyncOperation unloadOperation = SceneManager.UnloadSceneAsync(fromSceneName);
            AsyncOperation asyncOperation = SceneManager.LoadSceneAsync("GameScene",LoadSceneMode.Additive);

            while ((unloadOperation != null && !unloadOperation.isDone) ||
                   (asyncOperation != null && !asyncOperation.isDone))
            {
                yield return null;
            }
            soundManager.Instance.StopMusicInFade();
            soundManager.Instance.PlaySFX("ChangeScene");

            if (IsNarrativeStoryMode)
            {
                gameState = GameState.Playing;
                PlayCommonNarrativeMusic();
                FactoryEscapeAccessibility.RefreshScreen("story-description");
            }
            else
            {
                LoadNodeGraph();
                PlayCurrentLevelAudio();
            }

            canvasGroup.blocksRaycasts = false;
            yield return StartCoroutine(Fade(1,0,0.8f,Color.black));

            if (restoredFromSave)
            {
                restoredFromSave = false;
                Announce("已恢复上次存档");
            }
        }
        finally
        {
            if (canvasGroup != null)
            {
                canvasGroup.blocksRaycasts = false;
            }
            sceneTransitionInProgress = false;
        }
    }

    /// <summary>
    /// 根据当前的关卡数据载入节点图与关卡
    /// </summary>
    private void LoadNodeGraph()
    {
        NodeLevelSO currentNodeLevel = nodeLevelSOs[levelIndex];

        MatchRightAndLeftNodeGraphName(currentNodeLevel);
        BindNodeGraphButtons();

        if (currentNodeLevel.chapterBot != 0 && tongyi_AI.instance != null)
        {
            tongyi_AI.instance.changeRobot(currentNodeLevel.chapterBot);
        }
        previousChapterBot = currentNodeLevel.chapterBot;
        UIManager.Instance.SkyUI.gameObject.SetActive(levelIndex == SkyUiLevelIndex);

        NodeMapBuilder.Instance.GenerateNodeMap(
            currentNodeLevel.levelGraphs[graphIndex],
            enterNodeGraphTimesList[graphIndex]
            );
        enterNodeGraphTimesList[graphIndex]++;

        // 根据读取的节点状态数据重新载入节点图
        if (enterNodeGraphTimesList[graphIndex] != 1)
            NodeMapBuilder.Instance.LoadNodeMap(nodeIdsInGraph[graphIndex]);
    }

    private InkAdventureSession CreateInkStorySession()
    {
        InkAdventureStory definition = gameDefinition.inkStory;
        return new InkAdventureSession(
            definition.source.storyJson,
            definition.defaultTitle,
            definition.defaultVisualDescription);
    }

    private static void PlayCommonNarrativeMusic()
    {
        soundManager.Instance?.PlayMusicInFade("Theme");
    }


    /// <summary>
    /// 播放当前关卡的音乐
    /// </summary>
    public void PlayCurrentLevelAudio()
    {
        NodeLevelSO currentLevel = nodeLevelSOs[levelIndex];

        soundManager.Instance.StopMusicInFade();
        if (currentLevel.audioClip != null)
        {
            soundManager.Instance.PlayMusicInFade(currentLevel.audioClip);
        }
    }

}
