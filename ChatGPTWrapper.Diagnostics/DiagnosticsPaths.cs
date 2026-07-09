namespace ChatGPTWrapper.Diagnostics;

/// <summary>
/// Config root for diagnostics JSONL files — mirrors WPF <c>AppDirectories.ConfigRoot</c>.
/// </summary>
internal static class DiagnosticsPaths
{
    internal static string? TestRootOverride { get; set; }

    public static string ConfigRoot =>
        TestRootOverride
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChatGPTWrapper");

    public static void EnsureLogDirectory() =>
        Directory.CreateDirectory(ConfigRoot);
}
