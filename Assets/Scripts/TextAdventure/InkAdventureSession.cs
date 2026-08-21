using System;
using System.Collections.Generic;
using Ink.Runtime;

[Serializable]
public sealed class InkAdventurePage
{
    public int sequence;
    public string title;
    public string visualDescription;
    public List<TextAdventureLine> lines = new List<TextAdventureLine>();
    public List<InkAdventureChoice> choices = new List<InkAdventureChoice>();
    public bool isEnding;
}

[Serializable]
public sealed class InkAdventureChoice
{
    public int index;
    public string label;
}

public sealed class InkAdventureSession
{
    private readonly string storyJson;
    private readonly string defaultTitle;
    private readonly string defaultVisualDescription;
    private Story story;
    private int pageSequence;
    private string currentTitle;
    private string currentVisualDescription;

    public InkAdventurePage CurrentPage { get; private set; }

    public InkAdventureSession(
        string storyJson,
        string defaultTitle,
        string defaultVisualDescription)
    {
        this.storyJson = storyJson;
        this.defaultTitle = defaultTitle;
        this.defaultVisualDescription = defaultVisualDescription;
    }

    public bool Start(out string errorMessage)
    {
        try
        {
            story = new Story(storyJson);
            pageSequence = 0;
            currentTitle = defaultTitle;
            currentVisualDescription = defaultVisualDescription;
            return ReadNextPage(out errorMessage);
        }
        catch (Exception exception)
        {
            errorMessage = $"Ink 剧情无法载入：{exception.Message}";
            return false;
        }
    }

    public bool Restore(
        string stateJson,
        InkAdventurePage savedPage,
        out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(stateJson) || savedPage == null)
        {
            errorMessage = "Ink 存档内容不完整。";
            return false;
        }

        try
        {
            story = new Story(storyJson);
            story.state.LoadJson(stateJson);
            pageSequence = savedPage.sequence;
            currentTitle = string.IsNullOrWhiteSpace(savedPage.title)
                ? defaultTitle
                : savedPage.title;
            currentVisualDescription = string.IsNullOrWhiteSpace(savedPage.visualDescription)
                ? defaultVisualDescription
                : savedPage.visualDescription;
            CurrentPage = new InkAdventurePage
            {
                sequence = savedPage.sequence,
                title = currentTitle,
                visualDescription = currentVisualDescription,
                lines = CloneLines(savedPage.lines),
                choices = BuildChoices(),
                isEnding = !story.canContinue && story.currentChoices.Count == 0
            };
            errorMessage = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            errorMessage = $"Ink 存档无法恢复：{exception.Message}";
            return false;
        }
    }

    public bool Choose(int choiceIndex, out string errorMessage)
    {
        if (story == null || CurrentPage == null ||
            !CurrentPage.choices.Exists(choice => choice.index == choiceIndex))
        {
            errorMessage = "当前剧情没有这个选项。";
            return false;
        }

        try
        {
            story.ChooseChoiceIndex(choiceIndex);
            return ReadNextPage(out errorMessage);
        }
        catch (Exception exception)
        {
            errorMessage = $"Ink 选项执行失败：{exception.Message}";
            return false;
        }
    }

    public string GetStateJson()
    {
        return story?.state?.ToJson() ?? string.Empty;
    }

    private bool ReadNextPage(out string errorMessage)
    {
        var lines = new List<TextAdventureLine>();

        while (story.canContinue)
        {
            string content = story.Continue();
            ApplyTags(story.currentTags, ref currentTitle, ref currentVisualDescription);
            foreach (string part in content.Split(new[] { '\r', '\n' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                string text = ToPlainText(part);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    lines.Add(new TextAdventureLine { text = text });
                }
            }
        }

        if (story.hasError)
        {
            errorMessage = "Ink 剧情数据发生错误。";
            return false;
        }

        pageSequence++;
        CurrentPage = new InkAdventurePage
        {
            sequence = pageSequence,
            title = currentTitle,
            visualDescription = currentVisualDescription,
            lines = lines,
            choices = BuildChoices(),
            isEnding = story.currentChoices.Count == 0
        };
        errorMessage = string.Empty;
        return true;
    }

    private List<InkAdventureChoice> BuildChoices()
    {
        var choices = new List<InkAdventureChoice>();
        if (story == null)
        {
            return choices;
        }

        foreach (Choice choice in story.currentChoices)
        {
            choices.Add(new InkAdventureChoice
            {
                index = choice.index,
                label = ToPlainText(choice.text)
            });
        }
        return choices;
    }

    private static List<TextAdventureLine> CloneLines(List<TextAdventureLine> source)
    {
        var lines = new List<TextAdventureLine>();
        foreach (TextAdventureLine line in source ?? new List<TextAdventureLine>())
        {
            if (line != null)
            {
                lines.Add(new TextAdventureLine { speaker = line.speaker, text = line.text });
            }
        }
        return lines;
    }

    private static void ApplyTags(
        List<string> tags,
        ref string title,
        ref string visualDescription)
    {
        foreach (string tag in tags ?? new List<string>())
        {
            int separator = tag.IndexOf(':');
            if (separator <= 0 || separator >= tag.Length - 1)
            {
                continue;
            }

            string key = tag.Substring(0, separator).Trim();
            string value = tag.Substring(separator + 1).Trim();
            if (key.Equals("title", StringComparison.OrdinalIgnoreCase))
            {
                title = value;
            }
            else if (key.Equals("visual", StringComparison.OrdinalIgnoreCase))
            {
                visualDescription = value;
            }
        }
    }

    private static string ToPlainText(string text)
    {
        return (text ?? string.Empty)
            .Replace("<i>", string.Empty)
            .Replace("</i>", string.Empty)
            .Trim();
    }
}
