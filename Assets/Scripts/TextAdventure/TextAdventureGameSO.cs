using System;
using System.Collections.Generic;
using UnityEngine;

public enum TextAdventureGameMode
{
    LegacyNodeAdventure,
    TextStory
}

[CreateAssetMenu(
    fileName = "TextAdventureGame_",
    menuName = "文字游戏/游戏定义")]
public sealed class TextAdventureGameSO : ScriptableObject
{
    public const int CurrentSchemaVersion = 1;

    [Header("游戏信息")]
    public int schemaVersion = CurrentSchemaVersion;
    [Tooltip("稳定且唯一的英文标识，用于区分不同游戏的存档")]
    public string gameId;
    public string displayName;
    public string protagonistName;
    public TextAdventureGameMode mode = TextAdventureGameMode.TextStory;
    [Tooltip("仅用于兼容已有存档；新子游戏通常留空")]
    public string saveProfileNameOverride;

    [Header("通用文字剧情")]
    public TextAdventureStory story = new TextAdventureStory();

    [Header("原作节点模式兼容")]
    public List<NodeLevelSO> legacyLevels = new List<NodeLevelSO>();
    public int skyUiLevelIndex = -1;
    public List<CutSceneCell> primaryEnding = new List<CutSceneCell>();
    public List<CutSceneCell> alternateEnding = new List<CutSceneCell>();

    public string SaveProfileName => !string.IsNullOrWhiteSpace(saveProfileNameOverride)
        ? saveProfileNameOverride.Trim()
        : string.IsNullOrWhiteSpace(gameId)
            ? "GameProgress"
            : $"GameProgress_{gameId.Trim()}";
}

[Serializable]
public sealed class TextAdventureStory
{
    public string firstPageId;
    public List<TextAdventureChapter> chapters = new List<TextAdventureChapter>();
    public List<TextAdventurePage> pages = new List<TextAdventurePage>();
}

[Serializable]
public sealed class TextAdventureChapter
{
    public string id;
    public string title;
}

[Serializable]
public sealed class TextAdventurePage
{
    public string id;
    public string chapterId;
    public string title;
    [TextArea(2, 6)] public string visualDescription;
    [Tooltip("可选。为空时只显示文字，不影响读屏和游戏流程")]
    public Sprite artwork;
    public List<TextAdventureLine> lines = new List<TextAdventureLine>();
    [Tooltip("无选项页面的下一页；为空表示结局页")]
    public string nextPageId;
    public List<TextAdventureChoice> choices = new List<TextAdventureChoice>();
}

[Serializable]
public sealed class TextAdventureLine
{
    public string speaker;
    [TextArea(1, 5)] public string text;
}

[Serializable]
public sealed class TextAdventureChoice
{
    public string id;
    public string label;
    public string nextPageId;
}

public static class TextAdventureGameValidator
{
    public static List<string> Validate(TextAdventureGameSO game)
    {
        var errors = new List<string>();
        if (game == null)
        {
            errors.Add("游戏定义不能为空。");
            return errors;
        }

        if (game.schemaVersion != TextAdventureGameSO.CurrentSchemaVersion)
        {
            errors.Add($"不支持的数据版本：{game.schemaVersion}。");
        }

        if (!IsValidGameId(game.gameId))
        {
            errors.Add("gameId 只能包含小写英文字母、数字、点、下划线和连字符。");
        }

        if (string.IsNullOrWhiteSpace(game.displayName))
        {
            errors.Add("游戏名称不能为空。");
        }

        if (game.mode == TextAdventureGameMode.LegacyNodeAdventure)
        {
            if (game.legacyLevels == null || game.legacyLevels.Count == 0)
            {
                errors.Add("原作节点模式至少需要一个关卡。");
            }
            return errors;
        }

        ValidateStory(game.story, errors);
        return errors;
    }

    private static void ValidateStory(TextAdventureStory story, List<string> errors)
    {
        if (story == null || story.pages == null || story.pages.Count == 0)
        {
            errors.Add("文字剧情至少需要一个页面。");
            return;
        }

        var chapterIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (TextAdventureChapter chapter in story.chapters ?? new List<TextAdventureChapter>())
        {
            if (chapter == null || string.IsNullOrWhiteSpace(chapter.id))
            {
                errors.Add("章节 id 不能为空。");
            }
            else if (!chapterIds.Add(chapter.id))
            {
                errors.Add($"章节 id 重复：{chapter.id}。");
            }

            if (chapter == null || string.IsNullOrWhiteSpace(chapter.title))
            {
                errors.Add($"章节 {chapter?.id ?? "未知"} 的标题不能为空。");
            }
        }

        var pagesById = new Dictionary<string, TextAdventurePage>(StringComparer.Ordinal);
        foreach (TextAdventurePage page in story.pages)
        {
            if (page == null || string.IsNullOrWhiteSpace(page.id))
            {
                errors.Add("页面 id 不能为空。");
                continue;
            }

            if (!pagesById.TryAdd(page.id, page))
            {
                errors.Add($"页面 id 重复：{page.id}。");
            }
        }

        if (string.IsNullOrWhiteSpace(story.firstPageId) ||
            !pagesById.ContainsKey(story.firstPageId))
        {
            errors.Add("firstPageId 必须指向存在的页面。");
        }

        foreach (TextAdventurePage page in pagesById.Values)
        {
            ValidatePage(page, pagesById, chapterIds, errors);
        }

        if (!string.IsNullOrWhiteSpace(story.firstPageId) &&
            pagesById.ContainsKey(story.firstPageId))
        {
            var reachable = new HashSet<string>(StringComparer.Ordinal);
            AddReachablePages(story.firstPageId, pagesById, reachable);
            foreach (string pageId in pagesById.Keys)
            {
                if (!reachable.Contains(pageId))
                {
                    errors.Add($"页面无法从开场到达：{pageId}。");
                }
            }
        }
    }

    private static void ValidatePage(
        TextAdventurePage page,
        IReadOnlyDictionary<string, TextAdventurePage> pagesById,
        ISet<string> chapterIds,
        ICollection<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(page.chapterId) && !chapterIds.Contains(page.chapterId))
        {
            errors.Add($"页面 {page.id} 引用了不存在的章节：{page.chapterId}。");
        }

        if (string.IsNullOrWhiteSpace(page.title))
        {
            errors.Add($"页面 {page.id} 的标题不能为空。");
        }

        if (string.IsNullOrWhiteSpace(page.visualDescription))
        {
            errors.Add($"页面 {page.id} 缺少画面描述。");
        }

        foreach (TextAdventureLine line in page.lines ?? new List<TextAdventureLine>())
        {
            if (line == null || string.IsNullOrWhiteSpace(line.text))
            {
                errors.Add($"页面 {page.id} 存在空台词。");
            }
        }

        List<TextAdventureChoice> choices = page.choices ?? new List<TextAdventureChoice>();
        if (choices.Count > 0 && !string.IsNullOrWhiteSpace(page.nextPageId))
        {
            errors.Add($"页面 {page.id} 不能同时设置固定下一页和选项。");
        }

        if (!string.IsNullOrWhiteSpace(page.nextPageId) && !pagesById.ContainsKey(page.nextPageId))
        {
            errors.Add($"页面 {page.id} 的下一页不存在：{page.nextPageId}。");
        }

        var choiceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (TextAdventureChoice choice in choices)
        {
            if (choice == null || string.IsNullOrWhiteSpace(choice.id) || !choiceIds.Add(choice.id))
            {
                errors.Add($"页面 {page.id} 的选项 id 为空或重复。");
            }
            if (choice == null || string.IsNullOrWhiteSpace(choice.label))
            {
                errors.Add($"页面 {page.id} 存在没有文字的选项。");
            }
            if (choice == null || string.IsNullOrWhiteSpace(choice.nextPageId) ||
                !pagesById.ContainsKey(choice.nextPageId))
            {
                errors.Add($"页面 {page.id} 的选项 {choice?.id ?? "未知"} 没有有效目标页。");
            }
        }
    }

    private static void AddReachablePages(
        string pageId,
        IReadOnlyDictionary<string, TextAdventurePage> pagesById,
        ISet<string> reachable)
    {
        if (!reachable.Add(pageId) || !pagesById.TryGetValue(pageId, out TextAdventurePage page))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(page.nextPageId))
        {
            AddReachablePages(page.nextPageId, pagesById, reachable);
        }

        foreach (TextAdventureChoice choice in page.choices ?? new List<TextAdventureChoice>())
        {
            if (choice != null && !string.IsNullOrWhiteSpace(choice.nextPageId))
            {
                AddReachablePages(choice.nextPageId, pagesById, reachable);
            }
        }
    }

    private static bool IsValidGameId(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return false;
        }

        foreach (char character in gameId)
        {
            bool valid = character >= 'a' && character <= 'z' ||
                         character >= '0' && character <= '9' ||
                         character == '.' || character == '_' || character == '-';
            if (!valid)
            {
                return false;
            }
        }
        return true;
    }
}

public sealed class TextAdventureSession
{
    private readonly Dictionary<string, TextAdventurePage> pagesById;

    public TextAdventureGameSO Game { get; }
    public TextAdventurePage CurrentPage { get; private set; }

    public TextAdventureSession(TextAdventureGameSO game)
    {
        Game = game;
        pagesById = new Dictionary<string, TextAdventurePage>(StringComparer.Ordinal);
        if (game?.story?.pages == null)
        {
            return;
        }

        foreach (TextAdventurePage page in game.story.pages)
        {
            if (page != null && !string.IsNullOrWhiteSpace(page.id))
            {
                pagesById[page.id] = page;
            }
        }
    }

    public bool Start(out string errorMessage)
    {
        return MoveTo(Game?.story?.firstPageId, out errorMessage);
    }

    public bool Restore(string pageId, out string errorMessage)
    {
        return MoveTo(pageId, out errorMessage);
    }

    public bool Continue(out string errorMessage)
    {
        if (CurrentPage == null || string.IsNullOrWhiteSpace(CurrentPage.nextPageId))
        {
            errorMessage = "当前页面没有后续剧情。";
            return false;
        }

        return MoveTo(CurrentPage.nextPageId, out errorMessage);
    }

    public bool Choose(string choiceId, out string errorMessage)
    {
        TextAdventureChoice choice = CurrentPage?.choices?.Find(candidate =>
            candidate != null && candidate.id == choiceId);
        if (choice == null)
        {
            errorMessage = "当前页面没有这个选项。";
            return false;
        }

        return MoveTo(choice.nextPageId, out errorMessage);
    }

    private bool MoveTo(string pageId, out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(pageId) || !pagesById.TryGetValue(pageId, out TextAdventurePage page))
        {
            errorMessage = $"剧情页面不存在：{pageId ?? string.Empty}。";
            return false;
        }

        CurrentPage = page;
        errorMessage = string.Empty;
        return true;
    }
}
