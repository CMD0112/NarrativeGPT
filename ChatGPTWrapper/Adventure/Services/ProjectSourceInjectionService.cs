using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class ProjectSourceFileInfo
{
    public required string RelativePath { get; init; }

    public required string Role { get; init; }

    public required string Description { get; init; }
}

internal sealed class ProjectSourceReadiness
{
    public bool CanDelegateStaticContent { get; init; }

    public bool HasLinkedProject { get; init; }

    public bool HasManifestEntries { get; init; }

    public bool AllSourcesInSync { get; init; }

    public SourcePublishMode PublishMode { get; init; }

    public IReadOnlyList<ProjectSourceFileInfo> SyncedFiles { get; init; } = [];

    public string? BlockingReason { get; init; }

    public string? SuggestedAction { get; init; }

    public int OutOfSyncCount { get; init; }

    public int NeedsRepublishCount { get; init; }

    public int ProbeDifferCount { get; init; }

    public string? ProbeWarning { get; init; }

    public string ModeLabel =>
        CanDelegateStaticContent
            ? PublishMode == SourcePublishMode.Manual
                ? "manual publish | source-delegated"
                : "source-delegated"
            : "fat fallback";
}

internal static class ProjectSourceInjectionService
{
    private static readonly IReadOnlyDictionary<string, (string Role, string Description)> FileRoleMap =
        ProjectSourceFileTemplates.All.ToDictionary(
            t => t.RelativePath,
            t => (t.Role, t.Summary),
            StringComparer.OrdinalIgnoreCase);

    public static bool CanDelegateStaticContent(AdventureBundle bundle) =>
        Evaluate(bundle).CanDelegateStaticContent;

    public static ProjectSourceReadiness Evaluate(AdventureBundle bundle)
    {
        var publishMode = bundle.Metadata.Settings.SourcePublishMode;
        var hasLinked = !string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId);
        var loreEntries = GetLoreEntries(bundle);
        var hasEntries = loreEntries.Count > 0;

        if (publishMode == SourcePublishMode.Manual)
            return EvaluateManual(bundle, publishMode, hasLinked, loreEntries);

        return EvaluateApiSync(bundle, publishMode, hasLinked, hasEntries);
    }

    private static ProjectSourceReadiness EvaluateManual(
        AdventureBundle bundle,
        SourcePublishMode publishMode,
        bool hasLinked,
        List<SourceManifestEntry> loreEntries)
    {
        var needsRepublish = loreEntries.Count(e => e.NeedsManualRepublish);
        var publishedFiles = loreEntries
            .Where(e => e.IsManuallyCurrent())
            .Concat(GetPublishedReferenceEntries(bundle))
            .DistinctBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(ToFileInfo)
            .ToList();

        string? blockingReason = null;
        string? suggestedAction = null;

        if (bundle.Metadata.Settings.ForceFatPackets)
        {
            blockingReason = "Force fat packets is enabled in adventure settings";
            suggestedAction = "Disable force fat packets in Settings to use project sources";
        }
        else if (!hasLinked)
        {
            blockingReason = "No ChatGPT Project linked";
            suggestedAction = "Link a Project from the dashboard";
        }
        else if (loreEntries.Count == 0)
        {
            blockingReason = "Sources never exported for this adventure";
            suggestedAction = "Refresh export on the Sources tab";
        }
        else if (needsRepublish > 0)
        {
            blockingReason = needsRepublish == 1
                ? "1 source file needs manual publish"
                : $"{needsRepublish} source files need manual publish";
            suggestedAction = "Play settings → Sources → drag files to ChatGPT Project and check Published";
        }

        var probeDiffer = loreEntries.Count(e => e.RemoteProbeMatch == RemoteProbeMatch.Differ);
        string? probeWarning = probeDiffer > 0
            ? probeDiffer == 1
                ? "Project copy differs from canonical for 1 file — compare before play"
                : $"Project copy differs from canonical for {probeDiffer} files — compare before play"
            : null;

        return new ProjectSourceReadiness
        {
            CanDelegateStaticContent = blockingReason is null,
            HasLinkedProject = hasLinked,
            HasManifestEntries = loreEntries.Count > 0,
            AllSourcesInSync = needsRepublish == 0 && loreEntries.Count > 0,
            PublishMode = publishMode,
            SyncedFiles = publishedFiles,
            BlockingReason = blockingReason,
            SuggestedAction = suggestedAction,
            OutOfSyncCount = needsRepublish,
            NeedsRepublishCount = needsRepublish,
            ProbeDifferCount = probeDiffer,
            ProbeWarning = probeWarning,
        };
    }

    private static ProjectSourceReadiness EvaluateApiSync(
        AdventureBundle bundle,
        SourcePublishMode publishMode,
        bool hasLinked,
        bool hasEntries)
    {
        var outOfSync = bundle.SourceManifest.Entries.Count(e => e.SyncState != SourceSyncState.InSync);
        var allInSync = hasEntries && outOfSync == 0;

        var syncedFiles = bundle.SourceManifest.Entries
            .Where(e => e.SyncState == SourceSyncState.InSync)
            .OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(ToFileInfo)
            .ToList();

        string? blockingReason = null;
        string? suggestedAction = null;

        if (bundle.Metadata.Settings.ForceFatPackets)
        {
            blockingReason = "Force fat packets is enabled in adventure settings";
            suggestedAction = "Disable force fat packets in Settings to use project sources";
        }
        else if (!hasLinked)
        {
            blockingReason = "No ChatGPT Project linked";
            suggestedAction = "Link a Project from the dashboard";
        }
        else if (!hasEntries)
        {
            blockingReason = "Sources never exported for this adventure";
            suggestedAction = "Export and sync sources to the linked Project";
        }
        else if (!allInSync)
        {
            blockingReason = outOfSync == 1
                ? "1 source file out of sync"
                : $"{outOfSync} source files out of sync";
            suggestedAction = "Open source sync and apply pending changes";
        }

        return new ProjectSourceReadiness
        {
            CanDelegateStaticContent = blockingReason is null,
            HasLinkedProject = hasLinked,
            HasManifestEntries = hasEntries,
            AllSourcesInSync = allInSync,
            PublishMode = publishMode,
            SyncedFiles = syncedFiles,
            BlockingReason = blockingReason,
            SuggestedAction = suggestedAction,
            OutOfSyncCount = outOfSync,
            NeedsRepublishCount = 0,
        };
    }

    private static List<SourceManifestEntry> GetLoreEntries(AdventureBundle bundle)
    {
        var core = SectionSchema.CoreLoreFiles;
        var entries = bundle.SourceManifest.Entries
            .Where(e => SourceManifestHelper.IsCoreLoreFile(e.RelativePath))
            .ToList();

        if (entries.Count >= core.Length)
            return entries;

        return bundle.SourceManifest.Entries
            .Where(e => SourceManifestHelper.IsLoreSourceFile(e.RelativePath))
            .ToList();
    }

    private static IEnumerable<SourceManifestEntry> GetPublishedReferenceEntries(AdventureBundle bundle) =>
        bundle.SourceManifest.Entries
            .Where(e => SectionSchema.IsReferenceSourceFile(e.RelativePath) && e.IsManuallyCurrent());

    public static string BuildProjectSourcesSection(AdventureBundle bundle, ProjectSourceReadiness readiness)
    {
        if (!readiness.CanDelegateStaticContent)
            return "";

        var lines = new List<string>
        {
            "=== PROJECT SOURCES (retrieve via ChatGPT Project knowledge) ===",
            "",
            $"Project: {bundle.Metadata.LinkedProjectId}",
            "",
            "Use these uploaded source files as canonical static world material:",
            "",
        };

        foreach (var file in readiness.SyncedFiles)
            lines.Add($"- {file.RelativePath} — {file.Description}");

        var formatHints = ProjectSourceFileTemplates.BuildInlineFormatsSection(
            readiness.SyncedFiles.Select(f => f.RelativePath));
        if (!string.IsNullOrWhiteSpace(formatHints))
        {
            lines.Add("");
            lines.Add("Expected source file shapes (canonical — retrieve full content from Project):");
            lines.Add(formatHints);
        }

        lines.Add("");
        lines.Add(
            "Do not invent facts that contradict project sources. Use local sections below only for session state and recent play.");

        return string.Join('\n', lines);
    }

    public static string FormatLinkStatusSources(ProjectSourceReadiness readiness)
    {
        if (!readiness.HasLinkedProject)
            return "no project";

        if (readiness.CanDelegateStaticContent)
        {
            var count = readiness.SyncedFiles.Count;
            var prefix = readiness.PublishMode == SourcePublishMode.Manual
                ? "published"
                : "synced";
            return count == 1
                ? $"{prefix} (1 file) | source-delegated packets"
                : $"{prefix} ({count} files) | source-delegated packets";
        }

        if (readiness.PublishMode == SourcePublishMode.Manual && readiness.NeedsRepublishCount > 0)
        {
            return readiness.NeedsRepublishCount == 1
                ? "1 needs publish | fat fallback"
                : $"{readiness.NeedsRepublishCount} need publish | fat fallback";
        }

        if (readiness.OutOfSyncCount > 0)
        {
            var baseLine = readiness.OutOfSyncCount == 1
                ? "1 out of sync | fat fallback"
                : $"{readiness.OutOfSyncCount} out of sync | fat fallback";
            if (readiness.PublishMode == SourcePublishMode.ApiSync)
                return baseLine + " — switch to Manual publish (Sources tab) and use Source Manager";
            return baseLine;
        }

        return readiness.BlockingReason switch
        {
            var r when r?.Contains("never exported", StringComparison.OrdinalIgnoreCase) == true
                => "not exported | fat fallback",
            var r when r?.Contains("Force fat", StringComparison.OrdinalIgnoreCase) == true
                => "forced fat packets",
            _ => "not ready | fat fallback",
        };
    }

    private static ProjectSourceFileInfo ToFileInfo(SourceManifestEntry entry)
    {
        if (FileRoleMap.TryGetValue(entry.RelativePath, out var role))
        {
            return new ProjectSourceFileInfo
            {
                RelativePath = entry.RelativePath,
                Role = role.Role,
                Description = role.Description,
            };
        }

        return new ProjectSourceFileInfo
        {
            RelativePath = entry.RelativePath,
            Role = "Source",
            Description = "Project source file",
        };
    }
}
