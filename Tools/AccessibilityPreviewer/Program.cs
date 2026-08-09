namespace AccessibilityPreviewer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        string projectRoot = GetArgument(args, "--project-root")
            ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            Environment.ExitCode = PreviewFileChannel.RunSelfTest() ? 0 : 1;
            return;
        }

        string lockName = "Local\\FactoryEscapeAccessibilityPreview-" +
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(projectRoot)))[..16];
        using var instanceLock = new Mutex(true, lockName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new PreviewForm(projectRoot));
    }

    private static string? GetArgument(IReadOnlyList<string> args, string name)
    {
        for (int index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(args[index + 1]);
            }
        }

        return null;
    }
}
