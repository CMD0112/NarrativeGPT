using System.IO;
using ChatGPTWrapper.Adventure;
using ChatGPTWrapper.Shell;

namespace ChatGPTWrapper;

/// <summary>
/// Runtime folders under %LocalAppData%\ChatGPTWrapper (optional user CSS, WebView2 profile).
/// Adventures may use a user-configured root via <see cref="WrapperSettingsStore"/>.
/// </summary>
internal static class AppDirectories
{
    internal static string? TestRootOverride { get; set; }

    private static string? _adventuresDirectoryOverride;
    private static string? _initializedConfigRoot;

    /// <summary>Fixed config root (settings, WebView2, libraries) — not overridden by adventures path.</summary>
    public static string ConfigRoot =>
        TestRootOverride
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChatGPTWrapper");

    public static string Root => ConfigRoot;

    public static string StylesDirectory => Path.Combine(Root, "styles");

    public static string WebView2UserDataDirectory => Path.Combine(Root, "WebView2UserData");

    public static string WebView2AttachWorkerUserDataDirectory => Path.Combine(Root, "WebView2UserData-AttachWorker");

    public static string WebView2ParallelWorkerSlotDirectory(int slotId) =>
        Path.Combine(Root, $"WebView2UserData-ParallelWorker-{slotId}");

    public static string AutomationBrowserUserDataDirectory => Path.Combine(Root, "AutomationBrowser");

    public static string DefaultAdventuresDirectory => Path.Combine(Root, "adventures");

    public static string AdventuresDirectory =>
        _adventuresDirectoryOverride ?? DefaultAdventuresDirectory;

    /// <summary>Sorts first by name in Explorer; holds title symlinks to adventure folders.</summary>
    public const string AdventuresIndexDirectoryName = "! Adventures";

    public static string AdventuresIndexDirectory =>
        Path.Combine(AdventuresDirectory, AdventuresIndexDirectoryName);

    public static bool IsReservedAdventuresDirectory(string directoryName) =>
        string.Equals(directoryName, AdventuresIndexDirectoryName, StringComparison.OrdinalIgnoreCase);

    public static string LibrariesDirectory => Path.Combine(Root, "libraries");

    public static string BackupsDirectory => Path.Combine(Root, "backups");

    public static string AdventureDirectory(Guid adventureId)
    {
        var custom = AdventureLocationStore.TryGet(adventureId);
        if (!string.IsNullOrWhiteSpace(custom))
            return custom;

        return Path.Combine(AdventuresDirectory, adventureId.ToString("D"));
    }

    public static string AdventureSourcesDirectory(Guid adventureId) =>
        Path.Combine(AdventureDirectory(adventureId), "sources");

    internal static void ApplyAdventuresDirectoryOverride(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _adventuresDirectoryOverride = null;
            return;
        }

        _adventuresDirectoryOverride = Path.GetFullPath(path.Trim());
    }

    internal static void ResetStoresForTests() => _initializedConfigRoot = null;

    public static void EnsureCreated()
    {
        EnsureStoresInitialized();

        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(StylesDirectory);
        Directory.CreateDirectory(WebView2UserDataDirectory);
        Directory.CreateDirectory(WebView2AttachWorkerUserDataDirectory);
        Directory.CreateDirectory(AutomationBrowserUserDataDirectory);
        Directory.CreateDirectory(AdventuresDirectory);
        Adventure.Stores.AdventureIndexDirectoryService.EnsureDirectory();
        Directory.CreateDirectory(LibrariesDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(Path.Combine(LibrariesDirectory, "scenarios"));
        Directory.CreateDirectory(Path.Combine(LibrariesDirectory, "worlds"));
        Directory.CreateDirectory(Path.Combine(LibrariesDirectory, "characters"));
        Directory.CreateDirectory(Path.Combine(LibrariesDirectory, "presets"));
        Directory.CreateDirectory(Path.Combine(LibrariesDirectory, "templates"));
    }

    private static void EnsureStoresInitialized()
    {
        var configRoot = ConfigRoot;
        if (string.Equals(_initializedConfigRoot, configRoot, StringComparison.OrdinalIgnoreCase))
            return;

        WrapperSettingsStore.Initialize();
        DialogLayoutStore.Initialize();
        AdventureLocationStore.Initialize();
        AdventureRootPaths.AdventureDirectoryResolver = AdventureDirectory;
        _initializedConfigRoot = configRoot;
        Adventure.Stores.AdventureIndexDirectoryService.RebuildAll();
    }
}
