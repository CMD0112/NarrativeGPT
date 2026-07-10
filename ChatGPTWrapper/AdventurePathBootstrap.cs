using ChatGPTWrapper.Adventure;

namespace ChatGPTWrapper;

/// <summary>Wires adventure path resolution for shared library consumers.</summary>
public static class AdventurePathBootstrap
{
    public static void Register() =>
        AdventureRootPaths.AdventureDirectoryResolver = AppDirectories.AdventureDirectory;
}
