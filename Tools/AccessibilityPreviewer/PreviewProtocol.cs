using System.Text.Json;

namespace AccessibilityPreviewer;

internal sealed class PreviewState
{
    public long Revision { get; set; }
    public string FocusKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<PreviewItem> Items { get; set; } = [];
}

internal sealed class PreviewItem
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public bool Actionable { get; set; }
    public bool Editable { get; set; }

    public override string ToString()
    {
        string text = string.IsNullOrWhiteSpace(Value) ? Label : $"{Label}，{Value}";
        return State.Contains("Disabled", StringComparison.Ordinal)
            ? $"{text}，不可用"
            : text;
    }
}

internal sealed class PreviewCommand
{
    public long Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

internal sealed class PreviewFileChannel
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string previewDirectory;
    private long nextCommandId = DateTime.UtcNow.Ticks;

    public PreviewFileChannel(string projectRoot)
    {
        previewDirectory = Path.Combine(projectRoot, "Temp", "AccessibilityPreview");
    }

    public void Enable()
    {
        Directory.CreateDirectory(previewDirectory);
        foreach (string staleCommand in Directory.GetFiles(previewDirectory, "command-*.json"))
        {
            File.Delete(staleCommand);
        }
        string legacyCommand = Path.Combine(previewDirectory, "command.json");
        if (File.Exists(legacyCommand))
        {
            File.Delete(legacyCommand);
        }
        File.WriteAllText(Path.Combine(previewDirectory, "client.pid"), Environment.ProcessId.ToString());
        File.WriteAllText(Path.Combine(previewDirectory, "enabled"), "1");
    }

    public PreviewState? ReadState()
    {
        string path = Path.Combine(previewDirectory, "state.json");
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<PreviewState>(File.ReadAllText(path), JsonOptions);
    }

    public void Send(string action, string key = "", string value = "")
    {
        var command = new PreviewCommand
        {
            Id = Interlocked.Increment(ref nextCommandId),
            Action = action,
            Key = key,
            Value = value
        };
        WriteAtomically(
            Path.Combine(previewDirectory, $"command-{command.Id:D20}.json"),
            JsonSerializer.Serialize(command, JsonOptions));
    }

    public void Disable()
    {
        string clientPidPath = Path.Combine(previewDirectory, "client.pid");
        if (!File.Exists(clientPidPath) ||
            File.ReadAllText(clientPidPath).Trim() != Environment.ProcessId.ToString())
        {
            return;
        }

        foreach (string fileName in new[] { "enabled", "client.pid" })
        {
            string path = Path.Combine(previewDirectory, fileName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static void WriteAtomically(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, content);
        File.Move(temporaryPath, path, true);
    }

    public static bool RunSelfTest()
    {
        string testRoot = Path.Combine(Path.GetTempPath(), "AccessibilityPreviewer", Guid.NewGuid().ToString("N"));
        try
        {
            var channel = new PreviewFileChannel(testRoot);
            channel.Enable();
            string statePath = Path.Combine(testRoot, "Temp", "AccessibilityPreview", "state.json");
            File.WriteAllText(statePath,
                "{\"revision\":1,\"focusKey\":\"start\",\"status\":\"ready\",\"items\":[" +
                "{\"key\":\"start\",\"label\":\"开始游戏\",\"value\":\"\",\"role\":\"Button\"," +
                "\"state\":\"None\",\"actionable\":true,\"editable\":false}]}");

            PreviewState? state = channel.ReadState();
            channel.Send("activate", "start");
            string commandPath = Directory.GetFiles(
                Path.Combine(testRoot, "Temp", "AccessibilityPreview"), "command-*.json").Single();
            PreviewCommand? command = JsonSerializer.Deserialize<PreviewCommand>(
                File.ReadAllText(commandPath), JsonOptions);
            return PreviewKeyMap.RunSelfTest() &&
                   state?.Items.Single().Label == "开始游戏" &&
                   state.FocusKey == "start" &&
                   command?.Action == "activate" &&
                   command.Key == "start";
        }
        catch
        {
            return false;
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, true);
            }
        }
    }
}
