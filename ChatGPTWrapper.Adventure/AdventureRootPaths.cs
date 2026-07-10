namespace ChatGPTWrapper.Adventure;

/// <summary>Adventure folder resolution decoupled from WPF <c>AppDirectories</c>.</summary>
public static class AdventureRootPaths
{
    public static Func<Guid, string>? AdventureDirectoryResolver { get; set; }

    public static string AdventureDirectory(Guid adventureId) =>
        AdventureDirectoryResolver?.Invoke(adventureId)
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChatGPTWrapper",
            "adventures",
            adventureId.ToString("D"));
}
