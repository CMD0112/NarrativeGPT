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
            if (metadata.SchemaVersion < 6)
                BackfillMissingActiveEntries(metadata);
            MigrateDesignJobStateOnAllEntries(metadata);
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
            MigrateDesignJobStateOnEntry(designEntry, metadata);
            metadata.ThreadRegistry.Add(designEntry);
            metadata.ActiveThreadIds[KindKey(AdventureThreadKind.Design)] = designEntry.Id;
        }

        metadata.ThreadRegistryMigratedAt = DateTimeOffset.UtcNow;
        MigrateDesignJobStateOnAllEntries(metadata);
        return true;
    }

    private static void BackfillMissingActiveEntries(AdventureMetadata metadata)
    {
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
                MigrateDesignJobStateOnEntry(designEntry, metadata);
                metadata.ThreadRegistry.Add(designEntry);
                metadata.ActiveThreadIds[KindKey(AdventureThreadKind.Design)] = designEntry.Id;
            }
        }
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

    private static void MigrateDesignJobStateOnEntry(AdventureThreadEntry entry, AdventureMetadata metadata)
    {
        if (entry.Kind != AdventureThreadKind.Design || entry.DesignJobState is not null)
            return;

        if (metadata.UtilitySessions?.TryGetValue(GenerationJobId.DesignAdventure, out var session) == true)
            entry.DesignJobState = DesignThreadJobState.FromUtilitySession(session);
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

    public static DesignThreadJobState? GetActiveDesignJobState(AdventureBundle bundle)
    {
        var entry = GetActiveEntry(bundle, AdventureThreadKind.Design);
        return entry?.DesignJobState;
    }

    public static void UpdateDesignJobState(AdventureBundle bundle, Action<DesignThreadJobState> mutate)
    {
        EnsureMigrated(bundle);
        var entry = GetActiveEntry(bundle, AdventureThreadKind.Design)
                    ?? throw new InvalidOperationException("No active design thread.");

        entry.DesignJobState ??= new DesignThreadJobState
        {
            Sequence = GenerationUtilitySessionService.GetNextSequence(bundle.Metadata, GenerationJobId.DesignAdventure),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        mutate(entry.DesignJobState);
        entry.DesignJobState.LastUsedAt = DateTimeOffset.UtcNow;
    }

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
            previousPlayConversation = GetActiveConversationId(bundle, AdventureThreadKind.Play);

        metadata.ActiveThreadIds[KindKey(entry.Kind)] = entryId;

        if (entry.Kind == AdventureThreadKind.Play && notifyPlayThreadChanged)
        {
            var newConversation = entry.ConversationId;
            if (!string.IsNullOrWhiteSpace(newConversation)
                && !string.Equals(previousPlayConversation, newConversation, StringComparison.OrdinalIgnoreCase))
            {
                PlayTurnScopeService.OnPlayThreadChanged(bundle, previousPlayConversation, newConversation);
            }
        }
        else if (entry.Kind == AdventureThreadKind.Design)
        {
            SyncActiveDesignUtilitySession(bundle);
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

        var key = ThreadTabBindingService.GetOrAssignTabKey(
            ThreadTabBindingService.FindTabItem(webView, tabs)
            ?? throw new InvalidOperationException("WebView is not in a tab."));

        entry.PinnedTabKey = key;
        entry.PinnedTabTitle = ThreadTabBindingService.GetTabTitle(webView, tabs);
        entry.PinnedTabUrl = sourceUrl ?? webView.CoreWebView2?.Source;
    }

    public static void UpdateConversationId(AdventureBundle bundle, Guid entryId, string conversationId)
    {
        EnsureMigrated(bundle);
        var entry = GetEntry(bundle, entryId)
                    ?? throw new InvalidOperationException("Thread entry not found.");

        entry.ConversationId = conversationId;
    }

    /// <summary>
    /// Binds conversation id on the active thread for kind and persists legacy shim fields.
    /// </summary>
    public static void BindActiveConversation(
        AdventureBundle bundle,
        AdventureThreadKind kind,
        string conversationId,
        bool notifyPlayThreadChanged = true)
    {
        EnsureMigrated(bundle);
        var previous = kind == AdventureThreadKind.Play && notifyPlayThreadChanged
            ? GetActiveConversationId(bundle, AdventureThreadKind.Play)
            : null;

        var entry = GetOrCreateActiveEntry(bundle, kind);
        entry.ConversationId = conversationId;
        SetActivePin(bundle, entry.Id, notifyPlayThreadChanged: false);

        if (kind == AdventureThreadKind.Play
            && notifyPlayThreadChanged
            && !string.IsNullOrWhiteSpace(conversationId)
            && !string.Equals(previous, conversationId, StringComparison.OrdinalIgnoreCase))
        {
            PlayTurnScopeService.OnPlayThreadChanged(bundle, previous, conversationId);
        }
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

    public static void RemoveEntry(AdventureBundle bundle, Guid entryId)
    {
        EnsureMigrated(bundle);
        var metadata = bundle.Metadata;
        metadata.ThreadRegistry ??= [];

        if (IsActiveEntry(bundle, entryId))
            throw new InvalidOperationException("Cannot remove the active thread. Set another thread active first.");

        var entry = GetEntry(bundle, entryId)
                    ?? throw new InvalidOperationException("Thread entry not found.");

        if (entry.Kind == AdventureThreadKind.Design)
        {
            PurgeDesignUtilitySessionForConversation(bundle, entry.ConversationId);
            SyncActiveDesignUtilitySession(bundle);
        }

        metadata.ThreadRegistry.Remove(entry);
    }

    public static void ClearEntryPin(AdventureBundle bundle, Guid entryId)
    {
        EnsureMigrated(bundle);
        var entry = GetEntry(bundle, entryId)
                    ?? throw new InvalidOperationException("Thread entry not found.");

        entry.PinnedTabKey = null;
        entry.PinnedTabTitle = null;
        entry.PinnedTabUrl = null;
    }

    public static bool EntryHasPin(AdventureThreadEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.PinnedTabKey)
        || !string.IsNullOrWhiteSpace(entry.PinnedTabUrl);

    public static void ClearLegacyDesignBindingFields(AdventureMetadata metadata)
    {
        metadata.PinnedDesignTabKey = null;
        metadata.PinnedDesignTabTitle = null;
        metadata.PinnedDesignTabUrl = null;
    }

    /// <summary>
    /// Keeps <c>UtilitySessions[design_adventure]</c> aligned with the active design registry row.
    /// </summary>
    public static void SyncActiveDesignUtilitySession(AdventureBundle bundle)
    {
        EnsureMigrated(bundle);
        var metadata = bundle.Metadata;
        var jobId = GenerationJobId.DesignAdventure;
        metadata.UtilitySessions ??= new Dictionary<string, GenerationUtilitySession>(StringComparer.OrdinalIgnoreCase);

        var entry = GetActiveEntry(bundle, AdventureThreadKind.Design);
        if (entry is null || string.IsNullOrWhiteSpace(entry.ConversationId))
        {
            metadata.UtilitySessions.Remove(jobId);
            return;
        }

        GenerationUtilitySession session;
        if (entry.DesignJobState is { } jobState)
        {
            session = jobState.ToUtilitySession(entry.ConversationId);
        }
        else if (metadata.UtilitySessions.TryGetValue(jobId, out var existing)
                 && string.Equals(existing.ConversationId, entry.ConversationId, StringComparison.OrdinalIgnoreCase))
        {
            session = existing;
        }
        else
        {
            session = new GenerationUtilitySession
            {
                ConversationId = entry.ConversationId,
                Sequence = GenerationUtilitySessionService.GetNextSequence(metadata, jobId),
                SeedVersion = GenerationUtilitySessionService.GetSeedVersion(bundle, jobId),
                CreatedAt = DateTimeOffset.UtcNow,
                LastUsedAt = DateTimeOffset.UtcNow,
            };
        }

        metadata.UtilitySessions[jobId] = session;
    }

    public static void ReleaseActiveThread(AdventureBundle bundle, AdventureThreadKind kind)
    {
        EnsureMigrated(bundle);
        var metadata = bundle.Metadata;
        if (GetActiveEntry(bundle, kind) is not { } active)
        {
            metadata.ActiveThreadIds?.Remove(KindKey(kind));
            return;
        }

        active.Status = AdventureThreadStatus.Archived;
        active.ArchivedAt = DateTimeOffset.UtcNow;
        active.PinnedTabKey = null;
        active.PinnedTabTitle = null;
        active.PinnedTabUrl = null;
        active.DesignJobState = null;

        metadata.ActiveThreadIds?.Remove(KindKey(kind));
        ClearLegacyBindingFieldsForKind(metadata, kind);
    }

    private static void ClearLegacyBindingFieldsForKind(AdventureMetadata metadata, AdventureThreadKind kind)
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
                ClearLegacyDesignBindingFields(metadata);
                metadata.UtilitySessions?.Remove(GenerationJobId.DesignAdventure);
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
        if (entry.Kind == AdventureThreadKind.Play)
            return PlayThreadBindingService.ResolveBrowsableUrl(bundle);

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
                    "Play thread: not bound — open Threads to link and pin.",
                AdventureThreadKind.Design =>
                    "Design thread: not bound — open Threads to start or pin.",
                AdventureThreadKind.UtilityWorker =>
                    "Utility worker: not set up — Threads → Utility worker → Set up utility worker.",
                var k when k == AdventureThreadKindLegacy.Utility => "Utility thread: not pinned.",
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

    public static string FormatConnectionSummary(AdventureBundle bundle)
    {
        EnsureMigrated(bundle);
        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        var project = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        var play = GetActiveEntry(bundle, AdventureThreadKind.Play);
        var design = GetActiveEntry(bundle, AdventureThreadKind.Design);

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(project))
            parts.Add($"Project: {project}");

        if (play is { ConversationId: var playId } && !string.IsNullOrWhiteSpace(playId))
        {
            var tail = playId.Length > 8 ? playId[..8] + "…" : playId;
            parts.Add($"Play: {tail}");
        }
        else
        {
            parts.Add("Play: —");
        }

        if (design is { ConversationId: var designId } && !string.IsNullOrWhiteSpace(designId))
        {
            var tail = designId.Length > 8 ? designId[..8] + "…" : designId;
            parts.Add($"Design: {tail}");
        }
        else
        {
            parts.Add("Design: —");
        }

        var worker = GetActiveEntry(bundle, AdventureThreadKind.UtilityWorker);
        if (worker is { ConversationId: var workerId } && !string.IsNullOrWhiteSpace(workerId))
        {
            var tail = workerId.Length > 8 ? workerId[..8] + "…" : workerId;
            var ready = bundle.Metadata.UtilityWorkerCapabilities?.IsGreen == true ? "✓" : "…";
            parts.Add($"Worker: {tail}{ready}");
        }
        else
        {
            parts.Add("Worker: —");
        }

        return string.Join(" · ", parts);
    }

    public static string DefaultLabel(AdventureThreadKind kind) => kind switch
    {
        AdventureThreadKind.Play => "Play",
        AdventureThreadKind.Design => "Design",
        AdventureThreadKind.UtilityWorker => "Utility worker",
        var k when k == AdventureThreadKindLegacy.Utility => "Utility",
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

    private static void MigrateDesignJobStateOnAllEntries(AdventureMetadata metadata)
    {
        if (metadata.ThreadRegistry is null)
            return;

        foreach (var entry in metadata.ThreadRegistry.Where(e => e.Kind == AdventureThreadKind.Design))
            MigrateDesignJobStateOnEntry(entry, metadata);
    }

    private static void PurgeDesignUtilitySessionForConversation(AdventureBundle bundle, string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return;

        var jobId = GenerationJobId.DesignAdventure;
        if (bundle.Metadata.UtilitySessions is not { } sessions
            || !sessions.TryGetValue(jobId, out var session)
            || !string.Equals(session.ConversationId, conversationId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var activeConversation = GetActiveConversationId(bundle, AdventureThreadKind.Design);
        if (!string.IsNullOrWhiteSpace(activeConversation)
            && string.Equals(activeConversation, conversationId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        sessions.Remove(jobId);
    }
}
