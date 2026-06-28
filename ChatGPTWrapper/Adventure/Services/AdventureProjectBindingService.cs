// Instruction vs source delegation: docs/instruction-sources-paradigm.md
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class ProjectBindingResult
{
    public bool Success { get; init; }

    public string? GizmoId { get; init; }

    public string? ConversationId { get; init; }

    public string? Error { get; init; }
}

public sealed class AdventureProjectBindingService
{
    private readonly ChatGptProjectApiService _api;
    private readonly ProjectSourceSyncService _sync;

    public AdventureProjectBindingService(ChatGptProjectApiService api, ProjectSourceSyncService sync)
    {
        _api = api;
        _sync = sync;
    }

    public async Task<ChatGptSessionInfo> EnsureSessionAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken = default)
    {
        var session = await _api.GetSessionAsync(core, cancellationToken);
        if (!session.IsAuthenticated)
            throw new ChatGptApiException("Not logged in to ChatGPT.", ChatGptApiEndpoints.Session, 401);
        return session;
    }

    public async Task<IReadOnlyList<GizmoSummary>> ListProjectsAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken = default)
    {
        return await _api.ListProjectsAsync(core, cancellationToken);
    }

    public async Task<ProjectBindingResult> CreateAndLinkAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        string projectTitle,
        bool syncSources,
        bool createPlayThread,
        IProgress<string>? syncProgress = null,
        bool allowRecreate = false,
        CancellationToken cancellationToken = default)
    {
        await EnsureSessionAsync(core, cancellationToken);

        if (BlocksCreateWhenAlreadyLinked(GetLinkedProjectId(bundle.Metadata), allowRecreate))
        {
            return new ProjectBindingResult
            {
                Success = false,
                Error =
                    $"already_linked_use_existing: Adventure is linked to {bundle.Metadata.LinkedProjectId}. "
                    + "Use the existing project tab to re-link or sync sources instead of creating a new project.",
            };
        }

        var instructions = BuildProjectInstructions(bundle);
        var project = await _api.UpsertProjectAsync(
            core,
            gizmoId: null,
            title: projectTitle,
            instructions: instructions,
            caller: "CreateAndLink",
            adventureId: bundle.Metadata.Id,
            cancellationToken: cancellationToken);

        InstructionSourcesPolicy.RecordInstructionsSynced(bundle);

        return await FinalizeLinkAsync(
            core,
            bundle,
            project.Id,
            syncSources,
            createPlayThread,
            syncProgress,
            cancellationToken);
    }

    internal static bool BlocksCreateWhenAlreadyLinked(string? linkedProjectId, bool allowRecreate = false) =>
        !allowRecreate && !string.IsNullOrWhiteSpace(linkedProjectId);

    public static string? GetLinkedProjectId(AdventureMetadata? metadata)
    {
        if (metadata is null)
            return null;

        AdventureMetadataMigration.MigrateProjectLinkFields(metadata);

        if (!string.IsNullOrWhiteSpace(metadata.LinkedProjectId))
            return metadata.LinkedProjectId;

        if (!string.IsNullOrWhiteSpace(metadata.ProjectLink?.GizmoId))
            return metadata.ProjectLink.GizmoId;

        if (!string.IsNullOrWhiteSpace(metadata.LinkedProjectHint))
            return metadata.LinkedProjectHint;

        return null;
    }

    public static bool HasLinkedProject(AdventureBundle? bundle) =>
        bundle is not null && !string.IsNullOrWhiteSpace(GetLinkedProjectId(bundle.Metadata));

    /// <summary>
    /// Play sidebar "Link now…" banner visibility — hidden when a Project is linked.
    /// </summary>
    public static bool ShouldShowLinkProjectBanner(AdventureBundle? bundle) =>
        !HasLinkedProject(bundle);

    /// <summary>
    /// After project-only link, defer play-thread navigation and composer wait until the user
    /// pins a tab, starts a play thread, or sends a turn.
    /// </summary>
    public static bool ShouldDeferLinkedPlayContextAfterProjectLink(AdventureBundle? bundle) =>
        bundle is not null
        && HasLinkedProject(bundle)
        && !PlayTabPinService.HasPlayTabOrConversationBinding(bundle)
        && PlayTurnScopeService.IsFreshPlayThread(bundle);

    internal static bool ShouldProvisionPlayThreadOnLink(bool createPlayThread, AdventureStatus status) =>
        createPlayThread && status != AdventureStatus.Designing;

    public static void SyncLinkedProjectFields(AdventureMetadata? metadata)
    {
        if (metadata is null)
            return;

        TryPromoteLinkFromPinnedUrl(metadata);

        var linkedId = GetLinkedProjectId(metadata);
        if (string.IsNullOrWhiteSpace(linkedId))
            return;

        metadata.LinkedProjectId ??= linkedId;
        metadata.LinkedProjectHint ??= linkedId;
        metadata.ProjectLink ??= new ProjectLink
        {
            GizmoId = linkedId,
            CanonicalUrl = ChatGptUrls.BuildProjectUrl(linkedId),
        };
    }

    public static void TryPromoteLinkFromPinnedUrl(AdventureMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(GetLinkedProjectId(metadata))
            || string.IsNullOrWhiteSpace(metadata.PinnedPlayTabUrl))
        {
            return;
        }

        PlayTabPinService.TryBindProjectSessionFromSource(
            new AdventureBundle { Metadata = metadata },
            metadata.PinnedPlayTabUrl);
    }

    public static void MergeLinkMetadataFrom(AdventureMetadata? target, AdventureMetadata? source)
    {
        if (target is null || source is null)
            return;
        var linkedId = GetLinkedProjectId(source);
        if (string.IsNullOrWhiteSpace(linkedId))
            return;

        target.LinkedProjectId ??= linkedId;
        target.LinkedProjectHint ??= source.LinkedProjectHint ?? linkedId;
        target.LinkedConversationId ??= source.LinkedConversationId;
        target.ProjectLink ??= source.ProjectLink;
        target.PinnedPlayTabUrl ??= source.PinnedPlayTabUrl;
        target.PinnedPlayTabKey ??= source.PinnedPlayTabKey;
        target.PinnedPlayTabTitle ??= source.PinnedPlayTabTitle;
        target.PinnedDesignTabUrl ??= source.PinnedDesignTabUrl;
        target.PinnedDesignTabKey ??= source.PinnedDesignTabKey;
        target.PinnedDesignTabTitle ??= source.PinnedDesignTabTitle;
    }

    public static void ClearProjectLink(AdventureBundle bundle)
    {
        var previousGizmoId = GetLinkedProjectId(bundle.Metadata);
        bundle.Metadata.LinkedProjectId = null;
        bundle.Metadata.LinkedConversationId = null;
        bundle.Metadata.LinkedProjectHint = null;
        bundle.Metadata.ProjectLink = null;
        bundle.Metadata.PinnedPlayTabKey = null;
        bundle.Metadata.PinnedPlayTabTitle = null;
        bundle.Metadata.PinnedPlayTabUrl = null;
        bundle.Metadata.PinnedUtilityTabKey = null;
        bundle.Metadata.PinnedUtilityTabTitle = null;
        bundle.Metadata.PinnedDesignTabKey = null;
        bundle.Metadata.PinnedDesignTabTitle = null;
        bundle.Metadata.PinnedDesignTabUrl = null;
        ClearProjectRemoteState(bundle, previousGizmoId);

        AdventureStore.Save(bundle, allowLinkMetadataOverwrite: true);
    }

    internal static void ClearProjectRemoteState(AdventureBundle bundle, string? previousGizmoId)
    {
        EnsureSourceManifest(bundle);
        SourceManifestHelper.ClearRemoteBindings(bundle.SourceManifest);
        if (!string.IsNullOrWhiteSpace(previousGizmoId))
            ProjectRemoteListCache.Invalidate(previousGizmoId);

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        AdventureThreadRegistryService.ReleaseActiveThread(bundle, AdventureThreadKind.Play);
        AdventureThreadRegistryService.ReleaseActiveThread(bundle, AdventureThreadKind.Design);
        bundle.Metadata.PinnedUtilityTabKey = null;
        bundle.Metadata.PinnedUtilityTabTitle = null;
    }

    internal static void EnsureSourceManifest(AdventureBundle bundle)
    {
        bundle.SourceManifest ??= new SourceManifest();
        bundle.SourceManifest.Entries ??= [];
    }

    internal static void PrepareBundleForProjectLink(AdventureBundle bundle)
    {
        UtilityStoryContextSettingsService.EnsureDefaults(bundle.Metadata);
        EnsureSourceManifest(bundle);
    }

    public async Task<ProjectBindingResult> LinkExistingAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        string gizmoId,
        bool syncSources,
        bool updateInstructions,
        bool createPlayThread,
        string? projectTitle = null,
        IReadOnlyList<GizmoFileRef>? existingProjectFiles = null,
        IProgress<string>? syncProgress = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureSessionAsync(core, cancellationToken);

        if (updateInstructions)
        {
            var instructions = BuildProjectInstructions(bundle);
            var updated = await _api.UpsertProjectAsync(
                core,
                gizmoId,
                projectTitle,
                instructions,
                existingProjectFiles,
                caller: "LinkExisting",
                adventureId: bundle.Metadata.Id,
                cancellationToken);

            if (!ChatGptUrls.GizmoIdsEqual(updated.Id, gizmoId))
            {
                throw new ChatGptApiException(
                    $"ChatGPT returned a different project id ({updated.Id}) than selected ({gizmoId}). Link aborted to avoid creating duplicates.",
                    ChatGptApiEndpoints.ProjectUpsert);
            }

            InstructionSourcesPolicy.RecordInstructionsSynced(bundle);
        }

        return await FinalizeLinkAsync(
            core,
            bundle,
            gizmoId,
            syncSources,
            createPlayThread,
            syncProgress,
            cancellationToken);
    }

    private async Task<ProjectBindingResult> FinalizeLinkAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        string gizmoId,
        bool syncSources,
        bool createPlayThread,
        IProgress<string>? syncProgress,
        CancellationToken cancellationToken)
    {
        PrepareBundleForProjectLink(bundle);

        var previousGizmoId = GetLinkedProjectId(bundle.Metadata);
        var projectChanged = !string.IsNullOrWhiteSpace(previousGizmoId)
                             && !ChatGptUrls.GizmoIdsEqual(previousGizmoId, gizmoId);

        if (projectChanged)
        {
            ClearProjectRemoteState(bundle, previousGizmoId);
            ProjectRemoteListCache.Invalidate(gizmoId);
        }

        bundle.Metadata.LinkedProjectId = gizmoId;
        bundle.Metadata.LinkedProjectHint = gizmoId;
        bundle.Metadata.ProjectLink = new ProjectLink
        {
            GizmoId = gizmoId,
            CanonicalUrl = ChatGptUrls.BuildProjectUrl(gizmoId),
            LinkedAt = DateTimeOffset.UtcNow,
        };

        string? conversationId = bundle.Metadata.LinkedConversationId;
        var isDesigning = bundle.Metadata.Status == AdventureStatus.Designing;
        var shouldCreatePlayThread = ShouldProvisionPlayThreadOnLink(createPlayThread, bundle.Metadata.Status);

        if (shouldCreatePlayThread)
        {
            var convs = await _api.ListProjectConversationsAsync(core, gizmoId, cancellationToken);
            conversationId = convs
                .OrderByDescending(c => c.UpdatedAt ?? DateTimeOffset.MinValue)
                .FirstOrDefault()
                ?.Id;
            if (!string.IsNullOrWhiteSpace(conversationId))
            {
                ProjectLinkDiagnostics.Log(
                    $"Link: listed play thread {conversationId} for {gizmoId} (pending pin — not auto-navigating)");
            }
        }
        else if (!isDesigning && projectChanged)
        {
            conversationId = null;
        }

        AdventureThreadRegistryService.EnsureMigrated(bundle);

        if (!string.IsNullOrWhiteSpace(conversationId))
        {
            PlayThreadBindingService.MarkPendingPin(bundle, conversationId);
            bundle.Metadata.PinnedPlayTabUrl = ChatGptUrls.BuildProjectUrl(gizmoId);
        }
        else
        {
            if (!isDesigning && projectChanged)
                PlayThreadBindingService.MarkUnbound(bundle);

            bundle.Metadata.PinnedPlayTabUrl = ChatGptUrls.BuildProjectUrl(gizmoId);
        }

        if (bundle.Metadata.SchemaVersion < 6)
        {
            bundle.Metadata.LinkedConversationId = conversationId;
            if (bundle.Metadata.ProjectLink is not null)
                bundle.Metadata.ProjectLink.PlayConversationId = conversationId;
        }

        AdventureStore.Save(bundle);

        try
        {
            await _api.LogSidebarTitleSnapshotAsync(
                core,
                gizmoId,
                bundle.Metadata.Title,
                bundle.Metadata.Id,
                "after_link",
                cancellationToken);
        }
        catch
        {
            /* diagnostic only */
        }

        if (syncSources)
        {
            try
            {
                syncProgress?.Report("Syncing adventure sources…");
                ProjectSourceExportService.ExportForce(bundle);
                var syncResult = await _sync.SyncAsync(core, bundle, syncProgress, cancellationToken);
                if (!syncResult.Success && !syncResult.Partial)
                {
                    ProjectLinkDiagnostics.Log($"Sync after link failed: {syncResult.Error}");
                    return new ProjectBindingResult
                    {
                        Success = true,
                        GizmoId = gizmoId,
                        ConversationId = conversationId,
                        Error = "linked_but_sync_failed: " + syncResult.Error,
                    };
                }
            }
            catch (Exception ex)
            {
                ProjectLinkDiagnostics.Log($"Sync after link failed: {ex.Message}");
                return new ProjectBindingResult
                {
                    Success = true,
                    GizmoId = gizmoId,
                    ConversationId = conversationId,
                    Error = "linked_but_sync_failed: " + ex.Message,
                };
            }
        }

        AdventureStore.Save(bundle);

        return new ProjectBindingResult
        {
            Success = true,
            GizmoId = gizmoId,
            ConversationId = conversationId,
        };
    }

    public static string BuildProjectInstructions(AdventureBundle bundle) =>
        InstructionSourcesPolicy.BuildStaticInstructionsBody(bundle);
}
