using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "TextAdventureCatalog",
    menuName = "文字游戏/子游戏目录")]
public sealed class TextAdventureCatalogSO : ScriptableObject
{
    public const int CurrentSchemaVersion = 1;

    public int schemaVersion = CurrentSchemaVersion;
    [Tooltip("同一个 App 内按顺序显示的全部子游戏")]
    public List<TextAdventureGameSO> games = new List<TextAdventureGameSO>();
}

public static class TextAdventureCatalogValidator
{
    public static List<string> Validate(TextAdventureCatalogSO catalog)
    {
        var errors = new List<string>();
        if (catalog == null)
        {
            errors.Add("子游戏目录不能为空。");
            return errors;
        }

        if (catalog.schemaVersion != TextAdventureCatalogSO.CurrentSchemaVersion)
        {
            errors.Add($"不支持的子游戏目录版本：{catalog.schemaVersion}。");
        }

        if (catalog.games == null || catalog.games.Count == 0)
        {
            errors.Add("子游戏目录至少需要一个游戏。");
            return errors;
        }

        var gameIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (TextAdventureGameSO game in catalog.games)
        {
            if (game == null)
            {
                errors.Add("子游戏目录包含空引用。");
                continue;
            }

            errors.AddRange(TextAdventureGameValidator.Validate(game));
            if (!gameIds.Add(game.gameId))
            {
                errors.Add($"子游戏 gameId 重复：{game.gameId}。");
            }
        }

        return errors;
    }
}
