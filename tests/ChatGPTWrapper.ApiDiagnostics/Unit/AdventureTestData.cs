using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

internal static class AdventureTestData
{
    public const string DefaultMockGizmoId = "g-p-mock-perf";

    public static readonly string[] StandardSourcePaths =
    [
        "scenario.md",
        "world.md",
        "plot.md",
        "cast.md",
        "lexicon.md",
        "instructions-snippet.md",
    ];

    public static ScenarioDocument CreatePopulatedScenario() =>
        new()
        {
            Setting = "A haunted castle on the moor",
            PlayerRole = "Investigator",
            Genre = "Gothic horror",
            OpeningSituation = "Rain lashes the drawbridge.",
            PlotEssentials = "The lord vanished three nights ago.",
            WorldRules = "Magic is subtle and rare.",
            MajorConflicts = "The household staff whisper of curses.",
            StartingConstraints = "No iron may cross the threshold.",
            Tone = "Brooding and uncanny",
        };

    public static AdventureBundle CreateLinkedBundle(
        string? projectId = DefaultMockGizmoId,
        bool inSync = true,
        bool forceFat = false,
        int entryCount = 5)
    {
        var paths = StandardSourcePaths.Take(Math.Clamp(entryCount, 1, StandardSourcePaths.Length)).ToList();
        var entries = paths
            .Select(path => new SourceManifestEntry
            {
                RelativePath = path,
                SyncState = SourceSyncState.InSync,
            })
            .ToList();

        if (!inSync && entries.Count > 0)
            entries[0].SyncState = SourceSyncState.LocalNewer;

        return new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Id = Guid.NewGuid(),
                Title = "Perf Test Adventure",
                LinkedProjectId = projectId,
                Settings = new AdventureSettings { ForceInlineLore = forceFat },
            },
            Scenario = CreatePopulatedScenario(),
            SourceManifest = new SourceManifest { Entries = entries },
        };
    }

    public static void WriteLocalSources(AdventureBundle bundle, SourceExportMode mode = SourceExportMode.Force)
    {
        if (mode == SourceExportMode.Force)
            ProjectSourceExportService.ExportForce(bundle);
        else
            ProjectSourceExportService.ExportIfStale(bundle);
    }

    public static void AppendPaddingToSource(AdventureBundle bundle, string relativePath, int byteCount)
    {
        var path = Path.Combine(ProjectSourceExportService.SourcesDirectory(bundle), relativePath);
        if (!File.Exists(path))
            throw new FileNotFoundException("Source file missing for padding.", path);

        var padding = new string('x', byteCount);
        File.AppendAllText(path, Environment.NewLine + padding);
    }

    public static void DeleteBundle(AdventureBundle bundle)
    {
        try
        {
            AdventureStore.Delete(bundle.Metadata.Id);
        }
        catch
        {
            /* ignore */
        }
    }
}
