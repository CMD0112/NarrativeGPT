using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper.Adventure.Services;

internal static class AdventureThreadRegistryService
{
    public static string KindKey(AdventureThreadKind kind) => kind.ToString();

    public static bool EnsureMigrated(AdventureBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var metadata = bundle.Metadata;
        if (metadata.ThreadRegistryMigratedAt is not null)
        {
            BackfillMissingActiveEntries(bundle);
            PullLegacyBindingsIntoRegistry(bundle);
            SyncLegacyFields(metadata);
            return false;
        }

        metadata.ThreadRegistry ??= [];
        metadata.ActiveThreadIds ??= new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        var hasPlayBinding = !string.IsNullOrWhiteSpace(metadata.LinkedConversationId)
                             || !string.IsNullOrWhiteSpace(metadata.PinnedPlayTabKey)
                             || !string.IsNullOrWhiteSpace(metadata.PinnedPlayTabUrl);

        if (hasPlayBinding)
        {
            var playEntry = new AdventureThreadEntry
            {
                Kind = AdventureThreadKind.Play,
                Label = "Play",
                ConversationId = metadata.LinkedConversationId ?? "",
                PinnedTabKey = metadata.PinnedPlayTabKey,
                PinnedTabTitle = metadata.PinnedPlayTabTitle,
                PinnedTabUrl = metadata.PinnedPlayTabUrl,
                Status = AdventureThreadStatus.Active,
                CreatedAt = metadata.CreatedAt,
            };
            metadata.ThreadRegistry.Add(playEntry);
            metadata.ActiveThreadIds[KindKey(AdventureThreadKind.Play)] = playEntry.Id;
        }

        foreach (var archived in metadata.PlayThreadArchive ?? [])
        {
            if (string.IsNullOrWhiteSpace(archived.ConversationId))
                continue;

            if (metadata.ThreadRegistry.Any(e =>
                    e.Kind == AdventureThreadKind.Play
                    && string.Equals(e.ConversationId, archived.ConversationId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            metadata.ThreadRegistry.Add(new AdventureThreadEntry
            {
                Kind = AdventureThreadKind.Play,
                Label = "Play (archived)",
                ConversationId = archived.ConversationId,
                Status = AdventureThreadStatus.Archived,
                CreatedAt = archived.ArchivedAt,
                ArchivedAt = archived.ArchivedAt,
                AcceptedTurnCountAtArchive = archived.AcceptedTurnCountAtArchive,
            });
        }

        var designConversationId = metadata.UtilitySessions is not null
                                   && metadata.UtilitySessions.TryGetValue(GenerationJobId.DesignAdventure, out var designSession)
            ? designSession.ConversationId
            : "";

        var hasDesignBinding = !string.IsNullOrWhiteSpace(designConversationId)
                               || !string.IsNullOrWhiteSpace(metadata.PinnedDesignTabKey)
                               || !string.IsNullOrWhiteSpace(metadata.PinnedDesignTabUrl);

        if (hasDesignBinding)
        {
            var designEntry = new AdventureThreadEntry
            {
                Kind = AdventureThreadKind.Design,
                Label = "Design",
                ConversationId = designConversationId,
                PinnedTabKey = metadata.PinnedDesignTabKey,
                PinnedTabTitle = metadata.PinnedDesignTabTitle,
                PinnedTabUrl = metadata.PinnedDesignTabUrl,
                Status = AdventureThreadStatus.Active,
                CreatedAt = metadata.CreatedAt,
            };
            metadata.ThreadRegistry.Add(designEntry);
            metadata.ActiveThreadIds[KindKey(AdventureThreadKind.Design)] = designEntry.Id;
        }

        if (!string.IsNullOrWhiteSpace(metadata.PinnedUtilityTabKey))
        {
            var utilityEntry = new AdventureThreadEntry
            {
                Kind = AdventureThreadKind.Utility,
                Label = "Utility",
                PinnedTabKey = metadata.PinnedUtilityTabKey,
                PinnedTabTitle = metadata.PinnedUtilityTabTitle,
                Status = AdventureThreadStatus.Active,
                CreatedAt = metadata.CreatedAt,
            };
            metadata.ThreadRegistry.Add(utilityEntry);
            metadata.ActiveThreadIds[KindKey(AdventureThreadKind.Utility)] = utilityEntry.Id;
        }

        metadata.ThreadRegistryMigratedAt = DateTimeOffset.UtcNow;
        PullLegacyBindingsIntoRegistry(bundle);
        SyncLegacyFields(metadata);
        return true;
    }

    /// <summary>
    /// When legacy singleton fields were updated outside the registry (rollout shim), merge into active entries
    /// before pushing registry state back to legacy fields.
    /// </summary>
    private static void PullLegacyBindingsIntoRegistry(AdventureBundle bundle)
    {
        var metadata = bundle.Metadata;
        metadata.ThreadRegistry ??= [];
        metadata.ActiveThreadIds ??= new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        if (TryGetActiveEntry(metadata, AdventureThreadKind.Play) is { } playEntry)
        {
            if (!string.IsNullOrWhiteSpace(metadata.LinkedConversationId)
                && !string.Equals(playEntry.ConversationId, metadata.LinkedConversationId, StringComparison.OrdinalIgnoreCase))
            {
                playEntry.ConversationId = metadata.LinkedConversationId;
            }

            playEntry.PinnedTabKey = metadata.PinnedPlayTabKey;
            playEntry.PinnedTabTitle = metadata.PinnedPlayTabTitle;
            playEntry.PinnedTabUrl = metadata.PinnedPlayTabUrl;
        }

        if (TryGetActiveEntry(metadata, AdventureThreadKind.Design) is { } designEntry)
        {
            var designConversationId = metadata.UtilitySessions is not null
                                       && metadata.UtilitySessions.TryGetValue(
                                           GenerationJobId.DesignAdventure,
                                           out var designSession)
                ? designSession.ConversationId
                : "";

            if (!string.IsNullOrWhiteSpace(designConversationId)
                && !string.Equals(designEntry.ConversationId, designConversationId, StringComparison.OrdinalIgnoreCase))
            {
                designEntry.ConversationId = designConversationId;
            }

            designEntry.PinnedTabKey = metadata.PinnedDesignTabKey;
            designEntry.PinnedTabTitle = metadata.PinnedDesignTabTitle;
            designEntry.PinnedTabUrl = metadata.PinnedDesignTabUrl;
        }

        if (TryGetActiveEntry(metadata, AdventureThreadKind.Utility) is { } utilityEntry)
        {
            utilityEntry.PinnedTabKey = metadata.PinnedUtilityTabKey;
            utilityEntry.PinnedTabTitle = metadata.PinnedUtilityTabTitle;
        }
    }

    /// <summary>
    /// When migration marker is set but legacy bindings were added later (e.g. new adventure + manual link),
    /// materialize missing registry slots without re-running full migration.
    /// </summary>
    private static void BackfillMissingActiveEntries(AdventureBundle bundle)
    {
        var metadata = bundle.Metadata;
        metadata.ThreadRegistry ??= [];
        metadata.ActiveThreadIds ??= new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        if (TryGetActiveEntry(metadata, AdventureThreadKind.Play) is null)
        {
            var hasPlayBinding = !string.IsNullOrWhiteSpace(metadata.LinkedConversationId)
                                 || !string.IsNullOrWhiteSpace(metadata.PinnedPlayTabKey)
                                 || !string.IsNullOrWhiteSpace(metadata.PinnedPlayTabUrl);
            if (hasPlayBinding)
            {
                var playEntry = new AdventureThreadEntry
                {
                    Kind = AdventureThreadKind.Play,
                    Label = "Play",
                    ConversationId = metadata.LinkedConversationId ?? "",
                    PinnedTabKey = metadata.PinnedPlayTabKey,
                    PinnedTabTitle = metadata.PinnedPlayTabTitle,
                    PinnedTabUrl = metadata.PinnedPlayTabUrl,
                    Status = AdventureThreadStatus.Active,
                    CreatedAt = metadata.CreatedAt,
                };
                metadata.ThreadRegistry.Add(playEntry);
                metadata.ActiveThreadIds[KindKey(AdventureThreadKind.Play)] = playEntry.Id;
            }
        }

        if (TryGetActiveEntry(metadata, AdventureThreadKind.Design) is null)
        {
            var designConversationId = metadata.UtilitySessions is not null
                                       && metadata.UtilitySessions.TryGetValue(
                                           GenerationJobId.DesignAdventure,
                                           out var designSession)
                ? designSession.ConversationId
                : "";

            var hasDesignBinding = !string.IsNullOrWhiteSpace(designConversationId)
                                   || !string.IsNullOrWhiteSpace(metadata.PinnedDesignTabKey)
                                   || !string.IsNullOrWhiteSpace(metadata.PinnedDesignTabUrl);
            if (hasDesignBinding)
            {
                var designEntry = new AdventureThreadEntry
                {
                    Kind = AdventureThreadKind.Design,
                    Label = "Design",
                    ConversationId = designConversationId,
                    PinnedTabKey = metadata.PinnedDesignTabKey,
                    PinnedTabTitle = metadata.PinnedDesignTabTitle,
                    PinnedTabUrl = metadata.PinnedDesignTabUrl,
                    Status = AdventureThreadStatus.Active,
                    CreatedAt = metadata.CreatedAt,
                };
                metadata.ThreadRegistry.Add(designEntry);
                metadata.ActiveThreadIds[KindKey(AdventureThreadKind.Design)] = designEntry.Id;
            }
        }

        if (TryGetActiveEntry(metadata, AdventureThreadKind.Utility) is null
            && !string.IsNullOrWhiteSpace(metadata.PinnedUtilityTabKey))
        {
            var utilityEntry = new AdventureThreadEntry
            {
                Kind = AdventureThreadKind.Utility,
                Label = "Utility",
                PinnedTabKey = metadata.PinnedUtilityTabKey,
                PinnedTabTitle = metadata.PinnedUtilityTabTitle,
                Status = AdventureThreadStatus.Active,
                CreatedAt = metadata.CreatedAt,
            };
            metadata.ThreadRegistry.Add(utilityEntry);
            metadata.ActiveThreadIds[KindKey(AdventureThreadKind.Utility)] = utilityEntry.Id;
        }
    }

    public static AdventureThreadEntry? GetEntry(AdventureBundle bundle, Guid entryId) =>
        bundle.Metadata.ThreadRegistry?.FirstOrDefault(e => e.Id == entryId);

    public static AdventureThreadEntry? GetActiveEntry(AdventureBundle bundle, AdventureThreadKind kind)
    {
        EnsureMigrated(bundle);
        var metadata = bundle.Metadata;
        if (metadata.ActiveThreadIds is null
            || !metadata.ActiveThreadIds.TryGetValue(KindKey(kind), out var id))
        {
            return null;
        }

        return metadata.ThreadRegistry?.FirstOrDefault(e => e.Id == id);
    }

    public static string? GetActiveConversationId(AdventureBundle bundle, AdventureThreadKind kind) =>
        GetActiveEntry(bundle, kind) is { ConversationId: var id } && !string.IsNullOrWhiteSpace(id)
            ? id
            : null;

    public static IReadOnlyList<AdventureThreadEntry> ListEntries(
        AdventureBundle bundle,
        AdventureThreadKind kind,
        bool includeArchived = true)
    {
        EnsureMigrated(bundle);
        var entries = bundle.Metadata.ThreadRegistry?
            .Where(e => e.Kind == kind)
            ?? Enumerable.Empty<AdventureThreadEntry>();

        if (!includeArchived)
            entries = entries.Where(e => e.Status != AdventureThreadStatus.Archived);

        return entries
            .OrderByDescending(e => e.Status == AdventureThreadStatus.Active)
            .ThenByDescending(e => e.CreatedAt)
            .ToList();
    }

    public static bool IsActiveEntry(AdventureBundle bundle, Guid entryId)
    {
        var entry = GetEntry(bundle, entryId);
        if (entry is null)
            return false;

        return GetActiveEntry(bundle, entry.Kind)?.Id == entryId;
    }

    public static AdventureThreadEntry RegisterEntry(
        AdventureBundle bundle,
        AdventureThreadKind kind,
        string? label = null,
        string? conversationId = null)
    {
        EnsureMigrated(bundle);
        var metadata = bundle.Metadata;
        metadata.ThreadRegistry ??= [];

        var entry = new AdventureThreadEntry
        {
            Kind = kind,
            Label = string.IsNullOrWhiteSpace(label) ? DefaultLabel(kind) : label.Trim(),
            ConversationId = conversationId ?? "",
            Status = AdventureThreadStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        metadata.ThreadRegistry.Add(entry);
        return entry;
    }

    public static void SetActivePin(AdventureBundle bundle, Guid entryId, bool notifyPlayThreadChanged = true)
    {
        EnsureMigrated(bundle);
        var entry = GetEntry(bundle, entryId)
                    ?? throw new InvalidOperationException("Thread entry not found.");

        if (entry.Status == AdventureThreadStatus.Archived)
            throw new InvalidOperationException("Cannot activate an archived thread.");

        var metadata = bundle.Metadata;
        metadata.ActiveThreadIds ??= new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        string? previousPlayConversation = null;
        if (entry.Kind == AdventureThreadKind.Play && notifyPlayThreadChanged)
            previousPlayConversation = metadata.LinkedConversationId;

        metadata.ActiveThreadIds[KindKey(entry.Kind)] = entryId;
        SyncLegacyFields(metadata);

        if (entry.Kind == AdventureThreadKind.Play && notifyPlayThreadChanged)
        {
            var newConversation = entry.ConversationId;
            if (!string.IsNullOrWhiteSpace(newConversation)
                && !string.Equals(previousPlayConversation, newConversation, StringComparison.OrdinalIgnoreCase))
            {
                PlayTurnScopeService.OnPlayThreadChanged(bundle, previousPlayConversation, newConversation);
            }
        }
    }

    public static void UpdatePinFromWebView(
        AdventureBundle bundle,
        Guid entryId,
        WebView2 webView,
        TabControl tabs,
        string? sourceUrl = null)
    {
        EnsureMigrated(bundle);
        var entry = GetEntry(bundle, entryId)
                    ?? throw new InvalidOperationException("Thread entry not found.");

        var key = PlayTabPinService.GetOrAssignTabKey(
            PlayTabPinService.FindTabItem(webView, tabs)
            ?? throw new InvalidOperationException("WebView is not in a tab."));

        entry.PinnedTabKey = key;
        entry.PinnedTabTitle = PlayTabPinService.GetTabTitle(webView, tabs);
        entry.PinnedTabUrl = sourceUrl ?? webView.CoreWebView2?.Source;

        if (IsActiveEntry(bundle, entryId))
            SyncLegacyFields(bundle.Metadata);
    }

    public static void UpdateConversationId(AdventureBundle bundle, Guid entryId, string conversationId)
    {
        EnsureMigrated(bundle);
        var entry = GetEntry(bundle, entryId)
                    ?? throw new InvalidOperationException("Thread entry not found.");

        entry.ConversationId = conversationId;
        if (IsActiveEntry(bundle, entryId))
            SyncLegacyFields(bundle.Metadata);
    }

    public static void ArchiveEntry(AdventureBundle bundle, Guid entryId, string? reason = null)
    {
        EnsureMigrated(bundle);
        var entry = GetEntry(bundle, entryId)
                    ?? throw new InvalidOperationException("Thread entry not found.");

        if (IsActiveEntry(bundle, entryId))
            throw new InvalidOperationException("Switch active pin before archiving the current thread.");

        entry.Status = AdventureThreadStatus.Archived;
        entry.ArchivedAt = DateTimeOffset.UtcNow;
        _ = reason;
    }

    /// <summary>
    /// Archives the active thread for kind and clears the active pin (rotation prelude).
    /// </summary>
    public static void ReleaseActiveThread(AdventureBundle bundle, AdventureThreadKind kind)
    {
        EnsureMigrated(bundle);
        var metadata = bundle.Metadata;
        if (GetActiveEntry(bundle, kind) is not { } active)
        {
            ClearLegacyFieldsForKind(metadata, kind);
            return;
        }

        active.Status = AdventureThreadStatus.Archived;
        active.ArchivedAt = DateTimeOffset.UtcNow;
        active.PinnedTabKey = null;
        active.PinnedTabTitle = null;
        active.PinnedTabUrl = null;

        metadata.ActiveThreadIds?.Remove(KindKey(kind));
        ClearLegacyFieldsForKind(metadata, kind);

        if (kind == AdventureThreadKind.Design)
        {
            var jobId = GenerationJobId.DesignAdventure;
            if (metadata.UtilitySessions?.TryGetValue(jobId, out var session) == true)
                GenerationUtilitySessionService.ArchiveSession(metadata, jobId, session, "manual_rotate");
        }
    }

    public static void SyncLegacyFields(AdventureMetadata metadata)
    {
        metadata.ThreadRegistry ??= [];
        metadata.ActiveThreadIds ??= new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        SyncPlayLegacy(metadata);
        SyncDesignLegacy(metadata);
        SyncUtilityLegacy(metadata);
    }

    private static void SyncPlayLegacy(AdventureMetadata metadata)
    {
        if (TryGetActiveEntry(metadata, AdventureThreadKind.Play) is not { } entry)
        {
            metadata.LinkedConversationId = null;
            metadata.PinnedPlayTabKey = null;
            metadata.PinnedPlayTabTitle = null;
            metadata.PinnedPlayTabUrl = null;
            if (metadata.ProjectLink is not null)
                metadata.ProjectLink.PlayConversationId = null;
            return;
        }

        metadata.LinkedConversationId = string.IsNullOrWhiteSpace(entry.ConversationId)
            ? null
            : entry.ConversationId;
        metadata.PinnedPlayTabKey = entry.PinnedTabKey;
        metadata.PinnedPlayTabTitle = entry.PinnedTabTitle;
        metadata.PinnedPlayTabUrl = entry.PinnedTabUrl;

        if (!string.IsNullOrWhiteSpace(entry.ConversationId) && metadata.ProjectLink is not null)
            metadata.ProjectLink.PlayConversationId = entry.ConversationId;
    }

    private static void SyncDesignLegacy(AdventureMetadata metadata)
    {
        if (TryGetActiveEntry(metadata, AdventureThreadKind.Design) is not { } entry)
        {
            metadata.PinnedDesignTabKey = null;
            metadata.PinnedDesignTabTitle = null;
            metadata.PinnedDesignTabUrl = null;
            metadata.UtilitySessions?.Remove(GenerationJobId.DesignAdventure);
            return;
        }

        metadata.PinnedDesignTabKey = entry.PinnedTabKey;
        metadata.PinnedDesignTabTitle = entry.PinnedTabTitle;
        metadata.PinnedDesignTabUrl = entry.PinnedTabUrl;

        if (string.IsNullOrWhiteSpace(entry.ConversationId))
            return;

        metadata.UtilitySessions ??=
            new Dictionary<string, GenerationUtilitySession>(StringComparer.OrdinalIgnoreCase);

        if (metadata.UtilitySessions.TryGetValue(GenerationJobId.DesignAdventure, out var existing))
        {
            existing.ConversationId = entry.ConversationId;
            existing.LastUsedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            metadata.UtilitySessions[GenerationJobId.DesignAdventure] = new GenerationUtilitySession
            {
                ConversationId = entry.ConversationId,
                Sequence = GenerationUtilitySessionService.GetNextSequence(metadata, GenerationJobId.DesignAdventure),
                CreatedAt = DateTimeOffset.UtcNow,
                LastUsedAt = DateTimeOffset.UtcNow,
            };
        }
    }

    private static void SyncUtilityLegacy(AdventureMetadata metadata)
    {
        if (TryGetActiveEntry(metadata, AdventureThreadKind.Utility) is not { } entry)
        {
            metadata.PinnedUtilityTabKey = null;
            metadata.PinnedUtilityTabTitle = null;
            return;
        }

        metadata.PinnedUtilityTabKey = entry.PinnedTabKey;
        metadata.PinnedUtilityTabTitle = entry.PinnedTabTitle;
    }

    private static AdventureThreadEntry? TryGetActiveEntry(AdventureMetadata metadata, AdventureThreadKind kind)
    {
        if (metadata.ActiveThreadIds is null
            || !metadata.ActiveThreadIds.TryGetValue(KindKey(kind), out var id))
        {
            return null;
        }

        return metadata.ThreadRegistry?.FirstOrDefault(e => e.Id == id);
    }

    private static void ClearLegacyFieldsForKind(AdventureMetadata metadata, AdventureThreadKind kind)
    {
        switch (kind)
        {
            case AdventureThreadKind.Play:
                metadata.LinkedConversationId = null;
                metadata.PinnedPlayTabKey = null;
                metadata.PinnedPlayTabTitle = null;
                metadata.PinnedPlayTabUrl = null;
                if (metadata.ProjectLink is not null)
                    metadata.ProjectLink.PlayConversationId = null;
                break;
            case AdventureThreadKind.Design:
                metadata.PinnedDesignTabKey = null;
                metadata.PinnedDesignTabTitle = null;
                metadata.PinnedDesignTabUrl = null;
                break;
            case AdventureThreadKind.Utility:
                metadata.PinnedUtilityTabKey = null;
                metadata.PinnedUtilityTabTitle = null;
                break;
        }
    }

    public static string? GetTargetUrl(AdventureBundle bundle, AdventureThreadKind kind)
    {
        EnsureMigrated(bundle);
        var entry = GetActiveEntry(bundle, kind);
        return entry is null ? null : GetEntryTargetUrl(bundle, entry);
    }

    public static string? GetEntryTargetUrl(AdventureBundle bundle, AdventureThreadEntry entry)
    {
        EnsureMigrated(bundle);
        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);

        if (!string.IsNullOrWhiteSpace(entry.ConversationId) && !string.IsNullOrWhiteSpace(gizmoId))
            return ChatGptUrls.BuildProjectConversationUrl(entry.ConversationId, gizmoId);

        if (!string.IsNullOrWhiteSpace(entry.ConversationId))
            return ChatGptUrls.BuildConversationUrl(entry.ConversationId);

        if (!string.IsNullOrWhiteSpace(entry.PinnedTabUrl)
            && ChatGptUrls.TryCreateTrustedNavigationUri(entry.PinnedTabUrl, out _)
            && !AdventureNavigationService.IsGenericHomepage(entry.PinnedTabUrl))
        {
            return entry.PinnedTabUrl;
        }

        if (entry.Kind == AdventureThreadKind.Play && !string.IsNullOrWhiteSpace(gizmoId))
            return ChatGptUrls.BuildProjectUrl(gizmoId);

        return null;
    }

    public static void UpdateEntryLabel(AdventureBundle bundle, Guid entryId, string label)
    {
        EnsureMigrated(bundle);
        var entry = GetEntry(bundle, entryId)
                    ?? throw new InvalidOperationException("Thread entry not found.");

        entry.Label = string.IsNullOrWhiteSpace(label)
            ? DefaultLabel(entry.Kind)
            : label.Trim();
    }

    public static string FormatThreadStatus(AdventureBundle bundle, AdventureThreadKind kind)
    {
        EnsureMigrated(bundle);
        var entry = GetActiveEntry(bundle, kind);
        if (entry is null)
        {
            return kind switch
            {
                AdventureThreadKind.Play =>
                    "Play thread: not bound — link Project and pin a play tab.",
                AdventureThreadKind.Design =>
                    "Design thread: not bound — use Start new design thread… or pin a tab.",
                AdventureThreadKind.Utility => "Utility thread: not pinned.",
                _ => "Thread: not bound.",
            };
        }

        var labelPart = string.IsNullOrWhiteSpace(entry.Label) || entry.Label == DefaultLabel(kind)
            ? ""
            : $" ({entry.Label})";

        if (string.IsNullOrWhiteSpace(entry.ConversationId))
            return $"{DefaultLabel(kind)} thread{labelPart}: pinned tab — conversation not bound yet.";

        var shortId = entry.ConversationId.Length > 12
            ? entry.ConversationId[..12] + "…"
            : entry.ConversationId;
        return $"{DefaultLabel(kind)} thread{labelPart}: {shortId}";
    }

    public static string DefaultLabel(AdventureThreadKind kind) => kind switch
    {
        AdventureThreadKind.Play => "Play",
        AdventureThreadKind.Design => "Design",
        AdventureThreadKind.Utility => "Utility",
        _ => kind.ToString(),
    };

    public static AdventureThreadEntry BeginNewActiveThread(
        AdventureBundle bundle,
        AdventureThreadKind kind,
        string? label = null)
    {
        ReleaseActiveThread(bundle, kind);
        var entry = RegisterEntry(bundle, kind, label);
        SetActivePin(
            bundle,
            entry.Id,
            notifyPlayThreadChanged: kind == AdventureThreadKind.Play);
        return entry;
    }

    public static AdventureThreadEntry GetOrCreateActiveEntry(
        AdventureBundle bundle,
        AdventureThreadKind kind,
        string? label = null)
    {
        EnsureMigrated(bundle);
        return GetActiveEntry(bundle, kind)
               ?? BeginNewActiveThread(bundle, kind, label);
    }

    public static void Persist(AdventureBundle bundle, bool allowLinkMetadataOverwrite = false) =>
        AdventureStore.Save(bundle, allowLinkMetadataOverwrite);
}
