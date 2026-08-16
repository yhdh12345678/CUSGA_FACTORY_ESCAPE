using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class TextAdventureGameImporter
{
    private const string GeneratedDirectory = "Assets/GameContent/Generated";
    private const string CurrentGameAssetPath = "Assets/GameContent/CurrentGame.asset";
    private const string CatalogAssetPath = "Assets/GameContent/GameCatalog.asset";

    [Serializable]
    private sealed class ImportDocument
    {
        public int schemaVersion = TextAdventureGameSO.CurrentSchemaVersion;
        public string gameId;
        public string displayName;
        public string protagonistName;
        public string firstPageId;
        public List<TextAdventureChapter> chapters = new List<TextAdventureChapter>();
        public List<ImportPage> pages = new List<ImportPage>();
    }

    [Serializable]
    private sealed class ImportPage
    {
        public string id;
        public string chapterId;
        public string title;
        public string visualDescription;
        public string artworkPath;
        public List<TextAdventureLine> lines = new List<TextAdventureLine>();
        public string nextPageId;
        public List<TextAdventureChoice> choices = new List<TextAdventureChoice>();
    }

    [MenuItem("工具/文字游戏/导入剧情 JSON")]
    public static void ImportFromDialog()
    {
        string path = EditorUtility.OpenFilePanel("选择文字游戏剧情 JSON", string.Empty, "json");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        TextAdventureGameSO game = ImportFromFile(path);
        Selection.activeObject = game;
        EditorGUIUtility.PingObject(game);
    }

    public static TextAdventureGameSO ImportFromFile(string inputPath, string outputAssetPath = null)
    {
        if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
        {
            throw new FileNotFoundException("找不到剧情 JSON。", inputPath);
        }

        ImportDocument document = JsonUtility.FromJson<ImportDocument>(
            File.ReadAllText(inputPath));
        if (document == null)
        {
            throw new InvalidDataException("剧情 JSON 无法解析。");
        }

        var game = ScriptableObject.CreateInstance<TextAdventureGameSO>();
        game.schemaVersion = document.schemaVersion;
        game.gameId = document.gameId?.Trim();
        game.displayName = document.displayName?.Trim();
        game.protagonistName = document.protagonistName?.Trim();
        game.mode = TextAdventureGameMode.TextStory;
        game.story.firstPageId = document.firstPageId?.Trim();
        game.story.chapters = document.chapters ?? new List<TextAdventureChapter>();
        game.story.pages = new List<TextAdventurePage>();

        foreach (ImportPage importedPage in document.pages ?? new List<ImportPage>())
        {
            var page = new TextAdventurePage
            {
                id = importedPage.id?.Trim(),
                chapterId = importedPage.chapterId?.Trim(),
                title = importedPage.title?.Trim(),
                visualDescription = importedPage.visualDescription?.Trim(),
                artwork = LoadOptionalArtwork(importedPage.artworkPath),
                lines = importedPage.lines ?? new List<TextAdventureLine>(),
                nextPageId = importedPage.nextPageId?.Trim(),
                choices = importedPage.choices ?? new List<TextAdventureChoice>()
            };
            game.story.pages.Add(page);
        }

        List<string> errors = TextAdventureGameValidator.Validate(game);
        if (errors.Count > 0)
        {
            UnityEngine.Object.DestroyImmediate(game);
            throw new InvalidDataException("剧情数据校验失败：\n" + string.Join("\n", errors));
        }

        EnsureDirectory(GeneratedDirectory);
        string assetPath = string.IsNullOrWhiteSpace(outputAssetPath)
            ? $"{GeneratedDirectory}/{game.gameId}.asset"
            : outputAssetPath.Replace('\\', '/');
        EnsureDirectory(Path.GetDirectoryName(assetPath)?.Replace('\\', '/'));

        TextAdventureGameSO existing = AssetDatabase.LoadAssetAtPath<TextAdventureGameSO>(assetPath);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(game, assetPath);
            existing = game;
        }
        else
        {
            EditorUtility.CopySerialized(game, existing);
            UnityEngine.Object.DestroyImmediate(game);
            EditorUtility.SetDirty(existing);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"文字游戏剧情导入完成：pages={existing.story.pages.Count}; output={assetPath}");
        return existing;
    }

    [MenuItem("工具/文字游戏/校验选中的游戏定义")]
    public static void ValidateSelectedDefinition()
    {
        TextAdventureGameSO game = Selection.activeObject as TextAdventureGameSO;
        if (game == null)
        {
            throw new InvalidOperationException("请先选择一个文字游戏定义资源。");
        }

        List<string> errors = TextAdventureGameValidator.Validate(game);
        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join("\n", errors));
        }

        Debug.Log($"游戏定义校验通过：{game.displayName}，页面 {game.story?.pages?.Count ?? 0} 个。");
    }

    [MenuItem("工具/文字游戏/将选中定义添加到子游戏目录")]
    public static void AddSelectedDefinitionToCatalog()
    {
        TextAdventureGameSO game = Selection.activeObject as TextAdventureGameSO;
        if (game == null)
        {
            throw new InvalidOperationException("请先选择一个文字游戏定义资源。");
        }

        AddDefinitionToCatalog(game);
    }

    public static TextAdventureCatalogSO AddDefinitionToCatalog(TextAdventureGameSO game)
    {
        List<string> errors = TextAdventureGameValidator.Validate(game);
        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join("\n", errors));
        }

        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/LoadScene.unity", OpenSceneMode.Single);
        GameManager manager = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<GameManager>(true))
            .FirstOrDefault();
        if (manager == null)
        {
            throw new InvalidOperationException("LoadScene 中找不到 GameManager。");
        }

        EnsureDirectory("Assets/GameContent");
        TextAdventureCatalogSO catalog =
            AssetDatabase.LoadAssetAtPath<TextAdventureCatalogSO>(CatalogAssetPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<TextAdventureCatalogSO>();
            AssetDatabase.CreateAsset(catalog, CatalogAssetPath);
        }

        TextAdventureGameSO duplicate = catalog.games.FirstOrDefault(candidate =>
            candidate != null && candidate.gameId == game.gameId);
        if (duplicate != null && duplicate != game)
        {
            throw new InvalidDataException($"子游戏目录已经包含相同 gameId：{game.gameId}。");
        }

        if (!catalog.games.Contains(game))
        {
            catalog.games.Add(game);
            EditorUtility.SetDirty(catalog);
        }

        manager.gameCatalog = catalog;
        if (manager.gameDefinition == null)
        {
            manager.gameDefinition = catalog.games[0];
        }
        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"已添加子游戏：{game.displayName}；目录数量={catalog.games.Count}");
        return catalog;
    }

    public static void CreateCurrentLegacyDefinition()
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/LoadScene.unity", OpenSceneMode.Single);
        GameManager manager = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<GameManager>(true))
            .FirstOrDefault();
        if (manager == null)
        {
            throw new InvalidOperationException("LoadScene 中找不到 GameManager。");
        }

        EnsureDirectory("Assets/GameContent");
        TextAdventureGameSO game = AssetDatabase.LoadAssetAtPath<TextAdventureGameSO>(CurrentGameAssetPath);
        if (game == null)
        {
            game = ScriptableObject.CreateInstance<TextAdventureGameSO>();
            AssetDatabase.CreateAsset(game, CurrentGameAssetPath);
        }

        game.schemaVersion = TextAdventureGameSO.CurrentSchemaVersion;
        game.gameId = "stay-the-afterglow";
        game.displayName = "抓住未尽的余晖";
        game.protagonistName = "小明";
        game.mode = TextAdventureGameMode.LegacyNodeAdventure;
        game.saveProfileNameOverride = "GameProgress";
        game.legacyLevels = new List<NodeLevelSO>(manager.nodeLevelSOs);
        game.skyUiLevelIndex = 8;
        game.primaryEnding = new List<CutSceneCell>(manager.winCutScene);
        game.alternateEnding = new List<CutSceneCell>(manager.fakeCutScene);
        EditorUtility.SetDirty(game);

        TextAdventureCatalogSO catalog =
            AssetDatabase.LoadAssetAtPath<TextAdventureCatalogSO>(CatalogAssetPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<TextAdventureCatalogSO>();
            AssetDatabase.CreateAsset(catalog, CatalogAssetPath);
        }
        catalog.games.RemoveAll(candidate => candidate == null || candidate.gameId == game.gameId);
        catalog.games.Insert(0, game);
        EditorUtility.SetDirty(catalog);

        manager.gameCatalog = catalog;
        manager.gameDefinition = game;
        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"当前游戏已接入子游戏目录：levels={game.legacyLevels.Count}; catalog={catalog.games.Count}");
    }

    public static void ImportFromEnvironment()
    {
        string inputPath = Environment.GetEnvironmentVariable("TEXT_ADVENTURE_JSON");
        string outputPath = Environment.GetEnvironmentVariable("TEXT_ADVENTURE_ASSET");
        TextAdventureGameSO game = ImportFromFile(inputPath, outputPath);
        if (Environment.GetEnvironmentVariable("TEXT_ADVENTURE_ADD_TO_CATALOG") == "1")
        {
            AddDefinitionToCatalog(game);
        }
    }

    private static Sprite LoadOptionalArtwork(string artworkPath)
    {
        if (string.IsNullOrWhiteSpace(artworkPath))
        {
            return null;
        }

        string normalized = artworkPath.Replace('\\', '/');
        Sprite artwork = AssetDatabase.LoadAssetAtPath<Sprite>(normalized);
        if (artwork == null)
        {
            throw new InvalidDataException($"找不到可选图片资源：{normalized}");
        }

        return artwork;
    }

    private static void EnsureDirectory(string assetDirectory)
    {
        if (string.IsNullOrWhiteSpace(assetDirectory) || AssetDatabase.IsValidFolder(assetDirectory))
        {
            return;
        }

        string parent = Path.GetDirectoryName(assetDirectory)?.Replace('\\', '/');
        string name = Path.GetFileName(assetDirectory);
        EnsureDirectory(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
}
