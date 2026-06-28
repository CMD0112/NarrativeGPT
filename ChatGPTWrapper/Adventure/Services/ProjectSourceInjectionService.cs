using System.IO;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

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

    public int LoreEntryCount { get; init; }

    public string? ProbeWarning { get; init; }

    public string ModeLabel =>
        CanDelegateStaticContent
            ? "manual publish | source-delegated"
            : HasLinkedProject
                ? "inline fallback"
                : "minimal local";
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
        AdventureProjectBindingService.PrepareBundleForProjectLink(bundle);
        var publishMode = SourcePublishMode.Manual;
        var hasLinked = AdventureProjectBindingService.HasLinkedProject(bundle);
        var loreEntries = GetLoreEntries(bundle);
        RefreshLoreHashesFromDisk(bundle, loreEntries);
        return EvaluateManual(bundle, publishMode, hasLinked, loreEntries);
    }

    /// <summary>
    /// Materializes core lore <c>sources/*.md</c> and manifest rows when a linked adventure has none yet.
    /// Persists <c>source-manifest.json</c> when rows are created or reconciled from disk.
    /// </summary>
    public static bool EnsureLoreSourcesMaterialized(AdventureBundle bundle)
    {
        if (!AdventureProjectBindingService.HasLinkedProject(bundle))
            return false;

        var diskEntryCount = AdventureStore.LoadSourceManifest(bundle.Metadata.Id).Entries.Count;
        var diskManifestEmpty = diskEntryCount == 0;

        AdventureSourceFileService.EnsureLayout(bundle);
        var reconciled = AdventureSourceFileService.ReconcileManifest(bundle);

        if (GetLoreEntries(bundle).Count > 0 && !AdventureSourceFileService.HasLocalLoreSourceFiles(bundle))
        {
            bundle.SourceManifest.Entries.RemoveAll(e => SourceManifestHelper.IsCoreLoreFile(e.RelativePath));
            reconciled = true;
        }

        var bootstrapChanged = false;
        if (GetLoreEntries(bundle).Count == 0)
        {
            bootstrapChanged = AdventureSourceFileService.TryBootstrapLocalSourcesFromDesignWorkspace(bundle) > 0;
            reconciled |= AdventureSourceFileService.ReconcileManifest(bundle);
        }

        var exportChanged = false;
        if (GetLoreEntries(bundle).Count == 0)
            exportChanged = ProjectSourceExportService.ExportForce(bundle);

        if (GetLoreEntries(bundle).Count == 0)
            return bootstrapChanged || exportChanged || reconciled;

        if (!AdventureSourceFileService.HasLocalLoreSourceFiles(bundle))
        {
            exportChanged |= ProjectSourceExportService.ExportForce(bundle);
            reconciled |= AdventureSourceFileService.ReconcileManifest(bundle);
        }

        var shouldPersist = diskManifestEmpty || reconciled || bootstrapChanged || exportChanged;
        if (shouldPersist)
            AdventureStore.SaveSourceManifestOnly(bundle);

        return shouldPersist;
    }

    /// <summary>
    /// Refreshes a manifest row hash from on-disk <c>sources/</c> or from the export pipeline when the file is missing.
    /// </summary>
    public static bool TryRefreshEntryHash(AdventureBundle bundle, SourceManifestEntry entry)
    {
        var sourcesDir = ProjectSourceExportService.SourcesDirectory(bundle);
        var absolutePath = Path.Combine(sourcesDir, entry.RelativePath);
        if (File.Exists(absolutePath))
        {
            var hash = ProjectSourceExportService.ComputeManifestLocalSha256(entry.RelativePath, absolutePath);
            entry.LocalSha256 = hash;
            entry.Sha256 = hash;
            return !string.IsNullOrEmpty(hash);
        }

        if (!ProjectSourceExportService.TryGetExportContent(entry.RelativePath, bundle, out var content, out var sections))
            return false;

        var normalized = content.Trim() + Environment.NewLine;
        var contentHash = ProjectSourceExportService.ComputeSha256Bytes(
            System.Text.Encoding.UTF8.GetBytes(normalized));
        entry.LocalSha256 = contentHash;
        entry.Sha256 = contentHash;
        entry.Sections = sections;
        return true;
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

        if (bundle.Metadata.Settings.ForceInlineLore)
        {
            blockingReason = "Force inline lore is enabled in adventure settings";
            suggestedAction = "Disable force inline lore in Play settings → Behavior → Advanced automation";
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
            LoreEntryCount = loreEntries.Count,
            ProbeDifferCount = probeDiffer,
            ProbeWarning = probeWarning,
        };
    }

    private static void RefreshLoreHashesFromDisk(AdventureBundle bundle, IEnumerable<SourceManifestEntry> loreEntries)
    {
        var dir = ProjectSourceExportService.SourcesDirectory(bundle);
        foreach (var entry in loreEntries)
        {
            var path = Path.Combine(dir, entry.RelativePath);
            if (!File.Exists(path))
                continue;

            var hash = ProjectSourceExportService.ComputeManifestLocalSha256(entry.RelativePath, path);
            entry.LocalSha256 = hash;
            entry.Sha256 = hash;
        }
    }

    private static List<SourceManifestEntry> GetLoreEntries(AdventureBundle bundle)
    {
        var entries = bundle.SourceManifest.Entries ?? [];
        var core = SectionSchema.CoreLoreFiles;
        var lore = entries
            .Where(e => SourceManifestHelper.IsCoreLoreFile(e.RelativePath))
            .ToList();

        if (lore.Count >= core.Length)
            return lore;

        return entries
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
            return count == 1
                ? $"published (1 file) | source-delegated packets"
                : $"published ({count} files) | source-delegated packets";
        }

        if (readiness.NeedsRepublishCount > 0)
        {
            return readiness.NeedsRepublishCount == 1
                ? "1 needs publish | inline fallback"
                : $"{readiness.NeedsRepublishCount} need publish | inline fallback";
        }

        return readiness.BlockingReason switch
        {
            var r when r?.Contains("never exported", StringComparison.OrdinalIgnoreCase) == true
                => "not exported | inline fallback",
            var r when r?.Contains("Force inline", StringComparison.OrdinalIgnoreCase) == true
                => "forced inline lore",
            var r when r?.Contains("No ChatGPT Project", StringComparison.OrdinalIgnoreCase) == true
                => "no project | minimal local",
            _ => "not ready | inline fallback",
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
