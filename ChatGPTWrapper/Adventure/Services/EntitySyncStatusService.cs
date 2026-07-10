using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public enum EntitySyncStatus
{
    InSync,
    SourcesStale,
    NeedsPublish,
    UnresolvedDrift,
}

public static class EntitySyncStatusService
{
    public static EntitySyncStatus GetStatus(AdventureBundle bundle, Guid entityId, string category)
    {
        if (CanonReconciliationService.HasUnresolvedDrift(bundle) && IsEntityAffected(bundle, entityId))
            return EntitySyncStatus.UnresolvedDrift;

        var entityIdStr = entityId.ToString();
        var homeFile = CanonReconciliationService.FileForCategory(category);

        var driftReport = CanonReconciliationService.DetectDrift(bundle, new CanonEditContext
        {
            Category = category,
            EntityId = entityId,
        });

        var entityFiles = ResolveEntityFiles(bundle, entityIdStr, homeFile);
        if (driftReport.Files.Any(f => f.HasDrift && entityFiles.Contains(f.FileName, StringComparer.OrdinalIgnoreCase)))
            return EntitySyncStatus.SourcesStale;

        if (NeedsPublish(bundle, entityIdStr, homeFile))
            return EntitySyncStatus.NeedsPublish;

        return EntitySyncStatus.InSync;
    }

    public static string BadgeText(EntitySyncStatus status) => status switch
    {
        EntitySyncStatus.UnresolvedDrift => "drift",
        EntitySyncStatus.SourcesStale => "stale",
        EntitySyncStatus.NeedsPublish => "publish",
        _ => "",
    };

    public static string BadgeTooltip(EntitySyncStatus status) => status switch
    {
        EntitySyncStatus.UnresolvedDrift => "Sources out of sync — click to reconcile",
        EntitySyncStatus.SourcesStale => "Sources stale — click to reconcile",
        EntitySyncStatus.NeedsPublish => "Needs publish — click to open Source Manager",
        _ => "",
    };

    private static bool IsEntityAffected(AdventureBundle bundle, Guid entityId)
    {
        var id = entityId.ToString();
        var notify = bundle.SourceManifest.CanonChangeNotify;
        if (notify?.Hints.Any(h => h.EntityIds.Contains(id, StringComparer.OrdinalIgnoreCase)) == true)
            return true;

        return bundle.SourceManifest.Entries
            .SelectMany(e => e.Sections)
            .Any(s => string.Equals(s.SourceEntityId, id, StringComparison.OrdinalIgnoreCase));
    }

    private static HashSet<string> ResolveEntityFiles(AdventureBundle bundle, string entityId, string? homeFile)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(homeFile))
            files.Add(homeFile);

        foreach (var entry in bundle.SourceManifest.Entries)
        {
            if (entry.Sections.Any(s => string.Equals(s.SourceEntityId, entityId, StringComparison.OrdinalIgnoreCase)))
                files.Add(entry.RelativePath);
        }

        return files;
    }

    private static bool NeedsPublish(AdventureBundle bundle, string entityId, string? homeFile)
    {
        if (string.IsNullOrEmpty(homeFile))
            return false;

        var entry = bundle.SourceManifest.Entries
            .FirstOrDefault(e => string.Equals(e.RelativePath, homeFile, StringComparison.OrdinalIgnoreCase));
        if (entry is null || !entry.IsManuallyPublished)
            return false;

        var hints = SectionDiffService.GetChangedSectionsSincePublish(entry);
        return hints.Any(h => entry.Sections
            .Any(s => string.Equals(s.Id, h.SectionId, StringComparison.OrdinalIgnoreCase)
                      && string.Equals(s.SourceEntityId, entityId, StringComparison.OrdinalIgnoreCase)));
    }
}
